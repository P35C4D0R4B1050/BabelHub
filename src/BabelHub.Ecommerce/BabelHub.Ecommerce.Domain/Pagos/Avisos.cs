namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>La transición del pago que Pagos avisa a Carrito (D13): lista cerrada, una fila por <c>(intención, intento, transición)</c>.</summary>
public enum TransicionDeAviso
{
    Pendiente,
    Aprobado,
    Rechazado,
    Vencido,
    Compensado,
}

public enum EstadoDeAvisoACarrito
{
    /// <summary>El <c>PagoPendiente</c> con impresos espera a que termine <c>reservas.extender</c> para leer los instantes definitivos (PAG-13): todavía no se envía.</summary>
    Retenido,

    Pendiente,
    Enviado,

    /// <summary>Ya no tiene sentido enviarlo (su intento ya se resolvió o la intención se cerró).</summary>
    Omitido,
}

/// <summary>
/// Un aviso que Pagos debe dar a Carrito <b>después de confirmar</b> la transacción de la transición (D13): se inserta en esa misma transacción y lo envía el trabajo hasta que Carrito lo acepta; nunca es causa de compensación.
/// Su clave es <c>(intención, intento, transición)</c>: insertar dos veces es una sola fila. No lleva datos personales.
/// </summary>
public sealed class AvisoACarrito
{
    private AvisoACarrito(Guid id, Guid intencionId, Guid intentoId, TransicionDeAviso transicion, EstadoDeAvisoACarrito estado, DateTimeOffset ahora)
    {
        Id = id;
        IntencionId = intencionId;
        IntentoId = intentoId;
        Transicion = transicion;
        Estado = estado;
        CreadoEn = ahora;
    }

    public Guid Id { get; }

    public Guid IntencionId { get; }

    public Guid IntentoId { get; }

    public TransicionDeAviso Transicion { get; }

    public EstadoDeAvisoACarrito Estado { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset? EnviadoEn { get; private set; }

    public int Intentos { get; private set; }

    public DateTimeOffset? ProximoIntentoEn { get; private set; }

    /// <summary>La clave natural: no hay dos avisos de la misma transición del mismo intento.</summary>
    public (Guid IntencionId, Guid IntentoId, TransicionDeAviso Transicion) Clave => (IntencionId, IntentoId, Transicion);

    public static AvisoACarrito Crear(Guid intencionId, Guid intentoId, TransicionDeAviso transicion, bool retenido, DateTimeOffset ahora)
    {
        if (intencionId == Guid.Empty || intentoId == Guid.Empty)
        {
            throw new ArgumentException("El aviso necesita su intención y su intento.");
        }

        if (retenido && transicion != TransicionDeAviso.Pendiente)
        {
            throw new ArgumentException("Solo el aviso de pago pendiente se retiene hasta extender la reserva.", nameof(retenido));
        }

        return new AvisoACarrito(Guid.NewGuid(), intencionId, intentoId, transicion, retenido ? EstadoDeAvisoACarrito.Retenido : EstadoDeAvisoACarrito.Pendiente, ahora);
    }


    /// <summary>Ya no tiene sentido enviarlo (su intento ya se resolvió o la intención se cerró): no se envía. Devuelve si cambió.</summary>
    public bool Omitir()
    {
        if (Estado is EstadoDeAvisoACarrito.Enviado or EstadoDeAvisoACarrito.Omitido)
        {
            return false;
        }

        Estado = EstadoDeAvisoACarrito.Omitido;
        return true;
    }

    /// <summary>La espera tras la primera falla del envío; se duplica con cada falla hasta <see cref="EsperaMaxima"/>.</summary>
    public static readonly TimeSpan EsperaBase = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(15);

    /// <summary><c>reservas.extender</c> terminó: el <c>PagoPendiente</c> retenido ya puede enviarse con los instantes definitivos. Devuelve si cambió.</summary>
    public bool Liberar()
    {
        if (Estado != EstadoDeAvisoACarrito.Retenido)
        {
            return false;
        }

        Estado = EstadoDeAvisoACarrito.Pendiente;
        return true;
    }

    /// <summary>Hay que intentar enviarlo ahora: está pendiente y no espera tras una falla.</summary>
    public bool EstaListo(DateTimeOffset ahora) => Estado == EstadoDeAvisoACarrito.Pendiente && (ProximoIntentoEn is null || ProximoIntentoEn <= ahora);

    /// <summary>Carrito lo aceptó. Idempotente.</summary>
    public bool MarcarEnviado(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeAvisoACarrito.Pendiente)
        {
            return false;
        }

        Estado = EstadoDeAvisoACarrito.Enviado;
        EnviadoEn = ahora;
        ProximoIntentoEn = null;
        return true;
    }

