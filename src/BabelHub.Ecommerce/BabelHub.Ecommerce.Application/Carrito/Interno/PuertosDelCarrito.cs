using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

// Los puertos propios del módulo Carrito (plan §2): lo externo que la Aplicación necesita y la Infraestructura del módulo implementa. Son internos al módulo (regla 13): ningún otro módulo los usa y,
// por usar tipos del Dominio, no están en la raíz del contrato público. Carrito nunca abre un HttpClient propio: su adaptador del Inventario delega en el cliente de Plataforma (D01 de ecommerce-catalogo).

// ---------- Inventario ----------

/// <summary>Cómo terminó una llamada al Inventario: una falla del Inventario nunca es una excepción, es un estado (SEG-13).</summary>
public enum EstadoDeRespuestaDelInventarioDelCarrito
{
    Exitosa,

    /// <summary>Respondió que no (4xx, 409, 422): <see cref="RespuestaDelInventarioDelCarrito{T}.Codigo"/> trae el <c>INV-…</c> estable.</summary>
    Rechazada,

    /// <summary>429: <see cref="RespuestaDelInventarioDelCarrito{T}.DisponibleDesde"/> trae el instante de reintento (CAR-17).</summary>
    LimiteExcedido,

    /// <summary>Tiempo agotado, corte, cortacircuitos abierto, 5xx o un cuerpo que el contrato no admite.</summary>
    NoDisponible,
}

/// <summary>La respuesta del Inventario con un valor solo cuando fue exitosa. Nunca lleva el texto del error remoto ni el cuerpo (SEG-10).</summary>
public sealed record RespuestaDelInventarioDelCarrito<T>
{
    private readonly T? _valor;

    private RespuestaDelInventarioDelCarrito(EstadoDeRespuestaDelInventarioDelCarrito estado, T? valor, string? codigo, DateTimeOffset? disponibleDesde)
    {
        Estado = estado;
        _valor = valor;
        Codigo = codigo;
        DisponibleDesde = disponibleDesde;
    }

    public EstadoDeRespuestaDelInventarioDelCarrito Estado { get; }

    public bool Exitosa => Estado == EstadoDeRespuestaDelInventarioDelCarrito.Exitosa;

    public string? Codigo { get; }

    public DateTimeOffset? DisponibleDesde { get; }

    public T Valor => Exitosa ? _valor! : throw new InvalidOperationException("El Inventario no respondió con un valor y no hay valor.");

    public static RespuestaDelInventarioDelCarrito<T> ConValor(T valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return new(EstadoDeRespuestaDelInventarioDelCarrito.Exitosa, valor, null, null);
    }

    public static RespuestaDelInventarioDelCarrito<T> Rechazo(string? codigo) => new(EstadoDeRespuestaDelInventarioDelCarrito.Rechazada, default, codigo, null);

    public static RespuestaDelInventarioDelCarrito<T> Limitada(DateTimeOffset? disponibleDesde) => new(EstadoDeRespuestaDelInventarioDelCarrito.LimiteExcedido, default, "INV-LIMITE-EXCEDIDO", disponibleDesde);

    public static RespuestaDelInventarioDelCarrito<T> SinRespuesta { get; } = new(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, default, null, null);
}

/// <summary>El stock vendible de una edición impresa según el Inventario, sin memoria (CAR-05).</summary>
public sealed record StockDeEdicion(Guid EdicionId, int VendibleEnLaRed);

/// <summary>La disponibilidad real de varias ediciones: las que el Inventario no conoce (<c>noRegistradas</c>) se tratan como agotadas (CAR-05).</summary>
public sealed record StockDeVariasEdiciones(IReadOnlyList<StockDeEdicion> Ediciones, IReadOnlyList<Guid> NoRegistradas, DateTimeOffset ConsultadaEn);

/// <summary>Una línea impresa que se consulta o se reserva: el <c>lineaId</c> estable que asigna Carrito (D04).</summary>
public sealed record LineaParaElInventario(Guid LineaId, Guid EdicionId, int Cantidad);

