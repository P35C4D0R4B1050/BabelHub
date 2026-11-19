using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

// Los puertos propios del módulo Pedidos (plan §2): lo externo que la Aplicación necesita y la Infraestructura del módulo implementa. Usan tipos del Dominio, así que son internos al módulo y no están en la raíz del contrato público (regla 13).

/// <summary>Los criterios de un listado: la cuenta (el cliente), la sucursal de recojo (el encargado), solo pedidos a domicilio (logística, PED-26), el estado derivado y la página; el tamaño ya viene acotado a 50.</summary>
public sealed record CriterioDeListado(Guid? CuentaId, string? CodigoDeSucursalDeRecojo, bool SoloADomicilio, EstadoDePedido? Estado, int Pagina, int Tamano);

/// <summary>Un estado de traslado ya aplicado (<c>estados_de_traslado_recibidos</c>): el evento, el traslado, el pedido, el estado y la huella del cuerpo; se purga a los 90 días.</summary>
public sealed record EstadoDeTrasladoRegistrado(string EventoId, Guid TrasladoId, Guid PedidoId, string Estado, string HuellaDelCuerpo, DateTimeOffset RecibidoEn);

/// <summary>El bloqueo de un pedido no se obtuvo a tiempo (<c>lock_timeout</c> de 5 s): el caso de uso responde <c>conflicto</c>, reintentable.</summary>
public sealed class EsperaDeBloqueoDeUnPedidoAgotadaException() : Exception("No se obtuvo el bloqueo del pedido a tiempo.");

public sealed record PaginaDeDominio(IReadOnlyList<Pedido> Pedidos, int Total);

/// <summary>La auditoría de la lectura del contacto por el personal (PED-40, D09): si no se puede auditar lanza y el detalle se muestra sin contacto. Una acción propia del catálogo de auditoría llega con el carril de auditoría.</summary>
public interface IAuditoriaDeContacto
{
    Task ContactoConsultado(Guid pedidoId, string rol, CancellationToken cancelacion = default);
}

/// <summary>
/// Los pedidos y sus operaciones (esquema <c>pedidos</c>). Cada lectura <c>conBloqueo</c> es <c>SELECT … FOR UPDATE</c> con <c>lock_timeout</c> dentro de la transacción activa, en el orden global de bloqueo; sin transacción no se bloquea. <see cref="Agregar"/> inserta con <c>ON CONFLICT DO NOTHING</c> sin objetivo (una colisión de <c>numero</c> tampoco aborta la transacción) y dice si insertó.
/// </summary>
public interface IRepositorioDePedidos
{
    /// <summary>Un listado paginado, del más reciente al más antiguo, con el total; nunca carga el contacto en un listado que no lo necesita.</summary>
    Task<PaginaDeDominio> Listar(CriterioDeListado criterio, CancellationToken cancelacion = default);

    Task<Pedido?> Obtener(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default);

    Task<bool> Existe(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Inserta el pedido con sus líneas; <see langword="false"/> si ya existía por <c>id</c> o por <c>numero</c>.</summary>
    Task<bool> Agregar(Pedido pedido, CancellationToken cancelacion = default);

    /// <summary>Guarda los cambios de estado del pedido y de sus líneas con comprobación de versión.</summary>
    Task Actualizar(Pedido pedido, CancellationToken cancelacion = default);

    /// <summary>Anota que falta pedir a Pagos que elimine el contacto de la intención de este pedido (<c>liberaciones_de_contacto</c>, PED-35): se registra en la misma transacción de la creación; idempotente.</summary>
    Task RegistrarLiberacionDeContacto(Guid pedidoId, DateTimeOffset desde, CancellationToken cancelacion = default);

    /// <summary>El estado de traslado ya recibido por su <c>eventoId</c> (idempotencia de la entrada del Inventario): su huella dice si el cuerpo es el mismo.</summary>
    Task<EstadoDeTrasladoRegistrado?> ObtenerEstadoDeTraslado(string eventoId, CancellationToken cancelacion = default);

    Task GuardarEstadoDeTraslado(EstadoDeTrasladoRegistrado estado, CancellationToken cancelacion = default);

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
