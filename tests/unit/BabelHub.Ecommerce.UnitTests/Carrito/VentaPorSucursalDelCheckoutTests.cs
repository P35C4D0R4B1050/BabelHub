using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;
using TextoLegal = BabelHub.Ecommerce.Domain.Carrito.TextoLegal;
using TipoDeEntrega = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T40 y T42 (venta por sucursal): el checkout por línea y sucursal (CAR-08, CAR-09), el envío como suma por sucursal leído una vez al congelar (CAR-23, CAR-46, CAR-49), el rechazo de la reserva sin reasignar (CAR-27, CAR-41) y las
/// compras anteriores a la venta por sucursal (CAR-45, CAR-50).
/// </summary>
public sealed class VentaPorSucursalDelCheckoutTests
{
    private static readonly DateTimeOffset Ahora = EntornoDelCarrito.Inicio;

    private static readonly Guid Cuenta = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid Libro = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly IReadOnlyList<TextoVistoDeCompra> Textos = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private static ContactoDeEntrega Contacto() =>
        ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez"), TelefonoDeContacto.Crear("987654321"), DireccionDeEntrega.Crear("Jr. Lima 123", "ayacucho"));

    private static ContactoDeCompra ContactoDeLaWeb() => new("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho");

    private static LineaParaCheckout Impreso(Guid linea, string? sucursal, int cantidad = 1, Guid? edicion = null) =>
        new(edicion ?? Libro, false, "Libro", "9780000000019", cantidad, 50m, linea, sucursal);

    private static Checkout ConTextos(Checkout checkout)
    {
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        return checkout;
    }

    private static CarritoDeDominio CarritoPropio(Guid id) => CarritoDeDominio.Crear(id, PropietarioDelCarrito.DeCuenta(Cuenta), Ahora);

    // ---------- Dominio ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-21")]
    public void El_envio_es_una_vez_por_cada_sucursal_que_atiende_y_la_suma_se_congela()
    {
        var checkout = ConTextos(Checkout.Iniciar(Guid.NewGuid(), Cuenta, Guid.NewGuid(), [Impreso(Guid.NewGuid(), "ayacucho"), Impreso(Guid.NewGuid(), "ayacucho", 2), Impreso(Guid.NewGuid(), "cusco")], Guid.NewGuid, Ahora));
        checkout.ElegirDomicilio(Contacto(), Ahora);

        var desglose = EnvioPorSucursales.Desglose(checkout.SucursalesQueAtienden, new Dictionary<string, CostoDeEnvio> { ["cusco"] = CostoDeEnvio.Crear(18.50m) }, CostoDeEnvio.Crear(30m));
        var total = checkout.CalcularTotal(EnvioPorSucursales.Suma(desglose));

        Assert.Equal([("ayacucho", 30m), ("cusco", 18.50m)], desglose.Select(d => (d.CodigoDeSucursal, d.Costo.Valor)));
        Assert.Equal((200m, 48.50m, 248.50m), (total.Subtotal, total.Envio, total.Total));
    }

