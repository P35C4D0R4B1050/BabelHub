using System.Diagnostics;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.IntegrationTests.Restauracion;

/// <summary>
/// PLA-19 y AC-PLA-19: el informe de la restauración con los identificadores opacos de las cuentas sale por pantalla y a un archivo
/// local fuera del repositorio; lo único que se versiona en <c>progress/ensayos/</c> son contadores. Esta prueba de constancia lo
/// vigila: el archivo local está excluido por <c>.gitignore</c> y nada de lo versionado en <c>progress/ensayos/</c> contiene un
/// identificador (opaco, de cuenta o personal).
/// </summary>
public sealed partial class ConstanciaDeEnsayosTests
{
    private const string CarpetaDeEnsayos = "progress/ensayos";
    private const string CarpetaLocal = "progress/ensayos/local";

    private static string Raiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    /// <summary>Lo que no debe aparecer en un archivo versionado de ensayos: identificadores opacos (32 a 64 caracteres hexadecimales o con guiones), GUID y correos.</summary>
    internal static List<string> Identificadores(string texto)
    {
        var hallazgos = new List<string>();
        hallazgos.AddRange(Hexadecimal().Matches(texto).Select(m => "identificador opaco o suma"));
        hallazgos.AddRange(Guid().Matches(texto).Select(m => "GUID"));
        hallazgos.AddRange(Correo().Matches(texto).Select(m => "correo"));
        return hallazgos;
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{32,64}\b", RegexOptions.None, 2000)]
    private static partial Regex Hexadecimal();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.None, 2000)]
    private static partial Regex Guid();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.None, 2000)]
    private static partial Regex Correo();

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ElInformeLocalConIdentificadoresEstaExcluidoDelRepositorio()
    {
        var gitignore = File.ReadAllText(Path.Combine(Raiz(), ".gitignore"));

        Assert.Contains(CarpetaLocal + "/", gitignore, StringComparison.Ordinal);

        // Un archivo en esa carpeta no lo versiona git: lo comprueba el propio git, no solo el texto del .gitignore.
        var informacion = new ProcessStartInfo("git", ["check-ignore", "--quiet", CarpetaLocal + "/informe-de-reaplicacion.txt"])
        {
            WorkingDirectory = Raiz(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proceso = Process.Start(informacion)!;
        proceso.WaitForExit();
        Assert.Equal(0, proceso.ExitCode);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void LoVersionadoEnProgressEnsayos_NoContieneIdentificadores()
    {
        var carpeta = Path.Combine(Raiz(), CarpetaDeEnsayos);
        if (!Directory.Exists(carpeta))
        {
            return;
        }

        var hallazgos = Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(Raiz(), f).Replace('\\', '/').StartsWith(CarpetaLocal + "/", StringComparison.Ordinal))
            .SelectMany(f => Identificadores(File.ReadAllText(f)).Select(h => $"{Path.GetRelativePath(Raiz(), f)}: {h}"))
            .ToList();

        Assert.True(hallazgos.Count == 0, string.Join("\n", hallazgos));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void LaConstanciaDetectaLosIdentificadoresSembrados()
    {
        const string Cuenta = "3f2a9c41b8d74e6a9d015c7e2b4f8a60";
        var guid = System.Guid.NewGuid().ToString();

        Assert.NotEmpty(Identificadores($"intencion sin cierre: {Cuenta}"));
        Assert.NotEmpty(Identificadores($"cuenta {guid}"));
        Assert.NotEmpty(Identificadores("lector@correo.pe"));
        Assert.Empty(Identificadores("reaplicadas=2\nya_aplicadas=1\nintenciones_sin_cierre=1\ncierres_sin_intencion=0\nfallidas=0\nconfirmaciones_por_umbral=0"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void LosContadoresDelInforme_SonLoUnicoQueSePuedeVersionarAunqueElInformeTengaIdentificadores()
    {
        const string CuentaA = "3f2a9c41b8d74e6a9d015c7e2b4f8a60";
        const string CuentaB = "9b1e7d52c4a8436fa0e3d6b7c8f12a45";
        var informe = new InformeDeReaplicacion(1, 0, [CuentaB], [], [CuentaA], RequiereConfirmacion: false, ConfirmadaPorUmbral: false);

        Assert.Empty(Identificadores(informe.Contadores()));
        Assert.NotEmpty(Identificadores(informe.Detalle()));
    }
}
