using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.Infrastructure.Reservas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// El registro de dependencias del módulo <c>Reservas</c>, que se completa con cada tarea. Hoy: la estrategia de selección de fuente elegida por nombre en la configuración (T06). Vive fuera de la carpeta del módulo
/// porque la Infraestructura de un módulo no puede depender de la de <c>Plataforma</c> (regla 13).
/// </summary>
public static class ComposicionDeReservas
{
    /// <summary>
    /// Registra <c>SeleccionPorCercania</c> y las <paramref name="alternativas"/> (por ejemplo, una de prueba) y la estrategia activa según <c>Inventario:EstrategiaDeSeleccion</c>. El nombre inexistente falla al
    /// resolver la estrategia, y <see cref="ExigirEstrategiaDeSeleccion"/> lo hace a propósito en el arranque (SEL-10).
    /// </summary>
    public static IServiceCollection AgregarEstrategiasDeSeleccion(this IServiceCollection servicios, IConfiguration configuracion, IEnumerable<IEstrategiaDeSeleccion>? alternativas = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);
        var registradas = new List<IEstrategiaDeSeleccion> { new SeleccionPorCercania() };
        registradas.AddRange(alternativas ?? []);

        servicios.AddSingleton(new RegistroDeEstrategiasDeSeleccion(registradas));
        servicios.AddSingleton(sp => sp.GetRequiredService<RegistroDeEstrategiasDeSeleccion>().Obtener(ConfiguracionDeLaSeleccion.Nombre(configuracion)));
        return servicios;
    }

    /// <summary>Resuelve la estrategia activa al arrancar: si el nombre configurado no existe, el arranque falla con <see cref="EstrategiaDeSeleccionDesconocidaException"/>.</summary>
    public static IEstrategiaDeSeleccion ExigirEstrategiaDeSeleccion(IServiceProvider proveedor)
    {
        ArgumentNullException.ThrowIfNull(proveedor);
        return proveedor.GetRequiredService<IEstrategiaDeSeleccion>();
    }
}
