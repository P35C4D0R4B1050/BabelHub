using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T15: la Presentación del Libro de Reclamaciones del consumidor (REC-02, REC-04, REC-05, REC-10, REC-11, REC-14; AC-REC-02, AC-REC-03, AC-REC-09, AC-REC-12): el formulario con la clave de un solo uso y la verificación de persona, el esquema estricto del cuerpo, la constancia con
/// <c>no-store</c>, la lista de campos que faltan <b>sin el valor escrito</b>, la traducción de los errores (<c>ECO-REC-…</c>, verificación requerida, 429 con <c>Retry-After</c>, conflicto, no disponible), mis hojas con su paginación estricta y el número con formato en la ruta.
/// </summary>
public sealed class ReclamacionesApiTests : IAsyncLifetime
{
    private ReclamacionesApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ReclamacionesApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static KeyValuePair<string, string> C(string nombre, string valor) => new(nombre, valor);

    private static KeyValuePair<string, string>[] Hoja(params KeyValuePair<string, string>[] cambios)
    {
        var campos = new Dictionary<string, string>
        {
            ["nombre"] = "Ana Pérez Quispe",
            ["documento"] = "12345678",
            ["domicilio"] = "Jr. Lima 123, Ayacucho",
            ["correo"] = "ana@example.com",
            ["bienContratado"] = "Cien años de soledad",
            ["montoReclamado"] = "59.90",
            ["tipo"] = "reclamo",
            ["detalle"] = "Llegó con las páginas dañadas",
            ["claveDelFormulario"] = "clave-de-un-solo-uso",
            ["cf-turnstile-response"] = "token-de-verificacion",
        };
        foreach (var (nombre, valor) in cambios)
        {
            campos[nombre] = valor;
        }

        return [.. campos];
    }

