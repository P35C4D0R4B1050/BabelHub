using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Los dobles provisionales de Carrito (<c>ecommerce-carrito-checkout</c> T08, D07, R02): <b>no se publican</b> (la guarda de arranque lo impide), fallan cerrado salvo el de posesión de ediciones, un registro
/// real anterior se respeta, y el Inventario simulado responde como el contrato v1.
/// </summary>
public class DoblesProvisionalesDelCarritoTests
{
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Los_dobles_de_Pagos_e_Identidad_fallan_cerrado_y_no_dejan_pasar_al_pago()
    {
        var pagos = new CreadorDeIntencionesSinPagos();
        var identidad = new EstadoDeCuentaDelCarritoSinIdentidad();

        await Assert.ThrowsAsync<IntegracionProvisionalNoDisponibleException>(() => pagos.Reabrir(Cuenta, Guid.NewGuid()));
        await Assert.ThrowsAsync<IntegracionProvisionalNoDisponibleException>(() => pagos.Cancelar(Cuenta, Guid.NewGuid()));
        await Assert.ThrowsAsync<IntegracionProvisionalNoDisponibleException>(() => pagos.Crear(Cuenta, Instantanea(), "huella", DateTimeOffset.UnixEpoch));
        await Assert.ThrowsAsync<IntegracionProvisionalNoDisponibleException>(() => identidad.Consultar(Cuenta));
        Assert.Equal(ResultadoDeLaAceptacion.NoDisponible, await identidad.AceptarLosTextos(Cuenta, "2026-11-01"));
    }

    [Fact]
    public async Task El_unico_doble_abierto_es_el_de_posesion_y_responde_que_no_posee()
    {
        var resultado = await new PosesionDeEdicionesSinBiblioteca().Posee(Cuenta, [Guid.NewGuid()]);

        Assert.Equal(EstadoDePosesion.NoPosee, resultado.Estado);
    }

    [Fact]
    public void En_desarrollo_los_dobles_se_registran_sin_pisar_a_los_reales()
    {
        var servicios = ConReloj();
        servicios.AddSingleton<IPosesionDeEdiciones, PosesionReal>();
        servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.IsType<PosesionReal>(proveedor.GetRequiredService<IPosesionDeEdiciones>());
        Assert.IsType<CreadorDeIntencionesSinPagos>(proveedor.GetRequiredService<ICreadorDeIntencionesDePago>());
        Assert.IsType<InventarioSimuladoDelCarrito>(proveedor.GetRequiredService<IInventarioDelCarrito>());
        Assert.IsType<EstadoDeCuentaDelCarritoSinIdentidad>(proveedor.GetRequiredService<IEstadoDeCuenta>());
        Assert.IsType<LimitadorDelCarritoProvisional>(proveedor.GetRequiredService<ILimitadorDelCarrito>());
        Assert.IsType<AuditoriaDelCarritoProvisional>(proveedor.GetRequiredService<IAuditoriaDelCarrito>());
        Assert.IsType<MarcaDeCancelacionProvisional>(proveedor.GetRequiredService<IMarcaDeCancelacion>());
        Assert.IsType<ConsentimientoDeAvisosProvisional>(proveedor.GetRequiredService<IConsentimientoDeAvisos>());
        Assert.IsType<AlertasDelCarritoProvisional>(proveedor.GetRequiredService<IAlertasDelCarrito>());
    }

    [Fact]
    public void En_produccion_el_arranque_falla_si_algun_puerto_de_Carrito_queda_con_un_doble_provisional()
    {
        var servicios = ConReloj();

        var error = Assert.Throws<InvalidOperationException>(() => servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion: true));

