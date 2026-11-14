using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BabelHub.Ecommerce.Api.Web.Layout;

/// <summary>
/// <c>&lt;bh-icono nombre="libro" /&gt;</c>: un icono de trazo fino en línea, decorativo (<c>aria-hidden</c>); el significado lo da el texto
/// vecino o el <c>aria-label</c> del control. El trazado sale de una lista cerrada, nunca de un valor de la vista.
/// </summary>
[HtmlTargetElement("bh-icono", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconoTagHelper : TagHelper
{
    [HtmlAttributeName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [HtmlAttributeName("clase")]
    public string Clase { get; set; } = "size-5";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("xmlns", "http://www.w3.org/2000/svg");
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        output.Attributes.SetAttribute("stroke-width", "1.5");
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        output.Attributes.SetAttribute("class", Clase);
        output.Content.SetHtmlContent($"<path d=\"{Iconos.Trazado(Nombre)}\"></path>");
    }
}
