using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>
/// Implementa <see cref="IContadorDeEventosDeSeguridad"/> (puerto de <c>Plataforma</c>) para las reglas de umbral de SEG-09: cuenta una acción del catálogo en una
/// ventana de <b>como máximo 5 minutos</b> (deslizante: <c>desde</c> y <c>hasta</c> los fija quien evalúa). Los accesos denegados son el conteo local de
/// <c>registros</c>; los ingresos fallidos suman el conteo local de los rechazos del límite de ingresos y el de Identidad por
/// <see cref="IFuenteDeRegistrosAislados"/> (Auditoría no lee <c>identidad.registros</c>). Solo devuelve un número, con espera corta y fallo seguro: si Identidad
/// no responde, el límite sigue protegiendo con el conteo local y se publica una alerta técnica (T10).
/// </summary>
public sealed class ContadorDeEventosDeSeguridad(
    IConteoLocalDeEventos local,
    IFuenteDeRegistrosAislados identidad,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    TimeSpan? esperaDeIdentidad = null) : IContadorDeEventosDeSeguridad
{
    public static readonly TimeSpan EsperaDeIdentidad = TimeSpan.FromSeconds(2);

    public async Task<int> Contar(string motivo, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        if (desde.Offset != TimeSpan.Zero || hasta.Offset != TimeSpan.Zero || hasta <= desde || hasta - desde > SolicitudDeConteo.VentanaMaxima)
        {
            throw new ArgumentException("El conteo es de una ventana positiva de 5 minutos como máximo y en UTC.", nameof(hasta));
        }

        switch (motivo)
        {
            case MotivosDeAlerta.AccesosDenegados:
                return await local.ContarRegistros(AccionesAuditables.AccesoDenegado, desde, hasta, cancelacion).ConfigureAwait(false);

            case MotivosDeAlerta.IngresosFallidos:
                var propios = await local.ContarRechazosDeLimite(MotivosDeAlerta.IngresosFallidos, desde, hasta, cancelacion).ConfigureAwait(false);
                return Sumar(propios, await ContarEnIdentidad(desde, hasta, cancelacion).ConfigureAwait(false));

            default:
                throw new ArgumentException("El motivo no es un umbral conocido: se cuentan los accesos denegados y los ingresos fallidos.", nameof(motivo));
        }
    }

    private async Task<int> ContarEnIdentidad(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion)
    {
        try
        {
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            espera.CancelAfter(esperaDeIdentidad ?? EsperaDeIdentidad);
            return await identidad.Contar(new SolicitudDeConteo(AccionesAuditables.IngresoFallido, desde, hasta), espera.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await AlertarSinInterrumpir().ConfigureAwait(false);
            return 0;
        }
    }

    private static int Sumar(int a, int b) => (int)Math.Min((long)a + b, int.MaxValue);

    private async Task AlertarSinInterrumpir()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivosDeAlerta.ConteoDeIngresosNoDisponible, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin a dónde mandar la falla: el conteo local sigue protegiendo.
        }
    }
}
