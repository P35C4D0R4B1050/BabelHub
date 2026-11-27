using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// La forma de una solicitud de reservar o de consulta previa (SEL-42, D17). Es <b>por sucursal</b> cuando sus líneas impresas declaran la sucursal que las atiende (todas o ninguna): entonces no hay sucursal de recojo
/// común y una edición puede repetirse. Sin ella rige la forma anterior: sucursal común en recojo y edición sin repetir. La sucursal de una línea digital se ignora, así que esto se decide en el caso de uso, cuando ya
/// se conoce el formato de cada edición; el validador de forma le deja esas dos comprobaciones si alguna línea declara sucursal.
/// </summary>
internal static class FormaDeLaSolicitud
{
    private const string CampoDeLaSucursal = "lineas.codigoDeSucursal";

    public static bool AlgunaDeclaraSucursal(IReadOnlyList<LineaSolicitada>? lineas) => lineas?.Any(l => l?.CodigoDeSucursal is not null) == true;

    public static ErrorDeCampo SucursalInexistente { get; } = new(CampoDeLaSucursal, "inexistente");

    /// <summary>El error de forma de las líneas <b>impresas</b>, o <see langword="null"/>; <paramref name="porSucursal"/> dice con qué forma se atienden.</summary>
    public static ErrorDeCampo? Comprobar(TipoDeEntregaDeReserva tipo, string? sucursalDeRecojo, IReadOnlyList<LineaSolicitada> impresas, out bool porSucursal)
    {
        porSucursal = AlgunaDeclaraSucursal(impresas);
        if (porSucursal)
        {
            if (impresas.Any(l => l.CodigoDeSucursal is null))
            {
                return new ErrorDeCampo(CampoDeLaSucursal, ConstructorDeValidacion.Obligatorio);
            }

            return sucursalDeRecojo is null ? null : new ErrorDeCampo("codigoDeSucursalDeRecojo", ConstructorDeValidacion.FormatoInvalido);
        }

        if (impresas.Count == 0)
        {
            return null;
        }

        if (tipo == TipoDeEntregaDeReserva.Recojo && string.IsNullOrEmpty(sucursalDeRecojo))
        {
            return new ErrorDeCampo("codigoDeSucursalDeRecojo", ConstructorDeValidacion.Obligatorio);
        }

        return impresas.Select(l => l.EdicionId).Distinct().Count() == impresas.Count ? null : new ErrorDeCampo("lineas", ReglasDeEntradaDeReservas.Duplicadas);
    }
}
