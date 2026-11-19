using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Catalogo;

namespace BabelHub.Ecommerce.Application.Carrito;

// Modelos propios del contrato público del módulo Carrito (regla 13): la web, Pagos, Pedidos, Biblioteca e Identidad los usan sin tocar el Dominio, la Infraestructura ni el Interno del
// módulo, y por eso ningún tipo de aquí expone uno de ellos ni de Shared (lo exige una prueba de arquitectura). Son inmutables. Los del Dominio (`Carrito`, `Checkout`, `ContactoDeEntrega`)
// son otros tipos: estos son los que cruzan el borde del módulo. El contacto de la entrega es dato personal y se redacta al imprimirlo o serializarlo (D09, CAR-34).

/// <summary>Cómo se entrega la compra (CAR-07, CAR-08): a domicilio solo con impresos; un carrito solo digital es «digital» y no lleva envío ni sucursal.</summary>
public enum TipoDeEntregaDeCompra
{
    Recojo,
    Domicilio,
    Digital,
}

/// <summary>Los estados vivos del checkout que ve quien llama (los terminales no tienen vista: la compra ya no está en curso).</summary>
public enum EstadoVisibleDelCheckout
{
    EnEdicion,
    Reservando,
    EnPago,
    PagoPendiente,
}

/// <summary>Los textos legales que la persona acepta al pasar al pago, cada uno con su versión y su fecha (CAR-12, CAR-13, CAR-28).</summary>
public enum TextoLegalDeCompra
{
    Terminos,
    Privacidad,
    ReembolsoDigital,
}

/// <summary>Por qué se ajustó una línea al fusionar el carrito de un visitante con el de la cuenta (CAR-24, AC-CAR-22): lista cerrada, sin datos personales.</summary>
public enum MotivoDeAjusteDeAviso
{
    SinStock,
    YaPoseida,
    NoVendible,
    RecortadaAlMaximo,
    DigitalAUnaUnidad,
    TopeDeLineas,
    TopeDeUnidades,
}

public enum TipoDeAvisoDelCarrito
{
    /// <summary>La edición se retiró del catálogo y se quitó de este carrito (CAT-18).</summary>
    EdicionRetirada,

    /// <summary>La fusión cambió una línea (<see cref="AvisoDelCarrito.Motivo"/>).</summary>
    FusionAjustada,
}

/// <summary>Un aviso persistente del carrito que se muestra hasta que la persona lo marque como visto (sin datos personales).</summary>
public sealed record AvisoDelCarrito(Guid Id, TipoDeAvisoDelCarrito Tipo, Guid EdicionId, string Titulo, MotivoDeAjusteDeAviso? Motivo, int? CantidadFinal, DateTimeOffset CreadoEn);

/// <summary>Una línea del carrito tal como la ve la persona: precio vigente, importe y disponibilidad con su frescura (CAR-03, CAR-04). <see cref="Retirada"/> es una línea con retiro pendiente: ya cuenta como retirada.</summary>
public sealed record LineaDelCarritoVista(
    Guid EdicionId,
    string Titulo,
    string? Isbn,
    FormatoDeEdicion Formato,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    DisponibilidadDeEdicion? Disponibilidad,
    bool Retirada);

/// <summary>El carrito de la cuenta o del visitante: sus líneas, el subtotal (sin envío) y los avisos sin ver. <see cref="PreciosNoConfirmados"/> es verdadero cuando el Catálogo no respondió: los precios y el subtotal no se pueden mostrar y ninguna línea se marca como retirada por ese motivo. Una lectura no tiene efectos (CAR-25).</summary>
public sealed record VistaDelCarrito(IReadOnlyList<LineaDelCarritoVista> Lineas, decimal Subtotal, IReadOnlyList<AvisoDelCarrito> Avisos, bool HayUnaCompraEnCurso, bool PreciosNoConfirmados = false);

/// <summary>
/// Quién pide sobre el carrito: con sesión, la cuenta sale del actor y <see cref="CookieDelVisitante"/> es <see langword="null"/>; sin sesión, la cookie opaca <c>__Host-bh-carrito</c> (256 bits) identifica el carrito del
/// visitante. Un identificador de carrito, checkout o cuenta nunca llega de afuera (CAR-35).
/// </summary>
public sealed record SolicitudSobreElCarrito(string? CookieDelVisitante = null)
{
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        constructor.Append("ConCookie = ").Append(CookieDelVisitante is not null);
        return true;
    }
}

