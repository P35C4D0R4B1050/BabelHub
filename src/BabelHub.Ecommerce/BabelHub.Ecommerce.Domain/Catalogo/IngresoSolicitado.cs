namespace BabelHub.Ecommerce.Domain.Catalogo;

public enum EstadoDeIngreso
{
    /// <summary>Se pidió y no se sabe si el Inventario lo aplicó: se reintenta con su misma clave y carga o se concilia.</summary>
    Pendiente,

    Registrado,

    /// <summary>Se sabe que no se aplicó: se puede emitir una clave nueva.</summary>
    Fallido,
}

/// <summary>
/// La constancia local de un ingreso de mercadería pedido al Inventario (CAT-20, CAT-22, D04): nace <c>Pendiente</c> en su propia transacción <b>antes</b> de llamar al Inventario y
/// después pasa a <c>Registrado</c>, a <c>Fallido</c> (se sabe que no se aplicó) o se queda <c>Pendiente</c> (resultado incierto). La clave de idempotencia es el nonce del token firmado.
/// Solo el usuario de la fila puede reintentarla (el ámbito de la clave en el Inventario incluye al usuario). La fuente de verdad del stock es el Inventario: esto solo evita repetir el lote y
/// permite avisar «ingreso pendiente». Un solo <c>Pendiente</c> por edición (lo garantiza un índice único parcial).
/// </summary>
public sealed class IngresoSolicitado
{
    public const int CantidadMinima = 1;

    public const int CantidadMaxima = 5_000;

    public const int LongitudMaximaDeLaReferencia = 80;

    public const int LongitudMaximaDeLaNota = 500;

    /// <summary>Un <c>pendiente</c> de más de 24 horas ya no se reintenta (el Inventario podría haber olvidado la clave): se concilia.</summary>
    public static readonly TimeSpan PlazoDeReintento = TimeSpan.FromHours(24);

    private IngresoSolicitado(
        Guid id,
        Guid edicionId,
        Guid claveDeIdempotencia,
        Guid usuarioId,
        EstadoDeIngreso estado,
        Guid? ingresoId,
        int cantidad,
        string? referencia,
        string? nota,
        DateTimeOffset solicitadoEn,
        int intentos,
        DateTimeOffset ultimoIntentoEn,
        DateTimeOffset? resultadoEn)
    {
        Id = id;
        EdicionId = edicionId;
        ClaveDeIdempotencia = claveDeIdempotencia;
        UsuarioId = usuarioId;
        Estado = estado;
        IngresoId = ingresoId;
        Cantidad = cantidad;
        Referencia = referencia;
        Nota = nota;
        SolicitadoEn = solicitadoEn;
        Intentos = intentos;
        UltimoIntentoEn = ultimoIntentoEn;
        ResultadoEn = resultadoEn;
    }

    public Guid Id { get; }

    public Guid EdicionId { get; }

    /// <summary>El nonce de 128 bits del token firmado: es la <c>Idempotency-Key</c> del ingreso y es única.</summary>
    public Guid ClaveDeIdempotencia { get; }

    /// <summary>Quien lo pidió; <see cref="Guid.Empty"/> cuando se anonimizó a los 2 años (T23).</summary>
    public Guid UsuarioId { get; private set; }

    public EstadoDeIngreso Estado { get; private set; }

    /// <summary>El identificador opaco del ingreso en el Inventario, solo cuando consta como registrado.</summary>
    public Guid? IngresoId { get; private set; }

    public int Cantidad { get; }

    public string? Referencia { get; private set; }

    public string? Nota { get; private set; }

    public DateTimeOffset SolicitadoEn { get; }

    /// <summary>1 en el primer envío y +1 en cada reintento.</summary>
    public int Intentos { get; private set; }

    /// <summary>Nace igual a <see cref="SolicitadoEn"/> y se actualiza en cada reintento: la ventana de la conciliación se mide desde aquí.</summary>
    public DateTimeOffset UltimoIntentoEn { get; private set; }

    public DateTimeOffset? ResultadoEn { get; private set; }

