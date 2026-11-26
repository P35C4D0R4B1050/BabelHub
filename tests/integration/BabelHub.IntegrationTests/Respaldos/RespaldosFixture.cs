using System.Text;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.IntegrationTests.Imagenes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// El entorno de las pruebas de respaldo (PLA-09, 15, 16, 17): la imagen de PostgreSQL de <c>infra/postgres</c> tal como se publica (con
/// <c>age</c>, <c>rclone</c> y los scripts de <c>infra/postgres/respaldos</c>), un almacén compatible con S3 con los cuatro contenedores de
/// respaldo, y un par de claves de <c>age</c> generado en cada ejecución: a la VM de datos solo llega la pública; la privada se queda aquí,
/// como la copia fuera de línea de la persona responsable. Nada de esto usa credenciales de producción.
/// </summary>
public sealed partial class RespaldosFixture : IAsyncLifetime
{
    public const string Diarios = "respaldos-diarios";
    public const string Semanales = "respaldos-semanales";
    public const string Mensuales = "respaldos-mensuales";
    public const string Wal = "respaldos-wal";
    private const string ClaveDeAcceso = "acceso-de-pruebas-de-respaldos-0123456789";

    private readonly string _claveSecreta = "s" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private readonly string _etiqueta = "bh-respaldos-pruebas:" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _carpetaDeConfiguracion = Path.Combine(Path.GetTempPath(), "respaldos-config-" + Guid.NewGuid().ToString("N"));
    private readonly string _contrasenaDeLaBase = "p" + Guid.NewGuid().ToString("N");
    private INetwork _red = null!;
    private IContainer _almacen = null!;
    private Lazy<Task<ResultadoDeRespaldo>>? _primerRespaldo;

    public IContainer Postgres { get; private set; } = null!;

    /// <summary>La etiqueta de la imagen de PostgreSQL construida para las pruebas y la red del almacén, para levantar otros contenedores iguales.</summary>
    public string Etiqueta => _etiqueta;

    public INetwork Red => _red;

    public string ClaveDeAccesoDelAlmacen => ClaveDeAcceso;

    public IAmazonS3 S3 { get; private set; } = null!;

    /// <summary>La clave pública de age: lo único que la VM de datos conoce.</summary>
    public string Destinatario { get; private set; } = string.Empty;

    /// <summary>La clave que descifra: solo la tiene la prueba (la copia fuera de línea).</summary>
    public string Identidad { get; private set; } = string.Empty;

    public string SecretoDeLaConfiguracion { get; } = "marca-secreta-de-la-configuracion-" + Guid.NewGuid().ToString("N");

    public string MarcaDeDatos { get; } = "marca-de-datos-de-negocio-" + Guid.NewGuid().ToString("N");

    public string ClaveSecretaDelAlmacen => _claveSecreta;

    public Task<ResultadoDeRespaldo> PrimerRespaldo() => (_primerRespaldo ??= new Lazy<Task<ResultadoDeRespaldo>>(HacerElPrimerRespaldo)).Value;

    public async Task InitializeAsync()
    {
        Construir();
        (Destinatario, Identidad) = GenerarClaves();

        _red = new NetworkBuilder().Build();
        await _red.CreateAsync();
        _almacen = new ContainerBuilder("rustfs/rustfs:latest")
            .WithNetwork(_red)
            .WithNetworkAliases("almacen")
            .WithPortBinding(9000, true)
            .WithEnvironment("RUSTFS_ACCESS_KEY", ClaveDeAcceso)
            .WithEnvironment("RUSTFS_SECRET_KEY", _claveSecreta)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(9000))
            .Build();
        await _almacen.StartAsync();

