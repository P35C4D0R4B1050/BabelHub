using System.Globalization;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Envios;

/// <summary>
/// La traducción de los fallos de Envíos a los códigos <c>ECO-…</c> y <c>ECO-ENV-…</c> y a los estados HTTP de <c>plan.md</c> §4 (ENV-08, AC-ENV-04, AC-ENV-08, AC-ENV-09): la Aplicación devuelve una categoría y una clave
/// simbólica y esta tabla publica solo lo que el plan declara. El motivo (<c>errores</c>) se expone únicamente en el contacto incompleto, y allí lleva <b>el nombre del campo o «anonimizado», nunca su valor</b> (ENV-17);
/// el límite excedido lleva <c>Retry-After</c> con la espera en segundos. Nunca lleva trazas, ni el texto del Inventario ni el del operador. Cada respuesta es <c>no-store</c>.
/// </summary>
public static class ProblemasDeEnvios
{
    private sealed record Publicada(string Sufijo, int? Estado = null, bool ExponeCampos = false);

    private static readonly Dictionary<string, Publicada> PorClave = new(StringComparer.Ordinal)
    {
        ["envio-no-encontrado"] = new("NO-ENCONTRADO"),
        ["pedido-no-encontrado"] = new("NO-ENCONTRADO"),
        ["validacion-fallida"] = new("VALIDACION-INVALIDA", ExponeCampos: true),
        ["estado-invalido"] = new("ENV-ESTADO-INVALIDO", StatusCodes.Status409Conflict),
        ["guia-en-curso"] = new("ENV-GUIA-EN-CURSO", StatusCodes.Status409Conflict),
        ["contacto-incompleto"] = new("ENV-CONTACTO-INCOMPLETO", StatusCodes.Status422UnprocessableEntity, ExponeCampos: true),
        ["operador-no-disponible"] = new("ENV-OPERADOR-NO-DISPONIBLE", StatusCodes.Status503ServiceUnavailable),
        ["inventario-rechazo"] = new("ENV-INVENTARIO-RECHAZO", StatusCodes.Status409Conflict),
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

        var errores = publicada.ExponeCampos
            ? error.Errores.Select(e => new ErrorDeCampoDto(e.Campo, e.Clave)).ToList()
            : [];
        return generico with
        {
            Code = CodigosDeError.De(opciones.Servicio, publicada.Sufijo),
            Status = publicada.Estado ?? generico.Status,
            Errores = errores,
        };
    }

    /// <summary>La respuesta única de un límite excedido (SEG-07): el código, el motivo (la política), la espera y el instante de nueva disponibilidad. Sin datos de cuentas.</summary>
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

    /// <summary>La espera sugerida de un límite excedido, en segundos (el campo <c>espera-en-segundos</c> que fija la Aplicación), acotada; 60 si falta o no es válida.</summary>
    public static int EsperaEnSegundos(ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Errores.FirstOrDefault(e => e.Campo == "espera-en-segundos") is { } campo
            && int.TryParse(campo.Clave, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos)
                ? Math.Clamp(segundos, 1, 3600)
                : 60;
    }

    /// <summary>
    /// Escribe el fallo: el problema uniforme, nunca cacheable, con <c>Retry-After</c> en un límite. El acceso denegado ya lo registró la Aplicación (un evento por solicitud); el de un límite excedido también.
    /// Un 401 o un 403 pasa por el registrador único de denegaciones de la Api (un evento por solicitud).
    /// </summary>
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