    // ---------- Formulario ----------

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task El_formulario_trae_una_clave_nueva_en_cada_carga_la_verificacion_de_persona_y_no_se_guarda()
    {
        using var primera = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/nueva"));
        using var segunda = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/nueva"));

        var html = await primera.Content.ReadAsStringAsync();
        var claveA = Regex.Match(html, "name=\"claveDelFormulario\" value=\"([A-Za-z0-9_-]{22})\"").Groups[1].Value;
        var claveB = Regex.Match(await segunda.Content.ReadAsStringAsync(), "name=\"claveDelFormulario\" value=\"([A-Za-z0-9_-]{22})\"").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.NotEmpty(claveA);
        Assert.NotEqual(claveA, claveB);
        Assert.Contains("cf-turnstile", html, StringComparison.Ordinal);
        Assert.Contains("hoja-de-reclamacion", html, StringComparison.Ordinal);
        Assert.Contains("no-store", primera.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Empty(_host.Reclamaciones.Entradas);
    }

    [Fact]
    public async Task El_formulario_no_admite_parametros_de_consulta()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/nueva?x=1", html: false));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    // ---------- Registrar ----------

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public async Task Registrar_llama_al_modulo_con_los_campos_la_clave_y_el_token_y_muestra_la_constancia_sin_guardarla()
    {
        using var respuesta = await _host.Enviar(Hoja());

        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("2026-000001", html, StringComparison.Ordinal);
        Assert.Contains("04/12/2026", html, StringComparison.Ordinal);
        Assert.Contains("Ana Pérez", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var entrada = Assert.Single(_host.Reclamaciones.Entradas);
        Assert.Equal(("Ana Pérez Quispe", 59.90m, "reclamo", "clave-de-un-solo-uso", "token-de-verificacion"), (entrada.Nombre, entrada.MontoReclamado, entrada.Tipo, entrada.ClaveDelFormulario, entrada.TokenDeVerificacion));
        Assert.DoesNotContain("12345678", entrada.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_campo_desconocido_o_repetido_es_validacion_sin_llamar_al_modulo()
    {
        using var desconocido = await _host.Enviar([.. Hoja(), C("admin", "1")], html: false);

        Assert.Equal(HttpStatusCode.BadRequest, desconocido.StatusCode);
        Assert.Empty(_host.Reclamaciones.Entradas);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData("1.234")]
    public async Task Un_monto_con_mala_forma_es_validacion_sin_repetir_lo_escrito(string monto)
    {
        using var respuesta = await _host.Enviar(Hoja(C("montoReclamado", monto), C("nombre", "NombreUnico987")), html: false);

        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.DoesNotContain("NombreUnico987", texto, StringComparison.Ordinal);
        Assert.Empty(_host.Reclamaciones.Entradas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public async Task Los_campos_que_faltan_se_listan_sin_el_valor_escrito_en_la_pantalla_y_en_el_problema()
    {
        _host.Reclamaciones.Respuesta = Resultado<ConstanciaDeHoja>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("documento", "obligatorio"), new ErrorDeCampo("detalle", "obligatorio")]));

        using var pantalla = await _host.Enviar(Hoja(C("nombre", "NombreUnico987")));
        using var problema = await _host.Enviar(Hoja(C("nombre", "NombreUnico987")), html: false);

        var html = System.Net.WebUtility.HtmlDecode(await pantalla.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, pantalla.StatusCode);
        Assert.Contains("Documento de identidad: falta o no es válido", html, StringComparison.Ordinal);
        Assert.Contains("Detalle: falta o no es válido", html, StringComparison.Ordinal);
        Assert.DoesNotContain("NombreUnico987", html, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", html, StringComparison.Ordinal);
        var cuerpo = await ReclamacionesApiHost.Leer(problema);
        Assert.Equal(HttpStatusCode.BadRequest, problema.StatusCode);
        Assert.Equal("ECO-VALIDACION-INVALIDA", cuerpo.GetProperty("code").GetString());
        Assert.DoesNotContain("NombreUnico987", cuerpo.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CategoriaDeError.Prohibido, "verificacion-requerida", HttpStatusCode.Forbidden, "ECO-VERIFICACION-REQUERIDA")]
    [InlineData(CategoriaDeError.Prohibido, "verificacion-fallida", HttpStatusCode.Forbidden, "ECO-VERIFICACION-FALLIDA")]
    [InlineData(CategoriaDeError.Conflicto, "rec-conflicto", HttpStatusCode.Conflict, "ECO-CONFLICTO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable, "ECO-SERVICIO-NO-DISPONIBLE")]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Los_errores_del_registro_se_traducen_a_su_codigo_estable(CategoriaDeError categoria, string clave, HttpStatusCode estado, string codigo)
    {
        _host.Reclamaciones.Respuesta = Resultado<ConstanciaDeHoja>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Enviar(Hoja(), html: false);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal(codigo, (await ReclamacionesApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task El_limite_responde_429_con_retry_after_de_la_espera_sugerida()
    {
        _host.Reclamaciones.Respuesta = Resultado<ConstanciaDeHoja>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Limite, "limite-excedido", [new ErrorDeCampo("espera-en-segundos", "1800")]));

        using var respuesta = await _host.Enviar(Hoja(), html: false);

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        var espera = int.Parse(respuesta.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(espera, 1700, 1800);
        Assert.Equal("ECO-LIMITE-EXCEDIDO", (await ReclamacionesApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    // ---------- Mis hojas ----------

    [Fact]
    public async Task Mis_hojas_lista_con_el_estado_en_palabras_y_sin_guardarse()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/mis-hojas"));

        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("2026-000001", html, StringComparison.Ordinal);
        Assert.Contains("Pendiente de respuesta", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(["listar:1:20"], _host.Consumidor.Llamadas);
    }

    [Theory]
    [InlineData("/reclamaciones/mis-hojas?pagina=abc")]
    [InlineData("/reclamaciones/mis-hojas?tamano=-1")]
    [InlineData("/reclamaciones/mis-hojas?cuentaId=1")]
    public async Task La_paginacion_y_la_consulta_son_estrictas(string ruta)
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir(ruta, html: false));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Consumidor.Llamadas);
    }

    [Theory]
    [InlineData(CategoriaDeError.Prohibido, "correo-no-verificado", HttpStatusCode.Forbidden, "ECO-REC-CORREO-NO-VERIFICADO")]
    [InlineData(CategoriaDeError.NoAutenticado, "no-autenticado", HttpStatusCode.Unauthorized, "ECO-NO-AUTENTICADO")]
    [InlineData(CategoriaDeError.Limite, "limite-excedido", HttpStatusCode.TooManyRequests, "ECO-LIMITE-EXCEDIDO")]
    public async Task Los_errores_de_mis_hojas_se_traducen_a_su_codigo_estable(CategoriaDeError categoria, string clave, HttpStatusCode estado, string codigo)
    {
        _host.Consumidor.Pagina = Resultado<PaginaDeMisHojas>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/mis-hojas", html: false));

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal(codigo, (await ReclamacionesApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    // ---------- Una hoja por su número ----------

    [Fact]
    public async Task Una_hoja_por_su_numero_llama_al_modulo_y_codifica_el_texto_de_la_respuesta()
    {
        _host.Consumidor.Hoja = Resultado<MiHoja>.Exito(new MiHoja("2026-000001", new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero), "queja", EstadoDeLaHoja.Respondido, "<script>alert('x')</script> Gracias", new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/2026-000001"));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Equal(["obtener:2026-000001"], _host.Consumidor.Llamadas);
    }

    [Theory]
    [InlineData("/reclamaciones/abc")]
    [InlineData("/reclamaciones/2026-00001")]
    [InlineData("/reclamaciones/2026-0000011")]
    [InlineData("/reclamaciones/2026-000001%0A")]
    public async Task Un_numero_con_mala_forma_no_llega_al_modulo(string ruta)
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir(ruta, html: false));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Empty(_host.Consumidor.Llamadas);
    }

    [Fact]
    public async Task Una_hoja_ajena_o_inexistente_responde_no_encontrado_uniforme()
    {
        _host.Consumidor.Hoja = Resultado<MiHoja>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "hoja-no-encontrada"));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/2026-000009", html: false));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("ECO-NO-ENCONTRADO", (await ReclamacionesApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }
}

/// <summary>T17 y T18: las pantallas de error del Libro tienen su propio texto en palabras de la persona (la invitación a verificar el correo, el estado inválido y el conflicto), nunca el texto genérico.</summary>
public sealed class PantallasDeErrorDeReclamacionesTests : IAsyncLifetime
{
    private ReclamacionesApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ReclamacionesApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task Mis_hojas_sin_correo_verificado_invita_a_verificarlo_con_un_403()
    {
        _host.Consumidor.Pagina = Resultado<PaginaDeMisHojas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "correo-no-verificado"));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/mis-hojas"));

        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("Verifica tu correo para ver tus hojas", html, StringComparison.Ordinal);
        Assert.Contains("ECO-REC-CORREO-NO-VERIFICADO", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rec-estado-invalido", "Esta hoja ya no admite esa acción")]
    [InlineData("rec-conflicto", "No pudimos completar la solicitud")]
    public async Task Un_estado_invalido_o_un_conflicto_de_la_administracion_se_explican_en_su_pantalla(string clave, string titulo)
    {
        _host.Administracion.Operacion = Resultado<ResultadoDeLaHoja>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, clave));

        using var respuesta = await _host.Enviar([new("respuesta", "Una respuesta.")], ruta: "/administracion/reclamaciones/a0000000-0000-0000-0000-000000000001/responder");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains(titulo, System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_visitante_en_la_administracion_y_la_falta_de_permiso_usan_las_pantallas_estandar()
    {
        _host.Administracion.Pagina = Resultado<PaginaDeHojas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido"));

        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/administracion/reclamaciones"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("<html", await respuesta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>T17 (AC-REC-01): el enlace del pie de toda página lleva al formulario del Libro.</summary>
public sealed class EnlaceDelPieDelLibroTests : IAsyncLifetime
{
    private ReclamacionesApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ReclamacionesApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Criterio", "AC-REC-01")]
    public async Task La_ruta_permanente_del_pie_redirige_al_formulario_y_el_pie_de_la_pagina_la_enlaza()
    {
        using var enlace = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones"));
        using var formulario = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/nueva"));

        Assert.Equal(HttpStatusCode.SeeOther, enlace.StatusCode);
        Assert.Equal("/reclamaciones/nueva", enlace.Headers.Location?.OriginalString);
        Assert.Contains("href=\"/reclamaciones\"", await formulario.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}

/// <summary>Revisión de seguridad: los formularios llevan su propio token antifalsificación (un navegador sin JavaScript no lo manda en la cabecera) y la ruta del número se valida dentro de la acción.</summary>
public sealed class FormulariosConTokenTests : IAsyncLifetime
{
    private ReclamacionesApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ReclamacionesApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task El_formulario_publico_renderizado_se_envia_tal_cual_con_su_campo_de_token_y_sin_la_cabecera()
    {
        using var pagina = await _host.Cliente.SendAsync(_host.Pedir("/reclamaciones/nueva"));
        var html = await pagina.Content.ReadAsStringAsync();
        var cookie = pagina.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(BabelHub.Ecommerce.Api.Web.ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal));
        var token = Regex.Match(html, "name=\"" + Regex.Escape(BabelHub.Ecommerce.Api.Web.ProteccionContraFalsificacion.NombreDelCampo) + "\" value=\"([^\"]+)\"").Groups[1].Value;
        var clave = Regex.Match(html, "name=\"claveDelFormulario\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        using var envio = new HttpRequestMessage(HttpMethod.Post, "/reclamaciones/nueva");
        envio.Headers.TryAddWithoutValidation("Cookie", cookie);
        envio.Headers.TryAddWithoutValidation("Accept", "text/html");
        envio.Content = new FormUrlEncodedContent(
        [
            new(BabelHub.Ecommerce.Api.Web.ProteccionContraFalsificacion.NombreDelCampo, token),
            new("claveDelFormulario", clave),
            new("tipo", "reclamo"), new("nombre", "Ana Pérez"), new("documento", "12345678"), new("domicilio", "Jr. Lima 123"), new("correo", "ana@example.com"),
            new("bienContratado", "Libro"), new("montoReclamado", "10.50"), new("detalle", "Llegó dañado"),
        ]);
        using var respuesta = await _host.Cliente.SendAsync(envio);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Single(_host.Reclamaciones.Entradas);
    }

    [Theory]
    [InlineData("/reclamaciones/2026-000001%0A")]
    [InlineData("/reclamaciones/2026-00001")]
    [InlineData("/reclamaciones/abc")]
    public async Task Un_numero_mal_escrito_responde_no_encontrado_uniforme_sin_llegar_al_modulo(string ruta)
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir(ruta, html: false));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Empty(_host.Consumidor.Llamadas);
    }
}
