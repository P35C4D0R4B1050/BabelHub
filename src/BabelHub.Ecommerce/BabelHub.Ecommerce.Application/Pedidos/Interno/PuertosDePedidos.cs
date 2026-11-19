using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

// Los puertos propios del módulo Pedidos (plan §2): lo externo que la Aplicación necesita y la Infraestructura del módulo implementa. Usan tipos del Dominio, así que son internos al módulo y no están en la raíz del contrato público (regla 13).

/// <summary>Los criterios de un listado: la cuenta (el cliente), la sucursal de recojo (el encargado), solo pedidos a domicilio (logística, PED-26), el estado derivado y la página; el tamaño ya viene acotado a 50.</summary>
public sealed record CriterioDeListado(Guid? CuentaId, string? CodigoDeSucursalDeRecojo, bool SoloADomicilio, EstadoDePedido? Estado, int Pagina, int Tamano);

/// <summary>Un estado de traslado ya aplicado (<c>estados_de_traslado_recibidos</c>): el evento, el traslado, el pedido, el estado y la huella del cuerpo; se purga a los 90 días.</summary>
public sealed record EstadoDeTrasladoRegistrado(string EventoId, Guid TrasladoId, Guid PedidoId, string Estado, string HuellaDelCuerpo, DateTimeOffset RecibidoEn);

/// <summary>El bloqueo de un pedido no se obtuvo a tiempo (<c>lock_timeout</c> de 5 s): el caso de uso responde <c>conflicto</c>, reintentable.</summary>
public sealed class EsperaDeBloqueoDeUnPedidoAgotadaException() : Exception("No se obtuvo el bloqueo del pedido a tiempo.");

/// <summary>La base de pedidos no respondió a tiempo (conexión caída, interbloqueo, serialización o consulta cancelada): la Api la traduce a «servicio no disponible» (503) y es reintentable. Vive en la Aplicación para que los casos de uso y la Api la vean sin depender de la Infraestructura.</summary>
public sealed class PedidosNoDisponiblesException(string motivo) : Exception($"La base de pedidos no está disponible ({motivo}).");

/// <summary>Una línea marcada no recogida a la que aún falta liberar la reserva en el Inventario (<c>liberaciones_de_reserva</c>, PED-15): se borra al lograrlo o al saber que la reserva ya se cerró.</summary>
public sealed record LiberacionDeReservaPendiente(Guid PedidoId, Guid LineaId, string Motivo, DateTimeOffset PendienteDesde, int Intentos);

/// <summary>Una liberación del contacto de la intención de pago que aún falta pedir a Pagos (PED-35): desde cuándo y cuántos intentos lleva.</summary>
public sealed record LiberacionDeContactoPendiente(Guid PedidoId, DateTimeOffset Desde, int Intentos);

