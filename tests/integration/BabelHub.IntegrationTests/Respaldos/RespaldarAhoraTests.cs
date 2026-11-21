using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// El respaldo bajo demanda tras una corrección urgente con migración (PLA-09, PLA-16): <c>scripts/respaldar-ahora.sh</c> lo lanza la persona
/// responsable desde su equipo por ProxyJump, sin reenvío de agente, sin llevar ni imprimir secretos, y devuelve el código del respaldo remoto.
/// Y los archivos que lo hacen posible: la imagen de PostgreSQL trae los scripts, el Compose archiva los cambios y todo está en POSIX sh.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class RespaldarAhoraTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "ahora-" + Guid.NewGuid().ToString("N"));

    public RespaldarAhoraTests() => Directory.CreateDirectory(_carpeta);

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

    private static string EnLinux(string ruta) => ruta.Replace('\\', '/');

    private (int Codigo, string Salida, string Errores, string Llamada) Ejecutar(int codigoRemoto, params string[] argumentos)
    {
        var doble = Path.Combine(_carpeta, "ssh-doble.sh");
        var registro = Path.Combine(_carpeta, "llamada.txt");
        ArchivosDeDoble.Escribir(doble, $"#!/usr/bin/env bash\nprintf '%s\\n' \"$@\" > '{EnLinux(registro)}'\nexit {codigoRemoto}\n");
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(ComposeDeVms.Raiz(), "scripts", "respaldar-ahora.sh")));
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        informacion.Environment["SSH"] = EnLinux(doble);
        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(1)));
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result, File.Exists(registro) ? File.ReadAllText(registro) : string.Empty);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void ElRespaldoBajoDemanda_VaPorProxyJump_SinReenvioDeAgente_ElContenedorDePostgreSql()
    {
        var (codigo, _, _, llamada) = Ejecutar(0, "--salto", "operador@aux.interno", "--destino", "operador@inventario.interno");

        Assert.Equal(0, codigo);
        var lineas = llamada.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("ForwardAgent=no", lineas);
        Assert.Contains("BatchMode=yes", lineas);
        Assert.Equal("operador@aux.interno", lineas[Array.IndexOf(lineas, "-J") + 1]);
        Assert.Equal(["operador@inventario.interno", "docker", "exec", "babelhub-inventario-postgres-1", "/opt/babelhub/respaldar.sh", "--bajo-demanda"], lineas[(Array.IndexOf(lineas, "operador@inventario.interno"))..]);
        Assert.DoesNotContain("-A", lineas);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData(1)]
    [InlineData(75)]
    public void ElCodigoDelRespaldoRemoto_SePropaga(int remoto)
    {
        var (codigo, _, errores, _) = Ejecutar(remoto, "--salto", "operador@aux.interno", "--destino", "operador@inventario.interno");

        Assert.Equal(remoto, codigo);
        Assert.Contains($"código {remoto}", errores, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--destino", "operador@inventario.interno")]
    [InlineData("--salto", "operador@aux.interno")]
    [InlineData("--salto", "x;rm -rf /", "--destino", "operador@inventario.interno")]
    [InlineData("--salto", "operador@aux.interno", "--destino", "operador@inv$(id)")]
    [InlineData("--salto", "operador@aux.interno", "--destino", "operador@inventario.interno", "--contenedor", "a b;c")]
    [InlineData("--salto", "operador@aux.interno", "--destino", "operador@inventario.interno", "--otra", "cosa")]
    [InlineData]
    public void UnUsoInvalido_SeRechazaConCodigo2_SinLlamarASsh(params string[] argumentos)
    {
        var (codigo, _, errores, llamada) = Ejecutar(0, argumentos);

        Assert.Equal(2, codigo);
        Assert.False(string.IsNullOrWhiteSpace(errores));
        Assert.Equal(string.Empty, llamada);
    }

    // ---------- Los archivos que lo hacen posible ----------

    private static string Texto(string ruta) => File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), ruta));

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public void LaImagenDePostgreSqlTraeLosScriptsDeRespaldoYUnVolumenLocalParaLoPendiente()
    {
        var dockerfile = Regex.Replace(Texto("infra/postgres/Dockerfile"), @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.Contains("COPY respaldos/ /opt/babelhub/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/var/respaldos", dockerfile, StringComparison.Ordinal);
        Assert.Matches(@"(?s)USER root.*USER postgres\s*$", dockerfile);
        foreach (var script in new[] { "comun.sh", "archivar-wal.sh", "respaldar.sh" })
        {
            Assert.True(File.Exists(Path.Combine(ComposeDeVms.Raiz(), "infra", "postgres", "respaldos", script)), script);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-09")]
    [InlineData("infra/postgres/respaldos/comun.sh")]
    [InlineData("infra/postgres/respaldos/archivar-wal.sh")]
    [InlineData("infra/postgres/respaldos/respaldar.sh")]
    public void LosScriptsDeLaImagen_SonPosixShConSaltosDeLineaLF_YNoUsanBash(string ruta)
    {
        var texto = Texto(ruta);

        Assert.DoesNotContain('\r', texto);
        Assert.DoesNotContain("bash", Regex.Replace(texto, @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2)), StringComparison.Ordinal);
        Assert.DoesNotContain("[[", texto, StringComparison.Ordinal);
        if (!ruta.EndsWith("comun.sh", StringComparison.Ordinal))
        {
            Assert.StartsWith("#!/bin/sh\n", texto, StringComparison.Ordinal);
            Assert.Contains("\nset -eu\n", texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public void ElComposeDeInventarioArchivaLosCambiosAlAlmacen_ConLaConfiguracionYElVolumenLocal()
    {
        var compose = Texto("infra/docker-compose.inventario.yml");

        Assert.Contains("archive_mode=on", compose, StringComparison.Ordinal);
        Assert.Contains("archive_command=/opt/babelhub/archivar-wal.sh %p %f", compose, StringComparison.Ordinal);
        Assert.Contains("archive_timeout=300", compose, StringComparison.Ordinal);
        Assert.Contains("respaldo-espera:/var/respaldos", compose, StringComparison.Ordinal);
        Assert.Contains("./:/config:ro", compose, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void NingunScriptNiArchivoDeRespaldoLlevaUnaClavePrivadaDeAge()
    {
        foreach (var ruta in new[] { "infra/postgres/respaldos/respaldar.sh", "infra/postgres/respaldos/archivar-wal.sh", "scripts/respaldar-ahora.sh", "infra/docker-compose.inventario.yml" })
        {
            Assert.DoesNotMatch(@"AGE-SECRET-KEY-[0-9A-Z]{10,}", Texto(ruta));
        }
    }
}
