using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>Corre una corrida de un trabajo como el actor «sistema»: lo aporta el anfitrión (la Api, T16), que fija «sistema», ejecuta la corrida con su cancelación y limpia el actor al terminar, aunque falle.</summary>
public delegate Task AlcanceDeSistemaDeReclamaciones(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>
/// Los trabajos de fondo de Reclamaciones (T14 y T31, REC-17, REC-26): sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, su programación y su construcción sobre los casos de uso de T12. La anonimización corre <b>cada día a las 04:00 de Lima</b> (y una vez al arrancar, para no alertar el día del
/// despliegue); la revisión de que corrió, cada hora y sin correr al arrancar (alerta si pasaron 24 horas sin una corrida completa).
/// </summary>
public static class TrabajosDeFondoDeReclamaciones
{
    public const string AnonimizacionDiaria = "reclamaciones-anonimizacion-diaria";

    public const string RevisionDeLaAnonimizacion = "reclamaciones-revision-de-la-anonimizacion";

    /// <summary>El recorrido único de la vinculación inicial (REC-26, T31): corre cada día y una vez al arrancar hasta completarse; después no hace nada.</summary>
    public const string VinculacionInicial = "reclamaciones-vinculacion-inicial";

    /// <summary>La alerta técnica si el recorrido no se completó 7 días después de empezar (REC-34).</summary>
    public const string RevisionDeLaVinculacionInicial = "reclamaciones-revision-de-la-vinculacion-inicial";

    /// <summary>La hora de Lima de la anonimización diaria.</summary>
    public static readonly TimeSpan HoraDeLima = TimeSpan.FromHours(4);

    public static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    /// <summary>Lo que falta para la próxima 04:00 de Lima (siempre positivo: a la hora exacta, la de mañana).</summary>
    public static TimeSpan EsperaHastaLaProximaAnonimizacion(DateTimeOffset ahora)
    {
        var enLima = ahora.ToOffset(DesfaseDeLima);
        var hoy = new DateTimeOffset(enLima.Year, enLima.Month, enLima.Day, 0, 0, 0, DesfaseDeLima) + HoraDeLima;
        var proxima = hoy > enLima ? hoy : hoy.AddDays(1);
        return proxima - enLima;
    }

    public static IReadOnlyList<TrabajoDeFondoDeReclamaciones> Todos(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeReclamaciones alcanceDeSistema, IReloj reloj, TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDeReclamaciones Crear<TCaso>(string nombre, Func<DateTimeOffset, TimeSpan> espera, bool alIniciar, Func<TCaso, CancellationToken, Task> ejecutar)
            where TCaso : notnull =>
            new(
                nombre,
                async ct =>
                {
                    using var alcance = fabrica.CreateScope();
                    await ejecutar(alcance.ServiceProvider.GetRequiredService<TCaso>(), ct).ConfigureAwait(false);
                },
                registro,
                alcanceDeSistema,
                reloj,
                espera,
                alIniciar,
                tiempo);

        return
        [
            Crear<AnonimizarHojasVencidas>(AnonimizacionDiaria, EsperaHastaLaProximaAnonimizacion, alIniciar: true, async (caso, ct) => ExigirExito(await caso.Ejecutar(ct).ConfigureAwait(false))),
            Crear<RevisarHojasSinAnonimizar>(RevisionDeLaAnonimizacion, _ => TimeSpan.FromHours(1), alIniciar: false, async (caso, ct) => ExigirExito(await caso.Ejecutar(ct).ConfigureAwait(false))),
            Crear<VincularHojasExistentes>(VinculacionInicial, EsperaHastaLaProximaAnonimizacion, alIniciar: true, async (caso, ct) => ExigirExito(await caso.Ejecutar(ct).ConfigureAwait(false))),
            Crear<RevisarVinculacionInicial>(RevisionDeLaVinculacionInicial, _ => TimeSpan.FromHours(24), alIniciar: false, async (caso, ct) => ExigirExito(await caso.Ejecutar(ct).ConfigureAwait(false))),
        ];
    }

    /// <summary>Un caso de uso que rechaza (por ejemplo, porque el actor no es «sistema» o la base no responde) es una corrida fallida, nunca un éxito silencioso: falla cerrada.</summary>
    private static void ExigirExito<T>(Resultado<T> resultado)
    {
        if (!resultado.EsExito)
        {
            throw new InvalidOperationException("El trabajo de fondo de reclamaciones fue rechazado.");
        }
    }
}

/// <summary>
/// Un trabajo de fondo de Reclamaciones (T14): corre su corrida <b>dentro del <see cref="AlcanceDeSistemaDeReclamaciones"/> que le da el anfitrión</b>, que fija el actor «sistema» (sin ese contexto el caso de uso lo rechaza: es un defecto, no un permiso) y registra el resultado enumerado de cada
/// corrida en <c>plataforma.trabajos_de_fondo</c>. <b>Falla cerrada</b>: una corrida que lanza queda registrada como fallo y se reintenta en la próxima; nunca deja caer el servicio y solo se propaga la cancelación del apagado ordenado. La alerta de «sin correr en 24 horas» no es de este
/// ejecutor: la publica la revisión de la anonimización (<c>RevisarHojasSinAnonimizar</c>), que lee la marca que deja la propia función de base de datos.
/// </summary>
public sealed class TrabajoDeFondoDeReclamaciones(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDeReclamaciones alcanceDeSistema,
    IReloj reloj,
    Func<DateTimeOffset, TimeSpan> esperaHastaLaProxima,
    bool correrAlIniciar,
    TimeProvider? tiempo = null)
{
    public string Nombre { get; } = nombre;

    /// <summary>Ejecuta la corrida ahora y la registra. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            await alcanceDeSistema(corrida, cancelacion).ConfigureAwait(false);
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>Corre hasta que se cancele: una corrida al empezar (si se pidió) y otra cada vez que llega su hora.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        try
        {
            if (correrAlIniciar)
            {
                await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
            }

            while (!cancelacion.IsCancellationRequested)
            {
                await Task.Delay(esperaHastaLaProxima(reloj.Ahora), tiempo ?? TimeProvider.System, cancelacion).ConfigureAwait(false);
                await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado del servicio.
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
}

/// <summary>El servicio en segundo plano que corre los trabajos de Reclamaciones en paralelo hasta el apagado (T14; lo hospeda la Api en T16).</summary>
public sealed class ServicioDeTrabajosDeReclamaciones(IReadOnlyList<TrabajoDeFondoDeReclamaciones> trabajos) : BackgroundService
{
    public IReadOnlyList<TrabajoDeFondoDeReclamaciones> Trabajos => trabajos;

    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
