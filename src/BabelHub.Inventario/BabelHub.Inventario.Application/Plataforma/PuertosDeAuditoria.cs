namespace BabelHub.Inventario.Application.Plataforma;

public enum TipoDeEventoDeSeguridad
{
    /// <summary>Un límite de uso se excedió (SEG-08); <c>Auditoria</c> los agrupa por ventana de 5 minutos (AUD-28).</summary>
    LimiteExcedido,

    /// <summary>La solicitud llegó sin un origen determinable (SEG-23).</summary>
    OrigenIndeterminable,

    LlamadaEntreServiciosSinCredencial,

    VerificacionDePersonaFallida,

    /// <summary>Una dirección reenviada llegó sin la credencial de alcance «origen reenviado» (SEG-32, AUD-44).</summary>
    OrigenReenviadoSinCredencial,

    /// <summary>Una llamada entrante de una integración no se pudo verificar y se rechazó (SEG-12, AUD-37).</summary>
    EntradaDeIntegracionRechazada,
}

/// <summary>
/// Evento de seguridad que publica la plataforma. La dirección completa solo existe aquí y en el acumulado pendiente de
/// <c>Auditoria</c> (SEG-25): nunca en los contadores.
/// </summary>
public sealed record EventoDeSeguridad
{
    public EventoDeSeguridad(
        TipoDeEventoDeSeguridad tipo,
        DateTimeOffset instante,
        string? limite = null,
        HuellaDeOrigen? origen = null,
        string? direccionCompleta = null,
        string? usuario = null,
        string? proveedor = null,
        string? motivo = null)
    {
        if (tipo == TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada && string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El rechazo de una integración indica el motivo.", nameof(motivo));
        }

        if (tipo == TipoDeEventoDeSeguridad.LimiteExcedido)
        {
            if (string.IsNullOrWhiteSpace(limite))
            {
                throw new ArgumentException("Un límite excedido indica la política.", nameof(limite));
            }

            if (origen is null && string.IsNullOrWhiteSpace(usuario))
            {
                throw new ArgumentException("Un límite excedido indica el origen o el usuario.", nameof(origen));
            }
        }

        if (tipo == TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial && direccionCompleta is not null)
        {
            throw new ArgumentException("Este evento no conserva la dirección reenviada (AUD-44).", nameof(direccionCompleta));
        }

        Tipo = tipo;
        Instante = instante;
        Limite = limite;
        Origen = origen;
        DireccionCompleta = direccionCompleta;
        Usuario = usuario;
        Proveedor = proveedor;
        Motivo = motivo;
    }

    public TipoDeEventoDeSeguridad Tipo { get; }

    public DateTimeOffset Instante { get; }

    public string? Limite { get; }

    public HuellaDeOrigen? Origen { get; }

    public string? DireccionCompleta { get; }

    public string? Usuario { get; }

    /// <summary>Proveedor de la integración, solo si está en la lista blanca (nunca un nombre enviado por el cliente).</summary>
    public string? Proveedor { get; }

    /// <summary>Motivo estable del rechazo (por ejemplo «firma-invalida»).</summary>
    public string? Motivo { get; }
}

/// <summary>Alerta técnica (un trabajo de fondo que falla, una dependencia caída); el motivo viene de un catálogo cerrado.</summary>
public sealed record AlertaTecnica(string Motivo, DateTimeOffset Instante)
{
    public string Motivo { get; } = string.IsNullOrWhiteSpace(Motivo) ? throw new ArgumentException("El motivo no puede estar vacío.", nameof(Motivo)) : Motivo;
}

/// <summary>Alerta de seguridad al superarse un umbral (SEG-09); no lleva datos personales.</summary>
public sealed record AlertaDeSeguridad
{
    public AlertaDeSeguridad(string motivo, DateTimeOffset instante, int total)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo no puede estar vacío.", nameof(motivo));
        }

        if (total <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "El total que superó el umbral es positivo.");
        }

        Motivo = motivo;
        Instante = instante;
        Total = total;
    }

    public string Motivo { get; }

    public DateTimeOffset Instante { get; }

    public int Total { get; }
}

/// <summary>Puerto que implementa <c>Auditoria</c>: publica los eventos de seguridad de la plataforma.</summary>
public interface IPublicadorDeEventosDeSeguridad
{
    Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default);
}

/// <summary>Puerto que implementa <c>Auditoria</c>: publica alertas técnicas.</summary>
public interface IPublicadorDeAlertasTecnicas
{
    Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default);
}

/// <summary>Puerto que implementa <c>Auditoria</c>: guarda, agrupa y envía por correo las alertas de seguridad (SEG-09).</summary>
public interface IPublicadorDeAlertasDeSeguridad
{
    Task Publicar(AlertaDeSeguridad alerta, CancellationToken cancelacion = default);
}

/// <summary>Puerto que implementa <c>Auditoria</c>: cuenta los eventos de un motivo en una ventana (para evaluar umbrales).</summary>
public interface IContadorDeEventosDeSeguridad
{
    Task<int> Contar(string motivo, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default);
}
