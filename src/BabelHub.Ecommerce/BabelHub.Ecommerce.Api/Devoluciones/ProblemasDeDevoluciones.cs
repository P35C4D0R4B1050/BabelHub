using System.Globalization;
using BabelHub.Ecommerce.Api.Envios;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Devoluciones;

/// <summary>
/// La traducción de los fallos de Devoluciones a los códigos <c>ECO-…</c> y <c>ECO-DEV-…</c> y a los estados HTTP de <c>plan.md</c> §4 (DEV-13, DEV-29, DEV-31; AC-DEV-05, AC-DEV-31): la Aplicación devuelve una categoría y una clave simbólica y esta tabla publica
/// solo lo que el plan declara. <b>Nunca lleva el motivo ni el texto de la persona, ni los repite</b>: los únicos campos que se exponen son el nombre del campo inválido y el plazo o la política (números y claves cerradas), nunca un valor escrito por la persona.
/// Cada respuesta es <c>no-store</c> y un límite excedido lleva <c>Retry-After</c>.
/// </summary>
public static class ProblemasDeDevoluciones
{
    private sealed record Publicada(string Sufijo, int? Estado = null, bool ExponeCampos = false);

    private static readonly Dictionary<string, Publicada> PorClave = new(StringComparer.Ordinal)
    {
        ["no-encontrado"] = new("NO-ENCONTRADO"),
        ["validacion-fallida"] = new("VALIDACION-INVALIDA", ExponeCampos: true),
        ["textos-pendientes"] = new("TEXTOS-PENDIENTES", StatusCodes.Status403Forbidden),
        ["cuenta-en-cancelacion"] = new("CUENTA-EN-CANCELACION", StatusCodes.Status409Conflict),
        ["tipos-mezclados"] = new("DEV-TIPOS-MEZCLADOS", StatusCodes.Status422UnprocessableEntity),
        ["linea-no-entregada"] = new("DEV-LINEA-NO-ENTREGADA", StatusCodes.Status422UnprocessableEntity),
        ["plazo-vencido"] = new("DEV-PLAZO-VENCIDO", StatusCodes.Status422UnprocessableEntity, ExponeCampos: true),
        ["cantidad-excedida"] = new("DEV-CANTIDAD-EXCEDIDA", StatusCodes.Status422UnprocessableEntity),
        ["linea-no-devolvible"] = new("DEV-LINEA-NO-DEVOLVIBLE", StatusCodes.Status422UnprocessableEntity),
        ["politica-digital"] = new("DEV-POLITICA-DIGITAL", StatusCodes.Status422UnprocessableEntity, ExponeCampos: true),
        ["reporte-no-admitido"] = new("DEV-REPORTE-NO-ADMITIDO", StatusCodes.Status422UnprocessableEntity),
        ["estado-invalido"] = new("DEV-ESTADO-INVALIDO", StatusCodes.Status409Conflict),
        ["reingreso-rechazado"] = new("DEV-REINGRESO-RECHAZADO", StatusCodes.Status422UnprocessableEntity),
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

    /// <summary>Escribe el fallo: el problema uniforme, nunca cacheable, con <c>Retry-After</c> en un límite. Un 401 o un 403 pasa por el registrador único de denegaciones (un evento por solicitud).</summary>
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
            await ProblemasHttp.Escribir(
                contexto,
                ProblemasDeEnvios.ComoLimite(problema, error),
                new Dictionary<string, string> { ["Retry-After"] = ProblemasDeEnvios.EsperaEnSegundos(error).ToString(CultureInfo.InvariantCulture) }).ConfigureAwait(false);
            return;
        }

        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
    }
}
