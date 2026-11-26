using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Constancias;

/// <summary>
/// La parte comprobable en el repositorio de PLA-44 (AC-PLA-44): ningún flujo de integración continua, Compose, plantilla de secretos ni
/// generador lleva una credencial de la API de Cloudflare, que es la única que puede cambiar las reglas de retención o de ciclo de vida de los
/// respaldos. Lo demás de la constancia (segundo factor, los tokens de la cuenta, el aviso de facturación y la revisión del uso) se revisa en
/// la cuenta real y se anota en <c>progress/ensayos/</c> (T46).
/// </summary>
public sealed class AdministracionDeR2Tests
{
    private static readonly Regex CredencialDeLaApi = new(@"\b(CLOUDFLARE|CF)_(API_)?(TOKEN|KEY)\b", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static string Raiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-44")]
    public void NiLaIntegracionContinuaNiLoQueLlegaALasVms_LlevaUnaCredencialDeLaApiDeCloudflare()
    {
        var raiz = Raiz();
        var archivos = Directory.EnumerateFiles(Path.Combine(raiz, ".github"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(raiz, "infra"), "*", SearchOption.AllDirectories).Where(a => !a.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
            .Append(Path.Combine(raiz, "scripts", "generar-secretos.sh"))
            .ToList();

        Assert.Contains(archivos, a => a.EndsWith("lanzamiento.yml", StringComparison.Ordinal));
        Assert.Contains(archivos, a => a.EndsWith("secretos-requeridos.txt", StringComparison.Ordinal));
        Assert.Empty(archivos.Where(a => CredencialDeLaApi.IsMatch(File.ReadAllText(a))).Select(a => Path.GetRelativePath(raiz, a)));
    }
}
