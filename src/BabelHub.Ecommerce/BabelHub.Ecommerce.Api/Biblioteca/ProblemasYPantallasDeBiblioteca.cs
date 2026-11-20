using System.Globalization;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BabelHub.Ecommerce.Api.Biblioteca;

/// <summary>
/// La traducción de los fallos de Biblioteca a los códigos <c>ECO-…</c> y <c>ECO-BIB-…</c> y a los estados HTTP de <c>plan.md</c> §4 (BIB-03, BIB-06, BIB-07, BIB-13, BIB-15): la Aplicación devuelve una categoría y una clave simbólica y esta tabla publica solo lo que el plan declara. Un acceso ajeno o inexistente
/// responde <b>igual</b> (<c>ECO-NO-ENCONTRADO</c>, 404). Nunca lleva la URL firmada, la clave del archivo, trazas ni el texto del almacén. Cada respuesta es <c>no-store</c> y el límite excedido lleva <c>Retry-After</c>.
/// </summary>
public static class ProblemasDeBiblioteca
{
    private sealed record Publicada(string Sufijo, int? Estado = null, bool ExponeCampos = false);

    private static readonly Dictionary<string, Publicada> PorClave = new(StringComparer.Ordinal)
    {
        ["no-encontrado"] = new("NO-ENCONTRADO"),
        ["validacion-fallida"] = new("VALIDACION-INVALIDA", ExponeCampos: true),
        ["textos-pendientes"] = new("TEXTOS-PENDIENTES", StatusCodes.Status403Forbidden),
        ["acceso-no-disponible"] = new("BIB-ACCESO-NO-DISPONIBLE", StatusCodes.Status409Conflict),
        ["limite-de-descargas"] = new("BIB-LIMITE-DE-DESCARGAS", StatusCodes.Status409Conflict),
        ["archivo-no-disponible"] = new("BIB-ARCHIVO-NO-DISPONIBLE", StatusCodes.Status503ServiceUnavailable),
        ["conflicto"] = new("CONFLICTO", StatusCodes.Status409Conflict),
        ["servicio-no-disponible"] = new("SERVICIO-NO-DISPONIBLE", StatusCodes.Status503ServiceUnavailable),
    };

    public static ProblemaDto Crear(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(error);

        var generico = ProblemasHttp.Crear(contexto, opciones, error.Categoria, error.Clave, error.Errores);
        if (!PorClave.TryGetValue(error.Clave, out var publicada))
        {
            return generico;
        }

        var errores = publicada.ExponeCampos ? error.Errores.Select(e => new ErrorDeCampoDto(e.Campo, e.Clave)).ToList() : [];
        return generico with
        {
            Code = CodigosDeError.De(opciones.Servicio, publicada.Sufijo),
            Status = publicada.Estado ?? generico.Status,
            Errores = errores,
        };
    }

    /// <summary>La respuesta única de un límite excedido (SEG-07, BIB-15): el código, el motivo, la espera y el instante de nueva disponibilidad. Sin datos de cuentas.</summary>
    public static RespuestaDeLimiteDto ComoLimite(ProblemaDto problema, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(problema);
        ArgumentNullException.ThrowIfNull(error);
        var espera = EsperaEnSegundos(error);
        var motivo = error.Errores.FirstOrDefault(e => e.Campo == "motivo")?.Clave ?? string.Empty;
        var desde = error.Errores.FirstOrDefault(e => e.Campo == "disponibleDesde") is { } campo && DateTimeOffset.TryParse(campo.Clave, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instante)
            ? instante
            : DateTimeOffset.UtcNow.AddSeconds(espera);
        return new RespuestaDeLimiteDto
        {
            Type = problema.Type,
            Title = problema.Title,
            Status = problema.Status,
            Detail = problema.Detail,
            Code = problema.Code,
            TraceId = problema.TraceId,
            Motivo = motivo,
            RetryAfterSeconds = espera,
            DisponibleDesde = desde,
        };
    }

    public static int EsperaEnSegundos(ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Errores.FirstOrDefault(e => e.Campo == "espera-en-segundos") is { } campo && int.TryParse(campo.Clave, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos)
            ? Math.Clamp(segundos, 1, 3600)
            : 60;
    }

    /// <summary>Escribe el fallo como <c>application/problem+json</c>, nunca cacheable, con <c>Retry-After</c> en un límite. El acceso denegado ya lo registró la Aplicación; un 401 o un 403 pasa por el registrador único de denegaciones de la Api.</summary>
    public static async Task Escribir(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        var problema = Crear(contexto, opciones, error);
        if (error.Categoria == CategoriaDeError.Limite)
        {
            await ProblemasHttp.Escribir(contexto, ComoLimite(problema, error), new Dictionary<string, string> { ["Retry-After"] = EsperaEnSegundos(error).ToString(CultureInfo.InvariantCulture) }).ConfigureAwait(false);
            return;
        }

        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
    }
}

