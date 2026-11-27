using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>Las sucursales de las pruebas del carrito: la principal (la de por omisión) y otras dos.</summary>
internal static class Sucursal
{
    public const string Principal = "sede-central";
    public const string Cusco = "cusco";
    public const string Lima = "lima";
}

/// <summary>Atajos de las pruebas anteriores a la venta por sucursal: una línea por edición en la sucursal principal, con la edición como identificador de la línea.</summary>
internal static class AtajosDeCarrito
{
    public static ResultadoDeLinea Agregar(this CarritoDeDominio carrito, Guid edicionId, bool esDigital, int cantidad, DateTimeOffset ahora) =>
        carrito.Agregar(edicionId, edicionId, esDigital, esDigital ? null : Sucursal.Principal, cantidad, ahora);

    /// <summary>La línea de la edición en el primer carrito que la tenga; si ninguno, un identificador que no es de ninguna línea.</summary>
    public static Guid LineaDe(this EntornoDelCarrito entorno, Guid edicionId) =>
        entorno.Carritos.Todos.SelectMany(c => c.Lineas).FirstOrDefault(l => l.EdicionId == edicionId)?.LineaId ?? edicionId;
}

/// <summary>Atajos de las pruebas que dan un solo importe de envío: se carga a la primera sucursal que atiende y las demás van en cero.</summary>
internal static class AtajosDeEnvio
{
    private static IReadOnlyList<EnvioDeSucursal> Repartir(Checkout checkout, CostoDeEnvio envio) =>
        [.. checkout.SucursalesQueAtienden.Select((sucursal, i) => new EnvioDeSucursal(sucursal, i == 0 ? envio : CostoDeEnvio.Cero))];

    public static TotalDeCompra CalcularTotal(this Checkout checkout, CostoDeEnvio envio) =>
        checkout.CalcularTotal([new EnvioDeSucursal(Sucursal.Principal, envio)]);

    public static ResultadoDeCheckout PrepararPasoAlPago(this Checkout checkout, Guid pedidoIdNuevo, CostoDeEnvio envio, Func<string, string> reserva, Func<string, string> instantanea, CarritoDeDominio carrito, DateTimeOffset ahora) =>
        checkout.PrepararPasoAlPago(pedidoIdNuevo, Repartir(checkout, envio), reserva, instantanea, carrito, ahora);
}

/// <summary>La lista de sucursales que alimenta la ficha y la validación de la sucursal de una línea.</summary>
internal sealed class SucursalesDeCarritoFalsas : IPreferenciaDeSucursal
{
    public List<SucursalDeLaLista> Lista { get; } = [new(Sucursal.Principal, "Sede central"), new(Sucursal.Cusco, "Cusco"), new(Sucursal.Lima, "Lima")];

    /// <summary>Sin ninguna lista conocida.</summary>
    public bool SinLista { get; set; }

    public Task<SucursalesParaElegir> Resolver(string? valorGuardado, CancellationToken cancelacion = default) =>
        Task.FromResult(SinLista
            ? new SucursalesParaElegir(null, [], false, false)
            : new SucursalesParaElegir(Lista.Any(s => s.Codigo == valorGuardado) ? valorGuardado : null, Lista, true, true));

    public Task<string?> Validar(string? codigo, CancellationToken cancelacion = default) =>
        Task.FromResult(!SinLista && Lista.Any(s => s.Codigo == codigo) ? codigo : null);
}

/// <summary>Las tarifas propias en memoria; cuenta las lecturas para comprobar que el paso al pago las lee una sola vez.</summary>
internal sealed class TarifasDeEnvioEnMemoria : IRepositorioDeTarifasDeEnvio, BabelHub.Ecommerce.UnitTests.Catalogo.IInstantanea
{
    public object Capturar() => new Dictionary<string, CostoDeEnvio>(Propias, StringComparer.Ordinal);

    public void Restaurar(object instantanea)
    {
        Propias.Clear();
        foreach (var (sucursal, costo) in (Dictionary<string, CostoDeEnvio>)instantanea)
        {
            Propias[sucursal] = costo;
        }
    }

    public Dictionary<string, CostoDeEnvio> Propias { get; } = new(StringComparer.Ordinal);

    public int Lecturas { get; private set; }

    /// <summary>Se ejecuta una sola vez justo antes de leer: simula un cambio de tarifa concurrente.</summary>
    public Action? AlLeer { get; set; }

    public Task<IReadOnlyDictionary<string, CostoDeEnvio>> Leer(CancellationToken cancelacion = default)
    {
        if (AlLeer is { } accion)
        {
            AlLeer = null;
            accion();
        }

        Lecturas++;
        return Task.FromResult<IReadOnlyDictionary<string, CostoDeEnvio>>(new Dictionary<string, CostoDeEnvio>(Propias, StringComparer.Ordinal));
    }

    public Task<CostoDeEnvio?> Fijar(string codigoDeSucursal, CostoDeEnvio costo, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var anterior = Propias.GetValueOrDefault(codigoDeSucursal);
        Propias[codigoDeSucursal] = costo;
        return Task.FromResult(anterior);
    }
}
