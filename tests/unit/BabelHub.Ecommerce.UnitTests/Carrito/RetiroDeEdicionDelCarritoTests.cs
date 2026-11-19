using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T13: <c>RetirarEdicionDelCarrito</c> (CAT-18, CAR-29, D10, D21): lo invoca Catálogo dentro de su transacción con <b>todas</b> las ediciones de la baja; solo toca la base de datos (nunca llama a Pagos), bloquea los checkouts
/// y después los carritos por <c>id</c> ascendente, quita la línea (y deja el aviso) cuando no hay un checkout con el cobro en marcha y, si lo hay (<c>reservando</c>, <c>en_pago</c> o <c>pago_pendiente</c>), solo la anota con
/// <c>retiro_pendiente</c>.
/// </summary>
public sealed class RetiroDeEdicionDelCarritoTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static CarritoDeDominio CarritoDe(EntornoDelCarrito e, Guid? cuenta, params Guid[] ediciones)
    {
        var propietario = cuenta is { } c ? PropietarioDelCarrito.DeCuenta(c) : PropietarioDelCarrito.DeVisitante(Huella());
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), propietario, Inicio);
        foreach (var edicion in ediciones)
        {
            carrito.Agregar(edicion, false, 1, Inicio);
        }

        e.Carritos.Sembrar(carrito);
        return carrito;
    }

    private static byte[] Huella() => System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString()));

    private static Checkout CheckoutDe(EntornoDelCarrito e, Guid cuenta, CarritoDeDominio carrito, Action<Checkout, CarritoDeDominio>? llevar = null)
    {
        var edicion = carrito.Lineas.First().EdicionId;
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, carrito.Id, [new LineaParaCheckout(edicion, false, "Libro", "9780000000019", 1, 50m)], Guid.NewGuid, Inicio);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio);
        checkout.ElegirRecojo("lima", Inicio);
        llevar?.Invoke(checkout, carrito);
        e.Checkouts.Sembrar(checkout);
        return checkout;
    }

    private static void AReservando(Checkout c, CarritoDeDominio k) =>
        Assert.Equal(EfectoDeTransicion.Aplicada, c.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, k, Inicio).Efecto);

    private static void AEnPago(Checkout c, CarritoDeDominio k)
    {
        AReservando(c, k);
        Assert.Equal(EfectoDeTransicion.Aplicada, c.ConfirmarEnPago(c.PedidoId!.Value, c.GeneracionDeReserva, c.HuellaDeInstantanea!, Inicio.AddMinutes(15), k).Efecto);
    }

    private static void APagoPendiente(Checkout c, CarritoDeDominio k)
    {
        AEnPago(c, k);
        Assert.Equal(EfectoDeTransicion.Aplicada, c.PagoPendiente(c.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio).Efecto);
    }

    private static Task Retirar(EntornoDelCarrito e, params Guid[] ediciones) => new RetirarEdicionDelCarrito(e.Servicios()).Retirar(ediciones);

    [Fact]
    public async Task Sin_checkout_se_quita_la_linea_de_cada_carrito_y_la_cuenta_ve_su_aviso()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5, titulo: "Se retira");
        var otra = e.Impreso(5);
        var cuenta = Guid.NewGuid();
        var deCuenta = CarritoDe(e, cuenta, retirada, otra);
        var deVisitante = CarritoDe(e, null, retirada);

        await Retirar(e, retirada);

        Assert.Equal([otra], deCuenta.Lineas.Select(l => l.EdicionId));
        Assert.Empty(deVisitante.Lineas);
        var aviso = Assert.Single(await e.Carritos.AvisosSinVer(cuenta));
        Assert.Equal((TipoDeAvisoDelCarrito.EdicionRetirada, retirada, "Se retira"), (aviso.Tipo, aviso.EdicionId, aviso.Titulo));
    }

    [Fact]
    public async Task Con_el_checkout_en_edicion_tambien_se_quita_y_se_deja_el_aviso()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDe(e, cuenta, retirada);
        CheckoutDe(e, cuenta, carrito);

        await Retirar(e, retirada);

        Assert.Empty(carrito.Lineas);
        Assert.Single(await e.Carritos.AvisosSinVer(cuenta));
    }

    [Fact]
    public async Task Un_checkout_ya_terminado_no_cuenta_como_vivo_y_la_linea_se_quita()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDe(e, cuenta, retirada);
        var checkout = CheckoutDe(e, cuenta, carrito);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.Volver(carrito, Inicio).Efecto);

        await Retirar(e, retirada);

        Assert.Empty(carrito.Lineas);
    }

    [Theory]
    [InlineData("reservando")]
    [InlineData("en_pago")]
    [InlineData("pago_pendiente")]
    public async Task Con_el_cobro_en_marcha_solo_se_anota_el_retiro_pendiente_sin_llamar_a_Pagos(string estado)
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDe(e, cuenta, retirada);
        var checkout = CheckoutDe(e, cuenta, carrito, estado switch
        {
            "reservando" => AReservando,
            "en_pago" => AEnPago,
            _ => APagoPendiente,
        });
        var antes = checkout.Estado;

        await Retirar(e, retirada);

        var linea = Assert.Single(carrito.Lineas);
        Assert.True(linea.RetiroPendiente);
        Assert.Equal(antes, checkout.Estado);
        Assert.Empty(await e.Carritos.AvisosSinVer(cuenta));
        Assert.Equal((0, 0, 0), (e.Pagos.Reaperturas, e.Pagos.Cancelaciones, e.Pagos.Creaciones));
    }

    [Fact]
    public async Task La_baja_de_una_obra_pasa_todas_sus_ediciones_en_una_sola_llamada()
    {
        var e = EntornoDelCarrito.DeCliente();
        var impresa = e.Impreso(5);
        var digital = e.Digital();
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDe(e, cuenta, impresa, digital);

        await Retirar(e, impresa, digital);

        Assert.Empty(carrito.Lineas);
        Assert.Equal(2, (await e.Carritos.AvisosSinVer(cuenta)).Count);
        Assert.Equal([(2, 1, 2, 0)], e.Auditoria.Retiros);
    }

    [Fact]
    public async Task Los_carritos_que_no_tienen_la_edicion_no_se_tocan_ni_se_bloquean()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var otra = e.Impreso(5);
        var ajeno = CarritoDe(e, Guid.NewGuid(), otra);

        await Retirar(e, retirada);

        Assert.Single(ajeno.Lineas);
        Assert.Empty(e.Bitacora.Bloqueos);
        Assert.Equal([(1, 0, 0, 0)], e.Auditoria.Retiros);
    }

    [Fact]
    public async Task Bloquea_primero_los_checkouts_por_id_y_despues_los_carritos_por_id()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var carritos = new List<CarritoDeDominio>();
        var checkouts = new List<Checkout>();
        for (var i = 0; i < 4; i++)
        {
            var cuenta = Guid.NewGuid();
            var carrito = CarritoDe(e, cuenta, retirada);
            carritos.Add(carrito);
            checkouts.Add(CheckoutDe(e, cuenta, carrito));
        }

        var visitante = CarritoDe(e, null, retirada);

        await Retirar(e, retirada);

        var esperados = checkouts.Select(c => $"checkout:{c.Id}").Order().Concat(carritos.Append(visitante).Select(c => $"carrito:{c.Id}").Order());
        Assert.Equal(esperados, e.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task Repetir_el_retiro_no_duplica_avisos_ni_cambia_nada()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var cuenta = Guid.NewGuid();
        CarritoDe(e, cuenta, retirada);

        await Retirar(e, retirada);
        await Retirar(e, retirada);

        Assert.Single(await e.Carritos.AvisosSinVer(cuenta));
    }

    [Fact]
    public async Task Si_el_catalogo_no_responde_el_retiro_igual_se_aplica_con_un_titulo_generico()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDe(e, cuenta, retirada);
        e.Catalogo.Caido = true;

        await Retirar(e, retirada);

        Assert.Empty(carrito.Lineas);
        Assert.Equal("Edición retirada", Assert.Single(await e.Carritos.AvisosSinVer(cuenta)).Titulo);
    }

    [Fact]
    public async Task Si_no_se_puede_auditar_el_retiro_falla_para_que_la_baja_no_se_aplique()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = e.Impreso(5);
        CarritoDe(e, Guid.NewGuid(), retirada);
        e.Auditoria.Caida = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Retirar(e, retirada));
    }

    [Fact]
    public async Task Sin_ediciones_no_hace_nada()
    {
        var e = EntornoDelCarrito.DeCliente();

        await Retirar(e);

        Assert.Empty(e.Auditoria.Retiros);
    }
}
