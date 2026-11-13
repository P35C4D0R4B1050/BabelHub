using System.Security.Claims;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// Qué políticas aplica cada tramo del pipeline (la ausencia de una la omite): el tope previo por huella de origen y el del origen
/// desconocido, el límite de un origen sin sesión (SEG-18) y el de un usuario autenticado (SEG-05).
/// </summary>
public sealed record OpcionesDeLimitesHttp
{
    public ClaveDePolitica? TopePrevio { get; init; }

    public ClaveDePolitica? TopePrevioDelOrigenDesconocido { get; init; }

    public ClaveDePolitica? SinSesionPorOrigen { get; init; }

    public ClaveDePolitica? PorUsuario { get; init; }

    /// <summary>Las rutas (por prefijo) que no se limitan: la salud para el reinicio automático y los archivos estáticos no pasan por los límites.</summary>
    public IReadOnlyList<string> RutasExentas { get; init; } = ["/health"];

    public bool EsExenta(PathString ruta) => RutasExentas.Any(prefijo => ruta.StartsWithSegments(prefijo, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Origen de la solicitud en curso, guardado en la solicitud: lo fija <c>OrigenDelBorde</c> con el proxy conocido (SEG-17) y, hasta
/// entonces o si no se pudo determinar, es el origen desconocido (SEG-23). La Aplicación lo lee por <see cref="IOrigenDeSolicitud"/>.
/// </summary>
public sealed class OrigenDeLaSolicitudHttp(IHttpContextAccessor acceso) : IOrigenDeSolicitud
{
    private const string Clave = "babelhub.origen";

    public OrigenDeSolicitud Actual =>
        acceso.HttpContext?.Items[Clave] is OrigenDeSolicitud origen ? origen : OrigenDeSolicitud.Desconocido;

    public static void Establecer(HttpContext contexto, OrigenDeSolicitud origen)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(origen);
        contexto.Items[Clave] = origen;
    }
}

/// <summary>
/// Tope previo barato por huella de origen, <b>antes</b> de autenticar (SEG-05, SEG-23): una cookie falsa no le cuesta a Identidad una
/// búsqueda de sesión sin límite. El origen desconocido usa su propio tope, más alto. Un rechazo responde la respuesta uniforme de límite
/// (429, o 503 si el almacén no está disponible) y no sigue el pipeline.
/// </summary>
public sealed class TopePrevioMiddleware(RequestDelegate siguiente, OpcionesDeLimitesHttp opciones, OpcionesDeErrores errores)
{
    public async Task InvokeAsync(HttpContext contexto, IControlDeLimites control, IOrigenDeSolicitud origenes)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        if (opciones.EsExenta(contexto.Request.Path) || opciones.TopePrevio is not { } previo)
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        var politica = origenes.Actual.Huella.EsDesconocido ? opciones.TopePrevioDelOrigenDesconocido ?? previo : previo;
        var resultado = await control.Reservar(politica, cancelacion: contexto.RequestAborted).ConfigureAwait(false);
        if (resultado.EsPermitido)
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        await ProblemasHttp.EscribirLimite(contexto, errores, resultado).ConfigureAwait(false);
    }
}

/// <summary>
/// Límites generales, <b>después</b> de autenticar: un usuario autenticado cuenta por su identificador (SEG-05) y una solicitud sin sesión
/// válida cuenta por su origen (SEG-18). Así los usuarios de una red compartida no se perjudican entre sí: las solicitudes con sesión no
/// suman al límite del origen sin sesión.
/// </summary>
public sealed class LimitesGeneralesMiddleware(RequestDelegate siguiente, OpcionesDeLimitesHttp opciones, OpcionesDeErrores errores)
{
    public async Task InvokeAsync(HttpContext contexto, IControlDeLimites control)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        if (opciones.EsExenta(contexto.Request.Path))
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        var usuario = contexto.User.Identity?.IsAuthenticated == true ? contexto.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        ResultadoDeLimite? resultado = null;
        if (usuario is not null)
        {
            if (opciones.PorUsuario is { } porUsuario)
            {
                resultado = await control.Reservar(porUsuario, sujeto: usuario, cancelacion: contexto.RequestAborted).ConfigureAwait(false);
            }
        }
        else if (opciones.SinSesionPorOrigen is { } sinSesion)
        {
            resultado = await control.Reservar(sinSesion, cancelacion: contexto.RequestAborted).ConfigureAwait(false);
        }

        if (resultado is null || resultado.EsPermitido)
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        await ProblemasHttp.EscribirLimite(contexto, errores, resultado).ConfigureAwait(false);
    }
}

public static class LimitesDeUsoHttpExtensiones
{
    public static IServiceCollection AgregarLimitesHttp(this IServiceCollection servicios, OpcionesDeLimitesHttp opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AddHttpContextAccessor();
        servicios.AddSingleton(opciones);
        servicios.AddScoped<IOrigenDeSolicitud, OrigenDeLaSolicitudHttp>();
        return servicios;
    }

    /// <summary>Va después del origen y <b>antes</b> de la autenticación.</summary>
    public static IApplicationBuilder UsarTopePrevio(this IApplicationBuilder app) => app.UseMiddleware<TopePrevioMiddleware>();

    /// <summary>Va <b>después</b> de la autenticación y antes de la autorización.</summary>
    public static IApplicationBuilder UsarLimitesGenerales(this IApplicationBuilder app) => app.UseMiddleware<LimitesGeneralesMiddleware>();
}
