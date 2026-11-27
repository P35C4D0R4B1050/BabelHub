using BabelHub.Ecommerce.Api.Web.Errores;
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
        "en-la-sucursal" => DisponibilidadVista.EnLaSucursal(3),
        "en-otras" => DisponibilidadVista.EnOtrasSucursales(3),
        "agotado" => DisponibilidadVista.Agotado(),
        "al-instante" => DisponibilidadVista.DisponibleAlInstante(),
        "sin-confirmar" => DisponibilidadVista.NoSePuedeConfirmar(),
        _ => throw new ArgumentException("disponibilidad desconocida", nameof(clave)),
    } });
}

/// <summary>El carrito de una prueba: cuenta líneas y detecta cualquier cambio, para comprobar que un error del Inventario no lo toca (WEB-26).</summary>
public sealed class CarritoDePrueba
{
    public int Lineas { get; } = 2;

    public bool SeModifico { get; private set; }

    public void Vaciar() => SeModifico = true;
}

/// <summary>Rutas de prueba de la traducción de errores (T22).</summary>
[ApiController]
[Route("prueba")]
public sealed class ControladorDeErroresDePruebas(CarritoDePrueba? carrito = null) : Controller
{
    [HttpGet("traducir/{codigo}/{estado:int}")]
    public IActionResult Traducir(string codigo, int estado) =>
        BabelHub.Ecommerce.Api.Web.Errores.TraductorDeErrores.Traducir(
            new BabelHub.Shared.Errores.ProblemaDto { Code = codigo, Status = estado, TraceId = Guid.NewGuid().ToString("N") },
            rutaDeReintento: "/prueba")
            .ComoResultado(this);

    [HttpGet("ingreso/{codigo}/{estado:int}")]
    public IActionResult Ingreso(string codigo, int estado) =>
        BabelHub.Ecommerce.Api.Web.Errores.TraductorDeErrores.Traducir(
            new BabelHub.Shared.Errores.ProblemaDto { Code = codigo, Status = estado, TraceId = Guid.NewGuid().ToString("N") },
            BabelHub.Ecommerce.Api.Web.Errores.ContextoDeTraduccion.Ingreso,
            "/ingreso")
            .ComoResultado(this);

    /// <summary>Simula abrir el carrito con el Inventario caído: la excepción se traduce y el carrito no se toca.</summary>
    [HttpGet("carrito")]
    public IActionResult Carrito()
    {
        _ = carrito;
        try
        {
            throw new HttpRequestException("Connection refused 10.0.0.9:8080");
        }
        catch (HttpRequestException excepcion)
        {
            return BabelHub.Ecommerce.Api.Web.Errores.TraductorDeErrores.Traducir(excepcion, "/carrito", "No podemos validar el stock ahora.").ComoResultado(this);
        }
    }
}

/// <summary>Renderiza el botón de descarga como lo haría la pantalla «Mi biblioteca» (T27).</summary>
[ApiController]
[Route("prueba")]
public sealed class ControladorDeDescargaDePruebas : Controller
{
    [HttpGet("boton-de-descarga/{acceso:guid}")]
    public IActionResult Boton(Guid acceso) => ViewComponent("BhBotonDeDescarga", new { accesoId = acceso });
}
