using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// Correcciones de la revisión de ECO-4c: el aborto desde <c>en_pago</c> condicional (I2), la falla de 3b que aborta (I3), <c>ObtenerParaPagar</c> (I4) y la auditoría de los abortos (I6, DD-11).
/// </summary>
public sealed class CorreccionesDeLaRevisionDeEco4cTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private sealed record Escenario(EntornoDelCarrito Entorno, Checkout Checkout, Guid Impreso);

    private static async Task<Escenario> Listo()
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = e.Impreso(10, 40m, "Impreso");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(impreso, 3));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        e.Inventario.FijarFuente(impreso, "sede-central", false, null);
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null));
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        var checkout = e.Checkouts.Todos.Single();
        e.Inventario.AlReservar = ResultadoDeReservar.Reservada(new ReservaObtenida(
            Guid.Empty,
            Inicio.AddMinutes(15),
            [new SeleccionDeLinea(checkout.Lineas.Single().LineaId, impreso, true, "sede-central", false, null, 10)],
            false));
        return new Escenario(e, checkout, impreso);
    }

    private static Task<Resultado<ResultadoDePasarAlPago>> Pagar(Escenario s) =>
        new PasarAlPago(s.Entorno.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(120m, Los2));

    // ---------- I2 ----------

    [Fact]
    public async Task El_aborto_desde_en_pago_de_una_solicitud_vieja_no_cancela_la_intencion_del_checkout_ya_cambiado()
    {
        var s = await Listo();
        await Pagar(s);
        var cuenta = s.Checkout.CuentaId;
        var pedido = s.Checkout.PedidoId!.Value;
        var generacionVieja = s.Checkout.GeneracionDeReserva - 1;

        var desenlace = await new AbortarElPasoAlPago(s.Entorno.ServiciosDelCheckout())
            .Ejecutar(new SolicitudDeAborto(cuenta, EstadoDelCheckout.EnPago, pedido, generacionVieja, true, "checkout-cambio", null));

        Assert.Equal(DesenlaceDelAborto.NoAplica, desenlace);
        Assert.Equal(0, s.Entorno.Pagos.Cancelaciones);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    public async Task El_aborto_desde_en_pago_de_la_solicitud_vigente_cancela_la_intencion_y_libera()
    {
        var s = await Listo();
        await Pagar(s);

        var desenlace = await new AbortarElPasoAlPago(s.Entorno.ServiciosDelCheckout())
            .Ejecutar(new SolicitudDeAborto(s.Checkout.CuentaId, EstadoDelCheckout.EnPago, s.Checkout.PedidoId!.Value, s.Checkout.GeneracionDeReserva, true, "reserva-expirada", null));

        Assert.Equal(DesenlaceDelAborto.Abortado, desenlace);
        Assert.Equal(1, s.Entorno.Pagos.Cancelaciones);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
    }

    // ---------- I3 ----------

    [Fact]
    public async Task Si_3b_lanza_se_aborta_cancelando_la_intencion_y_liberando_la_reserva_y_se_responde_servicio_no_disponible()
    {
        var prueba = await Listo();
        await Pagar(prueba);
        var s = await Listo();
        s.Entorno.Marca.LecturaQueLanza = prueba.Entorno.Marca.Lecturas;

        var resultado = await Pagar(s);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.NotNull(s.Checkout.PedidoDescartado);
        Assert.Equal(1, s.Entorno.Pagos.Cancelaciones);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
        Assert.Equal(["falla-en-confirmacion"], s.Entorno.Auditoria.Abortos);
    }

    // ---------- I6 ----------

    [Fact]
    public async Task Todo_aborto_aplicado_se_audita_con_su_motivo_cerrado_y_uno_que_no_aplica_no()
    {
        var s = await Listo();
        await Pagar(s);
        var servicios = s.Entorno.ServiciosDelCheckout();
        await new AbortarElPasoAlPago(servicios).Ejecutar(new SolicitudDeAborto(s.Checkout.CuentaId, EstadoDelCheckout.EnPago, s.Checkout.PedidoId!.Value, 99, true, "otro", null));

        await new AbortarElPasoAlPago(servicios).Ejecutar(new SolicitudDeAborto(s.Checkout.CuentaId, EstadoDelCheckout.EnPago, s.Checkout.PedidoId!.Value, s.Checkout.GeneracionDeReserva, true, "reserva-expirada", null));

        Assert.Equal(["reserva-expirada"], s.Entorno.Auditoria.Abortos);
    }

    [Fact]
    public async Task La_recuperacion_de_un_reservando_atascado_audita_el_motivo_recuperacion()
    {
        var s = await Listo();
        s.Checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(0m), x => x, x => x, s.Entorno.Carritos.Todos.Single(), Inicio);
        s.Entorno.Actor.Establecer(ActorActual.DelSistema());
        s.Entorno.Reloj.Ahora = Inicio.AddMinutes(3);

        await new RecuperarCheckoutsReservando(s.Entorno.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Equal(["recuperacion"], s.Entorno.Auditoria.Abortos);
    }

    // ---------- I4 ----------

    [Fact]
    public async Task ObtenerParaPagar_devuelve_el_pedido_solo_en_en_pago_y_pago_pendiente()
    {
        var s = await Listo();
        var obtener = new ObtenerCheckoutParaPagar(s.Entorno.ServiciosDelCheckout());
        var enEdicion = await obtener.Ejecutar(new ObtenerCheckoutParaPagarEntrada());
        await Pagar(s);

        var enPago = await obtener.Ejecutar(new ObtenerCheckoutParaPagarEntrada());

        Assert.Equal(CategoriaDeError.NoEncontrado, enEdicion.Error!.Categoria);
        Assert.Equal((s.Checkout.PedidoId!.Value, EstadoVisibleDelCheckout.EnPago), (enPago.Valor.PedidoId, enPago.Valor.Estado));
    }

    [Fact]
    public async Task ObtenerParaPagar_no_devuelve_el_pedido_en_en_pago_con_un_retiro_pendiente()
    {
        var s = await Listo();
        await Pagar(s);
        s.Entorno.Carritos.Todos.Single().RetirarEdicion(s.Impreso, diferir: true, Inicio);

        var resultado = await new ObtenerCheckoutParaPagar(s.Entorno.ServiciosDelCheckout()).Ejecutar(new ObtenerCheckoutParaPagarEntrada());

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task ObtenerParaPagar_exige_una_cuenta_y_no_aplica_a_un_visitante()
    {
        var visitante = EntornoDelCarrito.DeVisitante();

        var resultado = await new ObtenerCheckoutParaPagar(visitante.ServiciosDelCheckout()).Ejecutar(new ObtenerCheckoutParaPagarEntrada());

        Assert.False(resultado.EsExito);
    }
}
