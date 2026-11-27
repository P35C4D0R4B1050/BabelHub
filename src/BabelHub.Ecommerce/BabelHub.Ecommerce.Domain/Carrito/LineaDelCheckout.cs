namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>
/// Lo que Carrito toma del carrito y del Catálogo para armar una línea del checkout: la edición, su título, su ISBN, la cantidad, el precio unitario vigente (el precio lo lee el servidor, D17), la línea del carrito de la que sale
/// y, de un impreso, la sucursal que lo atiende (nulos en una línea anterior a la venta por sucursal).
/// </summary>
public sealed record LineaParaCheckout(Guid EdicionId, bool EsDigital, string Titulo, string Isbn, int Cantidad, decimal PrecioUnitario, Guid? LineaDeCarritoId = null, string? CodigoDeSucursal = null);

/// <summary>
/// Una línea de la compra en curso (D04, R04): el <see cref="LineaId"/> es un GUID v4 <b>estable por (checkout, línea del carrito)</b> —también para las digitales, que no pasan por el Inventario— y es el que viaja a la reserva, a la instantánea y
/// al pedido. Guarda, de los impresos, la fuente, el traslado y el plazo que se le informaron a la persona al elegir la entrega, para compararlos al reservar (CAR-27).
/// </summary>
public sealed class LineaDelCheckout
{
    private LineaDelCheckout(
        Guid lineaId,
        Guid edicionId,
        bool esDigital,
        string titulo,
        string isbn,
        Cantidad cantidad,
        decimal precioUnitario,
        string? codigoDeFuenteInformado,
        bool? requiereTrasladoInformado,
        int? plazoInformadoDias,
        DateTimeOffset? consultadoEn,
        Guid? lineaDeCarritoId,
        string? codigoDeSucursal)
    {
        LineaId = lineaId;
        EdicionId = edicionId;
        EsDigital = esDigital;
        Titulo = titulo;
        Isbn = isbn;
        Cantidad = cantidad;
        PrecioUnitario = precioUnitario;
        CodigoDeFuenteInformado = codigoDeFuenteInformado;
        RequiereTrasladoInformado = requiereTrasladoInformado;
        PlazoInformadoDias = plazoInformadoDias;
        ConsultadoEn = consultadoEn;
        LineaDeCarritoId = lineaDeCarritoId;
        CodigoDeSucursal = codigoDeSucursal;
    }

    public Guid LineaId { get; }

    public Guid EdicionId { get; }

    public bool EsDigital { get; }

    public string Titulo { get; }

    public string Isbn { get; }

    public Cantidad Cantidad { get; }

    public decimal PrecioUnitario { get; }

    /// <summary>La fuente que la consulta previa informó para este impreso; nula si aún no se consultó (o es digital).</summary>
    public string? CodigoDeFuenteInformado { get; }

    public bool? RequiereTrasladoInformado { get; }

    /// <summary>El plazo estimado en días que se informó; nulo si «a confirmar» o si no hay traslado.</summary>
    public int? PlazoInformadoDias { get; }

    public DateTimeOffset? ConsultadoEn { get; }

    /// <summary>La línea del carrito de la que sale; nulo en un checkout anterior a la venta por sucursal.</summary>
    public Guid? LineaDeCarritoId { get; }

    /// <summary>La sucursal que atiende un impreso; nulo en una digital y en un checkout anterior.</summary>
    public string? CodigoDeSucursal { get; }

    public bool EsImpresoSinSucursal => !EsDigital && CodigoDeSucursal is null;

    public decimal Importe => Soles.Importe(PrecioUnitario, Cantidad.Valor);