        Assert.Contains(nameof(IPosesionDeEdiciones), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ICreadorDeIntencionesDePago), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IInventarioDelCarrito), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IEstadoDeCuenta), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ILimitadorDelCarrito), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IAuditoriaDelCarrito), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IMarcaDeCancelacion), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IConsentimientoDeAvisos), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IAlertasDelCarrito), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_produccion_el_arranque_pasa_cuando_todos_los_puertos_tienen_su_implementacion_real()
    {
        var servicios = ConReloj();
        servicios.AddSingleton<IPosesionDeEdiciones, PosesionReal>();
        servicios.AddSingleton<ICreadorDeIntencionesDePago, CreadorReal>();
        servicios.AddSingleton<IInventarioDelCarrito, InventarioReal>();
        servicios.AddSingleton<IEstadoDeCuenta, EstadoReal>();
        servicios.AddSingleton<ILimitadorDelCarrito, LimitadorReal>();
        servicios.AddSingleton<IAuditoriaDelCarrito, AuditoriaReal>();
        servicios.AddSingleton<IMarcaDeCancelacion, MarcaReal>();
        servicios.AddSingleton<IConsentimientoDeAvisos, ConsentimientoReal>();
        servicios.AddSingleton<IAlertasDelCarrito, AlertasReales>();

        var resultado = servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion: true);

        Assert.Same(servicios, resultado);
    }

    [Fact]
    public async Task El_Inventario_simulado_responde_la_disponibilidad_y_trata_las_desconocidas_como_no_registradas()
    {
        var inventario = Inventario(out _);
        var conocida = Guid.NewGuid();
        var desconocida = Guid.NewGuid();
        inventario.FijarStock(conocida, 5);

        var respuesta = (await inventario.ConsultarDisponibilidad([conocida, desconocida]));

        Assert.True(respuesta.Exitosa);
        Assert.Equal(5, Assert.Single(respuesta.Valor.Ediciones).VendibleEnLaRed);
        Assert.Equal(desconocida, Assert.Single(respuesta.Valor.NoRegistradas));
    }

    [Fact]
    public async Task Repetir_la_clave_devuelve_la_reserva_original_aunque_haya_expirado_y_una_clave_nueva_sobre_la_expirada_es_estado_invalido()
    {
        var inventario = Inventario(out var reloj);
        var edicion = Guid.NewGuid();
        inventario.FijarStock(edicion, 10);
        var pedido = Guid.NewGuid();
        var lineas = new[] { new LineaParaElInventario(Guid.NewGuid(), edicion, 2) };
        var entrega = new EntregaParaElInventario(TipoDeEntrega.Recojo, "sede-central", null);

        var primera = (await inventario.Reservar(pedido, 1, entrega, lineas));
        reloj.Avanzar(TimeSpan.FromMinutes(16));
        var repetida = (await inventario.Reservar(pedido, 1, entrega, lineas));
        var otraClave = (await inventario.Reservar(pedido, 2, entrega, lineas));

        Assert.Equal(TipoDeResultadoDeReservar.Reservada, primera.Tipo);
        Assert.Equal(TipoDeResultadoDeReservar.Reservada, repetida.Tipo);
        Assert.True(repetida.Reserva!.EsRepeticion);
        Assert.Equal(primera.Reserva!.ExpiraEn, repetida.Reserva.ExpiraEn);
        Assert.Equal(TipoDeResultadoDeReservar.EstadoInvalido, otraClave.Tipo);
    }

    [Fact]
    public async Task El_cuarto_reemplazo_de_un_pedido_es_429_con_el_instante_de_la_reserva_vigente_que_se_conserva()
    {
        var inventario = Inventario(out _);
        var edicion = Guid.NewGuid();
        inventario.FijarStock(edicion, 10);
        var pedido = Guid.NewGuid();
        var linea = Guid.NewGuid();
        var entrega = new EntregaParaElInventario(TipoDeEntrega.Recojo, "sede-central", null);

        ResultadoDeReservar? ultima = null;
        for (var generacion = 1; generacion <= 4; generacion++)
        {
            ultima = (await inventario.Reservar(pedido, generacion, entrega, [new LineaParaElInventario(linea, edicion, generacion)]));
        }

        Assert.Equal(TipoDeResultadoDeReservar.Reservada, ultima!.Tipo);
        var quinta = (await inventario.Reservar(pedido, 5, entrega, [new LineaParaElInventario(linea, edicion, 5)]));

        Assert.Equal(TipoDeResultadoDeReservar.LimiteExcedido, quinta.Tipo);
        Assert.NotNull(quinta.DisponibleDesde);
        Assert.Equal(4, inventario.Reservado(edicion));
    }

    [Fact]
    public async Task Una_reserva_sin_stock_suficiente_no_reserva_nada_y_dice_cuanto_hay()
    {
        var inventario = Inventario(out _);
        var hay = Guid.NewGuid();
        var falta = Guid.NewGuid();
        inventario.FijarStock(hay, 5);
        inventario.FijarStock(falta, 1);
        var entrega = new EntregaParaElInventario(TipoDeEntrega.Domicilio, null, "ayacucho");

        var resultado = (await inventario.Reservar(Guid.NewGuid(), 1, entrega, [new LineaParaElInventario(Guid.NewGuid(), hay, 2), new LineaParaElInventario(Guid.NewGuid(), falta, 3)]));

        Assert.Equal(TipoDeResultadoDeReservar.StockInsuficiente, resultado.Tipo);
        var sin = Assert.Single(resultado.SinStock!);
        Assert.Equal((falta, 3, 1), (sin.EdicionId, sin.Solicitada, sin.DisponibleEnLaRed));
        Assert.Equal(0, inventario.Reservado(hay));
    }

    [Fact]
    public async Task Liberar_es_idempotente_un_pedido_desconocido_es_no_existia_y_uno_confirmado_no_se_libera()
    {
        var inventario = Inventario(out _);
        var edicion = Guid.NewGuid();
        inventario.FijarStock(edicion, 3);
        var pedido = Guid.NewGuid();
        var entrega = new EntregaParaElInventario(TipoDeEntrega.Recojo, "sede-central", null);
        await inventario.Reservar(pedido, 1, entrega, [new LineaParaElInventario(Guid.NewGuid(), edicion, 2)]);

        Assert.Equal(ResultadoDeLiberar.NoExistia, (await inventario.Liberar(Guid.NewGuid(), "checkout-cancelado")));
        Assert.Equal(ResultadoDeLiberar.Liberada, (await inventario.Liberar(pedido, "checkout-cancelado")));
        Assert.Equal(ResultadoDeLiberar.Liberada, (await inventario.Liberar(pedido, "checkout-cancelado")));
        Assert.Equal(0, inventario.Reservado(edicion));

        var confirmado = Guid.NewGuid();
        await inventario.Reservar(confirmado, 1, entrega, [new LineaParaElInventario(Guid.NewGuid(), edicion, 1)]);
        inventario.Confirmar(confirmado);
        Assert.Equal(ResultadoDeLiberar.PedidoConfirmado, (await inventario.Liberar(confirmado, "checkout-cancelado")));
    }

    [Fact]
    public async Task Caido_el_Inventario_simulado_responde_sin_respuesta_en_todo()
    {
        var inventario = Inventario(out _);
        inventario.Caido = true;
        var entrega = new EntregaParaElInventario(TipoDeEntrega.Recojo, "sede-central", null);
        var linea = new LineaParaElInventario(Guid.NewGuid(), Guid.NewGuid(), 1);

        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, (await inventario.ConsultarDisponibilidad([linea.EdicionId])).Estado);
        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, (await inventario.ConsultarPrevia(entrega, [linea])).Estado);
        Assert.Null((await inventario.ListarSucursales()));
        Assert.Equal(TipoDeResultadoDeReservar.NoDisponible, (await inventario.Reservar(Guid.NewGuid(), 1, entrega, [linea])).Tipo);
        Assert.Equal(ResultadoDeLiberar.NoSePudo, (await inventario.Liberar(Guid.NewGuid(), "checkout-cancelado")));
    }

    [Fact]
    public void La_clave_de_reserva_cumple_el_formato_del_Inventario_y_cambia_con_la_generacion()
    {
        var pedido = Guid.NewGuid();

        var uno = ClaveDeReserva.Para(pedido, 1);
        var dos = ClaveDeReserva.Para(pedido, 2);

        Assert.Equal($"res-{pedido:D}-1", uno);
        Assert.NotEqual(uno, dos);
        Assert.Matches("^[A-Za-z0-9_-]{16,80}$", uno);
        Assert.Throws<ArgumentException>(() => ClaveDeReserva.Para(Guid.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaveDeReserva.Para(pedido, -1));
    }

    private static ServiceCollection ConReloj()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IReloj>(new RelojDePrueba());
        return servicios;
    }

    private static InventarioSimuladoDelCarrito Inventario(out RelojDePrueba reloj)
    {
        reloj = new RelojDePrueba();
        return new InventarioSimuladoDelCarrito(reloj);
    }

    private static InstantaneaDeCompra Instantanea() =>
        new(Guid.NewGuid(), Cuenta, TipoDeEntregaDeCompra.Recojo, "sede-central", null, 10m, 0m, 10m, null, [], [], "huella");

    private sealed class RelojDePrueba : IReloj
    {
        private DateTimeOffset _ahora = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Ahora => _ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(_ahora.UtcDateTime.AddHours(-5));

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }

    private sealed class PosesionReal : IPosesionDeEdiciones
    {
        public Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDePosesion.NoPosee);
    }

    private sealed class CreadorReal : ICreadorDeIntencionesDePago
    {
        public Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeCrearIntencion.Creada);

        public Task<ResultadoDeReabrirIntencion> Reabrir(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeReabrirIntencion.Reabierta);

        public Task<ResultadoDeCancelarIntencion> Cancelar(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeCancelarIntencion.Cancelada);
    }

    private sealed class InventarioReal : IInventarioDelCarrito
    {
        public Task<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ListaDeSucursales?> ListarSucursales(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class LimitadorReal : ILimitadorDelCarrito
    {
        public Task<ResultadoDeLimiteDelCarrito> CrearCarritoDeVisitante(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeLimiteDelCarrito> EditarLineas(Guid? cuentaId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeLimiteDelCarrito> RevisarCheckout(Guid cuentaId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeLimiteDelCarrito> PasarAlPago(Guid cuentaId, string claveDeRepeticion, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeLimiteDelCarrito> VolverOReiniciar(Guid cuentaId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class AuditoriaReal : IAuditoriaDelCarrito
    {
        public Task FusionAplicada(Guid cuentaId, int lineasDelVisitante, IReadOnlyList<MotivoDeAjusteDeAviso> ajustes, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task EdicionesRetiradas(int ediciones, int carritosAfectados, int lineasQuitadas, int lineasAnotadas, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task CheckoutTerminado(string motivo, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task CarritoEliminadoPorIdentidad(string motivo, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task PurgaRealizada(string trabajo, int cantidad, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task PasoAlPagoIniciado(bool conImpresos, bool aDomicilio, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task PasoAlPagoAbortado(string motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class MarcaReal : IMarcaDeCancelacion
    {
        public Task<EstadoDeLaMarcaDeCancelacion> LeerConBloqueo(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(EstadoDeLaMarcaDeCancelacion.Libre);
    }

    private sealed class ConsentimientoReal : IConsentimientoDeAvisos
    {
        public Task<OfertaDeConsentimiento> Consultar(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(OfertaDeConsentimiento.NoSeOfrece);

        public Task<bool> Guardar(Guid cuentaId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default) => Task.FromResult(true);
    }

    private sealed class AlertasReales : IAlertasDelCarrito
    {
        public Task Publicar(TipoDeAlertaDelCarrito tipo, int cantidad, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class EstadoReal : IEstadoDeCuenta
    {
        public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult<EstadoDeCuenta?>(null);

        public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.Aceptada);
    }
}