    [Fact]
    public void Dos_lineas_del_mismo_libro_conviven_y_cada_una_conserva_su_lineaid_al_sincronizar()
    {
        var (una, otra) = (Guid.NewGuid(), Guid.NewGuid());
        var checkout = Checkout.Iniciar(Guid.NewGuid(), Cuenta, Guid.NewGuid(), [Impreso(una, "ayacucho", 18), Impreso(otra, "cusco", 6)], Guid.NewGuid, Ahora);
        var antes = checkout.Lineas.ToDictionary(l => l.LineaDeCarritoId!.Value, l => l.LineaId);

        checkout.SincronizarLineas([Impreso(otra, "cusco", 7), Impreso(una, "ayacucho", 18)], Guid.NewGuid, Ahora);

        Assert.Equal([antes[otra], antes[una]], checkout.Lineas.Select(l => l.LineaId));
        Assert.Equal(["cusco", "ayacucho"], checkout.Lineas.Select(l => l.CodigoDeSucursal));
        Assert.Throws<ArgumentException>(() => Checkout.Iniciar(Guid.NewGuid(), Cuenta, Guid.NewGuid(), [Impreso(una, "ayacucho"), Impreso(una, "ayacucho")], Guid.NewGuid, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public void Con_un_impreso_sin_sucursal_el_checkout_no_avanza_ni_se_paga_salvo_el_pago_ya_pendiente()
    {
        var carrito = Guid.NewGuid();
        var checkout = ConTextos(Checkout.Iniciar(Guid.NewGuid(), Cuenta, carrito, [Impreso(Guid.NewGuid(), null)], Guid.NewGuid, Ahora));
        ResultadoDeCheckout Pagar() => checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, CarritoPropio(carrito), Ahora);

        Assert.Equal(RechazoDeCheckout.LineaSinSucursal, checkout.ConfirmarRevision(Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.LineaSinSucursal, checkout.ElegirRecojo(Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.LineaSinSucursal, checkout.ElegirDomicilio(Contacto(), Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.LineaSinSucursal, Pagar().Rechazo);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public void La_huella_de_una_compra_anterior_no_cambia_porque_la_sucursal_solo_entra_cuando_existe()
    {
        var linea = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var edicion = Guid.Parse("00000002-0000-0000-0000-000000000001");
        var carrito = Guid.NewGuid();
        var digital = new LineaParaCheckout(edicion, true, "Digital 2", "9780000000026", 1, 20m);
        var checkout = ConTextos(Checkout.Iniciar(Guid.NewGuid(), Cuenta, carrito, [digital], () => linea, Ahora));
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
        string? reserva = null;
        string? instantanea = null;

        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => reserva = s, s => instantanea = s, CarritoPropio(carrito), Ahora);

        // Los textos canónicos de antes de la venta por sucursal, fijados: cualquier cambio invalidaría las intenciones de pago ya creadas (CAR-50).
        Assert.Equal("reserva-v1|7:Digital|~|~", reserva);
        Assert.Equal(
            $"instantanea-v1|7:Digital|~|~|~|~|~|3:t-1|3:p-1|3:r-1|4:0.00|5:20.00|32:{linea:N}|32:{edicion:N}|7:digital|9:Digital 2|13:9780000000026|1:1|5:20.00|~|~|~",
            instantanea);
    }

    [Fact]
    public void La_huella_de_reserva_distingue_la_sucursal_de_cada_linea()
    {
        var (linea, deCarrito) = (Guid.NewGuid(), Guid.NewGuid());
        string Huella(string sucursal)
        {
            var carrito = Guid.NewGuid();
            var checkout = ConTextos(Checkout.Iniciar(Guid.NewGuid(), Cuenta, carrito, [Impreso(deCarrito, sucursal)], () => linea, Ahora));
            checkout.ElegirRecojo(Ahora);
            checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, CarritoPropio(carrito), Ahora);
            return checkout.HuellaDeReserva!;
        }

        Assert.NotEqual(Huella("ayacucho"), Huella("cusco"));
        Assert.Equal(Huella("ayacucho"), Huella("ayacucho"));
    }

    // ---------- Casos de uso ----------

    /// <summary>Un checkout en revisión con dos líneas del libro en la sede central y una en Cusco, y el envío predeterminado de S/ 30,00.</summary>
    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout, CarritoDeDominio Carrito, Guid Libro)> ConDosSucursales()
    {
        var e = EntornoDelCarrito.DeCliente();
        e.Costo.Valor = CostoDeEnvio.Crear(30m);
        var libro = e.Impreso(25, 40m);
        e.Inventario.FijarStock(libro, 10, Sucursal.Cusco);
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(e.Actor.Actual!.UsuarioId!.Value), Ahora);
        carrito.Agregar(Guid.NewGuid(), libro, false, Sucursal.Principal, 20, Ahora);
        carrito.Agregar(Guid.NewGuid(), libro, false, Sucursal.Principal, 2, Ahora.AddSeconds(1));
        carrito.Agregar(Guid.NewGuid(), libro, false, Sucursal.Cusco, 3, Ahora.AddSeconds(2));
        e.Carritos.Sembrar(carrito);
        var inicio = await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        Assert.True(inicio.EsExito, inicio.Error?.Clave);
        return (e, e.Checkouts.Todos.Single(), carrito, libro);
    }

    private static async Task<VistaDelCheckout> Elegir(EntornoDelCarrito e, TipoDeEntregaDeCompra tipo)
    {
        var elegida = await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(tipo, tipo == TipoDeEntregaDeCompra.Domicilio ? ContactoDeLaWeb() : null));
        Assert.True(elegida.EsExito, elegida.Error?.Clave);
        return elegida.Valor;
    }

    private static Task<Resultado<PasoAlPagoPreparado>> Pagar(EntornoDelCarrito e, decimal total) =>
        new PrepararPasoAlPago(e.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(total, Textos));

    [Fact]
    [Trait("Criterio", "AC-CAR-08")]
    public async Task A_domicilio_se_ve_el_costo_de_cada_sucursal_y_su_suma_y_con_recojo_es_gratis_en_la_sucursal_de_cada_linea()
    {
        var (e, checkout, _, _) = await ConDosSucursales();

        var domicilio = await Elegir(e, TipoDeEntregaDeCompra.Domicilio);
        var recojo = await Elegir(e, TipoDeEntregaDeCompra.Recojo);

        Assert.Equal([("cusco", "Cusco", 30m), (Sucursal.Principal, "Sede central", 30m)], domicilio.DesgloseDelEnvio!.Select(d => (d.CodigoDeSucursal, d.NombreDeSucursal!, d.Importe)));
        Assert.Equal((60m, 1000m + 60m), (domicilio.Envio, domicilio.Total));
        Assert.Equal(((decimal?)null, 1000m), (recojo.Envio, recojo.Total));
        Assert.Null(recojo.DesgloseDelEnvio);
        Assert.Null(recojo.CodigoDeSucursalDeRecojo);
        Assert.Equal(["Sede central", "Sede central", "Cusco"], recojo.Lineas.Select(l => l.NombreDeSucursal));
        Assert.Null(checkout.EnvioCongelado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-42")]
    public async Task Si_cambia_un_costo_antes_de_pagar_no_se_crea_nada_se_devuelve_el_total_nuevo_y_se_puede_confirmar()
    {
        var (e, checkout, _, _) = await ConDosSucursales();
        var mostrado = (await Elegir(e, TipoDeEntregaDeCompra.Domicilio)).Total;
        e.Tarifas.Propias[Sucursal.Cusco] = CostoDeEnvio.Crear(18.50m);

        var anterior = await Pagar(e, mostrado);

        Assert.Equal(ClavesDelCarrito.TotalCambio, anterior.Error!.Clave);
        Assert.Equal("1048.50", Assert.Single(anterior.Error.Errores).Clave);
        Assert.Equal((EstadoDelCheckout.EnEdicion, (Guid?)null), (checkout.Estado, checkout.PedidoId));
        Assert.Empty(e.Inventario.Reservas);
        Assert.Equal(0, e.Pagos.Creaciones);

        var lecturas = e.Tarifas.Lecturas;
        var confirmado = await Pagar(e, 1048.50m);

        Assert.True(confirmado.EsExito, confirmado.Error?.Clave);
        Assert.Equal(lecturas + 1, e.Tarifas.Lecturas);
        Assert.Equal((48.50m, 1048.50m), (checkout.EnvioCongelado, checkout.TotalCongelado));
        Assert.Equal((48.50m, (string?)null), (confirmado.Valor.Instantanea!.Envio, confirmado.Valor.Instantanea.CodigoDeSucursalDeRecojo));
        Assert.Equal([Sucursal.Principal, Sucursal.Principal, Sucursal.Cusco], confirmado.Valor.LineasImpresas.Select(l => l.CodigoDeSucursal));

        // Congelado el total, otro cambio de costo no lo mueve.
        e.Tarifas.Propias[Sucursal.Cusco] = CostoDeEnvio.Crear(500m);
        Assert.True((await Pagar(e, 1048.50m)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-24")]
    public async Task Una_reserva_rechazada_indica_la_linea_la_sucursal_y_lo_disponible_sin_cambiar_la_linea()
    {
        var (e, checkout, carrito, libro) = await ConDosSucursales();
        await Elegir(e, TipoDeEntregaDeCompra.Recojo);
        var preparado = (await Pagar(e, 1000m)).Valor;
        var deCusco = preparado.LineasImpresas.Single(l => l.CodigoDeSucursal == Sucursal.Cusco);
        e.Inventario.AlReservar = ResultadoDeReservar.Sin([new LineaSinStock(deCusco.LineaId, libro, 3, 100, 2)]);

        var resultado = await new ReservarImpresos(e.ServiciosDelCheckout()).Ejecutar(preparado);

        Assert.Equal(TipoDeResultadoDelPaso.Abortar, resultado.Tipo);
        Assert.Equal([("edicionId", libro.ToString("D")), ("sucursal", Sucursal.Cusco), ("maximo", "2")], resultado.Error!.Errores.Select(x => (x.Campo, x.Clave)));
        Assert.Equal((Sucursal.Cusco, 3), (carrito.Lineas[2].CodigoDeSucursal, carrito.Lineas[2].Cantidad.Valor));
        Assert.Equal(EstadoDelCheckout.Reservando, checkout.Estado);
    }

    [Fact]
    public async Task Una_reserva_desde_otra_sucursal_que_la_elegida_se_trata_como_falla_y_aborta()
    {
        var (e, _, _, _) = await ConDosSucursales();
        await Elegir(e, TipoDeEntregaDeCompra.Recojo);
        var preparado = (await Pagar(e, 1000m)).Valor;
        e.Inventario.AlReservar = ResultadoDeReservar.Reservada(new ReservaObtenida(
            preparado.PedidoId!.Value,
            Ahora.AddMinutes(15),
            [.. preparado.LineasImpresas.Select(l => new SeleccionDeLinea(l.LineaId, l.EdicionId, true, Sucursal.Lima, false, null, 10))],
            false));

        var resultado = await new ReservarImpresos(e.ServiciosDelCheckout()).Ejecutar(preparado);

        Assert.Equal((TipoDeResultadoDelPaso.Abortar, "fuente-distinta"), (resultado.Tipo, resultado.MotivoDelAborto));
        Assert.Null(resultado.NuevoInformado);
    }

    // ---------- Compras anteriores ----------

    /// <summary>Un checkout anterior a la venta por sucursal (líneas sin sucursal ni línea de carrito) restaurado en el estado pedido.</summary>
    private static (EntornoDelCarrito Entorno, Checkout Checkout, CarritoDeDominio Carrito, Guid Pedido) Anterior(EstadoDelCheckout estado)
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var libro = e.Impreso(10);
        var carrito = CarritoDeDominio.Restaurar(
            Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), EstadoDeCarrito.Abierto, 1, Ahora, Ahora, null,
            [LineaDeCarrito.Crear(Guid.NewGuid(), libro, false, null, Cantidad.Crear(2), Ahora)]);
        e.Carritos.Sembrar(carrito);
        var pedido = Guid.NewGuid();
        var checkout = Checkout.Restaurar(new DatosGuardadosDelCheckout
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta,
            CarritoId = carrito.Id,
            Estado = estado,
            Version = 3,
            CreadoEn = Ahora,
            ModificadoPorLaPersonaEn = Ahora,
            PasoDeEdicion = PasoDelCheckout.DatosYAceptaciones,
            Lineas = [LineaDelCheckout.Restaurar(Guid.NewGuid(), libro, false, "Libro", "9780000000019", 2, 50m, "lima", false, null, Ahora)],
            TipoDeEntrega = TipoDeEntrega.Recojo,
            CodigoDeSucursalDeRecojo = "lima",
            EnvioCongelado = 0m,
            TotalCongelado = 100m,
            PedidoId = pedido,
            ReservandoDesde = Ahora,
            HuellaDeReserva = new string('a', 64),
            GeneracionDeReserva = 1,
            ReservaExpiraEn = Ahora.AddMinutes(15),
        });
        e.Checkouts.Sembrar(checkout);
        return (e, checkout, carrito, pedido);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-39")]
    [InlineData(EstadoDelCheckout.EnPago)]
    [InlineData(EstadoDelCheckout.Reservando)]
    public async Task La_siguiente_solicitud_aborta_una_compra_anterior_sin_pago_pendiente_y_libera_su_reserva(EstadoDelCheckout estado)
    {
        var (e, checkout, _, pedido) = Anterior(estado);

        Assert.False((await new ObtenerCheckoutParaPagar(e.ServiciosDelCheckout()).Ejecutar(new ObtenerCheckoutParaPagarEntrada())).EsExito && estado == EstadoDelCheckout.EnPago);
        var resultado = await Pagar(e, 100m);

        Assert.Equal(ClavesDelCarrito.LineaSinSucursal, resultado.Error!.Clave);
        Assert.Equal((EstadoDelCheckout.EnEdicion, (Guid?)null, (Guid?)pedido), (checkout.Estado, checkout.PedidoId, checkout.PedidoDescartado));
        Assert.Equal(pedido, Assert.Single(e.Inventario.Liberaciones).PedidoId);
        Assert.Equal(1, e.Pagos.Cancelaciones);
        Assert.Equal(["linea-sin-sucursal"], e.Auditoria.Abortos);
        Assert.Equal(ClavesDelCarrito.LineaSinSucursal, (await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada())).Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public async Task Una_compra_anterior_con_un_cobro_vivo_no_cambia_nada()
    {
        var (e, checkout, _, pedido) = Anterior(EstadoDelCheckout.EnPago);
        e.Pagos.AlCancelar = ResultadoDeCancelarIntencion.NoCancelable;

        var resultado = await Pagar(e, 100m);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, resultado.Error!.Clave);
        Assert.Equal((EstadoDelCheckout.EnPago, (Guid?)pedido), (checkout.Estado, checkout.PedidoId));
        Assert.Empty(e.Inventario.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public async Task Un_pago_anterior_aprobado_se_completa_y_quita_del_carrito_lo_comprado()
    {
        var (e, checkout, carrito, pedido) = Anterior(EstadoDelCheckout.PagoPendiente);

        var resultado = await new ProgresoDelCheckout(e.ServiciosDelCheckout()).PagoAprobado(pedido);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, resultado);
        Assert.Equal((EstadoDelCheckout.Pagado, 100m), (checkout.Estado, checkout.TotalCongelado));
        Assert.Empty(carrito.Lineas);
        Assert.Empty(e.Inventario.Liberaciones);
    }
}
