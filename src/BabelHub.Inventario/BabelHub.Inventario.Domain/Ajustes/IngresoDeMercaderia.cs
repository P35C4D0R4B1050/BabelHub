namespace BabelHub.Inventario.Domain.Ajustes;

/// <summary>Por qué un ingreso de mercadería se rechaza como regla de negocio (AJU-19): la Aplicación lo traduce a su clave estable.</summary>
public enum MotivoDeRechazoDeIngreso
{
    /// <summary>La cantidad no es un entero de 1 a 5 000 (AJU-19, <c>cantidad-fuera-de-rango</c>).</summary>
    CantidadFueraDeRango,

    /// <summary>La referencia no usa letras, dígitos, espacio, <c>-</c>, <c>/</c> y <c>.</c> o pasa de 60 caracteres (AJU-13).</summary>
    ReferenciaInvalida,
}

/// <summary>
/// Las reglas de un ingreso de mercadería (AJU-13, AJU-19): cantidad entera de <b>1 a 5 000</b> —una <b>regla de negocio</b> que decide el Dominio y no el validador de forma (D08)— y referencia opcional con juego de
/// caracteres y largo acotados. El resto (solo administración central y solo la fuente de recepción) es de la Aplicación.
/// </summary>
public static class ReglasDeIngreso
{
    public const int CantidadMinima = 1;

    public const int CantidadMaxima = 5_000;

    /// <summary>El primer motivo de rechazo, o <see langword="null"/> si el ingreso cumple las reglas del Dominio.</summary>
    public static MotivoDeRechazoDeIngreso? Evaluar(int cantidad, string? referencia)
    {
        if (cantidad is < CantidadMinima or > CantidadMaxima)
        {
            return MotivoDeRechazoDeIngreso.CantidadFueraDeRango;
        }

        if (referencia is not null && !LimitesDeTextoDeAjustes.EsReferenciaValida(referencia))
        {
            return MotivoDeRechazoDeIngreso.ReferenciaInvalida;
        }

        return null;
    }
}

/// <summary>
/// Un ingreso de mercadería registrado por la administración central en la fuente de recepción (AJU-13), inmutable: guarda la edición, la cantidad, la referencia opcional del documento de llegada, la nota opcional y el
/// usuario (nulo solo tras la depuración de los 2 años, AJU-21), y el <see cref="AjusteId"/> del ajuste con motivo <c>ingreso-de-mercaderia</c> que se creó en la misma transacción. La referencia es un código de documento
/// y se conserva; la nota se vacía a los 2 años.
/// </summary>
public sealed record IngresoDeMercaderia
{
    private IngresoDeMercaderia(Guid id, Guid fuenteId, Guid edicionId, int cantidad, string? referencia, string? nota, Guid? usuarioId, DateTimeOffset fecha, Guid ajusteId)
    {
        if (id == Guid.Empty || fuenteId == Guid.Empty || edicionId == Guid.Empty || ajusteId == Guid.Empty)
        {
            throw new ArgumentException("Un ingreso necesita su identificador, la fuente, la edición y su ajuste.");
        }

        if (ReglasDeIngreso.Evaluar(cantidad, referencia) is { } rechazo)
        {
            throw new ArgumentException($"El ingreso no cumple las reglas del Dominio ({rechazo}).", nameof(cantidad));
        }

        if (nota is not null && !LimitesDeTextoDeAjustes.EsNotaValida(nota))
        {
            throw new ArgumentException("La nota supera el largo máximo.", nameof(nota));
        }

        Id = id;
        FuenteId = fuenteId;
        EdicionId = edicionId;
        Cantidad = cantidad;
        Referencia = referencia;
        Nota = nota;
        UsuarioId = usuarioId;
        Fecha = fecha;
        AjusteId = ajusteId;
    }

    public Guid Id { get; }

    public Guid FuenteId { get; }

    public Guid EdicionId { get; }

    public int Cantidad { get; }

    public string? Referencia { get; }

    public string? Nota { get; }

    public Guid? UsuarioId { get; }

    public DateTimeOffset Fecha { get; }

    public Guid AjusteId { get; }

    /// <summary>Un ingreso nuevo con el usuario responsable obligatorio.</summary>
    public static IngresoDeMercaderia Nuevo(Guid id, Guid fuenteId, Guid edicionId, int cantidad, string? referencia, string? nota, Guid usuarioId, DateTimeOffset fecha, Guid ajusteId) =>
        usuarioId == Guid.Empty
            ? throw new ArgumentException("Un ingreso nuevo tiene un usuario responsable.", nameof(usuarioId))
            : new IngresoDeMercaderia(id, fuenteId, edicionId, cantidad, referencia, nota, usuarioId, fecha, ajusteId);

    /// <summary>Rehidrata un ingreso guardado (el usuario puede ser nulo si ya se depuró).</summary>
    public static IngresoDeMercaderia Rehidratar(Guid id, Guid fuenteId, Guid edicionId, int cantidad, string? referencia, string? nota, Guid? usuarioId, DateTimeOffset fecha, Guid ajusteId) =>
        new(id, fuenteId, edicionId, cantidad, referencia, nota, usuarioId, fecha, ajusteId);
}
