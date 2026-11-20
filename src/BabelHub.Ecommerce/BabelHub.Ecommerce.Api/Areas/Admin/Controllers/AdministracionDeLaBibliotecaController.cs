using System.Globalization;
using BabelHub.Ecommerce.Api.Biblioteca;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>La biblioteca de un cliente vista por la administración central (T17, T20; BIB-12): la consulta queda auditada y la pantalla lo avisa.</summary>
public sealed class BibliotecaDeUnClienteVista : ModeloBaseDePagina
{
    public Guid CuentaId { get; set; }

    public IReadOnlyList<LibroDeBibliotecaVista> Libros { get; set; } = [];

    public EstadoVista? Estado { get; set; }
}

/// <summary>Una solicitud de ayuda lista para pintar: el acceso (identificador corto), el motivo en español, la fecha y el estado. <b>Sin datos de la cuenta</b> (RNF23).</summary>
public sealed record AyudaVista(Guid AyudaId, Guid AccesoId, string Motivo, string CreadaTexto, bool Abierta, string? CerradaTexto)
{
    public string AccesoCorto => AccesoId.ToString("N")[..8];
}

/// <summary>Las ayudas de la biblioteca para la administración central (T17, T20; BIB-16): abiertas, cerradas o todas, con la restitución de descargas.</summary>
public sealed class AyudasDeBibliotecaVista : ModeloBaseDePagina
{
    public IReadOnlyList<AyudaVista> Ayudas { get; set; } = [];

    public string Filtro { get; set; } = "abiertas";

    public int Pagina { get; set; } = 1;

    public int Tamano { get; set; }

    public int Total { get; set; }

    public EstadoVista? Estado { get; set; }

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => (long)Pagina * Tamano < Total;

