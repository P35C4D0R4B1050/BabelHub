using System.Net;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>La traducción de los errores de los módulos y del Inventario a estados de pantalla (T22): reintento, espera de un límite, ingreso uniforme y sin detalles técnicos.</summary>
public sealed class TraduccionDeErroresTests
{
    private static ProblemaDto Problema(string codigo, int estado, string detalle = "Detalle genérico.", string traza = "t-1", params ErrorDeCampoDto[] campos) =>
        new() { Code = codigo, Status = estado, Detail = detalle, TraceId = traza, Errores = campos };

    private static RespuestaDeLimiteDto Limite(string codigo, int segundos, DateTimeOffset desde, string traza = "t-2") =>
        new() { Code = codigo, Status = 429, TraceId = traza, Motivo = "ingresos-fallidos", RetryAfterSeconds = segundos, DisponibleDesde = desde };

    [Theory]
    [InlineData("ECO-ERROR-INESPERADO", 500)]
    [InlineData("INV-ERROR-INESPERADO", 500)]
    [InlineData("ECO-CONFLICTO", 409)]
    [InlineData("ECO-CODIGO-QUE-NADIE-CONOCE", 418)]
    [Trait("Criterio", "AC-WEB-04")]
    public void Un_error_inesperado_es_un_estado_de_error_con_reintento_y_sin_detalles_tecnicos(string codigo, int estado)
    {
        var problema = Problema(codigo, estado, "System.NullReferenceException en Servidor.cs:line 12 con password=abc", "traza-77");

        var respuesta = TraductorDeErrores.Traducir(problema, rutaDeReintento: "/catalogo");

        Assert.Equal(EstadoDePantalla.Error, respuesta.Vista.Estado);
        Assert.Equal("/catalogo", respuesta.Vista.RutaDeReintento);
        Assert.Equal(TextosDeInterfaz.ErrorTitulo, respuesta.Vista.Titulo);
        Assert.Equal(TextosDeInterfaz.ErrorMensaje, respuesta.Vista.Mensaje);
        Assert.DoesNotContain("NullReference", respuesta.Vista.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain("password", respuesta.Vista.Mensaje, StringComparison.Ordinal);
        Assert.True(respuesta.ConservaLoEscrito);
        Assert.Null(respuesta.RedirigirA);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public void El_codigo_estable_y_el_seguimiento_solo_van_en_el_soporte()
    {
        var respuesta = TraductorDeErrores.Traducir(Problema("ECO-ERROR-INESPERADO", 500, traza: "traza-77"), rutaDeReintento: "/");

        Assert.Equal("ECO-ERROR-INESPERADO", respuesta.Vista.CodigoDeSoporte);
        Assert.Equal("traza-77", respuesta.Vista.IdentificadorDeSeguimiento);
        Assert.DoesNotContain("traza-77", respuesta.Vista.Mensaje ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData("INV-SERVICIO-NO-DISPONIBLE")]
    [Trait("Criterio", "AC-WEB-21")]
    public void Un_modulo_o_el_Inventario_que_no_responde_es_servicio_no_disponible_con_reintento_y_nada_se_pierde(string codigo)
    {
        var respuesta = TraductorDeErrores.Traducir(
            Problema(codigo, 503),
            rutaDeReintento: "/carrito",
            parteAfectada: "No podemos validar el stock ahora.");

        Assert.Equal(EstadoDePantalla.ServicioNoDisponible, respuesta.Vista.Estado);
        Assert.Equal("No podemos validar el stock ahora.", respuesta.Vista.Mensaje);
        Assert.Equal("/carrito", respuesta.Vista.RutaDeReintento);
        Assert.Equal(503, respuesta.EstadoHttp);
        Assert.True(respuesta.ConservaLoEscrito);
        Assert.Null(respuesta.RedirigirA);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-21")]
    public void Sin_parte_afectada_el_mensaje_de_servicio_no_disponible_es_general()
    {
        var respuesta = TraductorDeErrores.Traducir(Problema("ECO-SERVICIO-NO-DISPONIBLE", 503), rutaDeReintento: "/");

        Assert.Equal(TextosDeInterfaz.NoDisponibleMensajeGeneral, respuesta.Vista.Mensaje);
    }

    [Theory]
    [MemberData(nameof(ExcepcionesDeComunicacion))]
    [Trait("Criterio", "AC-WEB-21")]
    public void Una_falla_de_comunicacion_con_el_Inventario_o_un_modulo_es_servicio_no_disponible_sin_mostrar_la_excepcion(Exception excepcion)
    {
        var respuesta = TraductorDeErrores.Traducir(excepcion, rutaDeReintento: "/carrito", parteAfectada: "No podemos consultar tu carrito ahora.");

        Assert.Equal(EstadoDePantalla.ServicioNoDisponible, respuesta.Vista.Estado);
        Assert.Equal("No podemos consultar tu carrito ahora.", respuesta.Vista.Mensaje);
        Assert.DoesNotContain(excepcion.Message, respuesta.Vista.Mensaje ?? string.Empty, StringComparison.Ordinal);
        Assert.True(respuesta.ConservaLoEscrito);
    }

    public static TheoryData<Exception> ExcepcionesDeComunicacion() =>
    [
        new HttpRequestException("No se puede conectar con 10.0.0.5:8080"),
        new TaskCanceledException("Se agotó el tiempo de espera"),
        new TimeoutException("tiempo agotado"),
    ];

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public void Una_excepcion_cualquiera_es_un_error_generico_sin_su_mensaje()
    {
        var respuesta = TraductorDeErrores.Traducir(new InvalidOperationException("secreto=xyz"), rutaDeReintento: "/");

        Assert.Equal(EstadoDePantalla.Error, respuesta.Vista.Estado);
        Assert.DoesNotContain("secreto", respuesta.Vista.Mensaje ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ECO-PROHIBIDO")]
    [InlineData("INV-PROHIBIDO")]
    [Trait("Criterio", "AC-WEB-02")]
    public void Un_acceso_prohibido_es_la_pantalla_de_sin_permiso_con_salidas(string codigo)
    {
        var respuesta = TraductorDeErrores.Traducir(Problema(codigo, 403), salidas: [new EnlaceDeSalida("Ir al inicio", "/")]);

        Assert.Equal(EstadoDePantalla.SinPermiso, respuesta.Vista.Estado);
        Assert.Equal(TextosDeInterfaz.SinPermisoMensaje, respuesta.Vista.Mensaje);
        Assert.Equal("/", Assert.Single(respuesta.Vista.Salidas).Ruta);
        Assert.Equal(403, respuesta.EstadoHttp);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void Sin_salidas_declaradas_el_sin_permiso_ofrece_al_menos_ir_al_inicio()
    {
        var respuesta = TraductorDeErrores.Traducir(Problema("ECO-PROHIBIDO", 403));

        Assert.Contains(respuesta.Vista.Salidas, s => s.Ruta == "/");
    }

    [Theory]
    [InlineData("ECO-NO-AUTENTICADO")]
    [InlineData("INV-NO-AUTENTICADO")]
    [Trait("Criterio", "AC-WEB-20")]
    public void Una_solicitud_no_autenticada_lleva_al_ingreso(string codigo)
    {
        var respuesta = TraductorDeErrores.Traducir(Problema(codigo, 401));

        Assert.Equal("/ingreso", respuesta.RedirigirA);
        Assert.True(respuesta.ConservaLoEscrito);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public void Un_limite_explica_el_motivo_y_la_espera_sugerida_y_conserva_lo_escrito()
    {
        // 2026-11-14 20:42 UTC es 15:42 en Lima.
        var respuesta = TraductorDeErrores.Traducir(
            Limite("ECO-LIMITE-EXCEDIDO", 900, new DateTimeOffset(2026, 11, 14, 20, 42, 0, TimeSpan.Zero)),
            rutaDeReintento: "/registro");

        Assert.Equal(EstadoDePantalla.Error, respuesta.Vista.Estado);
        Assert.Equal(900, respuesta.Vista.EsperaSugeridaSegundos);
        Assert.Equal("15:42", respuesta.Vista.DisponibleDesde);
        Assert.Contains("15 minutos", respuesta.Vista.Mensaje, StringComparison.Ordinal);
        Assert.Contains("15:42", respuesta.Vista.Mensaje, StringComparison.Ordinal);
        Assert.True(respuesta.ConservaLoEscrito);
        Assert.Equal(429, respuesta.EstadoHttp);
    }

    [Theory]
    [InlineData(30, "30 segundos")]
    [InlineData(1, "1 segundo")]
    [InlineData(61, "2 minutos")]
    [InlineData(3600, "60 minutos")]
    [Trait("Criterio", "AC-WEB-19")]
    public void La_espera_se_expresa_en_segundos_o_en_minutos_redondeados_hacia_arriba(int segundos, string esperado)
    {
        var respuesta = TraductorDeErrores.Traducir(Limite("ECO-LIMITE-EXCEDIDO", segundos, DateTimeOffset.UnixEpoch));

        Assert.Contains(esperado, respuesta.Vista.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public void Un_limite_sin_los_datos_de_espera_sigue_explicando_que_hay_que_esperar()
    {
        var respuesta = TraductorDeErrores.Traducir(Problema("ECO-LIMITE-EXCEDIDO", 429));

        Assert.Equal(EstadoDePantalla.Error, respuesta.Vista.Estado);
        Assert.Contains("unos instantes", respuesta.Vista.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public void Una_validacion_lista_los_campos_para_mostrarlos_junto_a_su_control_y_conserva_lo_escrito()
    {
        var respuesta = TraductorDeErrores.Traducir(
            Problema("ECO-VALIDACION-INVALIDA", 400, campos: [new ErrorDeCampoDto("correo", "formato-invalido")]),
            rutaDeReintento: null);

        Assert.Equal("correo", Assert.Single(respuesta.Campos).Campo);
        Assert.True(respuesta.ConservaLoEscrito);
        Assert.Null(respuesta.Vista.RutaDeReintento);
        Assert.Equal(400, respuesta.EstadoHttp);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-07")]
    public void Un_codigo_de_regla_de_negocio_muestra_su_detalle_que_ya_viene_redactado_para_la_persona()
    {
        var respuesta = TraductorDeErrores.Traducir(Problema("ECO-STOCK-INSUFICIENTE", 422, "No hay stock para completar el pedido."));

        Assert.Equal("No hay stock para completar el pedido.", respuesta.Vista.Mensaje);
        Assert.Null(respuesta.Vista.RutaDeReintento);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public void El_ingreso_responde_lo_mismo_exista_o_no_la_cuenta()
    {
        // El módulo puede rechazar de formas distintas según la cuenta exista, esté bloqueada o no esté verificada: la pantalla no lo distingue.
        var distintas = new[]
        {
            Problema("ECO-NO-AUTENTICADO", 401, traza: "a"),
            Problema("ECO-NO-ENCONTRADO", 404, traza: "b"),
            Problema("ECO-PROHIBIDO", 403, traza: "c"),
            Problema("ECO-REGLA-DE-NEGOCIO-INCUMPLIDA", 422, "La cuenta no está verificada.", traza: "d"),
            Problema("ECO-VALIDACION-INVALIDA", 400, traza: "e"),
        };

        var respuestas = distintas.Select(p => TraductorDeErrores.Traducir(p, ContextoDeTraduccion.Ingreso, rutaDeReintento: "/ingreso")).ToList();

        Assert.All(respuestas, r =>
        {
            Assert.Equal(EstadoDePantalla.Error, r.Vista.Estado);
            Assert.Equal("El correo o la contraseña no coinciden.", r.Vista.Mensaje);
            Assert.Equal(401, r.EstadoHttp);
            Assert.Null(r.RedirigirA);
            Assert.Null(r.Vista.CodigoDeSoporte);
            Assert.Null(r.Vista.IdentificadorDeSeguimiento);
            Assert.Empty(r.Campos);
            Assert.Equal(respuestas[0].Vista.Titulo, r.Vista.Titulo);
            Assert.Equal(respuestas[0].Vista.RutaDeReintento, r.Vista.RutaDeReintento);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public void El_limite_del_ingreso_es_igual_para_cuentas_existentes_e_inexistentes()
    {
        var desde = new DateTimeOffset(2026, 11, 14, 20, 42, 0, TimeSpan.Zero);

        var existente = TraductorDeErrores.Traducir(Limite("ECO-LIMITE-EXCEDIDO", 900, desde, "x1"), ContextoDeTraduccion.Ingreso, "/ingreso");
        var inexistente = TraductorDeErrores.Traducir(Limite("ECO-LIMITE-EXCEDIDO", 900, desde, "x2"), ContextoDeTraduccion.Ingreso, "/ingreso");

        Assert.Equal(existente.Vista.Mensaje, inexistente.Vista.Mensaje);
        Assert.Equal(existente.EstadoHttp, inexistente.EstadoHttp);
        Assert.Equal(existente.Vista.IdentificadorDeSeguimiento, inexistente.Vista.IdentificadorDeSeguimiento);
        Assert.Contains("recupera tu acceso", existente.Vista.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public void El_ingreso_con_servicio_no_disponible_sigue_siendo_servicio_no_disponible()
    {
        var respuesta = TraductorDeErrores.Traducir(Problema("ECO-SERVICIO-NO-DISPONIBLE", 503), ContextoDeTraduccion.Ingreso, "/ingreso");

        Assert.Equal(EstadoDePantalla.ServicioNoDisponible, respuesta.Vista.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Una_respuesta_no_autenticada_de_htmx_lleva_al_ingreso_con_HX_Redirect_y_una_normal_con_redireccion()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(partes: [typeof(TraduccionDeErroresTests).Assembly]);

        using var htmx = new HttpRequestMessage(HttpMethod.Get, "/prueba/traducir/ECO-NO-AUTENTICADO/401");
        htmx.Headers.Add("HX-Request", "true");
        using var respuestaHtmx = await anfitrion.Cliente.SendAsync(htmx);
        using var normal = await anfitrion.Cliente.GetAsync("/prueba/traducir/ECO-NO-AUTENTICADO/401");

        Assert.Equal(HttpStatusCode.OK, respuestaHtmx.StatusCode);
        Assert.Equal("/ingreso", Assert.Single(respuestaHtmx.Headers.GetValues("HX-Redirect")));
        Assert.Equal(HttpStatusCode.Redirect, normal.StatusCode);
        Assert.Equal("/ingreso", normal.Headers.Location?.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-19")]
    public async Task El_ingreso_devuelve_el_mismo_estado_html_cookies_y_redireccion_exista_o_no_la_cuenta()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(partes: [typeof(TraduccionDeErroresTests).Assembly]);

        async Task<(HttpStatusCode Estado, string Html, string Cookies, string? Ubicacion)> Pedir(string codigo, int estado)
        {
            using var respuesta = await anfitrion.Cliente.GetAsync($"/prueba/ingreso/{codigo}/{estado}");
            return (
                respuesta.StatusCode,
                await respuesta.Content.ReadAsStringAsync(),
                string.Join(';', respuesta.Headers.TryGetValues("Set-Cookie", out var c) ? c : []),
                respuesta.Headers.Location?.OriginalString);
        }

        var existe = await Pedir("ECO-NO-AUTENTICADO", 401);
        var noExiste = await Pedir("ECO-NO-ENCONTRADO", 404);
        var noVerificada = await Pedir("ECO-REGLA-DE-NEGOCIO-INCUMPLIDA", 422);

        Assert.Equal(HttpStatusCode.Unauthorized, existe.Estado);
        Assert.Equal(existe, noExiste);
        Assert.Equal(existe, noVerificada);
        Assert.Contains("El correo o la contraseña no coinciden.", System.Net.WebUtility.HtmlDecode(existe.Html), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-21")]
    public async Task Con_el_Inventario_caido_el_cliente_ve_servicio_no_disponible_y_ningun_cambio_se_aplica_al_carrito()
    {
        var carrito = new CarritoDePrueba();
        await using var anfitrion = await AnfitrionWeb.Iniciar(
            servicios: s => s.AddSingleton(carrito),
            partes: [typeof(TraduccionDeErroresTests).Assembly]);

        using var respuesta = await anfitrion.Cliente.GetAsync("/prueba/carrito");
        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Contains("No podemos mostrar esto ahora", html, StringComparison.Ordinal);
        Assert.Contains("No podemos validar el stock ahora.", html, StringComparison.Ordinal);
        Assert.Contains("Reintentar", html, StringComparison.Ordinal);
        Assert.Equal(2, carrito.Lineas);
        Assert.False(carrito.SeModifico);
    }
}
