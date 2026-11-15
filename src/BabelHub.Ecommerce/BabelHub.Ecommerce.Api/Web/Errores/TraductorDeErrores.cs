using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Web.Errores;

/// <summary>Dónde se muestra el error: el ingreso responde lo mismo sin importar qué falló (WEB-24).</summary>
public enum ContextoDeTraduccion
{
    General,
    Ingreso,
}

/// <summary>
/// Lo que la interfaz hace con un error: qué pantalla de estado muestra (<see cref="Vista"/>), con qué código HTTP, si lleva al ingreso
/// (<see cref="RedirigirA"/>) y si lo que la persona tenía escrito o en su carrito se conserva (siempre, salvo que no hubiera nada que conservar).
/// </summary>
public sealed record RespuestaDePantalla(
    EstadoVista Vista,
    int EstadoHttp,
    string? RedirigirA,
    bool ConservaLoEscrito,
    IReadOnlyList<ErrorDeCampoDto> Campos);

/// <summary>
/// Traduce el <see cref="ProblemaDto"/> de un módulo del E-commerce o del Inventario (con su <c>code</c> estable) a un estado de pantalla
/// (WEB-07, WEB-24, WEB-26, T22). Es presentación pura: elige el patrón visual y el texto de <c>docs/06/08</c>; la regla de negocio la
/// decidió el módulo. Nunca muestra trazas, excepciones ni datos internos: el código y el seguimiento solo van en el soporte plegable.
/// </summary>
public static class TraductorDeErrores
{
    public const string RutaDelIngreso = "/ingreso";
    public const string RutaDeAceptacion = "/textos/aceptar";

    /// <summary>El código estable de una acción bloqueada por los textos legales pendientes (403). <c>Shared</c> aún no lo declara: lo agrega el cambio de contrato de R12.</summary>
    public const string CodigoDeTextosPendientes = "ECO-TEXTOS-PENDIENTES";
    public const string MensajeDeCredencialInvalida = "El correo o la contraseña no coinciden.";
    private const string TituloDelIngreso = "No pudimos iniciar tu sesión";

    private enum Clase
    {
        Validacion,
        NoAutenticado,
        Prohibido,
        NoEncontrado,
        Conflicto,
        Regla,
        Limite,
        NoDisponible,
        Inesperado,
    }

    public static RespuestaDePantalla Traducir(
        ProblemaDto problema,
        ContextoDeTraduccion contexto = ContextoDeTraduccion.General,
        string? rutaDeReintento = null,
        string? parteAfectada = null,
        IReadOnlyList<EnlaceDeSalida>? salidas = null)
    {
        ArgumentNullException.ThrowIfNull(problema);
        // Textos legales pendientes (T38): no se muestra el problema en crudo; se invita a aceptar, sin conservar ni reenviar lo escrito en la acción bloqueada (WEB-48).
        if (string.Equals(problema.Code, CodigoDeTextosPendientes, StringComparison.Ordinal))
        {
            return new RespuestaDePantalla(EstadoVista.ErrorGenerico(null), StatusCodes.Status403Forbidden, RutaDeAceptacion, false, []);
        }

        var clase = Clasificar(problema);

        if (contexto == ContextoDeTraduccion.Ingreso)
        {
            return DelIngreso(problema, clase, rutaDeReintento, parteAfectada);
        }

        var soporte = new EstadoVista { CodigoDeSoporte = problema.Code, IdentificadorDeSeguimiento = problema.TraceId };
        return clase switch
        {
            Clase.NoAutenticado => new RespuestaDePantalla(
                EstadoVista.ErrorGenerico(null), StatusCodes.Status401Unauthorized, RutaDelIngreso, true, []),

            Clase.Prohibido => new RespuestaDePantalla(
                EstadoVista.SinPermiso(salidas is { Count: > 0 } ? salidas : [new EnlaceDeSalida("Ir al inicio", "/")]),
                StatusCodes.Status403Forbidden,
                null,
                true,
                []),

            Clase.NoDisponible => new RespuestaDePantalla(
                EstadoVista.ServicioNoDisponible(parteAfectada ?? TextosDeInterfaz.NoDisponibleMensajeGeneral, rutaDeReintento) with
                {
                    CodigoDeSoporte = soporte.CodigoDeSoporte,
                    IdentificadorDeSeguimiento = soporte.IdentificadorDeSeguimiento,
                },
                StatusCodes.Status503ServiceUnavailable,
                null,
                true,
                []),

            Clase.Limite => new RespuestaDePantalla(
                ErrorDeLimite(problema, TextoDelLimite(problema), rutaDeReintento, soporte),
                StatusCodes.Status429TooManyRequests,
                null,
                true,
                []),

            Clase.Validacion => new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = "Revisa los datos",
                    Mensaje = "Corrige los campos marcados e inténtalo de nuevo.",
                    RutaDeReintento = rutaDeReintento,
                    CodigoDeSoporte = soporte.CodigoDeSoporte,
                    IdentificadorDeSeguimiento = soporte.IdentificadorDeSeguimiento,
                },
                StatusCodes.Status400BadRequest,
                null,
                true,
                problema.Errores),