        S3 = new AmazonS3Client(
            ClaveDeAcceso,
            _claveSecreta,
            new AmazonS3Config { ServiceURL = $"http://{_almacen.Hostname}:{_almacen.GetMappedPublicPort(9000)}", ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        foreach (var contenedor in new[] { Diarios, Semanales, Mensuales, Wal })
        {
            await CrearContenedor(contenedor);
        }

        Directory.CreateDirectory(_carpetaDeConfiguracion);
        File.WriteAllText(Path.Combine(_carpetaDeConfiguracion, "docker-compose.prueba.yml"), "name: prueba\nservices: {}\n");
        File.WriteAllText(Path.Combine(_carpetaDeConfiguracion, "postgres.env"), "POSTGRES_PASSWORD=" + SecretoDeLaConfiguracion + "\n");
        File.WriteAllText(Path.Combine(_carpetaDeConfiguracion, "certificado-key.pem"), SecretoDeLaConfiguracion + "\n");
        File.WriteAllText(Path.Combine(_carpetaDeConfiguracion, "llave.key"), SecretoDeLaConfiguracion + "\n");

        Postgres = new ContainerBuilder(_etiqueta)
            .WithNetwork(_red)
            .WithEnvironment("POSTGRES_PASSWORD", _contrasenaDeLaBase)
            .WithEnvironment("BABELHUB_AGE_DESTINATARIO", Destinatario)
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_TYPE", "s3")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_PROVIDER", "Other")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_ENDPOINT", "http://almacen:9000")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_REGION", "us-east-1")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID", ClaveDeAcceso)
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY", _claveSecreta)
            .WithBindMount(_carpetaDeConfiguracion.Replace('\\', '/'), "/config", AccessMode.ReadOnly)
            .WithCommand("postgres", "-c", "archive_mode=on", "-c", "archive_command=/opt/babelhub/archivar-wal.sh %p %f", "-c", "archive_timeout=5")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("PostgreSQL init process complete; ready for start up."))
            .Build();
        await Postgres.StartAsync();
        await EsperarALaBase();

