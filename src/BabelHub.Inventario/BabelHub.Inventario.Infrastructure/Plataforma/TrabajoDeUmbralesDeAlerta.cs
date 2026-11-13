using System.Collections.Concurrent;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Estado de los umbrales de alerta en memoria del proceso.</summary>
/// <remarks>
/// Tras un reinicio un umbral que sigue superado alerta una vez más; <c>Auditoria</c> la agrupa dentro de su ventana de 15
/// minutos. Persistirlo es parte de la migración del esquema <c>plataforma</c> (plan de <c>transversal-plataforma</c>).
/// </remarks>
public sealed class EstadoDeUmbralesEnMemoria : IEstadoDeUmbrales
{
    private readonly ConcurrentDictionary<string, EstadoDeUmbral> _estados = new(StringComparer.Ordinal);

    public Task<EstadoDeUmbral?> Obtener(string motivo, CancellationToken cancelacion) =>
        Task.FromResult(_estados.TryGetValue(motivo, out var estado) ? estado : null);

    public Task Guardar(string motivo, EstadoDeUmbral estado, CancellationToken cancelacion)
    {
        _estados[motivo] = estado;
        return Task.CompletedTask;
    }
}

public sealed record OpcionesDelTrabajoDeUmbrales
{
    /// <summary>Cada cuánto se evalúan los umbrales (SEG-09: cada minuto).</summary>
    public TimeSpan Intervalo { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Con tantas corridas fallidas seguidas se publica una alerta técnica: si la evaluación se detiene, las alertas de seguridad dejan de salir.</summary>
    public int FallosSeguidosParaAlertar { get; init; } = 5;
}

/// <summary>
/// Trabajo de fondo que evalúa los umbrales de alerta de seguridad (SEG-09) cada minuto, como actor «sistema», y registra el
/// resultado de cada corrida. Una corrida fallida no detiene el trabajo: queda registrada y, si se repite, avisa por una alerta
/// técnica. Lo ejecuta un servicio hospedado de la Api (T23) con <see cref="Correr"/>.
/// </summary>
public sealed class TrabajoDeUmbralesDeAlerta(
    EvaluarUmbralesDeAlerta evaluar,
    IRegistroDeTrabajosDeFondo registro,
    IPublicadorDeAlertasTecnicas alertas,
    IEstablecedorDeActor actor,
    IReloj reloj,
    OpcionesDelTrabajoDeUmbrales? opciones = null,
    TimeProvider? tiempo = null)
{
    public const string Nombre = "evaluar-umbrales-de-alerta";
    public const string MotivoDeAlerta = "trabajo-de-fondo-detenido";

    private readonly OpcionesDelTrabajoDeUmbrales _opciones = opciones ?? new OpcionesDelTrabajoDeUmbrales();
    private int _fallosSeguidos;

    /// <summary>Evalúa los umbrales ahora y registra la corrida. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        actor.Establecer(ActorActual.DelSistema());
        try
        {
            await evaluar.Ejecutar(cancelacion).ConfigureAwait(false);
            _fallosSeguidos = 0;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            if (_fallosSeguidos == _opciones.FallosSeguidosParaAlertar)
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
        using var temporizador = new PeriodicTimer(_opciones.Intervalo, tiempo ?? TimeProvider.System);
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
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlerta, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}
