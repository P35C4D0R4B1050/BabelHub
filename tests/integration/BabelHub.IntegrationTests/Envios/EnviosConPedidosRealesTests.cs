using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.IntegrationTests.Devoluciones;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>
/// T22 (AC-ENV-01, AC-ENV-07, AC-ENV-10, AC-ENV-12, AC-ENV-13): el <b>Pedidos real</b> y el <b>Envíos real</b> sobre un mismo PostgreSQL, con el operador simulado y el Inventario simulado: marcar listas crea un envío por fuente (un recojo no crea ninguno), los envíos avanzan a Pedidos
/// con sus fechas y el pedido se deriva a entregado al entregar el segundo envío.
/// </summary>
[Collection(ColeccionPostgresDePedidosYEnvios.Nombre)]
public sealed class EnviosConPedidosRealesTests(PostgresDePedidosYEnviosFixture bd) : IAsyncLifetime
{
    private static int _contador;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "ENVP2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "E";

    private sealed class ReservasDelEnvioSimuladas : IReservasDelEnvio
    {
        public List<Guid> Cierres { get; } = [];

        public volatile bool Caido;

        public Task<ResultadoDeCierreDeReservas> Cerrar(Guid pedidoId, Guid envioId, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default)
        {
            if (Caido)
            {
                return Task.FromResult<ResultadoDeCierreDeReservas>(new ResultadoDeCierreDeReservas.Incierto());
            }

            lock (Cierres)
            {
                Cierres.Add(envioId);
            }

            return Task.FromResult<ResultadoDeCierreDeReservas>(new ResultadoDeCierreDeReservas.Cerrado());
        }
    }

    private (EscenarioDePedidos Escenario, ReservasDelEnvioSimuladas Reservas, SimuladorOperadorLogistico Operador) Crear()
    {
        var reservas = new ReservasDelEnvioSimuladas();
        var operador = new SimuladorOperadorLogistico();
        var escenario = EscenarioDePedidos.Crear(
            bd.Unidad,
            servicios =>
            {
                servicios.AddSingleton<IControlDeLimites>(new LimitesPermisivos());
                servicios.AddSingleton<IReservasDelEnvio>(reservas);
                servicios.AddSingleton<IOperadorLogistico>(operador);
            },
            enviosReales: true);
        return (escenario, reservas, operador);
    }

