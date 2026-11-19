using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>La página del carrito (visitante o cuenta): sus líneas, el subtotal, los avisos y si hay una compra en curso. Los precios y la disponibilidad ya vienen del servidor; la pantalla solo los pinta.</summary>
public sealed class CarritoVista : ModeloBaseDePagina
{
    public VistaDelCarrito Carrito { get; set; } = new([], 0m, [], false);
}

/// <summary>
/// El carrito (plan §4, CAR-01 a CAR-04, CAR-16, CAR-35): <c>GET /carrito</c> (sin efectos), <c>POST /carrito/lineas</c> (agregar), <c>POST /carrito/lineas/{edicionId}</c> (cambiar la cantidad) y <c>POST /carrito/lineas/{edicionId}/quitar</c>. <b>Presentación pura</b>:
/// lee el cuerpo con esquema estricto (un campo desconocido, repetido o un identificador de carrito, checkout o cuenta se rechaza y el último registra un acceso denegado), llama a <see cref="ICarritoDelCliente"/> y traduce su resultado: una pantalla, una redirección
/// <c>303</c> (o <c>HX-Redirect</c> con htmx) tras un <c>POST</c> exitoso o un <c>application/problem+json</c> con el código <c>ECO-…</c>. La cookie <c>__Host-bh-carrito</c> solo se escribe aquí y se rota al fusionar. El token antifalsificación de todo <c>POST</c> lo valida el
/// filtro global antes de la acción. Con una cuenta y una cookie de visitante todavía presente (una fusión aplazada por una compra en curso), cada <c>POST</c> reintenta la fusión primero y nunca la deja fallar la acción.
/// </summary>
[Area("Tienda")]
public sealed class CarritoController(ICarritoDelCliente carrito, IActorActual actor, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    private static readonly string[] CamposDeAgregar = ["edicionId", "cantidad"];

    private static readonly string[] CamposDeCambiar = ["cantidad"];

    private static readonly string[] SinCampos = [];

    [HttpGet("/carrito")]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        if (Request.Query.Count > 0)
        {
            return await Falla(EntradasHttpDelCarrito.CuerpoInvalido);
        }

        var vista = await pagina.Completar(new CarritoVista(), "Tu carrito", cancelacion: cancelacion);
        var resultado = await carrito.Obtener(new SolicitudSobreElCarrito(CookieDelCarrito.Leer(Request)), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!);
        }

        AplicarLaCookie(resultado.Valor.CookieNueva, resultado.Valor.BorrarLaCookie || (CookieDelCarrito.HayCookie(Request) && CookieDelCarrito.Leer(Request) is null));
        vista.Carrito = resultado.Valor.Carrito;
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    [HttpPost("/carrito/lineas")]
    public async Task<IActionResult> Agregar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeAgregar, cancelacion);
        if (campos is null)
        {
            return await Falla(error!);
        }

        if (!EntradasHttpDelCarrito.TryGuid(campos, "edicionId", out var edicionId))
        {
            return await Falla(campos.ContainsKey("edicionId") ? EntradasHttpDelCarrito.Invalido("edicionId") : EntradasHttpDelCarrito.Falta("edicionId"));
        }

        if (!EntradasHttpDelCarrito.TryEntero(campos, "cantidad", 1, 99, out var cantidad))
        {
            return await Falla(campos.ContainsKey("cantidad") ? EntradasHttpDelCarrito.Invalido("cantidad") : EntradasHttpDelCarrito.Falta("cantidad"));
        }

        await ReintentarLaFusion(cancelacion);
        return await Terminar(await carrito.AgregarLinea(new AgregarLineaEntrada(edicionId, cantidad, CookieDelCarrito.Leer(Request)), cancelacion));
    }

    [HttpPost("/carrito/lineas/{edicionId}")]
    public async Task<IActionResult> Cambiar(string edicionId, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeCambiar, cancelacion);
        if (campos is null)
        {
            return await Falla(error!);
        }

        if (!Guid.TryParseExact(edicionId, "D", out var edicion) || edicion == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("edicionId"));
        }

        if (!EntradasHttpDelCarrito.TryEntero(campos, "cantidad", 1, 99, out var cantidad))
        {
            return await Falla(campos.ContainsKey("cantidad") ? EntradasHttpDelCarrito.Invalido("cantidad") : EntradasHttpDelCarrito.Falta("cantidad"));
        }

        await ReintentarLaFusion(cancelacion);
        return await Terminar(await carrito.ModificarLinea(new ModificarLineaEntrada(edicion, cantidad, CookieDelCarrito.Leer(Request)), cancelacion));
    }

    [HttpPost("/carrito/lineas/{edicionId}/quitar")]
    public async Task<IActionResult> Quitar(string edicionId, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, SinCampos, cancelacion);
        if (campos is null)
        {
            return await Falla(error!);
        }

        if (!Guid.TryParseExact(edicionId, "D", out var edicion) || edicion == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("edicionId"));
        }

        await ReintentarLaFusion(cancelacion);
        return await Terminar(await carrito.EliminarLinea(new EliminarLineaEntrada(edicion, CookieDelCarrito.Leer(Request)), cancelacion));
    }

    // ---------- Piezas ----------

    /// <summary>
    /// Con una cuenta y la cookie de un visitante todavía presente (una fusión aplazada), la reintenta antes de la acción: es idempotente y <b>nunca</b> impide la acción. La cookie se borra solo cuando la Aplicación lo dice.
    /// </summary>
    private async Task ReintentarLaFusion(CancellationToken cancelacion)
    {
        if (actor.Actual is not { Tipo: TipoDeActor.Usuario } || CookieDelCarrito.Leer(Request) is not { } cookie)
        {
            return;
        }

        try
        {
            var fusion = await carrito.FusionarCarritoDeVisitante(cookie, cancelacion);
            if (fusion.EsExito)
            {
                AplicarLaCookie(null, fusion.Valor.BorrarLaCookie);
            }
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // La fusión nunca impide la acción de la persona: se reintenta en el siguiente POST.
        }
    }

    private async Task<IActionResult> Terminar(Resultado<ResultadoDeCarrito> resultado)
    {
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!);
        }

        AplicarLaCookie(resultado.Valor.CookieNueva, resultado.Valor.BorrarLaCookie);
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult("/carrito");
        }

        // POST/Redirect/GET: 303 See Other, para que el navegador pida la pantalla con GET.
        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = "/carrito";
        return new EmptyResult();
    }

    private void AplicarLaCookie(string? cookieNueva, bool borrar) => CookieDelCarrito.Aplicar(Response, cookieNueva, borrar);

    private async Task<IActionResult> Falla(ErrorDeAplicacion error)
    {
        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
