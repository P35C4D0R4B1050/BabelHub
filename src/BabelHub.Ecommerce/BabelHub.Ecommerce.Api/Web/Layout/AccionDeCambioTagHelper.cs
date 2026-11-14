using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BabelHub.Ecommerce.Api.Web.Layout;

/// <summary>
/// <c>&lt;bh-accion-de-cambio&gt;…&lt;/bh-accion-de-cambio&gt;</c>: envuelve una acción que cambia datos o paga. Mientras la persona tiene
/// pendiente aceptar los textos legales (<see cref="ModeloBaseDePagina.TextosPendientes"/>) no renderiza nada (WEB-42); si la página no
/// tiene el modelo base falla cerrado y tampoco la renderiza. Sin textos pendientes deja pasar su contenido sin envoltorio. Esto es
/// solo presentación: el servidor rechaza igualmente la acción directa (<c>ECO-TEXTOS-PENDIENTES</c>, T37).
/// </summary>
[HtmlTargetElement("bh-accion-de-cambio")]
public sealed class AccionDeCambioTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = null;
        if (ViewContext.ViewData.Model is not ModeloBaseDePagina { TextosPendientes: false })
        {
            output.SuppressOutput();
        }
    }
}
