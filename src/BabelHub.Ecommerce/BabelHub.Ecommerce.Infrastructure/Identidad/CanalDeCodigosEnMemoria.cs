using System.Threading.Channels;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// El canal en memoria, acotado y no persistente de las solicitudes de código (T34; IDE-65, D05): <see cref="Encolar"/> es síncrono y de tiempo constante y devuelve <b>falso si el canal está lleno</b> (la solicitud se
/// descarta en silencio, la respuesta a la persona es la misma y quien llama publica la alerta técnica). Nada se guarda en disco: un código no se reintenta, vence a los 10 minutos.
/// </summary>
public sealed class CanalDeCodigosEnMemoria : IColaDeCodigos
{
    public const int CapacidadPorDefecto = 1000;

    private readonly Channel<SolicitudDeCodigoEnSegundoPlano> _canal;

    public CanalDeCodigosEnMemoria(int capacidad = CapacidadPorDefecto)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacidad, 1);
        _canal = Channel.CreateBounded<SolicitudDeCodigoEnSegundoPlano>(new BoundedChannelOptions(capacidad)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ChannelReader<SolicitudDeCodigoEnSegundoPlano> Lector => _canal.Reader;

    public bool Encolar(SolicitudDeCodigoEnSegundoPlano solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return _canal.Writer.TryWrite(solicitud);
    }

    /// <summary>Cierra el canal al apagar: los consumidores terminan al vaciarlo.</summary>
    public void Completar() => _canal.Writer.TryComplete();
}

/// <summary>
/// Los consumidores del canal de códigos (T34): cada solicitud se procesa en su propio alcance de servicios y <b>como «sistema»</b> (lo fija el anfitrión por <see cref="AlcanceDeSistemaDeIdentidad"/>); busca la cuenta,
/// genera el código y lo envía por el transporte sin cola ni reintento. Un fallo de una solicitud no detiene a las demás ni al servicio: la persona pide otro código.
/// </summary>
public sealed class ServicioDeCodigosEnSegundoPlano(CanalDeCodigosEnMemoria canal, IServiceScopeFactory fabrica, AlcanceDeSistemaDeIdentidad alcanceDeSistema, int consumidores = ServicioDeCodigosEnSegundoPlano.ConsumidoresPorDefecto) : BackgroundService
{
    public const int ConsumidoresPorDefecto = 4;

    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(Enumerable.Range(0, consumidores).Select(_ => Consumir(cancelacion)));

    internal async Task Consumir(CancellationToken cancelacion)
    {
        try
        {
            await foreach (var solicitud in canal.Lector.ReadAllAsync(cancelacion).ConfigureAwait(false))
            {
                try
                {
                    await alcanceDeSistema(
                        async ct =>
                        {
                            using var alcance = fabrica.CreateScope();
                            await alcance.ServiceProvider.GetRequiredService<ProcesarSolicitudDeCodigo>().Ejecutar(solicitud, ct).ConfigureAwait(false);
                        },
                        cancelacion).ConfigureAwait(false);
                }
                catch (Exception) when (!cancelacion.IsCancellationRequested)
                {
                    // Una solicitud que falla se pierde sin más: la persona pide otro código (IDE-65). Ni la solicitud ni el correo se registran.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado del servicio.
        }
    }
}
