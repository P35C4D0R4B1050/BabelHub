using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Qué host sirve este proceso (AD15, D03): <c>tienda</c> o <c>gestion</c>, y los hosts que acepta el servidor. Es la configuración del <b>rol del proceso</b>
/// (<c>BABELHUB__Web__Host</c>), distinta de <c>BABELHUB__Web__HostsPermitidos</c> (los nombres de host que aceptan las solicitudes y que usa la comprobación
/// de <c>Origin</c>, T16). Ninguna es un secreto.
/// </summary>
public sealed record OpcionesDelHostWeb(HostWeb Host, IReadOnlyList<string> HostsPermitidos)
{
    public const string VariableDelHost = "BABELHUB__Web__Host";
    public const string VariableDeLosHostsPermitidos = "BABELHUB__Web__HostsPermitidos";

    /// <summary>
    /// Lee el rol y los hosts permitidos de la configuración. En producción ambos son obligatorios y el arranque falla nombrando la variable; fuera de
    /// producción faltar el rol significa <c>tienda</c>. Un valor presente pero inválido falla siempre.
    /// </summary>
    public static OpcionesDelHostWeb Leer(IConfiguration configuracion, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        var texto = configuracion["Web:Host"];
        HostWeb host;
        if (string.IsNullOrWhiteSpace(texto))
        {
            host = esProduccion
                ? throw new ArranqueRechazadoException($"Falta {VariableDelHost}: debe ser «tienda» o «gestion».")
                : HostWeb.Tienda;
        }
        else
        {
            host = texto switch
            {
                "tienda" => HostWeb.Tienda,
                "gestion" => HostWeb.Gestion,
                _ => throw new ArranqueRechazadoException($"{VariableDelHost} debe ser «tienda» o «gestion»."),
            };
        }

        var permitidos = (configuracion["Web:HostsPermitidos"] ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (permitidos.Any(p => p.Contains('*', StringComparison.Ordinal) || p.Contains(' ', StringComparison.Ordinal)))
        {
            throw new ArranqueRechazadoException($"{VariableDeLosHostsPermitidos} no admite comodines ni espacios: lista los hosts exactos.");
        }

        if (esProduccion && permitidos.Count == 0)
        {
            throw new ArranqueRechazadoException($"Falta {VariableDeLosHostsPermitidos}: lista los hosts que acepta este servidor.");
        }

        return new OpcionesDelHostWeb(host, permitidos);
    }
}

/// <summary>
/// Mapea las áreas por host (D03): un controlador de un área solo existe en el host que la sirve (Tienda en <c>tienda</c>; Sucursal y Administración en
/// <c>gestion</c>); los controladores sin área (ingreso, cuenta, textos legales, sin permiso, error, cierre de sesión) existen en ambos. Un área que no está en
/// la lista cerrada hace fallar el arranque: no se sirve por omisión.
/// </summary>
public sealed class ConvencionDeAreasPorHost(OpcionesDelHostWeb opciones, IAreasDeInterfaz areas) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);
        foreach (var controlador in application.Controllers.ToList())
        {
            if (!controlador.RouteValues.TryGetValue("area", out var nombre) || string.IsNullOrEmpty(nombre))
            {
                continue;
            }

            var area = AreaDe(nombre) ?? throw new InvalidOperationException($"El área «{nombre}» del controlador {controlador.ControllerType.Name} no está en la lista cerrada de áreas de la interfaz.");
            if (areas.HostDeLaArea(area) != opciones.Host)
            {
                application.Controllers.Remove(controlador);
            }
        }
    }

    /// <summary>La lista cerrada de áreas por su nombre en las rutas (la carpeta de Administración se llama <c>Admin</c>).</summary>
    internal static AreaWeb? AreaDe(string nombre) => nombre switch
    {
        "Tienda" => AreaWeb.Tienda,
        "Sucursal" => AreaWeb.Sucursal,
        "Admin" or "Administracion" => AreaWeb.Administracion,
        _ => null,
    };
}

internal sealed class ConfigurarAreasPorHost(OpcionesDelHostWeb opciones, IAreasDeInterfaz areas) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) => options.Conventions.Add(new ConvencionDeAreasPorHost(opciones, areas));
}

public static class HostDeLaWebExtensiones
{
    /// <summary>Registra el host de este proceso, el mapeo de las áreas por host y los hosts que acepta el servidor. Exige <see cref="IAreasDeInterfaz"/> (módulo <c>Web</c>).</summary>
    public static IServiceCollection AgregarHostWeb(this IServiceCollection servicios, OpcionesDelHostWeb opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IConfigureOptions<MvcOptions>, ConfigurarAreasPorHost>();
        if (opciones.HostsPermitidos.Count > 0)
        {
            servicios.PostConfigure<HostFilteringOptions>(o =>
            {
                o.AllowedHosts = [.. opciones.HostsPermitidos];
                o.AllowEmptyHosts = false;
            });
        }

        return servicios;
    }
}
