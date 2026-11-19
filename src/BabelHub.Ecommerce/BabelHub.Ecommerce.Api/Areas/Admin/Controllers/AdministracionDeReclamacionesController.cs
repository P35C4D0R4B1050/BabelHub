using System.Globalization;
using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>El listado de hojas de la administración: sin ningún dato personal (REC-10).</summary>
public sealed class HojasAdministradasVista : ModeloBaseDePagina
{
    public PaginaDeHojas Pagina { get; set; } = null!;

    public EstadoDeLaHoja? Estado { get; set; }

    public bool HayAnterior => Pagina.Pagina > 1;

    public bool HaySiguiente => (long)Pagina.Pagina * Pagina.Tamano < Pagina.Total;

    public static string Aviso(AvisoDeLaRespuesta? aviso) => aviso switch
    {
        AvisoDeLaRespuesta.Pendiente => "Aviso pendiente de enviar",
        AvisoDeLaRespuesta.Enviado => "Aviso enviado",
        _ => "—",
    };
}

/// <summary>El detalle de una hoja con sus datos personales (REC-15): solo la vista de la administración los lee; la consulta quedó auditada.</summary>
public sealed class DetalleDeHojaVista : ModeloBaseDePagina
{
    public DetalleDeHoja Hoja { get; set; } = null!;
}

/// <summary>
/// La administración de las hojas de reclamación (plan §4, REC-07 a REC-10, REC-12, REC-13, REC-15, REC-16; T16): <c>GET /administracion/reclamaciones</c> (listado sin datos personales), <c>GET …/{id}</c> (detalle con datos personales, <b>auditado en la misma transacción</b>), y
/// <c>POST …/{id}/responder</c>, <c>…/{id}/descartar</c> y <c>…/{id}/reintentar-aviso</c>. <b>Presentación pura</b>: <see cref="IAdministracionDeReclamaciones"/> decide quién puede (solo la administración central: otro rol recibe <c>ECO-PROHIBIDO</c> y un acceso denegado por cada intento) y qué estado admite
/// cada operación. Lee el cuerpo con esquema estricto, traduce los errores a <c>ECO-REC-…</c> y <c>ECO-…</c> y nunca repite la respuesta ni los datos personales en un error. Todas las respuestas son <c>no-store</c>.
/// </summary>
[Area("Admin")]
[RequestSizeLimit(EntradasHttpDelCarrito.TopeDelCuerpo)]
public sealed class AdministracionDeReclamacionesController(IAdministracionDeReclamaciones administracion, ContextoDePagina pantalla, OpcionesDeErrores errores) : Controller
{
    public const string Ruta = "/administracion/reclamaciones";

    private const int TamanoPorOmision = 20;

    [HttpGet(Ruta)]
    public async Task<IActionResult> Index([FromQuery] string? estado, [FromQuery] string? pagina, [FromQuery] string? tamano, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["estado", "pagina", "tamano"]) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        EstadoDeLaHoja? filtro;
        switch (estado)
        {
            case null:
                filtro = null;
                break;
            case "pendiente":
                filtro = EstadoDeLaHoja.Pendiente;
                break;
            case "respondido":
                filtro = EstadoDeLaHoja.Respondido;
                break;
            case "descartado":
                filtro = EstadoDeLaHoja.Descartado;
                break;
            default:
                return await Falla(EntradasHttpDelCarrito.Invalido("estado"), cancelacion);
        }

        if (!TryEntero(pagina, 1, out var numeroDePagina))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pagina"), cancelacion);
        }

        if (!TryEntero(tamano, TamanoPorOmision, out var tamanoDePagina))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("tamano"), cancelacion);
        }

        var resultado = await administracion.Listar(new ListarHojasEntrada(filtro, numeroDePagina, tamanoDePagina), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pantalla.Completar(new HojasAdministradasVista { Pagina = resultado.Valor, Estado = filtro }, "Hojas de reclamación", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    [HttpGet(Ruta + "/{id:guid}")]
    public async Task<IActionResult> Detalle(Guid id, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        var resultado = await administracion.Obtener(id, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pantalla.Completar(new DetalleDeHojaVista { Hoja = resultado.Valor }, $"Hoja {resultado.Valor.Numero}", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("Detalle", vista);
    }

    [HttpPost(Ruta + "/{id:guid}/responder")]
    public async Task<IActionResult> Responder(Guid id, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, ["respuesta"], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        return await Terminar(await administracion.Responder(new ResponderHojaEntrada(id, campos.GetValueOrDefault("respuesta")), cancelacion), id, cancelacion);
    }

    [HttpPost(Ruta + "/{id:guid}/descartar")]
    public async Task<IActionResult> Descartar(Guid id, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, [], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        return await Terminar(await administracion.Descartar(id, cancelacion), id, cancelacion);
    }

    [HttpPost(Ruta + "/{id:guid}/reintentar-aviso")]
    public async Task<IActionResult> ReintentarAviso(Guid id, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, [], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        return await Terminar(await administracion.ReintentarAviso(id, cancelacion), id, cancelacion);
    }

    // ---------- Piezas ----------

    private static bool TryEntero(string? texto, int porOmision, out int valor)
    {
        valor = porOmision;
        return texto is null || (texto.Length is > 0 and <= 9 && texto.All(char.IsAsciiDigit) && int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor));
    }

    /// <summary>Una operación hecha vuelve al detalle de la hoja (303, o <c>HX-Redirect</c> con htmx): el resultado no lleva datos personales.</summary>
    private async Task<IActionResult> Terminar(Resultado<ResultadoDeLaHoja> resultado, Guid id, CancellationToken cancelacion)
    {
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var destino = $"{Ruta}/{id:D}";
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
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