public sealed record AgregarLineaEntrada(Guid EdicionId, int Cantidad, string? CookieDelVisitante = null)
{
    /// <summary>La cookie del visitante es una capacidad: no se imprime.</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        constructor.Append("EdicionId = ").Append(EdicionId).Append(", Cantidad = ").Append(Cantidad);
        return true;
    }
}

public sealed record ModificarLineaEntrada(Guid EdicionId, int Cantidad, string? CookieDelVisitante = null)
{
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        constructor.Append("EdicionId = ").Append(EdicionId).Append(", Cantidad = ").Append(Cantidad);
        return true;
    }
}

public sealed record EliminarLineaEntrada(Guid EdicionId, string? CookieDelVisitante = null)
{
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        constructor.Append("EdicionId = ").Append(EdicionId);
        return true;
    }
}

/// <summary>
/// El carrito después de una operación y, si se creó el de un visitante o se rotó la cookie al fusionar, el valor <b>en claro</b> de la cookie nueva (que solo la Presentación escribe) o <see langword="null"/>
/// para borrarla cuando <see cref="BorrarLaCookie"/> es verdadero. El servidor guarda solo su SHA-256.
/// </summary>
public sealed record ResultadoDeCarrito(VistaDelCarrito Carrito, string? CookieNueva, bool BorrarLaCookie)
{
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        constructor.Append("Lineas = ").Append(Carrito.Lineas.Count).Append(", CookieNueva = ").Append(CookieNueva is not null).Append(", BorrarLaCookie = ").Append(BorrarLaCookie);
        return true;
    }
}

/// <summary>Resultado de la fusión al ingresar (CAR-24): el carrito de la cuenta, los ajustes hechos, si se aplazó por una compra en curso (se reintenta en el siguiente <c>POST</c> de la misma sesión y la cookie <b>sigue</b>) y si la Presentación debe borrar la cookie (la fusión se aplicó o ya estaba aplicada, o no había nada que fusionar).</summary>
public sealed record ResultadoDeFusionDeCarrito(VistaDelCarrito Carrito, IReadOnlyList<AvisoDelCarrito> Ajustes, bool Aplazada, bool BorrarLaCookie);

/// <summary>
/// El contacto de la entrega a domicilio (CAR-08, CAR-30): nombre, teléfono móvil peruano, dirección y región. Es dato personal: <c>ToString</c> y la serialización devuelven <c>[redactado]</c> y no se interpola en
/// excepciones ni registros (SEG-10). Es el modelo de entrada y el de la instantánea (distinto del <c>ContactoDeEntrega</c> del Dominio).
/// </summary>
[JsonConverter(typeof(ConversorDeContactoDeCompra))]
public sealed class ContactoDeCompra : IEquatable<ContactoDeCompra>
{
    public ContactoDeCompra(string nombre, string telefono, string direccion, string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(telefono);
        ArgumentException.ThrowIfNullOrWhiteSpace(direccion);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        Nombre = nombre;
        Telefono = telefono;
        Direccion = direccion;
        Region = region;
    }

    public string Nombre { get; }

    public string Telefono { get; }

    public string Direccion { get; }

    public string Region { get; }

    public const string Redactado = "[redactado]";

    public override string ToString() => Redactado;

    public bool Equals(ContactoDeCompra? otro) =>
        otro is not null && Nombre == otro.Nombre && Telefono == otro.Telefono && Direccion == otro.Direccion && Region == otro.Region;

    public override bool Equals(object? obj) => Equals(obj as ContactoDeCompra);

    public override int GetHashCode() => HashCode.Combine(Nombre, Telefono, Direccion, Region);
}

internal sealed class ConversorDeContactoDeCompra : JsonConverter<ContactoDeCompra>
{
    public override ContactoDeCompra Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("El contacto de la entrega no se lee de un texto: es dato personal y solo se escribe redactado.");

    public override void Write(Utf8JsonWriter escritor, ContactoDeCompra valor, JsonSerializerOptions opciones) => escritor.WriteStringValue(ContactoDeCompra.Redactado);
}

/// <summary>Un texto aceptado con la versión que se le mostró y la fecha de la primera aceptación de esa versión.</summary>
public sealed record AceptacionDeCompra(TextoLegalDeCompra Texto, string Version, DateTimeOffset AceptadoEn);

