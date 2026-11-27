namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>Una línea tal como la guarda la base (solo para rehidratar): todo lo que el agregado necesita, sin validar de nuevo.</summary>
public sealed record DatosPersistidosDeLinea(
    Guid Id,
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
    DateOnly? FechaEstimadaDeLlegada,
    EstadoDeLineaImpresa? EstadoImpreso,
    EstadoDeLineaDigital? EstadoDigital,
    DateTimeOffset? ListoEn,
    DateTimeOffset? EntregadaEn,
    DateTimeOffset? NoRecogidaEn,
    DateTimeOffset? AnuladaEn,
    Guid? EnvioId);

/// <summary>
/// Un pedido tal como lo guarda la base (solo para rehidratar). El contacto llega en sus tres campos y <b>nunca</b> sale en el texto del registro: <see cref="ToString"/> no lo incluye.
/// </summary>
public sealed record DatosPersistidosDePedido(
    Guid Id,
    string Numero,
    Guid CuentaId,
    TipoDeEntregaDelPedido Tipo,
    string? CodigoDeSucursalDeRecojo,
    string? RegionDeEntrega,
    decimal CostoDeEnvio,
    decimal Total,
    EstadoDePedido Estado,
    DateTimeOffset CreadoEn,
    DateTimeOffset? FechaEstadoFinal,
    string? ContactoNombre,
    string? ContactoTelefono,
    string? ContactoDireccion,
    DateTimeOffset? ContactoAnonimizadoEn,
    AceptacionDeTexto Terminos,
    AceptacionDeTexto Privacidad,
    AceptacionDeTexto? ReembolsoDigital,
    IReadOnlyList<DatosPersistidosDeLinea> Lineas,
    IReadOnlyList<EnvioDeSucursal>? EnviosPorSucursal = null)
{
    public override string ToString() => $"DatosPersistidosDePedido {{ Id = {Id} }}";
}

public sealed partial class Pedido
{
    /// <summary>
    /// Rehidrata un pedido desde lo guardado. Un número o un contacto que no cumplen las reglas del Dominio son un dato corrupto: lanza <see cref="InvalidOperationException"/> sin repetir ningún dato personal.
    /// </summary>
    public static Pedido Rehidratar(DatosPersistidosDePedido datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var numero = NumeroDePedido.Crear(datos.Numero) ?? throw new InvalidOperationException("El número del pedido guardado no es válido.");
        ContactoDeEntrega? contacto = null;
        if (datos.ContactoNombre is not null || datos.ContactoTelefono is not null || datos.ContactoDireccion is not null)
        {
            contacto = ContactoDeEntrega.Crear(datos.ContactoNombre, datos.ContactoTelefono, datos.ContactoDireccion)
                ?? throw new InvalidOperationException("El contacto guardado del pedido no es válido.");
        }

        return new Pedido(datos, numero, contacto);
    }
}

public sealed partial class Anulacion
{
    private Anulacion(
        Guid id,
        Guid pedidoId,
        OrigenDeLaAnulacion origen,
        MotivoDeAnulacion motivo,
        IniciadorDeOperacion iniciador,
        decimal envio,
        decimal monto,
        bool hayDigitales,
        IEnumerable<Guid> lineaIds,
        EstadoDeOperacion estado,
        Guid? reembolsoId,
        DateTimeOffset creadaEn,
        DateTimeOffset? completadaEn,
        int intentos,
        DateTimeOffset? proximoIntentoEn,
        IReadOnlyDictionary<PasoDeAnulacion, DateTimeOffset> pasos)
    {
        Id = id;
        PedidoId = pedidoId;
        Origen = origen;
        Motivo = motivo;
        Iniciador = iniciador;
        Envio = envio;
        Monto = monto;
        HayDigitales = hayDigitales;
        _lineaIds = [.. lineaIds];
        Estado = estado;
        ReembolsoId = reembolsoId;
        CreadaEn = creadaEn;
        CompletadaEn = completadaEn;
        Intentos = intentos;
        ProximoIntentoEn = proximoIntentoEn;
        foreach (var (paso, cuando) in pasos)
        {
            _pasos[paso] = cuando;
        }
    }

    /// <summary>Rehidrata una anulación desde su fila y sus líneas (solo la Infraestructura): conserva sus pasos y su estado tal como se guardaron.</summary>
    public static Anulacion Rehidratar(
        Guid id,
        Guid pedidoId,
        OrigenDeLaAnulacion origen,
        MotivoDeAnulacion motivo,
        IniciadorDeOperacion iniciador,
        decimal envio,
        decimal monto,
        bool hayDigitales,
        IEnumerable<Guid> lineaIds,
        EstadoDeOperacion estado,
        Guid? reembolsoId,
        DateTimeOffset creadaEn,
        DateTimeOffset? completadaEn,
        int intentos,
        DateTimeOffset? proximoIntentoEn,
        IReadOnlyDictionary<PasoDeAnulacion, DateTimeOffset> pasos) =>
        new(id, pedidoId, origen, motivo, iniciador, envio, monto, hayDigitales, lineaIds, estado, reembolsoId, creadaEn, completadaEn, intentos, proximoIntentoEn, pasos);
}

public sealed partial class Entrega
{
    private Entrega(Guid id, Guid pedidoId, IniciadorDeOperacion iniciador, EstadoDeOperacion estado, DateTimeOffset creadaEn, int intentos, DateTimeOffset? proximoIntentoEn, IEnumerable<LineaDeEntrega> lineas)
    {
        Id = id;
        PedidoId = pedidoId;
        Iniciador = iniciador;
        Estado = estado;
        CreadaEn = creadaEn;
        Intentos = intentos;
        ProximoIntentoEn = proximoIntentoEn;
        _lineas = [.. lineas];
    }

    /// <summary>Rehidrata una entrega desde su fila y sus líneas con el cierre confirmado de cada una (solo la Infraestructura).</summary>
    public static Entrega Rehidratar(Guid id, Guid pedidoId, IniciadorDeOperacion iniciador, EstadoDeOperacion estado, DateTimeOffset creadaEn, int intentos, DateTimeOffset? proximoIntentoEn, IEnumerable<LineaDeEntrega> lineas) =>
        new(id, pedidoId, iniciador, estado, creadaEn, intentos, proximoIntentoEn, lineas);
}
