using System.Net;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T28: la Presentación del checkout (<c>/checkout</c>; CAR-16, CAR-17, CAR-26, CAR-27, CAR-35; AC-CAR-13, AC-CAR-17, AC-CAR-27, AC-CAR-31): el esquema estricto de cada cuerpo, el <c>pedidoId</c> oculto, la verificación de persona que <b>falla cerrada</b> al pasar al
/// pago, las redirecciones (<c>/checkout/pago</c>, su estado y la revisión), la traducción de los errores <c>ECO-…</c> y los identificadores que no se admiten de afuera con su acceso denegado. Sin la Aplicación ni PostgreSQL.
/// </summary>
public sealed class CheckoutApiTests : IAsyncLifetime
{
    private static readonly Guid Pedido = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    private CarritoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await CarritoApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static KeyValuePair<string, string> C(string nombre, string valor) => new(nombre, valor);

    private static KeyValuePair<string, string>[] Aceptaciones(params KeyValuePair<string, string>[] otros) =>
        [C("terminosVersion", "t-1"), C("privacidadVersion", "p-1"), C("totalVisto", "132.00"), .. otros];

    // ---------- Iniciar y ver ----------

    [Fact]
    public async Task Iniciar_llama_a_la_Aplicacion_sin_campos_y_redirige_a_checkout()
    {
        using var respuesta = await _host.Enviar("/checkout/iniciar", [], conSesion: true);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/checkout", respuesta.Headers.Location?.OriginalString);
        Assert.IsType<IniciarCheckoutEntrada>(Assert.Single(_host.Checkout.Entradas));
    }

