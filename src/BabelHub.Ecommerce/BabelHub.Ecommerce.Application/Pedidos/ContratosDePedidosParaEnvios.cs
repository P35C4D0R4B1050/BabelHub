using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos;

// Contratos públicos del módulo Pedidos que consume Envíos (regla 13; plan de ecommerce-pedidos §4 y D01 de ecommerce-envios). Pedidos declara `IDespachoDeEnvios` (lo implementa Envíos) y es dueño de los datos que Envíos
// lee y avanza: el contacto de entrega, la lectura del pedido para envíos y el progreso de las líneas. El orden de módulos es Catálogo ← Carrito ← Pagos ← Biblioteca ← Pedidos ← Envíos: Envíos llama hacia abajo, a estos contratos,
// y Pedidos llama a `IDespachoDeEnvios` por un puerto que declara él (sin referencia a Envíos). Ningún modelo lleva precios, datos de pago ni el contacto fuera de `ContactoDeEntrega`, que se redacta al imprimirlo o serializarlo.

// ---------- Lo que Pedidos declara y Envíos implementa ----------

/// <summary>Por qué una línea no entra en un envío (ENV-09): hoy solo las digitales, que no se despachan.</summary>
public enum MotivoDeLineaRechazadaDelEnvio
{
    Digital,
}

/// <summary>Una línea que <c>Crear</c> excluyó del envío y por qué (el envío se crea con las impresas).</summary>
public sealed record LineaRechazadaDelEnvio(Guid LineaId, MotivoDeLineaRechazadaDelEnvio Motivo);

/// <summary>Crear el envío de las líneas listas de un pedido a domicilio que salen de una fuente (ENV-01): solo identificadores y el código de la fuente.</summary>
public sealed record CrearEnvioEntrada(Guid PedidoId, string CodigoDeFuente, IReadOnlyList<Guid> LineaIds);

/// <summary>El envío creado (o el existente, si la repetición lleva las mismas líneas: idempotente por <c>(pedido, fuente, líneas)</c>), las líneas que quedaron en él y las que se rechazaron.</summary>
public sealed record ResultadoDeCrearEnvio(Guid EnvioId, bool YaExistia, IReadOnlyList<Guid> LineaIds, IReadOnlyList<LineaRechazadaDelEnvio> Rechazadas);

/// <summary>Qué pasó con un envío al cancelar el pedido (ENV-13, ENV-19): se cancela si no salió y se mantiene si el despacho ya se inició, está en reparto o entregado (también uno ya cancelado: éxito idempotente).</summary>
public enum DestinoDelEnvioAlCancelar
{
    Cancelado,
    Mantenido,
}

/// <summary>El destino de cada envío del pedido con sus líneas: las de los envíos <see cref="DestinoDelEnvioAlCancelar.Mantenido"/> quedan fuera de la anulación antes de liberar (PED-27).</summary>
public sealed record ResultadoDeCancelarEnvio(Guid EnvioId, DestinoDelEnvioAlCancelar Destino, IReadOnlyList<Guid> LineaIds);

/// <summary>
/// El puerto que Pedidos declara y Envíos implementa (D01 de Envíos, D19 de Pedidos). Ambas operaciones corren <b>dentro de la unidad de trabajo del llamador</b> y con el pedido ya bloqueado (orden de bloqueo pedido → envío): no abren
/// su propia transacción ni toman el bloqueo del pedido. Solo llevan identificadores; para saber si una línea es digital o de qué fuente es, Envíos lee <see cref="IPedidosParaEnvios"/>. Los fallos son <see cref="ErrorDeAplicacion"/> (nunca excepciones de negocio): un <c>Fallo</c> no deja efectos y una excepción posterior al primer efecto se propaga, así que el llamador **revierte toda su transacción** ante cualquiera de los dos. Un envío ya cancelado se informa como <c>Mantenido</c>.
/// </summary>
public interface IDespachoDeEnvios
{
    /// <summary>Crea un envío pendiente de guía con las líneas impresas dadas (ENV-01). Una llamada que mezcla líneas de dos envíos se rechaza completa; si no queda ninguna impresa, no se crea nada.</summary>
    Task<Resultado<ResultadoDeCrearEnvio>> Crear(CrearEnvioEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Cancela los envíos del pedido que no salieron, en orden de identificador, y devuelve el destino de cada uno (ENV-13, ENV-19).</summary>
    Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default);
}

// ---------- Lo que Pedidos declara y Pedidos implementa (lo llama Envíos) ----------

/// <summary>El envío salió: sus líneas pasan a <c>despachada</c> (PED-29).</summary>
public sealed record EnvioEnRepartoEntrada(Guid PedidoId, Guid EnvioId, IReadOnlyList<Guid> LineaIds);

/// <summary>El envío se entregó en <paramref name="Fecha"/>: sus líneas pasan a <c>entregada</c> con esa fecha (PED-29).</summary>
public sealed record EnvioEntregadoEntrada(Guid PedidoId, Guid EnvioId, IReadOnlyList<Guid> LineaIds, DateTimeOffset Fecha);

/// <summary><paramref name="YaAplicado"/> es verdadero si el progreso ya estaba aplicado (repetición): es éxito, no un error.</summary>
public sealed record ProgresoDeEnvioAplicado(bool YaAplicado);

