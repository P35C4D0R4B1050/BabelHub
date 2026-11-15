using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>Cuándo corre un trabajo de fondo del módulo y cuántos fallos seguidos hacen falta para avisar.</summary>
public sealed record OpcionesDelTrabajoDeAuditoria(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3);

/// <summary>
/// Un trabajo de fondo del módulo <c>Auditoria</c> (T27): corre su corrida cada intervalo <b>como «sistema» fijado explícitamente</b> (un trabajo de fondo sin actor es un defecto, no un permiso: sin ese contexto el alcance
/// de datos del Inventario devuelve cero filas sin error), registra el resultado enumerado de cada corrida en <c>plataforma.trabajos_de_fondo</c> y, tras varios fallos seguidos, publica una alerta técnica
/// (<c>trabajo-de-fondo-detenido</c>). Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// </summary>
public sealed class TrabajoDeFondoDeAuditoria(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    IEstablecedorDeActor actor,
    IAlertas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDeAuditoria opciones,
    TimeProvider? tiempo = null)
{
    private int _fallosSeguidos;

    public string Nombre { get; } = nombre;

    /// <summary>Ejecuta la corrida ahora y la registra. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        actor.Establecer(ActorActual.DelSistema());
        try
        {
            await corrida(cancelacion).ConfigureAwait(false);
            _fallosSeguidos = 0;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            // Al llegar al umbral y después cada otro tanto (un trabajo que sigue caído vuelve a avisar; la agrupación de 15 minutos y el tope diario del correo evitan el ruido).
            if (_fallosSeguidos >= opciones.FallosSeguidosParaAlertar && (_fallosSeguidos - opciones.FallosSeguidosParaAlertar) % opciones.FallosSeguidosParaAlertar == 0)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }
        }
        finally
        {
            actor.Limpiar();
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
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlerta.TrabajoDeFondoDetenido), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>Los trabajos de fondo del módulo, con sus nombres estables, intervalos y corridas (T27; AUD-28, AUD-29, AUD-31, AUD-41).</summary>
public static class TrabajosDeAuditoria
{
    public const string CierreDeVentanasDeLimite = "auditoria-cierre-de-ventanas";

    public const string CorreoDeAlertas = "auditoria-correo-de-alertas";

    public const string DepuracionDiaria = "auditoria-depuracion-diaria";

    /// <summary>Cada 30 segundos: el margen de cierre es de 10 segundos y el evento no debe tardar en aparecer.</summary>
    public static readonly OpcionesDelTrabajoDeAuditoria IntervaloDelCierre = new(TimeSpan.FromSeconds(30));

    /// <summary>Cada 5 minutos: menor que la ventana de agrupación de 15 minutos (AUD-38).</summary>
    public static readonly OpcionesDelTrabajoDeAuditoria IntervaloDelCorreo = new(TimeSpan.FromMinutes(5));

    /// <summary>Cada 10 minutos se decide si toca: la depuración corre una vez cada 24 horas desde la última exitosa y reintenta con espera creciente si falla.</summary>
    public static readonly OpcionesDelTrabajoDeAuditoria IntervaloDeLaDepuracion = new(TimeSpan.FromMinutes(10));

    public static readonly TimeSpan PeriodoDeLaDepuracion = TimeSpan.FromHours(24);

    public static TrabajoDeFondoDeAuditoria Cierre(
        CerrarVentanasDeLimite cierre,
        IRegistroDeTrabajosDeFondo registro,
        IEstablecedorDeActor actor,
        IAlertas alertas,
        IReloj reloj,
        TimeProvider? tiempo = null) =>
        new(CierreDeVentanasDeLimite, async ct => await cierre.Ejecutar(ct).ConfigureAwait(false), registro, actor, alertas, reloj, IntervaloDelCierre, tiempo);

    /// <summary>La depuración diaria: solo corre si pasaron 24 horas desde la última exitosa (o si nunca corrió); lo demás (la espera tras un fallo, el bloqueo, la alerta de las 24 horas) lo resuelve <see cref="DepurarAuditoria"/>.</summary>
    public static TrabajoDeFondoDeAuditoria Depuracion(
        DepurarAuditoria depurar,
        IEstadoDeLaDepuracion estado,
        IRegistroDeTrabajosDeFondo registro,
        IEstablecedorDeActor actor,
        IAlertas alertas,
        IReloj reloj,
        TimeProvider? tiempo = null) =>
        new(
            DepuracionDiaria,
            async ct =>
            {
                var actual = await estado.Obtener(ct).ConfigureAwait(false);
                if (actual.UltimaExitosa is { } ultima && actual.IntentosFallidos == 0 && reloj.Ahora - ultima < PeriodoDeLaDepuracion)
                {
                    return;
                }

                var resultado = await depurar.Ejecutar(ct).ConfigureAwait(false);
                if (resultado.Estado == EstadoDeLaCorridaDeDepuracion.Fallida)
                {
                    // El fallo ya quedó en el estado (espera creciente y alerta a las 24 horas): la corrida del trabajo se registra como fallo.
                    throw new InvalidOperationException("La depuración diaria no se completó.");
                }
            },
            registro,
            actor,
            alertas,
            reloj,
            IntervaloDeLaDepuracion,
            tiempo);
}

/// <summary>El servicio en segundo plano que corre los trabajos del módulo en paralelo hasta el apagado (T27).</summary>
public sealed class ServicioDeTrabajosDeAuditoria(IEnumerable<TrabajoDeFondoDeAuditoria> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
