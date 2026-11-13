namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>Por qué no hubo una respuesta utilizable de una dependencia externa.</summary>
public enum MotivoDeSinRespuesta
{
    /// <summary>No respondió dentro del plazo (10 segundos; 30 para el proveedor del modelo de lenguaje, SEG-14).</summary>
    TiempoAgotado,

    /// <summary>La llamada falló (red, error del servicio).</summary>
    Fallo,

    /// <summary>Respondió con un formato inesperado: se descarta como si no hubiera respondido (SEG-13).</summary>
    RespuestaInvalida,
}

/// <summary>Plazo que se espera a una dependencia.</summary>
public enum ClaseDeDependencia
{
    /// <summary>10 segundos.</summary>
    Normal,

    /// <summary>30 segundos: el proveedor del modelo de lenguaje.</summary>
    ProveedorDeLlm,
}

/// <summary>Resultado de consultar una dependencia externa: una respuesta ya validada o la razón por la que no la hay.</summary>
public sealed class RespuestaExterna<T>
{
    private readonly T? _valor;

    private RespuestaExterna(T? valor, MotivoDeSinRespuesta? motivo)
    {
        _valor = valor;
        Motivo = motivo;
    }

    public bool HayRespuesta => Motivo is null;

    public MotivoDeSinRespuesta? Motivo { get; }

    /// <summary>La respuesta validada; pedirla cuando no la hay es un error de programación.</summary>
    public T Valor => HayRespuesta ? _valor! : throw new InvalidOperationException("La dependencia no dio una respuesta utilizable.");

    public static RespuestaExterna<T> ConRespuesta(T valor) => new(valor, null);

    public static RespuestaExterna<T> SinRespuesta(MotivoDeSinRespuesta motivo) => new(default, motivo);

    /// <summary>
    /// Para una dependencia necesaria: sin respuesta, la operación falla con «servicio no disponible» y las demás operaciones
    /// siguen atendiéndose. Una dependencia no necesaria no la usa: revisa <see cref="HayRespuesta"/> y continúa sin ella.
    /// </summary>
    public Resultado<T> ComoNecesaria() =>
        HayRespuesta
            ? Resultado<T>.Exito(_valor!)
            : Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClaveServicioNoDisponible));

    public const string ClaveServicioNoDisponible = "servicio-no-disponible";
}

/// <summary>
/// Consulta de dependencias externas: toda respuesta se trata como no confiable (se valida antes de usarla) y la espera tiene
/// un plazo (SEG-13, SEG-14). Un fallo de la dependencia nunca se propaga como excepción: se informa en el resultado.
/// </summary>
public interface IDependenciasExternas
{
    /// <summary>
    /// Ejecuta <paramref name="llamada"/> con el plazo de <paramref name="clase"/>, valida su respuesta con
    /// <paramref name="validador"/> y devuelve la respuesta o el motivo por el que no la hay. Si el llamador cancela, la
    /// cancelación sí se propaga.
    /// </summary>
    Task<RespuestaExterna<T>> Consultar<T>(
        string dependencia,
        ClaseDeDependencia clase,
        Func<CancellationToken, Task<T>> llamada,
        IValidadorDeRespuestaExterna<T> validador,
        CancellationToken cancelacion = default);
}