    /// <summary>Un pedido a domicilio con una línea de «LIM» y otra de «ARE», ya en preparación.</summary>
    private async Task<Pedido> SembrarADomicilio()
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var lineas = new[]
        {
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "LIM", false, null, null),
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "El Aleph", null, 1, 40m, 40m, "ARE", false, null, null),
        };
        var contacto = BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123");
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 12m, 102m, contacto, new("t-1", ahora), new("p-1", ahora), null, lineas, ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        var cargado = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true))!;
        cargado.PrepararLineas([.. cargado.Lineas.Select(l => l.Id)], ahora);
        await bd.Pedidos.Actualizar(cargado);
        return (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
    }

    private static Task<Resultado<LineasOperadas>> MarcarListas(EscenarioDePedidos e, Pedido pedido) =>
        e.Como(EscenarioDePedidos.DeLogistica(), sp => sp.GetRequiredService<IGestionDeDespacho>().MarcarListas(new OperarLineasEntrada(pedido.Id, [.. pedido.Lineas.Select(l => l.Id)])));

    private static Task<Resultado<T>> Logistica<T>(EscenarioDePedidos e, Func<IEnviosDeLogistica, Task<Resultado<T>>> operacion) =>
        e.Como(EscenarioDePedidos.DeLogistica(), sp => operacion(sp.GetRequiredService<IEnviosDeLogistica>()));

    private async Task<IReadOnlyList<Guid>> EnviosDe(Guid pedidoId)
    {
        var ids = new List<Guid>();
        await using var comando = bd.Administracion.CreateCommand($"SELECT id FROM envios.envios WHERE pedido_id = '{pedidoId:D}' ORDER BY codigo_de_fuente");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            ids.Add(lector.GetGuid(0));
        }

        return ids;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Marcar_listas_un_pedido_a_domicilio_crea_un_envio_por_fuente_y_repetirlo_no_crea_otros()
    {
        var (e, _, _) = Crear();
        await using var _ = e;
        var pedido = await SembrarADomicilio();

        var primera = await MarcarListas(e, pedido);
        var repetida = await MarcarListas(e, pedido);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.NotNull(repetida);
        Assert.Equal(2, (await EnviosDe(pedido.Id)).Count);
        Assert.Equal(["ARE", "LIM"], [.. await ConsultarFuentes(pedido.Id)]);
    }

    private async Task<IReadOnlyList<string>> ConsultarFuentes(Guid pedidoId)
    {
        var fuentes = new List<string>();
        await using var comando = bd.Administracion.CreateCommand($"SELECT codigo_de_fuente FROM envios.envios WHERE pedido_id = '{pedidoId:D}' ORDER BY codigo_de_fuente");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            fuentes.Add(lector.GetString(0));
        }

        return fuentes;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public async Task Los_envios_avanzan_a_Pedidos_con_su_fecha_y_el_pedido_pasa_a_entregado_al_entregar_el_segundo()
    {
        var (e, reservas, operador) = Crear();
        await using var _ = e;
        var pedido = await SembrarADomicilio();
        Assert.True((await MarcarListas(e, pedido)).EsExito);
        var envios = await EnviosDe(pedido.Id);

        foreach (var envio in envios)
        {
            Assert.True((await Logistica(e, l => l.SolicitarGuia(envio))).EsExito);
            Assert.True((await Logistica(e, l => l.PasarAEnReparto(envio))).EsExito);
        }

        var tras = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
        Assert.All(tras.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Despachado, l.EstadoImpreso));
        Assert.Equal(2, reservas.Cierres.Count);
        Assert.Equal(2, operador.GuiasCreadas);

        Assert.True((await Logistica(e, l => l.MarcarEntregado(envios[0]))).EsExito);
        var parcial = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
        Assert.Contains(parcial.Lineas, l => l.EstadoImpreso == EstadoDeLineaImpresa.Entregado);
        Assert.Contains(parcial.Lineas, l => l.EstadoImpreso == EstadoDeLineaImpresa.Despachado);

        Assert.True((await Logistica(e, l => l.MarcarEntregado(envios[1]))).EsExito);
        var final = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
        Assert.All(final.Lineas, l =>
        {
            Assert.Equal(EstadoDeLineaImpresa.Entregado, l.EstadoImpreso);
            Assert.NotNull(l.EntregadaEn);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Con_el_inventario_caido_el_envio_sigue_con_guia_y_las_lineas_sin_despachar_y_al_volver_se_completa_una_vez()
    {
        var (e, reservas, _) = Crear();
        await using var _ = e;
        var pedido = await SembrarADomicilio();
        Assert.True((await MarcarListas(e, pedido)).EsExito);
        var envio = (await EnviosDe(pedido.Id))[0];
        Assert.True((await Logistica(e, l => l.SolicitarGuia(envio))).EsExito);

        reservas.Caido = true;
        var caido = await Logistica(e, l => l.PasarAEnReparto(envio));
        reservas.Caido = false;
        var recuperado = await Logistica(e, l => l.PasarAEnReparto(envio));

        Assert.False(caido.EsExito);
        Assert.True(recuperado.EsExito, recuperado.Error?.Clave);
        Assert.Single(reservas.Cierres);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    [Trait("Criterio", "AC-PED-20")]
    public async Task Anular_el_pedido_con_un_envio_en_reparto_y_otro_pendiente_cancela_solo_el_pendiente_y_nunca_un_cancelado_con_el_fisico_descontado()
    {
        var (e, reservas, _) = Crear();
        await using var _ = e;
        var pedido = await SembrarADomicilio();
        Assert.True((await MarcarListas(e, pedido)).EsExito);
        var envios = await EnviosDe(pedido.Id);
        Assert.True((await Logistica(e, l => l.SolicitarGuia(envios[0]))).EsExito);
        Assert.True((await Logistica(e, l => l.PasarAEnReparto(envios[0]))).EsExito);

        var anulacion = await e.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido.Id, "otro")));

        Assert.NotNull(anulacion);
        var estados = new List<string>();
        await using (var comando = bd.Administracion.CreateCommand($"SELECT estado FROM envios.envios WHERE pedido_id = '{pedido.Id:D}' ORDER BY codigo_de_fuente"))
        await using (var lector = await comando.ExecuteReaderAsync())
        {
            while (await lector.ReadAsync())
            {
                estados.Add(lector.GetString(0));
            }
        }

        // El que salió a reparto sigue (su físico ya se descontó); el pendiente de guía se canceló: nunca un cancelado con el físico descontado.
        Assert.Contains("en_reparto", estados);
        Assert.Contains("cancelado", estados);
        Assert.Single(reservas.Cierres);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_pedido_de_recojo_no_crea_ningun_envio()
    {
        var (e, _, _) = Crear();
        await using var _ = e;
        var ahora = EscenarioDePedidos.InicioDelDia;
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "LIM", false, null, null);
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", ahora), new("p-1", ahora), null, [linea], ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        var cargado = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true))!;
        cargado.PrepararLineas([.. cargado.Lineas.Select(l => l.Id)], ahora);
        await bd.Pedidos.Actualizar(cargado);

        var resultado = await e.Como(
            EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Lima),
            sp => sp.GetRequiredService<IGestionDeRecojo>().MarcarListas("LIM", new OperarLineasEntrada(pedido.Id, [.. cargado.Lineas.Select(l => l.Id)])));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Empty(await EnviosDe(pedido.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    [Trait("Criterio", "AC-ENV-16")]
    public async Task Veinte_pares_de_solicitudes_de_guia_y_de_pasos_a_en_reparto_simultaneos_crean_una_guia_y_descuentan_el_fisico_una_sola_vez()
    {
        var (e, reservas, operador) = Crear();
        await using var _ = e;
        for (var i = 0; i < 20; i++)
        {
            var pedido = await SembrarADomicilio();
            Assert.True((await MarcarListas(e, pedido)).EsExito);
            var envio = (await EnviosDe(pedido.Id))[0];

            var guias = await Task.WhenAll(Task.Run(() => Logistica(e, l => l.SolicitarGuia(envio))), Task.Run(() => Logistica(e, l => l.SolicitarGuia(envio))));
            Assert.Contains(guias, r => r.EsExito);

            var antes = reservas.Cierres.Count;
            var pasos = await Task.WhenAll(Task.Run(() => Logistica(e, l => l.PasarAEnReparto(envio))), Task.Run(() => Logistica(e, l => l.PasarAEnReparto(envio))));
            Assert.Contains(pasos, r => r.EsExito);
            Assert.Equal(antes + 1, reservas.Cierres.Count(c => c == envio) + antes);
        }

        Assert.Equal(20, reservas.Cierres.Distinct().Count());
        Assert.True(operador.GuiasCreadas >= 20);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public async Task La_cancelacion_de_la_cuenta_conserva_los_envios_entregado_en_reparto_y_cancelado_sin_nombre_direccion_ni_telefono()
    {
        var (e, _, _) = Crear();
        await using var _ = e;

        // Pedido 1: la fuente de LIM llega a entregada; el pedido se anula y la de ARE (pendiente de guía) se cancela.
        var primero = await SembrarADomicilio();
        Assert.True((await MarcarListas(e, primero)).EsExito);
        var enviosDelPrimero = await EnviosDe(primero.Id);
        var fuentes = await ConsultarFuentes(primero.Id);
        var lima = enviosDelPrimero[fuentes.ToList().IndexOf("LIM")];
        Assert.True((await Logistica(e, l => l.SolicitarGuia(lima))).EsExito);
        Assert.True((await Logistica(e, l => l.PasarAEnReparto(lima))).EsExito);
        Assert.True((await Logistica(e, l => l.MarcarEntregado(lima))).EsExito);
        Assert.NotNull(await e.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(primero.Id, "otro"))));

        // Pedido 2: una fuente en reparto (la otra queda pendiente).
        var segundo = await SembrarADomicilio();
        Assert.True((await MarcarListas(e, segundo)).EsExito);
        var enviosDelSegundo = await EnviosDe(segundo.Id);
        Assert.True((await Logistica(e, l => l.SolicitarGuia(enviosDelSegundo[0]))).EsExito);
        Assert.True((await Logistica(e, l => l.PasarAEnReparto(enviosDelSegundo[0]))).EsExito);

        var antes = await bd.Consultar<string>("SELECT string_agg(id::text || estado || coalesce(numero_de_guia, '') || coalesce(operador, ''), '|' ORDER BY id) FROM envios.envios");

        await e.Como(
            ActorActual.DelSistema(),
            sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar<bool>(
                async token =>
                {
                    foreach (var receptor in sp.GetServices<BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion>())
                    {
                        await receptor.Aplicar(EscenarioDePedidos.Titular, EscenarioDePedidos.Titular, token);
                    }

                    return true;
                }));

        var estados = new List<string>();
        await using (var comando = bd.Administracion.CreateCommand("SELECT estado FROM envios.envios ORDER BY id"))
        await using (var lector = await comando.ExecuteReaderAsync())
        {
            while (await lector.ReadAsync())
            {
                estados.Add(lector.GetString(0));
            }
        }

        Assert.Contains("entregado", estados);
        Assert.Contains("en_reparto", estados);
        Assert.Contains("cancelado", estados);
        Assert.Equal(antes, await bd.Consultar<string>("SELECT string_agg(id::text || estado || coalesce(numero_de_guia, '') || coalesce(operador, ''), '|' ORDER BY id) FROM envios.envios"));
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM envios.envios x WHERE row_to_json(x)::text ~* '(Ana P|987654321|Jr\\. Lima)'"));
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM envios.envio_lineas x WHERE row_to_json(x)::text ~* '(Ana P|987654321|Jr\\. Lima)'"));
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE contacto_nombre IS NOT NULL OR contacto_telefono IS NOT NULL OR contacto_direccion IS NOT NULL"));
    }
}
