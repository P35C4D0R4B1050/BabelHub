namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Un campo de la entrada que no cumple su regla: el nombre del campo y una clave simbólica (nunca el valor recibido).</summary>
public sealed record ErrorDeCampo(string Campo, string Clave);

/// <summary>Resultado de validar una entrada o una respuesta externa.</summary>
public sealed record ResultadoDeValidacion
{
    private ResultadoDeValidacion(IReadOnlyList<ErrorDeCampo> errores) => Errores = errores;

    public IReadOnlyList<ErrorDeCampo> Errores { get; }

    public bool EsValido => Errores.Count == 0;

    public static ResultadoDeValidacion Valido { get; } = new([]);

    public static ResultadoDeValidacion Invalido(IEnumerable<ErrorDeCampo> errores)
    {
        ArgumentNullException.ThrowIfNull(errores);
        var lista = errores.ToList();
        return lista.Count == 0
            ? throw new ArgumentException("Una validación inválida trae al menos un error.", nameof(errores))
            : new ResultadoDeValidacion(lista);
    }
}

/// <summary>Valida el tipo, el largo, el formato y el rango de la entrada de un caso de uso (SEG-01).</summary>
public interface IValidador<in TEntrada>
{
    ResultadoDeValidacion Validar(TEntrada entrada);
}

/// <summary>Valida la forma de la respuesta de un servicio externo antes de usarla (SEG-13).</summary>
public interface IValidadorDeRespuestaExterna<in TRespuesta>
{
    ResultadoDeValidacion Validar(TRespuesta respuesta);
}

/// <summary>Verifica la firma de una entrada de integración (webhook) de un proveedor, en tiempo constante (SEG-12).</summary>
public interface IVerificadorDeFirma
{
    bool Verificar(string proveedor, string marcaDeTiempo, ReadOnlyMemory<byte> cuerpo, string firma);
}

public enum EstadoDeEntrada
{
    /// <summary>Primera vez: quien la reclamó la procesa.</summary>
    Nueva,

    /// <summary>Otro proceso la está procesando.</summary>
    EnCurso,

    /// <summary>Ya se procesó: se devuelve el mismo resultado.</summary>
    Procesada,
}

public sealed record ReclamoDeEntrada(EstadoDeEntrada Estado, ResultadoMinimo? Resultado = null);

/// <summary>Registro de entradas de integración ya vistas, para que una repetición no ejecute el efecto dos veces (SEG-12).</summary>
public interface IAlmacenDeEntradasProcesadas
{
    Task<ReclamoDeEntrada> Reclamar(string proveedor, string idDeEntrada, CancellationToken cancelacion = default);

    Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default);
}

public enum ResultadoDeVerificacionDePersona
{
    Verificada,
    Fallida,
    NoDisponible,
}

/// <summary>Verifica en el servidor que quien actúa es una persona (Cloudflare Turnstile, SEG-31).</summary>
public interface IVerificadorDePersona
{
    Task<ResultadoDeVerificacionDePersona> Verificar(string token, string accion, CancellationToken cancelacion = default);
}
