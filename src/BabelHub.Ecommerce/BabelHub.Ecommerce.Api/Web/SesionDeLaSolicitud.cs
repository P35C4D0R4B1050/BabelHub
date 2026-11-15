using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>Cómo quedó la sesión web en esta solicitud (lo fija <see cref="SesionWebHandler"/>); lo leen los filtros de área y de paso pendiente y las respuestas de cierre.</summary>
public enum EstadoDeLaSesionEnLaSolicitud
{
    /// <summary>La solicitud no trae cookie de sesión: es una visita anónima.</summary>
    SinCookie,

    /// <summary>La sesión es válida y su credencial quedó asegurada: hay un actor verificado.</summary>
    Asegurada,

    /// <summary>La cookie no corresponde a ninguna sesión o su forma no es válida.</summary>
    Inexistente,

    /// <summary>La sesión ya estaba cerrada (un identificador cerrado nunca vuelve a usarse).</summary>
    Cerrada,

    /// <summary>La sesión venció por inactividad o por el tope absoluto en esta solicitud.</summary>
    Vencida,

    /// <summary>La cookie es de una sesión del otro host.</summary>
    OtroHost,

    /// <summary>Identidad rechazó la renovación (o la credencial no se pudo verificar): la sesión se cerró.</summary>
    RenovacionRechazada,

    /// <summary>Identidad o el verificador no responden: no se cerró nada y no hay actor verificado.</summary>
    ServicioNoDisponible,

    /// <summary>Por la ruta o el modo de arranque (<c>Ensayo</c>, <c>Restauracion</c>) no se cargó ninguna sesión.</summary>
    NoSeCargo,
}

/// <summary>
/// Lo que la sesión web decidió para la solicitud en curso, guardado en <see cref="HttpContext.Items"/> bajo una clave que solo este tipo conoce:
/// el actor verificado y la credencial en memoria. Nada de esto sale del proceso ni de la solicitud (D02, regla 9 de AGENTS.md).
/// </summary>
public static class SesionDeLaSolicitud
{
    private static readonly object ClaveDelResultado = new();
    private static readonly object ClaveDelEstado = new();

    public static void EstablecerEstadoDeLaSesion(this HttpContext contexto, EstadoDeLaSesionEnLaSolicitud estado)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Items[ClaveDelEstado] = estado;
    }

    /// <summary>Cómo quedó la sesión en esta solicitud; <see cref="EstadoDeLaSesionEnLaSolicitud.NoSeCargo"/> si el handler no corrió.</summary>
    public static EstadoDeLaSesionEnLaSolicitud EstadoDeLaSesion(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return contexto.Items.TryGetValue(ClaveDelEstado, out var valor) && valor is EstadoDeLaSesionEnLaSolicitud estado ? estado : EstadoDeLaSesionEnLaSolicitud.NoSeCargo;
    }

    /// <summary>Guarda el resultado de asegurar la credencial (lo hace <c>SesionWebHandler</c>, una vez por solicitud).</summary>
    internal static void EstablecerCredencial(this HttpContext contexto, ResultadoDeCredencial resultado)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(resultado);
        contexto.Items[ClaveDelResultado] = resultado;
    }

    /// <summary>El resultado de asegurar la credencial en esta solicitud, o <see langword="null"/> si no se llegó a asegurar (sin cookie o fuera del handler).</summary>
    internal static ResultadoDeCredencial? CredencialDeLaSesion(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return contexto.Items.TryGetValue(ClaveDelResultado, out var valor) ? valor as ResultadoDeCredencial : null;
    }

    /// <summary>El actor verificado de esta solicitud: solo existe si la credencial quedó asegurada.</summary>
    public static ActorVerificado? ActorDeLaSolicitud(this HttpContext contexto) =>
        contexto.CredencialDeLaSesion() is { Estado: EstadoDeCredencial.Asegurada, Actor: { } actor } ? actor : null;
}

/// <summary>
/// Implementa <see cref="ITokenDelUsuarioDeLaSesion"/> desde la sesión de la solicitud en curso (T14, D13): entrega el token solo si la credencial quedó
/// <b>asegurada</b> (verificada y con más de 60 segundos de vida, ya renovada si hacía falta) y nunca lo pone en un campo, cabecera ni registro. Solo
/// la consume <c>TokenDelUsuarioDeSalidaHandler</c> (<c>Infrastructure.Plataforma</c>), que lo deja en las opciones de la solicitud para que <c>CredencialesDeSalidaHandler</c> lo coloque en <c>Authorization</c>.
/// </summary>
/// <remarks>
/// Es un singleton que lee el contexto HTTP del flujo asíncrono (<see cref="IHttpContextAccessor"/>): los manejadores de <c>IHttpClientFactory</c> viven
/// en su propio ámbito y no verían uno por solicitud. Fuera de una solicitud (un trabajo de fondo) no hay sesión y devuelve <see langword="null"/>.
/// </remarks>
public sealed class HttpTokenDelUsuarioDeLaSesion(IHttpContextAccessor contexto) : ITokenDelUsuarioDeLaSesion
{
    public Task<ValorSecreto?> Actual(CancellationToken cancelacion = default)
    {
        var resultado = contexto.HttpContext?.CredencialDeLaSesion();
        return Task.FromResult(resultado is { Estado: EstadoDeCredencial.Asegurada, Credencial: { } credencial }
            ? new ValorSecreto(credencial.Revelar())
            : null);
    }
}

public static class LimpiezaDelNavegador
{
    /// <summary>
    /// Pide al navegador vaciar su caché y su almacenamiento (<c>Clear-Site-Data</c>) y no guardar esta respuesta: tras cerrar la sesión, «atrás» no debe mostrar páginas de la
    /// persona anterior y <c>localStorage</c> debe quedar vacío (WEB-28, WEB-40). La cookie de sesión se borra aparte, con sus atributos. Se usa al cerrar la sesión, en «cerrar
    /// todas» y en toda redirección al ingreso por vencimiento o rechazo.
    /// </summary>
    public static void LimpiarElNavegador(this HttpResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        respuesta.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";
        respuesta.Headers.CacheControl = "no-store";
    }
}

public static class TokenDelUsuarioDeLaSesionExtensiones
{
    /// <summary>Registra la implementación del token de la sesión; el manejador de salida la usa para las operaciones de usuario.</summary>
    public static IServiceCollection AgregarTokenDelUsuarioDeLaSesion(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddHttpContextAccessor();
        servicios.AddSingleton<ITokenDelUsuarioDeLaSesion, HttpTokenDelUsuarioDeLaSesion>();
        // Todo cliente HTTP hacia otro servicio pone el token de la sesión en sus operaciones de usuario (el manejador va primero en la cadena).
        servicios.TryAddTransient<TokenDelUsuarioDeSalidaHandler>();
        servicios.ConfigureHttpClientDefaults(cliente => cliente.AddHttpMessageHandler<TokenDelUsuarioDeSalidaHandler>());
        return servicios;
    }
}