    /// <summary>Reconstruye una línea desde lo guardado (solo para la Infraestructura): la cantidad ya salió de <see cref="Crear"/>, así que solo se vuelve a validar su rango.</summary>
    public static LineaDelCheckout Restaurar(
        Guid lineaId,
        Guid edicionId,
        bool esDigital,
        string titulo,
        string isbn,
        int cantidad,
        decimal precioUnitario,
        string? codigoDeFuenteInformado,
        bool? requiereTrasladoInformado,
        int? plazoInformadoDias,
        DateTimeOffset? consultadoEn,
        Guid? lineaDeCarritoId = null,
        string? codigoDeSucursal = null) =>
        new(lineaId, edicionId, esDigital, titulo, isbn, esDigital ? Cantidad.Uno : Cantidad.Crear(cantidad), precioUnitario, codigoDeFuenteInformado, requiereTrasladoInformado, plazoInformadoDias, consultadoEn, lineaDeCarritoId, codigoDeSucursal);

    internal static LineaDelCheckout Crear(Guid lineaId, LineaParaCheckout origen)
    {
        ArgumentNullException.ThrowIfNull(origen);
        if (lineaId == Guid.Empty || origen.EdicionId == Guid.Empty)
        {
            throw new ArgumentException("La línea del checkout necesita su identificador y su edición.");
        }

        if (string.IsNullOrWhiteSpace(origen.Titulo) || string.IsNullOrWhiteSpace(origen.Isbn) || origen.PrecioUnitario < 0)
        {
            throw new ArgumentException("La línea del checkout necesita título, ISBN y un precio no negativo.", nameof(origen));
        }

        var cantidad = origen.EsDigital ? Cantidad.Uno : Cantidad.Crear(origen.Cantidad);
        return new LineaDelCheckout(lineaId, origen.EdicionId, origen.EsDigital, origen.Titulo, origen.Isbn, cantidad, origen.PrecioUnitario, null, null, null, null, origen.LineaDeCarritoId, SucursalDe(origen));
    }

    /// <summary>La misma línea (mismo <see cref="LineaId"/> y lo informado) con los datos actuales de la edición.</summary>
    internal LineaDelCheckout ConDatos(LineaParaCheckout origen)
    {
        var nueva = Crear(LineaId, origen);
        return new LineaDelCheckout(LineaId, EdicionId, EsDigital, nueva.Titulo, nueva.Isbn, nueva.Cantidad, nueva.PrecioUnitario, CodigoDeFuenteInformado, RequiereTrasladoInformado, PlazoInformadoDias, ConsultadoEn, nueva.LineaDeCarritoId, nueva.CodigoDeSucursal);
    }

    internal LineaDelCheckout ConLoInformado(string? codigoDeFuente, bool? requiereTraslado, int? plazoDias, DateTimeOffset consultadoEn) =>
        new(LineaId, EdicionId, EsDigital, Titulo, Isbn, Cantidad, PrecioUnitario, codigoDeFuente, requiereTraslado, plazoDias, consultadoEn, LineaDeCarritoId, CodigoDeSucursal);

    /// <summary>
    /// Si esta línea continúa en la que sale del carrito: la misma línea del carrito o, en un checkout anterior a la venta por sucursal (sin ella), la misma edición en la misma sucursal.
    /// </summary>
    internal bool SaleDe(LineaParaCheckout origen) =>
        LineaDeCarritoId is { } deCarrito ? origen.LineaDeCarritoId == deCarrito : EdicionId == origen.EdicionId && CodigoDeSucursal == SucursalDe(origen);

    internal bool MismosDatosQue(LineaParaCheckout origen) =>
        EdicionId == origen.EdicionId && LineaDeCarritoId == origen.LineaDeCarritoId && CodigoDeSucursal == SucursalDe(origen) && EsDigital == origen.EsDigital && Titulo == origen.Titulo && Isbn == origen.Isbn
        && Cantidad.Valor == (origen.EsDigital ? 1 : origen.Cantidad) && PrecioUnitario == origen.PrecioUnitario;

    private static string? SucursalDe(LineaParaCheckout origen) => origen.EsDigital ? null : origen.CodigoDeSucursal;
}
