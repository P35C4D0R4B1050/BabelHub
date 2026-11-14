using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// Implementa <see cref="IContadorDeEventosDeSeguridad"/> (puerto de <c>Plataforma</c>) para la regla de umbral de SEG-09 que evalúa el Inventario: los accesos
/// denegados de los últimos 5 minutos como máximo (ventana deslizante), el conteo local de <c>registros</c>. El Inventario no tiene ingresos (T10). Solo devuelve un número.
/// </summary>
public sealed class ContadorDeEventosDeSeguridad(IConteoLocalDeEventos local) : IContadorDeEventosDeSeguridad
{
    public static readonly TimeSpan VentanaMaxima = TimeSpan.FromMinutes(5);

    public Task<int> Contar(string motivo, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        if (desde.Offset != TimeSpan.Zero || hasta.Offset != TimeSpan.Zero || hasta <= desde || hasta - desde > VentanaMaxima)
        {
            throw new ArgumentException("El conteo es de una ventana positiva de 5 minutos como máximo y en UTC.", nameof(hasta));
        }

        return motivo == MotivosDeAlerta.AccesosDenegados
            ? local.ContarRegistros(AccionesAuditables.AccesoDenegado, desde, hasta, cancelacion)
            : throw new ArgumentException("El Inventario evalúa solo los accesos denegados.", nameof(motivo));
    }
}
