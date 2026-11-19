namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>Por qué se compensa un cobro: los cuatro motivos de la spec (PAG-15, PAG-20, PAG-29, PAG-32).</summary>
public enum MotivoDeCompensacion
{
    /// <summary>PAG-15: se aprobó pasados los 30 minutos o con la reserva expirada.</summary>
    PagoTardio,

    /// <summary>PAG-20: falló la confirmación de las reservas, la concesión de un acceso o la creación del pedido.</summary>
    FallaPosteriorAlPago,

    /// <summary>PAG-29: la intención ya se desvinculó por la cancelación de la cuenta.</summary>
    IntencionDesvinculada,

    /// <summary>PAG-32: la cancelación de la cuenta está en ejecución y debe prevalecer.</summary>
    CancelacionDeCuentaEnCurso,
}

/// <summary><c>Completa</c> libera reservas, reembolsa y cierra el checkout; <c>SoloReembolso</c> es un cobro sobrante: solo reembolsa, sin líneas ni cerrar el checkout (D07).</summary>
public enum VarianteDeCompensacion
{
    Completa,
    SoloReembolso,
}

/// <summary>El motivo que se envía al Inventario al liberar las reservas de una compra compensada (SEL-07).</summary>
public enum MotivoDeLiberacion
{
    /// <summary>Se intentó confirmar: libera las reservas pendientes y las confirmadas y cancela también los traslados.</summary>
    FallaPosteriorAlPago,

    /// <summary>Nunca se intentó confirmar: libera solo las reservas pendientes (no una ya confirmada).</summary>
    PagoVencido,
}

public static class MotivoDeLiberacionExtensiones
{
    /// <summary>El valor estable del contrato del Inventario.</summary>
    public static string ATexto(this MotivoDeLiberacion motivo) => motivo switch
    {
        MotivoDeLiberacion.FallaPosteriorAlPago => "falla-posterior-al-pago",
        MotivoDeLiberacion.PagoVencido => "pago-vencido",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };
}

public enum PasoDeCompensacion
{
    LiberarReservas,
    RegistrarReembolso,
    CerrarCheckout,
    Terminada,
}

/// <summary>
/// La compensación de un cobro que no se pudo completar (D07), una por cobro: su clave es <c>(intención, intento)</c>. Es la única ruta de PAG-15, PAG-20, PAG-29 y PAG-32 y se hace
/// en pasos cortos que se marcan y no se repiten: liberar las reservas, registrar el reembolso y cerrar el checkout. Una compensación ya iniciada se termina de inmediato y conserva su
/// motivo aunque la cancelación de la cuenta se revierta.
/// </summary>
public sealed class Compensacion
{
    private Compensacion(
        Guid intencionId,
        Guid intentoId,
        MotivoDeCompensacion motivo,
        VarianteDeCompensacion variante,
        bool tieneReservas,
        DateTimeOffset ahora)
    {
        IntencionId = intencionId;
        IntentoId = intentoId;
        Motivo = motivo;
        Variante = variante;
        IniciadaEn = ahora;
        Avisar = motivo is MotivoDeCompensacion.PagoTardio or MotivoDeCompensacion.FallaPosteriorAlPago;

        // Una compra solo digital no tiene reservas que liberar: el paso ya está hecho.
        if (variante == VarianteDeCompensacion.Completa && !tieneReservas)
        {
            ReservasLiberadasEn = ahora;
        }
    }

    public Guid IntencionId { get; }

    public Guid IntentoId { get; }

    public MotivoDeCompensacion Motivo { get; }

    public VarianteDeCompensacion Variante { get; }

    /// <summary>PAG-29 y PAG-32 no avisan ni leen el correo (PAG-26), tampoco en un cobro sobrante; los demás avisan «reembolso automático».</summary>
    public bool Avisar { get; }

    public DateTimeOffset IniciadaEn { get; }

    public DateTimeOffset? ReservasLiberadasEn { get; private set; }

    public MotivoDeLiberacion? MotivoDeLiberacion { get; private set; }

    public DateTimeOffset? ReembolsoRegistradoEn { get; private set; }

    public Guid? ReembolsoId { get; private set; }

    public DateTimeOffset? CheckoutCerradoEn { get; private set; }

    public DateTimeOffset? CompletadaEn { get; private set; }

    public bool EstaTerminada => CompletadaEn is not null;

    /// <summary>Una copia (para que las pruebas simulen la reversión de una transacción).</summary>
    /// <summary>La reconstruye desde el almacén (la infraestructura): no valida ni cambia nada. <see cref="Avisar"/> se deriva del motivo.</summary>
    public static Compensacion Rehidratar(
        Guid intencionId,
        Guid intentoId,
        MotivoDeCompensacion motivo,
        VarianteDeCompensacion variante,
        DateTimeOffset iniciadaEn,
        DateTimeOffset? reservasLiberadasEn,
        MotivoDeLiberacion? motivoDeLiberacion,
        DateTimeOffset? reembolsoRegistradoEn,
        Guid? reembolsoId,
        DateTimeOffset? checkoutCerradoEn,
        DateTimeOffset? completadaEn) =>
        new(intencionId, intentoId, motivo, variante, tieneReservas: true, iniciadaEn)
        {
            ReservasLiberadasEn = reservasLiberadasEn,
            MotivoDeLiberacion = motivoDeLiberacion,
            ReembolsoRegistradoEn = reembolsoRegistradoEn,
            ReembolsoId = reembolsoId,
            CheckoutCerradoEn = checkoutCerradoEn,
            CompletadaEn = completadaEn,
        };

