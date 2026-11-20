using BabelHub.Ecommerce.Api.Biblioteca;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Mi biblioteca (T16, T19; BIB-02): los libros digitales del cliente, la página pedida y el estado estándar que los reemplaza (vacío, error, sin permiso, servicio no disponible). Sin la clave del archivo ni ninguna URL.</summary>
public sealed class MiBibliotecaVista : ModeloBaseDePagina
{
    public IReadOnlyList<LibroDeBibliotecaVista> Libros { get; set; } = [];

    public int Pagina { get; set; } = 1;

    public int Tamano { get; set; }

    public int Total { get; set; }

    public EstadoVista? Estado { get; set; }

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => (long)Pagina * Tamano < Total;
}

/// <summary>
/// La biblioteca del cliente (T16; BIB-02, BIB-16; plan §4): <c>GET /biblioteca</c> la lista y <c>POST /biblioteca/{id}/ayuda</c> pide ayuda sobre un libro. <b>Presentación pura</b>: llama a <see cref="IBibliotecaDelCliente"/> e <see cref="IAyudaDeLaBiblioteca"/>, que
/// comprueban rol, textos legales pendientes y titularidad (la cuenta sale de la sesión, nunca de la solicitud); un acceso ajeno o inexistente responde <b>igual</b> (<c>ECO-NO-ENCONTRADO</c>) y queda registrado. La descarga misma es de <see cref="BibliotecaController"/>
/// (<c>POST /biblioteca/{id}/descargar</c>): esta página lleva el formulario, por eso abre <c>form-action</c> al almacén solo aquí (WEB-40). Toda respuesta es <c>no-store</c>.
/// </summary>
[Area("Tienda")]
public sealed class MiBibliotecaController(IBibliotecaDelCliente biblioteca, IAyudaDeLaBiblioteca ayuda, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    private const int TamanoPorOmision = 12;

    [HttpGet(PantallasDeBiblioteca.RutaDeMiBiblioteca)]
    [RequiereSesion]
    [PoliticaDeContenido(ContenidoDeTerceros.DescargaDeLibro)]
    public async Task<IActionResult> Index([FromQuery(Name = "pagina")] string? numero, [FromQuery] string? tamano, CancellationToken cancelacion = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.Any(q => q.Value.Count != 1))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("consulta"), cancelacion);
        }

        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["pagina", "tamano"]) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        if (!TryEntero(numero, 1, out var paginaPedida))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pagina"), cancelacion);
        }

        if (!TryEntero(tamano, TamanoPorOmision, out var tamanoPedido))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("tamano"), cancelacion);
        }

        var resultado = await biblioteca.Listar(paginaPedida, tamanoPedido, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var modelo = await pagina.Completar(
            new MiBibliotecaVista
            {
                Libros = [.. resultado.Valor.Accesos.Select(TextosDeBiblioteca.AVista)],
                Pagina = resultado.Valor.Pagina,
                Tamano = resultado.Valor.Tamano,
                Total = resultado.Valor.Total,
            },
            "Mi biblioteca",
            AreaDeLaPagina.Tienda,
            cancelacion);
        if (modelo.Libros.Count == 0)
        {
            modelo.Estado = EstadoVista.Vacio("Aún no tienes libros digitales", "Cuando compres uno, aparecerá aquí al instante.", "Ver libros digitales", "/catalogo?formato=digital");
        }

        return View("~/Areas/Tienda/Views/MiBiblioteca/Index.cshtml", modelo);
    }

    [HttpPost("/biblioteca/{id:guid}/ayuda")]
    [RequiereSesion]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ayuda(Guid id, CancellationToken cancelacion = default)
    {
        Response.Headers.CacheControl = "no-store";
        var (campos, error) = await EntradasHttpDeAdministracion.Leer(HttpContext, ["motivo"], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!campos.TryGetValue("motivo", out var motivo))
        {
            return await Falla(EntradasHttpDelCarrito.Falta("motivo"), cancelacion);
        }

        var resultado = await ayuda.Pedir(id, motivo, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        if (Request.EsHtmx())
        {
            return new HxRedirectResult(PantallasDeBiblioteca.RutaDeMiBiblioteca);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = PantallasDeBiblioteca.RutaDeMiBiblioteca;
        return new EmptyResult();
    }

    private static bool TryEntero(string? texto, int porOmision, out int valor)
    {
        valor = porOmision;
        return texto is null || (texto.Length is > 0 and <= 9 && texto.All(char.IsAsciiDigit) && int.TryParse(texto, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out valor));
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (!PantallasDeBiblioteca.PrefiereHtml(Request))
        {
            await ProblemasDeBiblioteca.Escribir(HttpContext, errores, error);
            return new EmptyResult();
        }

        return await PantallasDeBiblioteca.Escribir(this, pagina, errores, error, PantallasDeBiblioteca.RutaDeMiBiblioteca, AreaDeLaPagina.Tienda, cancelacion);
    }
}