            Clase.Regla => new RespuestaDePantalla(
                EstadoVista.ErrorGenerico(null) with
                {
                    // El módulo redactó este detalle para la persona (solo la validación, la regla de negocio y el límite exponen el motivo, SEG-16).
                    Mensaje = string.IsNullOrWhiteSpace(problema.Detail) ? TextosDeInterfaz.ErrorMensaje : problema.Detail,
                    CodigoDeSoporte = soporte.CodigoDeSoporte,
                    IdentificadorDeSeguimiento = soporte.IdentificadorDeSeguimiento,
                },
                StatusCodes.Status422UnprocessableEntity,
                null,
                true,
                []),

            Clase.NoEncontrado => new RespuestaDePantalla(
                EstadoVista.Vacio("No encontramos lo que buscas", "Revisa el enlace o vuelve al inicio.", "Ir al inicio", "/"),
                StatusCodes.Status404NotFound,
                null,
                true,
                []),

            _ => new RespuestaDePantalla(
                EstadoVista.ErrorGenerico(rutaDeReintento) with
                {
                    CodigoDeSoporte = soporte.CodigoDeSoporte,
                    IdentificadorDeSeguimiento = soporte.IdentificadorDeSeguimiento,
                },
                StatusCodes.Status500InternalServerError,
                null,
                true,
                []),
        };
    }

    /// <summary>
    /// Una excepción al llamar al Inventario o a un módulo: la falta de comunicación y el tiempo agotado son «servicio no disponible» (WEB-26,
    /// sin tocar el carrito); cualquier otra es un error genérico. El mensaje de la excepción nunca se muestra.
    /// </summary>
    public static RespuestaDePantalla Traducir(Exception excepcion, string? rutaDeReintento = null, string? parteAfectada = null)
    {
        ArgumentNullException.ThrowIfNull(excepcion);
        if (excepcion is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            return new RespuestaDePantalla(
                EstadoVista.ServicioNoDisponible(parteAfectada ?? TextosDeInterfaz.NoDisponibleMensajeGeneral, rutaDeReintento),
                StatusCodes.Status503ServiceUnavailable,
                null,
                true,
                []);
        }

        return new RespuestaDePantalla(EstadoVista.ErrorGenerico(rutaDeReintento), StatusCodes.Status500InternalServerError, null, true, []);
    }

    private static RespuestaDePantalla DelIngreso(ProblemaDto problema, Clase clase, string? rutaDeReintento, string? parteAfectada)
    {
        // Sin soporte, sin campos y sin distinguir la causa: mismo estado, mismo HTML, mismas cookies y misma redirección (WEB-24).
        if (clase == Clase.NoDisponible)
        {
            return new RespuestaDePantalla(
                EstadoVista.ServicioNoDisponible(parteAfectada ?? TextosDeInterfaz.NoDisponibleMensajeGeneral, rutaDeReintento),
                StatusCodes.Status503ServiceUnavailable,
                null,
                true,
                []);
        }

        if (clase == Clase.Limite)
        {
            return new RespuestaDePantalla(
                ErrorDeLimite(problema, TextoDelLimiteDelIngreso(problema), rutaDeReintento, new EstadoVista()) with { CodigoDeSoporte = null, IdentificadorDeSeguimiento = null },
                StatusCodes.Status429TooManyRequests,
                null,
                true,
                []);
        }

        return new RespuestaDePantalla(
            new EstadoVista
            {
                Estado = EstadoDePantalla.Error,
                Titulo = TituloDelIngreso,
                Mensaje = MensajeDeCredencialInvalida,
                RutaDeReintento = rutaDeReintento,
            },
            StatusCodes.Status401Unauthorized,
            null,
            true,
            []);
    }

    private static EstadoVista ErrorDeLimite(ProblemaDto problema, string mensaje, string? rutaDeReintento, EstadoVista soporte) =>
        new()
        {
            Estado = EstadoDePantalla.Error,
            Titulo = "Espera un momento",
            Mensaje = mensaje,
            RutaDeReintento = rutaDeReintento,
            EsperaSugeridaSegundos = problema is RespuestaDeLimiteDto { RetryAfterSeconds: > 0 } l ? l.RetryAfterSeconds : null,
            DisponibleDesde = problema is RespuestaDeLimiteDto { RetryAfterSeconds: > 0 } d ? FormatoPeruano.Hora(d.DisponibleDesde) : null,
            CodigoDeSoporte = soporte.CodigoDeSoporte,
            IdentificadorDeSeguimiento = soporte.IdentificadorDeSeguimiento,
        };

    private static string TextoDelLimite(ProblemaDto problema)
    {
        if (problema is not RespuestaDeLimiteDto { RetryAfterSeconds: > 0 } limite)
        {
            return "Estás yendo muy rápido. Vuelve a intentarlo en unos instantes.";
        }

        var texto = $"Estás yendo muy rápido. Vuelve a intentarlo en {Espera(limite.RetryAfterSeconds)}.";
        return limite.RetryAfterSeconds >= 120 ? $"{texto} Podrás hacerlo a las {FormatoPeruano.Hora(limite.DisponibleDesde)}." : texto;
    }

    private static string TextoDelLimiteDelIngreso(ProblemaDto problema)
    {
        if (problema is not RespuestaDeLimiteDto { RetryAfterSeconds: > 0 } limite)
        {
            return "Hiciste varios intentos. Espera unos instantes o recupera tu acceso.";
        }

        var texto = $"Hiciste varios intentos. Espera {Espera(limite.RetryAfterSeconds)} o recupera tu acceso.";
        return limite.RetryAfterSeconds >= 120 ? $"{texto} Podrás volver a probar a las {FormatoPeruano.Hora(limite.DisponibleDesde)}." : texto;
    }

    /// <summary>La espera en segundos (hasta un minuto) o en minutos redondeados hacia arriba.</summary>
    private static string Espera(int segundos)
    {
        if (segundos <= 60)
        {
            return segundos == 1 ? "1 segundo" : $"{segundos} segundos";
        }

        var minutos = (segundos + 59) / 60;
        return $"{minutos} minutos";
    }

    private static Clase Clasificar(ProblemaDto problema)
    {
        var codigo = problema.Code ?? string.Empty;
        var corte = codigo.IndexOf('-', StringComparison.Ordinal);
        var sufijo = corte >= 0 ? codigo[(corte + 1)..] : codigo;
        return sufijo switch
        {
            CodigosDeError.ValidacionInvalida => Clase.Validacion,
            CodigosDeError.NoAutenticado => Clase.NoAutenticado,
            CodigosDeError.Prohibido => Clase.Prohibido,
            CodigosDeError.NoEncontrado => Clase.NoEncontrado,
            CodigosDeError.Conflicto => Clase.Conflicto,
            CodigosDeError.ReglaDeNegocioIncumplida => Clase.Regla,
            CodigosDeError.LimiteExcedido => Clase.Limite,
            CodigosDeError.ServicioNoDisponible => Clase.NoDisponible,
            CodigosDeError.ErrorInesperado => Clase.Inesperado,
            _ => problema.Status switch
            {
                StatusCodes.Status422UnprocessableEntity => Clase.Regla,
                StatusCodes.Status429TooManyRequests => Clase.Limite,
                StatusCodes.Status503ServiceUnavailable => Clase.NoDisponible,
                _ => Clase.Inesperado,
            },
        };
    }
}
