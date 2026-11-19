using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// Corrección de la revisión de ECO-4c (I-1): la reserva por liberar se direcciona por un <c>pedido_a_liberar</c> explícito, de modo que el reintento de una liberación fallida nunca libere la reserva viva del pedido nuevo del mismo checkout.
/// </summary>
public sealed class LiberacionPendienteTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout, Domain.Carrito.Carrito Carrito)> ConCheckoutListo()
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Impreso(10, 40m), 3));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null, new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho")));
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        return (e, e.Checkouts.Todos.Single(), e.Carritos.Todos.Single());
    }

    [Fact]
    public async Task Tras_un_aborto_con_un_pedido_nuevo_vivo_la_liberacion_pendiente_sigue_apuntando_al_pedido_abortado()
    {
        var (e, checkout, carrito) = await ConCheckoutListo();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        checkout.PrepararPasoAlPago(p1, CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, p1, checkout.GeneracionDeReserva, carrito, Inicio);

        checkout.PrepararPasoAlPago(p2, CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio.AddMinutes(1));
        checkout.ConfirmarEnPago(p2, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(16), carrito);

        Assert.Equal(p1, checkout.PedidoALiberar);
        Assert.Equal(p2, checkout.PedidoId);
        var pendientes = await e.Checkouts.ConLiberacionPendiente(10);
        Assert.Equal(p1, Assert.Single(pendientes).PedidoId);
    }

    [Fact]
    public async Task El_reintento_libera_solo_el_pedido_abortado_y_no_la_reserva_viva_del_nuevo()
    {
        var (e, checkout, carrito) = await ConCheckoutListo();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        checkout.PrepararPasoAlPago(p1, CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, p1, checkout.GeneracionDeReserva, carrito, Inicio);
        checkout.PrepararPasoAlPago(p2, CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio.AddMinutes(1));
        checkout.ConfirmarEnPago(p2, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(16), carrito);
        e.Actor.Establecer(ActorActual.DelSistema());

        await new LiberarReservasPendientes(e.ServiciosDelCheckout()).Ejecutar(new LiberarReservasPendientesEntrada());

        Assert.Equal(p1, Assert.Single(e.Inventario.Liberaciones).PedidoId);
        Assert.Null(checkout.ReservaALiberarDesde);
        Assert.Null(checkout.PedidoALiberar);
        Assert.Equal(p2, checkout.PedidoId);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
    }

    [Fact]
    public async Task Volver_con_un_pedido_vivo_anota_ese_pedido_y_confirmar_la_liberacion_limpia_la_anotacion()
    {
        var (_, checkout, carrito) = await ConCheckoutListo();
        var p = Guid.NewGuid();
        checkout.PrepararPasoAlPago(p, CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        checkout.ConfirmarEnPago(p, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);

        checkout.Volver(carrito, Inicio.AddMinutes(2));

        Assert.Equal(p, checkout.PedidoALiberar);
        checkout.LiberacionConfirmada();
        Assert.Null(checkout.PedidoALiberar);
        Assert.Null(checkout.ReservaALiberarDesde);
    }
}
