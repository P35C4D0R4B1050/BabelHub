using System.Net;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>Una comprobación de un componente del que depende el servicio (la base, el almacén de contadores…), con un nombre estable.</summary>
public interface IComprobacionDeSalud
{
    string Nombre { get; }

    Task<bool> Comprobar(CancellationToken cancelacion);
}

/// <summary>Qué se considera la red privada: solo desde ahí <c>/health/ready</c> dice qué componente falla.</summary>
public sealed class OpcionesDeSalud
{
    public static readonly string[] RedesPrivadasPorDefecto = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7", "fe80::/10"];

    public IReadOnlyList<string> RedesPrivadas { get; init; } = RedesPrivadasPorDefecto;

    /// <summary>Plazo de cada comprobación: un componente que no responde cuenta como caído.</summary>
    public TimeSpan PlazoPorComponente { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Salud del servicio para Docker Compose, el reinicio automático y el monitoreo (RNF03). <c>/health/live</c> dice que el proceso
/// responde y nada más. <c>/health/ready</c> comprueba los componentes: 200 «listo» o 503; el detalle de cuál falla va solo a quien
/// llega por la red privada (la dirección de la conexión, nunca una cabecera de reenvío), y a los demás, el error uniforme de
/// «servicio no disponible», sin datos internos (versiones, cadenas de conexión, mensajes de excepción).
/// </summary>
public static class SaludHttp
{
    public static IEndpointRouteBuilder MapearSalud(this IEndpointRouteBuilder rutas, OpcionesDeSalud? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var configuracion = opciones ?? new OpcionesDeSalud();
        var redes = configuracion.RedesPrivadas.Select(IPNetwork.Parse).ToList();

        rutas.MapGet("/health/live", (HttpContext contexto) =>
        {
            contexto.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { estado = "vivo" });
        });

        rutas.MapGet("/health/ready", async (HttpContext contexto, IEnumerable<IComprobacionDeSalud> comprobaciones) =>
        {
            contexto.Response.Headers.CacheControl = "no-store";
            var resultados = await Task.WhenAll(comprobaciones.Select(c => Comprobar(c, configuracion.PlazoPorComponente, contexto.RequestAborted))).ConfigureAwait(false);
            var listo = resultados.All(r => r.Ok);
            var privada = EsRedPrivada(contexto.Connection.RemoteIpAddress, redes);

            if (listo)
            {
                return privada
                    ? Results.Ok(new { estado = "listo", componentes = Detalle(resultados) })
                    : Results.Ok(new { estado = "listo" });
            }

            if (privada)
            {
                return Results.Json(new { estado = "no-disponible", componentes = Detalle(resultados) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var errores = contexto.RequestServices.GetRequiredService<OpcionesDeErrores>();
            var problema = ProblemasHttp.Crear(contexto, errores, CategoriaDeError.NoDisponible);
            await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
            return Results.Empty;
        });

        return rutas;
    }

    private static object[] Detalle((string Nombre, bool Ok)[] resultados) =>
        [.. resultados.OrderBy(r => r.Nombre, StringComparer.Ordinal).Select(r => new { nombre = r.Nombre, estado = r.Ok ? "ok" : "falla" })];

    private static async Task<(string Nombre, bool Ok)> Comprobar(IComprobacionDeSalud comprobacion, TimeSpan plazo, CancellationToken cancelacion)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(plazo);
        try
        {
            return (comprobacion.Nombre, await comprobacion.Comprobar(limite.Token).ConfigureAwait(false));
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Una excepción o un plazo vencido es un componente caído; su mensaje no sale de aquí.
            return (comprobacion.Nombre, false);
        }
    }

    private static bool EsRedPrivada(IPAddress? direccion, List<IPNetwork> redes)
    {
        if (direccion is null)
        {
            return false;
        }

        var normalizada = direccion.IsIPv4MappedToIPv6 ? direccion.MapToIPv4() : direccion;
        return redes.Any(r => r.Contains(normalizada));
    }
}
