using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// Las correcciones de la revisión de seguridad y arquitectura de ECO-4a: los avisos de Pagos son condicionales al <c>pedidoId</c>, un checkout abandonado se termina a los 30 días, el carrito que se pasa debe ser el del checkout, el código
/// de sucursal se valida como el resto del texto de la entrega y los caracteres de formato no entran en los datos de la guía del operador.
/// </summary>
public sealed class CorreccionesDeLaRevisionDeEco4aTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CarritoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Func<string, string> Identidad = s => s;
    private static readonly CostoDeEnvio Envio = CostoDeEnvio.Crear(12.00m);

    private static Guid Edicion(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static LineaParaCheckout Impreso(int n) => new(Edicion(n), false, $"Libro {n}", "9780000000019", 1, 50m);

    private static LineaParaCheckout Digital(int n) => new(Edicion(n), true, $"Digital {n}", "9780000000026", 1, 20m);

    private static ContactoDeEntrega Contacto() =>
        ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez"), TelefonoDeContacto.Crear("987654321"), DireccionDeEntrega.Crear("Jr. Lima 123", "Ayacucho"));

    private static CarritoDeDominio Carrito(Guid? id = null, Guid? cuenta = null)
    {
        var carrito = CarritoDeDominio.Crear(id ?? CarritoId, PropietarioDelCarrito.DeCuenta(cuenta ?? Cuenta), Ahora);
        carrito.Agregar(Edicion(1), esDigital: false, cantidad: 1, Ahora);
        carrito.Agregar(Edicion(2), esDigital: true, cantidad: 1, Ahora);
        return carrito;
    }

    private static Checkout ADomicilio()
    {
        var checkout = Checkout.Iniciar(Guid.NewGuid(), Cuenta, CarritoId, [Impreso(1), Digital(2)], Guid.NewGuid, Ahora);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
        checkout.ElegirDomicilio(Contacto(), Ahora);
        return checkout;
    }

    private static Checkout Reservando(CarritoDeDominio carrito)
    {
        var checkout = ADomicilio();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, carrito, Ahora);
        return checkout;
    }

    private static Checkout EnPago(CarritoDeDominio carrito)
    {
        var checkout = Reservando(carrito);
        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Ahora.AddMinutes(15), carrito);
        return checkout;
    }

    // ---------------------------------------------------------------- avisos de Pagos de otro pedido

    [Fact]
    public void Un_aviso_rezagado_de_un_pedido_descartado_no_toca_el_checkout_nuevo()
    {
        var carrito = Carrito();
        var checkout = EnPago(carrito);
        var viejo = checkout.PedidoId!.Value;
        checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, viejo, checkout.GeneracionDeReserva, carrito, Ahora.AddMinutes(1));
        checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, carrito, Ahora.AddMinutes(2));
        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Ahora.AddMinutes(17), carrito);
        Assert.NotEqual(viejo, checkout.PedidoId);

        var aprobado = checkout.PagoAprobado(viejo, carrito, Ahora.AddMinutes(3));
        var pendiente = checkout.PagoPendiente(viejo, Ahora.AddMinutes(33), Ahora.AddMinutes(32), Ahora.AddMinutes(3));
        var resuelto = checkout.PagoResuelto(viejo, MotivoDePagoResuelto.Compensado, carrito, Ahora.AddMinutes(3));

        Assert.All([aprobado, pendiente, resuelto], r => Assert.Equal(EfectoDeTransicion.SinCambios, r.Efecto));
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.NotNull(checkout.Contacto);
    }

    [Fact]
    public void Un_aviso_de_un_pedido_que_no_es_el_del_checkout_ni_su_ultimo_descartado_no_aplica()
    {
        var carrito = Carrito();
        var checkout = EnPago(carrito);
        var ajeno = Guid.NewGuid();

        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.PagoAprobado(ajeno, carrito, Ahora).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.PagoPendiente(ajeno, Ahora.AddMinutes(30), Ahora.AddMinutes(30), Ahora).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.PagoResuelto(ajeno, MotivoDePagoResuelto.Rechazado, carrito, Ahora).Efecto);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.NotNull(checkout.Contacto);
    }

    [Fact]
    public void Un_aviso_del_pedido_de_un_checkout_sin_pedido_aun_no_aplica()
    {
        var checkout = ADomicilio();

        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.PagoAprobado(Guid.NewGuid(), Carrito(), Ahora).Efecto);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
    }

    // ---------------------------------------------------------------- abandono a los 30 días

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Criterio", "AC-CAR-30")]
    public void Un_checkout_abandonado_en_pago_o_reservando_a_los_30_dias_se_cancela_sin_contacto_y_anota_la_reserva(bool enPago)
    {
        var carrito = Carrito();
        var checkout = enPago ? EnPago(carrito) : Reservando(carrito);
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var antes = checkout.CancelarPorVencimiento(carrito, Ahora.AddDays(29).AddHours(23));
        var despues = checkout.CancelarPorVencimiento(carrito, Ahora.AddDays(30));

        Assert.Equal(EfectoDeTransicion.NoAplica, antes.Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, despues.Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, checkout.Estado);
        Assert.Null(checkout.Contacto);
        Assert.Null(checkout.HuellaDeInstantanea);
        Assert.Equal(Ahora.AddDays(30), checkout.ReservaALiberarDesde);
        Assert.Equal([new EdicionRetirada(Edicion(2), true)], despues.Retiradas);
        Assert.True(checkout.CumpleLaRetencionDelContacto);
    }

    [Fact]
    public void Cancelar_por_vencimiento_no_toca_un_checkout_en_edicion_ni_uno_con_el_pago_pendiente()
    {
        var carrito = Carrito();
        var enEdicion = ADomicilio();
        var pendiente = EnPago(carrito);
        pendiente.PagoPendiente(pendiente.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.NoAplica, enEdicion.CancelarPorVencimiento(carrito, Ahora.AddDays(40)).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, pendiente.CancelarPorVencimiento(carrito, Ahora.AddDays(40)).Efecto);
        Assert.Equal(EstadoDelCheckout.EnEdicion, enEdicion.Estado);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, pendiente.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Un_contacto_vencido_con_el_pago_pendiente_se_elimina_sin_cambiar_el_estado()
    {
        var carrito = Carrito();
        var checkout = EnPago(carrito);
        checkout.PagoPendiente(checkout.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1));

        var resultado = checkout.EliminarContacto(MotivoDeEliminacionDeContacto.Vencido, Ahora.AddDays(30));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, checkout.Estado);
        Assert.Null(checkout.Contacto);
        Assert.Null(checkout.HuellaDeInstantanea);
    }

    // ---------------------------------------------------------------- motivos de eliminación y carrito propio

    [Theory]
    [InlineData(MotivoDeEliminacionDeContacto.PagoAprobado)]
    [InlineData(MotivoDeEliminacionDeContacto.Cancelar)]
    [InlineData(MotivoDeEliminacionDeContacto.Reiniciar)]
    [InlineData(MotivoDeEliminacionDeContacto.CambioARecojo)]
    [InlineData(MotivoDeEliminacionDeContacto.CambioASoloDigital)]
    public void Los_motivos_que_aplica_la_propia_transicion_no_se_pueden_pedir_a_eliminar_contacto(MotivoDeEliminacionDeContacto motivo)
    {
        var checkout = EnPago(Carrito());

        Assert.Throws<ArgumentException>(() => checkout.EliminarContacto(motivo, Ahora));
        Assert.NotNull(checkout.Contacto);
    }

    [Fact]
    public void Un_carrito_de_otro_checkout_o_de_otra_cuenta_se_rechaza_en_toda_transicion_que_lo_toca()
    {
        var ajeno = Carrito(id: Guid.NewGuid());
        var deOtraCuenta = Carrito(cuenta: Guid.NewGuid());
        var propio = Carrito();
        var checkout = EnPago(propio);
        var pedido = checkout.PedidoId!.Value;

        foreach (var carrito in new[] { ajeno, deOtraCuenta })
        {
            Assert.Throws<ArgumentException>(() => checkout.Editar(carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.Volver(carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.Reiniciar(carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.VolverAEnEdicionTrasReabrir(carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, pedido, checkout.GeneracionDeReserva, carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.PagoAprobado(pedido, carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.PagoResuelto(pedido, MotivoDePagoResuelto.Compensado, carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.CancelarPorVencimiento(carrito, Ahora));
            Assert.Throws<ArgumentException>(() => checkout.ConfirmarEnPago(pedido, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Ahora, carrito));
            Assert.Throws<ArgumentException>(() => checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, carrito, Ahora));
        }

        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Contains(ajeno.Lineas, l => l.EdicionId == Edicion(2));
    }

    // ---------------------------------------------------------------- texto de la entrega

    [Theory]
    [InlineData("lima", "lima")]
    [InlineData("  lima  ", "lima")]
    public void El_codigo_de_la_sucursal_de_recojo_se_recorta_de_espacios(string texto, string esperado)
    {
        var checkout = ADomicilio();

        checkout.ElegirRecojo(texto, Ahora.AddMinutes(1));

        Assert.Equal(esperado, checkout.CodigoDeSucursalDeRecojo);
    }

    [Theory]
    [InlineData("li\nma")]
    [InlineData("lima\n")]
    [InlineData("li\tma")]
    [InlineData("   ")]
    public void Un_codigo_de_sucursal_con_caracteres_de_control_o_vacio_se_rechaza(string texto) =>
        Assert.Throws<ArgumentException>(() => ADomicilio().ElegirRecojo(texto, Ahora));

    [Fact]
    public void Un_codigo_de_sucursal_de_mas_de_40_caracteres_se_rechaza_y_uno_de_40_se_acepta()
    {
        Assert.Throws<ArgumentException>(() => ADomicilio().ElegirRecojo(new string('s', 41), Ahora));
        var checkout = ADomicilio();
        checkout.ElegirRecojo(new string('s', 40), Ahora);
        Assert.Equal(40, checkout.CodigoDeSucursalDeRecojo!.Length);
    }

    [Theory]
    [InlineData(0x200B)]
    [InlineData(0x200D)]
    [InlineData(0xFEFF)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0x00A0)]
    [InlineData(0x2003)]
    [Trait("Criterio", "AC-CAR-26")]
    public void Los_caracteres_de_formato_y_los_espacios_no_estandar_no_entran_en_el_nombre_ni_en_la_direccion(int puntoDeCodigo)
    {
        var raro = char.ConvertFromUtf32(puntoDeCodigo);

        Assert.False(NombreDeContacto.TryCrear("Ana" + raro + "Pérez", out _));
        Assert.False(DireccionDeEntrega.TryCrear("Jr. Lima" + raro + "123", "Ayacucho", out _));
        Assert.False(DireccionDeEntrega.TryCrear("Jr. Lima 123", "Aya" + raro + "cucho", out _));
    }

    [Fact]
    public void Un_telefono_de_mas_de_40_caracteres_se_rechaza_sin_recorrerlo()
    {
        Assert.False(TelefonoDeContacto.TryCrear(new string(' ', 5000) + "987654321", out _));
        Assert.False(TelefonoDeContacto.TryCrear("987654321" + new string('-', 41), out _));
    }
}