/// <summary>Lo que la reanudación debe retomar: las anulaciones y las entregas en curso que ya cumplieron su espera.</summary>
public sealed record OperacionesPorReanudar(IReadOnlyList<Anulacion> Anulaciones, IReadOnlyList<Entrega> Entregas);

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

    /// <summary>Guarda los cambios de estado del pedido y de sus líneas (suma la versión). Se llama siempre sobre un pedido leído <b>con bloqueo</b> en la misma transacción: el bloqueo, no una comparación de versión, impide que dos operaciones pisen el estado (PED-21).</summary>
    Task Actualizar(Pedido pedido, CancellationToken cancelacion = default);

    /// <summary>Anota que falta pedir a Pagos que elimine el contacto de la intención de este pedido (<c>liberaciones_de_contacto</c>, PED-35): se registra en la misma transacción de la creación; idempotente.</summary>
    Task RegistrarLiberacionDeContacto(Guid pedidoId, DateTimeOffset desde, CancellationToken cancelacion = default);

    /// <summary>El estado de traslado ya recibido por su <c>eventoId</c> (idempotencia de la entrada del Inventario): su huella dice si el cuerpo es el mismo.</summary>
    Task<EstadoDeTrasladoRegistrado?> ObtenerEstadoDeTraslado(string eventoId, CancellationToken cancelacion = default);

    Task GuardarEstadoDeTraslado(EstadoDeTrasladoRegistrado estado, CancellationToken cancelacion = default);

    /// <summary>Inserta la fila de una entrega (tiempo 1) y guarda sus cambios (el cierre confirmado de cada línea y su estado).</summary>
    Task AgregarEntrega(Entrega entrega, CancellationToken cancelacion = default);

    Task ActualizarEntrega(Entrega entrega, CancellationToken cancelacion = default);

    /// <summary>Inserta la fila de una anulación (tiempo 1) y guarda sus cambios (pasos, reembolso y estado).</summary>
    Task AgregarAnulacion(Anulacion anulacion, CancellationToken cancelacion = default);

    Task ActualizarAnulacion(Anulacion anulacion, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Anulacion>> AnulacionesEnCurso(Guid pedidoId, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Entrega>> EntregasEnCurso(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Todos los pedidos de la cuenta, <b>con bloqueo</b> y en orden de identificador (orden global de bloqueo: la marca de la cuenta es de Identidad y va antes): los que anonimiza la cancelación de la cuenta (T24). Corre dentro de la transacción de Identidad y bajo el rol del esquema.</summary>
    Task<IReadOnlyList<Pedido>> ListarDeLaCuentaConBloqueo(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>Los pedidos vencidos para depurar (dos años por fecha de Lima desde su último cambio a entregado o anulado, PED-34) a los que aún queda algo por anonimizar (el contacto o la identidad de quien inició sus operaciones), <b>con bloqueo</b> y en orden de identificador, hasta <paramref name="maximo"/> (T25).</summary>
    Task<IReadOnlyList<Pedido>> ListarVencidosParaDepurar(DateOnly hoyEnLima, int maximo, CancellationToken cancelacion = default);

    /// <summary>Anonimiza el iniciador (identificador, rol y sucursal) de todas las anulaciones y entregas del pedido (PED-34); devuelve cuántas filas cambió y repetirlo no cambia nada.</summary>
    Task<int> AnonimizarIniciadoresDeOperaciones(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Cuándo terminó con éxito la última depuración (<see langword="null"/> si nunca): la alerta de «sin corrida en 24 horas» (PED-39) se mide contra ella.</summary>
    Task<DateTimeOffset?> UltimaDepuracionExitosaEn(CancellationToken cancelacion = default);

    Task RegistrarDepuracionExitosa(DateTimeOffset cuando, CancellationToken cancelacion = default);

    /// <summary>Las liberaciones del contacto de la intención que faltan (<c>liberaciones_de_contacto</c>, PED-35), las más antiguas primero.</summary>
    Task<IReadOnlyList<LiberacionDeContactoPendiente>> LiberacionesDeContactoPendientes(int maximo, CancellationToken cancelacion = default);

    Task QuitarLiberacionDeContacto(Guid pedidoId, CancellationToken cancelacion = default);

    Task AnotarIntentoDeLiberacionDeContacto(Guid pedidoId, DateTimeOffset cuando, CancellationToken cancelacion = default);

    /// <summary>Cuántas líneas están <c>no recogidas</c> desde antes de <paramref name="marcadasAntesDe"/> sin anularse ni entregarse (T27).</summary>
    Task<int> ContarNoRecogidasSinAnularDesde(DateTimeOffset marcadasAntesDe, CancellationToken cancelacion = default);

    /// <summary>Cuántas líneas impresas, de pedidos creados antes de <paramref name="creadosAntesDe"/>, no llegaron a entregadas ni anuladas (T27).</summary>
    Task<int> ContarImpresasSinEstadoFinalDesde(DateTimeOffset creadosAntesDe, CancellationToken cancelacion = default);

    /// <summary>Borra los estados de traslado recibidos antes de <paramref name="recibidosAntesDe"/> (90 días: solo sirven para la idempotencia) y devuelve cuántos.</summary>
    Task<int> PurgarEstadosDeTraslado(DateTimeOffset recibidosAntesDe, CancellationToken cancelacion = default);

    /// <summary>Cuántos pedidos de la cuenta tienen alguna línea impresa sin entregar ni anular, incluidas las no recogidas (PED-31, AC-PED-26): un pedido solo digital no cuenta.</summary>
    Task<int> ContarPedidosConImpresaSinCerrar(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>Las anulaciones <c>fallida</c>s del pedido (repetir una anulación fallida crea una fila nueva): sus líneas ya pasaron por la cancelación de envíos, así que un envío que Envíos informa «mantenido» por estar ya cancelado no cuenta como salido.</summary>
    Task<IReadOnlyList<Anulacion>> AnulacionesFallidas(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Las anulaciones y entregas <c>en_curso</c> cuyo <c>proximo_intento_en</c> ya llegó (la reanudación de T22), las más antiguas primero y hasta <paramref name="maximo"/> en total; el adaptador reclama cada fila empujando su <c>proximo_intento_en</c> en una transacción corta (una concesión), para que dos instancias no tomen la misma; además el tiempo 3 revalida bajo el bloqueo del pedido que la fila siga en curso.</summary>
    Task<OperacionesPorReanudar> OperacionesEnCursoParaReanudar(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default);

    /// <summary>Los pedidos de recojo con alguna línea que lleva más de 360 horas en listo para recojo (candidatos de la revisión de plazos, PED-15), hasta <paramref name="maximo"/>; sin bloqueo: cada uno se revisa luego bajo el suyo.</summary>
    Task<IReadOnlyList<Guid>> PedidosConRecojoVencido(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default);

    /// <summary>Anota, en la transacción que marca las líneas no recogidas, que falta liberar su reserva con <paramref name="motivo"/>; idempotente por línea.</summary>
    Task RegistrarLiberacionesDeReserva(Guid pedidoId, IReadOnlyCollection<Guid> lineaIds, string motivo, DateTimeOffset desde, CancellationToken cancelacion = default);

    Task<IReadOnlyList<LiberacionDeReservaPendiente>> LiberacionesDeReservaPendientes(int maximo, CancellationToken cancelacion = default);

    Task QuitarLiberacionesDeReserva(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default);

    Task AnotarIntentoDeLiberacion(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default);
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
