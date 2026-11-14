using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Web.Estados;

/// <summary>
/// <c>&lt;vc:bh-estado modelo="…" /&gt;</c>: el único componente de los estados estándar de pantalla (WEB-06, D15). Una pantalla de datos
/// no escribe sus propios avisos. Los textos se codifican en la vista; nunca se escribe marcado desde aquí.
/// </summary>
public sealed class BhEstadoViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(EstadoVista modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        return View(modelo);
    }
}

/// <summary><c>&lt;vc:bh-disponibilidad modelo="…" /&gt;</c>: la etiqueta de disponibilidad con texto, icono y color (WEB-13).</summary>
public sealed class BhDisponibilidadViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(DisponibilidadVista modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        return View(modelo);
    }
}
