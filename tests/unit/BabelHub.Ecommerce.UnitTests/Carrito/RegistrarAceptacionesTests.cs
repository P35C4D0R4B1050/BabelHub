using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using TextoLegal = BabelHub.Ecommerce.Domain.Carrito.TextoLegal;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T16: <c>RegistrarAceptaciones</c> y <c>ObtenerCheckout</c> (CAR-12, CAR-13, CAR-18, CAR-22, CAR-28; AC-CAR-11, AC-CAR-15, AC-CAR-16, AC-CAR-20): compara las versiones vistas con las vigentes, exige términos y privacidad siempre y el reembolso
/// digital si hay digitales, guarda versión y fecha de cada texto, y ofrece el consentimiento de WhatsApp (solo con número, con su huella enmascarada) sin exigirlo y sin que una falla bloquee ni diga «guardado».
/// </summary>
public sealed class RegistrarAceptacionesTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout)> ConCheckout(bool conDigital)
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Impreso(10), 1, CodigoDeSucursal: Sucursal.Principal));
        if (conDigital)
        {
            await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Digital(), 1, CodigoDeSucursal: Sucursal.Principal));
        }

        Assert.True((await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada())).EsExito);
        return (e, e.Checkouts.Todos.Single());
    }

    private static TextoVistoDeCompra Visto(TextoLegalDeCompra texto, string version) => new(texto, version);

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];
    private static readonly TextoVistoDeCompra[] Los3 = [.. Los2, new(TextoLegalDeCompra.ReembolsoDigital, "r-1")];

    private static Task<Resultado<VistaDelCheckout>> Registrar(EntornoDelCarrito e, IReadOnlyList<TextoVistoDeCompra> aceptados, bool? whatsapp = null, string? huella = null, Guid? pedido = null) =>
        new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(aceptados, whatsapp, huella, pedido));

    // ---------- Textos ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-20")]
    [Trait("Criterio", "AC-COM-13")]
    public async Task Aceptar_los_tres_textos_guarda_la_version_y_la_fecha_de_cada_uno()
    {
        var (e, checkout) = await ConCheckout(conDigital: true);

        var resultado = await Registrar(e, Los3);

        Assert.True(resultado.EsExito);
        Assert.Equal(3, checkout.Aceptaciones.Aceptadas.Count);
        Assert.All(checkout.Aceptaciones.Aceptadas, a => Assert.Equal(Inicio, a.AceptadoEn));
        Assert.Equal(["t-1", "p-1", "r-1"], checkout.Aceptaciones.Aceptadas.Select(a => a.Version));
        Assert.Empty(resultado.Valor.TextosPorAceptar);
        Assert.Equal(3, resultado.Valor.Aceptaciones.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-15")]
    public async Task Sin_los_terminos_y_la_privacidad_no_se_puede_continuar_en_ningun_carrito()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);

        var resultado = await Registrar(e, [Visto(TextoLegalDeCompra.Terminos, "t-1")]);

        Assert.Equal(ClavesDelCarrito.AceptacionRequerida, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error.Categoria);
        Assert.Contains(new ErrorDeCampo("privacidad", ConstructorDeValidacion.Obligatorio), resultado.Error.Errores);
        Assert.Equal([TextoLegal.Privacidad], checkout.TextosPorAceptar());
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-11")]
    public async Task Con_un_libro_digital_tambien_se_exige_el_reembolso_digital()
    {
        var (e, checkout) = await ConCheckout(conDigital: true);

        var resultado = await Registrar(e, Los2);

        Assert.Equal(ClavesDelCarrito.AceptacionRequerida, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("reembolsoDigital", ConstructorDeValidacion.Obligatorio), resultado.Error.Errores);
        Assert.Equal([TextoLegal.ReembolsoDigital], checkout.TextosPorAceptar());
    }

    [Fact]
    public async Task Sin_digitales_el_reembolso_no_se_exige_ni_se_guarda_aunque_se_envie()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);

        var resultado = await Registrar(e, Los3);

        Assert.True(resultado.EsExito);
        Assert.Empty(resultado.Valor.TextosPorAceptar);
        Assert.DoesNotContain(checkout.Aceptaciones.Aceptadas, a => a.Texto == TextoLegal.ReembolsoDigital);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-20")]
    public async Task Si_la_version_vista_ya_no_es_la_vigente_se_rechaza_con_texto_cambio_y_se_pide_aceptar_de_nuevo()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);
        e.Textos.Vigente(TipoDeTextoLegal.Terminos, "t-2");

        var resultado = await Registrar(e, Los2);

        Assert.Equal(ClavesDelCarrito.TextoCambio, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error.Categoria);
        Assert.Contains(new ErrorDeCampo("terminos", "t-2"), resultado.Error.Errores);
        Assert.DoesNotContain(checkout.Aceptaciones.Aceptadas, a => a.Texto == TextoLegal.Terminos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-20")]
    public async Task Una_aceptacion_guardada_de_una_version_que_cambio_se_invalida_y_hay_que_volver_a_aceptar()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);
        await Registrar(e, Los2);
        e.Textos.Vigente(TipoDeTextoLegal.Privacidad, "p-2");

        var resultado = await Registrar(e, [Visto(TextoLegalDeCompra.Terminos, "t-1")]);

        Assert.Equal(ClavesDelCarrito.AceptacionRequerida, resultado.Error!.Clave);
        Assert.Equal([TextoLegal.Privacidad], checkout.TextosPorAceptar());
        Assert.Equal(["t-1"], checkout.Aceptaciones.Aceptadas.Select(a => a.Version));
    }

    [Fact]
    public async Task Si_no_se_pueden_obtener_las_versiones_vigentes_falla_cerrado_y_no_se_guarda_nada()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);
        e.Textos.Caido = true;

        var resultado = await Registrar(e, Los2);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(checkout.Aceptaciones.Aceptadas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Una_version_vacia_es_una_validacion_que_no_repite_el_valor(string version)
    {
        var (e, _) = await ConCheckout(conDigital: false);

        var resultado = await Registrar(e, [Visto(TextoLegalDeCompra.Terminos, version), Visto(TextoLegalDeCompra.Privacidad, "p-1")]);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("aceptados", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Equal(0, e.Textos.Consultas);
    }

    // ---------- WhatsApp ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-16")]
    public async Task Sin_marcar_el_consentimiento_se_puede_continuar_y_no_se_guarda_nada()
    {
        var (e, _) = await ConCheckout(conDigital: false);

        var resultado = await Registrar(e, Los2, whatsapp: null);

        Assert.True(resultado.EsExito);
        Assert.Empty(e.Consentimiento.Guardados);
        Assert.Equal(EstadoDelConsentimientoDeCompra.SeOfrece, resultado.Valor.ConsentimientoDeWhatsApp!.Estado);
    }

    [Fact]
    public async Task Marcado_el_consentimiento_se_guarda_con_la_huella_que_se_mostro_y_nunca_con_el_numero()
    {
        var (e, _) = await ConCheckout(conDigital: false);
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;

        var resultado = await Registrar(e, Los2, whatsapp: true, huella: "•••• •• 321");

        Assert.Equal(EstadoDelConsentimientoDeCompra.Guardado, resultado.Valor.ConsentimientoDeWhatsApp!.Estado);
        Assert.Equal((cuenta, "•••• •• 321"), Assert.Single(e.Consentimiento.Guardados));
    }

    [Fact]
    public async Task Si_el_guardado_del_consentimiento_falla_el_registro_sigue_y_no_dice_guardado()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);
        e.Consentimiento.GuardaBien = false;

        var resultado = await Registrar(e, Los2, whatsapp: true, huella: "•••• •• 321");

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelConsentimientoDeCompra.NoGuardado, resultado.Valor.ConsentimientoDeWhatsApp!.Estado);
        Assert.Equal(2, checkout.Aceptaciones.Aceptadas.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Marcarlo_sin_la_huella_mostrada_no_se_pide_a_Identidad(string? huella)
    {
        var (e, _) = await ConCheckout(conDigital: false);

        var resultado = await Registrar(e, Los2, whatsapp: true, huella: huella);

        Assert.Equal(EstadoDelConsentimientoDeCompra.NoGuardado, resultado.Valor.ConsentimientoDeWhatsApp!.Estado);
        Assert.Empty(e.Consentimiento.Guardados);
    }

    [Fact]
    public async Task Si_la_cuenta_no_puede_comprar_el_consentimiento_no_se_pide_aunque_se_haya_marcado()
    {
        var (e, _) = await ConCheckout(conDigital: false);
        e.Cuenta.NoPuedeComprar = true;

        var resultado = await Registrar(e, Los2, whatsapp: true, huella: "•••• •• 321");

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelConsentimientoDeCompra.NoGuardado, resultado.Valor.ConsentimientoDeWhatsApp!.Estado);
        Assert.Empty(e.Consentimiento.Guardados);
    }

    [Fact]
    public async Task Si_Identidad_ya_tiene_el_consentimiento_o_no_hay_numero_no_se_ofrece_ni_se_guarda()
    {
        var (e, _) = await ConCheckout(conDigital: false);
        e.Consentimiento.Oferta = new OfertaDeConsentimiento(EstadoDeLaOfertaDeConsentimiento.YaConsiente, null);
        var ya = await Registrar(e, Los2, whatsapp: true, huella: "x");
        e.Consentimiento.Oferta = OfertaDeConsentimiento.NoSeOfrece;
        var sinNumero = await Registrar(e, Los2, whatsapp: true, huella: "x");

        Assert.Equal(EstadoDelConsentimientoDeCompra.YaConsiente, ya.Valor.ConsentimientoDeWhatsApp!.Estado);
        Assert.Equal(EstadoDelConsentimientoDeCompra.NoSeOfrece, sinNumero.Valor.ConsentimientoDeWhatsApp!.Estado);
        Assert.Empty(e.Consentimiento.Guardados);
    }

    // ---------- El checkout vivo ----------

    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout)> EnPago()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);
        await Registrar(e, Los2);
        var carrito = e.Carritos.Todos.Single();
        checkout.ElegirRecojo(Inicio);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, carrito, Inicio).Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito).Efecto);
        return (e, checkout);
    }

    [Fact]
    public async Task Con_el_checkout_en_pago_aceptar_lo_mismo_es_un_no_op_y_un_texto_distinto_reabre_la_intencion()
    {
        var (e, checkout) = await EnPago();

        var igual = await Registrar(e, Los2);
        Assert.True(igual.EsExito);
        Assert.Equal(0, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);

        e.Textos.Vigente(TipoDeTextoLegal.Terminos, "t-2");
        var cambio = await Registrar(e, [Visto(TextoLegalDeCompra.Terminos, "t-2"), Visto(TextoLegalDeCompra.Privacidad, "p-1")]);

        Assert.True(cambio.EsExito);
        Assert.Equal(1, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal("t-2", checkout.Aceptaciones.Aceptadas.Single(a => a.Texto == TextoLegal.Terminos).Version);
        Assert.Contains(checkout, e.Checkouts.Guardados);
    }

    [Fact]
    public async Task Con_el_pago_pendiente_o_un_intento_activo_se_rechaza()
    {
        var (activo, _) = await EnPago();
        activo.Pagos.AlReabrir = ResultadoDeReabrirIntencion.NoCancelable;
        activo.Textos.Vigente(TipoDeTextoLegal.Terminos, "t-2");
        var (pendiente, checkoutPendiente) = await EnPago();
        pendiente.Textos.Vigente(TipoDeTextoLegal.Terminos, "t-2");
        checkoutPendiente.PagoPendiente(checkoutPendiente.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);
        TextoVistoDeCompra[] nuevos = [Visto(TextoLegalDeCompra.Terminos, "t-2"), Visto(TextoLegalDeCompra.Privacidad, "p-1")];

        Assert.Equal(ClavesDelCarrito.PagoPendiente, (await Registrar(activo, nuevos)).Error!.Clave);
        Assert.Equal(ClavesDelCarrito.PagoPendiente, (await Registrar(pendiente, nuevos)).Error!.Clave);
        Assert.Equal(0, pendiente.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Sin_sesion_con_personal_o_con_la_cuenta_cancelada_se_rechaza()
    {
        var visitante = EntornoDelCarrito.DeVisitante();
        var encargado = new EntornoDelCarrito(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var (cancelada, _) = await ConCheckout(conDigital: false);
        cancelada.Cuenta.Cancelada = true;

        Assert.Equal(CategoriaDeError.NoAutenticado, (await Registrar(visitante, Los2)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Registrar(encargado, Los2)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Registrar(cancelada, Los2)).Error!.Categoria);
    }

    // ---------- ObtenerCheckout ----------

    [Fact]
    public async Task Obtener_el_checkout_no_tiene_efectos_y_ofrece_el_consentimiento_con_la_huella_enmascarada()
    {
        var (e, checkout) = await ConCheckout(conDigital: false);
        var guardados = e.Checkouts.Guardados.Count;

        var resultado = await new ObtenerCheckout(e.ServiciosDelCheckout()).Ejecutar(new ObtenerCheckoutEntrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.SeOfrece, "•••• •• 321"), resultado.Valor.ConsentimientoDeWhatsApp);
        Assert.Equal(guardados, e.Checkouts.Guardados.Count);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
    }

    [Fact]
    public async Task Obtener_el_checkout_con_Identidad_caida_no_ofrece_el_consentimiento_pero_muestra_el_checkout()
    {
        var (e, _) = await ConCheckout(conDigital: false);
        e.Consentimiento.Oferta = OfertaDeConsentimiento.NoSeOfrece;

        var resultado = await new ObtenerCheckout(e.ServiciosDelCheckout()).Ejecutar(new ObtenerCheckoutEntrada());

        Assert.Equal(EstadoDelConsentimientoDeCompra.NoSeOfrece, resultado.Valor.ConsentimientoDeWhatsApp!.Estado);
    }
}
