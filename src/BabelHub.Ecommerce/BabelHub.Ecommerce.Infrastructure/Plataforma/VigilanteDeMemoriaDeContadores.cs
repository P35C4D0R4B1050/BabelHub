using System.Globalization;
using BabelHub.Ecommerce.Application.Plataforma;
using StackExchange.Redis;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// Vigila la memoria del Redis de contadores: al llegar al 70 % de su tope publica una alerta técnica (una vez por cruce), antes de
/// que las escrituras empiecen a fallar por memoria llena y las políticas que fallan cerradas dejen de atender (SEG-26).
/// <para>Necesita una conexión con el modo de administración del cliente habilitado (<c>allowAdmin</c>), porque <c>INFO</c> lo exige; el
/// usuario ACL del motor solo tiene permiso de leer <c>INFO</c>.</para>
/// </summary>
public sealed class VigilanteDeMemoriaDeContadores(
    IConnectionMultiplexer conexion,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    double umbral = 0.7)
{
    public const string MotivoDeAlerta = "memoria-de-contadores-alta";

    private bool _porEncima;

    /// <summary>Uso de memoria sobre el tope (0 a 1); <see langword="null"/> si la instancia no tiene tope y no se puede medir.</summary>
    public async Task<double?> Evaluar(CancellationToken cancelacion = default)
    {
        var servidor = conexion.GetServer(conexion.GetEndPoints()[0]);
        var memoria = await servidor.InfoAsync("memory").ConfigureAwait(false);
        var datos = memoria.SelectMany(g => g).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        if (!datos.TryGetValue("used_memory", out var usada) || !datos.TryGetValue("maxmemory", out var tope)
            || !long.TryParse(usada, NumberStyles.None, CultureInfo.InvariantCulture, out var bytesUsados)
            || !long.TryParse(tope, NumberStyles.None, CultureInfo.InvariantCulture, out var bytesTope)
            || bytesTope <= 0)
        {
            return null;
        }

        var uso = (double)bytesUsados / bytesTope;
        var porEncima = uso >= umbral;
        if (porEncima && !_porEncima)
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlerta, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }

        _porEncima = porEncima;
        return uso;
    }
}
