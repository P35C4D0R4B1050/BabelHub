using System.Text.Encodings.Web;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Web;

public sealed class VerificacionDePersonaWebTests
{
    private const string ClaveDeSitio = "0x4AAAAAAAbCdEfGhIjKlMn";

    private static DefaultHttpContext Contexto(params object[] metadatos)
    {
        var contexto = new DefaultHttpContext();
        contexto.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadatos), "pagina"));
        return contexto;
    }

    private static string Dibujar(HttpContext contexto)
    {
        var vista = new ViewContext(
            new ActionContext(contexto, new RouteData(), new ActionDescriptor()),
            new VistaFalsa(),
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            new TempDataDictionary(contexto, new ProveedorDeTempDataFalso()),
            TextWriter.Null,
            new HtmlHelperOptions());
        var ayudante = new VerificacionDePersonaTagHelper(new OpcionesDeVerificacionWeb(ClaveDeSitio)) { ViewContext = vista };
        var salida = new TagHelperOutput(
            "verificacion-de-persona",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        ayudante.Process(new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "id"), salida);

        using var escritor = new StringWriter();
        salida.WriteTo(escritor, HtmlEncoder.Default);
        return escritor.ToString();
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData(AccionProtegida.Ingreso, "ingreso")]
    [InlineData(AccionProtegida.HojaDeReclamacion, "hoja-de-reclamacion")]
    [InlineData(AccionProtegida.Reserva, "reserva")]
    public void UnaPaginaQueDeclaraLaVerificacion_DibujaElDesafioConSuAccionYElScriptDeTurnstile(AccionProtegida accion, string nombre)
    {
        var html = Dibujar(Contexto(new VerificacionDePersonaAttribute(accion)));

        Assert.Contains("class=\"cf-turnstile\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-sitekey=\"{ClaveDeSitio}\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-action=\"{nombre}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-response-field-name=\"cf-turnstile-response\"", html, StringComparison.Ordinal);
        Assert.Contains("<script src=\"https://challenges.cloudflare.com/turnstile/v0/api.js\" async defer></script>", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("<script", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public void UnaPaginaSinLaDeclaracion_NoDibujaNadaNiCargaElScript()
    {
        Assert.Equal(string.Empty, Dibujar(Contexto()));
        Assert.Equal(string.Empty, Dibujar(new DefaultHttpContext()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public void LaPoliticaDeContenidoSoloAbreElOrigenDeTurnstileEnLasPaginasQueLoDeclaran()
    {
        Assert.Equal(
            "https://challenges.cloudflare.com",
            Contexto(new VerificacionDePersonaAttribute(AccionProtegida.Registro)).OrigenParaLaPoliticaDeContenido());
        Assert.Null(Contexto().OrigenParaLaPoliticaDeContenido());
        Assert.Null(new DefaultHttpContext().OrigenParaLaPoliticaDeContenido());
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("\"><script>alert(1)</script>")]
    [InlineData("clave con espacios 12345")]
    public void UnaClaveDeSitioConFormaExtrana_NoSePermiteYNoLlegaAlMarcado(string clave)
    {
        var error = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => new OpcionesDeVerificacionWeb(clave));

        Assert.Contains("BABELHUB__Seguridad__Turnstile__ClaveDeSitio", error.Message, StringComparison.Ordinal);
        if (clave.Length > 0)
        {
            Assert.DoesNotContain(clave, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LaClaveDeSitioSeLeeDeLaConfiguracion()
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Seguridad:Turnstile:ClaveDeSitio"] = ClaveDeSitio }).Build();

        Assert.Equal(ClaveDeSitio, OpcionesDeVerificacionWeb.Cargar(configuracion).ClaveDeSitio);
        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDeVerificacionWeb.Cargar(new ConfigurationBuilder().Build()));
    }

    private sealed class VistaFalsa : IView
    {
        public string Path => "falsa";

        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }

    private sealed class ProveedorDeTempDataFalso : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