/// <summary>
/// Una línea congelada de la instantánea (D04): el <see cref="LineaId"/> estable que Carrito asignó y que el Inventario y los pedidos reutilizan, el precio y el importe congelados y, de los impresos, la fuente, el
/// traslado y el plazo que se le informó a la persona (<see langword="null"/> en plazo es «a confirmar»).
/// </summary>
public sealed record LineaDeInstantanea(
    Guid LineaId,
    Guid EdicionId,
    bool EsDigital,
    string Titulo,
    string? Isbn,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    string? CodigoDeFuenteInformado,
    bool? RequiereTrasladoInformado,
    int? PlazoInformadoDias);

/// <summary>
/// Lo que Carrito congela al pasar al pago y entrega a Pagos (D04, D19): <b>todo</b> lo que el pedido necesita para existir sin volver a Carrito. Lleva su <see cref="HuellaDeInstantanea"/> (un digest con clave que no
/// expone el teléfono) que Pagos compara y no recalcula. El contacto solo existe si la entrega es a domicilio y está redactado.
/// </summary>
public sealed record InstantaneaDeCompra(
    Guid PedidoId,
    Guid CuentaId,
    TipoDeEntregaDeCompra TipoDeEntrega,
    string? CodigoDeSucursalDeRecojo,
    string? RegionDeEntrega,
    decimal Subtotal,
    decimal Envio,
    decimal Total,
    ContactoDeCompra? Contacto,
    IReadOnlyList<AceptacionDeCompra> Aceptaciones,
    IReadOnlyList<LineaDeInstantanea> Lineas,
    string HuellaDeInstantanea);

// ---------- Checkout ----------

/// <summary>Lo que pide quien entra a la revisión: la sucursal preferida (código, opcional) con la que se muestra cada línea «en su sucursal» o «por traslado» (AC-CAR-04).</summary>
public sealed record IniciarCheckoutEntrada(string? CodigoDeSucursalPreferida = null);

public enum DisponibilidadDeLineaDeCheckout
{
    EnSuSucursal,
    PorTraslado,
    Agotada,
    DisponibleAlInstante,
    NoSePuedeValidar,
}

public sealed record LineaDeRevision(
    Guid LineaId,
    Guid EdicionId,
    string Titulo,
    FormatoDeEdicion Formato,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    DisponibilidadDeLineaDeCheckout Disponibilidad,
    int? PlazoInformadoDias,
    bool PlazoAConfirmar,
    bool Retirada);

/// <summary>El checkout en curso tal como lo ve la persona. <see cref="PedidoId"/> solo viaja en <c>en_pago</c> y <c>pago_pendiente</c>, y en <c>en_pago</c> no cuando una línea tiene retiro pendiente (CAR-10, CAR-26).</summary>
public sealed record VistaDelCheckout(
    EstadoVisibleDelCheckout Estado,
    int Paso,
    int TotalDePasos,
    IReadOnlyList<LineaDeRevision> Lineas,
    TipoDeEntregaDeCompra? TipoDeEntrega,
    string? CodigoDeSucursalDeRecojo,
    string? RegionDeEntrega,
    decimal Subtotal,
    decimal? Envio,
    decimal Total,
    ContactoDeCompra? Contacto,
    IReadOnlyList<AceptacionDeCompra> Aceptaciones,
    IReadOnlyList<TextoLegalDeCompra> TextosPorAceptar,
    DateTimeOffset? ReservaExpiraEn,
    Guid? PedidoId,
    bool CarritoCambio);

/// <summary>La entrega elegida: recojo con el código de una sucursal, domicilio con el contacto (dirección y región incluidas) o solo digital (CAR-07 a CAR-09, CAR-30).</summary>
public sealed record ElegirEntregaEntrada(TipoDeEntregaDeCompra Tipo, string? CodigoDeSucursalDeRecojo, ContactoDeCompra? Contacto, Guid? PedidoId = null);

/// <summary>Un texto con la versión que la persona vio al aceptarlo: se compara con la vigente de Identidad (CAR-28).</summary>
public sealed record TextoVistoDeCompra(TextoLegalDeCompra Texto, string VersionVista);

