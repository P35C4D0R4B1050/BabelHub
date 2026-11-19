using System.Net;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Pagos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T31: la auditoría unificada y las alertas de seguridad de la administración central, con las vistas reales y los contratos de Auditoría doblados (AC-WEB-25, AC-WEB-27): filtros estrictos, hora de Lima, el aviso del servicio que no respondió, los datos codificados contra XSS almacenado, el estado «sin permiso» para otro rol
/// y marcar una alerta como revisada con el contador que la persona vio.
/// </summary>
public sealed class PantallasDeAuditoriaTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Utc = new(2026, 11, 15, 2, 30, 5, TimeSpan.Zero);

    private readonly SesionesWebDePrueba _sesiones = new SesionesWebDePrueba().ConSesionValida(RolWeb.AdministradorCentral);
    private readonly List<ConsultaDeAuditoria> _consultas = [];
    private readonly List<(string Id, int Contador)> _revisadas = [];
    private AnfitrionWeb _anfitrion = null!;
    private Resultado<PaginaDeRegistros> _pagina = Resultado<PaginaDeRegistros>.Exito(new PaginaDeRegistros([Registro("{\"nota\":\"<script>alert(1)</script>\"}")], "cursor-siguiente"));
    private Resultado<PaginaDeAlertas> _alertas = Resultado<PaginaDeAlertas>.Exito(new PaginaDeAlertas([Alerta()], null));
    private Resultado<AlertaConsultada> _marcada = Resultado<AlertaConsultada>.Exito(Alerta());

    private static RegistroAuditado Registro(string? detalle) =>
        new(Guid.NewGuid(), ServicioAuditado.Inventario, TipoDeRegistroAuditado.Auditoria, TipoDeActor.Usuario, Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001"), "administrador-central", Utc, "AjusteRegistrado", ResultadoAuditado.Exitoso)
        {
            ObjetoTipo = "ajuste",
            ObjetoId = "AJ-0001",
            Detalle = detalle,
        };

    private static AlertaConsultada Alerta() => new("inv:a1000000-0000-4000-8000-000000000001", ServicioAuditado.Inventario, new CodigoDeAlerta("accesos-denegados"), ClaseDeAlertaConsultada.Seguridad, Utc, Utc.AddMinutes(4), 12, 5);

    public async Task InitializeAsync()
    {
        var auditoria = DobleDeMetodos.Crear<IConsultasDeAuditoria>(out var dAuditoria);
        dAuditoria.Con(nameof(IConsultasDeAuditoria.Consultar), a => { _consultas.Add((ConsultaDeAuditoria)a[0]!); return Task.FromResult(_pagina); });
        var alertas = DobleDeMetodos.Crear<IConsultaDeAlertas>(out var dAlertas);
        dAlertas.Con(nameof(IConsultaDeAlertas.Listar), _ => Task.FromResult(_alertas));
        dAlertas.Con(nameof(IConsultaDeAlertas.MarcarRevisada), a => { _revisadas.Add(((string)a[0]!, (int)a[1]!)); return Task.FromResult(_marcada); });
        _anfitrion = await AnfitrionConSesion.Iniciar(
            _sesiones,
            HostWeb.Gestion,
            servicios: s =>
            {
                s.AddSingleton(auditoria);
                s.AddSingleton(alertas);
            });
    }

    public async Task DisposeAsync() => await _anfitrion.DisposeAsync();

    private Task<HttpResponseMessage> Pedir(string ruta)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie(HostWeb.Gestion));
        return _anfitrion.Cliente.SendAsync(solicitud);
    }

    private async Task<HttpResponseMessage> Enviar(string ruta, params (string Campo, string Valor)[] campos)
    {
        using var solicitud = await AnfitrionConSesion.Post(_anfitrion, ruta, AnfitrionConSesion.Cookie(HostWeb.Gestion));
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Content = new FormUrlEncodedContent(campos.Select(c => new KeyValuePair<string, string>(c.Campo, c.Valor)));
        return await _anfitrion.Cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-25")]
    public async Task La_auditoria_muestra_la_hora_de_Lima_codifica_el_detalle_y_ofrece_los_mas_antiguos_con_los_mismos_filtros()
    {
        using var respuesta = await Pedir("/administracion/auditoria?accion=AjusteRegistrado&texto=ajuste");

        var crudo = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("14/11/2026 21:30:05", crudo, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", crudo, StringComparison.Ordinal);
        Assert.Contains("accion=AjusteRegistrado&amp;texto=ajuste&amp;cursor=cursor-siguiente", crudo, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var consulta = Assert.Single(_consultas);
        Assert.Equal(("AjusteRegistrado", "ajuste"), (consulta.Filtro.Accion, consulta.Filtro.Texto));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-25")]
    public async Task Las_fechas_se_leen_como_dias_de_Lima_y_un_filtro_invalido_no_llega_a_la_Aplicacion()
    {
        using var valida = await Pedir("/administracion/auditoria?desde=2026-11-14&hasta=2026-11-14");
        using var mala = await Pedir("/administracion/auditoria?desde=14/11/2026");
        using var usuario = await Pedir("/administracion/auditoria?usuario=no-es-guid");
        using var repetida = await Pedir("/administracion/auditoria?accion=a&accion=b");
        using var invertido = await Pedir("/administracion/auditoria?desde=2026-11-15&hasta=2026-11-14");
        using var ajeno = await Pedir("/administracion/auditoria?rol=administrador");

        var consulta = Assert.Single(_consultas);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 5, 0, 0, TimeSpan.Zero), consulta.Filtro.Desde!.Value.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 5, 0, 0, TimeSpan.Zero), consulta.Filtro.Hasta!.Value.ToUniversalTime());
        foreach (var respuesta in new[] { mala, usuario, repetida, invertido, ajeno })
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-25")]
    public async Task Si_el_Inventario_no_respondio_se_avisa_y_se_muestran_los_del_Ecommerce()
    {
        _pagina = Resultado<PaginaDeRegistros>.Exito(new PaginaDeRegistros([Registro(null)], null, faltanDelInventario: true));

        using var respuesta = await Pedir("/administracion/auditoria");

        Assert.Contains("No pudimos leer los registros del Inventario", WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-25")]
    [InlineData(CategoriaDeError.Prohibido, HttpStatusCode.Forbidden)]
    [InlineData(CategoriaDeError.NoDisponible, HttpStatusCode.ServiceUnavailable)]
    public async Task Un_rol_sin_permiso_o_la_caida_del_servicio_se_dicen_con_el_estado_estandar(CategoriaDeError categoria, HttpStatusCode estado)
    {
        _pagina = Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(categoria, "x"));

        using var respuesta = await Pedir("/administracion/auditoria");

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.DoesNotContain("Exception", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-25")]
    public async Task Una_auditoria_vacia_se_dice_con_su_estado()
    {
        _pagina = Resultado<PaginaDeRegistros>.Exito(new PaginaDeRegistros([], null));

        using var respuesta = await Pedir("/administracion/auditoria");

        Assert.Contains("Sin registros", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-27")]
    public async Task Las_alertas_muestran_el_motivo_del_catalogo_su_estado_y_el_contador_visto_en_el_formulario_de_revisar()
    {
        using var respuesta = await Pedir("/administracion/alertas");

        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Pendiente de revisar", html, StringComparison.Ordinal);
        Assert.Contains("name=\"contadorVisto\" value=\"12\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"alertaId\" value=\"inv:a1000000-0000-4000-8000-000000000001\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-27")]
    public async Task Marcar_una_alerta_envia_el_contador_visto_y_vuelve_con_un_aviso()
    {
        using var respuesta = await Enviar("/administracion/alertas/revisada", ("alertaId", "inv:a1000000-0000-4000-8000-000000000001"), ("contadorVisto", "12"));

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/administracion/alertas?aviso=revisada", respuesta.Headers.Location?.OriginalString);
        Assert.Equal([("inv:a1000000-0000-4000-8000-000000000001", 12)], _revisadas);
        using var pantalla = await Pedir("/administracion/alertas?aviso=revisada");
        Assert.Contains("Marcamos la alerta como revisada.", WebUtility.HtmlDecode(await pantalla.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-27")]
    [InlineData("12abc")]
    [InlineData("-1")]
    [InlineData("")]
    public async Task Un_contador_invalido_o_un_campo_de_mas_no_llega_a_la_Aplicacion(string contador)
    {
        using var invalido = await Enviar("/administracion/alertas/revisada", ("alertaId", "inv:x"), ("contadorVisto", contador));
        using var deMas = await Enviar("/administracion/alertas/revisada", ("alertaId", "inv:x"), ("contadorVisto", "1"), ("clase", "tecnica"));

        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deMas.StatusCode);
        Assert.Empty(_revisadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-27")]
    public async Task Una_alerta_que_no_se_puede_marcar_se_dice_sin_repetir_el_motivo_tecnico()
    {
        _marcada = Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible"));

        using var respuesta = await Enviar("/administracion/alertas/revisada", ("alertaId", "inv:x"), ("contadorVisto", "1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Contains("No pudimos marcar la alerta como revisada.", WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }
}
