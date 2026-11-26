using System.Globalization;

namespace BabelHub.IntegrationTests.Almacen;

/// <summary>Un contenedor de respaldos de R2: días de bloqueo de retención y días tras los que el ciclo de vida lo retira.</summary>
internal sealed record ContenedorDeR2(string Nombre, int BloqueoDias, int ExpiraDias);

/// <summary>Un token de R2 acotado a un contenedor: <c>lectura</c> o <c>lectura-escritura</c> (R2 no ofrece solo crear ni solo listar).</summary>
internal sealed record ConcesionDeR2(string Credencial, string Contenedor, string Permiso);

/// <summary>La definición de <c>infra/almacen/r2/</c> (los respaldos en Cloudflare R2, D26). Es la misma que lee <c>scripts/almacen.sh r2-…</c>.</summary>
internal sealed record PoliticasDeR2(IReadOnlyList<ContenedorDeR2> Contenedores, IReadOnlyList<ConcesionDeR2> Concesiones)
{
    public static string Carpeta() => Path.Combine(PoliticasDelAlmacen.Carpeta(), "r2");

    public static PoliticasDeR2 Cargar() => Cargar(Carpeta());

    public static PoliticasDeR2 Cargar(string carpeta) =>
        new(
            [.. Lineas(Path.Combine(carpeta, "contenedores.txt")).Select(c => new ContenedorDeR2(c[0], int.Parse(c[1], CultureInfo.InvariantCulture), int.Parse(c[2], CultureInfo.InvariantCulture)))],
            [.. Lineas(Path.Combine(carpeta, "credenciales.txt")).Select(c => new ConcesionDeR2(c[0], c[1], c[2]))]);

    private static IEnumerable<string[]> Lineas(string archivo) =>
        File.ReadAllLines(archivo)
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
