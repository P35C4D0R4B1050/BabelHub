using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Envios;

/// <summary>
/// Corre una corrida de un trabajo de Envíos como el actor «sistema»: lo aporta el anfitrión (la Api), que es de los pocos lugares que fijan el actor (regla de arquitectura): fija «sistema», ejecuta la corrida con su
/// cancelación y limpia el actor al terminar, aunque falle. Un trabajo de fondo sin actor es un defecto, no un permiso.
/// </summary>
public delegate Task AlcanceDeSistemaDeEnvios(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Cuándo corre un trabajo de fondo de Envíos, cuántos fallos seguidos hacen falta para avisar y el plazo máximo de una corrida.</summary>
public sealed record OpcionesDelTrabajoDeEnvios(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3, TimeSpan? PlazoMaximoDeLaCorrida = null)
{
    /// <summary>Una corrida colgada (red o base de datos) se cancela y cuenta como fallo: sin un plazo nunca llegaría a alertar.</summary>
    public TimeSpan PlazoDeLaCorrida => PlazoMaximoDeLaCorrida ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Un trabajo de fondo de Envíos (T15; ENV-07, ENV-11): corre su corrida cada intervalo <b>como «sistema»</b>, registra el resultado enumerado de cada corrida en <c>plataforma.trabajos_de_fondo</c> y, tras varios fallos
/// seguidos, publica la alerta técnica <c>trabajo-de-fondo-detenido</c> (sin datos personales). <b>Falla cerrado</b>: un caso de uso que rechaza (porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso.
/// Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// </summary>
public sealed class TrabajoDeFondoDeEnvios(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDeEnvios alcanceDeSistema,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDeEnvios opciones,
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

/// <summary>Los trabajos de fondo de Envíos con sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, sus intervalos y sus corridas.</summary>
public static class TrabajosDeEnvios
{
    /// <summary>Reintenta informar a Pedidos el progreso (en reparto, entregado) que no llegó (ENV-07): cada minuto.</summary>
    public const string ReintentoDeProgreso = "envios-reintento-de-progreso";

    /// <summary>Revisa los envíos con despacho incierto o con una guía sin uso por más de 24 horas (ENV-04, ENV-13): cada hora.</summary>
    public const string RevisionDeEstancados = "envios-revision-de-estancados";

    /// <summary>Cuántos envíos procesa una corrida: el resto queda para la siguiente.</summary>
    public const int MaximoPorCorrida = 100;

    public static readonly OpcionesDelTrabajoDeEnvios CadaMinuto = new(TimeSpan.FromMinutes(1), FallosSeguidosParaAlertar: 3, PlazoMaximoDeLaCorrida: TimeSpan.FromMinutes(2));

    public static readonly OpcionesDelTrabajoDeEnvios PorHora = new(TimeSpan.FromHours(1));

    /// <summary>Los dos trabajos; cada corrida abre su propio alcance de servicios (los casos de uso dependen del actor, de la unidad de trabajo y de los repositorios, que son por alcance).</summary>
    public static IReadOnlyList<TrabajoDeFondoDeEnvios> Todos(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeEnvios alcance, IPublicadorDeAlertasTecnicas alertas, IReloj reloj, TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDeEnvios Crear(string nombre, OpcionesDelTrabajoDeEnvios opciones, Func<IServiceProvider, CancellationToken, Task> ejecutar) =>
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
            Crear(ReintentoDeProgreso, CadaMinuto, async (p, ct) => ExigirExito(await p.GetRequiredService<InformarProgresoAPedidos>().Ejecutar(MaximoPorCorrida, ct).ConfigureAwait(false))),
            Crear(RevisionDeEstancados, PorHora, async (p, ct) => ExigirExito(await p.GetRequiredService<RevisarEnviosEstancados>().Ejecutar(MaximoPorCorrida, ct).ConfigureAwait(false))),
        ];
    }

    /// <summary>Un caso de uso que rechaza (por ejemplo, porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso: falla cerrada.</summary>
    private static void ExigirExito<T>(Resultado<T> resultado)
    {
        if (!resultado.EsExito)
        {
            throw new InvalidOperationException("El trabajo de fondo de Envíos fue rechazado.");
        }
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Envíos en paralelo hasta el apagado (lo hospeda la Api).</summary>
public sealed class ServicioDeTrabajosDeEnvios(IEnumerable<TrabajoDeFondoDeEnvios> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
