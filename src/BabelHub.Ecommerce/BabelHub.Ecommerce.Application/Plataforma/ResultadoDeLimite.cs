namespace BabelHub.Ecommerce.Application.Plataforma;

public enum EstadoDeLimite
{
    /// <summary>La solicitud cuenta y puede continuar.</summary>
    Permitido,

    /// <summary>Límite excedido: se responde con la respuesta uniforme y el tiempo de espera.</summary>
    Excedido,

    /// <summary>Repetición de una clave de idempotencia ya completada: se devuelve el resultado guardado y no cuenta.</summary>
    RepeticionCompletada,

    /// <summary>Otra solicitud con la misma clave sigue en curso (409).</summary>
    RepeticionEnCurso,

    /// <summary>La clave se reutilizó con otro cuerpo o ruta (409); cuenta contra el límite general.</summary>
    ClaveEnConflicto,

    /// <summary>El almacén de contadores no responde y la política falla cerrada (SEG-26): se responde servicio no disponible.</summary>
    NoDisponible,
}

/// <summary>Resultado mínimo que se guarda de una operación idempotente: nunca datos personales ni enlaces firmados.</summary>
public sealed record ResultadoMinimo
{
    public ResultadoMinimo(string desenlace, string? idDelRecurso = null)
    {
        if (string.IsNullOrWhiteSpace(desenlace))
        {
            throw new ArgumentException("El desenlace no puede estar vacío.", nameof(desenlace));
        }

        Desenlace = desenlace;
        IdDelRecurso = idDelRecurso;
    }

    public string Desenlace { get; }

    public string? IdDelRecurso { get; }
}

/// <summary>Clave de idempotencia del cliente ligada a su ruta y a la huella del cuerpo (SEG-22).</summary>
public sealed record ClaveDeIdempotencia
{
    public ClaveDeIdempotencia(string claveDelCliente, string ruta, string huellaDelCuerpo)
    {
        ClaveDelCliente = NoVacio(claveDelCliente, nameof(claveDelCliente));
        Ruta = NoVacio(ruta, nameof(ruta));
        HuellaDelCuerpo = NoVacio(huellaDelCuerpo, nameof(huellaDelCuerpo));
    }

    public string ClaveDelCliente { get; }

    public string Ruta { get; }

    public string HuellaDelCuerpo { get; }

    private static string NoVacio(string valor, string nombre) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException("El valor no puede estar vacío.", nombre) : valor;
}

/// <summary>
/// Resultado de reservar un cupo. Es un modelo propio del contrato público: no expone tipos del Dominio. Un rechazo trae el
/// motivo (la clave de la política), la espera sugerida y el instante de nueva disponibilidad; el código estable
/// <c>&lt;SRV&gt;-LIMITE-EXCEDIDO</c> lo asigna la Api.
/// </summary>
public sealed record ResultadoDeLimite
{
    private ResultadoDeLimite(
        EstadoDeLimite estado,
        ReservaDeLimite? reserva = null,
        string? motivo = null,
        int? esperaEnSegundos = null,
        DateTimeOffset? disponibleDesde = null,
        ResultadoMinimo? resultadoGuardado = null)
    {
        Estado = estado;
        Reserva = reserva;
        Motivo = motivo;
        EsperaEnSegundos = esperaEnSegundos;
        DisponibleDesde = disponibleDesde;
        ResultadoGuardado = resultadoGuardado;
    }

    public EstadoDeLimite Estado { get; }

    public bool EsPermitido => Estado == EstadoDeLimite.Permitido;

    /// <summary>El cupo reservado (para <c>Liberar</c>, <c>Completar</c> o <c>Fallar</c>); solo existe si la solicitud puede continuar y se contó (es <see langword="null"/> si <see cref="PermitidoSinContar"/>).</summary>
    public ReservaDeLimite? Reserva { get; }

    public string? Motivo { get; }

    public int? EsperaEnSegundos { get; }

    public DateTimeOffset? DisponibleDesde { get; }

    public ResultadoMinimo? ResultadoGuardado { get; }

    public static ResultadoDeLimite Permitido(ReservaDeLimite reserva)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        return new(EstadoDeLimite.Permitido, reserva);
    }

    /// <summary>La solicitud pasa sin contar: la política es por origen y esta llamada no trae un origen del cliente (SEG-32). No hay reserva que liberar.</summary>
    public static ResultadoDeLimite PermitidoSinContar() => new(EstadoDeLimite.Permitido);

    public static ResultadoDeLimite Excedido(string motivo, int esperaEnSegundos, DateTimeOffset disponibleDesde)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo no puede estar vacío.", nameof(motivo));
        }

        if (esperaEnSegundos < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(esperaEnSegundos), "La espera sugerida es de al menos un segundo.");
        }

        return new(EstadoDeLimite.Excedido, motivo: motivo, esperaEnSegundos: esperaEnSegundos, disponibleDesde: disponibleDesde);
    }

    public static ResultadoDeLimite RepeticionCompletada(ResultadoMinimo resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return new(EstadoDeLimite.RepeticionCompletada, resultadoGuardado: resultado);
    }

    public static ResultadoDeLimite RepeticionEnCurso() => new(EstadoDeLimite.RepeticionEnCurso);

    public static ResultadoDeLimite ClaveEnConflicto() => new(EstadoDeLimite.ClaveEnConflicto);

    public static ResultadoDeLimite NoDisponible() => new(EstadoDeLimite.NoDisponible);
}
