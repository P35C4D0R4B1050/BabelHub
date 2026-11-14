using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Lo que la sesión web decidió para la solicitud en curso, guardado en <see cref="HttpContext.Items"/> bajo una clave que solo este tipo conoce:
/// el actor verificado y la credencial en memoria. Nada de esto sale del proceso ni de la solicitud (D02, regla 9 de AGENTS.md).
/// </summary>
public static class SesionDeLaSolicitud
{
    private static readonly object ClaveDelResultado = new();

    /// <summary>Guarda el resultado de asegurar la credencial (lo hace <c>SesionWebHandler</c>, una vez por solicitud).</summary>
    public static void EstablecerCredencial(this HttpContext contexto, ResultadoDeCredencial resultado)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(resultado);
        contexto.Items[ClaveDelResultado] = resultado;
    }

    /// <summary>El resultado de asegurar la credencial en esta solicitud, o <see langword="null"/> si no se llegó a asegurar (sin cookie o fuera del handler).</summary>
    public static ResultadoDeCredencial? CredencialDeLaSesion(this HttpContext contexto)
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
/// la consume el manejador de salida de <c>Infrastructure.Plataforma</c>, que es quien lo coloca en <c>Authorization</c>.
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
            ? new ValorSecreto(credencial.Valor)
            : null);
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
        return servicios;
    }
}