/// <summary>
/// Las aceptaciones y el consentimiento de WhatsApp (CAR-12, CAR-13, CAR-16, CAR-18): <see cref="HuellaEnmascaradaDelNumero"/> es la huella que se mostró del número de la cuenta; Identidad la compara con el número vigente
/// al guardar. El número nunca llega a Carrito.
/// </summary>
public sealed record RegistrarAceptacionesEntrada(
    IReadOnlyList<TextoVistoDeCompra> Aceptados,
    bool? ConsentimientoDeWhatsApp = null,
    string? HuellaEnmascaradaDelNumero = null,
    Guid? PedidoId = null);

/// <summary>Pasar al pago: el total que la persona vio (solo se compara, D17), las versiones vistas de los textos y el <c>pedidoId</c> oculto cuando el checkout ya lo tiene (detecta una pestaña obsoleta).</summary>
public sealed record PasarAlPagoEntrada(decimal TotalVisto, IReadOnlyList<TextoVistoDeCompra> TextosVistos, Guid? PedidoId = null);

/// <summary>Volver al carrito o reiniciar: el <c>pedidoId</c> oculto cuando el checkout ya lo tiene; si no, se usa el checkout vivo de quien pide.</summary>
public sealed record SalirDelCheckoutEntrada(Guid? PedidoId = null);

/// <summary>Cómo quedó el checkout tras pasar al pago: el <c>pedidoId</c> y el estado (<c>en_pago</c> o <c>pago_pendiente</c>); la Presentación redirige a la pantalla de Pagos.</summary>
public sealed record ResultadoDePasarAlPago(Guid PedidoId, EstadoVisibleDelCheckout Estado);

/// <summary>El checkout vivo de la cuenta cuando Pagos necesita su <c>pedidoId</c>: solo en <c>en_pago</c> y <c>pago_pendiente</c>, y en <c>en_pago</c> no con una línea retirada (D13).</summary>
public sealed record CheckoutVivoParaPagar(Guid PedidoId, EstadoVisibleDelCheckout Estado);

// ---------- Puertos que Carrito declara ----------

public enum ResultadoDeCrearIntencion
{
    /// <summary>Se insertó o se reemplazó la intención (o ya estaba con la misma huella: idempotente).</summary>
    Creada,

    /// <summary>Hay un intento activo y la instantánea es otra u obsoleta: Carrito devuelve el estado actual.</summary>
    Conflicto,

    /// <summary>La intención ya está <c>aprobada</c> o <c>completada</c>: Carrito redirige al estado del pago sin abortar ni liberar la reserva.</summary>
    YaEnCurso,

    /// <summary>La intención está cerrada sin cobro o compensada: Carrito descarta el <c>pedidoId</c> y reinicia.</summary>
    Cerrada,
}

public enum ResultadoDeReabrirIntencion
{
    Reabierta,

    /// <summary>Hay un intento <b>activo</b> (iniciado, pendiente de confirmación o aprobado): no se puede editar.</summary>
    NoCancelable,

    Cerrada,
    NoExiste,
}

public enum ResultadoDeCancelarIntencion
{
    Cancelada,
    Cerrada,
    NoExiste,

    /// <summary>Hay un intento <b>activo</b>: no se aborta ni se vuelve.</summary>
    NoCancelable,
}

/// <summary>
/// Lo que <c>Carrito</c> declara y <c>Pagos</c> implementa (D07, D19): crear, reabrir y cancelar la intención de pago de una compra. Los tres comprueban la propiedad de la intención en la Aplicación de Pagos. «Sin
/// respuesta» es una excepción (la compra falla cerrada y se aborta o se reintenta como indica el plan). Mientras Pagos no exista, la composición usa un doble provisional que falla cerrado (T08).
/// </summary>
public interface ICreadorDeIntencionesDePago
{
    /// <summary>
    /// Idempotente por <c>pedidoId</c> y huella de instantánea: inserta o reemplaza la intención y renueva <c>iniciada_en</c>, <c>vence_en</c> (el menor entre 15 minutos y <paramref name="reservaExpiraEn"/>) y
    /// <c>reserva_expira_en</c>; <see cref="ResultadoDeCrearIntencion"/> dice qué hacer con lo que ya existía.
    /// </summary>
    Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default);

    /// <summary>Toma la intención, la deja <c>abierta</c>, elimina su contacto y la marca obsoleta, de modo que una pestaña vieja no puede pagarla.</summary>
    Task<ResultadoDeReabrirIntencion> Reabrir(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Cierra la intención sin cobro.</summary>
    Task<ResultadoDeCancelarIntencion> Cancelar(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default);
}

