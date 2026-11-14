using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// Declara que una página o acción exige el desafío de verificación de persona (SEG-31) para la <see cref="Accion"/> indicada:
/// ingreso, registro, hoja de reclamación, reserva (checkout) o asistente. Es lo único que permite cargar el script de Turnstile
/// y lo único que <c>web-base</c> consulta para abrir la política de contenido (WEB-40): sin este atributo, la página no lo carga.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class VerificacionDePersonaAttribute(AccionProtegida accion) : Attribute
{
    public AccionProtegida Accion { get; } = accion;
}

/// <summary>La clave de sitio de Turnstile (pública, a diferencia del secreto); se valida igual para que un valor extraño no llegue al marcado.</summary>
public sealed partial class OpcionesDeVerificacionWeb
{
    public const string VariableDeLaClaveDeSitio = "BABELHUB__Seguridad__Turnstile__ClaveDeSitio";

    public OpcionesDeVerificacionWeb(string claveDeSitio)
    {
        if (string.IsNullOrWhiteSpace(claveDeSitio) || !FormaDeLaClave().IsMatch(claveDeSitio))
        {
            throw new ConfiguracionDeSecretosInvalidaException([$"{VariableDeLaClaveDeSitio} falta o no tiene la forma de una clave de sitio de Turnstile."]);
        }

        ClaveDeSitio = claveDeSitio;
    }

    public string ClaveDeSitio { get; }

    public static OpcionesDeVerificacionWeb Cargar(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        return new OpcionesDeVerificacionWeb(configuracion["Seguridad:Turnstile:ClaveDeSitio"] ?? string.Empty);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaClave();
}

public static class VerificacionDePersonaWeb
{
    /// <summary>El único origen que se abre en la política de contenido, y solo en las páginas que declaran la verificación (script y marco).</summary>
    public const string OrigenDeTurnstile = "https://challenges.cloudflare.com";

    public const string UrlDelScript = OrigenDeTurnstile + "/turnstile/v0/api.js";

    /// <summary>El campo del formulario con el token; la Api lo lee igual que la cabecera <c>X-Verificacion-De-Persona</c> (htmx).</summary>
    public const string CampoDelToken = "cf-turnstile-response";

    /// <summary>La acción que declara la página del endpoint actual, o <c>null</c> si no exige verificación.</summary>
    public static AccionProtegida? AccionDeLaPagina(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return contexto.GetEndpoint()?.Metadata.GetMetadata<VerificacionDePersonaAttribute>()?.Accion;
    }

    /// <summary>
    /// Lo que la política de contenido de <c>web-base</c> agrega a <c>script-src</c> y <c>frame-src</c> para esta respuesta: el origen de
    /// Turnstile si la página lo exige y nada en cualquier otra (catálogo, pago y demás páginas).
    /// </summary>
    public static string? OrigenParaLaPoliticaDeContenido(this HttpContext contexto) =>
        contexto.AccionDeLaPagina() is null ? null : OrigenDeTurnstile;

    public static IServiceCollection AgregarVerificacionDePersonaWeb(this IServiceCollection servicios, OpcionesDeVerificacionWeb opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton(opciones);
        return servicios;
    }
}

/// <summary>
/// <c>&lt;verificacion-de-persona /&gt;</c>: dibuja el desafío de Turnstile y su script solo si el endpoint declara
/// <see cref="VerificacionDePersonaAttribute"/>; en cualquier otra página no produce nada. La acción sale del atributo, nunca de un
/// valor de la vista, y los valores van como atributos codificados.
/// </summary>
[HtmlTargetElement("verificacion-de-persona")]
public sealed class VerificacionDePersonaTagHelper(OpcionesDeVerificacionWeb opciones) : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (ViewContext.HttpContext.AccionDeLaPagina() is not { } accion)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.Attributes.SetAttribute("class", "cf-turnstile");
        output.Attributes.SetAttribute("data-sitekey", opciones.ClaveDeSitio);
        output.Attributes.SetAttribute("data-action", accion.NombreEstable());
        output.Attributes.SetAttribute("data-response-field-name", VerificacionDePersonaWeb.CampoDelToken);
        output.Content.Clear();
        output.PostElement.AppendHtml($"<script src=\"{VerificacionDePersonaWeb.UrlDelScript}\" async defer></script>");
    }
}
