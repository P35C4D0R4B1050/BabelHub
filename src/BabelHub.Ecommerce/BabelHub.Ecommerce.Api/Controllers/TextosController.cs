using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

/// <summary>Lo que la pantalla de aceptación muestra: la versión vigente de los tres textos y, si la persona acaba de enviar otra, un aviso de que cambió mientras leía.</summary>
public sealed class AceptarTextosVista : ModeloBaseDePagina
{
    public VersionParaAceptar Version { get; set; } = null!;

    public bool LaVersionCambio { get; set; }
}

/// <summary>
/// La pantalla mínima para aceptar los textos legales vigentes (T38, WEB-34, WEB-41, AC-WEB-34): <c>GET /textos/aceptar</c> (de la capacidad de leer los textos) muestra los tres textos con su versión y
/// <c>POST /textos/aceptar</c> (de la capacidad de aceptar) envía <b>el identificador de la versión mostrada</b>; Identidad la compara con la vigente y rechaza una que se publicó mientras la persona leía.
/// Ninguna de las dos se guarda como retorno. Tras aceptar vuelve, una sola vez, a donde iba (<see cref="IPoliticaDeAccesoPorTextos.TomarRetorno"/>) o a la portada. Existe en los dos hosts. Sin reglas: lo
/// que vale lo decide Identidad (<see cref="IAceptacionDeTextos"/>).
/// </summary>
[SinRetorno]
public sealed class TextosController(IAceptacionDeTextos aceptacion, IPoliticaDeAccesoPorTextos politica, ContextoDePagina pagina, OpcionesDelHostWeb host) : Controller
{
    private const string Portada = "/";

    [HttpGet("/textos/aceptar")]
    [PermitidaConTextosPendientes(CapacidadWeb.TextosLegales)]
    [RequiereSesion]
    public async Task<IActionResult> Ver(CancellationToken cancelacion)
    {
        // Si la política no marcó los textos como pendientes, no hay nada que aceptar: a la portada.
        if (HttpContext.Items[ContextoDePagina.ClaveTextosPendientes] is not true)
        {
            return new LocalRedirectResult(Portada);
        }

        return await Mostrar(laVersionCambio: false, cancelacion);
    }

    [HttpPost("/textos/aceptar")]
    [PermitidaConTextosPendientes(CapacidadWeb.Aceptar)]
    [RequiereSesion]
    public async Task<IActionResult> Aceptar([FromForm] string? version, CancellationToken cancelacion)
    {
        var actor = HttpContext.ActorDeLaSolicitud();
        if (actor is null)
        {
            return HttpContext.RedirigirAlIngreso();
        }

        switch (await aceptacion.Aceptar(actor, version ?? string.Empty, cancelacion))
        {
            case ResultadoDeAceptacion.Aceptada:
                string? retorno = null;
                try
                {
                    retorno = CookieDeSesionWeb.Leer(Request, host.Host) is { } cookie ? await politica.TomarRetorno(cookie, actor.Rol, cancelacion) : null;
                }
                catch (Exception) when (!cancelacion.IsCancellationRequested)
                {
                    // La aceptación ya quedó registrada: perder el retorno solo lleva a la portada.
                }

                var destino = retorno ?? Portada;
                Response.Headers.CacheControl = "no-store";
                return Request.EsHtmx() ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
            case ResultadoDeAceptacion.VersionCambiada:
                return await Mostrar(laVersionCambio: true, cancelacion);
            default:
                return Request.EsHtmx() ? new HxRedirectResult("/no-disponible") : new LocalRedirectResult("/no-disponible");
        }
    }

    private async Task<IActionResult> Mostrar(bool laVersionCambio, CancellationToken cancelacion)
    {
        var vigente = await aceptacion.VersionVigente(cancelacion);
        if (!vigente.EsExito)
        {
            var problema = new ProblemaDto { Code = $"ECO-{CodigosDeError.ServicioNoDisponible}", Status = StatusCodes.Status503ServiceUnavailable, TraceId = HttpContext.TraceIdentifier };
            return TraductorDeErrores.Traducir(problema, parteAfectada: "No podemos mostrar los textos ahora.", rutaDeReintento: "/textos/aceptar").ComoResultado(this);
        }

        Response.Headers.CacheControl = "no-store";
        var modelo = await pagina.Completar(new AceptarTextosVista { Version = vigente.Valor, LaVersionCambio = laVersionCambio }, "Aceptar los textos legales", cancelacion: cancelacion);
        return View("Aceptar", modelo);
    }
}