    internal Compensacion Copiar() => (Compensacion)MemberwiseClone();

    /// <summary>La clave natural del reembolso de esta compensación: <c>comp:&lt;intentoId&gt;</c>.</summary>
    public string ClaveDelReembolso => ClaveDeReembolso.Compensacion(IntentoId);

    public PasoDeCompensacion SiguientePaso
    {
        get
        {
            if (Variante == VarianteDeCompensacion.Completa && ReservasLiberadasEn is null)
            {
                return PasoDeCompensacion.LiberarReservas;
            }

            if (ReembolsoId is null)
            {
                return PasoDeCompensacion.RegistrarReembolso;
            }

            if (Variante == VarianteDeCompensacion.Completa && CheckoutCerradoEn is null)
            {
                return PasoDeCompensacion.CerrarCheckout;
            }

            return PasoDeCompensacion.Terminada;
        }
    }

    public static Compensacion Iniciar(
        Guid intencionId,
        Guid intentoId,
        MotivoDeCompensacion motivo,
        VarianteDeCompensacion variante,
        bool tieneReservas,
        DateTimeOffset ahora)
    {
        if (intencionId == Guid.Empty || intentoId == Guid.Empty)
        {
            throw new ArgumentException("La compensación necesita su intención y su intento.");
        }

        return new Compensacion(intencionId, intentoId, motivo, variante, tieneReservas, ahora);
    }

    /// <summary>
    /// Por qué se compensa una intención aprobada. La desvinculación (PAG-29) y la cancelación en ejecución (PAG-32) prevalecen sobre el pago tardío porque no avisan ni leen datos; si
    /// no hay ninguno de los dos y se aprobó pasados los 30 minutos o con la reserva expirada, es un pago tardío; y si es a tiempo no se compensa (devuelve <c>null</c>).
    /// </summary>
    public static MotivoDeCompensacion? Clasificar(IntencionDePago intencion, bool cancelacionDeCuentaEnCurso)
    {
        ArgumentNullException.ThrowIfNull(intencion);
        if (intencion.Estado != EstadoDeIntencion.Aprobada)
        {
            throw new InvalidOperationException("Solo se clasifica una intención aprobada.");
        }

        if (intencion.EstaDesvinculada)
        {
            return MotivoDeCompensacion.IntencionDesvinculada;
        }

        if (cancelacionDeCuentaEnCurso)
        {
            return MotivoDeCompensacion.CancelacionDeCuentaEnCurso;
        }

        return intencion.EsTardia ? MotivoDeCompensacion.PagoTardio : null;
    }

    /// <summary>
    /// <c>falla-posterior-al-pago</c> si consta que se intentó confirmar (aunque no conste que terminó), para liberar también lo confirmado; <c>pago-vencido</c> solo si nunca se
    /// intentó (con ese motivo el Inventario no libera una reserva confirmada).
    /// </summary>
    public static MotivoDeLiberacion MotivoDeLiberacionPara(IntencionDePago intencion)
    {
        ArgumentNullException.ThrowIfNull(intencion);
        return intencion.ConfirmacionIniciadaEn is not null ? Pagos.MotivoDeLiberacion.FallaPosteriorAlPago : Pagos.MotivoDeLiberacion.PagoVencido;
    }

    public bool MarcarReservasLiberadas(MotivoDeLiberacion motivo, DateTimeOffset ahora)
    {
        if (Variante != VarianteDeCompensacion.Completa)
        {
            throw new InvalidOperationException("Un cobro sobrante no libera reservas.");
        }

        if (ReservasLiberadasEn is not null)
        {
            return false;
        }

        ReservasLiberadasEn = ahora;
        MotivoDeLiberacion = motivo;
        return true;
    }

    public bool MarcarReembolsoRegistrado(Guid reembolsoId, DateTimeOffset ahora)
    {
        if (ReembolsoId is not null)
        {
            return false;
        }

        if (Variante == VarianteDeCompensacion.Completa && ReservasLiberadasEn is null)
        {
            throw new InvalidOperationException("El reembolso se registra después de liberar las reservas.");
        }

        if (reembolsoId == Guid.Empty)
        {
            throw new ArgumentException("El reembolso registrado necesita su identificador.", nameof(reembolsoId));
        }

        ReembolsoId = reembolsoId;
        ReembolsoRegistradoEn = ahora;
        CerrarSiTermino(ahora);
        return true;
    }

    public bool MarcarCheckoutCerrado(DateTimeOffset ahora)
    {
        if (Variante != VarianteDeCompensacion.Completa)
        {
            throw new InvalidOperationException("Un cobro sobrante no cierra el checkout.");
        }

        if (CheckoutCerradoEn is not null)
        {
            return false;
        }

        if (ReembolsoId is null)
        {
            throw new InvalidOperationException("El checkout se cierra después de registrar el reembolso.");
        }

        CheckoutCerradoEn = ahora;
        CerrarSiTermino(ahora);
        return true;
    }

    private void CerrarSiTermino(DateTimeOffset ahora)
    {
        if (SiguientePaso == PasoDeCompensacion.Terminada)
        {
            CompletadaEn ??= ahora;
        }
    }
}
