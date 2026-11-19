using System.Net;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>La Presentación de logística (T16; ENV-08, AC-ENV-04, AC-ENV-08, AC-ENV-09): rutas, <c>303</c>, la traducción de errores a <c>ECO-ENV-…</c> sin datos de contacto, el rechazo de entradas que no declara y el token antifalsificación.</summary>
public sealed class LogisticaApiTests
{
    private static readonly Guid Envio = Guid.Parse("e1000000-0000-0000-0000-000000000001");

    private static ErrorDeAplicacion Error(CategoriaDeError categoria, string clave, params ErrorDeCampo[] campos) => new(categoria, clave, campos);

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [InlineData("guia")]
    [InlineData("reparto")]
    [InlineData("entregado")]
    public async Task Una_operacion_exitosa_responde_303_a_la_lista_con_el_aviso_y_llama_una_sola_vez(string accion)
    {
        await using var host = await LogisticaApiHost.Crear();

        using var respuesta = await host.Enviar($"/logistica/envios/{Envio:D}/{accion}");

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/logistica/envios?hecho={accion}", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        Assert.Equal([$"{accion}:{Envio:D}"], host.Envios.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public async Task Con_htmx_la_operacion_exitosa_responde_200_con_HX_Redirect()
    {
        await using var host = await LogisticaApiHost.Crear();

        using var respuesta = await host.Enviar($"/logistica/envios/{Envio:D}/guia", htmx: true);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("/logistica/envios?hecho=guia", respuesta.Headers.GetValues("HX-Redirect").Single());
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-04")]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData(CategoriaDeError.Conflicto, "estado-invalido", 409, "ECO-ENV-ESTADO-INVALIDO")]
    [InlineData(CategoriaDeError.Conflicto, "guia-en-curso", 409, "ECO-ENV-GUIA-EN-CURSO")]
    [InlineData(CategoriaDeError.NoDisponible, "operador-no-disponible", 503, "ECO-ENV-OPERADOR-NO-DISPONIBLE")]
    [InlineData(CategoriaDeError.Conflicto, "inventario-rechazo", 409, "ECO-ENV-INVENTARIO-RECHAZO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", 503, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData(CategoriaDeError.NoEncontrado, "envio-no-encontrado", 404, "ECO-NO-ENCONTRADO")]
    [InlineData(CategoriaDeError.Prohibido, "prohibido", 403, "ECO-PROHIBIDO")]
    [InlineData(CategoriaDeError.NoAutenticado, "no-autenticado", 401, "ECO-NO-AUTENTICADO")]
    public async Task Los_fallos_salen_como_problem_json_con_el_codigo_y_el_estado_del_plan(CategoriaDeError categoria, string clave, int estado, string codigo)
    {
        await using var host = await LogisticaApiHost.Crear();
        host.Envios.RespuestaDeOperacion = Resultado<Ecommerce.Application.Envios.DetalleDeEnvio>.Fallo(Error(categoria, clave));

        using var respuesta = await host.Enviar($"/logistica/envios/{Envio:D}/reparto");

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.StartsWith("application/problem+json", respuesta.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        var problema = await LogisticaApiHost.Leer(respuesta);
        Assert.Equal(codigo, problema.GetProperty("code").GetString());
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        Assert.False(string.IsNullOrEmpty(problema.GetProperty("traceId").GetString()));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-09")]
    [InlineData("nombre")]
    [InlineData("direccion")]
    [InlineData("telefono")]
    [InlineData("anonimizado")]
    public async Task El_contacto_incompleto_es_422_y_nombra_solo_el_campo_o_la_anonimizacion(string campo)
    {
        await using var host = await LogisticaApiHost.Crear();
        host.Envios.RespuestaDeOperacion = Resultado<Ecommerce.Application.Envios.DetalleDeEnvio>.Fallo(Error(CategoriaDeError.ReglaDeNegocio, "contacto-incompleto", new ErrorDeCampo(campo, "faltante")));

        using var respuesta = await host.Enviar($"/logistica/envios/{Envio:D}/guia");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var problema = await LogisticaApiHost.Leer(respuesta);
        Assert.Equal("ECO-ENV-CONTACTO-INCOMPLETO", problema.GetProperty("code").GetString());
        var errores = problema.GetProperty("errores").EnumerateArray().ToList();
        Assert.Equal(campo, Assert.Single(errores).GetProperty("campo").GetString());
        Assert.Equal("faltante", errores[0].GetProperty("clave").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_limite_excedido_es_429_con_Retry_After_y_sin_otro_dato()
    {
        await using var host = await LogisticaApiHost.Crear();
        host.Envios.RespuestaDeOperacion = Resultado<Ecommerce.Application.Envios.DetalleDeEnvio>.Fallo(Error(CategoriaDeError.Limite, "limite-excedido", new ErrorDeCampo("espera-en-segundos", "42"), new ErrorDeCampo("motivo", "operaciones-de-envios-por-usuario"), new ErrorDeCampo("disponibleDesde", "2026-11-20T15:00:42.0000000+00:00")));

        using var respuesta = await host.Enviar($"/logistica/envios/{Envio:D}/guia");

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Equal("42", respuesta.Headers.GetValues("Retry-After").Single());
        var problema = await LogisticaApiHost.Leer(respuesta);
        Assert.Equal("ECO-LIMITE-EXCEDIDO", problema.GetProperty("code").GetString());
        Assert.Equal("operaciones-de-envios-por-usuario", problema.GetProperty("motivo").GetString());
        Assert.Equal(42, problema.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T15:00:42+00:00"), problema.GetProperty("disponibleDesde").GetDateTimeOffset());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_rechazo_por_rol_o_sesion_registra_un_acceso_denegado_de_la_Presentacion()
    {
        await using var host = await LogisticaApiHost.Crear();
        host.Envios.RespuestaDeOperacion = Resultado<Ecommerce.Application.Envios.DetalleDeEnvio>.Fallo(Error(CategoriaDeError.Prohibido, "prohibido"));

        using var respuesta = await host.Enviar($"/logistica/envios/{Envio:D}/guia");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains(host.Denegaciones.Registradas, d => d.Categoria == CategoriaDeError.Prohibido);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_POST_con_consulta_o_con_campos_que_no_declara_es_validacion_y_no_llega_a_la_Aplicacion()
    {
        await using var host = await LogisticaApiHost.Crear();

        using var conConsulta = await host.Enviar($"/logistica/envios/{Envio:D}/guia?usuarioId=x");
        using var conCampo = await host.Enviar($"/logistica/envios/{Envio:D}/guia", [new("nombre", "Ana Torres")]);

        Assert.Equal(HttpStatusCode.BadRequest, conConsulta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conCampo.StatusCode);
        Assert.Empty(host.Envios.Llamadas);
        var cuerpo = await conCampo.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Ana Torres", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Sin_el_token_antifalsificacion_o_con_un_identificador_que_no_es_Guid_no_se_ejecuta_nada()
    {
        await using var host = await LogisticaApiHost.Crear();

        using var sinToken = await host.Enviar($"/logistica/envios/{Envio:D}/guia", conToken: false);
        using var malId = await host.Enviar("/logistica/envios/no-es-un-guid/guia");

        Assert.False(sinToken.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malId.StatusCode);
        Assert.Empty(host.Envios.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-08")]
    [InlineData("/logistica/envios?estado=inexistente")]
    [InlineData("/logistica/envios?pagina=0")]
    [InlineData("/logistica/envios?pagina=abc")]
    [InlineData("/logistica/envios?hecho=borrar")]
    [InlineData("/logistica/envios?cuentaId=x")]
    [InlineData("/logistica/envios?estado=entregado&estado=cancelado")]
    public async Task La_lista_rechaza_una_consulta_invalida_sin_llamar_a_la_Aplicacion(string ruta)
    {
        await using var host = await LogisticaApiHost.Crear();

        using var respuesta = await host.Consultar(ruta);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(host.Envios.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task Un_fallo_de_la_lista_sale_como_problem_json()
    {
        await using var host = await LogisticaApiHost.Crear();
        host.Envios.RespuestaDeLista = Resultado<Ecommerce.Application.Envios.PaginaDeEnvios>.Fallo(Error(CategoriaDeError.Prohibido, "prohibido"));

        using var respuesta = await host.Consultar("/logistica/envios?estado=en-reparto&pagina=2");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal("ECO-PROHIBIDO", (await LogisticaApiHost.Leer(respuesta)).GetProperty("code").GetString());
        Assert.Equal(Ecommerce.Application.Envios.EstadoDelEnvio.EnReparto, host.Envios.UltimaConsulta!.Estado);
        Assert.Equal(2, host.Envios.UltimaConsulta.Pagina);
    }
}