/// <summary>La entrega con la que se consulta o se reserva: recojo con el código de sucursal, o domicilio con la región en minúsculas y con guiones (la misma de la fuente).</summary>
public sealed record EntregaParaElInventario(TipoDeEntrega Tipo, string? CodigoDeSucursalDeRecojo, string? RegionDeEntrega);

/// <summary>Lo que el Inventario seleccionaría o reservó para una línea (nunca se divide entre fuentes): fuente, si implica traslado y su plazo (<see langword="null"/> es «a confirmar», FUE-13).</summary>
public sealed record SeleccionDeLinea(Guid LineaId, Guid EdicionId, bool HayStock, string? CodigoDeFuente, bool RequiereTraslado, int? PlazoEstimadoDias, int DisponibleEnLaRed);

public sealed record ConsultaPreviaDelInventario(IReadOnlyList<SeleccionDeLinea> Lineas, DateTimeOffset ConsultadaEn);

/// <summary>Una sucursal y su región para elegir recojo o domicilio (R05).</summary>
public sealed record SucursalDeEntrega(string Codigo, string Nombre, string Region);

/// <summary>La lista de sucursales con su frescura: la última conocida (hasta 10 minutos) si el Inventario no responde (R05).</summary>
public sealed record ListaDeSucursales(IReadOnlyList<SucursalDeEntrega> Sucursales, DateTimeOffset ObtenidaEn, bool EsDeRespaldo);

/// <summary>Cómo respondió <c>reservas.crear</c> (D05, D18): el plan traduce cada tipo a un error estable o a un aborto.</summary>
public enum TipoDeResultadoDeReservar
{
    /// <summary>La reserva se creó o se devolvió la original de la misma clave (también si ya expiró: comparar <see cref="ReservaObtenida.ExpiraEn"/> con el reloj).</summary>
    Reservada,

    /// <summary><c>INV-STOCK-INSUFICIENTE</c> (409): <see cref="ResultadoDeReservar.SinStock"/> trae las líneas.</summary>
    StockInsuficiente,

    /// <summary><c>INV-IDEMPOTENCIA-EN-CURSO</c> (409): la reserva de la primera solicitud sigue; no se aborta.</summary>
    EnCurso,

    /// <summary><c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>: la clave está mal derivada (alerta técnica).</summary>
    ConflictoDeIdempotencia,

    /// <summary><c>INV-RESERVA-ESTADO-INVALIDO</c>: la reserva del pedido ya expiró, se liberó o se confirmó con otra clave.</summary>
    EstadoInvalido,

    /// <summary>429 (también el cuarto reemplazo del mismo pedido, SEL-36): <see cref="ResultadoDeReservar.DisponibleDesde"/> es el instante de reintento.</summary>
    LimiteExcedido,

    /// <summary>Otro rechazo del contrato (validación, sucursal o región inválidas, alcance).</summary>
    Rechazada,

    NoDisponible,
}

public sealed record LineaSinStock(Guid LineaId, Guid EdicionId, int Solicitada, int DisponibleEnLaRed);

public sealed record ReservaObtenida(Guid PedidoId, DateTimeOffset ExpiraEn, IReadOnlyList<SeleccionDeLinea> Lineas, bool EsRepeticion);

public sealed record ResultadoDeReservar(
    TipoDeResultadoDeReservar Tipo,
    ReservaObtenida? Reserva = null,
    IReadOnlyList<LineaSinStock>? SinStock = null,
    string? Codigo = null,
    DateTimeOffset? DisponibleDesde = null)
{
    public static ResultadoDeReservar Reservada(ReservaObtenida reserva) => new(TipoDeResultadoDeReservar.Reservada, Reserva: reserva);

    public static ResultadoDeReservar Sin(IReadOnlyList<LineaSinStock> lineas) => new(TipoDeResultadoDeReservar.StockInsuficiente, SinStock: lineas);

    public static ResultadoDeReservar De(TipoDeResultadoDeReservar tipo, string? codigo = null, DateTimeOffset? disponibleDesde = null) => new(tipo, Codigo: codigo, DisponibleDesde: disponibleDesde);
}

