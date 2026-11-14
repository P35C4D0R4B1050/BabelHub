using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// El respaldo diario y el archivado de cambios (PLA-09, PLA-15, PLA-16, PLA-17; AC-PLA-09, 15, 16, 17): PostgreSQL real con dos bases de
/// negocio, la imagen publicada de <c>infra/postgres</c> y un almacén compatible con S3. La clave que descifra solo existe en la prueba.
/// </summary>
[Collection(RespaldosCollection.Nombre)]
public sealed partial class RespaldosTests(RespaldosFixture entorno)
{
    private static readonly Regex NombreDelRespaldo = NombreDeBase();
    private static readonly Regex NombreDeLaConfiguracion = NombreDeConfig();

    private static string Sha256(byte[] contenido) => Convert.ToHexStringLower(SHA256.HashData(contenido));

    private static bool ContieneTexto(byte[] contenido, string texto) => Encoding.Latin1.GetString(contenido).Contains(texto, StringComparison.Ordinal);

    // ---------- Respaldo diario ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public async Task ElRespaldoDiario_SubeElClusterYLaConfiguracion_ConElNombreConLaSumaDelCifrado()
    {
        var respaldo = await entorno.PrimerRespaldo();

        Assert.True(respaldo.Ejecucion.ExitCode == 0, respaldo.Ejecucion.Stderr + respaldo.Ejecucion.Stdout);
        var cluster = Assert.Single(respaldo.Diarios, k => NombreDelRespaldo.IsMatch(k));
        var configuracion = Assert.Single(respaldo.Diarios, k => NombreDeLaConfiguracion.IsMatch(k));
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cluster[..10]);
        foreach (var clave in new[] { cluster, configuracion })
        {
            var cifrado = await entorno.Descargar(RespaldosFixture.Diarios, clave);
            Assert.Equal(Regex.Match(clave, "[0-9a-f]{64}").Value, Sha256(cifrado));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public async Task ElRespaldo_NoEsLegibleSinLaClave_NiDejaNadaEnLaVmDeDatos()
    {
        var respaldo = await entorno.PrimerRespaldo();
        var cluster = respaldo.Diarios.Single(k => NombreDelRespaldo.IsMatch(k));

        var cifrado = await entorno.Descargar(RespaldosFixture.Diarios, cluster);

        Assert.StartsWith("age-encryption.org/v1", Encoding.ASCII.GetString(cifrado, 0, 21), StringComparison.Ordinal);
        Assert.False(ContieneTexto(cifrado, entorno.MarcaDeDatos), "el objeto subido contiene datos sin cifrar");
        Assert.False(ContieneTexto(cifrado, "PG_VERSION"), "el objeto subido contiene el clúster sin cifrar");
        var restos = await entorno.Sh("ls -A /var/respaldos");
        Assert.Equal(string.Empty, restos.Stdout.Trim());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task ConSoloLaClavePrivadaDeLaCopiaFueraDeLinea_SeDescifraYSeRestauranLasDosBasesDeNegocio()
    {
        var respaldo = await entorno.PrimerRespaldo();
        var cluster = respaldo.Diarios.Single(k => NombreDelRespaldo.IsMatch(k));
        var tar = await entorno.Descifrar(await entorno.Descargar(RespaldosFixture.Diarios, cluster));

        await using var restaurada = new ContainerBuilder("postgres:17-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "no-se-usa")
            .WithResourceMapping(tar, "/tmp/respaldo.tar")
            .WithCreateParameterModifier(p => p.User = "postgres")
            .WithEntrypoint("sh", "-c", "tar -xf /tmp/respaldo.tar -C /var/lib/postgresql/data && chmod 700 /var/lib/postgresql/data && exec postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("database system is ready to accept connections"))
            .Build();
        await restaurada.StartAsync();

        foreach (var baseDeDatos in new[] { "ecommerce", "inventario" })
        {
            var consulta = await restaurada.ExecAsync(["psql", "-U", "postgres", "-d", baseDeDatos, "-tAc", "SELECT marca FROM datos"]);
            Assert.True(consulta.ExitCode == 0, consulta.Stderr);
            Assert.Equal($"{entorno.MarcaDeDatos}-{baseDeDatos}", consulta.Stdout.Trim());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public async Task LaConfiguracionRespaldada_NoLlevaSecretos()
    {
        var respaldo = await entorno.PrimerRespaldo();
        var configuracion = respaldo.Diarios.Single(k => NombreDeLaConfiguracion.IsMatch(k));

        var tar = await entorno.Descifrar(await entorno.Descargar(RespaldosFixture.Diarios, configuracion));

        Assert.True(ContieneTexto(tar, "docker-compose.prueba.yml"));
        Assert.False(ContieneTexto(tar, "postgres.env"));
        Assert.False(ContieneTexto(tar, "certificado-key.pem"));
        Assert.False(ContieneTexto(tar, "llave.key"));
        Assert.False(ContieneTexto(tar, entorno.SecretoDeLaConfiguracion));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public async Task ElRespaldoNoVaAlContenedorDeLosLibros_NiALosDeOtraClaseSiNoCorrespondeLaCopia()
    {
        var respaldo = await entorno.PrimerRespaldo();

        // El diario del día de las pruebas solo genera copias si cae en domingo o en el último día del mes.
        var esperadas = Clases(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Equal(esperadas.Contains("semanal") ? 2 : 0, respaldo.Semanales.Count);
        Assert.Equal(esperadas.Contains("mensual") ? 2 : 0, respaldo.Mensuales.Count);
        Assert.Empty(await entorno.S3.ListBucketsAsync().ContinueWith(t => t.Result.Buckets?.Where(b => b.BucketName == "libros") ?? []));
    }

    // ---------- Bajo demanda y copias ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public async Task ElRespaldoBajoDemanda_VaSoloAlDiario_ConLaMismaRetencion()
    {
        var respaldo = await entorno.Respaldar("--bajo-demanda");

        Assert.True(respaldo.Ejecucion.ExitCode == 0, respaldo.Ejecucion.Stderr);
        Assert.Equal(2, respaldo.Diarios.Count);
        Assert.Empty(respaldo.Semanales);
        Assert.Empty(respaldo.Mensuales);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public async Task LasCopiasDominicalYDelUltimoDiaDelMes_SubenElMismoObjetoALosContenedoresDeSuClase()
    {
        var respaldo = await entorno.Respaldar("--clases semanal,mensual");

        Assert.True(respaldo.Ejecucion.ExitCode == 0, respaldo.Ejecucion.Stderr);
        Assert.Equal(2, respaldo.Diarios.Count);
        Assert.Equal([.. respaldo.Diarios.Order(StringComparer.Ordinal)], [.. respaldo.Semanales.Order(StringComparer.Ordinal)]);
        Assert.Equal([.. respaldo.Diarios.Order(StringComparer.Ordinal)], [.. respaldo.Mensuales.Order(StringComparer.Ordinal)]);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData("--bajo-demanda --clases semanal")]
    [InlineData("--clases anual")]
    [InlineData("--inventado")]
    public async Task UnUsoInvalido_SeRechazaConCodigo2_SinSubirNada(string argumentos)
    {
        var respaldo = await entorno.Respaldar(argumentos);

        Assert.Equal(2, respaldo.Ejecucion.ExitCode);
        Assert.Empty(respaldo.Diarios);
        Assert.Empty(respaldo.Semanales);
        Assert.Empty(respaldo.Mensuales);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData(2026, 11, 15, "semanal")]
    [InlineData(2026, 11, 30, "mensual")]
    [InlineData(2026, 11, 29, "semanal")]
    [InlineData(2026, 12, 31, "mensual")]
    [InlineData(2028, 2, 29, "mensual")]
    [InlineData(2027, 2, 28, "semanal mensual")]
    [InlineData(2026, 11, 17, "")]
    [InlineData(2026, 11, 28, "")]
    public async Task LasClasesDeUnDia_SonElDomingoYElUltimoDiaDelMes(int anio, int mes, int dia, string esperado)
    {
        var segundos = new DateTimeOffset(anio, mes, dia, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var resultado = await entorno.Sh($". /opt/babelhub/comun.sh && clases_del_dia {segundos.ToString(CultureInfo.InvariantCulture)}");

        Assert.Equal(0, resultado.ExitCode);
        Assert.Equal(esperado, resultado.Stdout.Trim());
    }

    private static string Clases(long segundos)
    {
        var fecha = DateTimeOffset.FromUnixTimeSeconds(segundos);
        var clases = new List<string>();
        if (fecha.DayOfWeek == DayOfWeek.Sunday)
        {
            clases.Add("semanal");
        }

        if (fecha.AddDays(1).Day == 1)
        {
            clases.Add("mensual");
        }

        return string.Join(' ', clases);
    }

    // ---------- Fallos y protecciones ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public async Task SiLaSubidaFalla_ElRespaldoTermina1_SinSubirNiDejarCopiasLocales()
    {
        var respaldo = await entorno.Respaldar(string.Empty, "RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY=clave-equivocada");

        Assert.Equal(1, respaldo.Ejecucion.ExitCode);
        Assert.Empty(respaldo.Diarios);
        var restos = await entorno.Sh("ls -A /var/respaldos");
        Assert.Equal(string.Empty, restos.Stdout.Trim());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    public async Task SiYaHayOtroRespaldoEnCurso_NoSeLanzaUnoSegundo()
    {
        await entorno.Sh("mkdir /var/respaldos/respaldo.bloqueo");
        try
        {
            var respaldo = await entorno.Respaldar("--bajo-demanda");

            Assert.Equal(75, respaldo.Ejecucion.ExitCode);
            Assert.Empty(respaldo.Diarios);
        }
        finally
        {
            await entorno.Sh("rmdir /var/respaldos/respaldo.bloqueo");
        }
    }

    // ---------- La clave que descifra no está en la VM de datos ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task LaVmDeDatos_SoloConoceLaClavePublica_YNoPuedeDescifrarLoYaSubido()
    {
        var respaldo = await entorno.PrimerRespaldo();
        var cluster = respaldo.Diarios.Single(k => NombreDelRespaldo.IsMatch(k));
        await entorno.Postgres.CopyAsync(await entorno.Descargar(RespaldosFixture.Diarios, cluster), "/tmp/ya-subido.age");

        var entornoDelContenedor = await entorno.Sh("env");
        var intento = await entorno.Sh("age -d -o /tmp/descifrado /tmp/ya-subido.age");
        var clavesPrivadas = await entorno.Sh("grep -rl 'AGE-SECRET-KEY-' /etc /home /root /var/lib/postgresql/data/postgresql.conf /var/respaldos 2>/dev/null; true");

        Assert.DoesNotContain("AGE-SECRET-KEY-", entornoDelContenedor.Stdout, StringComparison.Ordinal);
        Assert.Contains(entorno.Destinatario, entornoDelContenedor.Stdout, StringComparison.Ordinal);
        Assert.NotEqual(0, intento.ExitCode);
        Assert.Equal(string.Empty, clavesPrivadas.Stdout.Trim());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task SiLaClavePrivadaApareceEnElEntorno_ElRespaldoSeNiegaAContinuar()
    {
        var respaldo = await entorno.Respaldar(string.Empty, $"OTRA_VARIABLE={entorno.Identidad}");

        Assert.Equal(1, respaldo.Ejecucion.ExitCode);
        Assert.Contains("clave privada", respaldo.Ejecucion.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(entorno.Identidad, respaldo.Ejecucion.Stderr + respaldo.Ejecucion.Stdout, StringComparison.Ordinal);
        Assert.Empty(respaldo.Diarios);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task ElDestinatarioDebeSerUnaClavePublica_NoUnaIdentidad()
    {
        var respaldo = await entorno.Respaldar(string.Empty, "BABELHUB_AGE_DESTINATARIO=no-es-una-clave");

        Assert.Equal(1, respaldo.Ejecucion.ExitCode);
        Assert.Empty(respaldo.Diarios);
    }

    // ---------- Archivado de cambios ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task ElArchivadoDeCambios_SubeSegmentosCifradosConSuSuma_AunqueSoloHayaElLatido()
    {
        var antes = await entorno.Claves(RespaldosFixture.Wal);

        // El latido: una escritura por segundo mientras no hay otra actividad; con archive_timeout=5 PostgreSQL cierra y archiva segmentos.
        var latido = entorno.Sh("for i in $(seq 1 25); do psql -U postgres -d ecommerce -qtAc 'UPDATE latido SET n = n + 1'; sleep 1; done");
        List<string> nuevos = [];
        for (var intento = 0; intento < 60 && nuevos.Count < 2; intento++)
        {
            await Task.Delay(1000);
            nuevos = [.. (await entorno.Claves(RespaldosFixture.Wal)).Except(antes)];
        }

        await latido;
        Assert.True(nuevos.Count >= 2, $"se esperaban al menos 2 segmentos nuevos y hubo {nuevos.Count}");
        Assert.All(nuevos, k => Assert.Matches(@"^[0-9A-F]{24}-[0-9a-f]{64}\.age$", k));
        var segmento = nuevos.First(k => !k.Contains(".history", StringComparison.Ordinal));
        var cifrado = await entorno.Descargar(RespaldosFixture.Wal, segmento);
        Assert.Equal(Regex.Match(segmento, "-([0-9a-f]{64})").Groups[1].Value, Sha256(cifrado));
        Assert.StartsWith("age-encryption.org/v1", Encoding.ASCII.GetString(cifrado, 0, 21), StringComparison.Ordinal);
        Assert.Equal(16 * 1024 * 1024, (await entorno.Descifrar(cifrado)).Length);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task ElArchivadoDeUnSegmento_Falla_SiElAlmacenRechazaLaSubida_ParaQuePostgreSqlLoReintente()
    {
        var ejecucion = await entorno.Sh("echo contenido > /tmp/000000010000000000000099 && RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY=mala /opt/babelhub/archivar-wal.sh /tmp/000000010000000000000099 000000010000000000000099");

        Assert.NotEqual(0, ejecucion.ExitCode);
        var restos = await entorno.Sh("ls -A /var/respaldos");
        Assert.Equal(string.Empty, restos.Stdout.Trim());
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-15")]
    [InlineData("../escape")]
    [InlineData("con espacio")]
    [InlineData("a;b")]
    public async Task ElArchivadoRechazaNombresDeSegmentoInvalidos(string nombre)
    {
        var ejecucion = await entorno.Sh($"echo x > /tmp/seg && /opt/babelhub/archivar-wal.sh /tmp/seg '{nombre}'");

        Assert.NotEqual(0, ejecucion.ExitCode);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}/\d{6}-[0-9a-f]{64}\.tar\.age$", RegexOptions.None, 2000)]
    private static partial Regex NombreDeBase();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}/\d{6}-config-[0-9a-f]{64}\.tar\.age$", RegexOptions.None, 2000)]
    private static partial Regex NombreDeConfig();
}