    /// <summary>Carrito no lo aceptó (no respondió o el checkout aún reserva): se reintenta con una espera creciente.</summary>
    public void RegistrarFalla(DateTimeOffset ahora)
    {
        Intentos++;
        var espera = TimeSpan.FromTicks(Math.Min(EsperaMaxima.Ticks, EsperaBase.Ticks << Math.Min(Intentos - 1, 10)));
        ProximoIntentoEn = ahora + espera;
    }
    internal AvisoACarrito Copiar() => (AvisoACarrito)MemberwiseClone();
}

public enum EventoDeAvisoDePago
{
    Pendiente,
    Aprobado,
    Rechazado,
    Vencido,
    ReembolsoAutomatico,
}

public enum EstadoDeAvisoDePago
{
    Pendiente,
    Enviado,

    /// <summary>No se envía: la cuenta ya no existe, no hay correo o la persona no consintió el canal.</summary>
    Omitido,
}

/// <summary>
/// Un aviso del estado del pago al cliente (PAG-22, PAG-23): la pantalla, el correo y el WhatsApp. Una fila por <c>(intención, intento, evento)</c>; se crea en la misma transacción de la transición y su envío es independiente
/// del estado del cobro: una falla del envío nunca lo cambia. No lleva el correo ni el teléfono (se leen al enviar) ni datos de pago.
/// </summary>
public sealed class AvisoDePago
{
    private AvisoDePago(Guid id, Guid intencionId, Guid intentoId, EventoDeAvisoDePago evento, DateTimeOffset ahora)
    {
        Id = id;
        IntencionId = intencionId;
        IntentoId = intentoId;
        Evento = evento;
        Estado = EstadoDeAvisoDePago.Pendiente;
        CreadoEn = ahora;
    }

    public Guid Id { get; }

    public Guid IntencionId { get; }

    public Guid IntentoId { get; }

    public EventoDeAvisoDePago Evento { get; }

    public EstadoDeAvisoDePago Estado { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset? EnviadoEn { get; private set; }

    public int Intentos { get; private set; }

    public DateTimeOffset? ProximoIntentoEn { get; private set; }

    public (Guid IntencionId, Guid IntentoId, EventoDeAvisoDePago Evento) Clave => (IntencionId, IntentoId, Evento);

    public static AvisoDePago Crear(Guid intencionId, Guid intentoId, EventoDeAvisoDePago evento, DateTimeOffset ahora)
    {
        if (intencionId == Guid.Empty || intentoId == Guid.Empty)
        {
            throw new ArgumentException("El aviso necesita su intención y su intento.");
        }

        return new AvisoDePago(Guid.NewGuid(), intencionId, intentoId, evento, ahora);
    }


    /// <summary>Ya no tiene sentido enviarlo (lo supera otro aviso del mismo intento): no se envía. Devuelve si cambió.</summary>
    public bool Omitir()
    {
        if (Estado != EstadoDeAvisoDePago.Pendiente)
        {
            return false;
        }

        Estado = EstadoDeAvisoDePago.Omitido;
        return true;
    }
    internal AvisoDePago Copiar() => (AvisoDePago)MemberwiseClone();
}
