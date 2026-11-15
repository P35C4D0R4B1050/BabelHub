using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// El punto único de la Api que registra un acceso denegado (T28; AUD-05, AC-AUD-04, AC-AUD-21): lo llaman el manejador de excepciones, los endpoints de la auditoría cuando la Aplicación responde «prohibido» o «no autenticado»,
/// el middleware de salida (<see cref="DenegacionesMiddleware"/>: autenticación, autorización, credencial de servicio, <c>OnChallenge</c> y <c>OnForbidden</c> terminan todos en un 401 o un 403) y cualquier otro rechazo que no pasó por un caso de uso.
/// Registra <b>un único evento por solicitud</b> (el contexto de denegación lo garantiza aunque el rechazo pase por varios puntos) con una correlación que genera el servidor, el motivo del catálogo y la ruta reducida a su plantilla.
/// Si el host no compuso el registrador, los puntos que lo llaman no hacen nada (los hosts que no usan la auditoría), pero <c>UsarRegistroDeDenegaciones</c> exige que esté compuesto y falla al construir el pipeline.
/// </summary>
public interface IRegistradorDeDenegacionesHttp
{
    Task Registrar(HttpContext contexto, CategoriaDeError categoria, string? motivo, string? codigoDeError);
}

public sealed class RegistradorDeDenegacionesHttp(IDenegacionDeAcceso denegaciones, OpcionesDeErrores opciones) : IRegistradorDeDenegacionesHttp
{
    public const string MotivoSinSesion = "sin-sesion";

    public const string MotivoFueraDeRol = "fuera-de-rol";

    public async Task Registrar(HttpContext contexto, CategoriaDeError categoria, string? motivo, string? codigoDeError)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var predeterminado = categoria == CategoriaDeError.NoAutenticado ? MotivoSinSesion : MotivoFueraDeRol;
        var codigo = codigoDeError ?? CodigosDeError.De(opciones.Servicio, ProblemasHttp.SufijoDe(categoria));
        DenegacionDeAcceso denegacion;
        try
        {
            denegacion = new DenegacionDeAcceso(string.IsNullOrEmpty(motivo) ? predeterminado : motivo, contexto.Request.Path.Value) { CodigoDeError = codigo };
        }
        catch (ArgumentException)
        {
            // Un motivo o un código con otra forma no puede hacer perder el evento: se registra con el motivo predeterminado de su categoría.
            denegacion = new DenegacionDeAcceso(predeterminado, contexto.Request.Path.Value);
        }

        await denegaciones.Registrar(denegacion, CancellationToken.None).ConfigureAwait(false);
    }
}

public static class DenegacionesHttp
{
    /// <summary>Registra el rechazo si es un 401 o un 403 y el host compuso la auditoría; cualquier otra categoría no es un acceso denegado.</summary>
    public static async Task Registrar(HttpContext contexto, CategoriaDeError categoria, string? motivo = null, string? codigoDeError = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (categoria is not (CategoriaDeError.NoAutenticado or CategoriaDeError.Prohibido))
        {
            return;
        }

        var registrador = contexto.RequestServices.GetService<IRegistradorDeDenegacionesHttp>();
        if (registrador is not null)
        {
            await registrador.Registrar(contexto, categoria, motivo, codigoDeError).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Las plantillas de ruta de los endpoints registrados, tal como las usa el módulo de auditoría (AUD-05, AUD-06): segmentos <c>{nombre}</c> sin restricciones, opcionales ni comodines. Se leen del
/// <see cref="EndpointDataSource"/> en el primer uso (después de mapear los endpoints); una plantilla fuera de forma la descarta el módulo.
/// </summary>
public sealed partial class PlantillasDeRutaDeLaApi(EndpointDataSource fuente) : IPlantillasDeRuta
{
    private readonly Lazy<IReadOnlyCollection<string>> _todas = new(() =>
        Array.AsReadOnly(fuente.Endpoints.OfType<RouteEndpoint>()
            .Select(e => "/" + Restriccion().Replace(e.RoutePattern.RawText ?? string.Empty, "{$1}").TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray()));

    public IReadOnlyCollection<string> Todas => _todas.Value;

    [System.Text.RegularExpressions.GeneratedRegex(@"\{(\w+)[:?*][^}]*\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant, 500)]
    private static partial System.Text.RegularExpressions.Regex Restriccion();
}

/// <summary>
/// Va justo dentro del manejador de errores y fuera de la credencial de servicio y de la autenticación: si la respuesta termina en 401 o 403 sin que un caso de uso lo haya registrado (el contexto de denegación evita el
/// segundo evento), lo registra (T28). Cubre el rechazo de la credencial de servicio, el de la autenticación y la autorización (el reto y la prohibición del esquema, o el resultado de una política) también contra el servicio directo.
/// </summary>
public sealed class DenegacionesMiddleware(RequestDelegate siguiente)
{
    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await siguiente(contexto).ConfigureAwait(false);

        var categoria = contexto.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => CategoriaDeError.NoAutenticado,
            StatusCodes.Status403Forbidden => CategoriaDeError.Prohibido,
            _ => CategoriaDeError.Inesperado,
        };
        await DenegacionesHttp.Registrar(contexto, categoria).ConfigureAwait(false);
    }
}

public static class DenegacionesHttpExtensiones
{
    /// <summary>
    /// Registra lo que la Api aporta para escribir los accesos denegados: las plantillas de ruta de sus endpoints y el registrador. El contexto de la solicitud (<b>Scoped</b>: con otra vida se registraría un solo evento por proceso) y el resolutor de rutas son del módulo (<c>AgregarModuloAuditoria</c>).
    /// El host además compone el módulo de auditoría (<c>IDenegacionDeAcceso</c>).
    /// </summary>
    public static IServiceCollection AgregarRegistroDeDenegaciones(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton<IPlantillasDeRuta, PlantillasDeRutaDeLaApi>();
        servicios.AddScoped<IRegistradorDeDenegacionesHttp, RegistradorDeDenegacionesHttp>();
        // El registro de petición del servidor web escribe la dirección completa con su consulta (el usuario, el origen y el texto buscado de la auditoría, AUD-06, SEG-10): solo avisos y errores.
        servicios.AddLogging(registro => registro.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning));
        return servicios;
    }

    /// <summary>
    /// Va después de <c>UsarManejoDeErrores</c> y antes de la credencial de servicio y de la autenticación. <b>Falla al construir el pipeline</b> si el anfitrión no compuso el registrador ni el módulo de auditoría
    /// (<c>IDenegacionDeAcceso</c>): un servicio que rechaza sin registrar no arranca (AGENTS §5).
    /// </summary>
    public static IApplicationBuilder UsarRegistroDeDenegaciones(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var servicios = app.ApplicationServices.GetRequiredService<IServiceProviderIsService>();
        if (!servicios.IsService(typeof(IRegistradorDeDenegacionesHttp)) || !servicios.IsService(typeof(IDenegacionDeAcceso)))
        {
            throw new InvalidOperationException("El anfitrión no compuso el registro de accesos denegados: faltan AgregarRegistroDeDenegaciones y el módulo de auditoría.");
        }

        return app.UseMiddleware<DenegacionesMiddleware>();
    }
}
