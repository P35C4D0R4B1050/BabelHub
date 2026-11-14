using BabelHub.Ecommerce.Api.Web.Estados;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Rutas de prueba que renderizan los componentes de la interfaz tal como los vería una vista (solo las registra el anfitrión de las pruebas que las piden).</summary>
[ApiController]
[Route("prueba")]
public sealed class ControladorDePruebas : Controller
{
    [HttpGet("estado/{clave}")]
    public IActionResult Estado(string clave) => ViewComponent("BhEstado", new { modelo = clave switch
    {
        "cargando" => EstadoVista.Cargando(),
        "vacio" => EstadoVista.Vacio("Tu carrito está vacío", "Explora el catálogo y encuentra tu próxima lectura.", "Ir al catálogo", "/catalogo"),
        "error" => EstadoVista.ErrorGenerico("/catalogo"),
        "error-con-soporte" => EstadoVista.ErrorGenerico("/catalogo") with { CodigoDeSoporte = "ECO-ERROR-INESPERADO", IdentificadorDeSeguimiento = "abc123" },
        "sin-permiso" => EstadoVista.SinPermiso([new EnlaceDeSalida("Ir al inicio", "/"), new EnlaceDeSalida("Ver mi cuenta", "/cuenta")]),
        "no-disponible" => EstadoVista.ServicioNoDisponible("No podemos validar el stock ahora.", "/catalogo"),
        "exito" => EstadoVista.Exito("Guardado", "Tus cambios se guardaron."),
        "xss" => EstadoVista.ErrorGenerico("/catalogo") with { Mensaje = "<script>alert(1)</script>" },
        _ => throw new ArgumentException("estado desconocido", nameof(clave)),
    } });

    [HttpGet("disponibilidad/{clave}")]
    public IActionResult Disponibilidad(string clave) => ViewComponent("BhDisponibilidad", new { modelo = clave switch
    {
        "en-tu-sucursal" => DisponibilidadVista.EnTuSucursal(),
        "en-otras" => DisponibilidadVista.EnOtrasSucursales(3),
        "agotado" => DisponibilidadVista.Agotado(),
        "al-instante" => DisponibilidadVista.DisponibleAlInstante(),
        "sin-confirmar" => DisponibilidadVista.NoSePuedeConfirmar(),
        _ => throw new ArgumentException("disponibilidad desconocida", nameof(clave)),
    } });
}
