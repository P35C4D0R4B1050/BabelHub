using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// AC-PLA-03: cada imagen propia se construye y arranca en ARM64 y en x86_64 con el mismo comportamiento: el componente arranca,
/// responde su comprobación de salud y da el mismo resultado en las dos arquitecturas, sin privilegios. ARM64 corre en emulación
/// (QEMU de Docker Desktop o <c>docker/setup-qemu-action</c> en CI). Construyen imágenes de verdad: tardan unos minutos la primera vez.
/// </summary>
public sealed class HumoDeImagenesTests : IDisposable
{
    private static readonly string[] Arquitecturas = ["amd64", "arm64"];
    private readonly List<string> _contenedores = [];
    private readonly List<string> _imagenes = [];

    public void Dispose()
    {
        foreach (var contenedor in _contenedores)
        {
            Docker(TimeSpan.FromSeconds(30), "stop", "--timeout", "2", contenedor);
            Docker(TimeSpan.FromSeconds(30), "rm", contenedor);
        }

        foreach (var imagen in _imagenes)
        {
            Docker(TimeSpan.FromSeconds(60), "rmi", "--force", imagen);
        }
    }

    private static string Raiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static (int Codigo, string Salida) Docker(TimeSpan plazo, params string[] argumentos)
    {
        var informacion = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Raiz() };
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        if (!proceso.WaitForExit(plazo))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException($"docker {string.Join(' ', argumentos.Take(3))} no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, (salida.Result + errores.Result).Trim());
    }

    private static string Exigir((int Codigo, string Salida) resultado, string que)
    {
        Assert.True(resultado.Codigo == 0, $"{que}: {resultado.Salida}");
        return resultado.Salida;
    }

    private static void ExigirEmulacionDeArm64()
    {
        var salida = Exigir(Docker(TimeSpan.FromSeconds(30), "buildx", "ls"), "No se pudo listar los constructores de Docker");
        Assert.True(
            salida.Contains("linux/arm64", StringComparison.Ordinal),
            "Docker no puede construir para linux/arm64: instala la emulación (QEMU de Docker Desktop o docker/setup-qemu-action).");
    }

    private string Construir(string contexto, string? dockerfile, string arquitectura, string nombre)
    {
        var etiqueta = $"babelhub-humo-{nombre}-{arquitectura}-{Guid.NewGuid().ToString("N")[..8]}";
        string[] argumentos = dockerfile is null
            ? ["buildx", "build", "--platform", $"linux/{arquitectura}", "--load", "--tag", etiqueta, contexto]
            : ["buildx", "build", "--platform", $"linux/{arquitectura}", "--load", "--tag", etiqueta, "--file", dockerfile, contexto];
        Exigir(Docker(TimeSpan.FromMinutes(10), argumentos), $"No se pudo construir {nombre} para {arquitectura}");
        _imagenes.Add(etiqueta);
        return etiqueta;
    }

    private string Arrancar(string imagen, string arquitectura, params string[] extra)
    {
        var nombre = "babelhub-humo-" + Guid.NewGuid().ToString("N")[..12];
        Exigir(Docker(TimeSpan.FromMinutes(1), ["run", "--detach", "--name", nombre, "--platform", $"linux/{arquitectura}", .. extra, imagen]), "No se pudo arrancar el contenedor");
        _contenedores.Add(nombre);
        return nombre;
    }

