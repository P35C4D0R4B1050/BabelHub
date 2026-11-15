namespace BabelHub.Inventario.Application.Fuentes;

/// <summary>
/// Las regiones que reconoce el Inventario: los códigos en minúsculas, sin tildes y con guiones de las 24 regiones del Perú y la Provincia Constitucional del Callao, los mismos que usa el E-commerce para la región de una
/// dirección (ECO-0 D08). Lo usan <c>Fuentes</c> para validar la región de una fuente y <c>Reservas</c> para validar la región de entrega de un pedido a domicilio (una región conocida puede no tener fuente, SEL-23). La
/// lista no estaba fijada en ningún documento: es un supuesto registrado en <c>inventario-fuentes/decisiones-de-implementacion.md</c> que debe conciliarse con el E-commerce.
/// </summary>
public static class RegionesDeEntrega
{
    private static readonly HashSet<string> Codigos = new(StringComparer.Ordinal)
    {
        "amazonas", "ancash", "apurimac", "arequipa", "ayacucho", "cajamarca", "callao", "cusco", "huancavelica", "huanuco", "ica", "junin", "la-libertad", "lambayeque",
        "lima", "loreto", "madre-de-dios", "moquegua", "pasco", "piura", "puno", "san-martin", "tacna", "tumbes", "ucayali",
    };

    public static bool Es(string? region) => region is not null && Codigos.Contains(region);

    public static IReadOnlyCollection<string> Todas => Codigos;
}