    public bool EstaPendiente => Estado == EstadoDeIngreso.Pendiente;

    /// <summary>La fila <c>Pendiente</c> que se inserta antes de llamar al Inventario, con el primer intento.</summary>
    public static IngresoSolicitado Solicitar(
        Guid id,
        Guid edicionId,
        Guid claveDeIdempotencia,
        Guid usuarioId,
        int cantidad,
        string? referencia,
        string? nota,
        DateTimeOffset ahora)
    {
        if (id == Guid.Empty || edicionId == Guid.Empty || claveDeIdempotencia == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El ingreso necesita su identificador, su edición, su clave y su usuario.");
        }

        if (cantidad is < CantidadMinima or > CantidadMaxima)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad de un ingreso es un entero de 1 a 5 000.");
        }

        if (referencia is { Length: > LongitudMaximaDeLaReferencia } || nota is { Length: > LongitudMaximaDeLaNota })
        {
            throw new ArgumentException("La referencia o la nota superan su largo máximo.");
        }

        return new IngresoSolicitado(
            id,
            edicionId,
            claveDeIdempotencia,
            usuarioId,
            EstadoDeIngreso.Pendiente,
            null,
            cantidad,
            string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim(),
            string.IsNullOrWhiteSpace(nota) ? null : nota.Trim(),
            ahora,
            1,
            ahora,
            null);
    }

    /// <summary>Reconstruye una fila guardada.</summary>
    public static IngresoSolicitado Rehidratar(
        Guid id,
        Guid edicionId,
        Guid claveDeIdempotencia,
        Guid usuarioId,
        EstadoDeIngreso estado,
        Guid? ingresoId,
        int cantidad,
        string? referencia,
        string? nota,
        DateTimeOffset solicitadoEn,
        int intentos,
        DateTimeOffset ultimoIntentoEn,
        DateTimeOffset? resultadoEn) =>
        new(id, edicionId, claveDeIdempotencia, usuarioId, estado, ingresoId, cantidad, referencia, nota, solicitadoEn, intentos, ultimoIntentoEn, resultadoEn);

    /// <summary>Solo el usuario que lo pidió lo reintenta; un ingreso anonimizado (<see cref="Guid.Empty"/>) no tiene autor y solo se concilia.</summary>
    public bool EsDelUsuario(Guid usuarioId) => usuarioId != Guid.Empty && UsuarioId == usuarioId;

    /// <summary>Si ya pasó el plazo del reintento (24 horas desde que se pidió): entonces solo se concilia.</summary>
    public bool ReintentoVencido(DateTimeOffset ahora) => ahora - SolicitadoEn > PlazoDeReintento;

    /// <summary>Un reintento: aumenta los intentos y adelanta el último intento (la conciliación mide su ventana desde aquí). Solo de un <c>Pendiente</c>.</summary>
    public void RegistrarReintento(DateTimeOffset ahora)
    {
        ExigirPendiente();
        Intentos++;
        UltimoIntentoEn = ahora;
    }

    public void MarcarRegistrado(Guid ingresoId, DateTimeOffset ahora)
    {
        ExigirPendiente();
        if (ingresoId == Guid.Empty)
        {
            throw new ArgumentException("El ingreso registrado necesita su identificador.", nameof(ingresoId));
        }

        Estado = EstadoDeIngreso.Registrado;
        IngresoId = ingresoId;
        ResultadoEn = ahora;
    }

    public void MarcarFallido(DateTimeOffset ahora)
    {
        ExigirPendiente();
        Estado = EstadoDeIngreso.Fallido;
        ResultadoEn = ahora;
    }

    /// <summary>A los 2 años (T23): el usuario pasa al valor reservado y se vacían la referencia y la nota. El identificador del ingreso es opaco y no se anonimiza.</summary>
    public void Anonimizar()
    {
        UsuarioId = Guid.Empty;
        Referencia = null;
        Nota = null;
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoDeIngreso.Pendiente)
        {
            throw new InvalidOperationException("Solo un ingreso pendiente cambia de estado.");
        }
    }
}
