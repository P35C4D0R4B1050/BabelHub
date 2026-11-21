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

    /// <summary>El campo estable con el que el módulo avisa de que la hoja no se guardó con la sesión (REC-33) y puede registrarse sin sesión.</summary>
    public const string CampoRegistrarSinSesion = "rec-registrar-sin-sesion";

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
        ["reserva-expirada"] = new("PAGO-RESERVA-EXPIRADA", StatusCodes.Status409Conflict),
        ["intencion-cancelada"] = new("PAGO-INTENCION-CANCELADA", StatusCodes.Status409Conflict),
        ["instantanea-obsoleta"] = new("PAGO-INSTANTANEA-OBSOLETA", StatusCodes.Status409Conflict),
        ["total-cambio"] = new("PAGO-TOTAL-CAMBIO", StatusCodes.Status409Conflict),
        ["pago-en-curso"] = new("PAGO-EN-CURSO", StatusCodes.Status409Conflict),
        ["clave-invalida"] = new("PAGO-CLAVE-INVALIDA", StatusCodes.Status409Conflict),
        ["sin-cargo-que-reembolsar"] = new("REEMBOLSO-SIN-CARGO", StatusCodes.Status409Conflict),
        ["reembolso-distinto"] = new("REEMBOLSO-DISTINTO", StatusCodes.Status409Conflict),
        ["reembolso-supera-el-tope"] = new("REEMBOLSO-SUPERA-EL-TOPE", StatusCodes.Status422UnprocessableEntity),
        ["reembolso-no-reintentable"] = new("REEMBOLSO-NO-REINTENTABLE", StatusCodes.Status409Conflict),
        ["rec-estado-invalido"] = new("REC-ESTADO-INVALIDO", StatusCodes.Status409Conflict),
        ["rec-conflicto"] = new("CONFLICTO", StatusCodes.Status409Conflict),
        ["hoja-no-encontrada"] = new("NO-ENCONTRADO"),
    };

    public static ProblemaDto Crear(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(error);

        var generico = ProblemasHttp.Crear(contexto, opciones, error.Categoria, error.Clave, error.Errores);
        if (error.Categoria == CategoriaDeError.NoDisponible && error.Errores.Any(e => e.Campo == CampoRegistrarSinSesion))
        {
            // REC-33: el rechazo del registro con sesión que no pudo auditar la vinculación. Se publica solo este campo fijo (sin causa ni datos) para que la pantalla muestre la guía de registrar sin sesión.
            return generico with { Errores = [new ErrorDeCampoDto(CampoRegistrarSinSesion, "disponible")] };
        }

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
    public static DateTimeOffset? ReintentarEn(ErrorDeAplicacion error)
    {
        if (error.Errores.FirstOrDefault(e => e.Campo == "disponibleDesde") is { } campo
            && DateTimeOffset.TryParse(campo.Clave, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instante))
        {
            return instante;
        }

        // El límite de las hojas de reclamación informa la espera sugerida en segundos (REC-11).
        return error.Errores.FirstOrDefault(e => e.Campo == "espera-en-segundos") is { } espera
            && int.TryParse(espera.Clave, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos) && segundos is > 0 and <= 86_400
                ? DateTimeOffset.UtcNow.AddSeconds(segundos)
                : null;
    }

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
