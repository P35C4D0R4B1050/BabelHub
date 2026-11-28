using System.Diagnostics;
using System.Text;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.VmAux;

/// <summary>
/// <c>scripts/verificar-cifrado-de-disco.sh</c> (T26; PLA-20, AM29, D19): pregunta al proveedor por el volumen de arranque y los volúmenes de
/// bloque de una VM y no da por verificado el cifrado si alguno no está disponible, no se puede consultar o no existe. Con un doble de la CLI
/// de la nube; contra la nube real no se verificó (requiere la cuenta de Oracle).
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
[Trait("Categoria", Categorias.Guiones)]
public sealed class CifradoDeDiscoTests : IDisposable
{
    private const string Instancia = "ocid1.instance.oc1.sa-saopaulo-1.aaaaaaaainstanciadeprueba";
    private const string Compartimento = "ocid1.compartment.oc1..aaaaaaaacompartimentodeprueba";
    private const string Arranque = "ocid1.bootvolume.oc1.sa-saopaulo-1.aaaaaaaaarranque12345678";
    private const string Bloque = "ocid1.volume.oc1.sa-saopaulo-1.aaaaaaaabloque87654321";
    private const string ClaveDelCliente = "ocid1.key.oc1.sa-saopaulo-1.aaaaaaaaclave11112222";

    private const string DobleDeOci = """
        #!/usr/bin/env bash
        echo "$*" >>"$DOBLE_REGISTRO"
        case "$1 $2 $3" in
          "compute boot-volume-attachment list")
            [[ -n "${DOBLE_FALLA_ARRANQUE:-}" ]] && exit 1
            echo "[${DOBLE_ARRANQUES:-}]" ;;
          "compute volume-attachment list")
            [[ -n "${DOBLE_FALLA_BLOQUES:-}" ]] && exit 1
            echo "[${DOBLE_BLOQUES:-}]" ;;
          "bv boot-volume get" | "bv volume get")
            id="$5"
            [[ "$id" == "${DOBLE_ILEGIBLE:-}" ]] && exit 1
            if [[ "$*" == *lifecycle-state* ]]; then echo "${DOBLE_ESTADO:-AVAILABLE}"; else echo "${DOBLE_KMS:-null}"; fi ;;
          *) echo "orden no esperada: $*" >&2; exit 1 ;;
        esac
        """;

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cifrado-disco-" + Guid.NewGuid().ToString("N"));

