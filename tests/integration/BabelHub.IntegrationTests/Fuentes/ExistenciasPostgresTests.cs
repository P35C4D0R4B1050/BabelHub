using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// T18: la primitiva única de escritura del stock con PostgreSQL real y el rol real de la aplicación: paridad con el Dominio, «denegado por alcance» frente a «no aplicable», el avance de un encargado
/// con una vencida en otra fuente, el interbloqueo entre orígenes en orden contrario, la concurrencia sin sobreventa y —el riesgo del hallazgo del 2026-11-14— que <b>tocar el stock sin actor falla a
/// la vista y se detecta</b> en lugar de leer «cero filas».
/// </summary>
public sealed class ExistenciasPostgresTests(PostgresDeFuentesFixture bd) : IClassFixture<PostgresDeFuentesFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Sistema => ActorActual.DelSistema();

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    private static Task<T> EnTransaccion<T>(PilaDeFuentes pila, Func<CancellationToken, Task<T>> trabajo) => pila.Unidad.Ejecutar(trabajo);

    private async Task<ResultadoDeAplicacion> Aplicar(PilaDeFuentes pila, Guid fuente, Guid edicion, int df, int dr) =>
        await EnTransaccion(pila, async token =>
        {
            await pila.Movimientos.Bloquear([edicion], token);
            return await pila.Movimientos.Aplicar(new MovimientoDeStock(fuente, edicion, df, dr), token);
        });

    // --- paridad con el Dominio ---

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Paridad_ElUpdateYElDominioDecidenLoMismoSobreLosMismosCasos()
    {
        var fuente = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        var azar = new Random(20261114);

        // Los casos límite a mano y, después, 250 al azar con valores chicos para que los bordes se crucen seguido.
        var casos = new List<(int Fisico, int Reservado, int Df, int Dr)>
        {
            (5, 5, 0, 1), (5, 5, 1, 1), (5, 5, -1, 0), (5, 5, 0, -5), (5, 5, 0, -6), (0, 0, 0, 0), (0, 0, 1, 0), (0, 0, -1, 0), (3, 1, -2, 0), (3, 1, -3, 0),
            (3, 1, 2, 2), (3, 1, 0, 3), (2147483647, 0, 1, 0), (2147483646, 2147483646, 1, 1), (4, 0, -4, 0), (4, 4, -4, -4),
        };
        for (var i = 0; i < 250; i++)
        {
            var fisico = azar.Next(0, 8);
            casos.Add((fisico, azar.Next(0, fisico + 1), azar.Next(-8, 9), azar.Next(-8, 9)));
        }

        foreach (var (fisico, reservado, df, dr) in casos)
        {
            await bd.PonerStock(fuente, edicion, fisico, reservado);
            var pila = new PilaDeFuentes(bd, Sistema);

            var enDominio = new ExistenciaDeStock(fisico, reservado).Aplicar(df, dr);
            var enBase = await EnTransaccion(pila, token => pila.Existencias.Aplicar(new MovimientoDeStock(fuente, edicion, df, dr), token));

            Assert.True(enDominio.Aplicado == enBase.Aplicado, $"paridad rota con físico {fisico}, reservado {reservado}, Δfísico {df}, Δreservado {dr}");
            if (enBase.Aplicado)
            {
                Assert.Equal(new CantidadesDeStock(fisico, reservado), enBase.Anterior);
                Assert.Equal(new CantidadesDeStock(enDominio.Existencia.Fisico, enDominio.Existencia.Reservado), enBase.Posterior);
                Assert.Equal((enDominio.Existencia.Fisico, enDominio.Existencia.Reservado), await bd.StockDe(fuente, edicion));
            }
            else
            {
                Assert.Equal(ResultadoDeMovimiento.NoAplicable, enBase.Resultado);
                Assert.Equal((fisico, reservado), await bd.StockDe(fuente, edicion));
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Aplicar_DevuelveElAnteriorYElPosteriorYSumaLaVersion()
    {
        var fuente = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(fuente, edicion, 9, 1);

        var resultado = await Aplicar(new PilaDeFuentes(bd, Sistema), fuente, edicion, 0, 2);

        Assert.Equal(ResultadoDeAplicacion.Exito(new CantidadesDeStock(9, 1), new CantidadesDeStock(9, 3)), resultado);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT version FROM fuentes.stock WHERE fuente_id = '{fuente}' AND edicion_id = '{edicion}'"));
    }

    // --- alcance ---

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Aplicar_UnEncargadoQueEscribeStockDeOtraFuente_RecibeDeniegadoPorAlcanceYNoFaltaDeStock()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cusco = await bd.AgregarFuente("cusco");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, edicion, 20, 0);
        await bd.PonerStock(ayacucho, edicion, 2, 0);

        var resultado = await Aplicar(new PilaDeFuentes(bd, Encargado(ayacucho)), cusco, edicion, -1, 0);

        Assert.Equal(ResultadoDeMovimiento.DeniegadoPorAlcance, resultado.Resultado);
        Assert.Equal((20, 0), await bd.StockDe(cusco, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Aplicar_ElStockInsuficienteEnLaPropiaFuente_EsNoAplicable_NoDenegado()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, edicion, 2, 2);

        var resultado = await Aplicar(new PilaDeFuentes(bd, Encargado(ayacucho)), ayacucho, edicion, -1, 0);

        Assert.Equal(ResultadoDeMovimiento.NoAplicable, resultado.Resultado);
        Assert.Equal((2, 2), await bd.StockDe(ayacucho, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Aplicar_ElEncargadoEnSuPropiaFuente_Aplica()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, edicion, 5, 0);

        var resultado = await Aplicar(new PilaDeFuentes(bd, Encargado(ayacucho)), ayacucho, edicion, -2, 0);

        Assert.True(resultado.Aplicado);
        Assert.Equal((3, 0), await bd.StockDe(ayacucho, edicion));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [InlineData("sistema")]
    [InlineData("integracion")]
    [InlineData("administrador")]
    public async Task Aplicar_LaRedCompleta_AplicaEnCualquierFuente(string quien)
    {
        var cusco = await bd.AgregarFuente("cusco");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, edicion, 5, 0);
        ActorActual actor = quien switch
        {
            "sistema" => ActorActual.DelSistema(),
            "integracion" => ActorActual.DeIntegracion("e-commerce"),
            _ => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral),
        };

        var resultado = await Aplicar(new PilaDeFuentes(bd, actor), cusco, edicion, 0, 1);

        Assert.True(resultado.Aplicado);
        Assert.Equal((5, 1), await bd.StockDe(cusco, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Aplicar_UnEncargadoDeLaSucursalAQueAjustaConUnaVencidaEnB_AvanzaSinDenegacion()
    {
        var a = await bd.AgregarFuente("sucursal-a");
        var b = await bd.AgregarFuente("sucursal-b");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(a, edicion, 10, 0);
        await bd.PonerStock(b, edicion, 6, 4);
        var reservas = new ReservasDeStockFijo();
        reservas.Liberadas.Add(new UnidadesLiberadas(b, edicion, 3));
        var pila = new PilaDeFuentes(bd, Encargado(a), reservas);

        var resultado = await EnTransaccion(pila, async token =>
        {
            var bloqueo = await pila.Movimientos.Bloquear([edicion], token);
            var ajuste = await pila.Movimientos.Aplicar(new MovimientoDeStock(a, edicion, -1, 0), token);
            return (bloqueo, ajuste);
        });

        // Los deltas de liberación de Bloquear llevan elevación: la vencida de la fuente B se libera aunque el actor sea de A.
        Assert.True(resultado.bloqueo.EstaBloqueado);
        Assert.Equal(1, resultado.bloqueo.Fila(b, edicion).Reservado);
        Assert.True(resultado.ajuste.Aplicado);
        Assert.Equal((6, 1), await bd.StockDe(b, edicion));
        Assert.Equal((9, 0), await bd.StockDe(a, edicion));
    }

    // --- filas ausentes ---

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Aplicar_SobreUnaFilaAusente_DistingueLaEdicionInexistenteDeLaQueSoloNoTienePareja()
    {
        var lima = await bd.AgregarFuente("lima");
        var impresa = await bd.AgregarEdicion();
        var digital = await bd.AgregarEdicion("digital");
        var pila = new PilaDeFuentes(bd, Sistema);

        var sinFila = await EnTransaccion(pila, t => pila.Existencias.Aplicar(new MovimientoDeStock(lima, impresa, 1, 0), t));
        var deDigital = await EnTransaccion(pila, t => pila.Existencias.Aplicar(new MovimientoDeStock(lima, digital, 1, 0), t));
        var desconocida = await EnTransaccion(pila, t => pila.Existencias.Aplicar(new MovimientoDeStock(lima, Guid.NewGuid(), 1, 0), t));
        var fuenteDesconocida = await EnTransaccion(pila, t => pila.Existencias.Aplicar(new MovimientoDeStock(Guid.NewGuid(), impresa, 1, 0), t));

        Assert.Equal(ResultadoDeMovimiento.NoAplicable, sinFila.Resultado);
        Assert.Equal(ResultadoDeMovimiento.EdicionInexistenteONoImpresa, deDigital.Resultado);
        Assert.Equal(ResultadoDeMovimiento.EdicionInexistenteONoImpresa, desconocida.Resultado);
        Assert.Equal(ResultadoDeMovimiento.EdicionInexistenteONoImpresa, fuenteDesconocida.Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task GarantizarFilas_CreaUnaFilaDeCerosPorFuenteSoloParaEdicionesImpresas_YNoPisaLoQueExiste()
    {
        var lima = await bd.AgregarFuente("lima");
        var cusco = await bd.AgregarFuente("cusco");
        var impresa = await bd.AgregarEdicion();
        var digital = await bd.AgregarEdicion("digital");
        await bd.PonerStock(lima, impresa, 7, 2);
        var pila = new PilaDeFuentes(bd, Sistema);

        await EnTransaccion(pila, async t =>
        {
            await pila.Existencias.GarantizarFilas([impresa, digital], t);
            await pila.Existencias.GarantizarFilas([impresa, digital], t);
            return 0;
        });

        Assert.Equal((7, 2), await bd.StockDe(lima, impresa));
        Assert.Equal((0, 0), await bd.StockDe(cusco, impresa));
        Assert.Null(await bd.StockDe(lima, digital));
        Assert.Null(await bd.StockDe(cusco, digital));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Bloquear_UnaEdicionDigitalOSinFicha_SeRechazaSinTocarNada()
    {
        await bd.AgregarFuente("lima");
        var digital = await bd.AgregarEdicion("digital");
        var pila = new PilaDeFuentes(bd, Sistema);

        var bloqueo = await EnTransaccion(pila, t => pila.Movimientos.Bloquear([digital, Guid.NewGuid()], t));

        Assert.Equal(ResultadoDeBloqueo.EdicionInexistenteONoImpresa, bloqueo.Resultado);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock"));
    }

    // --- interbloqueo y concurrencia ---

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Bloquear_DosOrigenesQueBloqueanLasMismasEdicionesEnOrdenContrario_NoSeInterbloquean()
    {
        var lima = await bd.AgregarFuente("lima");
        var cusco = await bd.AgregarFuente("cusco");
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(lima, a, 100, 0);
        await bd.PonerStock(lima, b, 100, 0);

        async Task<int> Origen(Guid[] ediciones, Guid fuente)
        {
            var aplicados = 0;
            for (var i = 0; i < 25; i++)
            {
                var pila = new PilaDeFuentes(bd, Sistema);
                await EnTransaccion(pila, async token =>
                {
                    await pila.Movimientos.Bloquear(ediciones, token);
                    await Task.Delay(5, token);
                    foreach (var edicion in ediciones)
                    {
                        if ((await pila.Movimientos.Aplicar(new MovimientoDeStock(lima, edicion, 0, 1), token)).Aplicado)
                        {
                            aplicados++;
                        }
                    }

                    return 0;
                });
            }

            Assert.NotEqual(Guid.Empty, fuente);
            return aplicados;
        }

        // Una transacción pide (A, B) y la otra (B, A): sin el orden único (edición, fuente) de Bloquear se interbloquearían (40P01).
        var resultados = await Task.WhenAll(Origen([a, b], lima), Origen([b, a], cusco));

        Assert.Equal([50, 50], resultados);
        Assert.Equal((100, 50), await bd.StockDe(lima, a));
        Assert.Equal((100, 50), await bd.StockDe(lima, b));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Aplicar_VeinteSolicitudesSimultaneasPorLaUltimaUnidad_UnaSolaGanaYElVendibleNuncaEsNegativo()
    {
        var lima = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(lima, edicion, 1, 0);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var pila = new PilaDeFuentes(bd, ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente));
            return await Aplicar(pila, lima, edicion, 0, 1);
        }));

        Assert.Single(resultados, r => r.Aplicado);
        Assert.Equal(19, resultados.Count(r => r.Resultado == ResultadoDeMovimiento.NoAplicable));
        Assert.Equal((1, 1), await bd.StockDe(lima, edicion));
    }

    // --- lecturas ---

    [Fact]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task LeerDeLaFuente_ConUnaListaVacia_NoDevuelveNada_YSinFiltroDevuelveTodo()
    {
        var lima = await bd.AgregarFuente("lima");
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(lima, a, 3, 0);
        await bd.PonerStock(lima, b, 4, 0);
        var pila = new PilaDeFuentes(bd, Sistema);

        var todas = await EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(lima, null, t));
        var ninguna = await EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(lima, [], t));
        var una = await EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(lima, [b], t));

        Assert.Equal(2, todas.Count);
        Assert.Empty(ninguna);
        Assert.Equal([b], una.Select(f => f.EdicionId));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task LeerDeLaFuente_CorreConElAlcanceDelActor_UnEncargadoNoVeLasFilasDeOtraFuenteAunqueLaAplicacionFalle()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cusco = await bd.AgregarFuente("cusco");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, edicion, 1, 0);
        await bd.PonerStock(cusco, edicion, 2, 0);
        var pila = new PilaDeFuentes(bd, Encargado(ayacucho));

        var propias = await EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(ayacucho, null, t));
        var ajenas = await EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(cusco, null, t));

        Assert.Single(propias);
        Assert.Empty(ajenas);
    }

    // --- el riesgo del hallazgo: sin actor, a la vista ---

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task SinActor_TocarElStockFallaALaVistaYDejaUnaAlertaTecnica_EnVezDeLeerCeroFilas()
    {
        var lima = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(lima, edicion, 5, 0);
        var pila = new PilaDeFuentes(bd, actor: null);

        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.Leer([edicion], t)));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(lima, null, t)));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.Aplicar(new MovimientoDeStock(lima, edicion, 0, 1), t)));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Movimientos.Bloquear([edicion], t)));

        Assert.Equal(4, pila.Alertas.Alertas.Count);
        Assert.All(pila.Alertas.Alertas, a => Assert.Equal("alcance-de-datos-sin-actor", a.Motivo));
        Assert.Equal((5, 0), await bd.StockDe(lima, edicion));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [InlineData("encargado-sin-sucursal")]
    [InlineData("encargado-con-sucursal-vacia")]
    [InlineData("rol-desconocido")]
    public async Task UnActorQueNoResuelveAlcance_TampocoTocaElStock_FallaALaVistaYSeDetecta(string quien)
    {
        var lima = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(lima, edicion, 5, 0);
        var actor = quien switch
        {
            "encargado-sin-sucursal" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal),
            "encargado-con-sucursal-vacia" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, Guid.Empty),
            _ => ActorActual.DeUsuario(Guid.NewGuid(), "auditor-externo", lima),
        };
        var pila = new PilaDeFuentes(bd, actor);

        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.LeerDeLaFuente(lima, null, t)));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.Leer([edicion], t)));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.Aplicar(new MovimientoDeStock(lima, edicion, 0, 1), t)));

        Assert.Equal(3, pila.Alertas.Alertas.Count);
        Assert.Equal((5, 0), await bd.StockDe(lima, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task UnVisitante_NoTocaElStockNiSiquieraConLaElevacion_YSeDetecta()
    {
        var lima = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(lima, edicion, 5, 0);
        var pila = new PilaDeFuentes(bd, ActorActual.Anonimo());

        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnTransaccion(pila, t => pila.Existencias.Leer([edicion], t)));

        Assert.Single(pila.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ElActorDeIntegracionYElDelSistema_LeenLoQueCorresponde_NoCeroFilas()
    {
        var lima = await bd.AgregarFuente("lima");
        var cusco = await bd.AgregarFuente("cusco");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(lima, edicion, 5, 1);
        await bd.PonerStock(cusco, edicion, 8, 0);

        foreach (var actor in new[] { ActorActual.DeIntegracion("e-commerce"), ActorActual.DelSistema() })
        {
            var pila = new PilaDeFuentes(bd, actor);

            var filas = await EnTransaccion(pila, t => pila.Existencias.Leer([edicion], t));

            Assert.Equal([(lima, 5, 1), (cusco, 8, 0)], filas.OrderBy(f => f.Fisico).Select(f => (f.FuenteId, f.Fisico, f.Reservado)));
            Assert.Empty(pila.Alertas.Alertas);
        }
    }
}
