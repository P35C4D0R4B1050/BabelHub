using System.Collections.Concurrent;
using System.Net;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>Declara la operación de servicio que implementa un endpoint de la superficie entre servicios; la matriz dice qué alcance de credencial la autoriza.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class OperacionDeServicioAttribute(string operacion) : Attribute
{
    public OperacionDeServicio Operacion { get; } = new(operacion);
}

/// <summary>La superficie de servicio es lo que cuelga de <see cref="Prefijo"/>: todo endpoint ahí exige credencial y declara su operación.</summary>
public sealed class OpcionesDeSuperficieDeServicio
{
    public string Prefijo { get; init; } = "/servicio";

    /// <summary>Entre servicios solo se acepta HTTPS (la credencial viaja en una cabecera). Se desactiva solo en pruebas locales.</summary>
    public bool ExigirHttps { get; init; } = true;

    /// <summary>
    /// Tope general de la dirección directa cuando la dirección reenviada se ignora por faltar la credencial de origen (SEG-32):
    /// 120 por minuto por defecto. Sin política configurada no se aplica.
    /// </summary>
    public ClaveDePolitica? TopeDeLaDireccionDirecta { get; init; }
}

/// <summary>Recuerda cuándo se avisó del uso de una credencial anterior, para no alertar en cada llamada de una rotación.</summary>
public sealed class AvisosDeCredencialAnterior
{
    private readonly ConcurrentDictionary<AlcanceDeServicio, DateTimeOffset> _ultimos = new();

    /// <summary>Verdadero si ya pasó al menos <paramref name="intervalo"/> desde el último aviso de ese alcance (y lo registra).</summary>
    public bool DebeAvisar(AlcanceDeServicio alcance, DateTimeOffset ahora, TimeSpan intervalo)
    {
        var anterior = _ultimos.GetOrAdd(alcance, DateTimeOffset.MinValue);
        if (ahora - anterior < intervalo)
        {
            return false;
        }

        _ultimos[alcance] = ahora;
        return true;
    }
}

/// <summary>
/// Verifica la credencial de servicio de toda llamada a la superficie entre servicios (SEG-15): HTTPS, credencial presente e
/// identificada con la actual o la anterior de un alcance, operación declarada y alcance correcto según la matriz (rechazo por defecto),
/// con un 401 uniforme y un evento de seguridad si no. El uso de la credencial anterior durante una rotación se acepta y se alerta. Después
/// resuelve el origen: la dirección reenviada solo vale con la credencial de origen; sin ella se ignora, se registra (AUD-44) y la
/// llamada queda sujeta al tope general de su dirección directa y no a los límites por origen (SEG-17, SEG-32).
/// </summary>
public sealed class CredencialDeServicioMiddleware(
    RequestDelegate siguiente,
    OpcionesDeSuperficieDeServicio superficie,
    OpcionesDeErrores errores,
    AvisosDeCredencialAnterior avisos)
{
    public const string CabeceraDeCredencial = "X-Credencial-De-Servicio";
    public const string CabeceraDeOrigenReenviado = "X-Origen-Reenviado";
    public const string CabeceraDeCredencialDeOrigen = "X-Credencial-De-Origen";
    public const string MotivoDeAlertaDeCredencialAnterior = "credencial-de-servicio-anterior-en-uso";
    public const string ClaveDelAlcance = "babelhub.alcance-de-servicio";

    private static readonly TimeSpan IntervaloEntreAvisos = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(
        HttpContext contexto,
        IAutorizadorDeLlamadasDeServicio autorizador,
        OpcionesDeCredencialesDeEntrada credenciales,
        IResolutorDeOrigenDeServicio resolutor,
        IControlDeLimites control,
        IPublicadorDeAlertasTecnicas alertas,
        IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var declarada = contexto.GetEndpoint()?.Metadata.GetMetadata<OperacionDeServicioAttribute>();
        if (declarada is null && !contexto.Request.Path.StartsWithSegments(superficie.Prefijo, StringComparison.OrdinalIgnoreCase))
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        if (superficie.ExigirHttps && !contexto.Request.IsHttps)
        {
            await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.Prohibido)).ConfigureAwait(false);
            return;
        }

        var directa = DireccionDirecta(contexto);

        // El origen provisional es la dirección directa: así un rechazo deja su evento con quién llamó.
        var provisional = await resolutor.Resolver(directa, null, false, cancelacion: contexto.RequestAborted).ConfigureAwait(false);
        OrigenDeLaSolicitudHttp.Establecer(contexto, provisional.Origen);

        var (alcance, esAnterior) = credenciales.Identificar(UnicoValor(contexto, CabeceraDeCredencial));
        var operacion = declarada?.Operacion ?? new OperacionDeServicio("no-declarada");
        var decision = await autorizador.Autorizar(new LlamadaDeServicio(operacion, alcance, ContextoDeUsuarioDiferido: true), contexto.RequestAborted).ConfigureAwait(false);
        if (!decision.Autorizada)
        {
            await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.NoAutenticado)).ConfigureAwait(false);
            return;
        }

        if (esAnterior && alcance is { } alcanceAnterior && avisos.DebeAvisar(alcanceAnterior, reloj.Ahora, IntervaloEntreAvisos))
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlertaDeCredencialAnterior, reloj.Ahora), contexto.RequestAborted).ConfigureAwait(false);
        }

        var reenviada = UnicoValor(contexto, CabeceraDeOrigenReenviado);
        var credencialDeOrigenValida = credenciales.Comprobar(AlcanceDeServicio.OrigenReenviado, UnicoValor(contexto, CabeceraDeCredencialDeOrigen)) != ResultadoDeCredencialDeEntrada.Invalida;
        var resuelto = await resolutor.Resolver(directa, reenviada, credencialDeOrigenValida, EsLlamadaDeCliente(alcance), contexto.RequestAborted).ConfigureAwait(false);
        OrigenDeLaSolicitudHttp.Establecer(contexto, resuelto.Origen);
        contexto.Request.Headers.Remove(CabeceraDeOrigenReenviado);
        contexto.Request.Headers.Remove(CabeceraDeCredencialDeOrigen);
        contexto.Items[ClaveDelAlcance] = alcance;

        if (resuelto.ReenvioIgnorado && superficie.TopeDeLaDireccionDirecta is { } tope)
        {
            var resultado = await control.Reservar(tope, cancelacion: contexto.RequestAborted).ConfigureAwait(false);
            if (!resultado.EsPermitido)
            {
                await ProblemasHttp.EscribirLimite(contexto, errores, resultado).ConfigureAwait(false);
                return;
            }
        }

        await siguiente(contexto).ConfigureAwait(false);
    }

    /// <summary>Las llamadas que actúan por un cliente final (operación de usuario y lectura de disponibilidad) llevan su origen reenviado; las de sistema no.</summary>
    private static bool EsLlamadaDeCliente(AlcanceDeServicio? alcance) => alcance is AlcanceDeServicio.OperacionDeUsuario or AlcanceDeServicio.LecturaDeDisponibilidad;

    private static string? DireccionDirecta(HttpContext contexto)
    {
        var ip = contexto.Connection.RemoteIpAddress;
        return ip is null ? null : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    /// <summary>El valor de una cabecera que debe venir una sola vez; si falta o se repite, se trata como ausente.</summary>
    private static string? UnicoValor(HttpContext contexto, string cabecera)
    {
        var valores = contexto.Request.Headers[cabecera];
        return valores.Count == 1 ? valores[0] : null;
    }
}

