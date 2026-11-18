namespace BabelHub.IntegrationTests.Almacen;

/// <summary>Un contenedor del almacén: su retención con bloqueo y cuándo lo vacía el ciclo de vida (<c>null</c> si nunca) y a los cuántos días aborta las subidas multipart incompletas (<c>null</c> si no lo hace).</summary>
internal sealed record ContenedorDelAlmacen(string Nombre, int RetencionDias, bool Bloqueada, int? ExpiraDias, int? SubidasIncompletasDias = null);

/// <summary>Lo que una credencial puede hacer en un contenedor (<c>crear</c>, <c>leer</c>, <c>listar</c>, <c>uso</c>, <c>sobrescribir</c>, <c>borrar</c>).</summary>
internal sealed record ConcesionDelAlmacen(string Credencial, string Contenedor, IReadOnlySet<string> Acciones);

/// <summary>
/// La definición de <c>infra/almacen/</c> (<c>contenedores.txt</c> y <c>credenciales.txt</c>) y el evaluador de qué se permite: todo lo que
/// no consta, se rechaza. Es el mismo modelo que lee <c>scripts/almacen.sh</c>.
/// </summary>
internal sealed class PoliticasDelAlmacen
{
    public static readonly string[] Acciones = ["crear", "leer", "listar", "uso", "sobrescribir", "borrar"];

    private PoliticasDelAlmacen(IReadOnlyList<ContenedorDelAlmacen> contenedores, IReadOnlyList<ConcesionDelAlmacen> concesiones)
    {
        Contenedores = contenedores;
        Concesiones = concesiones;
    }

    public IReadOnlyList<ContenedorDelAlmacen> Contenedores { get; }

    public IReadOnlyList<ConcesionDelAlmacen> Concesiones { get; }

    public IReadOnlyList<string> Credenciales => [.. Concesiones.Select(c => c.Credencial).Distinct(StringComparer.Ordinal)];

    public static string Carpeta()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return Path.Combine(carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio."), "infra", "almacen");
    }

    public static PoliticasDelAlmacen Cargar() => Cargar(Carpeta());

    public static PoliticasDelAlmacen Cargar(string carpeta)
    {
        var contenedores = Lineas(Path.Combine(carpeta, "contenedores.txt"))
            .Select(c => new ContenedorDelAlmacen(c[0], int.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), c[2] == "si", c[3] == "nunca" ? null : int.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture), c.Length > 4 && c[4] != "nunca" ? int.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture) : null))
            .ToList();
        var concesiones = Lineas(Path.Combine(carpeta, "credenciales.txt"))
            .Select(c => new ConcesionDelAlmacen(c[0], c[1], c[2].Split(',').ToHashSet(StringComparer.Ordinal)))
            .ToList();
        return new PoliticasDelAlmacen(contenedores, concesiones);
    }

    private static IEnumerable<string[]> Lineas(string archivo) =>
        File.ReadAllLines(archivo)
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>¿Puede la credencial hacer la acción en el contenedor? Solo si consta; todo lo demás se rechaza.</summary>
    public bool Permite(string credencial, string accion, string contenedor) =>
        Concesiones.Any(c => c.Credencial == credencial && c.Contenedor == contenedor && c.Acciones.Contains(accion));

    public ContenedorDelAlmacen Contenedor(string nombre) => Contenedores.Single(c => c.Nombre == nombre);
}
