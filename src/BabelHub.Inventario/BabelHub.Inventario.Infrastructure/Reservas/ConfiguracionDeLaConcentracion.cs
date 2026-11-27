using System.Globalization;
using BabelHub.Inventario.Application.Reservas.Interno;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// Lee el máximo de unidades de una edición que las reservas de una sucursal pueden apartar en 15 minutos antes de avisar (SEL-47): la variable opcional <c>BABELHUB__Inventario__MaximoApartadoParaAlertar</c> (clave
/// <c>Inventario:MaximoApartadoParaAlertar</c>). Sin valor rige <see cref="ContadorDeConcentracion.MaximoPorDefecto"/>; un valor que no sea un entero mayor que 0 impide arrancar.
/// </summary>
public static class ConfiguracionDeLaConcentracion
{
    public const string Clave = "Inventario:MaximoApartadoParaAlertar";

    public const string Variable = "BABELHUB__Inventario__MaximoApartadoParaAlertar";

    public static int Maximo(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var valor = configuracion[Clave];
        if (string.IsNullOrEmpty(valor))
        {
            return ContadorDeConcentracion.MaximoPorDefecto;
        }

        return int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var maximo) && maximo > 0
            ? maximo
            : throw new InvalidOperationException($"La variable {Variable} debe ser un entero mayor que 0.");
    }
}