    public string Ruta(int numero) => $"/administracion/biblioteca/ayudas?estado={Filtro}&pagina={numero.ToString(CultureInfo.InvariantCulture)}&tamano={Tamano.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// La administración de la biblioteca (T17; BIB-08, BIB-12, BIB-16; plan §4): <c>GET /administracion/clientes/{cuentaId}/biblioteca</c> (consulta <b>auditada</b>), <c>GET /administracion/biblioteca/ayudas</c>, <c>POST /administracion/biblioteca/accesos/{id}/restituir</c> y
/// <c>POST /administracion/biblioteca/ayudas/{id}/cerrar</c>. <b>Presentación pura</b>: <see cref="IAdministracionDeLaBiblioteca"/> decide quién puede (solo la administración central; cualquier otro rol recibe <c>ECO-PROHIBIDO</c> y queda el acceso denegado por cada intento) y audita.
/// Las ayudas no traen datos de la cuenta. Toda respuesta es <c>no-store</c>.
/// </summary>
[Area("Admin")]
public sealed class AdministracionDeLaBibliotecaController(IAdministracionDeLaBiblioteca administracion, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    private const int TamanoPorOmision = 20;

    public const string RutaDeAyudas = "/administracion/biblioteca/ayudas";

    [HttpGet("/administracion/clientes/{cuentaId:guid}/biblioteca")]
    public async Task<IActionResult> BibliotecaDeUnCliente(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.Count > 0)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("consulta"), RutaDeAyudas, cancelacion);
        }

        var resultado = await administracion.ConsultarBibliotecaDeUnCliente(cuentaId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDelCliente(cuentaId), cancelacion);
        }

        var modelo = await pagina.Completar(new BibliotecaDeUnClienteVista { CuentaId = cuentaId, Libros = [.. resultado.Valor.Select(TextosDeBiblioteca.AVista)] }, "Biblioteca del cliente", AreaDeLaPagina.Admin, cancelacion);
        if (modelo.Libros.Count == 0)
        {
            modelo.Estado = EstadoVista.Vacio("Este cliente no tiene libros digitales", "Todavía no compró ningún libro digital.");
        }

        return View("~/Areas/Admin/Views/AdministracionDeLaBiblioteca/BibliotecaDeUnCliente.cshtml", modelo);
    }

    [HttpGet(RutaDeAyudas)]
    public async Task<IActionResult> Ayudas([FromQuery] string? estado, [FromQuery(Name = "pagina")] string? numero, [FromQuery] string? tamano, CancellationToken cancelacion = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.Any(q => q.Value.Count != 1))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("consulta"), RutaDeAyudas, cancelacion);
        }

        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["estado", "pagina", "tamano"]) is { } consulta)
        {
            return await Falla(consulta, RutaDeAyudas, cancelacion);
        }

        EstadoDeLaAyudaDeBiblioteca? filtro;
        var etiqueta = estado ?? "abiertas";
        switch (etiqueta)
        {
            case "abiertas":
                filtro = EstadoDeLaAyudaDeBiblioteca.Abierta;
                break;
            case "cerradas":
                filtro = EstadoDeLaAyudaDeBiblioteca.Cerrada;
                break;
            case "todas":
                filtro = null;
                break;
            default:
                return await Falla(EntradasHttpDelCarrito.Invalido("estado"), RutaDeAyudas, cancelacion);
        }

        if (!TryEntero(numero, 1, out var paginaPedida))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pagina"), RutaDeAyudas, cancelacion);
        }

        if (!TryEntero(tamano, TamanoPorOmision, out var tamanoPedido))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("tamano"), RutaDeAyudas, cancelacion);
        }

        var resultado = await administracion.ListarAyudas(filtro, paginaPedida, tamanoPedido, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeAyudas, cancelacion);
        }

        var modelo = await pagina.Completar(
            new AyudasDeBibliotecaVista
            {
                Ayudas = [.. resultado.Valor.Ayudas.Select(a => new AyudaVista(a.AyudaId, a.AccesoId, TextosDeBiblioteca.Motivo(a.Motivo), FormatoPeruano.FechaYHora(a.CreadaEn), a.Estado == EstadoDeLaAyudaDeBiblioteca.Abierta, a.CerradaEn is { } c ? FormatoPeruano.FechaYHora(c) : null))],
                Filtro = etiqueta,
                Pagina = resultado.Valor.Pagina,
                Tamano = resultado.Valor.Tamano,
                Total = resultado.Valor.Total,
            },
            "Ayudas de la biblioteca",
            AreaDeLaPagina.Admin,
            cancelacion);
        if (modelo.Ayudas.Count == 0)
        {
            modelo.Estado = EstadoVista.Vacio("No hay solicitudes de ayuda", etiqueta == "abiertas" ? "No hay solicitudes abiertas por atender." : "No hay solicitudes con este filtro.");
        }

        return View("~/Areas/Admin/Views/AdministracionDeLaBiblioteca/Ayudas.cshtml", modelo);
    }

    [HttpPost("/administracion/biblioteca/accesos/{id:guid}/restituir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restituir(Guid id, CancellationToken cancelacion = default)
    {
        Response.Headers.CacheControl = "no-store";
        var (campos, error) = await EntradasHttpDeAdministracion.Leer(HttpContext, ["cantidad", "motivo", "ayudaId", "cuentaId"], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, RutaDeAyudas, cancelacion);
        }

        if (!campos.TryGetValue("cantidad", out var cantidadTexto))
        {
            return await Falla(EntradasHttpDelCarrito.Falta("cantidad"), RutaDeAyudas, cancelacion);
        }

        if (cantidadTexto.Length is 0 or > 3 || !cantidadTexto.All(char.IsAsciiDigit) || !int.TryParse(cantidadTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var cantidad))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("cantidad"), RutaDeAyudas, cancelacion);
        }

        if (!campos.TryGetValue("motivo", out var motivo))
        {
            return await Falla(EntradasHttpDelCarrito.Falta("motivo"), RutaDeAyudas, cancelacion);
        }

        Guid? ayudaId = null;
        if (campos.TryGetValue("ayudaId", out var ayudaTexto) && ayudaTexto.Length > 0)
        {
            if (!EntradasHttpDelCarrito.TryGuid(campos, "ayudaId", out var ayuda))
            {
                return await Falla(EntradasHttpDelCarrito.Invalido("ayudaId"), RutaDeAyudas, cancelacion);
            }

            ayudaId = ayuda;
        }

        Guid? cuentaDeRetorno = null;
        if (campos.TryGetValue("cuentaId", out var cuentaTexto) && cuentaTexto.Length > 0)
        {
            if (!EntradasHttpDelCarrito.TryGuid(campos, "cuentaId", out var cuenta))
            {
                return await Falla(EntradasHttpDelCarrito.Invalido("cuentaId"), RutaDeAyudas, cancelacion);
            }

            cuentaDeRetorno = cuenta;
        }

        var resultado = await administracion.RestituirDescargas(new RestituirDescargasEntrada(id, cantidad, motivo, ayudaId), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeAyudas, cancelacion);
        }

        // Solo vuelve a una de las dos pantallas de esta administración (nunca a un destino que mande la solicitud).
        return Volver(cuentaDeRetorno is { } c ? RutaDelCliente(c) : RutaDeAyudas);
    }

    [HttpPost("/administracion/biblioteca/ayudas/{id:guid}/cerrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cerrar(Guid id, CancellationToken cancelacion = default)
    {
        Response.Headers.CacheControl = "no-store";
        var (campos, error) = await EntradasHttpDeAdministracion.Leer(HttpContext, [], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, RutaDeAyudas, cancelacion);
        }

        var resultado = await administracion.CerrarAyuda(id, cancelacion);
        return resultado.EsExito ? Volver(RutaDeAyudas) : await Falla(resultado.Error!, RutaDeAyudas, cancelacion);
    }

    private static string RutaDelCliente(Guid cuentaId) => $"/administracion/clientes/{cuentaId:D}/biblioteca";

    private IActionResult Volver(string ruta)
    {
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(ruta);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = ruta;
        return new EmptyResult();
    }

    private static bool TryEntero(string? texto, int porOmision, out int valor)
    {
        valor = porOmision;
        return texto is null || (texto.Length is > 0 and <= 9 && texto.All(char.IsAsciiDigit) && int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor));
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion)
    {
        if (!PantallasDeBiblioteca.PrefiereHtml(Request))
        {
            await ProblemasDeBiblioteca.Escribir(HttpContext, errores, error);
            return new EmptyResult();
        }

        return await PantallasDeBiblioteca.Escribir(this, pagina, errores, error, rutaDeReintento, AreaDeLaPagina.Admin, cancelacion);
    }
}
