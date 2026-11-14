using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Imagenes;
using BabelHub.IntegrationTests.VmAux;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// El registro de custodia de la clave que descifra los respaldos y su comprobación (T28; PLA-17, AC-PLA-17): <c>infra/respaldos/custodia.md</c>
/// con la segunda copia obligatoria y la prueba periódica de descifrado, y <c>scripts/comprobar-custodia.sh</c>, que falla si falta algo, si la
/// custodia tiene una clave privada, si las dos copias están en el mismo lugar, si la prueba venció o si el destinatario con el que la VM de datos
/// cifra no es el registrado. Las claves de las pruebas se generan en cada ejecución.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class CustodiaDeLaClaveTests : IDisposable
{
    private const string Hoy = "2026-11-14";
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "custodia-" + Guid.NewGuid().ToString("N"));
    private readonly string _destinatario = "age1" + new string([.. Guid.NewGuid().ToString("N").Concat(Guid.NewGuid().ToString("N")).Take(58)]);

    public CustodiaDeLaClaveTests() => Directory.CreateDirectory(_carpeta);

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

    private static string Raiz => EntornoDeVmAux.RaizDelRepositorio();

    private static string Real => File.ReadAllText(Path.Combine(Raiz, "infra", "respaldos", "custodia.md"));

    /// <summary>La custodia real con los campos llenados con lo dado (la tabla del apartado 1 es la que lee el script).</summary>
    private string Llenar(string? destinatario = null, string copia1 = "caja fuerte de la oficina", string copia2 = "dispositivo cifrado en casa", string prueba = "2026-10-20", string periodo = "90", string titular = "Titular de prueba", string adicional = "")
    {
        var texto = Real;
        foreach (var (campo, valor) in new[]
        {
            ("Titular de la clave", titular), ("Destinatario público (age)", destinatario ?? _destinatario), ("Ubicación de la copia fuera de línea", copia1),
            ("Ubicación de la segunda copia (obligatoria, en otro lugar)", copia2), ("Última prueba de descifrado", prueba), ("Periodicidad de la prueba (días)", periodo),
        })
        {
            texto = Regex.Replace(texto, $@"(?m)^\| {Regex.Escape(campo)} \| .* \|$", _ => $"| {campo} | {valor} |", RegexOptions.None, TimeSpan.FromSeconds(2));
        }

        var ruta = Path.Combine(_carpeta, "custodia-" + Guid.NewGuid().ToString("N")[..6] + ".md");
        File.WriteAllText(ruta, texto + adicional);
        return ruta;
    }

    private static string Bash() =>
        OperatingSystem.IsWindows() && File.Exists(@"C:\Program Files\Git\bin\bash.exe") ? @"C:\Program Files\Git\bin\bash.exe" : "bash";

    private static (int Codigo, string Salida, string Errores) Comprobar(params string[] argumentos)
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EntornoDeVmAux.EnLinux(Path.Combine(Raiz, "scripts", "comprobar-custodia.sh")));
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(1)), "El script no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    private (int Codigo, string Salida, string Errores) ComprobarCon(string custodia, params string[] extra) =>
        Comprobar([.. new[] { "--custodia", EntornoDeVmAux.EnLinux(custodia), "--hoy", Hoy }, .. extra]);

    // ---------- El registro tal como está en el repositorio ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ElRegistroDeCustodia_TieneLosCamposYElProcedimiento_ExigeLaSegundaCopia_YNoGuardaNingunaClavePrivada()
    {
        var texto = Real;

        foreach (var campo in new[] { "Titular de la clave", "Destinatario público (age)", "Ubicación de la copia fuera de línea", "Ubicación de la segunda copia (obligatoria, en otro lugar)", "Última prueba de descifrado", "Periodicidad de la prueba (días)" })
        {
            Assert.Matches($@"(?m)^\| {Regex.Escape(campo)} \| .+ \|$", texto);
        }

        Assert.Contains("segunda copia", texto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obligatoria", texto, StringComparison.Ordinal);
        Assert.Contains("prueba periódica de descifrado", texto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rotación", texto, StringComparison.Ordinal);
        Assert.Contains("fuera de la VM, del repositorio y del contenedor de respaldos", texto, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"AGE-SECRET-KEY-[0-9A-Z]{10,}", texto);
        Assert.DoesNotMatch(@"age1[0-9a-z]{58}", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ElRegistroSinLlenar_NoPasa_PorqueSinLaClaveYSuCustodiaNoHayComoRestaurar()
    {
        var (codigo, salida, _) = Comprobar("--custodia", EntornoDeVmAux.EnLinux(Path.Combine(Raiz, "infra", "respaldos", "custodia.md")), "--hoy", Hoy);

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA faltan datos en el registro de custodia (pendiente): titular destinatario copia-fuera-de-linea segunda-copia ultima-prueba", salida, StringComparison.Ordinal);
    }

    // ---------- La comprobación ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ConElRegistroCompleto_ConDosCopiasDistintas_YUnaPruebaReciente_Pasa()
    {
        var (codigo, salida, errores) = ComprobarCon(Llenar());

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok el archivo de custodia no contiene claves privadas", salida, StringComparison.Ordinal);
        Assert.Contains("ok el registro de custodia está completo", salida, StringComparison.Ordinal);
        Assert.Contains("ok hay una segunda copia en un lugar distinto", salida, StringComparison.Ordinal);
        Assert.Contains("ok la última prueba de descifrado es reciente (2026-10-20, cada 90 días como máximo)", salida, StringComparison.Ordinal);
        Assert.Contains("Custodia de la clave: en regla.", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("titular", "Titular de la clave")]
    [InlineData("segunda-copia", "Ubicación de la segunda copia (obligatoria, en otro lugar)")]
    [InlineData("copia-fuera-de-linea", "Ubicación de la copia fuera de línea")]
    [InlineData("ultima-prueba", "Última prueba de descifrado")]
    [InlineData("periodicidad", "Periodicidad de la prueba (días)")]
    [InlineData("destinatario", "Destinatario público (age)")]
    [Trait("Criterio", "AC-PLA-17")]
    public void UnCampoPendienteOVacio_HaceFallar_NombrandoElCampo(string etiqueta, string campo)
    {
        var ruta = Llenar();
        File.WriteAllText(ruta, Regex.Replace(File.ReadAllText(ruta), $@"(?m)^\| {Regex.Escape(campo)} \| .* \|$", $"| {campo} | pendiente |", RegexOptions.None, TimeSpan.FromSeconds(2)));

        var (codigo, salida, _) = ComprobarCon(ruta);

        Assert.Equal(1, codigo);
        Assert.Contains($"(pendiente): {etiqueta}", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void SiLaSegundaCopiaEstaEnElMismoLugarQueLaPrimera_Falla()
    {
        var (codigo, salida, _) = ComprobarCon(Llenar(copia1: "Caja fuerte", copia2: "caja fuerte"));

        Assert.Equal(1, codigo);
        Assert.Contains("la segunda copia está en el mismo lugar que la primera", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("age1corta")]
    [InlineData("AGE1MAYUSCULAS0000000000000000000000000000000000000000000000")]
    [InlineData("no-es-un-destinatario")]
    [Trait("Criterio", "AC-PLA-17")]
    public void UnDestinatarioConFormaInvalida_Falla(string destinatario)
    {
        var (codigo, salida, _) = ComprobarCon(Llenar(destinatario: destinatario));

        Assert.Equal(1, codigo);
        Assert.Contains("no tiene la forma de una clave pública de age", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-08-01", "tiene más de 90 días")]
    [InlineData("2026-12-01", "es una fecha futura")]
    [InlineData("ayer", "no es una fecha válida")]
    [InlineData("2026-13-45", "no es una fecha válida")]
    [Trait("Criterio", "AC-PLA-17")]
    public void UnaPruebaDeDescifradoVencidaFuturaOInvalida_Falla(string prueba, string motivo)
    {
        var (codigo, salida, _) = ComprobarCon(Llenar(prueba: prueba));

        Assert.Equal(1, codigo);
        Assert.Contains(motivo, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void LaPeriodicidadRegistradaManda_UnaPruebaDe40DiasVenceConPeriodoDe30YPasaConElDeNoventa()
    {
        var vencida = ComprobarCon(Llenar(prueba: "2026-10-05", periodo: "30"));
        var vigente = ComprobarCon(Llenar(prueba: "2026-10-05", periodo: "90"));

        Assert.Equal(1, vencida.Codigo);
        Assert.Contains("tiene más de 30 días", vencida.Salida, StringComparison.Ordinal);
        Assert.Equal(0, vigente.Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void UnaClavePrivadaEnLaCustodia_SeRechaza_SinImprimirla()
    {
        var privada = "AGE-SECRET-" + "KEY-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

        var (codigo, salida, errores) = ComprobarCon(Llenar(adicional: "\nnota: " + privada + "\n"));

        Assert.Equal(1, codigo);
        Assert.Contains("el archivo de custodia contiene una clave privada de age", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(privada, salida + errores, StringComparison.Ordinal);
    }

    // ---------- El destinatario con el que cifra la VM de datos ----------

    private string EntornoDeLaVm(string linea)
    {
        var ruta = Path.Combine(_carpeta, "postgres-" + Guid.NewGuid().ToString("N")[..6] + ".env");
        File.WriteAllText(ruta, "POSTGRES_PASSWORD=" + Guid.NewGuid().ToString("N") + "\n" + linea + "\n");
        return EntornoDeVmAux.EnLinux(ruta);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ConElDestinatarioDeLaVmIgualAlRegistrado_Pasa_PorEntornoOPorArgumento()
    {
        var custodia = Llenar();

        var porEntorno = ComprobarCon(custodia, "--entorno", EntornoDeLaVm("BABELHUB_AGE_DESTINATARIO=" + _destinatario));
        var porArgumento = ComprobarCon(custodia, "--destinatario", _destinatario);

        Assert.Equal(0, porEntorno.Codigo);
        Assert.Contains("ok el destinatario con el que la VM de datos cifra coincide con el registrado en la custodia", porEntorno.Salida, StringComparison.Ordinal);
        Assert.Equal(0, porArgumento.Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ConUnDestinatarioDeLaVmDistintoDelRegistrado_Falla_PorqueNadieDescifrariaLosRespaldos()
    {
        var otro = "age1" + new string([.. Guid.NewGuid().ToString("N").Concat(Guid.NewGuid().ToString("N")).Take(58)]);

        var (codigo, salida, _) = ComprobarCon(Llenar(), "--entorno", EntornoDeLaVm("BABELHUB_AGE_DESTINATARIO=" + otro));

        Assert.Equal(1, codigo);
        Assert.Contains("NO coincide con el registrado en la custodia", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(otro, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void SiLaVmDeDatosTieneUnaClavePrivadaEnVezDelDestinatario_SeRechaza_SinImprimirla()
    {
        var privada = "AGE-SECRET-" + "KEY-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

        var (codigo, salida, errores) = ComprobarCon(Llenar(), "--entorno", EntornoDeLaVm("BABELHUB_AGE_DESTINATARIO=" + privada));

        Assert.Equal(1, codigo);
        Assert.Contains("no es una clave pública de age", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(privada, salida + errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void SiElEntornoDeLaVmNoDefineElDestinatario_LaVmNoTendriaConQueCifrar()
    {
        var (codigo, salida, _) = ComprobarCon(Llenar(), "--entorno", EntornoDeLaVm("OTRA=cosa"));

        Assert.Equal(1, codigo);
        Assert.Contains("no define BABELHUB_AGE_DESTINATARIO", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void SiLaCustodiaNoRegistraElDestinatario_NoSePuedeComparar()
    {
        var ruta = Llenar();
        File.WriteAllText(ruta, Regex.Replace(File.ReadAllText(ruta), @"(?m)^\| Destinatario público \(age\) \| .* \|$", "| Destinatario público (age) | pendiente |", RegexOptions.None, TimeSpan.FromSeconds(2)));

        var (codigo, salida, _) = ComprobarCon(ruta, "--destinatario", _destinatario);

        Assert.Equal(1, codigo);
        Assert.Contains("la custodia no registra todavía el destinatario público", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--entorno", "x", "--destinatario", "age1x")]
    [InlineData("--hoy", "ayer")]
    [InlineData("--custodia")]
    [InlineData("--desconocido")]
    [Trait("Criterio", "AC-PLA-17")]
    public void UnUsoInvalido_SeRechazaConCodigo2(params string[] argumentos)
    {
        var (codigo, _, errores) = Comprobar(argumentos);

        Assert.Equal(2, codigo);
        Assert.NotEmpty(errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void SiNoExisteLaCustodiaOElEntorno_SeRechazaConCodigo2()
    {
        Assert.Equal(2, Comprobar("--custodia", EntornoDeVmAux.EnLinux(Path.Combine(_carpeta, "no-existe.md"))).Codigo);
        Assert.Equal(2, ComprobarCon(Llenar(), "--entorno", EntornoDeVmAux.EnLinux(Path.Combine(_carpeta, "no-existe.env"))).Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ElComprobadorSoloLee_NoCambiaLaCustodia()
    {
        var ruta = Llenar();
        var antes = File.ReadAllText(ruta);

        ComprobarCon(ruta);

        Assert.Equal(antes, File.ReadAllText(ruta));
    }

    [Theory]
    [InlineData("comprobar-custodia.sh")]
    [InlineData("descifrar-respaldo.sh")]
    [InlineData("extraer-respaldo.sh")]
    [Trait("Criterio", "AC-PLA-17")]
    public void LosScriptsDeCustodia_NoTienenCrNiClavesNiTraza(string nombre)
    {
        var texto = File.ReadAllText(Path.Combine(Raiz, "scripts", nombre));

        Assert.DoesNotContain('\r', texto);
        Assert.DoesNotMatch(@"AGE-SECRET-KEY-[0-9A-Z]{10,}", texto);
        Assert.DoesNotMatch(@"(?m)^\s*set\s+-[a-z]*x", texto);
        Assert.Contains("set -uo pipefail", texto, StringComparison.Ordinal);
    }
}
