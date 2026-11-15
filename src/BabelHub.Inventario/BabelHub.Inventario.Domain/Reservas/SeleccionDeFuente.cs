namespace BabelHub.Inventario.Domain.Reservas;

/// <summary>
/// Una fuente que podría atender una línea, con el vendible ya calculado por la Aplicación (el stock confirmado y bloqueado, con las vencidas liberadas y lo ya asignado a otras líneas descontado en memoria).
/// <see cref="PlazoHaciaElDestinoEnDias"/> es el plazo de ruta de esta fuente hacia el destino de la entrega (en recojo, hacia la sucursal elegida; en domicilio, hacia la fuente más cercana de la región de la
/// dirección): <see langword="null"/> si la ruta no está definida, y esa fuente se trata como la menos cercana (SEL-25).
/// </summary>
public sealed record CandidataDeFuente(Guid FuenteId, string Codigo, int Vendible, bool EsElDestino, bool EsDeLaRegion, int? PlazoHaciaElDestinoEnDias);

/// <summary>Lo que se pide seleccionar: una línea impresa (su cantidad) para una entrega, y las fuentes candidatas con su vendible y sus plazos.</summary>
public sealed record SolicitudDeSeleccion(TipoDeEntrega TipoDeEntrega, int Cantidad, IReadOnlyList<CandidataDeFuente> Candidatas);

/// <summary>
/// El resultado de seleccionar la fuente de una línea: la fuente elegida, si implica un traslado y su plazo estimado en días (<see langword="null"/> es «a confirmar», FUE-13), o la falta de stock
/// (<see cref="FuenteId"/> nulo) con lo que hay en toda la red (CAR-04). Una línea nunca se divide entre fuentes (SEL-25).
/// </summary>
public sealed record Seleccion(Guid? FuenteId, string? Codigo, bool ImplicaTraslado, int? PlazoEnDias, int DisponibleEnLaRed)
{
    public bool HayStock => FuenteId is not null;

    public static Seleccion SinStock(int disponibleEnLaRed) => new(null, null, false, null, disponibleEnLaRed);
}

/// <summary>La regla de selección de fuente, sustituible por configuración sin cambiar el contrato de reservas ni al E-commerce (SEL-10, AC-SEL-10).</summary>
public interface IEstrategiaDeSeleccion
{
    /// <summary>El nombre con el que se elige en la configuración.</summary>
    string Nombre { get; }

    Seleccion Seleccionar(SolicitudDeSeleccion solicitud);
}

/// <summary>
/// La estrategia predeterminada: la fuente más cercana al destino (D02).
/// <list type="bullet">
/// <item><b>Recojo</b> (SEL-02, AC-SEL-23): la sucursal elegida si tiene vendible suficiente; si no, la de menor plazo de ruta hacia ella; la ruta sin definir cuenta como la menos cercana (SEL-25).</item>
/// <item><b>Domicilio</b> (SEL-24, AC-SEL-33): entre las fuentes de la región de la dirección con vendible suficiente, la de mayor vendible y luego la de código menor; si ninguna alcanza, la de menor plazo de ruta hacia una de la
/// región; <b>nunca hay traslado</b>.</item>
/// <item><b>Región sin fuente o plazos iguales</b> (SEL-23, AC-SEL-25): la de mayor vendible y, si empatan, la de código menor.</item>
/// <item>Si ninguna fuente tiene vendible suficiente para la línea entera, falta de stock (la línea no se divide, AC-SEL-34).</item>
/// </list>
/// </summary>
public sealed class SeleccionPorCercania : IEstrategiaDeSeleccion
{
    public const string NombreDeLaEstrategia = "cercania";

    public string Nombre => NombreDeLaEstrategia;

    public Seleccion Seleccionar(SolicitudDeSeleccion solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (solicitud.Cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(solicitud), "La cantidad es mayor que cero.");
        }

        var enLaRed = (int)Math.Min(int.MaxValue, solicitud.Candidatas.Sum(c => (long)Math.Max(0, c.Vendible)));
        var alcanzan = solicitud.Candidatas.Where(c => c.Vendible >= solicitud.Cantidad).ToList();
        if (alcanzan.Count == 0)
        {
            return Seleccion.SinStock(enLaRed);
        }

        return solicitud.TipoDeEntrega == TipoDeEntrega.Recojo ? PorRecojo(alcanzan, enLaRed) : PorDomicilio(alcanzan, enLaRed);
    }

    private static Seleccion PorRecojo(List<CandidataDeFuente> alcanzan, int enLaRed)
    {
        if (alcanzan.FirstOrDefault(c => c.EsElDestino) is { } sucursal)
        {
            return Elegida(sucursal, enLaRed, implicaTraslado: false);
        }

        return Elegida(PorPlazoLuegoVendible(alcanzan).First(), enLaRed, implicaTraslado: true);
    }

    private static Seleccion PorDomicilio(List<CandidataDeFuente> alcanzan, int enLaRed)
    {
        var deLaRegion = alcanzan.Where(c => c.EsDeLaRegion).ToList();
        var elegida = deLaRegion.Count > 0
            ? deLaRegion.OrderByDescending(c => c.Vendible).ThenBy(c => c.Codigo, StringComparer.Ordinal).First()
            : PorPlazoLuegoVendible(alcanzan).First();

        // A domicilio la fuente despacha: aunque no sea de la región, no hay traslado entre sucursales (SEL-24).
        return Elegida(elegida, enLaRed, implicaTraslado: false);
    }

    /// <summary>Menor plazo primero (la ruta sin definir, al final), luego mayor vendible y luego código menor.</summary>
    private static IOrderedEnumerable<CandidataDeFuente> PorPlazoLuegoVendible(IEnumerable<CandidataDeFuente> candidatas) =>
        candidatas
            .OrderBy(c => c.PlazoHaciaElDestinoEnDias is null)
            .ThenBy(c => c.PlazoHaciaElDestinoEnDias ?? int.MaxValue)
            .ThenByDescending(c => c.Vendible)
            .ThenBy(c => c.Codigo, StringComparer.Ordinal);

    private static Seleccion Elegida(CandidataDeFuente fuente, int enLaRed, bool implicaTraslado) =>
        new(fuente.FuenteId, fuente.Codigo, implicaTraslado, implicaTraslado ? fuente.PlazoHaciaElDestinoEnDias : null, enLaRed);
}
