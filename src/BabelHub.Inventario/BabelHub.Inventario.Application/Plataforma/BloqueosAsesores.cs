namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Bloqueos asesores de la transacción en curso (<c>pg_advisory_xact_lock</c>): duran hasta que la transacción termina y los módulos del
/// Inventario los toman siempre en el orden de la jerarquía única de <c>inventario-fuentes</c> D04 (cliente → pedido → claves del motor de
/// límites → devolución o recepción → traslados vinculados → líneas de reserva → filas de stock). Ningún módulo arma sus propias claves:
/// cada tipo tiene un prefijo propio, de modo que el mismo identificador en dos tipos distintos no choca (D04).
/// Todos exigen una transacción activa y esperan como máximo el tiempo configurado (<see cref="EsperaDeBloqueoAgotadaException"/>).
/// </summary>
public interface IBloqueosAsesores
{
    /// <summary>Bloquea al cliente (primer eslabón de la jerarquía).</summary>
    Task Cliente(Guid clienteId, CancellationToken cancelacion = default);

    /// <summary>Bloquea un pedido: toda operación sobre un pedido (reservar, confirmar, extender, liberar, cerrar y las de traslados vinculados) lo toma.</summary>
    Task Pedido(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>
    /// Intenta bloquear un pedido sin esperar (<c>pg_try_advisory_xact_lock</c>): <see langword="false"/> si otra transacción lo tiene. La usa la
    /// expiración de vencidas ajenas, que omite el pedido que no puede bloquear (error conservador).
    /// </summary>
    Task<bool> IntentarPedido(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Bloquea la recepción: la fuente de recepción es única, y quien la cambia o ingresa mercadería se serializa aquí (FUE-19, FUE-20).</summary>
    Task Recepcion(CancellationToken cancelacion = default);

    /// <summary>Bloquea una devolución por su identificador, para que dos reingresos de la misma devolución se apliquen una sola vez.</summary>
    Task Devolucion(Guid devolucionId, CancellationToken cancelacion = default);
}

/// <summary>
/// Un bloqueo asesor no se obtuvo dentro de la espera máxima (<c>lock_timeout</c>). El caso de uso lo traduce a su propio resultado
/// (por ejemplo, «operación en curso» en una repetición idempotente); nunca se oculta como éxito.
/// </summary>
public sealed class EsperaDeBloqueoAgotadaException : Exception
{
    public EsperaDeBloqueoAgotadaException()
        : base("No se obtuvo el bloqueo dentro de la espera máxima.")
    {
    }

    public EsperaDeBloqueoAgotadaException(Exception interna)
        : base("No se obtuvo el bloqueo dentro de la espera máxima.", interna)
    {
    }
}
