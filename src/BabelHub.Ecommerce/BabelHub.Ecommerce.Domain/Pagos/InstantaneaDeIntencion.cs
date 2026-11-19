using System.Text;

namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>Cómo se entrega la compra. <see cref="SinEntrega"/> es la compra solo digital (no hay nada que enviar ni recoger).</summary>
public enum TipoDeEntrega
{
    SinEntrega,
    Recojo,
    Domicilio,
}

/// <summary>Una línea de la compra con su importe (precio unitario por cantidad). Tras desvincular la cuenta (PAG-26) se anulan la edición, el título y el ISBN; quedan los importes.</summary>
public sealed record LineaDeIntencion(Guid LineaId, Guid? EdicionId, bool EsDigital, string? Titulo, string? Isbn, int Cantidad, Importe PrecioUnitario, Importe Importe)
{
    /// <summary>Sin la edición, el título ni el ISBN (PAG-26, PAG-30): un registro estructurado de la línea no los repite.</summary>
    public override string ToString() => $"LineaDeIntencion {{ LineaId = {LineaId}, Cantidad = {Cantidad}, Importe = {Importe} }}";
}

/// <summary>La versión de un texto legal que la cuenta aceptó y cuándo.</summary>
public sealed record TextoAceptado(string Version, DateTimeOffset AceptadoEn);

/// <summary>
/// El contacto de entrega a domicilio: nombre, teléfono y dirección. Es un dato personal que vive en la intención solo hasta que Pedidos guarda su copia (PAG-27) y que nunca viaja a la
/// pasarela ni a los registros (PAG-30): su <c>ToString</c> no muestra nada.
/// </summary>
public sealed record ContactoDeEntrega(string Nombre, string Telefono, string Direccion)
{
    private bool PrintMembers(StringBuilder constructor)
    {
        constructor.Append("(redactado)");
        return true;
    }
}

/// <summary>
/// Lo que Carrito congeló para cobrar (D02): las líneas con su importe, el envío, el total, la entrega, los textos aceptados y el contacto. Es inmutable y se valida al crearla:
/// el total es la suma de las líneas más el envío, una compra con impresos necesita su entrega (a domicilio con contacto y región, o con recojo en una sucursal) y una solo digital
/// no lleva entrega. La huella la calcula la Aplicación (<c>ICalculadorDeHuella</c>) sobre todo lo congelado.
/// </summary>
public sealed class InstantaneaDeIntencion
{
    private InstantaneaDeIntencion(
        Guid cuentaId,
        TipoDeEntrega entrega,
        string? codigoDeSucursalDeRecojo,
        string? regionDeEntrega,
        IReadOnlyList<LineaDeIntencion> lineas,
        Importe envio,
        Importe total,
        string huella,
        TextoAceptado terminos,
        TextoAceptado privacidad,
        TextoAceptado? reembolsoDigital,
        ContactoDeEntrega? contacto)
    {
        CuentaId = cuentaId;
        Entrega = entrega;
        CodigoDeSucursalDeRecojo = codigoDeSucursalDeRecojo;
        RegionDeEntrega = regionDeEntrega;
        Lineas = lineas;
        Envio = envio;
        Total = total;
        Huella = huella;
        Terminos = terminos;
        Privacidad = privacidad;
        ReembolsoDigital = reembolsoDigital;
        Contacto = contacto;
    }

    public Guid CuentaId { get; }

    public TipoDeEntrega Entrega { get; }

    public string? CodigoDeSucursalDeRecojo { get; }

    public string? RegionDeEntrega { get; }

    public IReadOnlyList<LineaDeIntencion> Lineas { get; }

    public Importe Envio { get; }

    public Importe Total { get; }

    public string Huella { get; }

    public TextoAceptado Terminos { get; }

    public TextoAceptado Privacidad { get; }

    /// <summary>El texto de reembolso de los libros digitales; obligatorio si la compra tiene alguno.</summary>
    public TextoAceptado? ReembolsoDigital { get; }

    public ContactoDeEntrega? Contacto { get; }

    public bool TieneImpresos => Lineas.Any(l => !l.EsDigital);

