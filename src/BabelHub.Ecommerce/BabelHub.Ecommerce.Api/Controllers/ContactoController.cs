using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Estados;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

/// <summary>La página de contacto: el teléfono, el correo y la dirección del comercio, que ya trae el modelo base (COM-06).</summary>
public sealed class ContactoVista : ModeloBaseDePagina;

/// <summary>
/// <c>GET /contacto</c> (COM-06): solo información de contacto, sin formulario ni servicios de terceros. Existe en los dos hosts. <b>No</b> lleva el atributo de la política de textos pendientes
/// ni el del paso pendiente del ingreso (DD-79): con textos por aceptar o un segundo factor pendiente recibe el desvío común de WEB-34 y WEB-46, y los datos se ven en el pie. Presentación pura: el
/// contacto lo valida el arranque (<c>DatosDeContacto</c>) y llega por <see cref="ContextoDePagina"/>.
/// </summary>
public sealed class ContactoController(ContextoDePagina pagina) : Controller
{
    [HttpGet("/contacto")]
    public async Task<IActionResult> Ver(CancellationToken cancelacion)
    {
        var modelo = await pagina.Completar(new ContactoVista(), "Contacto", cancelacion: cancelacion);
        if (modelo.ContactoDelComercio is null)
        {
            // Un anfitrión sin los datos registrados (solo los hosts de prueba) no responde con un error interno.
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("~/Views/Estados/Pantalla.cshtml", await pagina.Completar(new PantallaDeEstadoVista { Estado = EstadoVista.ServicioNoDisponible("No podemos mostrar los datos de contacto ahora.", Request.Path) }, "Contacto", cancelacion: cancelacion));
        }

        return View("Contacto", modelo);
    }
}
