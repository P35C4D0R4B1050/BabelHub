using System.Diagnostics.CodeAnalysis;

namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>
/// El costo de envío fijo de un pedido a domicilio (CAR-23, D11): soles, no negativo y con a lo más dos decimales, hasta el máximo de una columna <c>numeric(12,2)</c>. Se lee de la configuración al elegir domicilio y se
/// <b>congela</b> al pasar al pago.
/// </summary>
public sealed class CostoDeEnvio
{
    public const decimal Maximo = 9_999_999_999.99m;

    private CostoDeEnvio(decimal valor) => Valor = valor;

    public static CostoDeEnvio Cero { get; } = new(0.00m);

    public decimal Valor { get; }

    public static bool TryCrear(decimal soles, [NotNullWhen(true)] out CostoDeEnvio? costo)
    {
        costo = soles >= 0 && soles <= Maximo && decimal.Round(soles, Soles.Decimales) == soles ? new CostoDeEnvio(soles + 0.00m) : null;
        return costo is not null;
    }

    public static CostoDeEnvio Crear(decimal soles) =>
        TryCrear(soles, out var costo) ? costo : throw new ArgumentOutOfRangeException(nameof(soles), "El costo de envío no es una cantidad en soles válida.");
}

/// <summary>El precio unitario y la cantidad de una línea, para calcular el total (el precio lo lee el servidor del Catálogo).</summary>
public sealed record ImporteDeLinea(decimal PrecioUnitario, int Cantidad)
{
    public decimal Importe => Soles.Importe(PrecioUnitario, Cantidad);
}

/// <summary>
/// El total de una compra (CAR-23, D17): la suma de los importes de las líneas (cada uno a dos decimales, alejándose de cero) más el envío fijo <b>solo si la entrega es a domicilio</b> (uno por pedido); el recojo y lo digital
/// no llevan envío. Es lo que se congela al pasar al pago y lo que se compara con el total que la persona vio.
/// </summary>
public sealed class TotalDeCompra
{
    private TotalDeCompra(decimal subtotal, decimal envio)
    {
        Subtotal = subtotal;
        Envio = envio;
        Total = Soles.Redondear(subtotal + envio);
    }

    public decimal Subtotal { get; }

    public decimal Envio { get; }

    public decimal Total { get; }

    public static TotalDeCompra Calcular(IEnumerable<ImporteDeLinea> lineas, TipoDeEntrega entrega, CostoDeEnvio costoConfigurado)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(costoConfigurado);
        var subtotal = lineas.Sum(l => l.Importe);
        return new TotalDeCompra(subtotal, entrega == TipoDeEntrega.Domicilio ? costoConfigurado.Valor : 0m);
    }
}

/// <summary>Lo que cobra de envío a domicilio una sucursal que atiende alguna línea (CAR-23).</summary>
public sealed record EnvioDeSucursal(string CodigoDeSucursal, CostoDeEnvio Costo);

/// <summary>
/// El envío a domicilio (CAR-23, CAR-47): uno por cada sucursal que atiende alguna línea, incluida la primera, aunque tenga varias líneas; cada una cobra su costo propio o, sin él, el predeterminado. El envío de la compra es la suma.
/// </summary>
public static class EnvioPorSucursales
{
    public static IReadOnlyList<EnvioDeSucursal> Desglose(IEnumerable<string> sucursales, IReadOnlyDictionary<string, CostoDeEnvio> propias, CostoDeEnvio predeterminado)
    {
        ArgumentNullException.ThrowIfNull(sucursales);
        ArgumentNullException.ThrowIfNull(propias);
        ArgumentNullException.ThrowIfNull(predeterminado);
        return [.. sucursales.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(s => new EnvioDeSucursal(s, propias.TryGetValue(s, out var propio) ? propio : predeterminado))];
    }

    public static CostoDeEnvio Suma(IEnumerable<EnvioDeSucursal> desglose)
    {
        ArgumentNullException.ThrowIfNull(desglose);
        return CostoDeEnvio.Crear(desglose.Sum(e => e.Costo.Valor));
    }
}
