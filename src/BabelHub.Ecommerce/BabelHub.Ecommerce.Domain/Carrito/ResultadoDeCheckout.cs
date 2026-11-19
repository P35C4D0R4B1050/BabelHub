namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>
/// El estado de la compra en curso (D02, D20): vivos <c>en_edicion</c>, <c>reservando</c> (entre el paso 1 de pasar al pago y el 3b), <c>en_pago</c> y <c>pago_pendiente</c>; terminales <c>pagado</c>, <c>cancelado</c> y <c>reiniciado</c>.
/// </summary>
public enum EstadoDelCheckout
{
    EnEdicion,
    Reservando,
    EnPago,
    PagoPendiente,
    Pagado,
    Cancelado,
    Reiniciado,
}

/// <summary>Los cinco pasos de la compra (CAR-15): revisión del carrito, entrega, datos y aceptaciones, pago y confirmación. Un carrito solo digital no tiene el paso de entrega (son cuatro).</summary>
public enum PasoDelCheckout
{
    Revision = 1,
    Entrega = 2,
    DatosYAceptaciones = 3,
    Pago = 4,
    Confirmacion = 5,
}

/// <summary>Qué le pasó al checkout al pedirle una transición.</summary>
public enum EfectoDeTransicion
{
    /// <summary>La transición se aplicó.</summary>
    Aplicada,

    /// <summary>Ya estaba así o no hay nada que cambiar: éxito idempotente.</summary>
    SinCambios,

    /// <summary>El checkout está <c>reservando</c>: Pagos debe reintentar.</summary>
    AunNo,

    /// <summary>La condición ya no se cumple (otra solicitud lo llevó a otro estado, otro <c>pedidoId</c> u otra generación): no se toca nada.</summary>
    NoAplica,

    /// <summary>Una línea tiene un retiro pendiente: el estado no cambia y quien llama debe pasar por <c>Reabrir</c> y volver a <c>en_edicion</c> (D21).</summary>
    ConRetiroPendiente,

    Rechazada,
}

/// <summary>Por qué el checkout rechazó una operación.</summary>
public enum RechazoDeCheckout
{
    /// <summary>El pago está pendiente de confirmación: no se puede editar, volver ni cancelar (CAR-26).</summary>
    PagoPendiente,

    /// <summary>El checkout está <c>reservando</c>: otra solicitud pasa al pago (D20).</summary>
    EnCurso,

    /// <summary>El checkout ya terminó (<c>pagado</c>, <c>cancelado</c> o <c>reiniciado</c>).</summary>
    YaTerminado,

    /// <summary>El estado actual no admite la operación (por ejemplo elegir la entrega con el checkout <c>en_pago</c>: antes hay que reabrirlo).</summary>
    EstadoNoPermite,

    SinLineas,

    /// <summary>El carrito es solo digital: no tiene entrega (CAR-07).</summary>
    EsSoloDigital,

    EntregaNoElegida,

    /// <summary>Falta el nombre, el teléfono o la dirección de una entrega a domicilio (CAR-30).</summary>
    FaltaElContacto,

    /// <summary>Una línea del carrito tiene un retiro pendiente: el checkout la rechaza (D21).</summary>
    HayRetiroPendiente,

    /// <summary>Falta aceptar un texto legal requerido, o su versión cambió (CAR-12, CAR-13, CAR-28).</summary>
    AceptacionRequerida,
}

/// <summary>El resultado de una transición del checkout: su efecto, el rechazo si lo hubo y las ediciones que se quitaron del carrito por retiros pendientes (para dejar su aviso).</summary>
public sealed class ResultadoDeCheckout
{
    private ResultadoDeCheckout(EfectoDeTransicion efecto, RechazoDeCheckout? rechazo, IReadOnlyList<EdicionRetirada> retiradas)
    {
        Efecto = efecto;
        Rechazo = rechazo;
        Retiradas = retiradas;
    }

    public EfectoDeTransicion Efecto { get; }

    public RechazoDeCheckout? Rechazo { get; }

    public IReadOnlyList<EdicionRetirada> Retiradas { get; }

    public bool EsExito => Efecto is EfectoDeTransicion.Aplicada or EfectoDeTransicion.SinCambios;

    internal static ResultadoDeCheckout Aplicada(IReadOnlyList<EdicionRetirada>? retiradas = null) => new(EfectoDeTransicion.Aplicada, null, retiradas ?? []);

    internal static ResultadoDeCheckout SinCambios() => new(EfectoDeTransicion.SinCambios, null, []);

    internal static ResultadoDeCheckout De(EfectoDeTransicion efecto) => new(efecto, null, []);

    internal static ResultadoDeCheckout Rechazar(RechazoDeCheckout motivo) => new(EfectoDeTransicion.Rechazada, motivo, []);
}

/// <summary>El motivo con que Pagos resuelve un pago pendiente o compensa una compra (<c>IProgresoDelCheckout.PagoResuelto</c>).</summary>
public enum MotivoDePagoResuelto
{
    Vencido,
    Rechazado,
    Compensado,
}

/// <summary>Por qué se elimina el contacto de entrega de un checkout (CAR-34): lista cerrada, para el conteo de la auditoría (sin datos personales).</summary>
public enum MotivoDeEliminacionDeContacto
{
    PagoAprobado,
    Cancelar,
    Reiniciar,
    CambioARecojo,
    CambioASoloDigital,

    /// <summary>Pasaron 30 días desde la última modificación hecha por la persona (D08).</summary>
    Vencido,

    CuentaCancelada,
    CuentaReemplazada,
}
