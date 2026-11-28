using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// PLA-26 y PLA-27 en ejecución: los servicios de imágenes públicas fijadas por digest (Nginx de la entrada y la caché Redis de ventas) arrancan
/// de verdad con el usuario sin privilegios, el sistema de archivos de solo lectura, los límites y la comprobación de salud declarados.
/// Los de imagen propia se prueban en <c>ImagenesDeContenedorTests</c> y se resuelven con <c>ComposePorVmTests</c>.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
[Trait("Categoria", Categorias.Lanzamiento)]
public sealed class ComposeEnEjecucionTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "compose-ejec-" + Guid.NewGuid().ToString("N"));
    private readonly string _proyecto = "bhtest" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        foreach (var vm in ComposeDeVms.Vms)
        {
            try
            {
                ComposeDeVms.Docker(TimeSpan.FromMinutes(2), Entorno(vm), [.. Base(vm), "down", "-v", "--remove-orphans"]);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
            {
                // Limpieza: no debe ocultar el resultado de la prueba.
            }
        }

        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal.
        }
    }

    private readonly Dictionary<string, string> _sustituciones = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sinPuertos = new(StringComparer.Ordinal);

    /// <summary>
    /// El comienzo de cada orden de Compose. Un servicio de <c>_sinPuertos</c> no publica su puerto: el equipo que prueba puede tener el
    /// puerto reservado (Windows reserva rangos), y lo que se verifica aquí es el endurecimiento, no la publicación (ya la cubre la prueba estática).
    /// </summary>
    private string[] Base(string vm)
    {
        var argumentos = new List<string> { "compose", "-p", _proyecto + vm, "-f", $"docker-compose.{vm}.yml" };
        if (_sinPuertos.Count > 0)
        {
            Directory.CreateDirectory(_carpeta);
            var sustitucion = Path.Combine(_carpeta, "sin-puertos.yml");
            File.WriteAllText(sustitucion, "services:\n" + string.Concat(_sinPuertos.Select(s => $"  {s}:\n    ports: !reset []\n")));
            argumentos.AddRange(["-f", sustitucion.Replace('\\', '/')]);
        }

        return [.. argumentos];
    }

    private Dictionary<string, string> Entorno(string vm)
    {
        var entorno = ComposeDeVms.EntornoDeMuestra(vm, _carpeta);
        foreach (var (clave, valor) in _sustituciones)
        {
            entorno[clave] = valor;
        }

        return entorno;
    }

    private JsonElement Inspeccionar(string vm, string servicio)
    {
        var (codigo, ids, errores) = ComposeDeVms.Docker(TimeSpan.FromMinutes(1), Entorno(vm), [.. Base(vm), "ps", "-q", servicio]);
        Assert.True(codigo == 0 && ids.Trim().Length > 0, $"No hay contenedor de {servicio}: {errores}");
        var (_, json, _) = ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "inspect", ids.Trim().Split('\n')[0].Trim());
        return JsonDocument.Parse(json).RootElement[0].Clone();
    }

    private void Levantar(string vm, string servicio)
    {
        var (codigo, salida, errores) = ComposeDeVms.Docker(TimeSpan.FromMinutes(4), Entorno(vm), [.. Base(vm), "up", "-d", "--wait", "--wait-timeout", "120", "--no-deps", servicio]);
        Assert.True(codigo == 0, $"docker compose up de {servicio} falló:\n{errores}\n{salida}");
    }

    private static void AfirmarContenedorEndurecido(JsonElement contenedor, string usuarioEsperado, long memoria)
    {
        var anfitrion = contenedor.GetProperty("HostConfig");
        Assert.Equal(usuarioEsperado, contenedor.GetProperty("Config").GetProperty("User").GetString());
        Assert.True(anfitrion.GetProperty("ReadonlyRootfs").GetBoolean());
        Assert.False(anfitrion.GetProperty("Privileged").GetBoolean());
        Assert.Equal(memoria, anfitrion.GetProperty("Memory").GetInt64());
        Assert.True(anfitrion.GetProperty("NanoCpus").GetInt64() > 0);
        Assert.Contains(anfitrion.GetProperty("CapDrop").EnumerateArray().Select(c => c.GetString()), c => c == "ALL");
        Assert.Contains(anfitrion.GetProperty("SecurityOpt").EnumerateArray().Select(c => c.GetString()), c => c == "no-new-privileges:true");
        Assert.Equal("healthy", contenedor.GetProperty("State").GetProperty("Health").GetProperty("Status").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void NginxDeLaEntrada_ArrancaSinPrivilegiosConElSistemaDeArchivosDeSoloLecturaYSano()
    {
        Levantar("ventas", "nginx");

        AfirmarContenedorEndurecido(Inspeccionar("ventas", "nginx"), "101:101", 64L * 1024 * 1024);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void LaCacheRedisDeVentas_ArrancaSinPrivilegiosConElSistemaDeArchivosDeSoloLecturaYSana()
    {
        // La contraseña de la caché se genera aquí y vive solo en un archivo temporal de la prueba.
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Path.Combine(_carpeta, "redis.env"), "REDIS_CACHE_PASSWORD=" + Guid.NewGuid().ToString("N") + "\n");
        Levantar("ventas", "redis");

        AfirmarContenedorEndurecido(Inspeccionar("ventas", "redis"), "redis", 128L * 1024 * 1024);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void PostgreSqlDelInventario_ConLaImagenDerivada_ArrancaSinPrivilegiosConElSistemaDeArchivosDeSoloLecturaYSano()
    {
        var etiqueta = "bhtest-postgres:" + _proyecto;
        var (codigoDeConstruccion, _, erroresDeConstruccion) = ComposeDeVms.Docker(TimeSpan.FromMinutes(8), null, "build", "-t", etiqueta, "postgres");
        Assert.True(codigoDeConstruccion == 0, $"No se pudo construir la imagen de PostgreSQL:\n{erroresDeConstruccion}");
        try
        {
            _sustituciones["IMAGEN_POSTGRES"] = etiqueta;
            _sustituciones["IP_PRIVADA"] = "127.0.0.1";
            _sinPuertos.Add("postgres");
            Directory.CreateDirectory(_carpeta);
            var contrasenaDeEcommerce = Guid.NewGuid().ToString("N");
            File.WriteAllText(
                Path.Combine(_carpeta, "postgres.env"),
                $"POSTGRES_PASSWORD={Guid.NewGuid():N}\nPOSTGRES_ECOMMERCE_PASSWORD={contrasenaDeEcommerce}\nPOSTGRES_INVENTARIO_PASSWORD={Guid.NewGuid():N}\n");
            // El certificado y la clave del servidor (AM24): en una VM los emite la CA privada; aquí, uno autofirmado desechable, solo para que el servidor arranque con TLS.
            using (var rsa = RSA.Create(2048))
            {
                var solicitud = new CertificateRequest("CN=postgres", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var certificado = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
                File.WriteAllText(Path.Combine(_carpeta, "tls-postgres-cert.pem"), certificado.ExportCertificatePem());
                File.WriteAllText(Path.Combine(_carpeta, "tls-postgres-key.pem"), rsa.ExportPkcs8PrivateKeyPem());
            }

            Levantar("inventario", "postgres");

            var contenedor = Inspeccionar("inventario", "postgres");
            AfirmarContenedorEndurecido(contenedor, "postgres", 2048L * 1024 * 1024);

            // TLS de verdad (AM24): por la red (127.0.0.1 dentro del contenedor) una conexión con TLS entra y una sin TLS se rechaza antes de pedir la contraseña.
            var id = contenedor.GetProperty("Id").GetString()!;
            var (conTls, salidaConTls, erroresConTls) = ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "exec", "-e", "PGPASSWORD=" + contrasenaDeEcommerce, id, "psql", "-At", "postgresql://ecommerce@127.0.0.1/ecommerce?sslmode=require", "-c", "select ssl::text || ' ' || version from pg_stat_ssl where pid = pg_backend_pid()");
            Assert.True(conTls == 0, erroresConTls + salidaConTls);
            Assert.Equal("true TLSv1.3", salidaConTls.Trim());
            var (sinTls, _, erroresSinTls) = ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "exec", "-e", "PGPASSWORD=" + contrasenaDeEcommerce, id, "psql", "-At", "postgresql://ecommerce@127.0.0.1/ecommerce?sslmode=disable", "-c", "select 1");
            Assert.NotEqual(0, sinTls);
            Assert.Contains("no encryption", erroresSinTls, StringComparison.Ordinal);
            // El socket local sigue sirviendo a la comprobación de salud, al respaldo y al archivado (sin red).
            var (porSocket, _, erroresPorSocket) = ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "exec", id, "psql", "-U", "postgres", "-At", "-c", "select 1");
            Assert.True(porSocket == 0, erroresPorSocket);
        }
        finally
        {
            Dispose();
            ComposeDeVms.Docker(TimeSpan.FromMinutes(1), null, "rmi", "-f", etiqueta);
        }
    }
}