    /// <summary>Espera la salud del contenedor. La arquitectura que no es la del equipo corre en emulación (QEMU), donde un servicio .NET tarda varios minutos en arrancar: se le da un plazo mayor, sin cambiar lo que se exige.</summary>
    private static async Task<(string Vivo, string Listo)> EsperarSalud(string contenedor, string arquitectura)
    {
        var nativa = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "amd64";
        var plazo = arquitectura == nativa ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(12);
        var puerto = Exigir(Docker(TimeSpan.FromSeconds(30), "port", contenedor, "8080/tcp"), "No se pudo leer el puerto").Split('\n')[0].Split(':')[^1];
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var limite = DateTime.UtcNow.Add(plazo);
        Exception? ultimo = null;
        while (DateTime.UtcNow < limite)
        {
            try
            {
                using var vivo = await cliente.GetAsync($"http://127.0.0.1:{puerto}/health/live");
                if (vivo.IsSuccessStatusCode)
                {
                    using var listo = await cliente.GetAsync($"http://127.0.0.1:{puerto}/health/ready");
                    return ((int)vivo.StatusCode + " " + await vivo.Content.ReadAsStringAsync(), (int)listo.StatusCode + " " + await listo.Content.ReadAsStringAsync());
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                ultimo = error;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        var estado = Docker(TimeSpan.FromSeconds(30), "inspect", "--format", "{{.State.Status}} (código {{.State.ExitCode}})", contenedor).Salida;
        var registro = Docker(TimeSpan.FromSeconds(30), "logs", "--tail", "40", contenedor).Salida;
        throw new TimeoutException($"El contenedor {contenedor} ({arquitectura}) no respondió su salud en {plazo.TotalMinutes:0} min: {ultimo?.Message}. Estado: {estado}. Últimas líneas del registro: {registro}");
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-03")]
    [InlineData("Ecommerce")]
    [InlineData("Inventario")]
    public async Task UnServicioArrancaYRespondeSuSaludConElMismoResultadoEnArm64YX86SinPrivilegios(string servicio)
    {
        ExigirEmulacionDeArm64();
        var resultados = new Dictionary<string, (string Vivo, string Listo)>();

        foreach (var arquitectura in Arquitecturas)
        {
            var imagen = Construir(".", $"src/BabelHub.{servicio}/BabelHub.{servicio}.Api/Dockerfile", arquitectura, servicio.ToLowerInvariant());
            var contenedor = Arrancar(imagen, arquitectura, "--publish", "127.0.0.1::8080", "--env", "BABELHUB__Web__Host=tienda", "--env", "BABELHUB__Web__HostsPermitidos=www.babelhub.example");
            resultados[arquitectura] = await EsperarSalud(contenedor, arquitectura);

            // La arquitectura del contenedor es la pedida y no corre como root.
            var maquina = Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "uname", "-m"), "uname");
            Assert.Equal(arquitectura == "amd64" ? "x86_64" : "aarch64", maquina);
            Assert.NotEqual("0", Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "id", "-u"), "id"));

            // La comprobación de salud de la imagen (la misma que usa Docker) funciona dentro del contenedor.
            var prueba = JsonDocument.Parse(Exigir(Docker(TimeSpan.FromSeconds(30), "inspect", "--format", "{{json .Config.Healthcheck.Test}}", contenedor), "inspect")).RootElement
                .EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.Equal(["CMD", "bash", "-c"], prueba.Take(3));
            Assert.Equal(0, Docker(TimeSpan.FromSeconds(60), ["exec", contenedor, .. prueba.Skip(1)]).Codigo);
        }

        Assert.StartsWith("200 ", resultados["amd64"].Vivo, StringComparison.Ordinal);
        Assert.Equal(resultados["amd64"], resultados["arm64"]);
        Assert.Contains("\"estado\":\"listo\"", resultados["arm64"].Listo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public async Task LaImagenDePostgreSqlArrancaConAgeYElClienteS3EnArm64YX86SinPrivilegios()
    {
        ExigirEmulacionDeArm64();
        foreach (var arquitectura in Arquitecturas)
        {
            var imagen = Construir("infra/postgres", null, arquitectura, "postgres");
            var contenedor = Arrancar(imagen, arquitectura, "--env", "POSTGRES_PASSWORD=" + Guid.NewGuid().ToString("N"));

            var limite = DateTime.UtcNow.AddMinutes(3);
            while (Docker(TimeSpan.FromSeconds(30), "exec", contenedor, "pg_isready", "-q").Codigo != 0)
            {
                Assert.True(DateTime.UtcNow < limite, $"PostgreSQL no quedó listo en {arquitectura}.");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            Assert.Equal(arquitectura == "amd64" ? "x86_64" : "aarch64", Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "uname", "-m"), "uname"));
            Assert.Equal("70", Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "id", "-u"), "id"));
            Assert.Contains("1.3.2", Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "age", "--version"), "age"), StringComparison.Ordinal);
            Assert.Contains("rclone v1.75.2", Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "rclone", "version"), "rclone"), StringComparison.Ordinal);

            // El cifrado con `age` funciona y lo cifrado no se lee con otra clave (lo que necesita el archivado de cambios).
            const string Guion = """
                set -e
                cd /tmp
                age-keygen -o a.txt 2>/dev/null
                age-keygen -o b.txt 2>/dev/null
                printf 'contenido del respaldo' > plano.txt
                age -r "$(age-keygen -y a.txt)" -o cifrado.age plano.txt
                test "$(age -d -i a.txt cifrado.age)" = 'contenido del respaldo'
                if age -d -i b.txt cifrado.age >/dev/null 2>&1; then echo 'se leyó con otra clave'; exit 1; fi
                ! grep -q 'contenido del respaldo' cifrado.age
                echo ok
                """;
            Assert.Equal("ok", Exigir(Docker(TimeSpan.FromSeconds(60), "exec", contenedor, "sh", "-c", Guion), "age"));
        }
    }
}
