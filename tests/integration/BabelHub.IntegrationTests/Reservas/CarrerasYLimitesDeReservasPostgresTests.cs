using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T28 a T30 de <c>inventario-seleccion-fuente</c>: lo que las pruebas por tarea (T10 a T27) no juntaban. Límites con la reversión real de PostgreSQL (el rechazo por stock deja los eventos de límite como estaban),
/// el reemplazo frente a la confirmación y a la liberación, la expiración frente a la entrega, dos reservas que liberan la misma vencida, anular con un traslado en tránsito y el cierre con otra carga u otra causa,
/// y la vida completa de una reserva con el reloj controlado. Cada carrera comprueba la consecuencia observable: una sola operación gana, lo reservado coincide con las líneas activas y no hubo interbloqueos.
/// </summary>
public sealed class CarrerasYLimitesDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(120);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class ReceptorDePrueba : IAlLiberarPedido
    {
        private readonly object _candado = new();

        public List<(Guid Pedido, MotivoDeLiberacionDeReserva Motivo, Guid[] Traslados)> Recibidos { get; } = [];

        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default)
        {
            lock (_candado)
            {
                Recibidos.Add((pedidoId, motivo, [.. trasladoIds]));
            }

            return Task.CompletedTask;
        }
    }

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    private static Task<Resultado<ReservaDelPedido>> Confirmar(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(pedido, Guid.NewGuid().ToString("N")));

    private static Task<Resultado<ReservaDelPedido>> Liberar(PilaDeReservas pila, Guid pedido, MotivoDeLiberacionDeReserva motivo) =>
        pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(pedido, motivo, null));

    private static Task<Resultado<CierreDeReserva>> Entregar(PilaDeReservas pila, ActorActual actor, Guid pedido, Guid linea, string? clave = null) =>
        pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(actor, new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Entrega, [linea], null, clave ?? Guid.NewGuid().ToString("N")));

    private async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    private async Task<long> InterbloqueosDespuesDe(long antes)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        return await Interbloqueos() - antes;
    }

    private async Task AfirmarCoherencia(Guid edicion)
    {
        Assert.Equal(await bd.ReservadoDeLaRed(edicion), await bd.UnidadesDeLineasActivas(edicion));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM fuentes.stock WHERE edicion_id = '{edicion}' AND (reservado < 0 OR reservado > fisico)"));
    }

    private static async Task<T[]> Simultaneas<T>(params Func<Task<T>>[] trabajos)
    {
        var compuerta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tareas = trabajos.Select(trabajo => Task.Run(async () =>
        {
            await compuerta.Task;
            return await trabajo();
        })).ToList();
        compuerta.SetResult();
        return await Task.WhenAll(tareas).WaitAsync(Limite);
    }

    private async Task<long> EventosDeLimite() => await bd.Consultar<long>("SELECT count(*) FROM plataforma.eventos_limite");

    private async Task<(Guid Lima, Guid Libro)> Preparar(int fisico)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, fisico, 0);
        return (lima, libro);
    }

    // ---------- T28: límites y reversión real ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task UnPedidoDeDosLineasConLaSegundaSinStock_NoReservaNingunaYNoDejaEventosDeLimite_AntesNiDespues()
    {
        var (lima, libro) = await Preparar(5);
        var agotado = await bd.AgregarEdicion();
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var antes = await EventosDeLimite();

        var resultado = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 1), Linea(agotado, 1)]));

        Assert.Equal("stock-insuficiente", resultado.Error!.Clave);
        Assert.Equal(0L, antes);
        Assert.Equal(0L, await EventosDeLimite());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas"));
        Assert.Equal((5, 0), await bd.StockDe(lima, libro));

        // Con el cupo intacto, el cliente todavía puede reservar las cinco de la hora y la sexta es la que se rechaza.
        for (var i = 0; i < 5; i++)
        {
            var nueva = ARecojo("lima", [Linea(libro, 1)]);
            Assert.True((await pila.Reservar(cliente, nueva)).EsExito);
            Assert.True((await Confirmar(pila, nueva.PedidoId)).EsExito);
        }

        var sexta = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 1)]));
        Assert.Equal(CategoriaDeError.Limite, sexta.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task ElReemplazo_ConservaLaAnteriorSiLaNuevaNoTieneStock_YNingunReemplazoConsumeEventosDeLimite()
    {
        var (lima, libro) = await Preparar(10);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 3)], pedido))).EsExito);
        var eventosTrasLaPrimera = await EventosDeLimite();
        Assert.True(eventosTrasLaPrimera > 0);

        var aceptado = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 4)], pedido));
        var sinStock = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 20)], pedido));

        Assert.True(aceptado.EsExito, aceptado.Error?.Clave);
        Assert.Equal("stock-insuficiente", sinStock.Error!.Clave);
        Assert.Equal(eventosTrasLaPrimera, await EventosDeLimite());
        Assert.Equal((10, 4), await bd.StockDe(lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND estado = 'pendiente'"));
        Assert.Equal(1, await bd.Consultar<int>($"SELECT max(reemplazos) FROM reservas.reservas WHERE pedido_id = '{pedido}'"));
        await AfirmarCoherencia(libro);
    }

    // ---------- T29: carreras sobre una misma reserva ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task UnReemplazoYUnaConfirmacionALaVez_AplicanLaPrimera_SinEfectosParcialesNiInterbloqueos()
    {
        var (_, libro) = await Preparar(500);
        using var pila = new PilaDeReservas(bd);
        var antes = await Interbloqueos();

        for (var ronda = 0; ronda < 10; ronda++)
        {
            var cliente = Cliente();
            var pedido = Guid.NewGuid();
            Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 2)], pedido))).EsExito);

            var resultados = await Simultaneas<(string? Error, string Operacion)>(
                async () => ((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 3)], pedido))).Error?.Clave, "reemplazo"),
                async () => ((await Confirmar(pila, pedido)).Error?.Clave, "confirmar"));

            // La confirmación nunca falla (confirma la reserva que haya en ese momento); el reemplazo, o gana antes y lo confirmado es lo nuevo, o llega tarde y es estado inválido.
            Assert.Null(resultados.Single(r => r.Operacion == "confirmar").Error);
            var reemplazo = resultados.Single(r => r.Operacion == "reemplazo").Error;
            Assert.True(reemplazo is null or "reserva-estado-invalido", reemplazo);
            var activas = await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND estado IN ('pendiente', 'confirmada')");
            Assert.Equal(1L, activas);
            Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND estado IN ('pendiente', 'confirmada')"));
            Assert.Equal(reemplazo is null ? 3 : 2, await bd.Consultar<int>($"SELECT cantidad FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND estado = 'confirmada'"));
        }

        await AfirmarCoherencia(libro);
        Assert.Equal(0L, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task UnReemplazoYUnaLiberacionALaVez_NoDejanNadaReservadoSeaCualSeaElOrden()
    {
        var (lima, libro) = await Preparar(500);
        using var pila = new PilaDeReservas(bd);
        var antes = await Interbloqueos();

        for (var ronda = 0; ronda < 10; ronda++)
        {
            var cliente = Cliente();
            var pedido = Guid.NewGuid();
            Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 2)], pedido))).EsExito);

            var resultados = await Simultaneas<(string? Error, string Operacion)>(
                async () => ((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 3)], pedido))).Error?.Clave, "reemplazo"),
                async () => ((await Liberar(pila, pedido, MotivoDeLiberacionDeReserva.CheckoutCancelado)).Error?.Clave, "liberar"));

            Assert.Null(resultados.Single(r => r.Operacion == "liberar").Error);
            var reemplazo = resultados.Single(r => r.Operacion == "reemplazo").Error;
            Assert.True(reemplazo is null or "reserva-estado-invalido", reemplazo);
            Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND estado IN ('pendiente', 'confirmada')"));
        }

        Assert.Equal((500, 0), await bd.StockDe(lima, libro));
        await AfirmarCoherencia(libro);
        Assert.Equal(0L, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task DosReservasQueLiberanALaVezLaMismaVencida_LaExpiranUnaSolaVez_YSoloUnaConsigueLaUnidad()
    {
        var (lima, libro) = await Preparar(1);
        using var pila = new PilaDeReservas(bd);
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 1)]))).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(20);
        var antes = await Interbloqueos();

        var resultados = await Simultaneas(
            () => pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 1)])),
            () => pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 1)])));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.Equal("stock-insuficiente", resultados.Single(r => !r.EsExito).Error!.Clave);
        Assert.Equal(1, pila.Auditoria.Contar("ReservaExpirada"));
        Assert.Equal(TipoDeActor.Sistema, Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaExpirada").Actor);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas WHERE estado = 'expirada'"));
        Assert.Equal((1, 1), await bd.StockDe(lima, libro));
        await AfirmarCoherencia(libro);
        Assert.Equal(0L, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaExpiracionYLaEntregaALaVez_UnaConfirmadaNoExpira_LaEntregaBajaElFisicoUnaVez()
    {
        var (lima, libro) = await Preparar(9);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(2);

        var resultados = await Simultaneas(
            async () => (await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId))).EsExito,
            async () => (await Entregar(pila, Encargado(lima), entrada.PedidoId, entrada.Lineas[0].LineaId)).EsExito);

        Assert.All(resultados, ok => Assert.True(ok));
        Assert.Equal(0, pila.Auditoria.Contar("ReservaExpirada"));
        Assert.Equal(1, pila.Auditoria.Contar("StockDescontadoPorEntrega"));
        Assert.Equal((8, 0), await bd.StockDe(lima, libro));
        Assert.Equal("cerrada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-28")]
    public async Task LaEntregaDeUnaReservaPendienteVencida_SeRechazaPorEstadoInvalido_YLaExpiracionLaLibera()
    {
        var (lima, libro) = await Preparar(9);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(20);

        var resultados = await Simultaneas(
            async () => (await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId))).Error?.Clave,
            async () => (await Entregar(pila, Encargado(lima), entrada.PedidoId, entrada.Lineas[0].LineaId)).Error?.Clave);

        Assert.Null(resultados[0]);
        Assert.Equal("reserva-estado-invalido", resultados[1]);

        // Quien expira no espera el bloqueo del pedido (`IntentarPedido`): si la entrega ya lo tenía, esa pasada no expira nada y lo recoge la siguiente (el trabajo corre cada minuto; lo cubren las unitarias de `ExpirarReservasVencidasTests`). Según quién llegó primero el estado es uno de dos y nunca
        // un intermedio: el físico no se toca y lo reservado y la línea van juntos (libre y expirada, o reservada y pendiente).
        var estado = await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'");
        var stock = await bd.StockDe(lima, libro);
        Assert.True((stock, estado) is ((9, 0), "expirada") or ((9, 1), "pendiente"), $"Estado intermedio tras la carrera: stock {stock}, línea {estado}.");

        // La siguiente pasada del expirador (sin competencia) siempre la libera, y la entrega sigue rechazada.
        Assert.Null((await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId))).Error?.Clave);
        Assert.Equal("reserva-estado-invalido", (await Entregar(pila, Encargado(lima), entrada.PedidoId, entrada.Lineas[0].LineaId)).Error?.Clave);
        Assert.Equal((9, 0), await bd.StockDe(lima, libro));
        Assert.Equal("expirada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task AnularConUnTrasladoEnTransito_EsExito_ElStockNoCambia_LaVentaQuedaAnuladaYElReceptorRecibeElTraslado()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 9, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        var traslados = new TrasladosDeIntegracionDoble(bd);
        var receptor = new ReceptorDePrueba();
        using var pila = new PilaDeReservas(bd, s =>
        {
            s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => traslados);
            s.AddSingleton<IAlLiberarPedido>(receptor);
        });
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        var confirmada = await Confirmar(pila, entrada.PedidoId);
        var traslado = confirmada.Valor.Traslados.Single().TrasladoId;

        // El despacho del traslado consume la reserva en el origen: el físico de Cusco baja y la línea queda cerrada por traslado (las unidades van en tránsito).
        var consumo = await pila.EnUnAmbito(
            ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, cusco),
            sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(_ => sp.GetRequiredService<IReservasParaTraslados>().ConsumirEnElOrigen(entrada.PedidoId, cusco, libro)));
        Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, consumo);
        Assert.Equal((8, 0), await bd.StockDe(cusco, libro));

        var primera = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado);
        var repetida = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.True(repetida.EsExito, repetida.Error?.Clave);
        Assert.Equal((8, 0), await bd.StockDe(cusco, libro));
        Assert.Equal((0, 0), await bd.StockDe(lima, libro));
        Assert.True(await bd.Consultar<bool>($"SELECT anulada_en IS NOT NULL FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}' AND estado = 'cerrada'"));
        Assert.All(receptor.Recibidos, r =>
        {
            Assert.Equal((entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado), (r.Pedido, r.Motivo));
            Assert.Equal([traslado], r.Traslados);
        });
        Assert.NotEmpty(receptor.Recibidos);
        await AfirmarCoherencia(libro);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task CerrarConLaMismaClaveYOtraCarga_EsConflicto_YLaClaveNuevaSobreUnaLineaLiberada_EsEstadoInvalido()
    {
        var (lima, libro) = await Preparar(20);
        var otro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, otro, 20, 0);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1), Linea(otro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        var actor = Encargado(lima);
        var clave = Guid.NewGuid().ToString("N");

        var primera = await Entregar(pila, actor, entrada.PedidoId, entrada.Lineas[0].LineaId, clave);
        var otraCarga = await Entregar(pila, actor, entrada.PedidoId, entrada.Lineas[1].LineaId, clave);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.Equal((CategoriaDeError.Conflicto, "idempotencia-en-conflicto"), (otraCarga.Error!.Categoria, otraCarga.Error.Clave));
        Assert.Equal((19, 0), await bd.StockDe(lima, libro));
        Assert.Equal((20, 2), await bd.StockDe(lima, otro));

        // La segunda línea se anula con el pedido y luego se intenta entregar con una clave nueva: estado inválido, sin descontar nada.
        Assert.True((await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado)).EsExito);
        var tarde = await Entregar(pila, actor, entrada.PedidoId, entrada.Lineas[1].LineaId);

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (tarde.Error!.Categoria, tarde.Error.Clave));
        Assert.Equal((20, 0), await bd.StockDe(lima, otro));
        Assert.Equal(1, pila.Auditoria.Contar("StockDescontadoPorEntrega"));
        await AfirmarCoherencia(libro);
        await AfirmarCoherencia(otro);
    }

    // ---------- T30: reloj controlado ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    [Trait("Criterio", "AC-SEL-24")]
    [Trait("Criterio", "AC-SEL-27")]
    [Trait("Criterio", "AC-SEL-30")]
    public async Task LaVidaDeUnaReservaConElRelojControlado_ExtensionUnicaAlosTreintaMinutos_VencimientoConfirmarRechazadoYAvisoIgnorado()
    {
        var (lima, libro) = await Preparar(5);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 2)]);
        var inicio = pila.Reloj.Ahora;
        var reservada = await pila.Reservar(Cliente(), entrada);
        Assert.Equal(inicio.AddMinutes(15), reservada.Valor.ExpiraEn);

        // A los 10 minutos el pago queda pendiente: la expiración pasa a los 30 desde el inicio, una sola vez.
        pila.Reloj.Ahora = inicio.AddMinutes(10);
        var extension = await pila.Ejecutar<ExtenderReservasEntrada, ExtensionDeReserva>(Servicio, new ExtenderReservasEntrada(entrada.PedidoId));
        Assert.True(extension.Valor.Extendida);
        Assert.Equal(inicio.AddMinutes(30), extension.Valor.Reserva.ExpiraEn);

        // A los 20 minutos seguiría vencida sin la extensión: sigue reservando, y el barrido no la toca.
        pila.Reloj.Ahora = inicio.AddMinutes(20);
        Assert.Equal(0, (await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId))).Valor.LineasExpiradas);
        Assert.Equal((5, 2), await bd.StockDe(lima, libro));
        Assert.False((await pila.Ejecutar<ExtenderReservasEntrada, ExtensionDeReserva>(Servicio, new ExtenderReservasEntrada(entrada.PedidoId))).Valor.Extendida);

        // A los 31 minutos venció: no cuenta, el aviso pendiente se ignora, confirmar se rechaza y otro cliente reserva las cinco liberando la vencida con actor «sistema».
        pila.Reloj.Ahora = inicio.AddMinutes(31);
        Assert.False((await pila.Ejecutar<ExtenderReservasEntrada, ExtensionDeReserva>(Servicio, new ExtenderReservasEntrada(entrada.PedidoId))).Valor.Extendida);
        var confirmar = await Confirmar(pila, entrada.PedidoId);
        Assert.Equal("reserva-estado-invalido", confirmar.Error!.Clave);

        var otroCliente = await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 5)]));

        Assert.True(otroCliente.EsExito, otroCliente.Error?.Clave);
        var expirada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaExpirada");
        Assert.Equal(TipoDeActor.Sistema, expirada.Actor);
        Assert.Equal((5, 5), await bd.StockDe(lima, libro));
        Assert.Equal("expirada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        await AfirmarCoherencia(libro);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task ConfirmarLiberarOCerrarConLaAuditoriaNoDisponible_NoCambianElStock_DeLaMismaReserva()
    {
        var (lima, libro) = await Preparar(9);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Confirmar(pila, entrada.PedidoId));
        Assert.Equal((9, 1), await bd.StockDe(lima, libro));
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));

        pila.Auditoria.Falla = false;
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        pila.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Entregar(pila, Encargado(lima), entrada.PedidoId, entrada.Lineas[0].LineaId));
        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado));

        Assert.Equal((9, 1), await bd.StockDe(lima, libro));
        Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }
}
