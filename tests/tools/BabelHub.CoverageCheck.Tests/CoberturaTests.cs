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

    private static IReadOnlyList<CoberturaDeModulo> Modulos(params string[] paquetes) =>
        Cobertura.Calcular(Cobertura.Leer([Informe(paquetes)]), Politica);

    private static IReadOnlyList<string> Avisos(params string[] paquetes) => Cobertura.Avisos(Modulos(paquetes), Ambos, Politica);

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloCriticoConDominioYAplicacionEn84_SeAvisa()
    {
        // 84 de 100 en Dominio y Aplicación y todo lo demás cubierto: el total (92 %) llega, pero Dominio y Aplicación no.
        var avisos = Avisos(Modulo("Ecommerce/Pagos", 84, 100), Reservas);

        var aviso = Assert.Single(avisos);
        Assert.Contains("Ecommerce/Pagos (crítico)", aviso, StringComparison.Ordinal);
        Assert.Contains("Dominio y Aplicación es 84.0 % y la referencia es 85 %", aviso, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloCriticoConElTotalEn69_SeAvisa()
    {
        // Dominio y Aplicación 85 de 100 y Infraestructura y Api 53 de 100: total 138/200 = 69 %.
        var avisos = Avisos(Modulo("Ecommerce/Pagos", 85, 53), Reservas);

        Assert.Contains("la cobertura total es 69.0 % y la referencia es 70 %", Assert.Single(avisos), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void CadaModuloJustoEnSuReferencia_NoSeAvisa()
    {
        // Crítico: 85/100 y total 140/200 = 70 %. No crítico: 60/100 y total 80/200 = 40 %.
        Assert.Empty(Avisos(Modulo("Ecommerce/Pagos", 85, 55), Modulo("Ecommerce/Catalogo", 60, 20), Modulo("Inventario/Reservas", 85, 55)));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloNoCriticoSeComparaConSuPropiaReferencia()
    {
        var porDominio = Assert.Single(Avisos(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Catalogo", 59, 100), Reservas));
        var porTotal = Assert.Single(Avisos(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Catalogo", 60, 19), Reservas));

        Assert.Contains("Ecommerce/Catalogo (no crítico): la cobertura de Dominio y Aplicación es 59.0 % y la referencia es 60 %", porDominio, StringComparison.Ordinal);
        Assert.Contains("Ecommerce/Catalogo (no crítico): la cobertura total es 39.5 % y la referencia es 40 %", porTotal, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void CadaModuloSeInformaPorSeparado()
    {
        // El servicio entero suma 92 % en Dominio y Aplicación; el módulo de Pagos, 84 %.
        var paquetes = new[] { Modulo("Ecommerce/Pagos", 84, 100), Modulo("Ecommerce/Catalogo", 100, 100), Reservas };

        Assert.Equal(["Ecommerce/Catalogo", "Ecommerce/Pagos", "Inventario/Reservas"], Modulos(paquetes).Select(m => m.Nombre));
        Assert.Contains("Ecommerce/Pagos", Assert.Single(Avisos(paquetes)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloSinClasificarSeComparaConLaReferenciaDeCritico()
    {
        var avisos = Avisos(Modulo("Ecommerce/Pagos", 100, 100), Modulo("Ecommerce/Regalos", 84, 100), Reservas);

        var aviso = Assert.Single(avisos);
        Assert.Contains("Ecommerce/Regalos (sin clasificar, con la referencia de crítico)", aviso, StringComparison.Ordinal);
        Assert.Contains("la referencia es 85 %", aviso, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnArchivoCuyaRutaNoPasaPorSuEnsambladoQuedaSinClasificar()
    {
        var avisos = Avisos(Modulo("Ecommerce/Pagos", 100, 100), Reservas, Paquete("BabelHub.Ecommerce.Domain", "/src/Suelto.cs", 10, 8));

        Assert.Contains("Ecommerce/(ruta desconocida) (sin clasificar", Assert.Single(avisos), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void UnModuloCriticoSinDatos_SeAvisa()
    {
        var modulos = Modulos(Modulo("Ecommerce/Catalogo", 100, 100), Reservas);

        Assert.Contains("Ecommerce/Pagos (crítico): no hay datos", Assert.Single(Cobertura.Avisos(modulos, Ambos, Politica)), StringComparison.Ordinal);
        Assert.Empty(Cobertura.ServiciosSinDatos(modulos, Ambos));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void UnServicioEsperadoSinDatosDeDominioYAplicacion_EsUnError()
    {
        var modulos = Modulos(
            Modulo("Ecommerce/Pagos", 100, 100), Paquete("BabelHub.Inventario.Api", "BabelHub.Inventario.Api/Reservas/A.cs", 10, 10));

        Assert.Contains("Inventario: no hay datos", Assert.Single(Cobertura.ServiciosSinDatos(modulos, Ambos)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void LaComparacionNoRedondea_UnaCoberturaDe8499NoLlegaA85()
    {
        // 8499 de 10000 líneas = 84,99 % (y 5999 de 10000 = 59,99 %): no llegan aunque se redondeen.
        var avisos = Avisos(
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Pagos/D.cs", 10000, 8499),
            Paquete("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Domain/Catalogo/D.cs", 10000, 5999),
            Reservas);

        Assert.Equal(2, avisos.Count);
        Assert.Contains(avisos, e => e.Contains("Ecommerce/Pagos", StringComparison.Ordinal) && e.Contains("Dominio y Aplicación es 84.9 %", StringComparison.Ordinal));
        Assert.Contains(avisos, e => e.Contains("Ecommerce/Catalogo", StringComparison.Ordinal) && e.Contains("Dominio y Aplicación es 59.9 %", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void LasMigracionesYElCodigoGeneradoNoCuentan()
    {
        // Sin las exclusiones, estas 200 líneas sin cubrir bajarían Pagos de 100 % a 50 % en el total.
        Assert.Empty(Avisos(
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
        // La clasificación y las referencias solo las cambia la persona responsable: estas listas cambian con ella.
        var vigente = PoliticaDeCobertura.Vigente;
        string[] noCriticos = ["Ecommerce/Asistente", "Ecommerce/Carrito", "Ecommerce/Catalogo", "Ecommerce/Devoluciones", "Ecommerce/Envios", "Ecommerce/Notificaciones"];
        string[] criticos =
        [
            "Ecommerce/Auditoria", "Ecommerce/Biblioteca", "Ecommerce/Identidad", "Ecommerce/Pagos", "Ecommerce/Pedidos", "Ecommerce/Plataforma", "Ecommerce/Reclamaciones", "Ecommerce/Web",
            "Inventario/Ajustes", "Inventario/Auditoria", "Inventario/Fuentes", "Inventario/Plataforma", "Inventario/Reservas", "Inventario/Traslados",
        ];

        Assert.Equal(new Referencia(85, 70), vigente.ReferenciaDe(Criticidad.Critico));
        Assert.Equal(new Referencia(85, 70), vigente.ReferenciaDe(Criticidad.SinClasificar));
        Assert.Equal(new Referencia(60, 40), vigente.ReferenciaDe(Criticidad.NoCritico));
        Assert.Equal(criticos, vigente.ModulosCriticos.Order(StringComparer.Ordinal));
        Assert.Equal(noCriticos, vigente.ModulosNoCriticos.Order(StringComparer.Ordinal));
        Assert.All(noCriticos, m => Assert.Equal(Criticidad.NoCritico, vigente.CriticidadDe(m.Split('/')[0], m.Split('/')[1])));
        Assert.All(criticos, m => Assert.Equal(Criticidad.Critico, vigente.CriticidadDe(m.Split('/')[0], m.Split('/')[1])));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-14")]
    public void CadaModuloDelCodigoEstaClasificado()
    {
        // Un módulo nuevo toma la referencia de crítico hasta que se clasifique; esta prueba obliga a clasificarlo a la vista.
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
    public void ElProgramaInformaLoQueQuedaBajoSuReferenciaYNoFalla()
    {
        var enReferencia = Escribir("en-referencia", InformeVigente("Ecommerce/Catalogo", 60, 20));
        var debajo = Escribir("debajo", InformeVigente("Ecommerce/Pagos", 0, 0));

        var bien = Ejecutar(enReferencia);
        Assert.True(bien.Codigo == 0, bien.Salida);
        Assert.Contains("con 0 aviso(s)", bien.Salida, StringComparison.Ordinal);
        var conAvisos = Ejecutar(debajo);
        Assert.True(conAvisos.Codigo == 0, conAvisos.Salida);
        Assert.Contains("AVISO: Ecommerce/Pagos (crítico): la cobertura de Dominio y Aplicación es 0.0 % y la referencia es 85 %", conAvisos.Salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-45")]
    public void ElProgramaFallaSiNoPuedeLeerLosInformesOFaltaUnServicio()
    {
        var bueno = Escribir("bueno", InformeVigente("Ecommerce/Catalogo", 60, 20));
        var roto = Escribir("roto", "<coverage><packages>");
        var sinInventario = Escribir("sin-inventario", Informe(Modulo("Ecommerce/Pagos", 100, 100)));
        var carpetaVacia = Directory.CreateDirectory(Path.Combine(_carpeta, "vacia")).FullName;

        var incompleto = Ejecutar(sinInventario);
        Assert.Equal(2, incompleto.Codigo);
        Assert.Contains("Inventario: no hay datos", incompleto.Salida, StringComparison.Ordinal);
        Assert.Equal(2, Ejecutar(roto).Codigo);
        Assert.Equal(2, Ejecutar(bueno, roto).Codigo);
        Assert.Equal(2, Ejecutar(carpetaVacia).Codigo);
        Assert.Equal(2, Ejecutar(Path.Combine(_carpeta, "no-existe.xml")).Codigo);
        Assert.Equal(2, Ejecutar(bueno, "--servicios", ",").Codigo);
        Assert.Equal(2, Ejecutar(bueno, "--opcion-desconocida").Codigo);
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

        // Cada informe cubre una parte: Dominio y Aplicación llega a 85 % y el total a 70 % solo si se unen las líneas.
        var resultado = Ejecutar(_carpeta);

        Assert.True(resultado.Codigo == 0, resultado.Salida);
        Assert.Contains("con 0 aviso(s)", resultado.Salida, StringComparison.Ordinal);
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
