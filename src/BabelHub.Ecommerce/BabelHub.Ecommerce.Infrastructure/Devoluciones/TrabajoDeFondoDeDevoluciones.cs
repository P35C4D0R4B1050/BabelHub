using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Devoluciones;

/// <summary>
/// Corre una corrida de un trabajo de Devoluciones como el actor «sistema»: lo aporta el anfitrión (la Api), que es de los pocos lugares que fijan el actor (regla de arquitectura): fija «sistema», ejecuta la corrida con su cancelación y
/// limpia el actor al terminar, aunque falle. Un trabajo de fondo sin actor es un defecto, no un permiso.
/// </summary>
public delegate Task AlcanceDeSistemaDeDevoluciones(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>
/// Cuándo corre un trabajo de fondo de Devoluciones: cada <see cref="Intervalo"/> o, si se da <see cref="HoraEnLima"/>, <b>una vez al día a esa hora de Lima</b> (Lima no tiene horario de verano, UTC−5); cuántos fallos seguidos hacen falta para
/// avisar y el plazo máximo de una corrida.
/// </summary>
public sealed record OpcionesDelTrabajoDeDevoluciones(TimeSpan Intervalo, TimeOnly? HoraEnLima = null, int FallosSeguidosParaAlertar = 3, TimeSpan? PlazoMaximoDeLaCorrida = null)
{
    /// <summary>Una corrida colgada (red o base de datos) se cancela y cuenta como fallo: sin un plazo nunca llegaría a alertar.</summary>
    public TimeSpan PlazoDeLaCorrida => PlazoMaximoDeLaCorrida ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Un trabajo de fondo de Devoluciones (T22; DEV-14, DEV-21, DEV-27): corre su corrida <b>como «sistema»</b>, registra el resultado enumerado de cada corrida en <c>plataforma.trabajos_de_fondo</c> y, tras varios fallos seguidos, publica la alerta técnica
/// <c>trabajo-de-fondo-detenido</c> (sin datos personales). <b>Falla cerrado</b>: un caso de uso que rechaza (porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso. Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// </summary>
public sealed class TrabajoDeFondoDeDevoluciones(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDeDevoluciones alcanceDeSistema,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDeDevoluciones opciones,
    TimeProvider? tiempo = null)
{
    /// <summary>El motivo del catálogo cerrado de alertas técnicas para un trabajo de fondo que no corre.</summary>
    public const string MotivoDelTrabajoDetenido = "trabajo-de-fondo-detenido";

    private static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    private int _fallosSeguidos;

    public string Nombre { get; } = nombre;

    /// <summary>Cuánto falta para la próxima corrida diaria a la hora de Lima dada (siempre positivo: si ya pasó, la de mañana).</summary>
    public static TimeSpan EsperaHasta(TimeOnly horaEnLima, DateTimeOffset ahora)
    {
        var lima = ahora.ToOffset(DesfaseDeLima);
        var hoy = new DateTimeOffset(DateOnly.FromDateTime(lima.DateTime).ToDateTime(horaEnLima), DesfaseDeLima);
        return (hoy > lima ? hoy : hoy.AddDays(1)) - lima;
    }

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

    /// <summary>Corre hasta que se cancele: una corrida al empezar y otra en cada intervalo, o, si tiene hora en Lima, solo a esa hora de cada día.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        try
        {
            if (opciones.HoraEnLima is { } hora)
            {
                while (true)
                {
                    await Task.Delay(EsperaHasta(hora, reloj.Ahora), tiempo ?? TimeProvider.System, cancelacion).ConfigureAwait(false);
                    await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
                }
            }

            using var temporizador = new PeriodicTimer(opciones.Intervalo, tiempo ?? TimeProvider.System);
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
            // Una falla de la red o de la base de datos con el apagado ya pedido (la conexión se cierra): no se propaga ni detiene el anfitrión.
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

/// <summary>Los trabajos de fondo de Devoluciones con sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, sus horarios y sus corridas.</summary>
public static class TrabajosDeDevoluciones
{
    /// <summary>Rechaza por el plazo de recepción, alerta de reingresos estancados y vigila la depuración (DEV-14, DEV-16): cada hora.</summary>
    public const string RevisionDePlazos = "devoluciones-revision-de-plazos";

    /// <summary>Concilia los reembolsos que quedaron pendientes (DEV-08, DEV-17, DEV-21): cada 5 minutos.</summary>
    public const string ConciliacionDeReembolsos = "devoluciones-conciliacion-de-reembolsos";

    /// <summary>Redacta lo que cumplió dos años (DEV-27, DEV-30): una vez al día, a las 03:45 de Lima.</summary>
    public const string Depuracion = "devoluciones-depuracion";

    /// <summary>Cuántas devoluciones procesa una corrida de plazos o de conciliación: el resto queda para la siguiente.</summary>
    public const int MaximoPorCorrida = 100;

    public static readonly OpcionesDelTrabajoDeDevoluciones CadaCincoMinutos = new(TimeSpan.FromMinutes(5), PlazoMaximoDeLaCorrida: TimeSpan.FromMinutes(4));

    public static readonly OpcionesDelTrabajoDeDevoluciones PorHora = new(TimeSpan.FromHours(1));

    public static readonly OpcionesDelTrabajoDeDevoluciones DiariaALas0345DeLima = new(TimeSpan.FromDays(1), new TimeOnly(3, 45), FallosSeguidosParaAlertar: 1, PlazoMaximoDeLaCorrida: TimeSpan.FromMinutes(30));

    /// <summary>Los tres trabajos; cada corrida abre su propio alcance de servicios (los casos de uso dependen del actor, de la unidad de trabajo y de los repositorios, que son por alcance).</summary>
    public static IReadOnlyList<TrabajoDeFondoDeDevoluciones> Todos(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeDevoluciones alcance, IPublicadorDeAlertasTecnicas alertas, IReloj reloj, TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDeDevoluciones Crear(string nombre, OpcionesDelTrabajoDeDevoluciones opciones, Func<IServiceProvider, CancellationToken, Task> ejecutar) =>
            new(
                nombre,
                async ct =>
                {
                    using var ambito = fabrica.CreateScope();
                    await ejecutar(ambito.ServiceProvider, ct).ConfigureAwait(false);
                },
                registro,
                alcance,
                alertas,
                reloj,
                opciones,
                tiempo);

        return
        [
            Crear(
                RevisionDePlazos,
                PorHora,
                async (p, ct) =>
                {
                    // Las tres revisiones corren siempre: una que falla no impide a las otras; la corrida falla si cualquiera lo hizo.
                    var fallo = false;
                    fallo |= !(await p.GetRequiredService<RechazarDevolucionesVencidas>().Ejecutar(MaximoPorCorrida, ct).ConfigureAwait(false)).EsExito;
                    fallo |= !(await p.GetRequiredService<RevisarDevolucionesEstancadas>().Ejecutar(ct).ConfigureAwait(false)).EsExito;
                    fallo |= !(await p.GetRequiredService<RevisarDepuracionDeDevoluciones>().Ejecutar(ct).ConfigureAwait(false)).EsExito;
                    ExigirExito(!fallo);
                }),
            Crear(ConciliacionDeReembolsos, CadaCincoMinutos, async (p, ct) => ExigirExito((await p.GetRequiredService<ConciliarReembolsosDeDevoluciones>().Ejecutar(MaximoPorCorrida, ct).ConfigureAwait(false)).EsExito)),
            Crear(Depuracion, DiariaALas0345DeLima, async (p, ct) => ExigirExito((await p.GetRequiredService<RedactarDevolucionesVencidas>().Ejecutar(RedactarDevolucionesVencidas.MaximoPorCorrida, ct).ConfigureAwait(false)).EsExito)),
        ];
    }

    /// <summary>Un caso de uso que rechaza (por ejemplo, porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso: falla cerrada.</summary>
    private static void ExigirExito(bool exito)
    {
        if (!exito)
        {
            throw new InvalidOperationException("El trabajo de fondo de Devoluciones fue rechazado.");
        }
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Devoluciones en paralelo hasta el apagado (lo hospeda la Api).</summary>
public sealed class ServicioDeTrabajosDeDevoluciones(IEnumerable<TrabajoDeFondoDeDevoluciones> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
