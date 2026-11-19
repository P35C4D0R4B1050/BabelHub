using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Devoluciones;

// Contratos públicos de Devoluciones (T04; regla 13): los usa la interfaz web y, para la cancelación de la cuenta, Identidad (que define `IPendientesDeCuenta` e `IAplicadorDeCancelacion`; Devoluciones implementa su parte en T15 y T16).
// Modelos propios: ningún tipo del Dominio ni de `Interno`. El motivo y el texto libre viajan como `TextoLibre`, que no se vuelca en `ToString` ni en la serialización (DEV-29, DEV-31).

/// <summary>Un texto libre de la persona (motivo o texto de un reporte): su <c>ToString</c> y su serialización están redactados para que no llegue a un registro, un error ni un aviso (DEV-31). Solo la pantalla lo lee y siempre codificado.</summary>
[DebuggerDisplay("[redactado]")]
[JsonConverter(typeof(ConvertidorDeTextoLibre))]
public sealed class TextoLibre(string? valor)
{
    public const string Redactado = "[redactado]";

    /// <summary>El texto tal como lo escribió la persona, sin validar (la Aplicación lo valida y rechaza sin repetirlo).</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? Valor { get; } = valor;

    public override string ToString() => Redactado;
}

internal sealed class ConvertidorDeTextoLibre : JsonConverter<TextoLibre>
{
    public override TextoLibre Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("El texto de una devolución no se lee de un texto serializado.");

    public override void Write(Utf8JsonWriter escritor, TextoLibre valor, JsonSerializerOptions opciones) => escritor.WriteStringValue(TextoLibre.Redactado);
}

public enum TipoDeLaDevolucion
{
    Impreso,
    Digital,
}

public enum OrigenDeLaDevolucion
{
    Solicitud,
    Reporte,
}

public enum EstadoDeLaLineaDeDevolucion
{
    Solicitada,
    Aprobada,
    Rechazada,
    Recibida,
    Reembolsada,
}

/// <summary>Una línea de una devolución: su estado y su monto (sin el envío). <see cref="ReembolsoPendiente"/> es verdadero mientras una línea recibida (impreso) o aprobada (digital) espera la confirmación de la pasarela (DEV-21).</summary>
public sealed record LineaDeDevolucionVista(Guid LineaId, Guid EdicionId, int Cantidad, decimal Monto, EstadoDeLaLineaDeDevolucion Estado, DateTimeOffset EstadoEn, bool ReembolsoPendiente);

/// <summary>Una devolución para quien la ve. El motivo y el texto solo van para el cliente dueño, la sucursal receptora y la administración; <b>nunca el responsable de un cambio</b> para el cliente (DEV-28).</summary>
public sealed record DevolucionVista(
    Guid DevolucionId,
    Guid PedidoId,
    TipoDeLaDevolucion Tipo,
    OrigenDeLaDevolucion Origen,
    string? CodigoDeSucursalReceptora,
    TextoLibre Motivo,
    TextoLibre? Texto,
    bool Redactada,
    DateTimeOffset CreadaEn,
    IReadOnlyList<LineaDeDevolucionVista> Lineas);

public sealed record PaginaDeDevoluciones(IReadOnlyList<DevolucionVista> Devoluciones, int Pagina, int Tamano, int Total);

/// <summary>La línea que el cliente pide devolver: la del pedido y la cantidad.</summary>
public sealed record LineaPedidaParaDevolver(Guid LineaId, int Cantidad);

/// <summary>Solicitar la devolución de impresos (DEV-01): líneas de un solo tipo, el motivo y la sucursal receptora que eligió el cliente. Sin adjuntos (DEV-29).</summary>
public sealed record SolicitarDevolucionDeImpresoEntrada(Guid PedidoId, IReadOnlyList<LineaPedidaParaDevolver> Lineas, TextoLibre Motivo, string CodigoDeSucursalReceptora);

/// <summary>Pedir el reembolso de un libro digital sin defecto (DEV-04): una línea y el motivo.</summary>
public sealed record PedirReembolsoDeDigitalEntrada(Guid PedidoId, Guid LineaId, TextoLibre Motivo);

/// <summary>Reportar un archivo defectuoso de un acceso vigente (DEV-06): una línea, el motivo y el texto.</summary>
public sealed record ReportarArchivoDefectuosoEntrada(Guid PedidoId, Guid LineaId, TextoLibre Motivo, TextoLibre Texto);

/// <summary>El filtro de las listas: la página y el tamaño (tope de 50).</summary>
public sealed record ListarDevolucionesEntrada(int Pagina = 1, int Tamano = 20);

/// <summary>El filtro de la administración central: por tipo y por si es un reporte, además de la página.</summary>
public sealed record ListarDevolucionesDeLaAdministracionEntrada(TipoDeLaDevolucion? Tipo = null, bool SoloReportes = false, int Pagina = 1, int Tamano = 20);

/// <summary>
/// Lo que el cliente hace con sus devoluciones (DEV-01, DEV-04, DEV-06, DEV-28): solicitar un impreso, pedir el reembolso de un digital, reportar un archivo, ver las suyas. Un pedido ajeno o inexistente responde igual
/// (<c>ECO-NO-ENCONTRADO</c>, con un acceso denegado por intento). Ningún resultado lleva el responsable de un cambio.
/// </summary>
public interface IDevolucionesDelCliente
{
    Task<Resultado<DevolucionVista>> SolicitarImpreso(SolicitarDevolucionDeImpresoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Si procede, la línea queda aprobada por el sistema con su acceso revocado y, si la pasarela confirma, reembolsada; con la pasarela caída queda aprobada con el reembolso pendiente (DEV-09, DEV-21).</summary>
    Task<Resultado<DevolucionVista>> PedirReembolsoDeDigital(PedirReembolsoDeDigitalEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> ReportarArchivoDefectuoso(ReportarArchivoDefectuosoEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<PaginaDeDevoluciones>> ListarMias(ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> ObtenerMia(Guid devolucionId, CancellationToken cancelacion = default);
}

/// <summary>
/// Lo que el encargado de la sucursal receptora y la administración central hacen con las devoluciones (DEV-07, DEV-13, DEV-14, DEV-28): ver, aprobar, rechazar y confirmar la recepción de un impreso, y (solo la administración) ver todas y decidir un reporte.
/// El alcance por sucursal lo comprueba la Aplicación: una devolución de otra sucursal receptora o un reporte visto por un encargado responde <c>ECO-NO-ENCONTRADO</c> con un acceso denegado por intento.
/// </summary>
public interface IGestionDeDevoluciones
{
    Task<Resultado<PaginaDeDevoluciones>> ListarDeLaSucursal(string codigoDeSucursal, ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<PaginaDeDevoluciones>> ListarDeLaAdministracion(ListarDevolucionesDeLaAdministracionEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> Obtener(Guid devolucionId, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> Aprobar(Guid devolucionId, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> Rechazar(Guid devolucionId, CancellationToken cancelacion = default);

    /// <summary>Pide el reingreso al Inventario y, solo si lo logra, pasa las líneas a recibida y registra su reembolso; con el Inventario caído no pasa a recibida y se repite sin reingresar dos veces (DEV-07, DEV-17).</summary>
    Task<Resultado<DevolucionVista>> ConfirmarRecepcion(Guid devolucionId, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> AprobarReporte(Guid devolucionId, CancellationToken cancelacion = default);

    Task<Resultado<DevolucionVista>> RechazarReporte(Guid devolucionId, CancellationToken cancelacion = default);
}