/// <summary>Cómo respondió <c>reservas.liberar</c> (motivo <c>checkout-cancelado</c> o <c>checkout-reiniciado</c>, alcance de sistema): idempotente.</summary>
public enum ResultadoDeLiberar
{
    /// <summary>Liberada, o ya liberada o expirada: éxito sin cambios (AC-SEL-07).</summary>
    Liberada,

    /// <summary><c>INV-NO-ENCONTRADO</c>: la reserva nunca existió; se trata como liberada.</summary>
    NoExistia,

    /// <summary><c>INV-RESERVA-ESTADO-INVALIDO</c>: el pedido ya está confirmado; no se reintenta y se publica una alerta técnica.</summary>
    PedidoConfirmado,

    /// <summary>Sin respuesta, 429 u otro rechazo: se reintenta como <c>LiberarReservasPendientes</c> y la reserva expira sola.</summary>
    NoSePudo,
}

/// <summary>Con qué alcance se llama al Inventario: el del usuario (con su token) o el de sistema (<c>expiracion-de-reservas</c>, sin token).</summary>
public enum AlcanceDeLaLlamadaAlInventario
{
    OperacionDeUsuario,
    ExpiracionDeReservas,
}

/// <summary>
/// El Inventario visto por Carrito: solo lo que necesita, con modelos propios (sin Shared) y sin excepciones por fallas del Inventario. El cliente de <c>Plataforma</c> pone las credenciales y el token por dentro:
/// Carrito no usa <c>ITokenDelUsuarioDeLaSesion</c> (la regla 9 solo lo permite a Plataforma). Nada de esto se llama dentro de una transacción (D19).
/// </summary>
public interface IInventarioDelCarrito
{
    /// <summary><c>disponibilidad.consultar-varias</c> real, sin memoria, de hasta 50 ediciones impresas (CAR-05).</summary>
    Task<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary><c>reservas.consulta-previa</c>: la fuente y el plazo de cada línea sin reservar (CAR-09); las digitales no se envían.</summary>
    Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default);

    /// <summary><c>sucursales.listar</c> con su frescura; <see langword="null"/> si no hay lista ni respaldo.</summary>
    Task<ListaDeSucursales?> ListarSucursales(CancellationToken cancelacion = default);

    /// <summary><c>reservas.crear</c> con <c>Idempotency-Key = res-&lt;pedidoId&gt;-&lt;generación&gt;</c>: todas las líneas o ninguna; repetirla con la misma clave devuelve la original.</summary>
    Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default);

    /// <summary><c>reservas.liberar</c> de todo el pedido; direcciona por el <c>pedidoId</c> (el vigente o el descartado).</summary>
    Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default);
}

// ---------- Persistencia ----------

