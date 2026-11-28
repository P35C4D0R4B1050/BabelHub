using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T13: los repositorios de <c>Traslados</c> y el buzón de salida con PostgreSQL real y el rol real de la aplicación: ida y vuelta de un traslado, transiciones condicionales por la versión, bloqueo <c>FOR UPDATE</c>,
/// generación idempotente sin abortar la transacción, buzón con <c>ON CONFLICT DO NOTHING</c> y el alcance de datos: el sistema y las integraciones ven y escriben toda la red; un encargado solo lo suyo y no alcanza los puertos
/// de <c>Reservas</c> ni los trabajos (sin elevación).
/// </summary>
public sealed class RepositoriosDeTrasladosPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Sistema = ActorActual.DelSistema();

    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    private async Task<(Guid Lima, Guid Cusco, Guid Ayacucho, Guid Libro)> Escenario()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");
        return (lima, cusco, ayacucho, await bd.AgregarEdicion());
    }

    private static Traslado Reposicion(Guid edicion, Guid origen, Guid destino, int cantidad = 3) =>
        Traslado.SolicitarReposicion(Guid.NewGuid(), edicion, origen, destino, cantidad, "reposición", Guid.NewGuid(), Ahora);

    private static Traslado Vinculado(Guid edicion, Guid origen, Guid destino, Guid pedido) =>
        Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, origen, destino, 1, pedido, EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 18), 2), null, Ahora);

    private static Task<T> EnUnaTransaccion<T>(PilaDeReservas pila, ActorActual? actor, Func<IServiceProvider, Task<T>> operacion) =>
        pila.EnUnAmbito(actor, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(_ => operacion(sp)));

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task LaTransicionEsCondicionalPorLaVersion_LaSegundaConLaMismaVersionNoActualizaNada()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Reposicion(libro, cusco, ayacucho);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado); return 0; });

        var (primera, segunda, estado) = await EnUnaTransaccion(pila, Sistema, async sp =>
        {
            var repo = sp.GetRequiredService<IRepositorioDeTraslados>();
            var a = await repo.PorIdBloqueado(traslado.Id);
            var b = await repo.PorIdBloqueado(traslado.Id);
            a!.Aprobar(Ahora.AddHours(1));
            b!.Aprobar(Ahora.AddHours(2));
            var uno = await repo.Guardar(a, 0);
            var dos = await repo.Guardar(b, 0);
            return (uno, dos, (await repo.PorId(traslado.Id))!);
        });

        Assert.Equal((true, false, EstadoDeTraslado.Aprobado, 1L, Ahora.AddHours(1)), (primera, segunda, estado.Estado, estado.Version, estado.AprobadoEn));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task ElBloqueoDeLaFilaHaceEsperarAOtraTransaccion_YElLockTimeoutEsEsperaAgotada()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Reposicion(libro, cusco, ayacucho);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado); return 0; });
        var liberar = new TaskCompletionSource();
        var tomada = new TaskCompletionSource();

        var dueno = EnUnaTransaccion(pila, Sistema, async sp =>
        {
            await sp.GetRequiredService<IRepositorioDeTraslados>().PorIdBloqueado(traslado.Id);
            tomada.SetResult();
            await liberar.Task;
            return 0;
        });
        await tomada.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var espera = EnUnaTransaccion(pila, Sistema, async sp =>
        {
            await using var cmd = new Npgsql.NpgsqlCommand("SET LOCAL lock_timeout = '300ms'", (Npgsql.NpgsqlConnection)sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>().Conexion, (Npgsql.NpgsqlTransaction)sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>().Transaccion);
            await cmd.ExecuteNonQueryAsync();
            return await sp.GetRequiredService<IRepositorioDeTraslados>().PorIdBloqueado(traslado.Id);
        });

        await Assert.ThrowsAsync<EsperaDeBloqueoAgotadaException>(() => espera);
        liberar.SetResult();
        await dueno;
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task ElVinculadoSeInsertaConOnConflictDoNothing_RepetirLaConfirmacionNoAbortaLaTransaccion()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var pedido = Guid.NewGuid();

        var (primero, repetido, despuesDelChoque, existente) = await EnUnaTransaccion(pila, Sistema, async sp =>
        {
            var repo = sp.GetRequiredService<IRepositorioDeTraslados>();
            var uno = Vinculado(libro, cusco, ayacucho, pedido);
            var dos = Vinculado(libro, cusco, ayacucho, pedido);
            var a = await repo.InsertarVinculado(uno);
            var b = await repo.InsertarVinculado(dos);
            var siguienteSentencia = await repo.PorId(uno.Id);
            return (a, b, siguienteSentencia is not null, await repo.VinculadoActivo(pedido, libro, cusco));
        });

        Assert.Equal((true, false, true), (primero, repetido, despuesDelChoque));
        Assert.NotNull(existente);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task ElGeneradorDePedidosEsIdempotente_YSuEstimacionNoCambiaSiLaRutaCambia()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        await bd.DefinirRuta(cusco, ayacucho, 2);
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var pedido = Guid.NewGuid();
        var solicitud = new[] { new SolicitudDeTrasladoDePedido(cusco, libro, 1) };

        var primera = await EnUnaTransaccion(pila, Sistema, sp => sp.GetRequiredService<IGeneradorDeTrasladosDePedido>().Generar(pedido, ayacucho, solicitud));
        await bd.DefinirRuta(cusco, ayacucho, 5);
        var repetida = await EnUnaTransaccion(pila, Sistema, sp => sp.GetRequiredService<IGeneradorDeTrasladosDePedido>().Generar(pedido, ayacucho, solicitud));

        Assert.Equal(primera.Single().TrasladoId, repetida.Single().TrasladoId);
        Assert.Equal(primera.Single().FechaEstimadaDeLlegada, repetida.Single().FechaEstimadaDeLlegada);
        Assert.Equal((1L, 2L), (await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"), await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes")));
        Assert.Equal(1, pila.Auditoria.Contar("TrasladoSolicitado"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElBuzonInsertaConOnConflictDoNothing_UnDuplicadoNoAbortaLaTransaccionDeQuienConfirma()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Reposicion(libro, cusco, ayacucho);
        var clave = $"traslado:{traslado.Id:D}:aprobado";
        MensajeSaliente Mensaje(string sucursal, string tipo = "aviso") => new(Guid.NewGuid(), tipo, clave, sucursal, "{\"estado\":\"aprobado\"}", Ahora);

        await EnUnaTransaccion(pila, Sistema, async sp =>
        {
            var buzon = sp.GetRequiredService<IBuzonDeSalida>();
            await buzon.Insertar([Mensaje("cusco"), Mensaje("ayacucho")]);
            await buzon.Insertar([Mensaje("cusco"), Mensaje("cusco", "estado-de-pedido")]);
            await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado);
            return 0;
        });

        Assert.Equal(3L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
        Assert.Equal("pendiente", await bd.Consultar<string>("SELECT DISTINCT estado FROM traslados.mensajes_salientes"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task SiLaTransaccionFalla_NiElTrasladoNiSusMensajesQuedan()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Reposicion(libro, cusco, ayacucho);

        await Assert.ThrowsAsync<InvalidOperationException>(() => EnUnaTransaccion<int>(pila, Sistema, async sp =>
        {
            await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado);
            await sp.GetRequiredService<IBuzonDeSalida>().Insertar([new MensajeSaliente(Guid.NewGuid(), "aviso", $"traslado:{traslado.Id:D}:solicitado", "cusco", "{}", Ahora)]);
            throw new InvalidOperationException("falla después de escribir");
        }));

        Assert.Equal((0L, 0L), (await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"), await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes")));
    }

    // ---------- alcance de datos, sin elevación ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task ElEncargadoLeeSoloLosTrasladosDeSuSucursalComoOrigenODestino_ElSistemaYLaIntegracionVenTodo()
    {
        var (lima, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Reposicion(libro, cusco, ayacucho);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado); return 0; });

        async Task<bool> Ve(ActorActual actor) => await EnUnaTransaccion(pila, actor, sp => sp.GetRequiredService<IRepositorioDeTraslados>().PorId(traslado.Id)) is not null;

        Assert.Equal((true, true, false, true, true), (await Ve(Encargado(cusco)), await Ve(Encargado(ayacucho)), await Ve(Encargado(lima)), await Ve(Sistema), await Ve(ActorActual.DeIntegracion("expiracion-de-reservas"))));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task UnEncargadoNoAlcanzaLosPuertosDeReservasNiLosTrabajos_LaPoliticaDeFilasLeRechazaLaEscrituraAjena()
    {
        var (lima, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var pedido = Guid.NewGuid();
        var solicitud = new[] { new SolicitudDeTrasladoDePedido(cusco, libro, 1) };

        await Assert.ThrowsAsync<AlcanceDeDatosDenegadoException>(() => EnUnaTransaccion(pila, Encargado(lima), sp => sp.GetRequiredService<IGeneradorDeTrasladosDePedido>().Generar(pedido, ayacucho, solicitud)));
        var propio = await EnUnaTransaccion(pila, Encargado(ayacucho), sp => sp.GetRequiredService<IGeneradorDeTrasladosDePedido>().Generar(Guid.NewGuid(), ayacucho, solicitud));

        Assert.Single(propio);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task SinActorConContextoElAccesoFallaCerrado()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);

        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnUnaTransaccion(pila, null, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(Reposicion(libro, cusco, ayacucho)); return 0; }));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnUnaTransaccion(pila, ActorActual.Anonimo(), async sp => { await sp.GetRequiredService<IBuzonDeSalida>().Insertar([new MensajeSaliente(Guid.NewGuid(), "aviso", $"traslado:{Guid.NewGuid():D}:solicitado", "cusco", "{}", Ahora)]); return 0; }));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task LaPoliticaDeAccesoDelRolNoDejaBorrarUnTraslado_AunqueElCodigoLoIntentara()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(Reposicion(libro, cusco, ayacucho)); return 0; });

        var error = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => EnUnaTransaccion(pila, Sistema, async sp =>
        {
            await using var cmd = new Npgsql.NpgsqlCommand("DELETE FROM traslados.traslados", (Npgsql.NpgsqlConnection)sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>().Conexion, (Npgsql.NpgsqlTransaction)sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>().Transaccion);
            return await cmd.ExecuteNonQueryAsync();
        }));

        Assert.Equal("42501", error.SqlState);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task CancelarPorLaAnulacionDelPedidoTomaLosTrasladosOrdenadosYLosCancelaEnLaMismaTransaccion()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var pedido = Guid.NewGuid();
        var uno = Vinculado(libro, cusco, ayacucho, pedido);
        var dos = Vinculado(await bd.AgregarEdicion(), cusco, ayacucho, pedido);
        await EnUnaTransaccion(pila, Sistema, async sp =>
        {
            var repo = sp.GetRequiredService<IRepositorioDeTraslados>();
            await repo.InsertarVinculado(uno);
            await repo.InsertarVinculado(dos);
            return 0;
        });

        await EnUnaTransaccion(pila, Sistema, async sp =>
        {
            await sp.GetRequiredService<IEnumerable<IAlLiberarPedido>>().OfType<CancelarTrasladosDePedido>().Single().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [uno.Id, dos.Id]);
            return 0;
        });

        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados WHERE estado = 'cancelado' AND motivo_de_cierre = 'pedido-anulado'"));
        Assert.Equal(2, pila.Auditoria.Contar("TrasladoCancelado"));
        Assert.Equal(6L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task LaAnulacionDeUnPedidoConUnActorSinAlcanceSobreElTraslado_FallaCerradoYNoDejaElTrasladoActivoEnSilencio()
    {
        var (lima, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var pedido = Guid.NewGuid();
        var traslado = Vinculado(libro, cusco, ayacucho, pedido);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().InsertarVinculado(traslado); return 0; });

        await Assert.ThrowsAsync<AlcanceDeDatosDenegadoException>(() => EnUnaTransaccion(pila, Encargado(lima), async sp =>
        {
            await sp.GetRequiredService<IEnumerable<IAlLiberarPedido>>().OfType<CancelarTrasladosDePedido>().Single().AlLiberar(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [traslado.Id]);
            return 0;
        }));

        Assert.Equal("solicitado", await bd.Consultar<string>($"SELECT estado FROM traslados.traslados WHERE id = '{traslado.Id}'"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task UnEncargadoSinSucursalTieneActorPeroNoAlcance_ElAccesoFallaCerradoEnLugarDeDevolverCeroFilas()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Reposicion(libro, cusco, ayacucho);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado); return 0; });
        var sinSucursal = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal);

        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => EnUnaTransaccion(pila, sinSucursal, sp => sp.GetRequiredService<IRepositorioDeTraslados>().PorId(traslado.Id)));

        Assert.Contains(pila.Alertas.Alertas, a => a.Motivo == "alcance-de-datos-sin-actor");
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task UnErrorDeIntegridadSePropagaSinElDetailDeLaFila_ElTextoLibreNuncaSale()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd, conTraslados: true);
        var traslado = Traslado.SolicitarReposicion(Guid.NewGuid(), libro, cusco, ayacucho, 1, "MOTIVO-CENTINELA-77", Guid.NewGuid(), Ahora);
        await EnUnaTransaccion(pila, Sistema, async sp => { await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado); return 0; });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => EnUnaTransaccion(pila, Sistema, async sp =>
        {
            // La misma clave primaria: la base responde 23505 con el Detail de la fila.
            await sp.GetRequiredService<IRepositorioDeTraslados>().Insertar(traslado);
            return 0;
        }));

        Assert.Contains("23505", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("CENTINELA", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }
}
