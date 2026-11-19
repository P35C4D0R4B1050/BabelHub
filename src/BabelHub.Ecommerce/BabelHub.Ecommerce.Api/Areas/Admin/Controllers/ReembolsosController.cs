using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>La pantalla de reembolsos pendientes: la página pedida y el tamaño con que se lista.</summary>
public sealed class ReembolsosVista : ModeloBaseDePagina
{
    public PaginaDeReembolsos Pagina { get; set; } = null!;

    public bool HayAnterior => Pagina.Pagina > 1;

    public bool HaySiguiente => (long)Pagina.Pagina * Pagina.Tamano < Pagina.Total;

    public static string Origen(OrigenDelReembolso origen) => origen switch
    {
        OrigenDelReembolso.Compensacion => "Compensación de un cobro",
        OrigenDelReembolso.Anulacion => "Anulación del pedido",
        OrigenDelReembolso.CancelacionDelCliente => "Cancelación del cliente",
        _ => "Devolución",
    };

    public static string Motivo(MotivoPendienteDelReembolso? motivo) => motivo switch
    {
        MotivoPendienteDelReembolso.SinRespuesta => "La pasarela no respondió",
        MotivoPendienteDelReembolso.RechazadoPorPasarela => "La pasarela lo rechazó",
        MotivoPendienteDelReembolso.NoSoportado => "La pasarela no reembolsa con ese medio",
        _ => "Aún no se pide",
    };
}

/// <summary>
/// Los reembolsos pendientes de la plataforma para la administración central (T29, T34; PAG-10, AC-PAG-08): <c>GET /administracion/reembolsos</c> los lista con su origen, monto y motivo (<b>sin datos de pago ni de la persona</b>) y
/// <c>POST /administracion/reembolsos/reintentar</c> reintenta uno. Presentación pura: <see cref="IReembolsos"/> decide quién puede (solo la administración central; otro rol recibe un 403 y queda el acceso denegado) y respeta la espera tras un «sin respuesta».
/// </summary>
[Area("Admin")]
[RequestSizeLimit(1024)]
public sealed class ReembolsosController(IReembolsos reembolsos, ContextoDePagina pantalla, OpcionesDeErrores errores) : Controller
{
    public const string Ruta = "/administracion/reembolsos";

    private const int TamanoPorOmision = 20;

    [HttpGet(Ruta)]
    public async Task<IActionResult> Index([FromQuery] int pagina = 1, [FromQuery] int tamano = TamanoPorOmision, CancellationToken cancelacion = default)
    {
        var resultado = await reembolsos.ListarPendientes(pagina, tamano, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pantalla.Completar(new ReembolsosVista { Pagina = resultado.Valor }, "Reembolsos pendientes", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    [HttpPost(Ruta + "/reintentar")]
    public async Task<IActionResult> Reintentar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, ["reembolsoId"], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!EntradasHttpDelCarrito.TryGuid(campos, "reembolsoId", out var id))
        {
            return await Falla(campos.ContainsKey("reembolsoId") ? EntradasHttpDelCarrito.Invalido("reembolsoId") : EntradasHttpDelCarrito.Falta("reembolsoId"), cancelacion);
        }

        var resultado = await reembolsos.Reintentar(id, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(Ruta);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = Ruta;
        return new EmptyResult();
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await PantallasDelCarrito.Escribir(this, pantalla, errores, error, Ruta, cancelacion);
        }

        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
