using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// Lee de la configuración el nombre de la estrategia de selección de fuente (SEL-10, D02): la variable de entorno <c>BABELHUB__Inventario__EstrategiaDeSeleccion</c> (clave <c>Inventario:EstrategiaDeSeleccion</c>).
/// Sin valor rige la predeterminada, <c>cercania</c>; con un nombre que no existe el arranque falla. Es la Infraestructura quien lee la configuración: el Dominio y la Aplicación no.
/// </summary>
public static class ConfiguracionDeLaSeleccion
{
    public const string Clave = "Inventario:EstrategiaDeSeleccion";

    /// <summary>El nombre configurado, o <see cref="SeleccionPorCercania.NombreDeLaEstrategia"/> si no hay ninguno.</summary>
    public static string Nombre(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var valor = configuracion[Clave];
        return string.IsNullOrWhiteSpace(valor) ? SeleccionPorCercania.NombreDeLaEstrategia : valor.Trim();
    }

    /// <summary>La estrategia activa: busca el nombre configurado entre las registradas y falla si no existe.</summary>
    public static IEstrategiaDeSeleccion Resolver(IConfiguration configuracion, IEnumerable<IEstrategiaDeSeleccion> registradas) =>
        new RegistroDeEstrategiasDeSeleccion(registradas).Obtener(Nombre(configuracion));
}