/// <summary>
/// Los fallos de Biblioteca en <b>pantalla</b> (T16, T17, T19; BIB-06, BIB-13, BIB-18): con un navegador (<c>Accept: text/html</c>) el error se pinta con el componente <c>bh-estado</c> y el texto de su código; sin él sigue saliendo el <c>application/problem+json</c>. Lo que la persona puede hacer a
/// continuación (pedir ayuda, reportar el archivo, pedir un enlace nuevo) está en su biblioteca, a la que apunta el reintento. <b>Nunca muestra la URL firmada, la clave del archivo ni el texto del almacén.</b>
/// </summary>
public static class PantallasDeBiblioteca
{
    public const string RutaDeMiBiblioteca = "/biblioteca";

    private sealed record Texto(string Titulo, string Mensaje);

    private static readonly Dictionary<string, Texto> PorCodigo = new(StringComparer.Ordinal)
    {
        ["ECO-BIB-ACCESO-NO-DISPONIBLE"] = new("Este libro no se puede descargar ahora", "Tu acceso está suspendido mientras revisamos un reporte, o se revocó por un reembolso o una anulación. Si crees que es un error, pide ayuda desde tu biblioteca."),
        ["ECO-BIB-LIMITE-DE-DESCARGAS"] = new("Alcanzaste el límite de descargas", "Cada libro se puede descargar hasta 5 veces. Si perdiste el archivo, pide ayuda desde tu biblioteca y la administración revisará tu caso."),
        ["ECO-BIB-ARCHIVO-NO-DISPONIBLE"] = new("No pudimos preparar tu archivo", "El archivo de este libro no está disponible ahora. No se consumió ninguna descarga. Reporta el problema desde tu biblioteca y lo revisaremos."),
        ["ECO-NO-ENCONTRADO"] = new("No encontramos ese libro", "Revisa tu biblioteca: puede que el enlace ya no sea válido."),
        ["ECO-CONFLICTO"] = new("Ya estamos preparando esa descarga", "Espera unos segundos y vuelve a intentarlo desde tu biblioteca."),
    };

    /// <summary>Un navegador pide una página: <c>Accept</c> incluye <c>text/html</c>.</summary>
    public static bool PrefiereHtml(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.GetTypedHeaders().Accept.Any(a => a.MediaType.HasValue && string.Equals(a.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    public static RespuestaDePantalla Traducir(ProblemaDto problema, ErrorDeAplicacion error, string? rutaDeReintento, string parteAfectada = "No podemos mostrar tu biblioteca ahora. Inténtalo de nuevo en unos minutos.")
    {
        ArgumentNullException.ThrowIfNull(problema);
        ArgumentNullException.ThrowIfNull(error);
        if (PorCodigo.TryGetValue(problema.Code, out var texto))
        {
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = texto.Titulo,
                    Mensaje = texto.Mensaje,
                    RutaDeReintento = rutaDeReintento,
                    CodigoDeSoporte = problema.Code,
                    IdentificadorDeSeguimiento = problema.TraceId,
                },
                problema.Status,
                null,
                true,
                []);
        }

        var respuesta = TraductorDeErrores.Traducir(problema, rutaDeReintento: rutaDeReintento, parteAfectada: parteAfectada);
        return error.Categoria == CategoriaDeError.Limite
            ? respuesta with { Vista = respuesta.Vista with { EsperaSugeridaSegundos = ProblemasDeBiblioteca.EsperaEnSegundos(error) } }
            : respuesta;
    }

    /// <summary>Escribe el fallo como pantalla completa (el estado dentro del layout) con el código HTTP del módulo (<c>200</c> con htmx), <c>Retry-After</c> en un límite y <c>no-store</c>.</summary>
    public static async Task<IActionResult> Escribir(Controller controlador, ContextoDePagina pagina, OpcionesDeErrores errores, ErrorDeAplicacion error, string? rutaDeReintento, AreaDeLaPagina area, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(controlador);
        var contexto = controlador.HttpContext;
        var problema = ProblemasDeBiblioteca.Crear(contexto, errores, error);
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        var respuesta = Traducir(problema, error, rutaDeReintento);
        if (respuesta.RedirigirA is { } destino)
        {
            return controlador.Request.EsHtmx() ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = respuesta.Vista }, respuesta.Vista.Titulo, area, cancelacion).ConfigureAwait(false);
        if (error.Categoria == CategoriaDeError.Limite)
        {
            controlador.Response.Headers["Retry-After"] = ProblemasDeBiblioteca.EsperaEnSegundos(error).ToString(CultureInfo.InvariantCulture);
        }

        controlador.Response.StatusCode = controlador.Request.EsHtmx() ? StatusCodes.Status200OK : respuesta.EstadoHttp;
        controlador.Response.Headers.CacheControl = "no-store";
        return controlador.View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}
