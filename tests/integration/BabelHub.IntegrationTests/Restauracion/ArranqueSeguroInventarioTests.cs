using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Api.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Restauracion;

/// <summary>
/// Lo propio del Inventario en el arranque seguro (PLA-19, PLA-43, PLA-39; plan §5.1): el ensayo se niega con sus propias bases de
/// producción y el proceso real de su Api exige la marca de un solo uso. La marca, el modo, los adaptadores, los trabajos de fondo y la
/// salud son el mismo código que en el E-commerce (<c>CopiasDeAdaptadoresTests</c> obliga a que siga igual) y se prueban una sola vez en
/// <c>ArranqueSeguroEcommerceTests</c>.
/// </summary>
public sealed class ArranqueSeguroInventarioTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "arranque-" + Guid.NewGuid().ToString("N"));

    // Los secretos de estas pruebas se generan en cada ejecución: nunca hay uno fijo en el repositorio.
    private readonly string _secretoDeLaMarca = "marca-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private sealed class AlmacenEnMemoria : IAlmacenDeNoncesDeMarcas
    {
        private readonly HashSet<string> _usados = [];

        public bool Consumir(string nonce)
        {
            lock (_usados)
            {
                return _usados.Add(nonce);
            }
        }
    }

    public ArranqueSeguroInventarioTests()
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

    private byte[] Secreto => Encoding.UTF8.GetBytes(_secretoDeLaMarca);

    private static string Nonce() => Guid.NewGuid().ToString("N");

    private static IConfiguration Configuracion(params (string Clave, string? Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor))).Build();

    private static string Huella(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    [Theory]
    [InlineData("Inventario:Base:Aplicacion")]
    [InlineData("Inventario:Base:DepuracionDeAjustes")]
    [Trait("Criterio", "AC-PLA-22")]
    public void ElEnsayoNoArrancaConLaBaseOLaPimientaDeProduccion_YNombraLaVariableSinElValor(string ruta)
    {
        var deProduccion = "Host=bd-de-produccion;Database=inventario;Username=acceso;Pass" + "word=" + Guid.NewGuid().ToString("N");
        var configuracion = Configuracion(
            ("Modo", "Ensayo"),
            ("Ensayo:ProhibidosSha256", Huella(deProduccion)),
            (ruta, deProduccion));

        var error = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(configuracion, esProduccion: false, Ahora, new AlmacenEnMemoria()));

        Assert.Contains("BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(deProduccion, error.Message, StringComparison.Ordinal);
    }

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private (int Codigo, string Salida) Arrancar(string entorno, string modo, string? marca)
    {
        var dll = Path.Combine(RaizDelRepositorio(), "src", "BabelHub.Inventario", "BabelHub.Inventario.Api", "bin", "Debug", "net10.0", "BabelHub.Inventario.Api.dll");
        var informacion = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        informacion.ArgumentList.Add(dll);
        informacion.Environment["ASPNETCORE_ENVIRONMENT"] = entorno;

        // La composición real (COMP-1) exige todos sus secretos: el proceso arranca con una configuración completa y desechable.
        foreach (var (ruta, valor) in BabelHub.IntegrationTests.Api.AnfitrionDelInventarioTests.ConfiguracionCompleta())
        {
            informacion.Environment["BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal)] = valor;
        }

        informacion.Environment["BABELHUB__Modo"] = modo;
        informacion.Environment["BABELHUB__Restauracion__SecretoDeLaMarca"] = _secretoDeLaMarca;
        informacion.Environment["BABELHUB__Restauracion__CarpetaDeMarcas"] = Path.Combine(_carpeta, "marcas-del-proceso");
        if (marca is not null)
        {
            informacion.Environment["BABELHUB__Restauracion__Marca"] = marca;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = new StringBuilder();
        proceso.OutputDataReceived += (_, e) => { lock (salida) { salida.AppendLine(e.Data); } };
        proceso.ErrorDataReceived += (_, e) => { lock (salida) { salida.AppendLine(e.Data); } };
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
        if (!proceso.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException("El servicio no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, salida.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void ElServicioEnProduccionNoArrancaEnRestauracionSinLaMarca()
    {
        var (codigo, salida) = Arrancar("Production", "Restauracion", marca: null);

        Assert.NotEqual(0, codigo);
        Assert.Contains("BABELHUB__Restauracion__Marca", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(_secretoDeLaMarca, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void ElServicioEnProduccionArrancaEnRestauracionConLaMarcaYNoConLaMismaDosVeces()
    {
        var marca = MarcaDeRestauracion.Emitir(Secreto, ModoDeArranque.Restauracion, DateTimeOffset.UtcNow, Nonce());

        var primera = Arrancar("Production", "Restauracion", marca);
        var segunda = Arrancar("Production", "Restauracion", marca);

        // Con la composición real el paso de depuración de Auditoría (AUD-31) habla con la base: sin ella la restauración no se abre, nombrando el paso, pero la marca válida sí se aceptó (llegó a los pasos) y se consumió.
        Assert.NotEqual(0, primera.Codigo);
        Assert.Contains("paso: auditoria-depurar", primera.Salida, StringComparison.Ordinal);
        Assert.Contains("restauracion: no se abre, falló el paso auditoria-depurar", primera.Salida, StringComparison.Ordinal);
        Assert.NotEqual(0, segunda.Codigo);
        Assert.Contains("Reutilizada", segunda.Salida, StringComparison.Ordinal);
        Assert.DoesNotContain(marca, segunda.Salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void ElServicioEnProduccionNoArrancaConUnaMarcaVencida()
    {
        var marca = MarcaDeRestauracion.Emitir(Secreto, ModoDeArranque.Restauracion, DateTimeOffset.UtcNow.AddMinutes(-10), Nonce());

        var (codigo, salida) = Arrancar("Production", "Restauracion", marca);

        Assert.NotEqual(0, codigo);
        Assert.Contains("Vencida", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void FueraDeProduccion_LaRestauracionCorreSusPasosSinMarcaYSinBaseNoSeAbre_YUnModoInvalidoNoArranca()
    {
        var sinBase = Arrancar("Development", "Restauracion", marca: null);
        var invalido = Arrancar("Development", "Cualquiera", marca: null);

        // El contenedor se valida con alcances (el ejecutor es un singleton y la depuración es por alcance): el paso corre y, sin base de datos, falla nombrándose.
        Assert.NotEqual(0, sinBase.Codigo);
        Assert.DoesNotContain("Cannot consume scoped service", sinBase.Salida, StringComparison.Ordinal);
        Assert.Contains("restauracion: no se abre, falló el paso auditoria-depurar", sinBase.Salida, StringComparison.Ordinal);
        Assert.NotEqual(0, invalido.Codigo);
        Assert.Contains("BABELHUB__Modo", invalido.Salida, StringComparison.Ordinal);
    }
}