    [Fact]
    public async Task Iniciar_ya_no_admite_una_sucursal_porque_cada_linea_dice_la_suya()
    {
        using var respuesta = await _host.Enviar("/checkout/iniciar", [C("sucursalPreferida", "sede-central")], conSesion: true);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Checkout.Llamadas);
    }

    [Fact]
    public async Task GET_checkout_con_un_error_lo_traduce_y_con_parametros_de_consulta_es_validacion()
    {
        _host.Checkout.RespuestaDeVista = Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "checkout-no-encontrado"));

        using var sinCheckout = await _host.Cliente.SendAsync(Pedir(HttpMethod.Get, "/checkout"));
        using var conConsulta = await _host.Cliente.SendAsync(Pedir(HttpMethod.Get, "/checkout?x=1"));

        Assert.Equal(HttpStatusCode.NotFound, sinCheckout.StatusCode);
        Assert.Equal("ECO-NO-ENCONTRADO", (await CarritoApiHost.Leer(sinCheckout)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, conConsulta.StatusCode);
        Assert.Equal(["obtener"], _host.Checkout.Llamadas);
    }

    private static HttpRequestMessage Pedir(HttpMethod metodo, string ruta)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta);
        solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", CarritoApiHost.UsuarioPorOmision.ToString("D"));
        return solicitud;
    }

    // ---------- Entrega ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-08")]
    public async Task Elegir_recojo_manda_solo_el_tipo_sin_sucursal_ni_contacto_y_una_sucursal_es_validacion()
    {
        using var respuesta = await _host.Enviar("/checkout/entrega", [C("tipoDeEntrega", "recojo"), C("pedidoId", Pedido.ToString("D"))], conSesion: true);
        using var conSucursal = await _host.Enviar("/checkout/entrega", [C("tipoDeEntrega", "recojo"), C("sucursal", "sede-central")], conSesion: true);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conSucursal.StatusCode);
        var entrada = Assert.IsType<ElegirEntregaEntrada>(Assert.Single(_host.Checkout.Entradas));
        Assert.Equal((TipoDeEntregaDeCompra.Recojo, (Guid?)Pedido), (entrada.Tipo, entrada.PedidoId));
        Assert.Null(entrada.Contacto);
    }

    [Fact]
    public async Task Elegir_domicilio_manda_el_contacto_que_no_se_imprime()
    {
        using var respuesta = await _host.Enviar(
            "/checkout/entrega",
            [C("tipoDeEntrega", "domicilio"), C("direccion", "Jr. Lima 123"), C("region", "ayacucho"), C("nombre", "Ana Pérez"), C("telefono", "987654321")],
            conSesion: true);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        var entrada = Assert.IsType<ElegirEntregaEntrada>(Assert.Single(_host.Checkout.Entradas));
        Assert.Equal(TipoDeEntregaDeCompra.Domicilio, entrada.Tipo);
        Assert.Equal(("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho"), (entrada.Contacto!.Nombre, entrada.Contacto.Telefono, entrada.Contacto.Direccion, entrada.Contacto.Region));
        Assert.DoesNotContain("Ana", entrada.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", entrada.Contacto.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("domiciliooo")]
    [InlineData("")]
    public async Task Un_tipo_de_entrega_invalido_es_validacion(string tipo)
    {
        using var respuesta = await _host.Enviar("/checkout/entrega", [C("tipoDeEntrega", tipo)], conSesion: true);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Checkout.Llamadas);
    }

    [Theory]
    [InlineData("recojo", "nombre")]
    [InlineData("digital", "direccion")]
    [InlineData("domicilio", "sucursal")]
    public async Task Un_campo_que_no_corresponde_al_tipo_de_entrega_se_rechaza_en_vez_de_ignorarse(string tipo, string campo)
    {
        using var respuesta = await _host.Enviar("/checkout/entrega", [C("tipoDeEntrega", tipo), C(campo, "x")], conSesion: true);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Checkout.Llamadas);
    }

    // ---------- Pasar al pago ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task Pasar_al_pago_registra_las_aceptaciones_y_despues_paga_y_redirige_a_la_pantalla_de_Pagos()
    {
        using var respuesta = await _host.Enviar(
            "/checkout",
            Aceptaciones(C("pedidoId", Pedido.ToString("D")), C("consentimientoDeWhatsApp", "true"), C("huellaDelNumero", "***321"), C("cf-turnstile-response", "token-de-prueba")),
            conSesion: true);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/checkout/pago", respuesta.Headers.Location?.OriginalString);
        Assert.Equal(["aceptaciones", "pagar"], _host.Checkout.Llamadas);
        var aceptaciones = Assert.IsType<RegistrarAceptacionesEntrada>(_host.Checkout.Entradas[0]);
        Assert.Equal([(TextoLegalDeCompra.Terminos, "t-1"), (TextoLegalDeCompra.Privacidad, "p-1")], aceptaciones.Aceptados.Select(a => (a.Texto, a.VersionVista)));
        Assert.Equal((true, "***321", Pedido), (aceptaciones.ConsentimientoDeWhatsApp, aceptaciones.HuellaEnmascaradaDelNumero, aceptaciones.PedidoId));
        var pago = Assert.IsType<PasarAlPagoEntrada>(_host.Checkout.Entradas[1]);
        Assert.Equal((132.00m, Pedido), (pago.TotalVisto, pago.PedidoId));
        Assert.Equal([(AccionProtegida.Reserva, "token-de-prueba")], _host.Verificacion.Llamadas);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pasar_al_pago_sin_pedidoId_usa_el_checkout_vivo_y_el_token_de_verificacion_puede_ir_en_la_cabecera()
    {
        using var respuesta = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true, cabeceras: [("X-Verificacion-De-Persona", "token-de-cabecera")]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Null(Assert.IsType<PasarAlPagoEntrada>(_host.Checkout.Entradas[^1]).PedidoId);
        Assert.Equal("token-de-cabecera", Assert.Single(_host.Verificacion.Llamadas).Token);
    }

    [Fact]
    public async Task Con_el_pago_ya_en_curso_redirige_a_su_estado_y_con_una_revision_vuelve_a_checkout_sin_pagar()
    {
        _host.Checkout.RespuestaDePago = Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(Pedido, EstadoVisibleDelCheckout.EnPago, PagoEnCurso: true));
        using var enCurso = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true);
        _host.Checkout.RespuestaDePago = Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(null, EstadoVisibleDelCheckout.EnEdicion, Revision: CheckoutDelClienteFalso.VistaVacia with { CarritoCambio = true }));
        using var revision = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true);

        Assert.Equal("/checkout/pago/estado", enCurso.Headers.Location?.OriginalString);
        Assert.Equal("/checkout", revision.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(ResultadoDeExigencia.Requerida, 403, "ECO-VERIFICACION-REQUERIDA")]
    [InlineData(ResultadoDeExigencia.Fallida, 403, "ECO-VERIFICACION-FALLIDA")]
    [InlineData(ResultadoDeExigencia.NoDisponible, 503, "ECO-SERVICIO-NO-DISPONIBLE")]
    public async Task Sin_verificacion_de_persona_o_con_el_servicio_caido_no_se_llama_a_la_Aplicacion_y_falla_cerrado(ResultadoDeExigencia resultado, int estado, string codigo)
    {
        _host.Verificacion.Resultado = resultado;

        using var respuesta = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true);

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Equal(codigo, (await CarritoApiHost.Leer(respuesta)).GetProperty("code").GetString());
        Assert.Empty(_host.Checkout.Llamadas);
    }

    [Theory]
    [InlineData("132")]
    [InlineData("0.5")]
    [InlineData("9999999999.99")]
    public async Task Un_total_visto_valido_se_acepta(string total)
    {
        using var respuesta = await _host.Enviar("/checkout", [C("terminosVersion", "t-1"), C("privacidadVersion", "p-1"), C("totalVisto", total)], conSesion: true);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("132,00")]
    [InlineData("132.001")]
    [InlineData("-1")]
    [InlineData("12345678901")]
    [InlineData("")]
    public async Task Un_total_visto_invalido_es_validacion_y_no_se_registra_nada(string total)
    {
        using var respuesta = await _host.Enviar("/checkout", [C("terminosVersion", "t-1"), C("privacidadVersion", "p-1"), C("totalVisto", total)], conSesion: true);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Checkout.Llamadas);
    }

    [Fact]
    public async Task Falta_el_total_visto_una_version_con_caracteres_de_control_o_un_consentimiento_raro_son_validacion()
    {
        using var sinTotal = await _host.Enviar("/checkout", [C("terminosVersion", "t-1"), C("privacidadVersion", "p-1")], conSesion: true);
        using var versionRara = await _host.Enviar("/checkout", Aceptaciones(C("reembolsoDigitalVersion", "r\n1")), conSesion: true);
        using var consentimiento = await _host.Enviar("/checkout", Aceptaciones(C("consentimientoDeWhatsApp", "quizas")), conSesion: true);

        Assert.All(new[] { sinTotal, versionRara, consentimiento }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Empty(_host.Checkout.Llamadas);
    }

    [Fact]
    public async Task Un_error_al_registrar_las_aceptaciones_se_traduce_y_no_se_llega_a_pagar()
    {
        _host.Checkout.RespuestaDeVista = Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "checkout-texto-cambio", [new ErrorDeCampo("texto", "terminos")]));

        using var respuesta = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("ECO-CHECKOUT-TEXTO-CAMBIO", (await CarritoApiHost.Leer(respuesta)).GetProperty("code").GetString());
        Assert.Equal(["aceptaciones"], _host.Checkout.Llamadas);
    }

    [Theory]
    [InlineData("checkout-reserva-rechazada", CategoriaDeError.Conflicto, 409, "ECO-CHECKOUT-RESERVA-RECHAZADA")]
    [InlineData("checkout-stock-no-validable", CategoriaDeError.NoDisponible, 503, "ECO-CHECKOUT-STOCK-NO-VALIDABLE")]
    [InlineData("checkout-cuenta-no-puede-comprar", CategoriaDeError.Prohibido, 403, "ECO-CHECKOUT-CUENTA-NO-PUEDE-COMPRAR")]
    [InlineData("checkout-aceptacion-requerida", CategoriaDeError.Validacion, 422, "ECO-CHECKOUT-ACEPTACION-REQUERIDA")]
    public async Task Los_errores_de_pasar_al_pago_se_traducen_a_su_codigo_y_estado(string clave, CategoriaDeError categoria, int estado, string codigo)
    {
        _host.Checkout.RespuestaDePago = Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true);

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Equal(codigo, (await CarritoApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    // ---------- Volver y reiniciar ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-17")]
    public async Task Volver_y_reiniciar_llaman_a_la_Aplicacion_con_el_pedidoId_oculto_o_sin_el_y_redirigen_al_carrito()
    {
        using var volver = await _host.Enviar("/checkout/volver", [C("pedidoId", Pedido.ToString("D"))], conSesion: true);
        using var reiniciar = await _host.Enviar("/checkout/reiniciar", conSesion: true);

        Assert.Equal(["volver", "reiniciar"], _host.Checkout.Llamadas);
        Assert.Equal(Pedido, ((SalirDelCheckoutEntrada)_host.Checkout.Entradas[0]).PedidoId);
        Assert.Null(((SalirDelCheckoutEntrada)_host.Checkout.Entradas[1]).PedidoId);
        Assert.Equal("/carrito", volver.Headers.Location?.OriginalString);
        Assert.Equal("/carrito", reiniciar.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("checkout-pago-pendiente", CategoriaDeError.Conflicto, 409, "ECO-CHECKOUT-PAGO-PENDIENTE")]
    [InlineData("checkout-en-curso", CategoriaDeError.Conflicto, 409, "ECO-CHECKOUT-EN-CURSO")]
    [InlineData("no-encontrado", CategoriaDeError.NoEncontrado, 404, "ECO-NO-ENCONTRADO")]
    public async Task Los_errores_de_volver_se_traducen(string clave, CategoriaDeError categoria, int estado, string codigo)
    {
        _host.Checkout.RespuestaDeSalida = Resultado<VistaDelCarrito>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Enviar("/checkout/volver", conSesion: true);

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Equal(codigo, (await CarritoApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    // ---------- Esquema estricto y antifalsificación ----------

    [Theory]
    [InlineData("/checkout/iniciar")]
    [InlineData("/checkout/entrega")]
    [InlineData("/checkout")]
    [InlineData("/checkout/volver")]
    [InlineData("/checkout/reiniciar")]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Un_identificador_que_no_se_admite_de_afuera_se_rechaza_y_registra_un_acceso_denegado_en_cada_accion(string ruta)
    {
        using var respuesta = await _host.Enviar(ruta, [C("checkoutId", Guid.NewGuid().ToString("D")), C("tipoDeEntrega", "recojo"), .. Aceptaciones()], conSesion: true);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Checkout.Llamadas);
        Assert.Equal([(CategoriaDeError.Prohibido, "recurso-ajeno")], _host.Denegaciones.Registradas);
    }

    [Theory]
    [InlineData("/checkout/iniciar")]
    [InlineData("/checkout/entrega")]
    [InlineData("/checkout")]
    [InlineData("/checkout/volver")]
    [InlineData("/checkout/reiniciar")]
    public async Task Todo_POST_exige_el_token_antifalsificacion(string ruta)
    {
        using var respuesta = await _host.Enviar(ruta, [C("tipoDeEntrega", "recojo"), .. Aceptaciones()], conSesion: true, conToken: false);

        Assert.False(respuesta.IsSuccessStatusCode || respuesta.StatusCode == HttpStatusCode.SeeOther, ruta);
        Assert.Empty(_host.Checkout.Llamadas);
        Assert.Empty(_host.Verificacion.Llamadas);
    }

    [Fact]
    public async Task Un_campo_desconocido_o_repetido_en_pasar_al_pago_se_rechaza()
    {
        using var desconocido = await _host.Enviar("/checkout", Aceptaciones(C("descuento", "50")), conSesion: true);
        using var repetido = await _host.Enviar("/checkout", Aceptaciones(C("totalVisto", "1.00")), conSesion: true);

        Assert.All(new[] { desconocido, repetido }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Empty(_host.Checkout.Llamadas);
    }

    [Fact]
    public async Task Con_htmx_un_exito_responde_HX_Redirect_al_destino()
    {
        using var respuesta = await _host.Enviar("/checkout", Aceptaciones(), conSesion: true, htmx: true);

        Assert.Equal("/checkout/pago", respuesta.Headers.GetValues("HX-Redirect").Single());
    }
}