/// <summary>
/// Los carritos del módulo (esquema <c>carrito</c>). Cada lectura <c>conBloqueo</c> es <c>SELECT … FOR UPDATE</c> dentro de la transacción activa, en el orden de bloqueo global (D10); sin transacción no se bloquea.
/// El de un visitante se identifica por el SHA-256 de su cookie (32 bytes), nunca por la cookie.
/// </summary>
public interface IRepositorioDeCarritos
{
    Task<CarritoDeDominio?> ObtenerDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default);

    Task<CarritoDeDominio?> ObtenerDeVisitante(byte[] huellaDeLaCookie, bool conBloqueo, CancellationToken cancelacion = default);

    Task<CarritoDeDominio?> Obtener(Guid carritoId, bool conBloqueo, CancellationToken cancelacion = default);

    Task Agregar(CarritoDeDominio carrito, CancellationToken cancelacion = default);

    /// <summary>Guarda los cambios del carrito (líneas incluidas) y sube su versión; un carrito <c>fusionado</c> no admite líneas.</summary>
    Task Guardar(CarritoDeDominio carrito, CancellationToken cancelacion = default);

    /// <summary>Los avisos sin ver de la cuenta, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<AvisoDelCarrito>> AvisosSinVer(Guid cuentaId, CancellationToken cancelacion = default);

    Task AgregarAvisos(Guid cuentaId, IReadOnlyList<AvisoDelCarrito> avisos, CancellationToken cancelacion = default);

    Task MarcarAvisosComoVistos(Guid cuentaId, IReadOnlyCollection<Guid> avisoIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>La constancia de una fusión aplicada (<c>fusiones_aplicadas</c>, única por huella de cookie): hace idempotente la fusión de un segundo dispositivo. Se purga a los 30 días.</summary>
    Task RegistrarFusion(byte[] huellaDeLaCookie, Guid carritoDeCuentaId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Los carritos (de cuenta o de visitante) que tienen alguna de las ediciones, <b>sin bloqueo</b>: lo que necesita el retiro de ediciones para saber qué bloquear después, en orden (D10).</summary>
    Task<IReadOnlyList<CarritoConEdicion>> BuscarConEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}

/// <summary>Un carrito que tiene alguna de las ediciones buscadas: su <c>id</c> y su cuenta (nulo si es de un visitante).</summary>
public sealed record CarritoConEdicion(Guid CarritoId, Guid? CuentaId);

/// <summary>Los checkouts del módulo: a lo más uno vivo por cuenta (índice único parcial). Las lecturas con bloqueo siguen el orden global (D10): checkout antes que carrito.</summary>
public interface IRepositorioDeCheckouts
{
    /// <summary>El checkout vivo de la cuenta (<c>en_edicion</c>, <c>reservando</c>, <c>en_pago</c> o <c>pago_pendiente</c>); <see langword="null"/> si no hay.</summary>
    Task<Checkout?> ObtenerVivoDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default);

    /// <summary>El checkout cuyo <c>pedido_id</c> o <c>pedido_descartado</c> es el dado (vivo o terminado).</summary>
    Task<Checkout?> ObtenerPorPedido(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default);

    /// <summary>Los checkouts vivos de esas cuentas, <b>con bloqueo y por <c>id</c> ascendente</b> (D10): lo que usa el retiro de ediciones, que bloquea todos los checkouts antes que los carritos.</summary>
    Task<IReadOnlyList<Checkout>> ObtenerVivosDeCuentasConBloqueo(IReadOnlyCollection<Guid> cuentaIds, CancellationToken cancelacion = default);

    /// <summary>El estado del checkout vivo de la cuenta <b>sin bloqueo</b>: para decidir si hay que llamar a <c>Reabrir</c> antes de bloquear (D10).</summary>
    Task<EstadoDelCheckout?> LeerEstadoSinBloqueo(Guid cuentaId, CancellationToken cancelacion = default);

    Task Agregar(Checkout checkout, CancellationToken cancelacion = default);

    /// <summary>Los checkouts con una reserva por liberar (<c>reserva_a_liberar_desde</c> con valor), sin bloqueo y como mucho <paramref name="maximo"/>: lo que reintenta <c>LiberarReservasPendientes</c>.</summary>
    Task<IReadOnlyList<PedidoPorLiberar>> ConLiberacionPendiente(int maximo, CancellationToken cancelacion = default);

    /// <summary>Los checkouts <c>reservando</c> desde antes de <paramref name="antesDe"/> (atascados por un corte entre los pasos), sin bloqueo: lo que recoge <c>RecuperarCheckoutsReservando</c>.</summary>
    Task<IReadOnlyList<CheckoutPorRecuperar>> ReservandoDesdeAntesDe(DateTimeOffset antesDe, int maximo, CancellationToken cancelacion = default);

    /// <summary>Guarda el checkout con su versión (concurrencia optimista) y reconstruye sus líneas conservando el <c>lineaId</c> de las que siguen.</summary>
    Task Guardar(Checkout checkout, CancellationToken cancelacion = default);
}

/// <summary>Los identificadores del módulo (D04): GUID v4 aleatorios y la cookie del visitante de 256 bits. Un puerto para que las pruebas sean deterministas.</summary>
public interface IGeneradorDeIdentificadores
{
    Guid NuevoId();