    public CifradoDeDiscoTests()
    {
        Directory.CreateDirectory(_carpeta);
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "oci-doble.sh"), DobleDeOci.ReplaceLineEndings("\n"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal.
        }
    }

    private static string Linux(string ruta) => ruta.Replace('\\', '/');

    private string Registro => Path.Combine(_carpeta, "llamadas.log");

    private (int Codigo, string Salida, string Errores) Ejecutar(Dictionary<string, string>? entorno = null, params string[] argumentos)
    {
        var bash = OperatingSystem.IsWindows() && File.Exists(@"C:\Program Files\Git\bin\bash.exe") ? @"C:\Program Files\Git\bin\bash.exe" : "bash";
        var informacion = new ProcessStartInfo(bash) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(Linux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "verificar-cifrado-de-disco.sh")));
        foreach (var argumento in argumentos.Length > 0 ? argumentos : ["--instancia", Instancia, "--compartimento", Compartimento, "--dominio", "Abcd:SA-SAOPAULO-1-AD-1"])
        {
            informacion.ArgumentList.Add(argumento);
        }

        informacion.Environment["OCI"] = Linux(Path.Combine(_carpeta, "oci-doble.sh"));
        informacion.Environment["DOBLE_REGISTRO"] = Linux(Registro);
        informacion.Environment["DOBLE_ARRANQUES"] = $"\"{Arranque}\"";
        foreach (var (clave, valor) in entorno ?? [])
        {
            informacion.Environment[clave] = valor;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(2)), "El script no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ConElVolumenDeArranqueDisponibleYCifrado_SeVerificaYSeDejaConstancia()
    {
        var (codigo, salida, errores) = Ejecutar();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok …" + Arranque[^8..] + " AVAILABLE clave-de-oracle", salida, StringComparison.Ordinal);
        Assert.Contains("Cifrado de disco verificado en 1 volumen(es)", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(Arranque, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ConVolumenesDeBloqueAdjuntos_SeVerificanTodos_ConLaClaveDelClienteSiLaHay()
    {
        var (codigo, salida, errores) = Ejecutar(new Dictionary<string, string> { ["DOBLE_BLOQUES"] = $"\"{Bloque}\"", ["DOBLE_KMS"] = ClaveDelCliente });

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok …" + Arranque[^8..] + " AVAILABLE clave-del-cliente", salida, StringComparison.Ordinal);
        Assert.Contains("ok …" + Bloque[^8..] + " AVAILABLE clave-del-cliente", salida, StringComparison.Ordinal);
        Assert.Contains("Cifrado de disco verificado en 2 volumen(es)", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ConLaClaveDeOracleYExigiendoUnaDelCliente_Falla()
    {
        var (codigo, salida, _) = Ejecutar(null, "--instancia", Instancia, "--compartimento", Compartimento, "--dominio", "Abcd:SA-SAOPAULO-1-AD-1", "--exigir-clave-del-cliente");

        Assert.Equal(1, codigo);
        Assert.Contains("usa la clave por defecto de Oracle y se exigió una clave del cliente", salida, StringComparison.Ordinal);
        Assert.Contains("El cifrado de disco NO está verificado", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DOBLE_ESTADO", "TERMINATED")]
    [InlineData("DOBLE_FALLA_ARRANQUE", "1")]
    [InlineData("DOBLE_FALLA_BLOQUES", "1")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnVolumenNoDisponibleOQueNoSePuedeConsultar_NuncaSeDaPorBueno(string variable, string valor)
    {
        var (codigo, salida, _) = Ejecutar(new Dictionary<string, string> { [variable] = valor });

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("Cifrado de disco verificado", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiLaInstanciaNoTieneVolumenDeArranque_Falla()
    {
        var (codigo, salida, _) = Ejecutar(new Dictionary<string, string> { ["DOBLE_ARRANQUES"] = string.Empty });

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA la instancia no tiene volumen de arranque adjunto", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiUnVolumenDeBloqueNoSePuedeConsultar_FallaAunqueElDeArranqueEsteBien()
    {
        var (codigo, salida, _) = Ejecutar(new Dictionary<string, string> { ["DOBLE_BLOQUES"] = $"\"{Bloque}\"", ["DOBLE_ILEGIBLE"] = Bloque });

        Assert.Equal(1, codigo);
        Assert.Contains("no se pudo consultar el volumen al proveedor", salida, StringComparison.Ordinal);
        Assert.Contains("ok …" + Arranque[^8..], salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--instancia", "no-es-un-ocid")]
    [InlineData("--compartimento", "ocid1.compartment.oc1..x; rm -rf /")]
    [InlineData("--dominio", "AD 1; echo")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnUsoInvalidoSeRechazaConCodigo2_SinLlamarALaCli(string opcion, string valor)
    {
        var argumentos = new Dictionary<string, string> { ["--instancia"] = Instancia, ["--compartimento"] = Compartimento, ["--dominio"] = "Abcd:SA-SAOPAULO-1-AD-1" };
        argumentos[opcion] = valor;

        var (codigo, _, errores) = Ejecutar(null, argumentos.SelectMany(p => new[] { p.Key, p.Value }).ToArray());

        Assert.Equal(2, codigo);
        Assert.NotEmpty(errores);
        Assert.False(File.Exists(Registro));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElScriptSoloLeeDeLaNube_NoCambiaNada()
    {
        Ejecutar(new Dictionary<string, string> { ["DOBLE_BLOQUES"] = $"\"{Bloque}\"" });

        var llamadas = File.ReadAllLines(Registro);
        Assert.NotEmpty(llamadas);
        Assert.All(llamadas, l => Assert.Matches(@"^(compute (boot-volume-attachment|volume-attachment) list|bv (boot-volume|volume) get) ", l));
    }
}
