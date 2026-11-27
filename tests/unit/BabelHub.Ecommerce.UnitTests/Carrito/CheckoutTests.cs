using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T04: el agregado <see cref="Checkout"/> (D02, D04, D10, D20, D21): estados y transiciones, pasos de 5 o de 4, la generación de reserva y las dos huellas, los instantes que informa Pagos y la cuenta regresiva, y la regla central
/// <c>AplicarRetirosPendientes</c> en toda salida de un estado vivo.
/// </summary>
public sealed class CheckoutTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CarritoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Func<string, string> Identidad = s => s;
    private static readonly CostoDeEnvio Envio = CostoDeEnvio.Crear(12.00m);

    private static Guid Edicion(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static LineaParaCheckout Impreso(int n, int cantidad = 1, decimal precio = 50m) => new(Edicion(n), false, $"Libro {n}", "9780000000019", cantidad, precio, Edicion(n), Sucursal.Principal);

    private static LineaParaCheckout Digital(int n) => new(Edicion(n), true, $"Digital {n}", "9780000000026", 1, 20m);

    private static readonly Guid LineaFija = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Checkout Nuevo(params LineaParaCheckout[] lineas) => ConAceptaciones(Checkout.Iniciar(Guid.NewGuid(), Cuenta, CarritoId, lineas, Guid.NewGuid, Ahora));

    /// <summary>Los tres textos aceptados (el del reembolso digital solo cuenta si hay digitales): lo que T05 exige antes de pasar al pago.</summary>
    private static Checkout ConAceptaciones(Checkout checkout)
    {
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
        return checkout;
    }

    /// <summary>Un checkout de una sola línea con un <c>lineaId</c> fijo: sirve para comparar huellas entre dos checkouts distintos.</summary>
    private static Checkout Determinista(LineaParaCheckout linea) => ConAceptaciones(Checkout.Iniciar(Guid.NewGuid(), Cuenta, CarritoId, [linea], () => LineaFija, Ahora));

    private static CarritoDeDominio CarritoCon(params LineaParaCheckout[] lineas)
    {
        var carrito = CarritoDeDominio.Crear(CarritoId, PropietarioDelCarrito.DeCuenta(Cuenta), Ahora);
        foreach (var l in lineas)
        {
            carrito.Agregar(l.EdicionId, l.EsDigital, l.Cantidad, Ahora);
        }

        return carrito;
    }

    private static ContactoDeEntrega Contacto(string telefono = "987654321") =>
        ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez"), TelefonoDeContacto.Crear(telefono), DireccionDeEntrega.Crear("Jr. Lima 123", "Ayacucho"));

    private static ResultadoDeCheckout Preparar(Checkout c, CarritoDeDominio k, Guid? pedido = null, DateTimeOffset? ahora = null) =>
        c.PrepararPasoAlPago(pedido ?? Guid.NewGuid(), Envio, Identidad, Identidad, k, ahora ?? Ahora);

    private static ResultadoDeCheckout ConfirmarEnPago(Checkout c, CarritoDeDominio k, DateTimeOffset? expira = null) =>
        c.ConfirmarEnPago(c.PedidoId!.Value, c.GeneracionDeReserva, c.HuellaDeInstantanea!, expira ?? Ahora.AddMinutes(15), k);

    /// <summary>Un checkout mixto (un impreso y una digital) con recojo, ya <c>reservando</c>.</summary>
    private static (Checkout Checkout, CarritoDeDominio Carrito) Reservando()
    {
        var checkout = Nuevo(Impreso(1, 2), Digital(2));
        checkout.ElegirRecojo(Ahora);
        var carrito = CarritoCon(Impreso(1, 2), Digital(2));
        Assert.Equal(EfectoDeTransicion.Aplicada, Preparar(checkout, carrito).Efecto);
        return (checkout, carrito);
    }

    private static (Checkout Checkout, CarritoDeDominio Carrito) EnPago()
    {
        var (checkout, carrito) = Reservando();
        Assert.Equal(EfectoDeTransicion.Aplicada, ConfirmarEnPago(checkout, carrito).Efecto);
        return (checkout, carrito);
    }

    private static (Checkout Checkout, CarritoDeDominio Carrito) PagoPendiente()
    {
        var (checkout, carrito) = EnPago();
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.PagoPendiente(checkout.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1)).Efecto);
        return (checkout, carrito);
    }

    // ---------------------------------------------------------------- inicio, pasos y entrega

    [Fact]
    public void Iniciar_arma_el_checkout_en_edicion_con_un_lineaid_estable_por_linea_tambien_las_digitales()
    {
        var checkout = Nuevo(Impreso(1, 2), Digital(2));

        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(PasoDelCheckout.Revision, checkout.Paso);
        Assert.Equal(2, checkout.Lineas.Count);
        Assert.All(checkout.Lineas, l => Assert.NotEqual(Guid.Empty, l.LineaId));
        Assert.Equal(2, checkout.Lineas.Select(l => l.LineaId).Distinct().Count());
        Assert.Null(checkout.PedidoId);
        Assert.Null(checkout.TipoDeEntrega);
        Assert.True(checkout.EsVivo);
        Assert.Equal(Ahora, checkout.ModificadoPorLaPersonaEn);
    }

    [Fact]
    public void Iniciar_sin_lineas_o_con_una_edicion_repetida_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => Nuevo());
        Assert.Throws<ArgumentException>(() => Nuevo(Impreso(1), Impreso(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-06")]
    public void Un_carrito_solo_digital_omite_la_entrega_y_la_reserva_y_se_completa_en_cuatro_pasos()
    {
        var checkout = Nuevo(Digital(1), Digital(2));

        Assert.True(checkout.EsSoloDigital);
        Assert.False(checkout.RequiereEntrega);
        Assert.False(checkout.RequiereReserva);
        Assert.Equal(TipoDeEntrega.Digital, checkout.TipoDeEntrega);
        Assert.Equal(4, checkout.TotalDePasos);
        Assert.Equal(RechazoDeCheckout.EsSoloDigital, checkout.ElegirRecojo(Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.EsSoloDigital, checkout.ElegirDomicilio(Contacto(), Ahora).Rechazo);

        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarRevision(Ahora).Efecto);
        Assert.Equal(PasoDelCheckout.DatosYAceptaciones, checkout.Paso);
        Assert.Equal(2, checkout.NumeroDePaso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-06")]
    public void Un_carrito_con_impresos_tiene_cinco_pasos_y_pasa_por_la_entrega()
    {
        var checkout = Nuevo(Impreso(1), Digital(2));

        Assert.Equal(5, checkout.TotalDePasos);
        Assert.True(checkout.RequiereEntrega);
        Assert.True(checkout.RequiereReserva);
        checkout.ConfirmarRevision(Ahora);
        Assert.Equal(PasoDelCheckout.Entrega, checkout.Paso);
        checkout.ElegirRecojo(Ahora);
        Assert.Equal(PasoDelCheckout.DatosYAceptaciones, checkout.Paso);
        Assert.Equal(3, checkout.NumeroDePaso);
    }

    [Fact]
    public void Elegir_domicilio_guarda_el_contacto_y_la_region_y_cambiar_a_recojo_los_quita_sin_sucursal_comun()
    {
        var checkout = Nuevo(Impreso(1));

        checkout.ElegirDomicilio(Contacto(), Ahora);
        Assert.Equal((TipoDeEntrega.Domicilio, "Ayacucho", null), (checkout.TipoDeEntrega, checkout.RegionDeEntrega, checkout.CodigoDeSucursalDeRecojo));
        Assert.NotNull(checkout.Contacto);

        checkout.ElegirRecojo(Ahora.AddMinutes(1));
        Assert.Equal((TipoDeEntrega.Recojo, null, null), (checkout.TipoDeEntrega, checkout.RegionDeEntrega, checkout.CodigoDeSucursalDeRecojo));
        Assert.Null(checkout.Contacto);
    }

    [Fact]
    public void Repetir_la_misma_entrega_no_cambia_nada_ni_la_version()
    {
        var checkout = Nuevo(Impreso(1));
        checkout.ElegirDomicilio(Contacto(), Ahora);
        var version = checkout.Version;

        var otraVez = checkout.ElegirDomicilio(Contacto(), Ahora.AddMinutes(5));

        Assert.Equal(EfectoDeTransicion.SinCambios, otraVez.Efecto);
        Assert.Equal(version, checkout.Version);
        Assert.Equal(Ahora, checkout.ModificadoPorLaPersonaEn);
    }

    [Fact]
    public void Sincronizar_las_lineas_conserva_el_lineaid_de_las_ediciones_que_siguen_y_lo_informado_y_da_uno_nuevo_a_las_nuevas()
    {
        var checkout = Nuevo(Impreso(1, 1), Impreso(2, 1));
        var idUno = checkout.Lineas.Single(l => l.EdicionId == Edicion(1)).LineaId;
        checkout.RegistrarLoInformado(checkout.Lineas.Single(l => l.EdicionId == Edicion(1)).LineaId, "lima", requiereTraslado: true, plazoDias: 3, Ahora);

        var resultado = checkout.SincronizarLineas([Impreso(1, 4), Impreso(3, 1)], Guid.NewGuid, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal([Edicion(1), Edicion(3)], checkout.Lineas.Select(l => l.EdicionId));
        var uno = checkout.Lineas.Single(l => l.EdicionId == Edicion(1));
        Assert.Equal((idUno, 4, "lima", true, 3), (uno.LineaId, uno.Cantidad.Valor, uno.CodigoDeFuenteInformado, uno.RequiereTrasladoInformado, uno.PlazoInformadoDias));
        Assert.NotEqual(idUno, checkout.Lineas.Single(l => l.EdicionId == Edicion(3)).LineaId);
    }

    [Fact]
    public void Sincronizar_las_mismas_lineas_no_cambia_nada_y_sin_lineas_se_rechaza()
    {
        var checkout = Nuevo(Impreso(1, 2));
        var version = checkout.Version;

        Assert.Equal(EfectoDeTransicion.SinCambios, checkout.SincronizarLineas([Impreso(1, 2)], Guid.NewGuid, Ahora.AddMinutes(1)).Efecto);
        Assert.Equal(version, checkout.Version);
        Assert.Equal(RechazoDeCheckout.SinLineas, checkout.SincronizarLineas([], Guid.NewGuid, Ahora).Rechazo);
    }

    [Fact]
    public void Si_las_lineas_pasan_a_ser_solo_digitales_la_entrega_y_el_contacto_se_quitan_y_si_vuelven_a_haber_impresos_la_entrega_queda_por_elegir()
    {
        var checkout = Nuevo(Impreso(1), Digital(2));
        checkout.ElegirDomicilio(Contacto(), Ahora);

        checkout.SincronizarLineas([Digital(2)], Guid.NewGuid, Ahora.AddMinutes(1));
        Assert.Equal(TipoDeEntrega.Digital, checkout.TipoDeEntrega);
        Assert.Null(checkout.Contacto);
        Assert.Null(checkout.RegionDeEntrega);

        checkout.SincronizarLineas([Digital(2), Impreso(1)], Guid.NewGuid, Ahora.AddMinutes(2));
        Assert.Null(checkout.TipoDeEntrega);
    }

    [Fact]
    public void El_total_suma_las_lineas_y_el_envio_solo_a_domicilio()
    {
        var checkout = Nuevo(Impreso(1, 3, 37.30m));
        checkout.ElegirDomicilio(Contacto(), Ahora);
        Assert.Equal(123.90m, checkout.CalcularTotal(Envio).Total);

        checkout.ElegirRecojo(Ahora);
        Assert.Equal(111.90m, checkout.CalcularTotal(Envio).Total);
    }

    // ---------------------------------------------------------------- pasar al pago

    [Fact]
    public void Pasar_al_pago_congela_el_envio_y_el_total_asigna_el_pedido_y_deja_el_checkout_reservando()
    {
        var checkout = Nuevo(Impreso(1, 3, 37.30m));
        checkout.ElegirDomicilio(Contacto(), Ahora);
        var pedido = Guid.NewGuid();

        var resultado = Preparar(checkout, CarritoCon(Impreso(1, 3)), pedido, Ahora.AddMinutes(2));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Reservando, checkout.Estado);
        Assert.Equal(pedido, checkout.PedidoId);
        Assert.Equal(Ahora.AddMinutes(2), checkout.ReservandoDesde);
        Assert.Equal((12.00m, 123.90m), (checkout.EnvioCongelado, checkout.TotalCongelado));
        Assert.Equal(1, checkout.GeneracionDeReserva);
        Assert.NotNull(checkout.HuellaDeReserva);
        Assert.NotNull(checkout.HuellaDeInstantanea);
        Assert.Equal(PasoDelCheckout.Pago, checkout.Paso);
    }

    [Fact]
    public void Pasar_al_pago_se_rechaza_sin_entrega_sin_contacto_o_con_un_retiro_pendiente_y_no_cambia_nada()
    {
        var sinEntrega = Nuevo(Impreso(1));
        Assert.Equal(RechazoDeCheckout.EntregaNoElegida, Preparar(sinEntrega, CarritoCon(Impreso(1))).Rechazo);
        Assert.Equal(EstadoDelCheckout.EnEdicion, sinEntrega.Estado);
        Assert.Null(sinEntrega.PedidoId);

        var conRetiro = Nuevo(Impreso(1));
        conRetiro.ElegirRecojo(Ahora);
        var carrito = CarritoCon(Impreso(1));
        carrito.RetirarEdicion(Edicion(1), diferir: true, Ahora);
        Assert.Equal(RechazoDeCheckout.HayRetiroPendiente, Preparar(conRetiro, carrito).Rechazo);
        Assert.Equal(EstadoDelCheckout.EnEdicion, conRetiro.Estado);
        Assert.Equal(0, conRetiro.GeneracionDeReserva);
    }

    [Fact]
    public void Pasar_al_pago_de_un_carrito_solo_digital_no_requiere_entrega()
    {
        var checkout = Nuevo(Digital(1));

        var resultado = Preparar(checkout, CarritoCon(Digital(1)));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Reservando, checkout.Estado);
        Assert.Equal(0m, checkout.EnvioCongelado);
        Assert.Equal(20m, checkout.TotalCongelado);
    }

    [Fact]
    public void Un_segundo_clic_con_lo_mismo_continua_con_el_mismo_pedido_sin_cambiar_nada()
    {
        var (checkout, carrito) = Reservando();
        var pedido = checkout.PedidoId;
        var generacion = checkout.GeneracionDeReserva;
        var version = checkout.Version;

        var otraVez = Preparar(checkout, carrito, Guid.NewGuid());

        Assert.Equal(EfectoDeTransicion.SinCambios, otraVez.Efecto);
        Assert.Equal((pedido, generacion, version), (checkout.PedidoId, checkout.GeneracionDeReserva, checkout.Version));
    }

    [Fact]
    public void Con_el_checkout_ya_en_pago_y_la_misma_huella_pasar_al_pago_es_un_exito_idempotente()
    {
        var (checkout, carrito) = EnPago();

        var otraVez = Preparar(checkout, carrito);

        Assert.Equal(EfectoDeTransicion.SinCambios, otraVez.Efecto);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
    }

    [Fact]
    public void Con_el_pago_pendiente_o_terminado_pasar_al_pago_se_rechaza()
    {
        var (pendiente, carritoP) = PagoPendiente();
        Assert.Equal(RechazoDeCheckout.PagoPendiente, Preparar(pendiente, carritoP).Rechazo);

        var (pagado, carritoG) = PagoPendiente();
        pagado.PagoAprobado(pagado.PedidoId!.Value, carritoG, Ahora);
        Assert.Equal(RechazoDeCheckout.YaTerminado, Preparar(pagado, carritoG).Rechazo);
    }

    [Fact]
    public void La_generacion_de_reserva_sube_solo_cuando_cambia_la_huella_de_reserva_asi_A_B_A_no_repite_la_clave_de_A()
    {
        var (checkout, carrito) = EnPago();
        var pedido = checkout.PedidoId;
        Assert.Equal(1, checkout.GeneracionDeReserva);

        // B: otra cantidad. Se edita con el checkout en pago (Reabrir ya se llamó) y se vuelve a pasar al pago.
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.Editar(carrito, Ahora.AddMinutes(1)).Efecto);
        checkout.SincronizarLineas([Impreso(1, 3), Digital(2)], Guid.NewGuid, Ahora.AddMinutes(1));
        Preparar(checkout, carrito, ahora: Ahora.AddMinutes(2));
        Assert.Equal((pedido, 2), (checkout.PedidoId, checkout.GeneracionDeReserva));
        ConfirmarEnPago(checkout, carrito);

        // A otra vez: la huella de A ya no es la vigente, así que sube a 3 y no reutiliza la clave de la generación 1.
        checkout.Editar(carrito, Ahora.AddMinutes(3));
        checkout.SincronizarLineas([Impreso(1, 2), Digital(2)], Guid.NewGuid, Ahora.AddMinutes(3));
        Preparar(checkout, carrito, ahora: Ahora.AddMinutes(4));
        Assert.Equal((pedido, 3), (checkout.PedidoId, checkout.GeneracionDeReserva));
    }

    [Fact]
    public void Cambiar_solo_el_telefono_reconstruye_la_instantanea_pero_no_la_reserva()
    {
        var checkout = Nuevo(Impreso(1));
        checkout.ElegirDomicilio(Contacto("987654321"), Ahora);
        var carrito = CarritoCon(Impreso(1));
        Preparar(checkout, carrito);
        ConfirmarEnPago(checkout, carrito);
        var huellaDeReserva = checkout.HuellaDeReserva;
        var huellaDeInstantanea = checkout.HuellaDeInstantanea;

        checkout.Editar(carrito, Ahora.AddMinutes(1));
        checkout.ElegirDomicilio(Contacto("911222333"), Ahora.AddMinutes(1));
        Preparar(checkout, carrito, ahora: Ahora.AddMinutes(2));

        Assert.Equal(huellaDeReserva, checkout.HuellaDeReserva);
        Assert.Equal(1, checkout.GeneracionDeReserva);
        Assert.NotEqual(huellaDeInstantanea, checkout.HuellaDeInstantanea);
    }

    [Fact]
    public void La_huella_de_reserva_cubre_lineas_cantidades_y_entrega_y_nunca_el_contacto_ni_los_precios_ni_las_digitales()
    {
        string? reserva = null;
        string? instantanea = null;
        var checkout = Nuevo(Impreso(1, 2, 50m), Digital(2));
        checkout.ElegirDomicilio(Contacto("987654321"), Ahora);

        checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, s => reserva = s, s => instantanea = s, CarritoCon(Impreso(1, 2), Digital(2)), Ahora);

        Assert.NotNull(reserva);
        Assert.DoesNotContain("987654321", reserva, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", reserva, StringComparison.Ordinal);
        Assert.DoesNotContain("Jr. Lima", reserva, StringComparison.Ordinal);
        Assert.DoesNotContain("Digital 2", reserva, StringComparison.Ordinal);
        Assert.DoesNotContain(Edicion(2).ToString("N"), reserva, StringComparison.Ordinal);
        Assert.Contains(Edicion(1).ToString("N"), reserva, StringComparison.Ordinal);
        Assert.Contains("Ayacucho", reserva, StringComparison.Ordinal);
        Assert.NotNull(instantanea);
        Assert.Contains("987654321", instantanea, StringComparison.Ordinal);
        Assert.Contains("Digital 2", instantanea, StringComparison.Ordinal);
    }

    [Fact]
    public void La_huella_de_instantanea_cambia_con_el_precio_el_envio_y_el_contacto_y_es_estable_si_nada_cambia()
    {
        static string? Huella(decimal precio, decimal envio, string telefono)
        {
            string? h = null;
            var checkout = Determinista(Impreso(1, 1, precio));
            checkout.ElegirDomicilio(Contacto(telefono), Ahora);
            checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(envio), Identidad, s => h = s, CarritoCon(Impreso(1)), Ahora);
            return h;
        }

        var baseA = Huella(50m, 12m, "987654321");
        var baseB = Huella(50m, 12m, "987654321");

        Assert.Equal(baseA, baseB);
        Assert.NotEqual(baseA, Huella(51m, 12m, "987654321"));
        Assert.NotEqual(baseA, Huella(50m, 15m, "987654321"));
        Assert.NotEqual(baseA, Huella(50m, 12m, "911222333"));
    }

    [Fact]
    public void Los_campos_de_texto_de_la_huella_no_se_confunden_aunque_contengan_el_separador()
    {
        static string? Huella(string titulo)
        {
            string? h = null;
            var checkout = Determinista(new LineaParaCheckout(Edicion(1), false, titulo, "9780000000019", 1, 50m, Edicion(1), Sucursal.Principal));
            checkout.ElegirRecojo(Ahora);
            checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, s => h = s, CarritoCon(Impreso(1)), Ahora);
            return h;
        }

        Assert.NotEqual(Huella("a|b"), Huella("a"));
        Assert.NotEqual(Huella("a|9780000000019"), Huella("a"));
    }

    // ---------------------------------------------------------------- confirmar en pago

    [Fact]
    public void Confirmar_en_pago_pasa_a_en_pago_con_la_expiracion_de_la_reserva_y_quita_el_instante_de_reservando()
    {
        var (checkout, carrito) = Reservando();

        var resultado = ConfirmarEnPago(checkout, carrito, Ahora.AddMinutes(15));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Equal(Ahora.AddMinutes(15), checkout.ReservaExpiraEn);
        Assert.Null(checkout.ReservandoDesde);
    }

    [Fact]
    public void Confirmar_en_pago_con_otro_pedido_otra_generacion_u_otra_huella_no_aplica_y_no_toca_nada()
    {
        var (checkout, carrito) = Reservando();

        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.ConfirmarEnPago(Guid.NewGuid(), checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Ahora.AddMinutes(15), carrito).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva + 1, checkout.HuellaDeInstantanea!, Ahora.AddMinutes(15), carrito).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, "otra", Ahora.AddMinutes(15), carrito).Efecto);
        Assert.Equal(EstadoDelCheckout.Reservando, checkout.Estado);
    }

    [Fact]
    public void Confirmar_en_pago_dos_veces_con_lo_mismo_es_un_exito_idempotente()
    {
        var (checkout, carrito) = EnPago();
        var version = checkout.Version;

        var otraVez = ConfirmarEnPago(checkout, carrito);

        Assert.Equal(EfectoDeTransicion.SinCambios, otraVez.Efecto);
        Assert.Equal(version, checkout.Version);
    }

    [Fact]
    public void Con_una_linea_con_retiro_pendiente_confirmar_en_pago_no_cambia_el_estado()
    {
        var (checkout, carrito) = Reservando();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var resultado = ConfirmarEnPago(checkout, carrito);

        Assert.Equal(EfectoDeTransicion.ConRetiroPendiente, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Reservando, checkout.Estado);
        Assert.True(carrito.TieneRetirosPendientes);
    }

    // ---------------------------------------------------------------- editar

    [Fact]
    public void Editar_con_el_checkout_en_pago_lo_devuelve_a_en_edicion_conservando_el_pedido_y_la_reserva()
    {
        var (checkout, carrito) = EnPago();
        var pedido = checkout.PedidoId;

        var resultado = checkout.Editar(carrito, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(pedido, checkout.PedidoId);
        Assert.Equal(Ahora.AddMinutes(15), checkout.ReservaExpiraEn);
        Assert.Null(checkout.ReservaALiberarDesde);
        Assert.Equal(Ahora.AddMinutes(1), checkout.ModificadoPorLaPersonaEn);
    }

    [Fact]
    public void Editar_con_el_checkout_en_edicion_no_hace_nada_y_el_cambio_lo_hace_quien_llama()
    {
        var checkout = Nuevo(Impreso(1));

        Assert.Equal(EfectoDeTransicion.SinCambios, checkout.Editar(CarritoCon(Impreso(1)), Ahora).Efecto);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-23")]
    public void Con_el_pago_pendiente_no_se_puede_editar_volver_ni_reiniciar()
    {
        var (checkout, carrito) = PagoPendiente();

        Assert.Equal(RechazoDeCheckout.PagoPendiente, checkout.Editar(carrito, Ahora.AddMinutes(2)).Rechazo);
        Assert.Equal(RechazoDeCheckout.PagoPendiente, checkout.Volver(carrito, Ahora.AddMinutes(2)).Rechazo);
        Assert.Equal(RechazoDeCheckout.PagoPendiente, checkout.Reiniciar(carrito, Ahora.AddMinutes(2)).Rechazo);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, checkout.Estado);
    }

    [Fact]
    public void Con_el_checkout_reservando_editar_volver_y_reiniciar_responden_en_curso()
    {
        var (checkout, carrito) = Reservando();

        Assert.Equal(RechazoDeCheckout.EnCurso, checkout.Editar(carrito, Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.EnCurso, checkout.Volver(carrito, Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.EnCurso, checkout.Reiniciar(carrito, Ahora).Rechazo);
        Assert.Equal(EstadoDelCheckout.Reservando, checkout.Estado);
    }

    [Fact]
    public void Con_el_checkout_reservando_o_en_pago_no_se_puede_cambiar_la_entrega_ni_las_lineas()
    {
        var (reservando, _) = Reservando();
        Assert.Equal(RechazoDeCheckout.EnCurso, reservando.ElegirRecojo(Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.EnCurso, reservando.SincronizarLineas([Impreso(1)], Guid.NewGuid, Ahora).Rechazo);

        var (enPago, _) = EnPago();
        Assert.Equal(RechazoDeCheckout.EstadoNoPermite, enPago.ElegirRecojo(Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.EstadoNoPermite, enPago.SincronizarLineas([Impreso(1)], Guid.NewGuid, Ahora).Rechazo);
    }

    // ---------------------------------------------------------------- abortar y recuperar

    [Fact]
    public void Abortar_desde_reservando_vuelve_a_en_edicion_descarta_el_pedido_sube_la_generacion_y_anota_la_liberacion()
    {
        var (checkout, carrito) = Reservando();
        var pedido = checkout.PedidoId!.Value;
        var generacion = checkout.GeneracionDeReserva;

        var resultado = checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, pedido, generacion, carrito, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Null(checkout.PedidoId);
        Assert.Equal(pedido, checkout.PedidoDescartado);
        Assert.Equal(generacion + 1, checkout.GeneracionDeReserva);
        Assert.Equal(Ahora.AddMinutes(1), checkout.ReservaALiberarDesde);
        Assert.Null(checkout.ReservandoDesde);
    }

    [Fact]
    public void Abortar_desde_en_pago_hace_lo_mismo_y_limpia_la_expiracion()
    {
        var (checkout, carrito) = EnPago();
        var pedido = checkout.PedidoId!.Value;

        var resultado = checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, pedido, checkout.GeneracionDeReserva, carrito, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal((EstadoDelCheckout.EnEdicion, (Guid?)null, (Guid?)pedido, (DateTimeOffset?)null), (checkout.Estado, checkout.PedidoId, checkout.PedidoDescartado, checkout.ReservaExpiraEn));
    }

    [Fact]
    public void Un_aborto_condicional_no_hace_nada_si_el_checkout_ya_cambio_de_estado_de_pedido_o_de_generacion()
    {
        var (checkout, carrito) = EnPago();
        var pedido = checkout.PedidoId!.Value;
        var generacion = checkout.GeneracionDeReserva;

        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, pedido, generacion, carrito, Ahora).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, Guid.NewGuid(), generacion, carrito, Ahora).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, pedido, generacion + 1, carrito, Ahora).Efecto);
        Assert.Equal((EstadoDelCheckout.EnPago, (Guid?)pedido), (checkout.Estado, checkout.PedidoId));
        Assert.Throws<ArgumentException>(() => checkout.AbortarPasoAlPago(EstadoDelCheckout.EnEdicion, pedido, generacion, carrito, Ahora));
    }

    [Fact]
    public void Despues_de_un_aborto_el_siguiente_intento_usa_un_pedido_nuevo_y_la_generacion_no_se_repite()
    {
        var (checkout, carrito) = Reservando();
        var primero = checkout.PedidoId!.Value;
        checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, primero, checkout.GeneracionDeReserva, carrito, Ahora.AddMinutes(1));
        var generacionDelAborto = checkout.GeneracionDeReserva;

        Preparar(checkout, carrito, ahora: Ahora.AddMinutes(2));

        Assert.NotEqual(primero, checkout.PedidoId);
        Assert.Equal(generacionDelAborto, checkout.GeneracionDeReserva);
        Assert.Equal(primero, checkout.PedidoDescartado);
    }

    [Fact]
    public void Un_aborto_de_un_carrito_solo_digital_no_anota_ninguna_reserva_que_liberar()
    {
        var checkout = Nuevo(Digital(1));
        var carrito = CarritoCon(Digital(1));
        Preparar(checkout, carrito);

        checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, checkout.PedidoId!.Value, checkout.GeneracionDeReserva, carrito, Ahora);

        Assert.Null(checkout.ReservaALiberarDesde);
    }

    [Fact]
    public void Un_checkout_reservando_desde_hace_dos_minutos_o_mas_esta_vencido_y_uno_mas_reciente_no()
    {
        var (checkout, _) = Reservando();

        Assert.False(checkout.ReservandoVencido(Ahora.AddMinutes(1).AddSeconds(59)));
        Assert.True(checkout.ReservandoVencido(Ahora.AddMinutes(2)));
        Assert.False(Nuevo(Impreso(1)).ReservandoVencido(Ahora.AddHours(5)));
    }

    // ---------------------------------------------------------------- volver y reiniciar

    [Fact]
    public void Volver_desde_en_edicion_o_en_pago_cancela_y_anota_la_reserva_que_liberar()
    {
        var (enPago, carritoP) = EnPago();
        var pedido = enPago.PedidoId;

        var resultado = enPago.Volver(carritoP, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, enPago.Estado);
        Assert.Equal(Ahora.AddMinutes(1), enPago.ReservaALiberarDesde);
        Assert.Equal(pedido, enPago.PedidoId);
        Assert.False(enPago.EsVivo);

        var enEdicion = Nuevo(Impreso(1));
        Assert.Equal(EfectoDeTransicion.Aplicada, enEdicion.Volver(CarritoCon(Impreso(1)), Ahora).Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, enEdicion.Estado);
        Assert.Null(enEdicion.ReservaALiberarDesde);
    }

    [Fact]
    public void Reiniciar_deja_el_checkout_reiniciado_y_anota_la_reserva_que_liberar()
    {
        var (checkout, carrito) = EnPago();

        var resultado = checkout.Reiniciar(carrito, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Reiniciado, checkout.Estado);
        Assert.Equal(Ahora.AddMinutes(1), checkout.ReservaALiberarDesde);
    }

    [Fact]
    public void Volver_o_reiniciar_un_checkout_ya_terminado_se_rechaza()
    {
        var (checkout, carrito) = EnPago();
        checkout.Volver(carrito, Ahora);

        Assert.Equal(RechazoDeCheckout.YaTerminado, checkout.Volver(carrito, Ahora).Rechazo);
        Assert.Equal(RechazoDeCheckout.YaTerminado, checkout.Reiniciar(carrito, Ahora).Rechazo);
    }

    [Fact]
    public void Confirmar_que_la_reserva_se_libero_limpia_la_anotacion()
    {
        var (checkout, carrito) = EnPago();
        checkout.Volver(carrito, Ahora);

        checkout.LiberacionConfirmada();

        Assert.Null(checkout.ReservaALiberarDesde);
    }

    // ---------------------------------------------------------------- lo que informa Pagos

    [Fact]
    [Trait("Criterio", "AC-CAR-23")]
    public void Pago_pendiente_guarda_los_instantes_de_pagos_y_la_cuenta_regresiva_corre_hasta_los_30_minutos()
    {
        var (checkout, _) = EnPago();

        var resultado = checkout.PagoPendiente(checkout.PedidoId!.Value, limiteEn: Ahora.AddMinutes(31), reservaExpiraEn: Ahora.AddMinutes(30), Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, checkout.Estado);
        Assert.Equal((Ahora.AddMinutes(31), Ahora.AddMinutes(1), Ahora.AddMinutes(30)), (checkout.LimitePendienteEn, checkout.PagoPendienteDesde, checkout.ReservaExpiraEn));
        Assert.Equal(TimeSpan.FromMinutes(30), checkout.CuentaRegresiva(Ahora.AddMinutes(1)));
        Assert.Equal(TimeSpan.FromMinutes(10), checkout.CuentaRegresiva(Ahora.AddMinutes(21)));
        Assert.Equal(TimeSpan.Zero, checkout.CuentaRegresiva(Ahora.AddHours(2)));
    }

    [Fact]
    public void La_cuenta_regresiva_llega_a_cero_y_la_reserva_deja_de_estar_vigente_al_llegar_a_su_expiracion()
    {
        var (checkout, _) = EnPago();

        Assert.Equal(TimeSpan.FromMinutes(15), checkout.CuentaRegresiva(Ahora));
        Assert.Equal(TimeSpan.FromMinutes(5), checkout.CuentaRegresiva(Ahora.AddMinutes(10)));
        Assert.True(checkout.ReservaVigente(Ahora.AddMinutes(14).AddSeconds(59)));
        Assert.False(checkout.ReservaVigente(Ahora.AddMinutes(15)));
        Assert.Equal(TimeSpan.Zero, checkout.CuentaRegresiva(Ahora.AddMinutes(16)));
    }

    [Fact]
    public void Sin_estado_de_pago_no_hay_cuenta_regresiva()
    {
        Assert.Null(Nuevo(Impreso(1)).CuentaRegresiva(Ahora));
    }

    [Fact]
    public void Pago_pendiente_mientras_el_checkout_reserva_responde_aun_no_y_repetido_o_ya_pagado_es_idempotente()
    {
        var (reservando, _) = Reservando();
        Assert.Equal(EfectoDeTransicion.AunNo, reservando.PagoPendiente(reservando.PedidoId!.Value, Ahora.AddMinutes(30), Ahora.AddMinutes(30), Ahora).Efecto);
        Assert.Equal(EstadoDelCheckout.Reservando, reservando.Estado);

        var (pendiente, carrito) = PagoPendiente();
        Assert.Equal(EfectoDeTransicion.SinCambios, pendiente.PagoPendiente(pendiente.PedidoId!.Value, Ahora.AddMinutes(40), Ahora.AddMinutes(40), Ahora.AddMinutes(5)).Efecto);
        Assert.Equal(Ahora.AddMinutes(31), pendiente.LimitePendienteEn);
        pendiente.PagoAprobado(pendiente.PedidoId!.Value, carrito, Ahora.AddMinutes(6));
        Assert.Equal(EfectoDeTransicion.SinCambios, pendiente.PagoPendiente(pendiente.PedidoId!.Value, Ahora.AddMinutes(40), Ahora.AddMinutes(40), Ahora.AddMinutes(7)).Efecto);
        Assert.Equal(EstadoDelCheckout.Pagado, pendiente.Estado);
    }

    [Fact]
    public void Pago_aprobado_desde_pago_pendiente_o_desde_en_pago_deja_el_checkout_pagado_y_es_idempotente()
    {
        var (pendiente, carritoP) = PagoPendiente();
        Assert.Equal(EfectoDeTransicion.Aplicada, pendiente.PagoAprobado(pendiente.PedidoId!.Value, carritoP, Ahora.AddMinutes(5)).Efecto);
        Assert.Equal(EstadoDelCheckout.Pagado, pendiente.Estado);
        Assert.Equal(PasoDelCheckout.Confirmacion, pendiente.Paso);
        Assert.Equal(EfectoDeTransicion.SinCambios, pendiente.PagoAprobado(pendiente.PedidoId!.Value, carritoP, Ahora.AddMinutes(6)).Efecto);

        var (enPago, carritoE) = EnPago();
        Assert.Equal(EfectoDeTransicion.Aplicada, enPago.PagoAprobado(enPago.PedidoId!.Value, carritoE, Ahora.AddMinutes(5)).Efecto);
        Assert.Equal(EstadoDelCheckout.Pagado, enPago.Estado);
    }

    [Fact]
    public void Pago_aprobado_mientras_reserva_responde_aun_no_y_en_un_checkout_cancelado_no_aplica()
    {
        var (reservando, carritoR) = Reservando();
        Assert.Equal(EfectoDeTransicion.AunNo, reservando.PagoAprobado(reservando.PedidoId!.Value, carritoR, Ahora).Efecto);

        var (cancelado, carritoC) = EnPago();
        cancelado.Volver(carritoC, Ahora);
        Assert.Equal(EfectoDeTransicion.NoAplica, cancelado.PagoAprobado(cancelado.PedidoId!.Value, carritoC, Ahora).Efecto);
    }

    [Theory]
    [InlineData(MotivoDePagoResuelto.Vencido)]
    [InlineData(MotivoDePagoResuelto.Rechazado)]
    public void Un_pago_vencido_o_rechazado_con_la_reserva_vigente_vuelve_a_en_pago_y_quita_los_instantes_del_pago_pendiente(MotivoDePagoResuelto motivo)
    {
        var (checkout, carrito) = PagoPendiente();

        var resultado = checkout.PagoResuelto(checkout.PedidoId!.Value, motivo, carrito, Ahora.AddMinutes(10));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Null(checkout.LimitePendienteEn);
        Assert.Null(checkout.PagoPendienteDesde);
    }

    [Theory]
    [InlineData(MotivoDePagoResuelto.Vencido)]
    [InlineData(MotivoDePagoResuelto.Rechazado)]
    public void Un_pago_vencido_o_rechazado_con_la_reserva_expirada_cancela_el_checkout_y_anota_la_liberacion(MotivoDePagoResuelto motivo)
    {
        var (checkout, carrito) = PagoPendiente();

        var resultado = checkout.PagoResuelto(checkout.PedidoId!.Value, motivo, carrito, Ahora.AddMinutes(31));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, checkout.Estado);
        Assert.Equal(Ahora.AddMinutes(31), checkout.ReservaALiberarDesde);
    }

    [Fact]
    public void Un_pago_compensado_cancela_el_checkout_desde_pago_pendiente_o_en_pago_y_repetirlo_es_idempotente()
    {
        var (pendiente, carritoP) = PagoPendiente();
        Assert.Equal(EfectoDeTransicion.Aplicada, pendiente.PagoResuelto(pendiente.PedidoId!.Value, MotivoDePagoResuelto.Compensado, carritoP, Ahora.AddMinutes(5)).Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, pendiente.Estado);
        Assert.Equal(EfectoDeTransicion.SinCambios, pendiente.PagoResuelto(pendiente.PedidoId!.Value, MotivoDePagoResuelto.Compensado, carritoP, Ahora.AddMinutes(6)).Efecto);

        var (enPago, carritoE) = EnPago();
        Assert.Equal(EfectoDeTransicion.Aplicada, enPago.PagoResuelto(enPago.PedidoId!.Value, MotivoDePagoResuelto.Compensado, carritoE, Ahora.AddMinutes(5)).Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, enPago.Estado);
    }

    [Fact]
    public void Resolver_un_pago_mientras_el_checkout_reserva_responde_aun_no_y_si_ya_esta_en_pago_es_idempotente()
    {
        var (reservando, carritoR) = Reservando();
        Assert.Equal(EfectoDeTransicion.AunNo, reservando.PagoResuelto(reservando.PedidoId!.Value, MotivoDePagoResuelto.Vencido, carritoR, Ahora).Efecto);

        var (enPago, carritoE) = EnPago();
        Assert.Equal(EfectoDeTransicion.SinCambios, enPago.PagoResuelto(enPago.PedidoId!.Value, MotivoDePagoResuelto.Rechazado, carritoE, Ahora).Efecto);
        Assert.Equal(EstadoDelCheckout.EnPago, enPago.Estado);
    }

    [Fact]
    public void Un_pago_rechazado_con_un_retiro_pendiente_no_cambia_el_estado_y_pide_reabrir()
    {
        var (checkout, carrito) = PagoPendiente();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var resultado = checkout.PagoResuelto(checkout.PedidoId!.Value, MotivoDePagoResuelto.Rechazado, carrito, Ahora.AddMinutes(10));

        Assert.Equal(EfectoDeTransicion.ConRetiroPendiente, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, checkout.Estado);
    }

    [Fact]
    public void Despues_de_reabrir_se_vuelve_a_en_edicion_desde_pago_pendiente_o_en_pago_aplicando_el_retiro()
    {
        var (checkout, carrito) = PagoPendiente();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var resultado = checkout.VolverAEnEdicionTrasReabrir(carrito, Ahora.AddMinutes(10));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal([new EdicionRetirada(Edicion(2), true)], resultado.Retiradas);
        Assert.Null(checkout.LimitePendienteEn);
        Assert.NotNull(checkout.PedidoId);
        Assert.Equal(Ahora, checkout.ModificadoPorLaPersonaEn);
        Assert.Equal(EfectoDeTransicion.SinCambios, checkout.VolverAEnEdicionTrasReabrir(carrito, Ahora).Efecto);
    }

    // ---------------------------------------------------------------- retiros pendientes

    [Fact]
    public void Una_salida_del_estado_vivo_aplica_los_retiros_pendientes_y_los_quita_del_carrito()
    {
        // Entrar: con el retiro anotado antes de pasar a en_pago, 3b no cambia el estado (cubierto arriba); anotado después, la línea sigue.
        var (checkout, carrito) = EnPago();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);
        Assert.Contains(carrito.Lineas, l => l.EdicionId == Edicion(2) && l.RetiroPendiente);

        var resultado = checkout.Editar(carrito, Ahora.AddMinutes(1));

        Assert.Equal([new EdicionRetirada(Edicion(2), true)], resultado.Retiradas);
        Assert.DoesNotContain(carrito.Lineas, l => l.EdicionId == Edicion(2));
        Assert.False(carrito.TieneRetirosPendientes);
    }

    [Fact]
    public void Volver_reiniciar_abortar_y_pagado_tambien_quitan_las_lineas_con_retiro_pendiente()
    {
        foreach (var salida in new Func<Checkout, CarritoDeDominio, ResultadoDeCheckout>[]
        {
            (c, k) => c.Volver(k, Ahora.AddMinutes(1)),
            (c, k) => c.Reiniciar(k, Ahora.AddMinutes(1)),
            (c, k) => c.AbortarPasoAlPago(EstadoDelCheckout.EnPago, c.PedidoId!.Value, c.GeneracionDeReserva, k, Ahora.AddMinutes(1)),
            (c, k) => c.PagoAprobado(c.PedidoId!.Value, k, Ahora.AddMinutes(1)),
        })
        {
            var (checkout, carrito) = EnPago();
            carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

            var resultado = salida(checkout, carrito);

            Assert.Equal([new EdicionRetirada(Edicion(2), true)], resultado.Retiradas);
            Assert.False(carrito.TieneRetirosPendientes);
        }
    }

    [Fact]
    public void Recuperar_un_checkout_reservando_aplica_los_retiros_al_volver_a_en_edicion()
    {
        var (checkout, carrito) = Reservando();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var resultado = checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, checkout.PedidoId!.Value, checkout.GeneracionDeReserva, carrito, Ahora.AddMinutes(3));

        Assert.Equal([new EdicionRetirada(Edicion(2), true)], resultado.Retiradas);
    }

    [Fact]
    public void Los_estados_que_sostienen_un_retiro_pendiente_son_reservando_en_pago_y_pago_pendiente()
    {
        Assert.Equal(
            [EstadoDelCheckout.Reservando, EstadoDelCheckout.EnPago, EstadoDelCheckout.PagoPendiente],
            Enum.GetValues<EstadoDelCheckout>().Where(Checkout.SostieneRetirosPendientes));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-21")]
    public void El_envio_y_el_total_quedan_congelados_al_pasar_al_pago_aunque_cambie_la_configuracion_despues()
    {
        var checkout = Nuevo(Impreso(1, 3, 37.30m));
        checkout.ElegirDomicilio(Contacto(), Ahora);
        var carrito = CarritoCon(Impreso(1, 3));
        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12.00m), Identidad, Identidad, carrito, Ahora);
        Assert.Equal((12.00m, 123.90m), (checkout.EnvioCongelado, checkout.TotalCongelado));

        var otraVez = checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(15.00m), Identidad, Identidad, carrito, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.SinCambios, otraVez.Efecto);
        Assert.Equal((12.00m, 123.90m), (checkout.EnvioCongelado, checkout.TotalCongelado));
        ConfirmarEnPago(checkout, carrito);
        Assert.Equal(EfectoDeTransicion.SinCambios, checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(15.00m), Identidad, Identidad, carrito, Ahora.AddMinutes(2)).Efecto);
        Assert.Equal(123.90m, checkout.TotalCongelado);
    }

    [Fact]
    public void Con_un_retiro_pendiente_y_un_intento_activo_el_pago_pendiente_vuelve_a_en_pago_conservando_el_retiro()
    {
        var (checkout, carrito) = PagoPendiente();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var resultado = checkout.VolverAEnPagoConservandoElRetiro(checkout.PedidoId!.Value, Ahora.AddMinutes(10));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Null(checkout.LimitePendienteEn);
        Assert.True(carrito.TieneRetirosPendientes);
        Assert.Equal(EfectoDeTransicion.SinCambios, checkout.VolverAEnPagoConservandoElRetiro(checkout.PedidoId!.Value, Ahora.AddMinutes(11)).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, checkout.VolverAEnPagoConservandoElRetiro(Guid.NewGuid(), Ahora).Efecto);
    }

    [Fact]
    public void Si_reabrir_responde_cerrada_o_no_existe_el_checkout_se_cancela_anota_la_liberacion_y_aplica_el_retiro()
    {
        var (checkout, carrito) = PagoPendiente();
        carrito.RetirarEdicion(Edicion(2), diferir: true, Ahora);

        var resultado = checkout.CancelarPorIntencionCerrada(checkout.PedidoId!.Value, carrito, Ahora.AddMinutes(10));

        Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
        Assert.Equal(EstadoDelCheckout.Cancelado, checkout.Estado);
        Assert.Equal(Ahora.AddMinutes(10), checkout.ReservaALiberarDesde);
        Assert.Equal([new EdicionRetirada(Edicion(2), true)], resultado.Retiradas);
        Assert.Equal(EfectoDeTransicion.SinCambios, checkout.CancelarPorIntencionCerrada(checkout.PedidoId!.Value, carrito, Ahora.AddMinutes(11)).Efecto);

        var (enPago, carritoE) = EnPago();
        Assert.Equal(EfectoDeTransicion.Aplicada, enPago.CancelarPorIntencionCerrada(enPago.PedidoId!.Value, carritoE, Ahora.AddMinutes(1)).Efecto);
        var (reservando, carritoR) = Reservando();
        Assert.Equal(EfectoDeTransicion.AunNo, reservando.CancelarPorIntencionCerrada(reservando.PedidoId!.Value, carritoR, Ahora).Efecto);
    }
}
