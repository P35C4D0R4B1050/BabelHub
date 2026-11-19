using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

// Los puertos propios del módulo Pedidos (plan §2): lo externo que la Aplicación necesita y la Infraestructura del módulo implementa. Usan tipos del Dominio, así que son internos al módulo y no están en la raíz del contrato público (regla 13).

/// <summary>
/// Los pedidos y sus operaciones (esquema <c>pedidos</c>). Cada lectura <c>conBloqueo</c> es <c>SELECT … FOR UPDATE</c> con <c>lock_timeout</c> dentro de la transacción activa, en el orden global de bloqueo (cuenta → pedido → envío → intención → reembolso → …); sin transacción
/// no se bloquea. <see cref="Agregar"/> inserta con <c>ON CONFLICT DO NOTHING</c> sin objetivo (una colisión de <c>numero</c> tampoco aborta la transacción) y dice si insertó.
/// </summary>
public interface IRepositorioDePedidos
{
    Task<Pedido?> Obtener(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default);

    Task<bool> Existe(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Inserta el pedido con sus líneas; <see langword="false"/> si ya existía por <c>id</c> o por <c>numero</c>.</summary>
    Task<bool> Agregar(Pedido pedido, CancellationToken cancelacion = default);

    /// <summary>Guarda los cambios de estado del pedido y de sus líneas con comprobación de versión.</summary>
    Task Actualizar(Pedido pedido, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Anulacion>> AnulacionesEnCurso(Guid pedidoId, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Entrega>> EntregasEnCurso(Guid pedidoId, CancellationToken cancelacion = default);
}

public enum ResultadoDeCerrarReserva
{
    /// <summary>Se descontó el físico y se cerró la reserva de la línea.</summary>
    Cerrada,

    /// <summary>Repetir la clave devolvió el resultado original, o la línea ya estaba cerrada por esta causa: no se descuenta otra vez.</summary>
    YaCerrada,

    /// <summary><c>INV-RESERVA-ESTADO-INVALIDO</c>: la línea ya salió (cerrada por Envíos) y queda fuera.</summary>
    YaSalio,

    /// <summary><c>INV-IDEMPOTENCIA-EN-CURSO</c> u otro resultado incierto: el primer cierre puede seguir ejecutándose (reintentable).</summary>
    Incierto,

    /// <summary>Un error permanente del Inventario distinto de 409.</summary>
    Rechazada,
}

public enum ResultadoDeLiberarReserva
{
    Liberada,
    NoExistia,

    /// <summary><c>INV-RESERVA-ESTADO-INVALIDO</c>: la reserva ya se cerró (una entrega o un envío ganó).</summary>
    YaCerrada,

    NoSePudo,
}

/// <summary>
/// Las reservas del pedido sobre el Inventario (<c>reservas.cerrar</c> y <c>reservas.liberar</c>): <b>una llamada por línea</b>, con la clave <c>cierre-&lt;pedido&gt;-&lt;línea&gt;</c>, fuera de toda transacción. Pedidos no usa el token del usuario: el cliente de <c>Plataforma</c> lo pone
/// en <c>cerrar</c> y la credencial de sistema en <c>liberar</c>. Una falla del Inventario nunca es una excepción.
/// </summary>
public interface IReservasDelPedido
{
    Task<ResultadoDeCerrarReserva> Cerrar(Guid pedidoId, Guid lineaId, string claveDeCierre, CancellationToken cancelacion = default);

    /// <summary>Libera la reserva de las líneas dadas (solo las que no salieron) con un motivo (<c>pedido-anulado</c>, <c>cancelado-por-el-cliente</c> o <c>no-recogido</c>); idempotente por estado.</summary>
    Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, CancellationToken cancelacion = default);
}

/// <summary>
/// La equivalencia entre el código de una sucursal y su identificador (D11): el alcance del encargado se comprueba en la Aplicación con el identificador de su sucursal; si no se puede resolver, se niega y se registra. Hasta que Identidad fije la equivalencia (D15 de
/// <c>docs/03/08</c>) la resuelve un doble por código.
/// </summary>
public interface IResolucionDeSucursal
{
    Task<Guid?> IdentificadorDe(string codigoDeSucursal, CancellationToken cancelacion = default);
}

/// <summary>El número aleatorio de un pedido (D06): generador criptográfico en la Infraestructura; el formato es del Dominio.</summary>
public interface IGeneradorDeNumerosDePedido
{
    NumeroDePedido Siguiente();
}