    public static InstantaneaDeIntencion Crear(
        Guid cuentaId,
        TipoDeEntrega entrega,
        string? codigoDeSucursalDeRecojo,
        string? regionDeEntrega,
        IReadOnlyList<LineaDeIntencion> lineas,
        Importe envio,
        Importe total,
        string huella,
        TextoAceptado terminos,
        TextoAceptado privacidad,
        TextoAceptado? reembolsoDigital,
        ContactoDeEntrega? contacto)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(terminos);
        ArgumentNullException.ThrowIfNull(privacidad);

        if (cuentaId == Guid.Empty)
        {
            throw new ArgumentException("La instantánea necesita la cuenta que paga.", nameof(cuentaId));
        }

        if (string.IsNullOrWhiteSpace(huella))
        {
            throw new ArgumentException("La instantánea necesita su huella.", nameof(huella));
        }

        if (lineas.Count == 0)
        {
            throw new ArgumentException("Una compra tiene al menos una línea.", nameof(lineas));
        }

        if (lineas.Select(l => l.LineaId).Distinct().Count() != lineas.Count)
        {
            throw new ArgumentException("Una línea aparece una sola vez en la compra.", nameof(lineas));
        }

        if (lineas.Any(l => l.EdicionId is null || string.IsNullOrWhiteSpace(l.Titulo)))
        {
            throw new ArgumentException("Cada línea de una compra lleva su edición y su título (el ISBN es opcional): solo se anulan al desvincular la cuenta.", nameof(lineas));
        }

        if (envio.Valor > 0m && entrega != TipoDeEntrega.Domicilio)
        {
            throw new ArgumentException("Solo la entrega a domicilio tiene envío.", nameof(envio));
        }

        var suma = Importe.Cero;
        foreach (var linea in lineas)
        {
            if (linea.LineaId == Guid.Empty || linea.Cantidad < 1 || linea.PrecioUnitario.Valor * linea.Cantidad != linea.Importe.Valor)
            {
                throw new ArgumentException("El importe de una línea es su precio unitario por su cantidad.", nameof(lineas));
            }

            suma += linea.Importe;
        }

        if (suma + envio != total || total.Valor <= 0m)
        {
            throw new ArgumentException("El total es la suma de las líneas más el envío y es mayor que cero.", nameof(total));
        }

        var tieneImpresos = lineas.Any(l => !l.EsDigital);
        if (tieneImpresos == (entrega == TipoDeEntrega.SinEntrega))
        {
            throw new ArgumentException("Una compra con impresos necesita entrega y una solo digital no la tiene.", nameof(entrega));
        }

        if (lineas.Any(l => l.EsDigital) && reembolsoDigital is null)
        {
            throw new ArgumentException("Una compra con libros digitales necesita el texto de reembolso aceptado.", nameof(reembolsoDigital));
        }

        switch (entrega)
        {
            case TipoDeEntrega.Domicilio when contacto is null || string.IsNullOrWhiteSpace(regionDeEntrega) || codigoDeSucursalDeRecojo is not null:
                throw new ArgumentException("La entrega a domicilio exige el contacto y la región, y no lleva sucursal de recojo.");
            case TipoDeEntrega.Recojo when string.IsNullOrWhiteSpace(codigoDeSucursalDeRecojo) || regionDeEntrega is not null || contacto is not null:
                throw new ArgumentException("El recojo exige la sucursal y no lleva región ni contacto de entrega.");
            case TipoDeEntrega.SinEntrega when contacto is not null || codigoDeSucursalDeRecojo is not null || regionDeEntrega is not null:
                throw new ArgumentException("Una compra solo digital no lleva datos de entrega.");
        }

        return new InstantaneaDeIntencion(
            cuentaId, entrega, codigoDeSucursalDeRecojo, regionDeEntrega, lineas.ToArray(), envio, total, huella.Trim(), terminos, privacidad, reembolsoDigital, contacto);
    }

    /// <summary>Sin el contacto, la dirección ni los títulos.</summary>
    public override string ToString() => $"InstantaneaDeIntencion {{ Lineas = {Lineas.Count}, Total = {Total}, Entrega = {Entrega} }}";
}
