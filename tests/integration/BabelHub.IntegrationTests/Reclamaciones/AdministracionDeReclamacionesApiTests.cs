using System.Net;
using BabelHub.Ecommerce.Api.Reclamaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T16: la administración de las hojas (REC-07 a REC-10, REC-12, REC-13, REC-15; AC-REC-06, AC-REC-07, AC-REC-10, AC-REC-13): el listado sin datos personales con su filtro y su paginación estrictos, el detalle con los datos codificados y no cacheables, responder, descartar y
/// reintentar el aviso con esquema estricto, la traducción de los errores (<c>ECO-PROHIBIDO</c>, <c>ECO-NO-ENCONTRADO</c>, <c>ECO-REC-ESTADO-INVALIDO</c>, <c>ECO-CONFLICTO</c>) y el anfitrión de los trabajos con el actor «sistema» solo en modo normal.
/// </summary>
public sealed class AdministracionDeReclamacionesApiTests : IAsyncLifetime
{
    private static readonly Guid Hoja = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    private ReclamacionesApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ReclamacionesApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task El_listado_muestra_numero_fecha_tipo_estado_y_aviso_sin_datos_personales()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/administracion/reclamaciones"));

        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("2026-000001", html, StringComparison.Ordinal);
        Assert.Contains("Pendiente de respuesta", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(["listar::1:20"], _host.Administracion.Llamadas);
    }

    [Theory]
    [InlineData("?estado=descartado", "listar:Descartado:1:20")]
    [InlineData("?estado=pendiente&pagina=2&tamano=5", "listar:Pendiente:2:5")]
    public async Task El_filtro_y_la_paginacion_llegan_al_modulo(string consulta, string esperada)
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/administracion/reclamaciones" + consulta));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal([esperada], _host.Administracion.Llamadas);
    }

    [Theory]
    [InlineData("?estado=otro")]
    [InlineData("?pagina=abc")]
    [InlineData("?tamano=-1")]
    [InlineData("?cuentaId=1")]
    public async Task Un_filtro_o_una_paginacion_invalida_es_validacion_sin_llamar_al_modulo(string consulta)
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/administracion/reclamaciones" + consulta, html: false));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_host.Administracion.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public async Task El_detalle_pinta_los_datos_codificados_avisa_de_la_auditoria_y_ofrece_responder_y_descartar_si_esta_pendiente()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir($"/administracion/reclamaciones/{Hoja:D}"));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("queda registrada en la auditor", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains($"/administracion/reclamaciones/{Hoja:D}/responder", html, StringComparison.Ordinal);
        Assert.Contains($"/administracion/reclamaciones/{Hoja:D}/descartar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("reintentar-aviso", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal([$"obtener:{Hoja:D}"], _host.Administracion.Llamadas);
    }

    [Fact]
    public async Task Un_id_que_no_es_guid_no_llega_al_modulo()
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir("/administracion/reclamaciones/no-es-guid", html: false));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Empty(_host.Administracion.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-06")]
    public async Task Responder_pasa_la_respuesta_al_modulo_y_vuelve_al_detalle()
    {
        using var respuesta = await _host.Enviar([new("respuesta", "Le ofrecemos reponer el libro.")], ruta: $"/administracion/reclamaciones/{Hoja:D}/responder");

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/administracion/reclamaciones/{Hoja:D}", respuesta.Headers.Location?.OriginalString);
        Assert.Equal("Le ofrecemos reponer el libro.", Assert.Single(_host.Administracion.Respuestas).Respuesta);
    }

    [Theory]
    [InlineData("descartar")]
    [InlineData("reintentar-aviso")]
    [Trait("Criterio", "AC-REC-07")]
    public async Task Descartar_y_reintentar_el_aviso_llaman_al_modulo_y_vuelven_al_detalle(string accion)
    {
        using var respuesta = await _host.Enviar([], ruta: $"/administracion/reclamaciones/{Hoja:D}/{accion}");

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal($"/administracion/reclamaciones/{Hoja:D}", respuesta.Headers.Location?.OriginalString);
        Assert.Single(_host.Administracion.Llamadas);
    }

    [Fact]
    public async Task Un_campo_de_mas_en_descartar_o_en_responder_es_validacion_sin_llamar_al_modulo()
    {
        using var descartar = await _host.Enviar([new("estado", "respondido")], html: false, ruta: $"/administracion/reclamaciones/{Hoja:D}/descartar");
        using var responder = await _host.Enviar([new("respuesta", "x"), new("numero", "2026-000001")], html: false, ruta: $"/administracion/reclamaciones/{Hoja:D}/responder");

        Assert.Equal(HttpStatusCode.BadRequest, descartar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, responder.StatusCode);
        Assert.Empty(_host.Administracion.Llamadas);
    }

    [Theory]
    [InlineData(CategoriaDeError.Prohibido, "prohibido", HttpStatusCode.Forbidden, "ECO-PROHIBIDO")]
    [InlineData(CategoriaDeError.NoEncontrado, "hoja-no-encontrada", HttpStatusCode.NotFound, "ECO-NO-ENCONTRADO")]
    [InlineData(CategoriaDeError.Conflicto, "estado-invalido", HttpStatusCode.Conflict, "ECO-REC-ESTADO-INVALIDO")]
    [InlineData(CategoriaDeError.Conflicto, "conflicto", HttpStatusCode.Conflict, "ECO-CONFLICTO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData(CategoriaDeError.NoAutenticado, "no-autenticado", HttpStatusCode.Unauthorized, "ECO-NO-AUTENTICADO")]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Los_errores_de_la_administracion_se_traducen_a_su_codigo_estable_sin_repetir_la_respuesta(CategoriaDeError categoria, string clave, HttpStatusCode estado, string codigo)
    {
        _host.Administracion.Operacion = Resultado<ResultadoDeLaHoja>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await _host.Enviar([new("respuesta", "Texto reservado de la respuesta")], html: false, ruta: $"/administracion/reclamaciones/{Hoja:D}/responder");

        Assert.Equal(estado, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains(codigo, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Texto reservado", texto, StringComparison.Ordinal);
    }

    // ---------- Trabajos de fondo ----------

    [Fact]
    public async Task El_alcance_de_los_trabajos_fija_el_actor_sistema_en_la_corrida_y_lo_limpia_aunque_falle()
    {
        var actor = new ActorDelFlujo();
        var alcance = TrabajosDeReclamacionesEnLaApi.AlcanceDeSistema(actor);
        var visto = new List<ActorActual?>();

        await alcance(_ => { visto.Add(actor.Actual); return Task.CompletedTask; }, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => alcance(_ => { visto.Add(actor.Actual); throw new InvalidOperationException("falló"); }, CancellationToken.None));

        Assert.Equal(2, visto.Count);
        Assert.All(visto, a => Assert.Equal(TipoDeActor.Sistema, a!.Tipo));
        Assert.Null(actor.Actual);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void El_servicio_hospedado_solo_se_registra_en_modo_normal(bool soloEnModoNormal, int hospedados)
    {
        var servicios = new ServiceCollection();
        servicios.AgregarTrabajosDeReclamacionesEnLaApi(soloEnModoNormal);

        Assert.Equal(hospedados, servicios.Count(s => s.ServiceType == typeof(IHostedService)));
    }
}
