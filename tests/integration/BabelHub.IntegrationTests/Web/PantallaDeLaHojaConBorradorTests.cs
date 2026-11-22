using System.Net;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Carrito;
using BabelHub.IntegrationTests.Pagos;
using BabelHub.IntegrationTests.Reclamaciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El formulario del Libro de Reclamaciones al volver de la sesión vencida (web-base WEB-25; AC-WEB-20): con sesión recupera, una sola vez, lo que la persona había escrito y no la identifica (bien, monto, referencia, tipo y detalle);
/// sin sesión no consulta nada; si el almacén falla, el formulario sale vacío; y el texto recuperado se codifica. Los datos personales (REC-04) nunca están en el borrador, así que nunca vuelven.
/// </summary>
public sealed class PantallaDeLaHojaConBorradorTests : IAsyncLifetime
{
    private readonly SesionesWebDePrueba _sesiones = new SesionesWebDePrueba().ConSesionValida();
    private readonly List<string> _consultas = [];
    private Func<IReadOnlyDictionary<string, string>?> _borrador = () => null;
    private AnfitrionWeb _anfitrion = null!;

    public async Task InitializeAsync()
    {
        var borradores = DobleDeMetodos.Crear<IBorradoresDePantalla>(out var doble);
        doble.Con(nameof(IBorradoresDePantalla.Recuperar), a => { _consultas.Add(((Guid)a[0]!) + "|" + a[1]); return Task.FromResult(_borrador()); });
        _anfitrion = await AnfitrionConSesion.Iniciar(
            _sesiones,
            servicios: s =>
            {
                s.AddSingleton<IReclamaciones>(new ReclamacionesFalso());
                s.AddSingleton<IHojasDelConsumidor>(new HojasDelConsumidorFalso());
                s.AddSingleton(borradores);
                s.AddSingleton<IRegistradorDeDenegacionesHttp>(new DenegacionesEspia());
                s.AddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
            });
    }

    public async Task DisposeAsync() => await _anfitrion.DisposeAsync();

    private async Task<string> Pedir(bool conSesion = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/reclamaciones/nueva");
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        if (conSesion)
        {
            solicitud.Headers.TryAddWithoutValidation("Cookie", AnfitrionConSesion.Cookie());
        }

        using var respuesta = await _anfitrion.Cliente.SendAsync(solicitud);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        return await respuesta.Content.ReadAsStringAsync();
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Con_sesion_el_formulario_recupera_lo_escrito_que_no_identifica_a_la_persona()
    {
        _borrador = () => new Dictionary<string, string> { ["BienContratado"] = "Cien años de soledad", ["MontoReclamado"] = "59.90", ["Tipo"] = "queja", ["Detalle"] = "Llegó con las páginas dañadas", ["PedidoReferencia"] = "PED-1" };

        var html = WebUtility.HtmlDecode(await Pedir());

        Assert.Equal([_sesiones.UsuarioId + "|/reclamaciones/nueva"], _consultas);
        Assert.Contains("value=\"Cien años de soledad\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"59.90\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"PED-1\"", html, StringComparison.Ordinal);
        Assert.Contains(">Llegó con las páginas dañadas</textarea>", html, StringComparison.Ordinal);
        Assert.Matches("name=\"tipo\" value=\"queja\"[^>]*checked", html);
        Assert.DoesNotMatch("name=\"tipo\" value=\"reclamo\"[^>]*checked", html);
        // Lo que identifica a la persona (REC-04) sale siempre vacío.
        Assert.DoesNotMatch("name=\"(nombre|documento|domicilio|correo)\"[^>]*value=\"[^\"]", html);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Lo_recuperado_se_codifica_y_no_ejecuta_nada()
    {
        _borrador = () => new Dictionary<string, string> { ["Detalle"] = "</textarea><script>alert(1)</script>", ["BienContratado"] = "\"><img src=x onerror=alert(1)>" };

        var html = await Pedir();

        Assert.DoesNotContain("<script>alert(1)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Sin_sesion_no_se_consulta_ningun_borrador_y_el_formulario_sale_vacio()
    {
        _borrador = () => new Dictionary<string, string> { ["Detalle"] = "de otra persona" };

        var html = await Pedir(conSesion: false);

        Assert.Empty(_consultas);
        Assert.DoesNotContain("de otra persona", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Si_el_almacen_de_borradores_falla_el_formulario_sale_vacio_y_responde_200()
    {
        _borrador = () => throw new InvalidOperationException("la base no responde");

        var html = await Pedir();

        Assert.Contains("Libro de Reclamaciones", html, StringComparison.Ordinal);
        Assert.DoesNotContain("la base no responde", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task Al_vencer_la_sesion_al_enviar_la_hoja_se_conserva_solo_lo_que_no_identifica_y_se_lleva_al_ingreso()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        var borradores = new BorradoresDePrueba(sesiones.UsuarioId);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(
            sesiones,
            servicios: s =>
            {
                s.AddSingleton<IReclamaciones>(new ReclamacionesFalso());
                s.AddSingleton<IHojasDelConsumidor>(new HojasDelConsumidorFalso());
                s.AddSingleton<IBorradoresDePantalla>(borradores);
                s.AddSingleton<IRegistradorDeDenegacionesHttp>(new DenegacionesEspia());
                s.AddSingleton<BabelHub.Ecommerce.Application.Auditoria.IDenegacionDeAcceso>(new DenegacionesDePrueba());
                s.AddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
            });
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/reclamaciones/nueva", AnfitrionConSesion.Cookie());
        solicitud.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["nombre"] = "Ana Pérez Quispe",
            ["documento"] = "12345678",
            ["domicilio"] = "Jr. Lima 123, Ayacucho",
            ["correo"] = "ana@example.com",
            ["bienContratado"] = "Cien años de soledad",
            ["montoReclamado"] = "59.90",
            ["tipo"] = "reclamo",
            ["detalle"] = "Llegó con las páginas dañadas",
            ["pedidoReferencia"] = "PED-1",
            ["claveDelFormulario"] = "clave-de-un-solo-uso",
        });
        sesiones.ConCarga(EstadoDeCarga.VencidaPorInactividad);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        var (plantilla, campos) = Assert.Single(borradores.Guardados);
        Assert.Equal("/reclamaciones/nueva", plantilla);
        Assert.Equal(
            new Dictionary<string, string> { ["BienContratado"] = "Cien años de soledad", ["MontoReclamado"] = "59.90", ["Tipo"] = "reclamo", ["Detalle"] = "Llegó con las páginas dañadas", ["PedidoReferencia"] = "PED-1" }.OrderBy(p => p.Key),
            campos.OrderBy(p => p.Key));
    }
}
