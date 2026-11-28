using System.Diagnostics;
using System.Text;
using BabelHub.Tools.CoverageCheck;
using static BabelHub.CoverageCheck.Tests.Informes;

namespace BabelHub.CoverageCheck.Tests;

public sealed class CoberturaTests : IDisposable
{
    private static readonly string[] Ambos = ["Ecommerce", "Inventario"];

    /// <summary>Una política pequeña: un módulo crítico por servicio y uno no crítico.</summary>
    private static readonly PoliticaDeCobertura Politica = new(["Ecommerce/Pagos", "Inventario/Reservas"], ["Ecommerce/Catalogo"]);

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

    private static string Reservas => Modulo("Inventario/Reservas", 100, 100);

    private static IReadOnlyList<string> Verificar(params string[] paquetes) =>
        Cobertura.Verificar(Cobertura.Calcular(Cobertura.Leer([Informe(paquetes)]), Politica), Ambos, Politica);

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloCriticoConDominioYAplicacionEn84_Falla()
    {
        // 84 de 100 en Dominio y Aplicación y todo lo demás cubierto: el total (92 %) cumple, pero no el 85 %.
        var incumplimientos = Verificar(Modulo("Ecommerce/Pagos", 84, 100), Reservas);

        var error = Assert.Single(incumplimientos);
        Assert.Contains("Ecommerce/Pagos (crítico)", error, StringComparison.Ordinal);
        Assert.Contains("Dominio y Aplicación es 84.0 % y el mínimo es 85 %", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloCriticoConDominioYAplicacionEn85PeroElTotalEn69_Falla()
    {
        // Dominio y Aplicación 85 de 100 y Infraestructura y Api 53 de 100: total 138/200 = 69 %.
        var incumplimientos = Verificar(Modulo("Ecommerce/Pagos", 85, 53), Reservas);

        var error = Assert.Single(incumplimientos);
        Assert.Contains("la cobertura total es 69.0 % y el mínimo es 70 %", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void CadaModuloJustoEnSusMinimos_Pasa()
    {
        // Crítico: 85/100 y total 140/200 = 70 %. No crítico: 60/100 y total 80/200 = 40 %.
        Assert.Empty(Verificar(Modulo("Ecommerce/Pagos", 85, 55), Modulo("Ecommerce/Catalogo", 60, 20), Modulo("Inventario/Reservas", 85, 55)));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloNoCriticoPorDebajoDe60O40_Falla()
    {
        var porDominio = Assert.Single(Verificar(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Catalogo", 59, 100), Reservas));
        var porTotal = Assert.Single(Verificar(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Catalogo", 60, 19), Reservas));

        Assert.Contains("Ecommerce/Catalogo (no crítico): la cobertura de Dominio y Aplicación es 59.0 % y el mínimo es 60 %", porDominio, StringComparison.Ordinal);
        Assert.Contains("Ecommerce/Catalogo (no crítico): la cobertura total es 39.5 % y el mínimo es 40 %", porTotal, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloCriticoNoSeCompensaConElRestoDelServicio()
    {
        // El servicio entero suma 92 % en Dominio y Aplicación; el módulo crítico, 84 %.
        var incumplimientos = Verificar(Modulo("Ecommerce/Pagos", 84, 100), Modulo("Ecommerce/Catalogo", 100, 100), Reservas);

        Assert.Contains("Ecommerce/Pagos", Assert.Single(incumplimientos), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void UnModuloSinClasificarSeExigeComoCritico()
    {
        var incumplimientos = Verificar(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Regalos", 84, 100), Reservas);

        var error = Assert.Single(incumplimientos);
        Assert.Contains("Ecommerce/Regalos (sin clasificar, se exige como crítico)", error, StringComparison.Ordinal);
        Assert.Contains("el mínimo es 85 %", error, StringComparison.Ordinal);

        var porTotal = Assert.Single(Verificar(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Regalos", 85, 53), Reservas));
        Assert.Contains("la cobertura total es 69.0 % y el mínimo es 70 %", porTotal, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void UnArchivoCuyaRutaNoPasaPorSuEnsambladoSeExigeComoCritico()
    {
        var incumplimientos = Verificar(
            Modulo("Ecommerce/Pagos", 100, 100), Reservas, Paquete("BabelHub.Ecommerce.Domain", "/src/Suelto.cs", 10, 8));

        Assert.Contains("Ecommerce/(ruta desconocida) (sin clasificar", Assert.Single(incumplimientos), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void UnModuloCriticoSinDatos_Falla()
    {
        var incumplimientos = Verificar(Modulo("Ecommerce/Catalogo", 100, 100), Reservas);

        Assert.Contains("Ecommerce/Pagos (crítico): no hay datos", Assert.Single(incumplimientos), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void UnServicioEsperadoSinDatosDeDominioYAplicacion_Falla()
    {
        var incumplimientos = Verificar(
            Modulo("Ecommerce/Pagos", 100, 100), Paquete("BabelHub.Inventario.Api", "BabelHub.Inventario.Api/Reservas/A.cs", 10, 10));

        Assert.Contains("Inventario: no hay datos", Assert.Single(incumplimientos), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ElMinimoSeCompararSinRedondear_UnaCoberturaDe8499NoEs85()
    {
        // 8499 de 10000 líneas = 84,99 % (y 5999 de 10000 = 59,99 %): no llegan aunque se redondeen.
        var incumplimientos = Verificar(
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/D.cs", 10000, 8499),
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Catalogo/D.cs", 10000, 5999),
            Reservas);

        Assert.Equal(2, incumplimientos.Count);
        Assert.Contains(incumplimientos, e => e.Contains("Ecommerce/Pagos", StringComparison.Ordinal) && e.Contains("Dominio y Aplicación es 84.9 %", StringComparison.Ordinal));
        Assert.Contains(incumplimientos, e => e.Contains("Ecommerce/Catalogo", StringComparison.Ordinal) && e.Contains("Dominio y Aplicación es 59.9 %", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void LasMigracionesYElCodigoGeneradoNoCuentan()
    {
        // Sin las exclusiones, estas 200 líneas sin cubrir bajarían Pagos de 100 % a 50 % en el total.
        Assert.Empty(Verificar(
            Modulo("Ecommerce/Pagos", 100, 100),
            Reservas,
            Paquete("BabelHub.Ecommerce.Infrastructure", "BabelHub.Ecommerce.Infrastructure/Pagos/Migrations/20261113_Inicial.cs", 50, 0),
            Paquete("BabelHub.Ecommerce.Infrastructure", "C:\\src\\BabelHub.Ecommerce.Infrastructure\\Pagos\\Migrations\\ModeloModelSnapshot.cs", 50, 0),
            Paquete("BabelHub.Ecommerce.Application", "BabelHub.Ecommerce.Application/obj/Debug/net10.0/Regex.g.cs", 50, 0),
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/Tabla.Designer.cs", 50, 0)));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnaLineaCubiertaPorAlgunInformeCuentaComoCubierta()
    {
        var unitarias = Informe(
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/D.cs", 100, 80),
            Paquete("BabelHub.Ecommerce.Application", "BabelHub.Ecommerce.Application/Pagos/A.cs", 100, 100));
        // El otro informe cubre además la línea 81 de Dominio.
        var integracion = Informe(Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/D.cs", 100, 0)
            .Replace("number=\"81\" hits=\"0\"", "number=\"81\" hits=\"3\"", StringComparison.Ordinal));

        var modulo = Assert.Single(Cobertura.Calcular(Cobertura.Leer([unitarias, integracion]), Politica));

        Assert.Equal(200, modulo.TotalTotales);
        Assert.Equal(181, modulo.TotalCubiertas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void LosInformesConRaicesDeFuentesDistintasUnenLasLineasDelMismoArchivo()
    {
        // Las pruebas unitarias dan la ruta desde el ensamblado y las de integración desde el servicio: es el mismo archivo.
        var unitarias = Informe(Paquete("BabelHub.Inventario.Domain", "BabelHub.Inventario.Domain\\Plataforma\\D.cs", 10, 10));
        var integracion = Informe(Paquete("BabelHub.Inventario.Domain", "BabelHub.Inventario\\BabelHub.Inventario.Domain\\Plataforma\\D.cs", 10, 0));
        // Y un informe cuya raíz de fuentes ya está dentro de la carpeta del ensamblado.
        var conRaiz = Informe(Paquete("BabelHub.Inventario.Domain", "D.cs", 10, 0))
            .Replace("<packages>", "<sources><source>C:\\repo\\src\\BabelHub.Inventario\\BabelHub.Inventario.Domain\\Plataforma\\</source></sources><packages>", StringComparison.Ordinal);

        var modulo = Assert.Single(Cobertura.Calcular(Cobertura.Leer([unitarias, integracion, conRaiz]), Politica));

        Assert.Equal("Inventario/Plataforma", modulo.Nombre);
        Assert.Equal(10, modulo.TotalTotales);
        Assert.Equal(10, modulo.TotalCubiertas);
    }

    [Theory]
    [InlineData("Api", "Areas/Tienda/Controllers/PagoController.cs", "Web")]
    [InlineData("Api", "Controllers/CuentaController.cs", "Web")]
    [InlineData("Api", "Views/Cuenta/Cuenta.cshtml", "Web")]
    [InlineData("Api", "Program.cs", "Plataforma")]
    [InlineData("Domain", "AssemblyMarker.cs", "Plataforma")]
    [InlineData("Infrastructure", "Composicion/ComposicionDePagos.cs", "Plataforma")]
    [InlineData("Infrastructure", "Migracion/MigradorDeLaBase.cs", "Plataforma")]
    [InlineData("Infrastructure", "Pagos/Interno/Adaptador.cs", "Pagos")]
    public void CadaCarpetaPerteneceAUnModulo(string capa, string archivo, string esperado)
    {
        var ensamblado = $"BabelHub.Ecommerce.{capa}";

        var modulo = Assert.Single(Cobertura.Calcular(Cobertura.Leer([Informe(Paquete(ensamblado, $"{ensamblado}/{archivo}", 1, 1))]), Politica));

        Assert.Equal(esperado, modulo.Modulo);
    }

    [Fact]
    public void LasLineasDeLosMetodosNoSeCuentanDosVeces()
    {
        var xml = Informe(Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/D.cs", 10, 5, conMetodos: true));

        var modulo = Assert.Single(Cobertura.Calcular(Cobertura.Leer([xml]), Politica));

        Assert.Equal(10, modulo.TotalTotales);
        Assert.Equal(5, modulo.TotalCubiertas);
    }

    [Fact]
    public void LosEnsambladosQueNoSonDeUnServicioNoCuentan()
    {
        var xml = Informe(
            Paquete("BabelHub.Shared", "BabelHub.Shared/Contratos/S.cs", 100, 0),
            Paquete("BabelHub.Ecommerce.UnitTests", "BabelHub.Ecommerce.UnitTests/Pagos/P.cs", 100, 0),
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/D.cs", 10, 10));

        var modulo = Assert.Single(Cobertura.Calcular(Cobertura.Leer([xml]), Politica));

        Assert.Equal("Ecommerce/Pagos", modulo.Nombre);
        Assert.Equal(10, modulo.TotalTotales);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void LaPoliticaVigenteEsLaDecidida()
    {
        // Bajar la exigencia de un módulo es una decisión de la persona responsable: estas listas cambian con ella.
        var vigente = PoliticaDeCobertura.Vigente;
        string[] noCriticos = ["Ecommerce/Asistente", "Ecommerce/Carrito", "Ecommerce/Catalogo", "Ecommerce/Devoluciones", "Ecommerce/Envios", "Ecommerce/Notificaciones"];
        string[] criticos =
        [
            "Ecommerce/Auditoria", "Ecommerce/Biblioteca", "Ecommerce/Identidad", "Ecommerce/Pagos", "Ecommerce/Pedidos", "Ecommerce/Plataforma", "Ecommerce/Reclamaciones", "Ecommerce/Web",
            "Inventario/Ajustes", "Inventario/Auditoria", "Inventario/Fuentes", "Inventario/Plataforma", "Inventario/Reservas", "Inventario/Traslados",
        ];

        Assert.Equal(new Umbrales(85, 70), vigente.UmbralesDe(Criticidad.Critico));
        Assert.Equal(new Umbrales(85, 70), vigente.UmbralesDe(Criticidad.SinClasificar));
        Assert.Equal(new Umbrales(60, 40), vigente.UmbralesDe(Criticidad.NoCritico));
        Assert.Equal(criticos, vigente.ModulosCriticos.Order(StringComparer.Ordinal));
        Assert.Equal(noCriticos, vigente.ModulosNoCriticos.Order(StringComparer.Ordinal));
        Assert.All(noCriticos, m => Assert.Equal(Criticidad.NoCritico, vigente.CriticidadDe(m.Split('/')[0], m.Split('/')[1])));
        Assert.All(criticos, m => Assert.Equal(Criticidad.Critico, vigente.CriticidadDe(m.Split('/')[0], m.Split('/')[1])));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void CadaModuloDelCodigoEstaClasificado()
    {
        // Un módulo nuevo se exige como crítico hasta que se clasifique; esta prueba obliga a clasificarlo a la vista.
        var raiz = RaizDelRepositorio();
        var sinClasificar = new SortedSet<string>(StringComparer.Ordinal);
        var revisadas = 0;
        foreach (var servicio in Ambos)
        {
            foreach (var capa in new[] { "Domain", "Application", "Infrastructure", "Api" })
            {
                var proyecto = Path.Combine(raiz, "src", $"BabelHub.{servicio}", $"BabelHub.{servicio}.{capa}");
                foreach (var carpeta in Directory.EnumerateDirectories(proyecto)
                    .Where(c => Path.GetFileName(c) is not ("bin" or "obj") && Directory.EnumerateFiles(c, "*.cs", SearchOption.AllDirectories).Any()))
                {
                    revisadas++;
                    var modulo = PoliticaDeCobertura.ModuloDeLaCarpeta(Path.GetFileName(carpeta));
                    if (PoliticaDeCobertura.Vigente.CriticidadDe(servicio, modulo) == Criticidad.SinClasificar)
                    {
                        sinClasificar.Add($"{servicio}/{modulo}");
                    }
                }
            }
        }

        Assert.True(revisadas >= 40, $"Solo se revisaron {revisadas} carpetas: la prueba no está mirando el código.");
        Assert.True(sinClasificar.Count == 0, "Módulos sin clasificar en PoliticaDeCobertura: " + string.Join(", ", sinClasificar));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void ElProgramaSalePorLosCodigosDeSalidaDelCriterio()
    {
        var cumple = Escribir("cumple", InformeVigente("Ecommerce/Catalogo", 60, 20));
        var noCumple = Escribir("no-cumple", InformeVigente("Ecommerce/Pagos", 84, 100));
        var roto = Escribir("roto", "<coverage><packages>");

        var bien = Ejecutar(cumple);
        Assert.True(bien.Codigo == 0, bien.Salida);
        var fallo = Ejecutar(noCumple);
        Assert.Equal(1, fallo.Codigo);
        Assert.Contains("Ecommerce/Pagos (crítico): la cobertura de Dominio y Aplicación es 84.0 %", fallo.Salida, StringComparison.Ordinal);
        Assert.Equal(2, Ejecutar(cumple, "--minimo-total", "0").Codigo);
        Assert.Equal(2, Ejecutar(cumple, "--servicios", ",").Codigo);
        Assert.Equal(2, Ejecutar(roto).Codigo);
        Assert.Equal(2, Ejecutar(Path.Combine(_carpeta, "no-existe.xml")).Codigo);
        Assert.Equal(2, Ejecutar().Codigo);
    }

    [Fact]
    public void ElProgramaRecorreUnaCarpetaYUneLosInformes()
    {
        var subcarpeta = Path.Combine(_carpeta, "TestResults", "uno");
        Directory.CreateDirectory(subcarpeta);
        var criticos = PoliticaDeCobertura.Vigente.ModulosCriticos;
        File.WriteAllText(Path.Combine(subcarpeta, "a.cobertura.xml"), Informe([.. criticos.Select(m => Modulo(m, 85, 0))]));
        File.WriteAllText(Path.Combine(subcarpeta, "b.cobertura.xml"), Informe([.. criticos.Select(m => Modulo(m, 0, 55))]));

        // Cada informe cubre una parte: Dominio y Aplicación 85 % y total 70 % solo si se unen las líneas.
        var resultado = Ejecutar(_carpeta);

        Assert.True(resultado.Codigo == 0, resultado.Salida);
    }

    /// <summary>Un informe con todos los módulos críticos vigentes cubiertos y uno más (o uno de ellos) con las cifras dadas.</summary>
    private static string InformeVigente(string modulo, int dominioYAplicacion, int otras) =>
        Informe([.. PoliticaDeCobertura.Vigente.ModulosCriticos.Where(m => m != modulo).Select(m => Modulo(m, 100, 100)), Modulo(modulo, dominioYAplicacion, otras)]);

    private string Escribir(string nombre, string contenido)
    {
        var ruta = Path.Combine(_carpeta, nombre + ".cobertura.xml");
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (BabelHub.sln).");
    }

    private static (int Codigo, string Salida) Ejecutar(params string[] argumentos)
    {
        var informacion = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
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
