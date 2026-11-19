namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Cómo terminó una llamada al Inventario.</summary>
public enum EstadoDeLlamadaAlInventario
{
    /// <summary>Respondió con el éxito del contrato y, si lleva cuerpo, con un valor válido.</summary>
    Exitosa,

    /// <summary>Respondió que no (400, 401, 403, 404, 409 o 422): <see cref="ResultadoDelInventario.Codigo"/> trae el <c>INV-…</c> estable. No es una falla del servicio.</summary>
    Rechazada,

    /// <summary>Respondió 429 (<c>INV-LIMITE-EXCEDIDO</c>): quien llama lo trata como «sin respuesta» y usa lo último que sabía.</summary>
    LimiteExcedido,

    /// <summary>No respondió a tiempo, cortó, el cortacircuitos estaba abierto, respondió 5xx o con un cuerpo que el contrato no admite, o no había sesión para una operación de usuario.</summary>
    NoDisponible,
}

/// <summary>
/// Resultado de una llamada al Inventario sin valor de retorno (204). <b>Una falla del Inventario nunca es una excepción</b>: es un estado. Nunca lleva el
/// texto del error remoto, el cuerpo de la respuesta ni el token (SEG-10).
/// </summary>
public record ResultadoDelInventario
{
    protected ResultadoDelInventario(EstadoDeLlamadaAlInventario estado, string? codigo, int? estadoHttp)
    {
        Estado = estado;
        Codigo = codigo;
        EstadoHttp = estadoHttp;
    }

    public EstadoDeLlamadaAlInventario Estado { get; }

    public bool Exitosa => Estado == EstadoDeLlamadaAlInventario.Exitosa;

    /// <summary>El <c>code</c> estable del problema (<c>INV-NO-ENCONTRADO</c>…); <see langword="null"/> si no hubo uno válido.</summary>
    public string? Codigo { get; }

    /// <summary>El estado HTTP de una respuesta rechazada o limitada; <see langword="null"/> si no hubo respuesta.</summary>
    public int? EstadoHttp { get; }

    /// <summary>En un rechazo por falta de stock (<c>INV-STOCK-INSUFICIENTE</c>, 409): las líneas que no alcanzan y lo máximo que hay en la red (CAR-04, CAR-27); ninguna se reservó.</summary>
    public IReadOnlyList<LineaSinStockDelInventario>? LineasSinStock { get; init; }

    /// <summary>En un rechazo por límite (429): el motivo (la clave de la política), la espera sugerida y desde cuándo se puede reintentar (SEG-07).</summary>
    public RechazoPorLimiteDelInventario? Limite { get; init; }

    public static ResultadoDelInventario Correcta { get; } = new(EstadoDeLlamadaAlInventario.Exitosa, null, null);

    public static ResultadoDelInventario Rechazo(int estadoHttp, string? codigo) => new(EstadoDeLlamadaAlInventario.Rechazada, codigo, estadoHttp);

    public static ResultadoDelInventario Limitada(string? codigo) => new(EstadoDeLlamadaAlInventario.LimiteExcedido, codigo, 429);

    public static ResultadoDelInventario SinRespuesta { get; } = new(EstadoDeLlamadaAlInventario.NoDisponible, null, null);
}

/// <summary>El resultado de una llamada al Inventario con valor de retorno; pedir el valor sin éxito es un error de programación.</summary>
public sealed record ResultadoDelInventario<T> : ResultadoDelInventario
{
    private readonly T? _valor;

    private ResultadoDelInventario(EstadoDeLlamadaAlInventario estado, string? codigo, int? estadoHttp, T? valor)
        : base(estado, codigo, estadoHttp)
    {
        _valor = valor;
    }

    public T Valor => Exitosa ? _valor! : throw new InvalidOperationException("El Inventario no respondió con un valor y no hay valor.");

    public static ResultadoDelInventario<T> ConValor(T valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return new ResultadoDelInventario<T>(EstadoDeLlamadaAlInventario.Exitosa, null, null, valor);
    }