        foreach (var baseDeDatos in new[] { "ecommerce", "inventario" })
        {
            await Psql("postgres", $"CREATE DATABASE {baseDeDatos}");
            await Psql(baseDeDatos, $"CREATE TABLE datos(marca text); INSERT INTO datos VALUES ('{MarcaDeDatos}-{baseDeDatos}'); CREATE TABLE latido(n bigint); INSERT INTO latido VALUES (0)");
        }
    }

    public async Task DisposeAsync()
    {
        S3?.Dispose();
        if (Postgres is not null)
        {
            await Postgres.DisposeAsync();
        }

        if (_almacen is not null)
        {
            await _almacen.DisposeAsync();
        }

        if (_red is not null)
        {
            await _red.DeleteAsync();
        }

        ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "rmi", "-f", _etiqueta);
        try
        {
            Directory.Delete(_carpetaDeConfiguracion, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal.
        }
    }

    private void Construir()
    {
        var (codigo, _, errores) = ComposeDeVms.Docker(TimeSpan.FromMinutes(10), null, "build", "-t", _etiqueta, "postgres");
        if (codigo != 0)
        {
            throw new InvalidOperationException("No se pudo construir la imagen de PostgreSQL: " + errores);
        }
    }

    private (string Destinatario, string Identidad) GenerarClaves()
    {
        var (codigo, salida, errores) = ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "run", "--rm", "--entrypoint", "age-keygen", _etiqueta);
        var texto = salida + errores;
        var publica = ClavePublica().Match(texto);
        var privada = ClavePrivada().Match(texto);
        if (codigo != 0 || !publica.Success || !privada.Success)
        {
            throw new InvalidOperationException("age-keygen no generó las claves.");
        }

        return (publica.Groups[1].Value, privada.Groups[1].Value);
    }

    private async Task CrearContenedor(string nombre)
    {
        for (var intento = 0; intento < 40; intento++)
        {
            try
            {
                await S3.PutBucketAsync(nombre);
                return;
            }
            catch (Exception) when (intento < 39)
            {
                await Task.Delay(500);
            }
        }
    }

    private async Task EsperarALaBase()
    {
        for (var intento = 0; intento < 60; intento++)
        {
            var resultado = await Postgres.ExecAsync(["psql", "-U", "postgres", "-d", "postgres", "-tAc", "SELECT 1"]);
            if (resultado.ExitCode == 0)
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("PostgreSQL no quedó listo.");
    }

    public async Task<ExecResult> Psql(string baseDeDatos, string sentencia) =>
        await Postgres.ExecAsync(["psql", "-U", "postgres", "-d", baseDeDatos, "-v", "ON_ERROR_STOP=1", "-tAc", sentencia]);

    public async Task<ExecResult> Sh(string orden) => await Postgres.ExecAsync(["sh", "-c", orden]);

    public async Task<List<string>> Claves(string contenedor)
    {
        var claves = new List<string>();
        string? continuar = null;
        do
        {
            var respuesta = await S3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = contenedor, ContinuationToken = continuar });
            claves.AddRange((respuesta.S3Objects ?? []).Select(o => o.Key));
            continuar = respuesta.IsTruncated == true ? respuesta.NextContinuationToken : null;
        }
        while (continuar is not null);

        return claves;
    }

    public async Task<byte[]> Descargar(string contenedor, string clave)
    {
        using var respuesta = await S3.GetObjectAsync(contenedor, clave);
        using var memoria = new MemoryStream();
        await respuesta.ResponseStream.CopyToAsync(memoria);
        return memoria.ToArray();
    }

    /// <summary>Descifra y descomprime (D25) en «otra máquina»: un contenedor aparte que solo tiene la copia de la clave privada y el objeto.
    /// Falla si lo descifrado no es zstd.</summary>
    public async Task<byte[]> Descifrar(byte[] cifrado)
    {
        await using var otraMaquina = new ContainerBuilder(_etiqueta)
            .WithEntrypoint("sleep", "600")
            .WithResourceMapping(Encoding.UTF8.GetBytes(Identidad + "\n"), "/tmp/identidad.txt")
            .WithResourceMapping(cifrado, "/tmp/entrada.age")
            .Build();
        await otraMaquina.StartAsync();
        var resultado = await otraMaquina.ExecAsync(["sh", "-c", "age -d -i /tmp/identidad.txt -o /tmp/salida.zst /tmp/entrada.age && zstd -q -d -o /tmp/salida /tmp/salida.zst"]);
        if (resultado.ExitCode != 0)
        {
            throw new InvalidOperationException("No se pudo descifrar: " + resultado.Stderr);
        }

        return await otraMaquina.ReadFileAsync("/tmp/salida");
    }

    /// <summary>Lo que hay tras ejecutar una orden de respaldo: las claves nuevas de cada contenedor.</summary>
    public async Task<ResultadoDeRespaldo> Respaldar(string argumentos = "", string? entorno = null)
    {
        var antes = await InstantaneaDeClaves();
        var ejecucion = await Sh($"{(entorno is null ? string.Empty : entorno + " ")}/opt/babelhub/respaldar.sh {argumentos}");
        var despues = await InstantaneaDeClaves();
        return new ResultadoDeRespaldo(
            ejecucion,
            despues[Diarios].Except(antes[Diarios]).ToList(),
            despues[Semanales].Except(antes[Semanales]).ToList(),
            despues[Mensuales].Except(antes[Mensuales]).ToList(),
            despues[Wal].Except(antes[Wal]).ToList());
    }

    private async Task<ResultadoDeRespaldo> HacerElPrimerRespaldo() => await Respaldar();

    private async Task<Dictionary<string, List<string>>> InstantaneaDeClaves() =>
        new()
        {
            [Diarios] = await Claves(Diarios),
            [Semanales] = await Claves(Semanales),
            [Mensuales] = await Claves(Mensuales),
            [Wal] = await Claves(Wal),
        };

    [GeneratedRegex(@"public key:\s*(age1[0-9a-z]+)", RegexOptions.None, 2000)]
    private static partial Regex ClavePublica();

    [GeneratedRegex(@"(AGE-SECRET-KEY-[0-9A-Z]+)", RegexOptions.None, 2000)]
    private static partial Regex ClavePrivada();
}

/// <summary>Lo que hizo una ejecución de <c>respaldar.sh</c>: su salida y los objetos nuevos que aparecieron en cada contenedor (WAL incluido).</summary>
public sealed record ResultadoDeRespaldo(ExecResult Ejecucion, IReadOnlyList<string> Diarios, IReadOnlyList<string> Semanales, IReadOnlyList<string> Mensuales, IReadOnlyList<string> Wal);

[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class RespaldosCollection : ICollectionFixture<RespaldosFixture>
{
    public const string Nombre = "Respaldos";
}
