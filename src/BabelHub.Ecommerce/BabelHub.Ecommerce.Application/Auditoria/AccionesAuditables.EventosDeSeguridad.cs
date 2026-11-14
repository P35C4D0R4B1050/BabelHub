namespace BabelHub.Ecommerce.Application.Auditoria;

public static partial class AccionesAuditables
{
    // ---------- Eventos de seguridad de la plataforma (AUD-05, AUD-28, AUD-37) ----------

    /// <summary>Un acceso fuera del rol, de la sucursal o de lo que le pertenece a quien lo pide, o sin la sesión que exige: un único evento por solicitud.</summary>
    public static readonly AccionAuditable AccesoDenegado = new(nameof(AccesoDenegado));

    /// <summary>Se excedió un límite de uso: un solo evento por usuario u origen, límite y ventana de 5 minutos, con el total (AUD-28).</summary>
    public static readonly AccionAuditable LimiteExcedido = new(nameof(LimiteExcedido));

    /// <summary>Se excedió el presupuesto del personal (AUD-37).</summary>
    public static readonly AccionAuditable PresupuestoDelPersonalExcedido = new(nameof(PresupuestoDelPersonalExcedido));

    /// <summary>Una llamada entre servicios llegó sin la credencial de servicio (AUD-37).</summary>
    public static readonly AccionAuditable LlamadaEntreServiciosSinCredencial = new(nameof(LlamadaEntreServiciosSinCredencial));

    /// <summary>No se pudo determinar el origen de una solicitud (AUD-37).</summary>
    public static readonly AccionAuditable OrigenIndeterminable = new(nameof(OrigenIndeterminable));

    /// <summary>Falló la verificación de que quien solicita es una persona (AUD-37).</summary>
    public static readonly AccionAuditable VerificacionDePersonaFallida = new(nameof(VerificacionDePersonaFallida));

    /// <summary>Una llamada de una integración no se pudo verificar y se rechazó (AUD-37).</summary>
    public static readonly AccionAuditable EntradaDeIntegracionRechazada = new(nameof(EntradaDeIntegracionRechazada));

    // ---------- Eventos de Identidad (AUD-10, AUD-33): todos «aislados» (D17) ----------

    /// <summary>Un ingreso fallido: el identificador de la cuenta solo si existe; si no, solo el origen.</summary>
    public static readonly AccionAuditable IngresoFallido = new(nameof(IngresoFallido), aislada: true);

    /// <summary>Se aplicó un bloqueo temporal tras los intentos fallidos (se aplica aunque la auditoría no responda, AC-AUD-03).</summary>
    public static readonly AccionAuditable BloqueoTemporalAplicado = new(nameof(BloqueoTemporalAplicado), aislada: true);

    /// <summary>Se solicitó una recuperación de acceso (mantiene su efecto aunque la auditoría no responda).</summary>
    public static readonly AccionAuditable RecuperacionDeAccesoSolicitada = new(nameof(RecuperacionDeAccesoSolicitada), aislada: true);

    /// <summary>Se completó una recuperación de acceso: si no se puede auditar, no se completa (AC-AUD-03).</summary>
    public static readonly AccionAuditable RecuperacionDeAccesoCompletada = new(nameof(RecuperacionDeAccesoCompletada), aislada: true);

    public static readonly AccionAuditable SegundoFactorFallido = new(nameof(SegundoFactorFallido), aislada: true);

    /// <summary>Se usó un código de respaldo (sin el código en el registro).</summary>
    public static readonly AccionAuditable CodigoDeRespaldoUsado = new(nameof(CodigoDeRespaldoUsado), aislada: true);

    public static readonly AccionAuditable ContrasenaCambiada = new(nameof(ContrasenaCambiada), aislada: true);

    public static readonly AccionAuditable CorreoCambiado = new(nameof(CorreoCambiado), aislada: true);

    public static readonly AccionAuditable SegundoFactorActivado = new(nameof(SegundoFactorActivado), aislada: true);

    public static readonly AccionAuditable SegundoFactorDesactivado = new(nameof(SegundoFactorDesactivado), aislada: true);

    public static readonly AccionAuditable SegundoFactorRegenerado = new(nameof(SegundoFactorRegenerado), aislada: true);

    /// <summary>Un cambio de correo desde un dispositivo o una región no reconocidos se confirmó (concede, así que si no se puede auditar no se aplica).</summary>
    public static readonly AccionAuditable CambioDeCorreoNoReconocidoConfirmado = new(nameof(CambioDeCorreoNoReconocidoConfirmado), aislada: true);

    public static readonly AccionAuditable CambioDeCorreoNoReconocidoRechazado = new(nameof(CambioDeCorreoNoReconocidoRechazado), aislada: true);

    public static readonly AccionAuditable CambioDeCorreoNoReconocidoVencido = new(nameof(CambioDeCorreoNoReconocidoVencido), aislada: true);
}
