namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>Plazos de espera a las dependencias: 10 segundos y, para el modelo de lenguaje, 30 (SEG-14).</summary>
public sealed record OpcionesDeDependencias
{
    public static readonly TimeSpan EsperaNormalPorDefecto = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan EsperaDelLlmPorDefecto = TimeSpan.FromSeconds(30);

    public OpcionesDeDependencias(TimeSpan? esperaNormal = null, TimeSpan? esperaDelLlm = null)
    {
        EsperaNormal = Positivo(esperaNormal ?? EsperaNormalPorDefecto, nameof(esperaNormal));
        EsperaDelLlm = Positivo(esperaDelLlm ?? EsperaDelLlmPorDefecto, nameof(esperaDelLlm));
    }

    public TimeSpan EsperaNormal { get; }

    public TimeSpan EsperaDelLlm { get; }

    public TimeSpan Espera(ClaseDeDependencia clase) =>
        clase == ClaseDeDependencia.ProveedorDeLlm ? EsperaDelLlm : EsperaNormal;

    private static TimeSpan Positivo(TimeSpan valor, string nombre) =>
        valor > TimeSpan.Zero ? valor : throw new ArgumentOutOfRangeException(nombre, "El plazo debe ser positivo.");
}

/// <summary>Implementa <see cref="IDependenciasExternas"/>: corta la espera al vencer el plazo aunque la llamada ignore la cancelación.</summary>
public sealed class DependenciasExternas(OpcionesDeDependencias opciones) : IDependenciasExternas
{
    public async Task<RespuestaExterna<T>> Consultar<T>(
        string dependencia,
        ClaseDeDependencia clase,
        Func<CancellationToken, Task<T>> llamada,
        IValidadorDeRespuestaExterna<T> validador,
        CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencia);
        ArgumentNullException.ThrowIfNull(llamada);
        ArgumentNullException.ThrowIfNull(validador);

        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        Task<T>? tarea = null;
        try
        {
            tarea = llamada(plazo.Token);
            var respuesta = await tarea.WaitAsync(opciones.Espera(clase), cancelacion).ConfigureAwait(false);

            return validador.Validar(respuesta).EsValido
                ? RespuestaExterna<T>.ConRespuesta(respuesta)
                : RespuestaExterna<T>.SinRespuesta(MotivoDeSinRespuesta.RespuestaInvalida);
        }
        catch (TimeoutException)
        {
            await plazo.CancelAsync().ConfigureAwait(false);
            Observar(tarea);
            return RespuestaExterna<T>.SinRespuesta(MotivoDeSinRespuesta.TiempoAgotado);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Cualquier otro fallo de la dependencia (red, error del servicio, cancelación interna) es «no respondió».
            return RespuestaExterna<T>.SinRespuesta(MotivoDeSinRespuesta.Fallo);
        }
    }

    /// <summary>Evita que una llamada abandonada por el plazo deje una excepción sin observar.</summary>
    private static void Observar(Task? tarea) =>
        _ = tarea?.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
}
