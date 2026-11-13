namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Fallo esperado de un caso de uso: una categoría (que la Api traduce al código estable <c>&lt;SRV&gt;-…</c>), una clave
/// simbólica del motivo y, cuando aplica, los campos inválidos. Nunca lleva trazas ni datos internos (SEG-03).
/// </summary>
public sealed record ErrorDeAplicacion
{
    public ErrorDeAplicacion(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            throw new ArgumentException("El error necesita una clave simbólica.", nameof(clave));
        }

        Categoria = categoria;
        Clave = clave;
        Errores = errores ?? [];
    }

    public CategoriaDeError Categoria { get; }

    public string Clave { get; }

    public IReadOnlyList<ErrorDeCampo> Errores { get; }
}

/// <summary>Resultado explícito de un caso de uso: éxito con un valor o un <see cref="ErrorDeAplicacion"/> (sin excepciones para flujos esperados).</summary>
public sealed class Resultado<T>
{
    private readonly T? _valor;

    private Resultado(T? valor, ErrorDeAplicacion? error)
    {
        _valor = valor;
        Error = error;
    }

    public bool EsExito => Error is null;

    public ErrorDeAplicacion? Error { get; }

    /// <summary>El valor del éxito; pedirlo de un fallo es un error de programación.</summary>
    public T Valor => EsExito ? _valor! : throw new InvalidOperationException("El resultado es un fallo y no tiene valor.");

    public static Resultado<T> Exito(T valor) => new(valor, null);

    public static Resultado<T> Fallo(ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Resultado<T>(default, error);
    }
}

/// <summary>Un caso de uso de un módulo: recibe su entrada tipada y devuelve un resultado explícito.</summary>
public interface ICasoDeUso<in TEntrada, TSalida>
{
    Task<Resultado<TSalida>> Ejecutar(TEntrada entrada, CancellationToken cancelacion = default);
}
