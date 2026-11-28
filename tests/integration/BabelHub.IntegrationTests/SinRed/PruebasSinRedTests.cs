using System.Diagnostics;
using System.Text;

namespace BabelHub.IntegrationTests.SinRed;

/// <summary>
/// AC-PLA-01: las pruebas de Dominio y Aplicación se ejecutan en un entorno sin acceso a la red ni a una base de datos
/// (<c>scripts/pruebas-sin-red.sh</c>, con <c>docker run --network none</c>): si todas son autónomas pasan, y si una intenta conectarse,
/// la ejecución falla. Se prueba el mecanismo con dos proyectos de pruebas de muestra que se compilan en cada ejecución. La ejecución
/// real con las pruebas unitarias de los dos servicios la hace el flujo de CI con el mismo script.
/// </summary>
public sealed class PruebasSinRedTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "sin-red-" + Guid.NewGuid().ToString("N"));

    public PruebasSinRedTests()
    {
        Directory.CreateDirectory(_carpeta);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza de una carpeta temporal: no debe hacer fallar la prueba.
        }
    }

    private static string RutaDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string Bash()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidato in new[] { @"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files (x86)\Git\bin\bash.exe" })
            {
                if (File.Exists(candidato))
                {
                    return candidato;
                }
            }
        }

        return "bash";
    }

    private static (int Codigo, string Salida) Ejecutar(string programa, IEnumerable<string> argumentos, string directorio)
    {
        var informacion = new ProcessStartInfo(programa)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directorio,
        };
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = new StringBuilder();
        proceso.OutputDataReceived += (_, e) => { lock (salida) { salida.AppendLine(e.Data); } };
        proceso.ErrorDataReceived += (_, e) => { lock (salida) { salida.AppendLine(e.Data); } };
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
        if (!proceso.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException($"{programa} {string.Join(' ', argumentos)} no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, salida.ToString());
    }

    /// <summary>Compila un proyecto de pruebas de muestra y devuelve la ruta de su ensamblado.</summary>
    private string Compilar(string nombre, string pruebas)
    {
        var proyecto = Path.Combine(_carpeta, nombre);
        Directory.CreateDirectory(proyecto);
        File.WriteAllText(
            Path.Combine(proyecto, nombre + ".csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
              </ItemGroup>
              <ItemGroup>
                <Using Include="Xunit" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(proyecto, "Pruebas.cs"), pruebas);

        var (codigo, salida) = Ejecutar("dotnet", ["build", "--nologo", "-c", "Debug"], proyecto);
        Assert.True(codigo == 0, salida);
        return Path.Combine(proyecto, "bin", "Debug", "net10.0", nombre + ".dll");
    }

    private static (int Codigo, string Salida) EjecutarSinRed(string ensamblado) =>
        Ejecutar(Bash(), [Path.Combine("scripts", "pruebas-sin-red.sh").Replace('\\', '/'), ensamblado.Replace('\\', '/')], RutaDelRepositorio());

    [Fact]
    [Trait("Criterio", "AC-PLA-01")]
    [Trait("Categoria", Categorias.Lanzamiento)]
    public void LasPruebasAutonomas_PasanSinRed()
    {
        var ensamblado = Compilar(
            "Autonomas",
            """
            namespace Autonomas;

            public class Pruebas
            {
                [Fact]
                public void Suma() => Assert.Equal(4, 2 + 2);

                [Theory]
                [InlineData("a")]
                [InlineData("b")]
                public void Texto(string valor) => Assert.NotEmpty(valor);
            }
            """);

        var (codigo, salida) = EjecutarSinRed(ensamblado);

        Assert.True(codigo == 0, salida);
        Assert.Contains("Passed:     3", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-01")]
    [Trait("Categoria", Categorias.Lanzamiento)]
    public void UnaPruebaQueIntentaConectarseAlaRedOAUnaBaseDeDatos_HaceFallarLaEjecucionYSeNombra()
    {
        var ensamblado = Compilar(
            "ConRed",
            """
            using System.Net;
            using System.Net.Sockets;

            namespace ConRed;

            public class Pruebas
            {
                [Fact]
                public void Autonoma() => Assert.True(true);

                [Fact]
                public async Task IntentaUnaConexionExterna()
                {
                    using var cliente = new TcpClient();
                    await cliente.ConnectAsync("example.com", 80);
                }

                [Fact]
                public async Task IntentaConectarseAUnaBaseDeDatos()
                {
                    using var cliente = new TcpClient();
                    await cliente.ConnectAsync(IPAddress.Loopback, 5432);
                }
            }
            """);

        var (codigo, salida) = EjecutarSinRed(ensamblado);

        Assert.Equal(1, codigo);
        Assert.Contains("Failed:     2", salida, StringComparison.Ordinal);
        Assert.Contains("IntentaUnaConexionExterna", salida, StringComparison.Ordinal);
        Assert.Contains("IntentaConectarseAUnaBaseDeDatos", salida, StringComparison.Ordinal);
        Assert.Contains("SocketException", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void ElScript_RechazaUnUsoInvalido()
    {
        var (codigo, _) = Ejecutar(Bash(), [Path.Combine("scripts", "pruebas-sin-red.sh").Replace('\\', '/'), Path.Combine(_carpeta, "no-existe.dll").Replace('\\', '/')], RutaDelRepositorio());

        Assert.Equal(2, codigo);
    }
}