    /// <summary>El valor en claro de la cookie del visitante: 32 bytes aleatorios en base64url sin relleno. Solo la Presentación lo escribe; el servidor guarda su SHA-256.</summary>
    string NuevaCookieDeVisitante();
}

// ---------- Límites de uso ----------

public enum DecisionDeLimiteDelCarrito
{
    Permitido,

    /// <summary>Se pasó del límite: <see cref="ResultadoDeLimiteDelCarrito.ReintentarEn"/> dice desde cuándo.</summary>
    Excedido,

    /// <summary>El almacén de contadores no responde y la política falla cerrada (SEG-26): se responde «servicio no disponible».</summary>
    NoDisponible,
}

public sealed record ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito Decision, DateTimeOffset? ReintentarEn = null)
{
    public static ResultadoDeLimiteDelCarrito Permitido { get; } = new(DecisionDeLimiteDelCarrito.Permitido);

    public static ResultadoDeLimiteDelCarrito NoDisponible { get; } = new(DecisionDeLimiteDelCarrito.NoDisponible);
}

/// <summary>
/// Los límites de uso de Carrito (plan §6, SEG-05, SEG-07, SEG-26), valores iniciales y configurables: <b>crear carritos de visitante</b>, 10 por origen y hora; <b>agregar o cambiar líneas</b>, 60 por cuenta o por origen y
/// minuto; <b>pasar al pago</b>, 10 por cuenta y hora y <b>falla cerrado</b>; volver y reiniciar, 20 por cuenta y hora; revisión y entrega, 30 por cuenta y minuto. Un puerto propio hasta que <c>Plataforma</c> defina las
/// políticas (hallazgo para el carril de plataforma): la Infraestructura lo implementa sobre <c>IControlDeLimites</c> y, mientras tanto, con un doble que falla cerrado en lo que reserva stock.
/// </summary>
public interface ILimitadorDelCarrito
{
    Task<ResultadoDeLimiteDelCarrito> CrearCarritoDeVisitante(CancellationToken cancelacion = default);

    /// <summary>Agregar, cambiar o quitar una línea: por la cuenta si la hay y, si no, por el origen.</summary>
    Task<ResultadoDeLimiteDelCarrito> EditarLineas(Guid? cuentaId, CancellationToken cancelacion = default);

    /// <summary>Revisar el checkout y elegir la entrega: consultan el Inventario sin memoria, 30 por cuenta y minuto.</summary>
    Task<ResultadoDeLimiteDelCarrito> RevisarCheckout(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>Pasar al pago reserva stock: 10 por cuenta y hora y <b>falla cerrado</b>. <paramref name="claveDeRepeticion"/> identifica el pedido en curso: repetir el mismo (un doble clic, otra pestaña) no cuenta otra vez (DD-12).</summary>
    Task<ResultadoDeLimiteDelCarrito> PasarAlPago(Guid cuentaId, string claveDeRepeticion, CancellationToken cancelacion = default);

    /// <summary>Volver al carrito y reiniciar el checkout: 20 por cuenta y hora.</summary>
    Task<ResultadoDeLimiteDelCarrito> VolverOReiniciar(Guid cuentaId, CancellationToken cancelacion = default);
}

// ---------- Auditoría ----------

/// <summary>
/// La auditoría de Carrito (plan §6): en la <b>misma transacción</b> del caso de uso, y si no se puede auditar la operación falla. Sin datos personales, sin la cookie del visitante ni su huella. Es un puerto propio hasta que
/// el catálogo de acciones de auditoría (<c>AccionesAuditables</c>, del carril de auditoría) tenga las de Carrito: la Infraestructura lo implementa sobre <c>IAuditoria</c> (T25). Cada tarea agrega el método de su evento.
/// </summary>
public interface IAuditoriaDelCarrito
{
    /// <summary>Una fusión aplicada: cuántas líneas aportó el visitante y los motivos de los ajustes (sin identificar al visitante).</summary>
    Task FusionAplicada(Guid cuentaId, int lineasDelVisitante, IReadOnlyList<MotivoDeAjusteDeAviso> ajustes, CancellationToken cancelacion = default);