/// <summary>
/// El progreso de los envíos hacia las líneas del pedido. Lo implementa Pedidos y lo llama Envíos <b>después de confirmar su propia transacción</b> (nunca dentro de ella: no hay interbloqueo envío → pedido); ambas operaciones son <b>idempotentes por envío</b>
/// y un trabajo de Envíos las reintenta. <see cref="EnvioEntregado"/> solo se acepta tras <see cref="EnvioEnReparto"/> (si no, es estado inválido).
/// </summary>
public interface IProgresoDeEnvios
{
    Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEnReparto(EnvioEnRepartoEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEntregado(EnvioEntregadoEntrada entrada, CancellationToken cancelacion = default);
}

/// <summary>Los campos del contacto de entrega que pueden faltar (lista cerrada, ENV-10).</summary>
public enum CampoDelContactoDeEntrega
{
    Nombre,
    Telefono,
    Direccion,
    Region,
}

/// <summary>El contacto de entrega de un pedido a domicilio: dato personal, redactado al imprimirlo o serializarlo (nunca en avisos, auditoría, registros ni errores; PED-32, PED-40).</summary>
[JsonConverter(typeof(ConversorDeContactoDeEntrega))]
public sealed class ContactoDeEntrega
{
    public const string Redactado = "[redactado]";

    public ContactoDeEntrega(string nombre, string telefono, string direccion, string region)
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

    public override string ToString() => Redactado;
}

internal sealed class ConversorDeContactoDeEntrega : JsonConverter<ContactoDeEntrega>
{
    public override ContactoDeEntrega Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("El contacto de la entrega no se lee de un texto.");

    public override void Write(Utf8JsonWriter escritor, ContactoDeEntrega valor, JsonSerializerOptions opciones) => escritor.WriteStringValue(ContactoDeEntrega.Redactado);
}

/// <summary>Cómo quedó la lectura del contacto (ENV-10): disponible, con un campo faltante, anonimizado (la cuenta se canceló o pasaron dos años) o no disponible ahora (reintentable: por ejemplo, la auditoría caída).</summary>
public enum EstadoDeLecturaDelContacto
{
    Disponible,
    Faltante,
    Anonimizado,
    NoDisponible,
}

/// <summary>El resultado de leer el contacto: solo <see cref="EstadoDeLecturaDelContacto.Disponible"/> lleva el contacto y solo <see cref="EstadoDeLecturaDelContacto.Faltante"/> el campo.</summary>
public sealed class LecturaDelContactoDeEntrega
{
    private LecturaDelContactoDeEntrega(EstadoDeLecturaDelContacto estado, ContactoDeEntrega? contacto, CampoDelContactoDeEntrega? campoFaltante)
    {
        Estado = estado;
        Contacto = contacto;
        CampoFaltante = campoFaltante;
    }

    public EstadoDeLecturaDelContacto Estado { get; }

    public ContactoDeEntrega? Contacto { get; }

    public CampoDelContactoDeEntrega? CampoFaltante { get; }

    public static LecturaDelContactoDeEntrega Disponible(ContactoDeEntrega contacto)
    {
        ArgumentNullException.ThrowIfNull(contacto);
        return new(EstadoDeLecturaDelContacto.Disponible, contacto, null);
    }

    public static LecturaDelContactoDeEntrega Faltante(CampoDelContactoDeEntrega campo) => new(EstadoDeLecturaDelContacto.Faltante, null, campo);

    public static LecturaDelContactoDeEntrega Anonimizado() => new(EstadoDeLecturaDelContacto.Anonimizado, null, null);

    public static LecturaDelContactoDeEntrega NoDisponible() => new(EstadoDeLecturaDelContacto.NoDisponible, null, null);

    /// <summary>Sin el contacto.</summary>
    public override string ToString() => $"LecturaDelContactoDeEntrega {{ Estado = {Estado} }}";
}

/// <summary>
/// La lectura del contacto de entrega de un pedido para armar la guía. <b>Cada lectura se audita</b> (el pedido y el rol, no el contacto) <b>con el actor de la solicitud</b> y <b>falla cerrada</b>: si no se puede auditar, la lectura es
/// <see cref="EstadoDeLecturaDelContacto.NoDisponible"/>. Un trabajo de «sistema» no puede leerlo. Envíos lo lee en nombre de logística o de la administración central, fuera de toda transacción.
/// </summary>
public interface IContactoDeEntregaDelPedido
{
    Task<LecturaDelContactoDeEntrega> Leer(Guid pedidoId, CancellationToken cancelacion = default);
}

// ---------- El contrato de lectura de Pedidos para Envíos (D01 de Envíos) ----------

/// <summary>Una línea del pedido tal como la necesita Envíos: si es digital, de qué fuente sale (solo los impresos) y si ya está lista. Sin precios, sin edición y sin contacto.</summary>
public sealed record LineaDePedidoParaEnvios(Guid LineaId, bool EsDigital, string? CodigoDeFuente, bool EstaLista);

/// <summary>El pedido para Envíos: su número (para el aviso), la cuenta opaca (la propiedad del titular, ENV-05), si es a domicilio y sus líneas. Sin contacto, precios ni datos de pago.</summary>
public sealed record PedidoParaEnvios(Guid PedidoId, string Numero, Guid CuentaId, bool EsADomicilio, IReadOnlyList<LineaDePedidoParaEnvios> Lineas);

/// <summary>
/// El contrato de lectura de Pedidos para Envíos (D01 de Envíos). Es del dueño de los datos y se llama hacia abajo. <b>No bloquea y no abre su propia transacción</b>: cuando lo usa <c>IDespachoDeEnvios.Crear</c> corre en la unidad de trabajo del
/// llamador y ve las líneas que Pedidos acaba de marcar listas en esa misma transacción; fuera de <c>Crear</c> (la consulta del titular y <c>SolicitarGuia</c>) lee sin bloqueo en otra transacción. Un pedido que no existe es <c>NoEncontrado</c>.
/// </summary>
public interface IPedidosParaEnvios
{
    Task<Resultado<PedidoParaEnvios>> Obtener(Guid pedidoId, CancellationToken cancelacion = default);
}
