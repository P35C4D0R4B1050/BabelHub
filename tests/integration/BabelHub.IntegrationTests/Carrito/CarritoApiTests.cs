using System.Net;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T27: la Presentación del carrito (<c>/carrito</c>; CAR-16, CAR-35; AC-CAR-01, AC-CAR-13, AC-CAR-31): la cookie <c>__Host-bh-carrito</c> con sus atributos y su rotación, el token antifalsificación en todo <c>POST</c>, el esquema estricto (campos
/// desconocidos o repetidos, identificadores que no se admiten de afuera con su acceso denegado), la traducción de los errores a <c>application/problem+json</c> con su código <c>ECO-…</c> y la redirección tras un éxito. Sin la Aplicación ni PostgreSQL.
/// </summary>
public sealed class CarritoApiTests : IAsyncLifetime
{
    private static readonly string CookieValida = new('A', 43);

    private static readonly Guid Edicion = Guid.Parse("e0000000-0000-0000-0000-000000000001");

    private CarritoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await CarritoApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static KeyValuePair<string, string> C(string nombre, string valor) => new(nombre, valor);

    // ---------- La cookie ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-01")]
    public async Task La_cookie_nueva_del_visitante_se_escribe_con_los_atributos_de_un_prefijo_Host_y_sin_Domain()
    {
        _host.Carrito.Respuesta = Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(new VistaDelCarrito([], 0m, [], false), CookieValida, false));

        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "2")]);

        var cookie = Assert.Single(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-bh-carrito=", StringComparison.Ordinal));
        var atributos = cookie.Split(';').Select(a => a.Trim()).ToList();
        Assert.Equal($"__Host-bh-carrito={CookieValida}", atributos[0]);
        Assert.Contains("path=/", atributos, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("secure", atributos, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("httponly", atributos, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", atributos, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(atributos, a => a.StartsWith("max-age=2592000", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(atributos, a => a.StartsWith("domain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cuando_la_Aplicacion_pide_borrar_la_cookie_se_expira()
    {
        _host.Carrito.Respuesta = Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(new VistaDelCarrito([], 0m, [], false), null, true));

        using var respuesta = await _host.Enviar($"/carrito/lineas/{Edicion:D}/quitar", cookieDelCarrito: CookieValida);

        var cookie = Assert.Single(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-bh-carrito=;", StringComparison.Ordinal) || c.StartsWith("__Host-bh-carrito=\"\"", StringComparison.Ordinal) || c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public async Task Una_cookie_mal_formada_se_trata_como_sin_carrito_y_no_llega_a_la_Aplicacion(string cookie)
    {
        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], cookieDelCarrito: cookie);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        var entrada = Assert.IsType<AgregarLineaEntrada>(Assert.Single(_host.Carrito.Entradas));
        Assert.Null(entrada.CookieDelVisitante);
    }

    [Fact]
    public async Task Una_cookie_valida_llega_a_la_Aplicacion_y_no_se_imprime()
    {
        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "3")], cookieDelCarrito: CookieValida);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        var entrada = Assert.IsType<AgregarLineaEntrada>(Assert.Single(_host.Carrito.Entradas));
        Assert.Equal((Edicion, 3, CookieValida), (entrada.EdicionId, entrada.Cantidad, entrada.CookieDelVisitante));
        Assert.DoesNotContain(CookieValida, entrada.ToString(), StringComparison.Ordinal);
    }

    // ---------- Antifalsificación y redirección ----------

    [Fact]
    public async Task Todo_POST_exige_el_token_antifalsificacion_y_sin_el_no_llega_a_la_Aplicacion()
    {
        foreach (var ruta in new[] { "/carrito/lineas", $"/carrito/lineas/{Edicion:D}", $"/carrito/lineas/{Edicion:D}/quitar" })
        {
            using var respuesta = await _host.Enviar(ruta, [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], conToken: false);

            Assert.False(respuesta.IsSuccessStatusCode || respuesta.StatusCode == HttpStatusCode.SeeOther, ruta);
        }

        Assert.Empty(_host.Carrito.Llamadas);
    }

    [Fact]
    public async Task Un_exito_redirige_con_303_a_carrito_y_con_htmx_responde_HX_Redirect()
    {
        using var normal = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")]);
        using var htmx = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], htmx: true);

        Assert.Equal(HttpStatusCode.SeeOther, normal.StatusCode);
        Assert.Equal("/carrito", normal.Headers.Location?.OriginalString);
        Assert.True(htmx.Headers.Contains("HX-Redirect"));
        Assert.Equal("/carrito", htmx.Headers.GetValues("HX-Redirect").Single());
        Assert.Contains("no-store", normal.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cambiar_y_quitar_llaman_a_la_Aplicacion_con_la_edicion_de_la_ruta()
    {
        using var cambio = await _host.Enviar($"/carrito/lineas/{Edicion:D}", [C("cantidad", "5")]);
        using var quita = await _host.Enviar($"/carrito/lineas/{Edicion:D}/quitar");

        Assert.Equal([HttpStatusCode.SeeOther, HttpStatusCode.SeeOther], [cambio.StatusCode, quita.StatusCode]);
        Assert.Equal(["cambiar", "quitar"], _host.Carrito.Llamadas);
        Assert.Equal((Edicion, 5), (((ModificarLineaEntrada)_host.Carrito.Entradas[0]).EdicionId, ((ModificarLineaEntrada)_host.Carrito.Entradas[0]).Cantidad));
        Assert.Equal(Edicion, ((EliminarLineaEntrada)_host.Carrito.Entradas[1]).EdicionId);
    }

    // ---------- Esquema estricto ----------

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData("1e1")]
    [InlineData(" 2")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Una_cantidad_fuera_de_1_a_99_o_con_otra_forma_es_validacion_y_no_llega_a_la_Aplicacion(string cantidad)
    {
        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", cantidad)]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await CarritoApiHost.Leer(respuesta);
        Assert.Equal("ECO-VALIDACION-INVALIDA", problema.GetProperty("code").GetString());
        Assert.Empty(_host.Carrito.Llamadas);
    }

    [Theory]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("e00000000000000000000000000000001")]
    public async Task Una_edicion_que_no_es_un_GUID_valido_es_validacion(string edicion)
    {
        using var enCuerpo = await _host.Enviar("/carrito/lineas", [C("edicionId", edicion), C("cantidad", "1")]);
        using var enRuta = await _host.Enviar($"/carrito/lineas/{edicion}/quitar");

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], [enCuerpo.StatusCode, enRuta.StatusCode]);
        Assert.Empty(_host.Carrito.Llamadas);
    }

    [Fact]
    public async Task Un_campo_desconocido_o_repetido_se_rechaza_y_nunca_se_ignora_en_silencio()
    {
        using var desconocido = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1"), C("precio", "0.01")]);
        using var repetido = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1"), C("cantidad", "2")]);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], [desconocido.StatusCode, repetido.StatusCode]);
        Assert.Empty(_host.Carrito.Llamadas);
        var campos = (await CarritoApiHost.Leer(desconocido)).GetProperty("errores").EnumerateArray().Select(e => e.GetProperty("campo").GetString()).ToList();
        Assert.Contains("precio", campos);
    }

    [Fact]
    public async Task Un_nombre_de_campo_con_caracteres_raros_no_se_repite_en_el_error()
    {
        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1"), C("<script>x</script>", "1")]);

        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", texto, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("carritoId")]
    [InlineData("checkoutId")]
    [InlineData("cuentaId")]
    [InlineData("usuarioId")]
    [InlineData("CuentaId")]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Un_identificador_que_no_se_admite_de_afuera_en_el_cuerpo_se_rechaza_y_registra_un_acceso_denegado(string campo)
    {
        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1"), C(campo, Guid.NewGuid().ToString("D"))], conSesion: true);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Carrito.Llamadas);
        Assert.Equal([(CategoriaDeError.Prohibido, "recurso-ajeno")], _host.Denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Un_identificador_prohibido_en_la_consulta_o_cualquier_parametro_de_consulta_se_rechaza()
    {
        using var identificador = await _host.Enviar($"/carrito/lineas?cuentaId={Guid.NewGuid():D}", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")]);
        using var otro = await _host.Enviar("/carrito/lineas?x=1", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")]);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], [identificador.StatusCode, otro.StatusCode]);
        Assert.Empty(_host.Carrito.Llamadas);
        Assert.Single(_host.Denegaciones.Registradas);
    }

    [Fact]
    public async Task Un_cuerpo_JSON_estricto_se_acepta_y_uno_con_un_campo_extra_o_mal_formado_no()
    {
        using var bueno = await _host.Enviar("/carrito/lineas", cuerpo: new StringContent($"{{\"edicionId\":\"{Edicion:D}\",\"cantidad\":2}}", System.Text.Encoding.UTF8, "application/json"));
        using var extra = await _host.Enviar("/carrito/lineas", cuerpo: new StringContent($"{{\"edicionId\":\"{Edicion:D}\",\"cantidad\":2,\"otro\":1}}", System.Text.Encoding.UTF8, "application/json"));
        using var malo = await _host.Enviar("/carrito/lineas", cuerpo: new StringContent("{no es json", System.Text.Encoding.UTF8, "application/json"));
        using var anidado = await _host.Enviar("/carrito/lineas", cuerpo: new StringContent($"{{\"edicionId\":\"{Edicion:D}\",\"cantidad\":{{\"x\":1}}}}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.SeeOther, bueno.StatusCode);
        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], [extra.StatusCode, malo.StatusCode, anidado.StatusCode]);
        Assert.Single(_host.Carrito.Llamadas);
    }

    // ---------- Traducción de errores ----------

    [Theory]
    [InlineData("carrito-sin-stock", CategoriaDeError.Conflicto, 409, "ECO-CARRITO-SIN-STOCK")]
    [InlineData("carrito-limite-de-lineas", CategoriaDeError.Validacion, 422, "ECO-CARRITO-LIMITE-DE-LINEAS")]
    [InlineData("digital-ya-poseida", CategoriaDeError.Conflicto, 409, "ECO-DIGITAL-YA-POSEIDA")]
    [InlineData("edicion-no-disponible", CategoriaDeError.Conflicto, 409, "ECO-EDICION-NO-DISPONIBLE")]
    [InlineData("checkout-pago-pendiente", CategoriaDeError.Conflicto, 409, "ECO-CHECKOUT-PAGO-PENDIENTE")]
    [InlineData("checkout-en-curso", CategoriaDeError.Conflicto, 409, "ECO-CHECKOUT-EN-CURSO")]
    [InlineData("servicio-no-disponible", CategoriaDeError.NoDisponible, 503, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData("no-encontrado", CategoriaDeError.NoEncontrado, 404, "ECO-NO-ENCONTRADO")]
    public async Task Los_errores_de_la_Aplicacion_se_traducen_a_su_codigo_y_estado_y_la_respuesta_no_se_guarda(string clave, CategoriaDeError categoria, int estado, string codigo)
    {
        _host.Carrito.Respuesta = Resultado<ResultadoDeCarrito>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")]);

        Assert.Equal(estado, (int)respuesta.StatusCode);
        var problema = await CarritoApiHost.Leer(respuesta);
        Assert.Equal(codigo, problema.GetProperty("code").GetString());
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task El_limite_excedido_responde_429_con_Retry_After_y_el_codigo_estable()
    {
        var hasta = DateTimeOffset.UtcNow.AddMinutes(2);
        _host.Carrito.Respuesta = Resultado<ResultadoDeCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Limite, "limite-excedido", [new ErrorDeCampo("disponibleDesde", hasta.ToString("O", System.Globalization.CultureInfo.InvariantCulture))]));

        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")]);

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Equal("ECO-LIMITE-EXCEDIDO", (await CarritoApiHost.Leer(respuesta)).GetProperty("code").GetString());
        var espera = int.Parse(respuesta.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(espera, 100, 125);
    }

    [Fact]
    public async Task El_error_no_lleva_trazas_ni_la_cookie_ni_el_valor_recibido()
    {
        _host.Carrito.Respuesta = Resultado<ResultadoDeCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "carrito-sin-stock", [new ErrorDeCampo("maximo", "4")]));

        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "9")], cookieDelCarrito: CookieValida);

        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(CookieValida, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at BabelHub", texto, StringComparison.Ordinal);
        Assert.Contains("maximo", texto, StringComparison.Ordinal);
    }

    // ---------- Tope y tipo del cuerpo, y la consulta de un GET ----------

    [Fact]
    public async Task Un_cuerpo_de_mas_de_16_KiB_se_rechaza_con_413_antes_de_leerlo_y_nunca_llega_a_la_Aplicacion()
    {
        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1"), C("relleno", new string('x', 20_000))]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
        Assert.Empty(_host.Carrito.Llamadas);
    }

    [Fact]
    public async Task Solo_se_admite_formulario_urlencoded_o_JSON_y_un_multipart_o_texto_plano_es_415()
    {
        using var multipart = await _host.Enviar("/carrito/lineas", cuerpo: new MultipartFormDataContent { { new StringContent("1"), "cantidad" } });
        using var texto = await _host.Enviar("/carrito/lineas", cuerpo: new StringContent("cantidad=1", System.Text.Encoding.UTF8, "text/plain"));

        Assert.Equal([HttpStatusCode.UnsupportedMediaType, HttpStatusCode.UnsupportedMediaType], [multipart.StatusCode, texto.StatusCode]);
        Assert.Empty(_host.Carrito.Llamadas);
    }

    [Theory]
    [InlineData("carritoId")]
    [InlineData("checkoutId")]
    [InlineData("cuentaId")]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Un_GET_con_un_identificador_prohibido_en_la_consulta_se_rechaza_y_registra_el_acceso_denegado(string campo)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/carrito?{campo}={Guid.NewGuid():D}");

        using var respuesta = await _host.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal([(CategoriaDeError.Prohibido, "recurso-ajeno")], _host.Denegaciones.Registradas);
        Assert.Empty(_host.Carrito.Llamadas);
    }

    // ---------- Fusión aplazada ----------

    [Fact]
    public async Task Con_una_cuenta_y_la_cookie_de_un_visitante_cada_POST_reintenta_la_fusion_antes_de_la_accion_y_la_cookie_se_borra_solo_si_la_Aplicacion_lo_dice()
    {
        _host.Carrito.RespuestaDeFusion = Resultado<ResultadoDeFusionDeCarrito>.Exito(new ResultadoDeFusionDeCarrito(new VistaDelCarrito([], 0m, [], false), [], Aplazada: false, BorrarLaCookie: true));

        using var respuesta = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], conSesion: true, cookieDelCarrito: CookieValida);

        Assert.Equal(["fusionar", "agregar"], _host.Carrito.Llamadas);
        Assert.Contains(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-bh-carrito=", StringComparison.Ordinal) && c.Contains("1970", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_fusion_aplazada_no_borra_la_cookie_y_una_fusion_que_lanza_nunca_impide_la_accion()
    {
        _host.Carrito.RespuestaDeFusion = Resultado<ResultadoDeFusionDeCarrito>.Exito(new ResultadoDeFusionDeCarrito(new VistaDelCarrito([], 0m, [], false), [], Aplazada: true, BorrarLaCookie: false));
        using var aplazada = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], conSesion: true, cookieDelCarrito: CookieValida);
        _host.Carrito.LaFusionLanza = true;
        using var lanza = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], conSesion: true, cookieDelCarrito: CookieValida);

        Assert.False(aplazada.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-bh-carrito=", StringComparison.Ordinal)));
        Assert.Equal([HttpStatusCode.SeeOther, HttpStatusCode.SeeOther], [aplazada.StatusCode, lanza.StatusCode]);
        Assert.Equal(["fusionar", "agregar", "fusionar", "agregar"], _host.Carrito.Llamadas);
    }

    [Fact]
    public async Task Sin_sesion_o_sin_cookie_no_se_intenta_fusionar()
    {
        using var sinSesion = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], cookieDelCarrito: CookieValida);
        using var sinCookie = await _host.Enviar("/carrito/lineas", [C("edicionId", Edicion.ToString("D")), C("cantidad", "1")], conSesion: true);

        Assert.Equal(["agregar", "agregar"], _host.Carrito.Llamadas);
    }
}
