using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Notificaciones;

/// <summary>Corre una corrida de un trabajo de Notificaciones como el actor «sistema» (SEG-15): lo aporta el anfitrión, que fija «sistema», ejecuta la corrida con su cancelación y limpia el actor al terminar, aunque falle.</summary>
public delegate Task AlcanceDeSistemaDeNotificaciones(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Cuándo corre un trabajo de fondo de Notificaciones, cuántos fallos seguidos hacen falta para avisar y el plazo máximo de una corrida.</summary>
public sealed record OpcionesDelTrabajoDeNotificaciones(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3, TimeSpan? PlazoMaximoDeLaCorrida = null)
{
    /// <summary>Una corrida colgada (red o base de datos) se cancela y cuenta como fallo: sin un plazo nunca llegaría a alertar.</summary>
    public TimeSpan PlazoDeLaCorrida => PlazoMaximoDeLaCorrida ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Un trabajo de fondo de Notificaciones (T14; NOT-04, AUD-31): corre su corrida cada intervalo <b>como «sistema»</b>, registra el resultado enumerado de cada corrida en <c>plataforma.trabajos_de_fondo</c> y, tras varios fallos seguidos,
/// publica la alerta técnica <c>trabajo-de-fondo-detenido</c> (sin datos personales). Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado. En los modos <c>Ensayo</c> y <c>Restauracion</c> no se registra (PLA-19).
/// </summary>
public sealed class TrabajoDeFondoDeNotificaciones(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDeNotificaciones alcanceDeSistema,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDeNotificaciones opciones,
    TimeProvider? tiempo = null)
{
    /// <summary>El motivo del catálogo cerrado de alertas técnicas para un trabajo de fondo que no corre.</summary>
    public const string MotivoDelTrabajoDetenido = "trabajo-de-fondo-detenido";

    private int _fallosSeguidos;

    public string Nombre { get; } = nombre;

    /// <summary>Ejecuta la corrida ahora y la registra. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            plazo.CancelAfter(opciones.PlazoDeLaCorrida);
            await alcanceDeSistema(corrida, plazo.Token).ConfigureAwait(false);
            _fallosSeguidos = 0;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            if (_fallosSeguidos >= opciones.FallosSeguidosParaAlertar && (_fallosSeguidos - opciones.FallosSeguidosParaAlertar) % opciones.FallosSeguidosParaAlertar == 0)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Corre hasta que se cancele: una corrida al empezar y otra en cada intervalo.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(opciones.Intervalo, tiempo ?? TimeProvider.System);
        try
        {
            do
            {
                await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
            }
            while (await temporizador.WaitForNextTickAsync(cancelacion).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado del servicio.
        }
        catch (Exception) when (cancelacion.IsCancellationRequested)
        {
            // Una falla de la red o de la base de datos con el apagado ya pedido: no se propaga ni detiene el anfitrión.
        }
    }

    private async Task Registrar(ResultadoDeTrabajo resultado, CancellationToken cancelacion)
    {
        try
        {
            await registro.Registrar(Nombre, resultado, reloj.Ahora, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El registro de la corrida es informativo: si falla, el trabajo sigue.
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDelTrabajoDetenido, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>Los trabajos de fondo de Notificaciones con sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, sus intervalos y sus corridas.</summary>
public static class TrabajosDeNotificaciones
{
    /// <summary>Envía los avisos pendientes (NOT-04): cada 30 segundos, por lotes de 25 y hasta 20 lotes por corrida.</summary>
    public const string EnvioDeAvisos = "notificaciones-envio-de-avisos";

    /// <summary>Depura los avisos terminados hace más de 90 días (AUD-31): cada 6 horas.</summary>
    public const string PurgaDeAvisos = "notificaciones-purga-de-avisos";

    public static readonly OpcionesDelTrabajoDeNotificaciones OpcionesDelEnvio = new(TimeSpan.FromSeconds(30));

    public static readonly OpcionesDelTrabajoDeNotificaciones OpcionesDeLaPurga = new(TimeSpan.FromHours(6));

    public const int LotesPorCorrida = 20;

    public static IReadOnlyList<TrabajoDeFondoDeNotificaciones> Todos(
        IServiceScopeFactory fabrica,
        IRegistroDeTrabajosDeFondo registro,
        AlcanceDeSistemaDeNotificaciones alcanceDeSistema,
        IPublicadorDeAlertasTecnicas alertas,
        IReloj reloj,
        TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDeNotificaciones Crear(string nombre, OpcionesDelTrabajoDeNotificaciones opciones, Func<IServiceProvider, CancellationToken, Task> ejecutar) =>
            new(
                nombre,
                async ct =>
                {
                    using var alcance = fabrica.CreateScope();
                    await ejecutar(alcance.ServiceProvider, ct).ConfigureAwait(false);
                },
                registro,
                alcanceDeSistema,
                alertas,
                reloj,
                opciones,
                tiempo);

        return
        [
            Crear(EnvioDeAvisos, OpcionesDelEnvio, async (_, ct) =>
            {
                // Cada lote usa su propio alcance: un lote lleno sigue con otro, sin dejar a los demás avisos esperando 30 segundos.
                for (var lote = 0; lote < LotesPorCorrida; lote++)
                {
                    using var delLote = fabrica.CreateScope();
                    var tomados = await delLote.ServiceProvider.GetRequiredService<EnviarAvisosPendientes>().Ejecutar(EnviarAvisosPendientes.LoteMaximo, ct).ConfigureAwait(false);
                    if (tomados < EnviarAvisosPendientes.LoteMaximo)
                    {
                        break;
                    }
                }
            }),
            Crear(PurgaDeAvisos, OpcionesDeLaPurga, async (proveedor, ct) => _ = await proveedor.GetRequiredService<PurgarAvisosFinalizados>().Ejecutar(ct).ConfigureAwait(false)),
        ];
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Notificaciones en paralelo hasta el apagado (lo hospeda la Api solo en modo normal).</summary>
public sealed class ServicioDeTrabajosDeNotificaciones(IReadOnlyList<TrabajoDeFondoDeNotificaciones> trabajos) : BackgroundService
{
    public IReadOnlyList<TrabajoDeFondoDeNotificaciones> Trabajos => trabajos;

    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