    public static new ResultadoDelInventario<T> Rechazo(int estadoHttp, string? codigo) => new(EstadoDeLlamadaAlInventario.Rechazada, codigo, estadoHttp, default);

    public static new ResultadoDelInventario<T> Limitada(string? codigo) => new(EstadoDeLlamadaAlInventario.LimiteExcedido, codigo, 429, default);

    public static new ResultadoDelInventario<T> SinRespuesta { get; } = new(EstadoDeLlamadaAlInventario.NoDisponible, null, null, default);
}

/// <summary>Una edición que se da de alta en el Inventario con su formato (<c>impreso</c> o <c>digital</c>; solo las impresas tienen stock).</summary>
public sealed record EdicionARegistrar(Guid EdicionId, string Formato);

/// <summary>Lo vendible de una edición en una fuente; el físico y el reservado no salen del Inventario (FUE-08).</summary>
public sealed record FuenteConVendible(string CodigoDeFuente, string Nombre, int Vendible, string EstadoDeStock);

/// <summary>La disponibilidad consolidada de una edición y su desglose por fuente.</summary>
public sealed record DisponibilidadDeLaEdicion(Guid EdicionId, int VendibleEnLaRed, IReadOnlyList<FuenteConVendible> Fuentes, DateTimeOffset ConsultadaEn);

/// <summary>La disponibilidad de varias ediciones; las que el Inventario no conoce van aparte, sin fallar toda la consulta.</summary>
public sealed record DisponibilidadDeVariasEdiciones(IReadOnlyList<DisponibilidadDeLaEdicion> Ediciones, IReadOnlyList<Guid> NoRegistradas);

/// <summary>Un ingreso de mercadería que se pide registrar: sin fuente va a la de recepción; la cantidad es un entero entre 1 y 5 000.</summary>
public sealed record IngresoARegistrar(Guid EdicionId, string? CodigoDeFuente, int Cantidad, string? Referencia, string? Nota);

/// <summary>Un ingreso de mercadería registrado, con su fecha y el usuario que lo hizo.</summary>
public sealed record IngresoRegistrado(Guid IngresoId, Guid EdicionId, string CodigoDeFuente, int Cantidad, string? Referencia, string? Nota, Guid? UsuarioId, DateTimeOffset Instante);

/// <summary>Las reservas activas (pendientes no vencidas o confirmadas) de un titular, sin datos de las ediciones (IDE-27, SEL-33).</summary>
public sealed record PendientesDeTitular(int ReservasActivas);

/// <summary>Una sucursal tal como la ve quien elige un recojo o una sucursal preferida.</summary>
public sealed record SucursalPublica(string Codigo, string Nombre, string Region);