public enum EstadoDePosesion
{
    /// <summary>La cuenta posee al menos una de las ediciones consultadas (<see cref="ResultadoDePosesion.Poseidas"/>).</summary>
    Posee,

    NoPosee,

    /// <summary>El tercero no respondió o respondió algo que no se entiende: pasar al pago falla cerrado; agregar y fusionar aceptan la línea.</summary>
    NoSePuedeConfirmar,
}

/// <summary>Qué ediciones de las consultadas posee la cuenta. <see cref="Poseidas"/> solo tiene contenido con <see cref="EstadoDePosesion.Posee"/>.</summary>
public sealed record ResultadoDePosesion
{
    private ResultadoDePosesion(EstadoDePosesion estado, IReadOnlyList<Guid> poseidas)
    {
        Estado = estado;
        Poseidas = poseidas;
    }

    public EstadoDePosesion Estado { get; }

    public IReadOnlyList<Guid> Poseidas { get; }

    public static ResultadoDePosesion NoPosee { get; } = new(EstadoDePosesion.NoPosee, []);

    public static ResultadoDePosesion NoSePuedeConfirmar { get; } = new(EstadoDePosesion.NoSePuedeConfirmar, []);

    public static ResultadoDePosesion Posee(IReadOnlyCollection<Guid> poseidas)
    {
        ArgumentNullException.ThrowIfNull(poseidas);
        return poseidas.Count == 0 ? NoPosee : new ResultadoDePosesion(EstadoDePosesion.Posee, [.. poseidas.Distinct()]);
    }
}

/// <summary>
/// Lo que <c>Carrito</c> declara y <c>Biblioteca</c> implementa (CAR-20): si la cuenta ya posee una edición digital, al agregar (con sesión), fusionar y pasar al pago; Pagos la reutiliza al iniciar el cobro. Mientras
/// Biblioteca no exista, la composición usa un doble provisional que responde «no posee» (D07, R02) con una guarda de arranque que impide registrarlo en producción.
/// </summary>
public interface IPosesionDeEdiciones
{
    Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}

// ---------- Avisos de Pagos al checkout ----------

/// <summary>Qué hizo Carrito con un aviso de Pagos (idempotente por <c>pedidoId</c> y estado).</summary>
public enum ResultadoDelAvisoDePago
{
    Aplicado,

    /// <summary>El checkout ya estaba en el estado destino o posterior: no hizo nada.</summary>
    SinCambios,

    /// <summary>El checkout aún está <c>reservando</c>: Pagos reintenta.</summary>
    AunNo,

    /// <summary>Otro <c>pedidoId</c> (uno descartado) o un checkout que ya no está en ese estado: se ignora.</summary>
    NoAplica,
}

public enum MotivoDePagoResueltoDeCompra
{
    Vencido,
    Rechazado,
    Compensado,
}

/// <summary>
/// Lo que Pagos llama a Carrito <b>después de confirmar</b> su transacción (D13): idempotente y reintentable; <b>la fuente de los instantes es Pagos</b>. Todo aviso lleva el <c>pedidoId</c>: uno rezagado de un pedido
/// descartado no cierra el checkout nuevo. La Presentación no referencia este contrato (lo exige una prueba de arquitectura).
/// </summary>
public interface IProgresoDelCheckout
{
    /// <summary>El cobro quedó pendiente de confirmación (<c>en_pago → pago_pendiente</c>): trae el límite de espera y el instante en que expira la reserva.</summary>
    Task<ResultadoDelAvisoDePago> PagoPendiente(Guid pedidoId, DateTimeOffset limiteEn, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default);

    /// <summary>El pago se aprobó: <c>pagado</c>, elimina el contacto y quita del carrito la cantidad comprada de las líneas pagadas.</summary>
    Task<ResultadoDelAvisoDePago> PagoAprobado(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>El pago pendiente se resolvió sin aprobar: <c>vencido</c> o <c>rechazado</c> con la reserva vigente vuelven a <c>en_pago</c>; con la reserva expirada o <c>compensado</c> pasa a <c>cancelado</c>.</summary>
    Task<ResultadoDelAvisoDePago> PagoResuelto(Guid pedidoId, MotivoDePagoResueltoDeCompra motivo, CancellationToken cancelacion = default);
}
