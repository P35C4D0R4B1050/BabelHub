using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El Libro de Reclamaciones del consumidor (plan §4, REC-02, REC-04, REC-05, REC-10, REC-11, REC-14; T15): <c>GET</c> y <c>POST /reclamaciones/nueva</c> (sin sesión, con la clave de un solo uso del servidor y la constancia con <c>no-store</c>) y las hojas de quien la escribió
/// (<c>GET /reclamaciones/mis-hojas</c> y <c>GET /reclamaciones/{numero}</c>, el número con formato <c>AAAA-NNNNNN</c> en la ruta). <b>Presentación pura</b>: lee el cuerpo con esquema estricto (un campo desconocido es validación), llama a <see cref="IReclamaciones"/> y a
/// <see cref="IHojasDelConsumidor"/> y traduce el resultado: una pantalla o un <c>application/problem+json</c> con los códigos <c>ECO-REC-…</c> y <c>ECO-…</c>, <b>la lista de campos que faltan sin el valor escrito</b>, el 403 de verificación requerida y el 429 con <c>Retry-After</c>. Los datos
/// de la hoja solo se pintan en la constancia de la respuesta del registro; ningún error ni registro los repite. Las rutas se permiten con los textos pendientes (WEB-41).
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(EntradasHttpDelCarrito.TopeDelCuerpo)]
public sealed partial class ReclamacionesController(IReclamaciones reclamaciones, IHojasDelConsumidor consumidor, ContextoDePagina pantalla, OpcionesDeErrores errores) : Controller
{
    public const string RutaDelFormulario = "/reclamaciones/nueva";

    private const int TamanoPorOmision = 20;

    private static readonly string[] CamposDelRegistro =
    [
        "nombre", "documento", "domicilio", "correo", "bienContratado", "montoReclamado", "tipo", "detalle", "pedidoReferencia", "claveDelFormulario", VerificacionDePersonaWeb.CampoDelToken,
    ];

    // ---------- Registro público ----------

    [HttpGet(RutaDelFormulario)]
    [PermitidaConTextosPendientes(CapacidadWeb.LibroDeReclamaciones)]
    [VerificacionDePersona(AccionProtegida.HojaDeReclamacion)]
    public async Task<IActionResult> Nueva(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        return await Formulario([], StatusCodes.Status200OK, cancelacion);
    }

    [HttpPost(RutaDelFormulario)]
    [PermitidaConTextosPendientes(CapacidadWeb.EnviarHoja)]
    [VerificacionDePersona(AccionProtegida.HojaDeReclamacion)]
    public async Task<IActionResult> Registrar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelRegistro, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        decimal? monto = null;
        if (campos.TryGetValue("montoReclamado", out var textoDelMonto))
        {
            if (!FormaDelMonto().IsMatch(textoDelMonto) || !decimal.TryParse(textoDelMonto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
            {
                return await FallaDelFormulario(new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("montoReclamado", "formato-invalido")]), cancelacion);
            }

            monto = valor;
        }

        // El token de la verificación de persona viaja en el formulario o, con htmx, en la cabecera (igual que el checkout).
        var token = campos.GetValueOrDefault(VerificacionDePersonaWeb.CampoDelToken) ?? Request.Headers["X-Verificacion-De-Persona"].FirstOrDefault();
        var resultado = await reclamaciones.Registrar(
            new RegistrarHojaEntrada(
                campos.GetValueOrDefault("nombre"),
                campos.GetValueOrDefault("documento"),
                campos.GetValueOrDefault("domicilio"),
                campos.GetValueOrDefault("correo"),
                campos.GetValueOrDefault("bienContratado"),
                monto,
                campos.GetValueOrDefault("tipo"),
                campos.GetValueOrDefault("detalle"),
                campos.GetValueOrDefault("pedidoReferencia"),
                campos.GetValueOrDefault("claveDelFormulario"),
                token),
            cancelacion);
        if (!resultado.EsExito)
        {
            return await FallaDelFormulario(resultado.Error!, cancelacion);
        }

        // La constancia lleva los datos que la persona acaba de escribir: nunca se cachea ni se guarda en el navegador.
        var vista = await pantalla.Completar(new ConstanciaVista { Constancia = resultado.Valor }, "Constancia de tu hoja de reclamación", cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("Constancia", vista);
    }

    // ---------- Mis hojas ----------

    [HttpGet("/reclamaciones/mis-hojas")]
    [PermitidaConTextosPendientes(CapacidadWeb.LibroDeReclamaciones)]
    public async Task<IActionResult> MisHojas([FromQuery] string? pagina, [FromQuery] string? tamano, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["pagina", "tamano"]) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        if (!TryEntero(pagina, 1, out var numeroDePagina))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pagina"), cancelacion);
        }

        if (!TryEntero(tamano, TamanoPorOmision, out var tamanoDePagina))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("tamano"), cancelacion);
        }

        var resultado = await consumidor.Listar(new ListarMisHojasEntrada(numeroDePagina, tamanoDePagina), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pantalla.Completar(new MisHojasVista { Pagina = resultado.Valor }, "Mis hojas de reclamación", cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("MisHojas", vista);
    }

    // El final es \z y no $ (que acepta un salto de línea al final) y los dígitos son ASCII.
    [HttpGet("/reclamaciones/{numero:regex(^[[0-9]]{{4}}-[[0-9]]{{6}}\\z)}")]
    [PermitidaConTextosPendientes(CapacidadWeb.LibroDeReclamaciones)]
    public async Task<IActionResult> MiHoja(string numero, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        var resultado = await consumidor.Obtener(numero, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pantalla.Completar(new MiHojaVista { Hoja = resultado.Valor }, $"Hoja {resultado.Valor.Numero}", cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("MiHoja", vista);
    }

    // ---------- Piezas ----------

    [GeneratedRegex(@"^\d{1,10}(\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelMonto();

    /// <summary>Un entero positivo de hasta nueve dígitos; ausente es el valor por omisión. Lo demás es validación (no se ignora en silencio); los topes los valida el módulo.</summary>
    private static bool TryEntero(string? texto, int porOmision, out int valor)
    {
        valor = porOmision;
        return texto is null || (texto.Length is > 0 and <= 9 && texto.All(char.IsAsciiDigit) && int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor));
    }

    /// <summary>El formulario con una clave de un solo uso nueva y los campos con problema, <b>sin lo que la persona escribió</b> (REC-04).</summary>
    private async Task<IActionResult> Formulario(IReadOnlyList<string> camposConProblema, int estado, CancellationToken cancelacion)
    {
        var vista = await pantalla.Completar(
            new ReclamacionNuevaVista { ClaveDelFormulario = Guid.NewGuid().ToString("N"), CamposConProblema = camposConProblema },
            "Libro de Reclamaciones",
            cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Nueva", vista);
    }

    /// <summary>Un fallo del registro: un navegador ve el formulario con la lista de campos que faltan (sin valores) y los demás errores como pantalla; una integración recibe el problema.</summary>
    private async Task<IActionResult> FallaDelFormulario(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (error.Categoria == CategoriaDeError.Validacion && PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await Formulario([.. error.Errores.Select(e => e.Campo).Distinct()], StatusCodes.Status400BadRequest, cancelacion);
        }

        return await Falla(error, cancelacion);
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await PantallasDelCarrito.Escribir(this, pantalla, errores, error, RutaDelFormulario, cancelacion);
        }

        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