    /// <summary>La baja de ediciones aplicada a los carritos: cuántas ediciones, cuántos carritos afectados y cuántas líneas se quitaron o quedaron anotadas con retiro pendiente (sin identificar a nadie).</summary>
    Task EdicionesRetiradas(int ediciones, int carritosAfectados, int lineasQuitadas, int lineasAnotadas, CancellationToken cancelacion = default);

    /// <summary>El checkout terminó por voluntad de la persona (<c>volver</c> o <c>reiniciar</c>); sin datos personales.</summary>
    Task CheckoutTerminado(string motivo, CancellationToken cancelacion = default);

    /// <summary>Se pasó al pago (la intención creada y el checkout en <c>en_pago</c>): con o sin impresos y el tipo de entrega; sin contacto, sin cuenta ni pedido en el detalle (D13).</summary>
    Task PasoAlPagoIniciado(bool conImpresos, bool aDomicilio, CancellationToken cancelacion = default);
}

/// <summary>
/// <c>IRepositorioDeCarritos.Agregar</c> la lanza cuando el carrito de esa cuenta (o de esa cookie) ya existe (índice único): otro dispositivo lo creó a la vez. La Aplicación <b>rehace la transacción</b> una vez, y esta vez lo
/// encuentra y lo bloquea (AC-CAR-01, AC-CAR-02). Con PostgreSQL una violación de unicidad aborta la transacción, así que no se puede seguir dentro de ella.
/// </summary>
public sealed class CarritoYaExisteException() : Exception("El carrito ya existe: otro dispositivo lo creó a la vez.");

// ---------- Opciones ----------

/// <summary>
/// El costo de envío fijo configurado (<c>Carrito:CostoDeEnvio</c>, CAR-23): soles con dos decimales y no negativo, validado al arrancar. Se lee al elegir el domicilio y se <b>congela</b> al pasar al pago; uno por pedido a
/// domicilio. No es un puerto de un tercero sino una opción tipada: la Infraestructura lee la configuración y la Aplicación solo ve este valor.
/// </summary>
public interface ICostoDeEnvioConfigurado
{
    CostoDeEnvio Valor { get; }
}

/// <summary>
/// <c>IRepositorioDeCheckouts.Agregar</c> la lanza cuando la cuenta ya tiene un checkout vivo (índice único parcial): otra solicitud lo creó a la vez. La Aplicación rehace la lectura y devuelve ese checkout (<c>POST
/// /checkout/iniciar</c> con un checkout vivo devuelve ese checkout).
/// </summary>
public sealed class CheckoutYaExisteException() : Exception("La cuenta ya tiene un checkout vivo: otra solicitud lo creó a la vez.");

// ---------- Consentimiento de avisos por WhatsApp ----------

public enum EstadoDeLaOfertaDeConsentimiento
{
    /// <summary>La cuenta no tiene un número guardado, o Identidad no responde: no se ofrece (se ofrece solo si ya hay un número, CAR-18).</summary>
    NoSeOfrece,

    /// <summary>La cuenta tiene un número y aún no consiente: se ofrece, mostrando la huella enmascarada.</summary>
    SeOfrece,

