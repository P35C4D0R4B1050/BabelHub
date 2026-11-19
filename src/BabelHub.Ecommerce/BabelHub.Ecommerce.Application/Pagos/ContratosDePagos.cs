using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pagos;

// Modelos propios del contrato público del módulo Pagos (regla 13): la web, Pedidos, Devoluciones e Identidad los usan sin tocar el Dominio, la Infraestructura ni el Interno. Ninguno lleva tarjeta, token del
// medio (salvo `TokenDelMedioDePago`, redactado) ni contacto de entrega fuera del contrato de la compra aprobada, que va a Pedidos y se redacta al imprimirlo o serializarlo.

/// <summary>El resultado de un cobro que ve el cliente (PAG-05, PAG-06, PAG-07): aprobado, rechazado con su motivo o pendiente de confirmación.</summary>
public enum EstadoDelCobro
{
    Aprobado,
    Rechazado,
    PendienteDeConfirmacion,
}

/// <summary>El motivo de un rechazo, de una lista cerrada (D13): el texto de la pasarela nunca se muestra.</summary>
public enum MotivoDeRechazoDePago
{
    TarjetaRechazada,
    FondosInsuficientes,
    TarjetaVencida,
    CodigoDeSeguridadInvalido,
    YapeRechazado,
    MedioNoPermitido,
    AutenticacionRequerida,
    Otro,
}

/// <summary>Dónde está el pago de una compra (la pantalla de estado, PAG-14): lo que el cliente puede esperar o hacer.</summary>
public enum EstadoDelPagoDeLaCompra
{
    /// <summary>Se puede pagar (la intención está abierta o se rechazó el último intento).</summary>
    ListoParaPagar,

    ConfirmandoElPago,
    Aprobado,

    /// <summary>La compra se completó: hay pedido.</summary>
    Completado,

    /// <summary>Se reembolsó o se cerró sin cobro (venció, se canceló o la reserva expiró).</summary>
    Cerrado,
}

public sealed record EmitirClaveDeIntentoEntrada(Guid PedidoId);

/// <summary>La clave de un intento que el servidor emite al mostrar el formulario, sin escribir nada (D04): un token firmado que lleva la intención, la cuenta, la huella de la instantánea y el total.</summary>
public sealed record ClaveDeIntentoEmitida([property: JsonIgnore] string Token, DateTimeOffset ExpiraEn, decimal Total, DateTimeOffset VenceEn, DateTimeOffset? ReservaExpiraEn)
{
    /// <summary>Sin el token.</summary>
    public override string ToString() => $"ClaveDeIntentoEmitida {{ Total = {Total} }}";
}

/// <summary>Cobrar: la clave del intento, el medio y el identificador que entrega la pasarela (nunca el número de la tarjeta). Su texto no repite ninguno.</summary>
public sealed record CobrarEntrada(Guid PedidoId, [property: JsonIgnore] string ClaveDeIntento, MedioDePago Medio, TokenDelMedioDePago TokenDelMedio)
{
    public override string ToString() => $"CobrarEntrada {{ PedidoId = {PedidoId}, Medio = {Medio} }}";
}

public sealed record ResultadoDelCobroDelCliente(EstadoDelCobro Estado, MotivoDeRechazoDePago? Motivo, Guid IntentoId);

public sealed record EstadoDelPagoVista(EstadoDelPagoDeLaCompra Estado, DateTimeOffset? LimiteEn, DateTimeOffset? ReservaExpiraEn, MotivoDeRechazoDePago? UltimoMotivo, bool PuedeReintentar);

/// <summary>
/// El pago de una compra para la web (PAG-01 a PAG-09): solo el cliente dueño. Cada operación comprueba el alcance en la Aplicación y audita en la misma transacción; ninguna llamada de red va dentro de una
/// transacción (D03).
/// </summary>
public interface IPagos
{
    Task<Resultado<ClaveDeIntentoEmitida>> EmitirClaveDeIntento(EmitirClaveDeIntentoEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDelCobroDelCliente>> Cobrar(CobrarEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDelPagoVista>> ObtenerEstado(Guid pedidoId, CancellationToken cancelacion = default);
}

// ---------- Reembolsos ----------

public enum OrigenDelReembolso
{
    Compensacion,
    Anulacion,
    CancelacionDelCliente,
    Devolucion,
}

public enum EstadoDelReembolso
{
    Pendiente,
    Solicitado,
    Reembolsado,
}

public enum MotivoPendienteDelReembolso
{
    SinRespuesta,
    RechazadoPorPasarela,
    NoSoportado,
}

public sealed record LineaAReembolsar(Guid LineaId, decimal Monto);

/// <summary>Pedir un reembolso (PAG-10, D08): el origen y su clave natural identifican la solicitud; con otro monto sobre la misma clave se rechaza.</summary>
public sealed record RegistrarReembolsoEntrada(Guid PedidoId, OrigenDelReembolso Origen, string ClaveNatural, IReadOnlyList<LineaAReembolsar> Lineas, decimal Envio);

public sealed record ReembolsoRegistrado(Guid ReembolsoId, decimal Monto, EstadoDelReembolso Estado, bool EraRepetido);

public sealed record ReembolsoPendienteVista(Guid Id, OrigenDelReembolso Origen, decimal Monto, EstadoDelReembolso Estado, MotivoPendienteDelReembolso? Motivo, DateTimeOffset CreadoEn, int Intentos);

public sealed record PaginaDeReembolsos(IReadOnlyList<ReembolsoPendienteVista> Reembolsos, int Pagina, int Tamano, int Total);

/// <summary>
/// Los reembolsos de toda la plataforma (D08): Pedidos y Devoluciones los piden por aquí. <c>Registrar</c> solo inserta (dentro de la transacción de quien pide, bloqueando la intención); <c>Procesar</c> llama a la pasarela
/// <b>fuera</b> de transacción.
/// </summary>
public interface IReembolsos
{
    Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default);

    /// <summary>Cuántos reembolsos pendientes (pendientes o solicitados) de un origen tiene una cuenta, sin datos de pago (PAG-24).</summary>
    Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default);

    Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default);

    Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default);

    Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default);
}

