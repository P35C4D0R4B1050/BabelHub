namespace BabelHub.Ecommerce.Domain.Devoluciones;

/// <summary>Una devolución es de un solo tipo (DEV-01): impresos o digitales; una mezcla se rechaza y se piden dos devoluciones.</summary>
public enum TipoDeDevolucion
{
    Impreso,
    Digital,
}

/// <summary>De dónde nace: una solicitud del cliente (impreso o reembolso digital) o el reporte de un archivo defectuoso (DEV-06), que solo decide la administración central.</summary>
public enum OrigenDeDevolucion
{
    Solicitud,
    Reporte,
}

/// <summary>Los estados de una línea (DEV-10). <c>Recibida</c> solo existe en impresos; <c>Rechazada</c> y <c>Reembolsada</c> son finales.</summary>
public enum EstadoDeLineaDeDevolucion
{
    Solicitada,
    Aprobada,
    Rechazada,
    Recibida,
    Reembolsada,
}

public static class EstadosDeLineaDeDevolucion
{
    public static string Codigo(this EstadoDeLineaDeDevolucion estado) => estado switch
    {
        EstadoDeLineaDeDevolucion.Solicitada => "solicitada",
        EstadoDeLineaDeDevolucion.Aprobada => "aprobada",
        EstadoDeLineaDeDevolucion.Rechazada => "rechazada",
        EstadoDeLineaDeDevolucion.Recibida => "recibida",
        EstadoDeLineaDeDevolucion.Reembolsada => "reembolsada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static EstadoDeLineaDeDevolucion Desde(string codigo) => codigo switch
    {
        "solicitada" => EstadoDeLineaDeDevolucion.Solicitada,
        "aprobada" => EstadoDeLineaDeDevolucion.Aprobada,
        "rechazada" => EstadoDeLineaDeDevolucion.Rechazada,
        "recibida" => EstadoDeLineaDeDevolucion.Recibida,
        "reembolsada" => EstadoDeLineaDeDevolucion.Reembolsada,
        _ => throw new ArgumentException("Estado de línea de devolución desconocido.", nameof(codigo)),
    };

    /// <summary>Un estado del que la línea no sale: ya no cuenta como pendiente y, a los dos años, deja redactar la devolución (DEV-27).</summary>
    public static bool EsFinal(this EstadoDeLineaDeDevolucion estado) => estado is EstadoDeLineaDeDevolucion.Rechazada or EstadoDeLineaDeDevolucion.Reembolsada;

    /// <summary>Cuenta para el tope de unidades de DEV-03: una línea rechazada devuelve su cantidad (DEV-18).</summary>
    public static bool ComprometeCupo(this EstadoDeLineaDeDevolucion estado) => estado != EstadoDeLineaDeDevolucion.Rechazada;
}

/// <summary>El valor cero es el sistema: un <c>default</c> nunca se hace pasar por un cliente.</summary>
public enum TipoDeResponsable
{
    Sistema,
    Cliente,
    Personal,
}

/// <summary>
/// Quién hizo un cambio (DEV-10): el cliente, una persona del personal o el sistema. El identificador <b>se quita</b> si es el cliente al cancelar su cuenta y a los dos años (DEV-24, DEV-27); el del personal se conserva y el del sistema nunca existe.
/// </summary>
public readonly record struct ResponsableDelCambio
{
    private ResponsableDelCambio(TipoDeResponsable tipo, Guid? id)
    {
        Tipo = tipo;
        Id = id;
    }

    public TipoDeResponsable Tipo { get; }

    public Guid? Id { get; }

    /// <summary>Solo el tipo: el identificador del cliente (la cuenta opaca) no sale en un texto ni en un registro.</summary>
    public override string ToString() => Tipo.ToString();

    public static ResponsableDelCambio Cliente(Guid id) => id == Guid.Empty ? throw new ArgumentException("El cliente necesita un identificador.", nameof(id)) : new(TipoDeResponsable.Cliente, id);

    public static ResponsableDelCambio Personal(Guid id) => id == Guid.Empty ? throw new ArgumentException("El personal necesita un identificador.", nameof(id)) : new(TipoDeResponsable.Personal, id);

    public static ResponsableDelCambio Sistema { get; } = new(TipoDeResponsable.Sistema, null);

    /// <summary>Lo que lee el repositorio: un cliente ya redactado no tiene identificador; el del personal sí y el del sistema nunca.</summary>
    public static ResponsableDelCambio Rehidratar(TipoDeResponsable tipo, Guid? id) => tipo switch
    {
        TipoDeResponsable.Sistema when id is null => Sistema,
        TipoDeResponsable.Sistema => throw new ArgumentException("El sistema no tiene identificador.", nameof(id)),
        TipoDeResponsable.Personal when id is null || id == Guid.Empty => throw new ArgumentException("El personal necesita un identificador.", nameof(id)),
        TipoDeResponsable.Cliente or TipoDeResponsable.Personal => new(tipo, id),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    /// <summary>La redacción (DEV-24, DEV-27): quita el identificador solo si es el cliente.</summary>
    public ResponsableDelCambio SinIdentidadDelCliente() => Tipo == TipoDeResponsable.Cliente ? new(TipoDeResponsable.Cliente, null) : this;
}

/// <summary>Un cambio de estado de una línea con su fecha y su responsable (DEV-10); la línea nace con su primer cambio, «solicitada».</summary>
public sealed record CambioDeEstado(EstadoDeLineaDeDevolucion Estado, DateTimeOffset Instante, ResponsableDelCambio Responsable);

/// <summary>La respuesta de una operación del agregado: se aplicó o por qué no (el caso de uso la traduce a su error).</summary>
public enum ResultadoDeTransicionDeDevolucion
{
    Aplicada,

    /// <summary>Alguna línea no está en el estado que la operación exige (repetida, ya decidida o de otro flujo): sin efectos (DEV-17).</summary>
    EstadoInvalido,

    /// <summary>La operación no corresponde al tipo de devolución (por ejemplo, recibir un digital).</summary>
    TipoInvalido,

    /// <summary>El plazo aún no venció: la revisión no rechaza nada.</summary>
    NoVencida,

    /// <summary>El reingreso ya se inició (el marcador de DEV-16): el plazo de recepción no puede rechazar la devolución.</summary>
    ReingresoIniciado,

    /// <summary>Falta el reingreso confirmado: una impresa no pasa a recibida sin que el Inventario lo logre (DEV-07).</summary>
    ReingresoSinConfirmar,
}

/// <summary>El resultado de fijar el marcador de reingreso al empezar la recepción (D07).</summary>
public enum ResultadoDeInicioDeReingreso
{
    /// <summary>El marcador quedó fijado ahora.</summary>
    Iniciado,

    /// <summary>Ya estaba fijado por una confirmación anterior: se reintenta el reingreso, que es idempotente en el Inventario.</summary>
    YaIniciado,

    /// <summary>Alguna línea no está aprobada, o el plazo de recepción ya la rechazó: estado inválido.</summary>
    EstadoInvalido,

    TipoInvalido,
}
