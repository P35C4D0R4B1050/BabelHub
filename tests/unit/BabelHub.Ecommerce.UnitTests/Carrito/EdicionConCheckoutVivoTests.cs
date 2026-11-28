using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T11: la edición con el checkout vivo, común al carrito, la entrega, el contacto, las aceptaciones y la fusión (CAR-26, D10, D21): con <c>en_pago</c> llama primero a <c>Reabrir</c> (fuera de la transacción, antes de bloquear)
/// y luego devuelve el checkout a <c>en_edicion</c> conservando el <c>pedidoId</c>; con el pago pendiente o un intento activo rechaza, con <c>reservando</c> responde en curso, y <c>Reabrir</c> cerrado o inexistente aborta desde
/// <c>en_pago</c>. Se prueba por los casos de uso del carrito, que ya la usan.
/// </summary>
public sealed class EdicionConCheckoutVivoTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static (EntornoDelCarrito Entorno, Checkout Checkout, CarritoDeDominio Carrito, Guid Libro) EnPago()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var libro = e.Impreso(10);
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        carrito.Agregar(libro, false, 2, Inicio);
        e.Carritos.Sembrar(carrito);
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, carrito.Id, [new LineaParaCheckout(libro, false, "Libro", "9780000000019", 2, 50m, libro, Sucursal.Principal)], Guid.NewGuid, Inicio);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio);
        checkout.ElegirRecojo(Inicio);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, carrito, Inicio).Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito).Efecto);
        e.Checkouts.Sembrar(checkout);
        return (e, checkout, carrito, libro);
    }

    private static Task<Resultado<ResultadoDeCarrito>> Modificar(EntornoDelCarrito e, Guid libro, int cantidad) =>
        new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(e.LineaDe(libro), cantidad));

    [Fact]
    public async Task Editar_con_el_checkout_en_pago_reabre_la_intencion_antes_de_bloquear_y_deja_el_checkout_en_edicion_con_su_pedido()
    {
        var (e, checkout, carrito, libro) = EnPago();
        var pedido = checkout.PedidoId;
        var generacion = checkout.GeneracionDeReserva;

        var resultado = await Modificar(e, libro, 5);

        Assert.True(resultado.EsExito);
        Assert.Equal(["reabrir", "checkout", "carrito"], e.Bitacora.Bloqueos);
        Assert.Equal(1, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(pedido, checkout.PedidoId);
        Assert.Equal(generacion, checkout.GeneracionDeReserva);
        Assert.Equal(5, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Con_un_intento_activo_Reabrir_responde_no_cancelable_y_se_rechaza_sin_cambiar_nada()
    {
        var (e, checkout, carrito, libro) = EnPago();
        e.Pagos.AlReabrir = ResultadoDeReabrirIntencion.NoCancelable;

        var resultado = await Modificar(e, libro, 5);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error.Categoria);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Equal(2, carrito.Lineas.Single().Cantidad.Valor);
        Assert.Equal(["reabrir"], e.Bitacora.Bloqueos);
    }

    [Theory]
    [InlineData(ResultadoDeReabrirIntencion.Cerrada)]
    [InlineData(ResultadoDeReabrirIntencion.NoExiste)]
    public async Task Si_la_intencion_ya_no_admite_cobro_se_aborta_desde_en_pago_descartando_el_pedido_y_la_edicion_sigue(ResultadoDeReabrirIntencion respuesta)
    {
        var (e, checkout, carrito, libro) = EnPago();
        var pedido = checkout.PedidoId!.Value;
        var generacion = checkout.GeneracionDeReserva;
        e.Pagos.AlReabrir = respuesta;

        var resultado = await Modificar(e, libro, 5);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Null(checkout.PedidoId);
        Assert.Equal(pedido, checkout.PedidoDescartado);
        Assert.Equal(generacion + 1, checkout.GeneracionDeReserva);
        Assert.NotNull(checkout.ReservaALiberarDesde);
        Assert.Equal(5, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Si_Pagos_no_responde_a_Reabrir_se_falla_cerrado_y_no_se_edita()
    {
        var (e, checkout, carrito, libro) = EnPago();
        e.Pagos.NoResponde = true;

        var resultado = await Modificar(e, libro, 5);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Equal(2, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Con_el_pago_pendiente_no_se_llama_a_Pagos_y_se_rechaza()
    {
        var (e, checkout, _, libro) = EnPago();
        checkout.PagoPendiente(checkout.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);

        var resultado = await Modificar(e, libro, 5);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, resultado.Error!.Clave);
        Assert.Equal(0, e.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Con_el_checkout_en_edicion_no_se_llama_a_Pagos()
    {
        var (e, checkout, _, libro) = EnPago();
        checkout.Editar(e.Carritos.Todos.Single(), Inicio);
        var antes = e.Pagos.Reaperturas;

        var resultado = await Modificar(e, libro, 5);

        Assert.True(resultado.EsExito);
        Assert.Equal(antes, e.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Un_visitante_nunca_llama_a_Pagos()
    {
        var e = EntornoDelCarrito.DeVisitante();

        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Impreso(5), 1, CodigoDeSucursal: Sucursal.Principal));

        Assert.Equal(0, e.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Si_otra_solicitud_ya_lo_devolvio_a_edicion_entre_Reabrir_y_el_bloqueo_esta_edita_con_normalidad()
    {
        var (e, checkout, carrito, libro) = EnPago();
        e.Pagos.EnLaCarreraDeReabrir = () => checkout.Editar(carrito, Inicio);

        var resultado = await Modificar(e, libro, 5);

        // Otra solicitud ya lo devolvió a edición: esta no necesita reabrir y edita con normalidad (sin doble edición ni estado atascado).
        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
    }

    [Fact]
    public async Task Si_el_checkout_cambia_a_otro_estado_vivo_entre_Reabrir_y_el_bloqueo_responde_en_curso()
    {
        var (e, checkout, carrito, libro) = EnPago();
        e.Pagos.EnLaCarreraDeReabrir = () =>
        {
            checkout.PagoPendiente(checkout.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);
        };

        var resultado = await Modificar(e, libro, 5);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, resultado.Error!.Clave);
        Assert.Equal(2, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Reabrir_con_una_linea_con_retiro_pendiente_aplica_el_retiro_y_deja_el_aviso()
    {
        var (e, checkout, carrito, libro) = EnPago();
        var otro = e.Impreso(5, 10m, "Retirado");
        carrito.Agregar(otro, false, 1, Inicio);
        carrito.RetirarEdicion(otro, diferir: true, Inicio);

        var resultado = await Modificar(e, libro, 3);

        Assert.True(resultado.EsExito);
        Assert.Equal([libro], carrito.Lineas.Select(l => l.EdicionId));
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        var aviso = Assert.Single(resultado.Valor.Carrito.Avisos);
        Assert.Equal(otro, aviso.EdicionId);
    }

    [Fact]
    public async Task Quitar_una_linea_tambien_reabre_la_intencion()
    {
        var (e, checkout, carrito, libro) = EnPago();

        var resultado = await new EliminarLinea(e.Servicios()).Ejecutar(new EliminarLineaEntrada(e.LineaDe(libro)));

        Assert.True(resultado.EsExito);
        Assert.Equal(1, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Empty(carrito.Lineas);
    }
}
