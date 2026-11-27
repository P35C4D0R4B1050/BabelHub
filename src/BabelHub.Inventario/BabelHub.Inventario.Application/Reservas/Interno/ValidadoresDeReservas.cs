using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Los topes de entrada de las operaciones de reservas (plan §6): las líneas del carrito (CAR-38) y la cantidad por línea.</summary>
internal static class LimitesDeReservas
{
    public const int MaximoDeLineas = 30;

    public const int MinimoDeCantidad = 1;

    /// <summary>Tope de unidades por línea: 20 (decisión de la persona, 2026-11-19, DD-16; SEL-40 y AC-SEL-41). Con 30 líneas, una cuenta retiene a lo más 600 unidades (residual de AM01).</summary>
    public const int MaximoDeCantidad = 20;
}

/// <summary>Las reglas de forma de la entrada de reservar y de la consulta previa: cada error lleva el campo y una clave, nunca el valor recibido (SEG-01).</summary>
internal static partial class ReglasDeEntradaDeReservas
{
    [GeneratedRegex(@"^[a-z0-9-]{2,20}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelCodigo();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{16,80}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDeLaClave();

    public const string Duplicadas = "duplicadas";

    /// <summary>
    /// <paramref name="porSucursal"/> (alguna línea declara sucursal) deja al caso de uso la obligación de la sucursal de recojo común, que solo rige si ninguna línea impresa la declara (SEL-42); su formato se
    /// comprueba siempre que venga.
    /// </summary>
    public static void Entrega(List<ErrorDeCampo> errores, TipoDeEntregaDeReserva tipo, string? sucursal, string? region, bool porSucursal)
    {
        if (!Enum.IsDefined(tipo))
        {
            errores.Add(new ErrorDeCampo("tipoDeEntrega", ConstructorDeValidacion.FormatoInvalido));
            return;
        }

        if (tipo == TipoDeEntregaDeReserva.Recojo)
        {
            if (string.IsNullOrEmpty(sucursal) && !(porSucursal && sucursal is null))
            {
                errores.Add(new ErrorDeCampo("codigoDeSucursalDeRecojo", ConstructorDeValidacion.Obligatorio));
            }
            else if (sucursal is not null && !FormaDelCodigo().IsMatch(sucursal))
            {
                errores.Add(new ErrorDeCampo("codigoDeSucursalDeRecojo", ConstructorDeValidacion.FormatoInvalido));
            }

            if (region is not null)
            {
                errores.Add(new ErrorDeCampo("regionDeEntrega", ConstructorDeValidacion.FormatoInvalido));
            }

            return;
        }

        if (string.IsNullOrEmpty(region))
        {
            errores.Add(new ErrorDeCampo("regionDeEntrega", ConstructorDeValidacion.Obligatorio));
        }
        else if (!RegionesDeEntrega.Es(region))
        {
            errores.Add(new ErrorDeCampo("regionDeEntrega", ConstructorDeValidacion.FormatoInvalido));
        }

        if (sucursal is not null)
        {
            errores.Add(new ErrorDeCampo("codigoDeSucursalDeRecojo", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Lineas(List<ErrorDeCampo> errores, IReadOnlyList<LineaSolicitada>? lineas)
    {
        if (lineas is null || lineas.Count == 0)
        {
            errores.Add(new ErrorDeCampo("lineas", ConstructorDeValidacion.Obligatorio));
            return;
        }

        if (lineas.Count > LimitesDeReservas.MaximoDeLineas)
        {
            errores.Add(new ErrorDeCampo("lineas", ConstructorDeValidacion.DemasiadoLargo));
            return;
        }

        if (lineas.Any(l => l is null || l.LineaId == Guid.Empty || l.EdicionId == Guid.Empty))
        {
            errores.Add(new ErrorDeCampo("lineas", ConstructorDeValidacion.FormatoInvalido));
            return;
        }

        if (lineas.Any(l => l.Cantidad is < LimitesDeReservas.MinimoDeCantidad or > LimitesDeReservas.MaximoDeCantidad))
        {
            errores.Add(new ErrorDeCampo("lineas.cantidad", ConstructorDeValidacion.FueraDeRango));
        }

        // La cadena vacía no es «sin sucursal»: es un código mal formado. El error nombra el campo, nunca el valor.
        if (lineas.Any(l => l.CodigoDeSucursal is { } codigo && !FormaDelCodigo().IsMatch(codigo)))
        {
            errores.Add(new ErrorDeCampo("lineas.codigoDeSucursal", ConstructorDeValidacion.FormatoInvalido));
        }

        // Una línea de pedido no se repite nunca; una edición, solo si se declara la sucursal (la venta por lotes). Sin ella evita apartar de más (plan §6); con ella lo decide el caso de uso (SEL-42).
        if (lineas.Select(l => l.LineaId).Distinct().Count() != lineas.Count
            || (!FormaDeLaSolicitud.AlgunaDeclaraSucursal(lineas) && lineas.Select(l => l.EdicionId).Distinct().Count() != lineas.Count))
        {
            errores.Add(new ErrorDeCampo("lineas", Duplicadas));
        }
    }

    public static void Clave(List<ErrorDeCampo> errores, string? clave)
    {
        if (string.IsNullOrEmpty(clave))
        {
            errores.Add(new ErrorDeCampo("idempotencyKey", ConstructorDeValidacion.Obligatorio));
        }
        else if (!FormaDeLaClave().IsMatch(clave))
        {
            errores.Add(new ErrorDeCampo("idempotencyKey", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static ResultadoDeValidacion Resultado(List<ErrorDeCampo> errores) => errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
}

internal sealed class ValidadorDeReservar : IValidador<ReservarEntrada>
{
    public ResultadoDeValidacion Validar(ReservarEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.PedidoId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio));
        }

        ReglasDeEntradaDeReservas.Entrega(errores, entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega, FormaDeLaSolicitud.AlgunaDeclaraSucursal(entrada.Lineas));
        ReglasDeEntradaDeReservas.Lineas(errores, entrada.Lineas);
        ReglasDeEntradaDeReservas.Clave(errores, entrada.ClaveDeIdempotencia);
        return ReglasDeEntradaDeReservas.Resultado(errores);
    }
}

/// <summary>
/// La carga canónica de una solicitud de reservar y su huella SHA-256 (D03): pedido, forma de entrega y líneas ordenadas. Con la misma clave, la misma huella es la misma solicitud y otra huella es un conflicto
/// (SEL-31); el orden de las líneas no cambia la huella. No lleva datos personales.
/// </summary>
internal static class CargaCanonicaDeReservar
{
    public static string Huella(ReservarEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // La sucursal entra solo si se declara: una carga sin ella conserva la huella de antes (D17).
        var lineas = string.Join(
            ';',
            entrada.Lineas.OrderBy(l => l.LineaId).Select(l => $"{l.LineaId:N}:{l.EdicionId:N}:{l.Cantidad}" + (l.CodigoDeSucursal is { } sucursal ? $":{sucursal}" : string.Empty)));
        var canonica = $"reservar|{entrada.PedidoId:N}|{(int)entrada.TipoDeEntrega}|{entrada.CodigoDeSucursalDeRecojo}|{entrada.RegionDeEntrega}|{lineas}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonica)));
    }
}