    /// <summary>La cuenta ya consiente: no se vuelve a ofrecer.</summary>
    YaConsiente,
}

/// <summary>Lo que se ofrece en el checkout: el estado y, si se ofrece, la huella enmascarada del número (nunca el número, que no llega a Carrito).</summary>
public sealed record OfertaDeConsentimiento(EstadoDeLaOfertaDeConsentimiento Estado, string? HuellaEnmascarada)
{
    public static OfertaDeConsentimiento NoSeOfrece { get; } = new(EstadoDeLaOfertaDeConsentimiento.NoSeOfrece, null);
}

/// <summary>
/// El consentimiento de avisos por WhatsApp (CAR-18, IDE-31, IDE-32): lo guarda <c>identidad-seguridad</c> y <b>no se exige</b> para pagar. Un puerto propio de Carrito mientras el contrato de Identidad no tenga una operación que reciba la
/// huella enmascarada en vez del número (el número nunca llega a Carrito, a sus registros ni a su auditoría): Identidad compara la huella con el número vigente al guardar. Una falla no bloquea el pago ni dice «guardado».
/// </summary>
public interface IConsentimientoDeAvisos
{
    /// <summary>Si se ofrece el consentimiento y la huella enmascarada del número de la cuenta; ante cualquier falla, <see cref="OfertaDeConsentimiento.NoSeOfrece"/>.</summary>
    Task<OfertaDeConsentimiento> Consultar(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary><see langword="true"/> solo si Identidad confirmó que guardó el consentimiento para el número al que se refiere la huella; <see langword="false"/> ante cualquier falla, cambio de número o respuesta no entendida.</summary>
    Task<bool> Guardar(Guid cuentaId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default);
}

// ---------- Marca de cancelación de la cuenta ----------

public enum EstadoDeLaMarcaDeCancelacion
{
    /// <summary>La cuenta está activa y sin cancelación en marcha.</summary>
    Libre,

    /// <summary>Tiene una cancelación diferida (IDE-27, CAR-31).</summary>
    Diferida,

    /// <summary>La cancelación está en ejecución (IDE-71): no se inicia el pago.</summary>
    EnEjecucion,

    /// <summary>La cuenta ya se canceló o no existe.</summary>
    Ejecutada,
}

/// <summary>
/// La marca de cancelación de la cuenta leída <b>con bloqueo</b> dentro de la transacción de pasar al pago (D10: es lo primero que se bloquea): la cancelación de una cuenta y el paso a <c>reservando</c> se serializan, de modo que
/// una cancelación que empezó entre la revisión y el pago impide iniciarlo (AC-CAR-27). La implementa Identidad (T26b); mientras tanto, un doble que falla cerrado. Si no se puede leer, lanza: nunca se asume libre.
/// </summary>
public interface IMarcaDeCancelacion
{
    Task<EstadoDeLaMarcaDeCancelacion> LeerConBloqueo(Guid cuentaId, CancellationToken cancelacion = default);
}

// ---------- Alertas técnicas ----------

public enum TipoDeAlertaDelCarrito
{
    /// <summary><c>liberar</c> respondió <c>INV-RESERVA-ESTADO-INVALIDO</c> con un pedido que ya está confirmado: no se reintenta y se limpia la anotación.</summary>
    LiberacionDeUnPedidoConfirmado,

    /// <summary>El Inventario respondió <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>: la clave de reserva está mal derivada (un defecto).</summary>
    ClaveDeReservaEnConflicto,

    /// <summary>Hay checkouts con el pago pendiente más de 45 minutos (AUD-31): Pagos no los resolvió.</summary>
    CheckoutsConElPagoPendienteDemasiado,

    /// <summary>Un trabajo de fondo de Carrito no corre desde hace más de 24 horas (AUD-31).</summary>
    TrabajoSinCorrer,
}

/// <summary>Las alertas técnicas de Carrito (<c>AUD-31</c>): sin datos personales, solo el tipo y un conteo. La Infraestructura (T26b) las publica por el canal de alertas de Auditoría.</summary>
public interface IAlertasDelCarrito
{
    Task Publicar(TipoDeAlertaDelCarrito tipo, int cantidad, CancellationToken cancelacion = default);
}

/// <summary>Un pedido cuya reserva falta por liberar (el <c>pedido_descartado</c> o el <c>pedido_id</c> del checkout).</summary>
public sealed record PedidoPorLiberar(Guid CheckoutId, Guid PedidoId);

/// <summary>Un checkout <c>reservando</c> atascado: la cuenta, el pedido y la generación leídos, de modo que el aborto sea condicional a ellos (no toca uno que ya pasó a <c>en_pago</c>).</summary>
public sealed record CheckoutPorRecuperar(Guid CuentaId, Guid PedidoId, int Generacion, bool IntencionPudoCrearse);
