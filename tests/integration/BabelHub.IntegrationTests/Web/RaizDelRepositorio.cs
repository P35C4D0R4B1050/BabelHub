namespace BabelHub.IntegrationTests.Web;

/// <summary>Rutas del repositorio para las pruebas de archivos de la interfaz web.</summary>
internal static class RaizDelRepositorio
{
    public static string Raiz { get; } = BuscarRaiz();

    public static string Api { get; } = Path.Combine(Raiz, "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Api");

    public static string DeLaApi(string relativa) => Path.Combine(Api, relativa.Replace('/', Path.DirectorySeparatorChar));

    private static string BuscarRaiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
