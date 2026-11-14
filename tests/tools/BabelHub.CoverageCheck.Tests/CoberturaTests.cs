using System.Diagnostics;
using System.Text;
using BabelHub.Tools.CoverageCheck;

namespace BabelHub.CoverageCheck.Tests;

public sealed class CoberturaTests : IDisposable
{
    private static readonly string[] Ambos = ["Ecommerce", "Inventario"];
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "coveragecheck-" + Guid.NewGuid().ToString("N"));

    public CoberturaTests()
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

    /// <summary>Un informe de coverlet de un ensamblado: una clase por archivo con tantas líneas como se indique, las primeras cubiertas.</summary>
    private static string Paquete(string ensamblado, string archivo, int totales, int cubiertas, bool conMetodos = false)
    {
        var lineas = new StringBuilder();
        for (var i = 1; i <= totales; i++)
        {
            lineas.Append($"<line number=\"{i}\" hits=\"{(i <= cubiertas ? 1 : 0)}\" branch=\"False\" />");
        }

        var metodos = conMetodos ? $"<methods><method name=\"M\"><lines>{lineas}</lines></method></methods>" : "<methods />";
        return $"<package name=\"{ensamblado}\"><classes><class name=\"{archivo}\" filename=\"{archivo}\">{metodos}<lines>{lineas}</lines></class></classes></package>";
    }

    private static string Informe(params string[] paquetes) =>
        $"<?xml version=\"1.0\"?><coverage line-rate=\"0\"><packages>{string.Concat(paquetes)}</packages></coverage>";

    /// <summary>Un servicio con 100 líneas en Dominio y Aplicación (la mitad en cada una) y 100 más en Infraestructura y Api.</summary>
    private static string Servicio(string nombre, int cubiertasDominioYAplicacion, int cubiertasOtras)
    {
        var dominio = Math.Min(50, cubiertasDominioYAplicacion);
        var aplicacion = cubiertasDominioYAplicacion - dominio;
        var infraestructura = Math.Min(50, cubiertasOtras);
        var api = cubiertasOtras - infraestructura;
        return string.Concat(
            Paquete($"BabelHub.{nombre}.Domain", $"/src/{nombre}/Dominio.cs", 50, dominio),
            Paquete($"BabelHub.{nombre}.Application", $"/src/{nombre}/Aplicacion.cs", 50, aplicacion),
            Paquete($"BabelHub.{nombre}.Infrastructure", $"/src/{nombre}/Infraestructura.cs", 50, infraestructura),
            Paquete($"BabelHub.{nombre}.Api", $"/src/{nombre}/Api.cs", 50, api));
    }

    private static IReadOnlyList<string> Verificar(string xml, int dominioYAplicacion = 85, int total = 70, params string[] servicios) =>
        Cobertura.Verificar(Cobertura.Calcular(Cobertura.Leer([xml])), servicios.Length == 0 ? Ambos : servicios, new Umbrales(dominioYAplicacion, total));

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ConDominioYAplicacionEn84_Falla()
    {
        // 84 de 100 en Dominio y Aplicación y todo lo demás cubierto: el total (92 %) cumple, pero no el 85 %.
        var xml = Informe(Servicio("Ecommerce", 84, 100), Servicio("Inventario", 100, 100));

        var incumplimientos = Verificar(xml);

        var error = Assert.Single(incumplimientos);
        Assert.Contains("Ecommerce", error, StringComparison.Ordinal);
        Assert.Contains("Dominio y Aplicación es 84.0 %", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ConDominioYAplicacionEn85PeroElTotalEn69_Falla()
    {
        // Dominio y Aplicación 85 de 100 y Infraestructura y Api 53 de 100: total 138/200 = 69 %.
        var xml = Informe(Servicio("Ecommerce", 85, 53), Servicio("Inventario", 100, 100));

        var incumplimientos = Verificar(xml);

        var error = Assert.Single(incumplimientos);
        Assert.Contains("la cobertura total es 69.0 %", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ConDominioYAplicacionEn85YElTotalEn70_Pasa()
    {
        // 85/100 y 55/100: total 140/200 = 70 % exacto.
        var xml = Informe(Servicio("Ecommerce", 85, 55), Servicio("Inventario", 85, 55));

        Assert.Empty(Verificar(xml));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ElMinimoSeCompararSinRedondear_UnaCoberturaDe8499NoEs85()
    {
        // 8499 de 10000 líneas = 84,99 %: no llega a 85 % aunque se redondee a 85.
        var xml = Informe(
            Paquete("BabelHub.Ecommerce.Domain", "/src/D.cs", 10000, 8499),
            Paquete("BabelHub.Ecommerce.Application", "/src/A.cs", 1, 1),
            Paquete("BabelHub.Inventario.Domain", "/src/D.cs", 10, 10),
            Paquete("BabelHub.Inventario.Application", "/src/A.cs", 10, 10));

        var incumplimientos = Verificar(xml);

        Assert.Contains(incumplimientos, e => e.Contains("Ecommerce", StringComparison.Ordinal) && e.Contains("Dominio y Aplicación es 84.9 %", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void LasMigracionesYElCodigoGeneradoNoCuentan()
    {
        // Sin las exclusiones, estas 200 líneas sin cubrir bajarían el servicio a 33 %.
        var xml = Informe(
            Servicio("Ecommerce", 100, 100),
            Servicio("Inventario", 100, 100),
            Paquete("BabelHub.Ecommerce.Infrastructure", "/src/Ecommerce/Migrations/20261113_Inicial.cs", 50, 0),
            Paquete("BabelHub.Ecommerce.Infrastructure", "C:\\src\\Ecommerce\\Migrations\\ModeloModelSnapshot.cs", 50, 0),
            Paquete("BabelHub.Ecommerce.Application", "/src/Ecommerce/obj/Debug/net10.0/Regex.g.cs", 50, 0),
            Paquete("BabelHub.Ecommerce.Domain", "/src/Ecommerce/Tabla.Designer.cs", 50, 0));

        Assert.Empty(Verificar(xml));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnaLineaCubiertaPorAlgunInformeCuentaComoCubierta()
    {
        var unitarias = Informe(Paquete("BabelHub.Ecommerce.Domain", "/src/D.cs", 100, 80), Paquete("BabelHub.Ecommerce.Application", "/src/A.cs", 100, 100));
        // El otro informe cubre otras 10 líneas de Dominio (las que van de la 81 a la 90).
        var integracion = Informe(Paquete("BabelHub.Ecommerce.Domain", "/src/D.cs", 100, 0).Replace("number=\"81\" hits=\"0\"", "number=\"81\" hits=\"3\"", StringComparison.Ordinal));
        var lineas = Cobertura.Leer([unitarias, integracion]);

        var servicio = Assert.Single(Cobertura.Calcular(lineas));

        Assert.Equal(200, servicio.TotalTotales);
        Assert.Equal(181, servicio.TotalCubiertas);
    }

    [Fact]
    public void LasLineasDeLosMetodosNoSeCuentanDosVeces()
    {
        var xml = Informe(Paquete("BabelHub.Ecommerce.Domain", "/src/D.cs", 10, 5, conMetodos: true));

        var servicio = Assert.Single(Cobertura.Calcular(Cobertura.Leer([xml])));

        Assert.Equal(10, servicio.TotalTotales);
        Assert.Equal(5, servicio.TotalCubiertas);
    }

    [Fact]
    public void LosEnsambladosQueNoSonDeUnServicioNoCuentan()
    {
        var xml = Informe(
            Paquete("BabelHub.Shared", "/src/Shared.cs", 100, 0),
            Paquete("BabelHub.Ecommerce.UnitTests", "/tests/P.cs", 100, 0),
            Paquete("BabelHub.Ecommerce.Domain", "/src/D.cs", 10, 10));

        var servicio = Assert.Single(Cobertura.Calcular(Cobertura.Leer([xml])));

        Assert.Equal("Ecommerce", servicio.Servicio);
        Assert.Equal(10, servicio.TotalTotales);
    }

    [Fact]
    public void UnServicioEsperadoSinDatosDeDominioYAplicacion_Falla()
    {
        var xml = Informe(Servicio("Ecommerce", 100, 100));

        var incumplimientos = Verificar(xml);

        Assert.Contains(incumplimientos, e => e.Contains("Inventario", StringComparison.Ordinal) && e.Contains("no hay datos", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ElProgramaSalePorLosCodigosDeSalidaDelCriterio()
    {
        var cumple = Path.Combine(_carpeta, "cumple.cobertura.xml");
        File.WriteAllText(cumple, Informe(Servicio("Ecommerce", 85, 55), Servicio("Inventario", 100, 100)));
        var noCumple = Path.Combine(_carpeta, "no-cumple.cobertura.xml");
        File.WriteAllText(noCumple, Informe(Servicio("Ecommerce", 84, 100), Servicio("Inventario", 100, 100)));
        var roto = Path.Combine(_carpeta, "roto.cobertura.xml");
        File.WriteAllText(roto, "<coverage><packages>");

        Assert.Equal(0, Ejecutar(cumple).Codigo);
        var fallo = Ejecutar(noCumple);
        Assert.Equal(1, fallo.Codigo);
        Assert.Contains("84.0 %", fallo.Salida, StringComparison.Ordinal);
        Assert.Equal(1, Ejecutar(cumple, "--minimo-dominio-aplicacion", "90").Codigo);
        Assert.Equal(2, Ejecutar(roto).Codigo);
        Assert.Equal(2, Ejecutar(Path.Combine(_carpeta, "no-existe.xml")).Codigo);
        Assert.Equal(2, Ejecutar().Codigo);
    }

    [Fact]
    public void ElProgramaRecorreUnaCarpetaYUneLosInformes()
    {
        var subcarpeta = Path.Combine(_carpeta, "TestResults", "uno");
        Directory.CreateDirectory(subcarpeta);
        File.WriteAllText(Path.Combine(subcarpeta, "a.cobertura.xml"), Informe(Servicio("Ecommerce", 85, 0), Servicio("Inventario", 85, 0)));
        File.WriteAllText(Path.Combine(subcarpeta, "b.cobertura.xml"), Informe(Servicio("Ecommerce", 0, 55), Servicio("Inventario", 0, 55)));

        // Cada informe cubre una parte: Dominio y Aplicación 85 % y total 70 % solo si se unen las líneas.
        var resultado = Ejecutar(_carpeta);

        Assert.True(resultado.Codigo == 0, resultado.Salida);
    }

    private static (int Codigo, string Salida) Ejecutar(params string[] argumentos)
    {
        var informacion = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        informacion.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CoverageCheck.dll"));
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEnd() + proceso.StandardError.ReadToEnd();
        proceso.WaitForExit();
        return (proceso.ExitCode, salida);
    }
}
