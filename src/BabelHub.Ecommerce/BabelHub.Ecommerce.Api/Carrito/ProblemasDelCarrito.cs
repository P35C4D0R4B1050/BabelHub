using System.Globalization;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Carrito;

/// <summary>
/// La traducción de los fallos de Carrito y del checkout a los códigos <c>ECO-…</c> y los estados HTTP de <c>plan.md</c> §4 (CAR-16, CAR-35, AC-CAR-13): la Aplicación devuelve una categoría y una clave simbólica y esta tabla publica el código estable. Se
/// expone el motivo solo donde el plan lo publica (la validación, el máximo de unidades, el plazo nuevo de la reserva rechazada); el límite excedido lleva <c>Retry-After</c> con el instante de reintento. Nunca lleva trazas, el contacto ni la cookie (SEG-03).
/// Cada respuesta es <c>no-store</c>.
/// </summary>
public static class ProblemasDelCarrito
{
    /// <summary>Lo que la Api publica de una clave: el sufijo del código (<c>ECO-…</c>), un estado que reemplaza al de la categoría y si se exponen los campos del error.</summary>
    private sealed record Publicada(string Sufijo, int? Estado = null, bool ExponeCampos = false);

    private static readonly Dictionary<string, Publicada> PorClave = new(StringComparer.Ordinal)
    {
        ["no-encontrado"] = new("NO-ENCONTRADO"),
        ["checkout-no-encontrado"] = new("NO-ENCONTRADO"),
        ["linea-no-encontrada"] = new("NO-ENCONTRADO"),
        ["pedido-obsoleto"] = new("CHECKOUT-PEDIDO-OBSOLETO", StatusCodes.Status409Conflict),
        ["validacion-fallida"] = new("VALIDACION-INVALIDA", ExponeCampos: true),
        ["carrito-sin-stock"] = new("CARRITO-SIN-STOCK", StatusCodes.Status409Conflict, ExponeCampos: true),
        ["carrito-limite-de-lineas"] = new("CARRITO-LIMITE-DE-LINEAS", StatusCodes.Status422UnprocessableEntity),
        ["carrito-limite-de-unidades"] = new("CARRITO-LIMITE-DE-UNIDADES", StatusCodes.Status422UnprocessableEntity),
        ["digital-ya-poseida"] = new("DIGITAL-YA-POSEIDA", StatusCodes.Status409Conflict, ExponeCampos: true),
        ["edicion-no-disponible"] = new("EDICION-NO-DISPONIBLE", StatusCodes.Status409Conflict),
        ["checkout-pago-pendiente"] = new("CHECKOUT-PAGO-PENDIENTE", StatusCodes.Status409Conflict),
        ["checkout-en-curso"] = new("CHECKOUT-EN-CURSO", StatusCodes.Status409Conflict),
        ["checkout-stock-no-validable"] = new("CHECKOUT-STOCK-NO-VALIDABLE", StatusCodes.Status503ServiceUnavailable),
        ["checkout-vacio"] = new("CHECKOUT-VACIO", StatusCodes.Status422UnprocessableEntity),
        ["entrega-no-validable"] = new("CHECKOUT-STOCK-NO-VALIDABLE", StatusCodes.Status503ServiceUnavailable),
        ["checkout-aceptacion-requerida"] = new("CHECKOUT-ACEPTACION-REQUERIDA", StatusCodes.Status422UnprocessableEntity, ExponeCampos: true),
        ["checkout-datos-de-entrega-incompletos"] = new("CHECKOUT-DATOS-DE-ENTREGA-INCOMPLETOS", StatusCodes.Status422UnprocessableEntity, ExponeCampos: true),
        ["checkout-texto-cambio"] = new("CHECKOUT-TEXTO-CAMBIO", StatusCodes.Status409Conflict, ExponeCampos: true),
        ["checkout-total-cambio"] = new("CHECKOUT-TOTAL-CAMBIO", StatusCodes.Status409Conflict, ExponeCampos: true),
        ["checkout-licencia-no-vigente"] = new("CHECKOUT-LICENCIA-NO-VIGENTE", StatusCodes.Status409Conflict, ExponeCampos: true),
        ["checkout-cuenta-no-puede-comprar"] = new("CHECKOUT-CUENTA-NO-PUEDE-COMPRAR", StatusCodes.Status403Forbidden, ExponeCampos: true),
        ["cuenta-no-puede-comprar"] = new("CHECKOUT-CUENTA-NO-PUEDE-COMPRAR", StatusCodes.Status403Forbidden, ExponeCampos: true),
        ["checkout-correo-sin-verificar"] = new("CHECKOUT-CORREO-SIN-VERIFICAR", StatusCodes.Status403Forbidden),
        ["checkout-textos-pendientes"] = new("CHECKOUT-TEXTOS-PENDIENTES", StatusCodes.Status403Forbidden),
        ["checkout-reserva-rechazada"] = new("CHECKOUT-RESERVA-RECHAZADA", StatusCodes.Status409Conflict, ExponeCampos: true),
        ["servicio-no-disponible"] = new("SERVICIO-NO-DISPONIBLE", StatusCodes.Status503ServiceUnavailable),
        ["verificacion-requerida"] = new("VERIFICACION-REQUERIDA", StatusCodes.Status403Forbidden),
        ["verificacion-fallida"] = new("VERIFICACION-FALLIDA", StatusCodes.Status403Forbidden),
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

        var errores = publicada.ExponeCampos || error.Categoria.ExponeElMotivo()
            ? error.Errores.Select(e => new ErrorDeCampoDto(e.Campo, e.Clave)).ToList()
            : [];
        return generico with
        {
            Code = CodigosDeError.De(opciones.Servicio, publicada.Sufijo),
            Status = publicada.Estado ?? generico.Status,
            Errores = errores,
        };
    }

    /// <summary>
    /// El instante de reintento de un límite (el campo <c>disponibleDesde</c> que traduce la Aplicación desde el 429 del Inventario o del límite propio), si lo hay y es válido.
    /// </summary>
    public static DateTimeOffset? ReintentarEn(ErrorDeAplicacion error) =>
        error.Errores.FirstOrDefault(e => e.Campo == "disponibleDesde") is { } campo
        && DateTimeOffset.TryParse(campo.Clave, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instante)
            ? instante
            : null;

    /// <summary>
    /// Escribe el fallo: el problema uniforme, nunca cacheable (<c>no-store</c>), con <c>Retry-After</c> en un límite. El acceso denegado ya lo registró la Aplicación (un evento por solicitud) y el de límite excedido también.
    /// </summary>
    public static async Task Escribir(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        var problema = Crear(contexto, opciones, error);
        if (error.Categoria == CategoriaDeError.Limite)
        {
            var reintentarEn = ReintentarEn(error);
            var espera = reintentarEn is { } hasta ? Math.Max(1, (int)Math.Ceiling((hasta - DateTimeOffset.UtcNow).TotalSeconds)) : 60;
            await ProblemasHttp.Escribir(contexto, problema, new Dictionary<string, string> { ["Retry-After"] = espera.ToString(CultureInfo.InvariantCulture) }).ConfigureAwait(false);
            return;
        }

        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
    }
}