/// <summary>
/// Segunda parte, después de autenticar: una operación de usuario (credencial de alcance «operación de usuario») exige además el contexto
/// de un usuario autenticado, no una cabecera de actor. Rechaza con el mismo 401 uniforme.
/// </summary>
public sealed class ContextoDeUsuarioDeServicioMiddleware(RequestDelegate siguiente, OpcionesDeErrores errores)
{
    public async Task InvokeAsync(HttpContext contexto, IAutorizadorDeLlamadasDeServicio autorizador)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var declarada = contexto.GetEndpoint()?.Metadata.GetMetadata<OperacionDeServicioAttribute>();
        if (declarada is not null && contexto.Items[CredencialDeServicioMiddleware.ClaveDelAlcance] is AlcanceDeServicio.OperacionDeUsuario)
        {
            var decision = await autorizador.Autorizar(new LlamadaDeServicio(declarada.Operacion, AlcanceDeServicio.OperacionDeUsuario), contexto.RequestAborted).ConfigureAwait(false);
            if (!decision.Autorizada)
            {
                await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.NoAutenticado)).ConfigureAwait(false);
                return;
            }
        }

        await siguiente(contexto).ConfigureAwait(false);
    }
}

public static class SuperficieDeServicio
{
    /// <summary>
    /// Los problemas de la superficie entre servicios: un endpoint bajo el prefijo que no declara su operación, o una operación declarada
    /// que la matriz no conoce (nadie podría llamarla: rechazo por defecto). Una lista vacía significa que todo endpoint declara su alcance.
    /// </summary>
    public static IReadOnlyList<string> Verificar(EndpointDataSource fuente, IMatrizDeAlcancesDeServicio matriz, OpcionesDeSuperficieDeServicio opciones)
    {
        ArgumentNullException.ThrowIfNull(fuente);
        ArgumentNullException.ThrowIfNull(matriz);
        ArgumentNullException.ThrowIfNull(opciones);

        var problemas = new List<string>();
        foreach (var endpoint in fuente.Endpoints.OfType<RouteEndpoint>())
        {
            var ruta = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var declarada = endpoint.Metadata.GetMetadata<OperacionDeServicioAttribute>();
            var enSuperficie = ruta.StartsWith(opciones.Prefijo, StringComparison.OrdinalIgnoreCase);

            if (enSuperficie && declarada is null)
            {
                problemas.Add($"{ruta}: no declara su operación de servicio");
            }
            else if (declarada is not null && !matriz.Operaciones.Contains(declarada.Operacion))
            {
                problemas.Add($"{ruta}: la operación «{declarada.Operacion}» no está en la matriz de alcances");
            }
        }

        return problemas;
    }
}

public static class CredencialDeServicioExtensiones
{
    public static IServiceCollection AgregarCredencialDeServicio(
        this IServiceCollection servicios,
        OpcionesDeSuperficieDeServicio superficie,
        OpcionesDeCredencialesDeEntrada credenciales)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton(superficie);
        servicios.AddSingleton(credenciales);
        servicios.AddSingleton<AvisosDeCredencialAnterior>();
        return servicios;
    }

    /// <summary>Va después de la correlación y antes del origen y de la autenticación.</summary>
    public static IApplicationBuilder UsarCredencialDeServicio(this IApplicationBuilder app) => app.UseMiddleware<CredencialDeServicioMiddleware>();

    /// <summary>Va después de la autenticación.</summary>
    public static IApplicationBuilder UsarContextoDeUsuarioDeServicio(this IApplicationBuilder app) => app.UseMiddleware<ContextoDeUsuarioDeServicioMiddleware>();
}
