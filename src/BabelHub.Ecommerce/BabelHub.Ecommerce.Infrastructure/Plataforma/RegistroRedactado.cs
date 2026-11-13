using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>Una excepción ya sin datos prohibidos en su mensaje; conserva el tipo original y la pila, que no llevan valores.</summary>
public sealed class ExcepcionRedactada : Exception
{
    public ExcepcionRedactada(Exception original)
        : base(RedactorDeDatosProhibidos.Redactar(original?.Message))
    {
        TipoOriginal = original?.GetType().FullName ?? string.Empty;
        PilaOriginal = original?.StackTrace;
    }

    public ExcepcionRedactada()
        : base(string.Empty)
    {
        TipoOriginal = string.Empty;
    }

    public ExcepcionRedactada(string message)
        : base(RedactorDeDatosProhibidos.Redactar(message))
    {
        TipoOriginal = string.Empty;
    }

    public ExcepcionRedactada(string message, Exception innerException)
        : base(RedactorDeDatosProhibidos.Redactar(message), innerException)
    {
        TipoOriginal = innerException.GetType().FullName ?? string.Empty;
    }

    public string TipoOriginal { get; }

    public string? PilaOriginal { get; }

    public override string StackTrace => PilaOriginal ?? string.Empty;

    public override string ToString() => $"{TipoOriginal}: {Message}{Environment.NewLine}{PilaOriginal}";
}

/// <summary>
/// Registro que no deja pasar datos prohibidos (SEG-10): antes de entregar cada mensaje al registro real, redacta el texto, los
/// valores de los campos estructurados de nombre sensible y el mensaje de las excepciones, con
/// <see cref="RedactorDeDatosProhibidos"/>. Conserva el nivel, el identificador del evento y el estado habilitado.
/// </summary>
public sealed class RegistroRedactado(ILogger interno) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        interno.BeginScope(RedactarAlcance(state));

    public bool IsEnabled(LogLevel logLevel) => interno.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!interno.IsEnabled(logLevel))
        {
            return;
        }

        var mensaje = RedactorDeDatosProhibidos.Redactar(formatter(state, exception));
        var campos = state is IEnumerable<KeyValuePair<string, object?>> pares
            ? pares.Select(p => new KeyValuePair<string, object?>(
                p.Key,
                RedactorDeDatosProhibidos.EsNombreSensible(p.Key) ? ValorSecreto.Redactado : p.Value is string texto ? RedactorDeDatosProhibidos.Redactar(texto) : p.Value)).ToList()
            : [];

        interno.Log(
            logLevel,
            eventId,
            new EstadoRedactado(mensaje, campos),
            exception is null ? null : new ExcepcionRedactada(exception),
            static (estado, _) => estado.Mensaje);
    }

    private static object RedactarAlcance<TState>(TState state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pares
            ? new EstadoRedactado(
                RedactorDeDatosProhibidos.Redactar(state.ToString()),
                pares.Select(p => new KeyValuePair<string, object?>(p.Key, RedactorDeDatosProhibidos.EsNombreSensible(p.Key) ? ValorSecreto.Redactado : p.Value)).ToList())
            : RedactorDeDatosProhibidos.Redactar(state?.ToString());

    private sealed class EstadoRedactado(string mensaje, IReadOnlyList<KeyValuePair<string, object?>> campos) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public string Mensaje { get; } = mensaje;

        public int Count => campos.Count;

        public KeyValuePair<string, object?> this[int index] => campos[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => campos.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => Mensaje;
    }
}

/// <summary>Fábrica de registros que entrega a cada categoría un <see cref="RegistroRedactado"/> sobre el registro real.</summary>
public sealed class FabricaDeRegistrosRedactada(ILoggerFactory interna) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new RegistroRedactado(interna.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => interna.AddProvider(provider);

    public void Dispose() => interna.Dispose();
}

public static class RedaccionDeRegistros
{
    /// <summary>
    /// Todo <c>ILogger</c> y <c>ILogger&lt;T&gt;</c> que se resuelva pasa por la redacción. Se llama después de <c>AddLogging</c>:
    /// reemplaza la fábrica de registros por una que envuelve a la original.
    /// </summary>
    public static IServiceCollection UsarRedaccionDeRegistros(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddSingleton<ILoggerFactory>(proveedor => new FabricaDeRegistrosRedactada(
            ActivatorUtilities.CreateInstance<LoggerFactory>(proveedor)));
        return servicios;
    }
}