/// <summary>
/// El puerto con el que el E-commerce llama al Inventario por REST (contratos v1, <c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c>): vive en el contrato público de
/// <c>Plataforma</c> con modelos propios (ningún DTO de <c>Shared</c> ni tipo del Dominio) y lo consumen los módulos mediante un puerto fino propio, sin abrir un <c>HttpClient</c>
/// (decisión D01 de <c>ecommerce-catalogo</c>, regla 13). La Infraestructura lo implementa con plazo de 10 segundos, reintentos solo de lo idempotente, cortacircuitos, la credencial
/// del alcance de cada operación y el token del usuario en las operaciones de usuario (nunca una cabecera de actor). <b>Una falla del Inventario nunca es una excepción</b>: es un
/// <see cref="EstadoDeLlamadaAlInventario"/>. Las operaciones de pendientes y bajas de un titular las agrega <c>identidad-seguridad</c> (T12 y T35) en este mismo puerto.
/// </summary>
public interface IClienteDelInventario
{
    /// <summary>
    /// <c>inventario.ediciones.registrar</c>: da de alta (o confirma) una edición con su formato. Es una operación de la administración: lleva el token del usuario de la sesión. <c>PUT</c> idempotente (204).
    /// Un formato distinto del ya registrado se rechaza con <c>INV-FORMATO-EN-CONFLICTO</c>.
    /// </summary>
    Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.disponibilidad.consultar-varias</c>: la disponibilidad de hasta 50 ediciones (alcance de lectura, sin token). Reenvía la dirección del cliente final con la credencial
    /// <c>origen-reenviado</c> si la solicitud en curso la tiene (SEG-17). Una lectura: se puede reintentar.
    /// </summary>
    Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.ingresos.registrar</c>: registra un ingreso de mercadería (token de la administración central). La <paramref name="claveDeIdempotencia"/> (16 a 80 caracteres
    /// <c>[A-Za-z0-9_-]</c>) hace seguro el reintento: repetirla con la misma carga devuelve el ingreso original, con otra, <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>.
    /// </summary>
    Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary><c>inventario.ingresos.listar</c>: los últimos ingresos de una edición, del más reciente al más antiguo (hasta <paramref name="limite"/>, de 1 a 50; token del usuario, no es una lectura de servicio).</summary>
    Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.titulares.consultar-pendientes</c>: cuántas reservas activas tiene el titular de una cuenta (alcance <c>consulta-de-pendientes</c>, sin token; IDE-27, SEL-33). El identificador viaja en el cuerpo, nunca en la ruta.
    /// Es una lectura: se puede reintentar.
    /// </summary>
    Task<ResultadoDelInventario<PendientesDeTitular>> ConsultarPendientes(Guid titularId, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.titulares.baja</c>: da de baja al titular de una cuenta cancelada (alcance <c>baja-de-titular</c>, sin token; IDE-64, SEL-34): el Inventario sustituye su identificador por uno anónimo estable. Repetirla no tiene efecto duplicado
    /// (204 también al repetirla), así que se puede reintentar; Identidad la reintenta hasta que se acepta.
    /// </summary>
    Task<ResultadoDelInventario> DarDeBajaAlTitular(Guid titularId, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.consulta-previa</c>: qué fuente se seleccionaría para cada línea impresa, sin reservar (alcance de lectura, sin token; SEL-20). Una lectura: se puede reintentar. Reenvía el origen del cliente final
    /// si la solicitud en curso lo tiene (SEG-17).
    /// </summary>
    Task<ResultadoDelInventario<RespuestaDeConsultaPrevia>> ConsultarPrevia(ConsultaPrevia consulta, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.crear</c>: reserva los impresos de un pedido, todas las líneas o ninguna (token del usuario, clave de idempotencia obligatoria; SEL-01). <b>Un 409 de stock</b> trae <see cref="ResultadoDelInventario.LineasSinStock"/>;
    /// <b>un 429</b> trae <see cref="ResultadoDelInventario.Limite"/> (con el instante de reintento). Repetir la clave con la misma carga devuelve la reserva original.
    /// </summary>
    Task<ResultadoDelInventario<ReservaDelInventario>> Reservar(SolicitudDeReservaDeStock solicitud, string claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.liberar</c>: libera la reserva de un pedido o solo algunas líneas (<paramref name="lineaIds"/> nulo o vacío son todas), con un motivo del contrato (alcance <c>expiracion-de-reservas</c>, sin token; SEL-07). Repetirla no cambia el stock,
    /// así que se puede reintentar.
    /// </summary>
    Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.consultar</c>: la reserva de un pedido (alcance de usuario: lleva el token de la persona, sin clave de idempotencia; SEL-04). Una lectura: se puede reintentar. Un <c>404</c> es <c>INV-NO-ENCONTRADO</c> y
    /// un <c>403</c>, que la reserva no es de esa persona.
    /// </summary>
    Task<ResultadoDelInventario<ReservaDelInventario>> ConsultarReserva(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.extender</c>: alarga la reserva de un pedido con un pago pendiente (alcance <c>expiracion-de-reservas</c>, sin token y sin cuerpo; SEL-22, SEL-30). <see cref="ExtensionDeLaReserva.Extendida"/> es falso si ya se extendió o la
    /// reserva ya no está pendiente. Repetirla no extiende dos veces, así que se puede reintentar.
    /// </summary>
    Task<ResultadoDelInventario<ExtensionDeLaReserva>> ExtenderReserva(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.confirmar</c>: confirma la reserva de un pedido ya pagado y genera sus traslados (alcance <c>expiracion-de-reservas</c>, sin token y con clave de idempotencia obligatoria; SEL-03). Repetir la clave devuelve la reserva ya confirmada;
    /// <c>INV-RESERVA-ESTADO-INVALIDO</c> si ya no puede confirmarse.
    /// </summary>
    Task<ResultadoDelInventario<ReservaDelInventario>> ConfirmarReserva(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.reservas.cerrar</c>: descuenta el físico de las líneas de una reserva confirmada y las cierra (alcance de usuario: lleva el token de la persona, con clave de idempotencia obligatoria; SEL-11, SEL-21, ENV-12).
    /// <paramref name="motivo"/> es <c>entrega</c> (un pedido de recojo) o <c>reparto</c> (un envío, con su <paramref name="envioId"/>). Repetir la misma clave devuelve el resultado original; una clave nueva sobre líneas ya cerradas responde
    /// <c>ya-cerrada</c> sin volver a descontar; <c>INV-RESERVA-ESTADO-INVALIDO</c> si otra operación ganó la competencia (el stock no cambió). Con clave se puede reintentar.
    /// </summary>
    Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary><c>inventario.sucursales.listar</c>: las sucursales para elegir recojo o sucursal preferida (alcance de lectura, sin token).</summary>
    Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.devoluciones.reingresar</c>: suma los impresos de una devolución aceptada a la sucursal receptora (alcance de usuario: lleva el token de la persona que confirma la recepción; DEV-07, AJU-11). <b>Sin clave de idempotencia</b>: la devolución de la ruta identifica el reingreso y repetirlo con la misma carga responde
    /// <c>yaAplicado</c> sin sumar otra vez (AJU-16); con otra carga, <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>. De 1 a 20 líneas con ediciones distintas. Con la misma carga se puede reintentar.
    /// Tiene una <b>implementación por omisión que falla cerrado</b> (sin respuesta): las implementaciones y los dobles que no lo usan siguen compilando y nunca simulan un reingreso; <c>HttpClienteDelInventario</c> lo implementa.
    /// </summary>
    Task<ResultadoDelInventario<ReingresoDeDevolucion>> ReingresarDevolucion(Guid devolucionId, Guid pedidoId, string codigoDeFuente, IReadOnlyList<LineaAReingresar> lineas, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDelInventario<ReingresoDeDevolucion>.SinRespuesta);
}

/// <summary>Una línea de un reingreso por devolución: la edición y la cantidad.</summary>
public sealed record LineaAReingresar(Guid EdicionId, int Cantidad);

/// <summary>El resultado de un reingreso por devolución: la devolución y si ya se había aplicado (no se sumó nada).</summary>
public sealed record ReingresoDeDevolucion(Guid DevolucionId, bool YaAplicado);

// ---------- Reservas (consulta previa, reservar y liberar; contrato v1 de reservas) ----------

/// <summary>Cómo recibe la persona un pedido con impresos (CAR-08).</summary>
public enum TipoDeEntregaDeLaReserva
{
    Recojo,
    Domicilio,
}

/// <summary>Una línea impresa de un carrito o pedido: su identificador (el que asigna el E-commerce), la edición y la cantidad.</summary>
public sealed record LineaDeSolicitud(Guid LineaId, Guid EdicionId, int Cantidad);

/// <summary>
/// La consulta previa de la selección de fuentes de un carrito, sin reservar (SEL-20). Recojo: <see cref="CodigoDeSucursalDeRecojo"/>; domicilio: <see cref="RegionDeEntrega"/>, el identificador de región de la dirección en
/// minúsculas y con guiones.
/// </summary>
public sealed record ConsultaPrevia(TipoDeEntregaDeLaReserva Tipo, string? CodigoDeSucursalDeRecojo, string? RegionDeEntrega, IReadOnlyList<LineaDeSolicitud> Lineas);

/// <summary>La fuente que se seleccionaría para una línea (o la falta de stock), si implica traslado y su plazo en días (<see langword="null"/> es «a confirmar», FUE-13).</summary>
public sealed record SeleccionDeLineaDelInventario(Guid LineaId, Guid EdicionId, bool Disponible, string? CodigoDeFuente, bool RequiereTraslado, int? PlazoEstimadoDias, int DisponibleEnLaRed);

/// <summary>La respuesta de la consulta previa: una entrada por línea impresa; las digitales se ignoran (SEL-20).</summary>
public sealed record RespuestaDeConsultaPrevia(IReadOnlyList<SeleccionDeLineaDelInventario> Lineas, DateTimeOffset ConsultadaEn);

/// <summary>
/// La reserva de los impresos de un pedido (SEL-01): todas las líneas o ninguna. <see cref="PedidoId"/> es el identificador opaco que el E-commerce asigna al iniciar el pago; repetirlo con una reserva pendiente la
/// reemplaza (SEL-16). Expira a los 15 minutos de crearla.
/// </summary>
public sealed record SolicitudDeReservaDeStock(Guid PedidoId, TipoDeEntregaDeLaReserva Tipo, string? CodigoDeSucursalDeRecojo, string? RegionDeEntrega, IReadOnlyList<LineaDeSolicitud> Lineas);

/// <summary>Una línea reservada con la fuente asignada (nunca se divide entre fuentes, SEL-25).</summary>
public sealed record LineaReservada(Guid LineaId, Guid EdicionId, int Cantidad, string Estado, string CodigoDeFuente, bool RequiereTraslado, int? PlazoEstimadoDias);

/// <summary>Un traslado vinculado a un pedido; la fecha estimada es <see langword="null"/> si la ruta no está definida (TRA-21).</summary>
public sealed record TrasladoDeLaReserva(Guid TrasladoId, Guid EdicionId, int Cantidad, string CodigoDeOrigen, string CodigoDeDestino, string Estado, DateOnly? FechaEstimadaDeLlegada);

/// <summary>El resultado de cerrar las líneas de una reserva: por cada una, <c>cerrada</c> ahora o <c>ya-cerrada</c> (el descuento ya estaba aplicado).</summary>
public sealed record CierreDeLaReserva(Guid PedidoId, IReadOnlyList<ResultadoDeCierreDeLinea> Lineas);

/// <summary>El resultado de cerrar una línea: <c>cerrada</c> o <c>ya-cerrada</c>.</summary>
public sealed record ResultadoDeCierreDeLinea(Guid LineaId, string Resultado);

/// <summary>El resultado de extender una reserva (SEL-22, SEL-30): si se extendió y cómo quedó la reserva.</summary>
public sealed record ExtensionDeLaReserva(bool Extendida, ReservaDelInventario Reserva);

/// <summary>La reserva de un pedido tal como la devuelve el Inventario; los traslados solo traen contenido después de confirmarla (SEL-03).</summary>
public sealed record ReservaDelInventario(Guid PedidoId, string Estado, DateTimeOffset CreadaEn, DateTimeOffset? ExpiraEn, IReadOnlyList<LineaReservada> Lineas, IReadOnlyList<TrasladoDeLaReserva> Traslados);

/// <summary>Una línea sin stock suficiente y lo máximo que hay en la red (CAR-04, CAR-27).</summary>
public sealed record LineaSinStockDelInventario(Guid LineaId, Guid EdicionId, int Solicitada, int DisponibleEnLaRed);

/// <summary>Un rechazo por límite: la clave de la política, la espera sugerida y el instante desde el que se puede reintentar. La respuesta es la misma para todos los motivos (SEL-15).</summary>
public sealed record RechazoPorLimiteDelInventario(string Motivo, int EsperaEnSegundos, DateTimeOffset DisponibleDesde);
