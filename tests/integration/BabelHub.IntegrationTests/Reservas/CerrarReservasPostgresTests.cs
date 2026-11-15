using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T17: <c>CerrarReservas</c> con PostgreSQL real y el rol real de la aplicación (SEL-11, SEL-21, SEL-28, SEL-29, SEL-38, SEL-39): el descuento del físico baja una sola vez aunque las operaciones lleguen a la vez.</summary>
public sealed class CerrarReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Escenario(Guid Lima, Guid Cusco, Guid Libro, ReservarEntrada Entrada);

    /// <summary>Una reserva de recojo en Lima confirmada: físico 9 y reservado 1 en Lima.</summary>
    private async Task<(PilaDeReservas Pila, Escenario Datos)> ReservaConfirmadaDeRecojo()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 9, 0);
        var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")))).EsExito);
        return (pila, new Escenario(lima, cusco, libro, entrada));
    }

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    private static Task<Resultado<CierreDeReserva>> Entregar(PilaDeReservas pila, ActorActual actor, Escenario d, string? clave = null) =>
        pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(actor, new CerrarReservasEntrada(d.Entrada.PedidoId, MotivoDeCierreDeReserva.Entrega, [d.Entrada.Lineas[0].LineaId], null, clave ?? Guid.NewGuid().ToString("N")));

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task LaSucursalDeRecojoEntrega_ElFisicoPasaDeNueveAOcho_YLaReservaQuedaCerrada()
    {
        var (pila, d) = await ReservaConfirmadaDeRecojo();
        using (pila)
        {
            var resultado = await Entregar(pila, Encargado(d.Lima), d);

            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            Assert.Equal([false], resultado.Valor.Lineas.Select(l => l.YaCerrada));
            Assert.Equal((8, 0), await bd.StockDe(d.Lima, d.Libro));
            Assert.Equal(("cerrada", "entrega"), (await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{d.Entrada.PedidoId}'"), await bd.Consultar<string>($"SELECT cerrada_por FROM reservas.reserva_lineas WHERE pedido_id = '{d.Entrada.PedidoId}'")));
            var auditada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "StockDescontadoPorEntrega");
            Assert.Equal(("9", "8"), (auditada.Detalle["cantidadAnterior"], auditada.Detalle["cantidadPosterior"]));
            Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.operaciones_idempotentes WHERE pedido_id = '{d.Entrada.PedidoId}' AND tipo = 'cerrar'"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task RepetirConLaMismaClave_DevuelveElResultadoOriginal_YConOtraClave_YaCerrada_SinBajarElFisicoOtraVez()
    {
        var (pila, d) = await ReservaConfirmadaDeRecojo();
        using (pila)
        {
            var clave = Guid.NewGuid().ToString("N");
            var actor = Encargado(d.Lima);
            Assert.True((await Entregar(pila, actor, d, clave)).EsExito);

            var misma = await Entregar(pila, actor, d, clave);
            var otra = await Entregar(pila, actor, d);

            Assert.Equal([false], misma.Valor.Lineas.Select(l => l.YaCerrada));
            Assert.Equal([true], otra.Valor.Lineas.Select(l => l.YaCerrada));
            Assert.Equal((8, 0), await bd.StockDe(d.Lima, d.Libro));
            Assert.Equal(1, pila.Auditoria.Contar("StockDescontadoPorEntrega"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task UnEncargadoDeOtraSucursalOUnRolSinAlcance_RecibeProhibidoYElFisicoNoCambia()
    {
        var (pila, d) = await ReservaConfirmadaDeRecojo();
        using (pila)
        {
            var deCusco = await Entregar(pila, Encargado(d.Cusco), d);
            var logistica = await Entregar(pila, ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica), d);
            var cliente = await Entregar(pila, ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente), d);
            var sinActor = await Entregar(pila, null!, d);

            Assert.All([deCusco, logistica, cliente], r => Assert.Equal(CategoriaDeError.Prohibido, r.Error!.Categoria));
            Assert.Equal(CategoriaDeError.NoAutenticado, sinActor.Error!.Categoria);
            Assert.Equal((9, 1), await bd.StockDe(d.Lima, d.Libro));
            Assert.Equal(["fuera-de-sucursal", "fuera-de-rol", "fuera-de-rol", "sin-sesion"], pila.Denegaciones.Motivos);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task DiezEntregasSimultaneasConClavesDistintas_BajanElFisicoUnaSolaVez_LasDemasSonYaCerrada()
    {
        var (pila, d) = await ReservaConfirmadaDeRecojo();
        using (pila)
        {
            var compuerta = new TaskCompletionSource();
            var tareas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await compuerta.Task;
                return await Entregar(pila, Encargado(d.Lima), d);
            })).ToList();
            compuerta.SetResult();

            var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(60));

            Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
            Assert.Equal(1, resultados.Count(r => !r.Valor.Lineas.Single().YaCerrada));
            Assert.Equal((8, 0), await bd.StockDe(d.Lima, d.Libro));
            Assert.Equal(1, pila.Auditoria.Contar("StockDescontadoPorEntrega"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task LaAnulacionYLaEntregaALaVez_AplicanSoloLaPrimera_ElFisicoBajaUnaVezONinguna_LaOtraEsEstadoInvalido()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 500, 0);
        using var pila = new PilaDeReservas(bd);

        for (var ronda = 0; ronda < 12; ronda++)
        {
            var entrada = ARecojo("lima", [Linea(libro, 1)]);
            Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
            Assert.True((await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")))).EsExito);
            var datos = new Escenario(lima, Guid.Empty, libro, entrada);
            var compuerta = new TaskCompletionSource();
            var entrega = Task.Run(async () =>
            {
                await compuerta.Task;
                return (await Entregar(pila, Encargado(lima), datos)).Error?.Clave;
            });
            var anulacion = Task.Run(async () =>
            {
                await compuerta.Task;
                return (await pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado, null))).Error?.Clave;
            });
            compuerta.SetResult();

            var errores = await Task.WhenAll(entrega, anulacion).WaitAsync(TimeSpan.FromSeconds(60));

            // Exactamente una se aplica y la otra es estado inválido, sin cambios parciales.
            Assert.Equal(1, errores.Count(e => e == "reserva-estado-invalido"));
            Assert.Equal(1, errores.Count(e => e is null));
            var estado = await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'");
            Assert.Contains(estado, new[] { "cerrada", "liberada" });
        }

        // Coherencia: el físico bajó una sola vez por cada reserva cerrada y lo reservado coincide con las líneas activas.
        var cerradas = await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas WHERE estado = 'cerrada'");
        Assert.Equal((int)(500 - cerradas), (await bd.StockDe(lima, libro))!.Value.Fisico);
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaFalla_ElCierreSeRevierte_FisicoLineaYOperacion()
    {
        var (pila, d) = await ReservaConfirmadaDeRecojo();
        using (pila)
        {
            pila.Auditoria.Falla = true;

            await Assert.ThrowsAsync<BabelHub.Inventario.Application.Auditoria.AuditoriaNoDisponibleException>(() => Entregar(pila, Encargado(d.Lima), d));

            Assert.Equal((9, 1), await bd.StockDe(d.Lima, d.Libro));
            Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{d.Entrada.PedidoId}'"));
            Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.operaciones_idempotentes"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-21")]
    public async Task PasarAEnRepartoUnaReservaADomicilio_BajaElFisicoDeCusco_ConLogistica()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 9, 0);
        using var pila = new PilaDeReservas(bd);
        var entrada = ADomicilio("cusco", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")))).EsExito);
        var logistica = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica);
        var envio = Guid.NewGuid();

        var primera = await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(logistica, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Reparto, [entrada.Lineas[0].LineaId], envio, Guid.NewGuid().ToString("N")));
        var repetida = await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(logistica, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Reparto, [entrada.Lineas[0].LineaId], envio, Guid.NewGuid().ToString("N")));

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.Equal([true], repetida.Valor.Lineas.Select(l => l.YaCerrada));
        Assert.Equal((8, 0), await bd.StockDe(cusco, libro));
        Assert.Equal(1, pila.Auditoria.Contar("StockDescontadoPorReparto"));
    }
}