/// <summary>Que Pedidos pida eliminar el contacto de la intención tras guardar su copia (PED-35, PAG-27): idempotente.</summary>
public interface ILiberacionDeContactoDePago
{
    Task Liberar(Guid pedidoId, CancellationToken cancelacion = default);
}

/// <summary>El aviso entrante de la pasarela (D20): firma y marca de tiempo se verifican sin red y el aviso solo adelanta la consulta; la aprobación la hace el trabajo de consulta.</summary>
public sealed record AvisoDeLaPasarelaEntrada(ReadOnlyMemory<byte> Cuerpo, string MarcaDeTiempo, string Firma)
{
    public override string ToString() => "AvisoDeLaPasarelaEntrada { (redactado) }";
}

public interface IAvisosDeLaPasarela
{
    /// <summary>Responde también a un duplicado; una firma o marca inválida es «no autenticado» y se registra.</summary>
    Task<Resultado<bool>> Recibir(AvisoDeLaPasarelaEntrada entrada, CancellationToken cancelacion = default);
}

// ---------- Lo que Pagos declara y Pedidos implementa ----------

/// <summary>El contacto de entrega que Pagos entrega a Pedidos al aprobarse una compra a domicilio: dato personal, redactado al imprimirlo o serializarlo (PAG-27, PAG-30).</summary>
[JsonConverter(typeof(ConversorDeContactoDeLaCompra))]
public sealed class ContactoDeLaCompra
{
    public const string Redactado = "[redactado]";

    public ContactoDeLaCompra(string nombre, string telefono, string direccion, string region)
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

internal sealed class ConversorDeContactoDeLaCompra : JsonConverter<ContactoDeLaCompra>
{
    public override ContactoDeLaCompra Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("El contacto de la entrega no se lee de un texto.");

    public override void Write(Utf8JsonWriter escritor, ContactoDeLaCompra valor, JsonSerializerOptions opciones) => escritor.WriteStringValue(ContactoDeLaCompra.Redactado);
}

/// <summary>Una línea de la compra aprobada con, para los impresos, lo que devolvió <c>reservas.confirmar</c>: la fuente, si requiere traslado, el traslado vinculado y su fecha estimada (D02).</summary>
public sealed record LineaDeCompraAprobada(
    Guid LineaId,
    Guid EdicionId,
    bool EsDigital,
    string Titulo,
    string? Isbn,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    string? CodigoDeFuente,
    bool RequiereTraslado,
    Guid? TrasladoId,
    DateOnly? FechaEstimadaDeLlegada);

/// <summary>Lo que Pagos entrega a Pedidos cuando se aprueba una compra (D02): todo lo necesario para crear el pedido y los accesos en una sola transacción. El texto no repite el contacto ni los títulos.</summary>
public sealed record CompraAprobada(
    Guid PedidoId,
    Guid CuentaId,
    TipoDeEntregaDeCompra TipoDeEntrega,
    string? CodigoDeSucursalDeRecojo,
    string? RegionDeEntrega,
    decimal Envio,
    decimal Total,
    ContactoDeLaCompra? Contacto,
    IReadOnlyList<AceptacionDeCompra> Aceptaciones,
    IReadOnlyList<LineaDeCompraAprobada> Lineas)
{
    public override string ToString() => $"CompraAprobada {{ PedidoId = {PedidoId}, Lineas = {Lineas.Count} }}";
}

public enum ResultadoDeConfirmarCompra
{
    Creado,

    /// <summary>La cancelación de la cuenta está en ejecución y prevalece (PAG-32): no se crea el pedido.</summary>
    CuentaEnCancelacion,

    /// <summary>Dos compras se cruzaron y una digital ya la posee la cuenta (PAG-20).</summary>
    DigitalYaPoseida,

    Fallo,
}

/// <summary>
/// Lo que <c>Pagos</c> declara y <c>Pedidos</c> implementa (D01): crea el pedido y pide los accesos a Biblioteca en <b>la misma transacción</b> de Pagos, de modo que una falla revierte ambos y no hay accesos que revocar.
/// Repetir con el mismo <c>pedidoId</c> devuelve <see cref="ResultadoDeConfirmarCompra.Creado"/>.
/// </summary>
public interface IConfirmacionDeCompra
{
    Task<ResultadoDeConfirmarCompra> Confirmar(CompraAprobada compra, CancellationToken cancelacion = default);
}
