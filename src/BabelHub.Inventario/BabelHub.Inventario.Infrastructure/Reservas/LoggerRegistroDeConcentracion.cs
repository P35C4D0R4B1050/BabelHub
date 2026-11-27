using BabelHub.Inventario.Application.Reservas;
using Microsoft.Extensions.Logging;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// El aviso de concentración en el registro del servicio (SEL-47): un aviso estructurado con la edición, el código de la sucursal y las unidades, y nada más (sin datos personales). El campo se llama
/// <c>CodigoDeSucursal</c> y no <c>Codigo</c>, un nombre que la redacción de registros oculta.
/// </summary>
internal sealed class LoggerRegistroDeConcentracion(ILogger<LoggerRegistroDeConcentracion> registro) : IRegistroDeConcentracion
{
    private static readonly Action<ILogger, Guid, string, int, Exception?> Aviso = LoggerMessage.Define<Guid, string, int>(
        LogLevel.Warning,
        new EventId(4701, "ConcentracionDeReservas"),
        "Reservas concentradas en 15 minutos: edición {EdicionId}, sucursal {CodigoDeSucursal}, {Unidades} unidades.");

    public void Registrar(Guid edicionId, string codigoDeSucursal, int unidades) => Aviso(registro, edicionId, codigoDeSucursal, unidades, null);
}
