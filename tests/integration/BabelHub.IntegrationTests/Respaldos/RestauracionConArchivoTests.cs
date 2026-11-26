using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Amazon.S3.Model;
using BabelHub.IntegrationTests.VmAux;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// Una restauración real de principio a fin con los objetos comprimidos que suben <c>respaldar.sh</c> y <c>archivar-wal.sh</c> (T43; PLA-09,
/// PLA-17, PLA-18; D25, D28): sin el archivo de cambios las bases quedan en el instante del respaldo; con él llegan a un instante intermedio;
/// en las dos vías la línea de tiempo es nueva, una historia ajena subida al almacén no desvía la recuperación ni su número se reutiliza, y el
/// clúster restaurado sigue archivando, de modo que con un respaldo suyo se vuelve a restaurar. La «VM
/// destino» es un contenedor de la imagen publicada; los scripts de restauración son los del repositorio.
/// </summary>
[Collection(RespaldosCollection.Nombre)]
public sealed class RestauracionConArchivoTests(RespaldosFixture entorno) : IAsyncLifetime
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);

    private IContainer _destino = null!;

    public async Task InitializeAsync()
    {
        var builder = new ContainerBuilder(entorno.Etiqueta)
            .WithNetwork(entorno.Red)
            .WithEntrypoint("sleep", "3600")
            .WithCreateParameterModifier(p => p.User = "root")
            .WithEnvironment("BABELHUB_AGE_DESTINATARIO", entorno.Destinatario)
            .WithEnvironment("BABELHUB_CONFIG", "/tmp/config")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_TYPE", "s3")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_PROVIDER", "Other")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_ENDPOINT", "http://almacen:9000")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_REGION", "us-east-1")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID", entorno.ClaveDeAccesoDelAlmacen)
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY", entorno.ClaveSecretaDelAlmacen)
            .WithResourceMapping(Encoding.UTF8.GetBytes(RestauracionDeEnsayoTests.CustodiaDePrueba(entorno.Destinatario)), "/tmp/custodia.md")
            .WithResourceMapping(Encoding.UTF8.GetBytes(entorno.Identidad + "\n"), "/tmp/identidad.txt")
            // Lo que en la VM fija el Compose con hba_file: aquí solo el socket local, también para que respaldar.sh pueda copiar el clúster.
            .WithResourceMapping(Encoding.UTF8.GetBytes("local all postgres peer\nlocal replication postgres peer\n"), "/tmp/hba.conf");
        foreach (var script in new[] { "descifrar-respaldo.sh", "descifrar-wal.sh", "extraer-respaldo.sh" })
        {
            builder = builder.WithResourceMapping(File.ReadAllBytes(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", script)), "/opt/scripts/" + script);
        }

        _destino = builder.Build();
        await _destino.StartAsync();
        await Exigir("apk add --no-cache tar >/dev/null 2>&1; chmod 0644 /tmp/custodia.md /tmp/identidad.txt /tmp/hba.conf && mkdir -p /tmp/config && echo 'services: {}' > /tmp/config/docker-compose.yml && chmod -R a+rX /tmp/config /opt/scripts && tar --version | head -n 1 | grep -q 'GNU tar'");
    }

    public async Task DisposeAsync() => await _destino.DisposeAsync();

    /// <summary>Una orden en la VM destino con el usuario de PostgreSQL y el entorno del contenedor.</summary>
    private async Task<ExecResult> Como(string orden) => await _destino.ExecAsync(["su", "-s", "/bin/bash", "postgres", "-c", orden]);

    private async Task<ExecResult> Exigir(string orden, bool comoRoot = true)
    {
        var resultado = comoRoot ? await _destino.ExecAsync(["bash", "-c", orden]) : await Como(orden);
        Assert.True(resultado.ExitCode == 0, $"falló «{orden}»:\n{resultado.Stdout}\n{resultado.Stderr}\n{(await _destino.ExecAsync(["sh", "-c", "tail -n 30 /tmp/pg.log 2>/dev/null; true"])).Stdout}");
        return resultado;
    }

    private async Task<string> EnElPrimario(string sentencia)
    {
        var resultado = await entorno.Psql("ecommerce", sentencia);
        Assert.True(resultado.ExitCode == 0, resultado.Stderr);
        return resultado.Stdout.Trim();
    }

    /// <summary>Cierra el segmento actual, espera a que quede archivado y devuelve su nombre.</summary>
    private static async Task<string> ArchivarHastaAqui(Func<string, Task<string>> psql)
    {
        var segmento = await psql("SELECT pg_walfile_name(pg_current_wal_lsn())");
        await psql("SELECT pg_switch_wal()");
        for (var intento = 0; intento < 120; intento++)
        {
            // Solo se comparan nombres de la misma línea de tiempo (los de historia y respaldo no cuentan).
            if (await psql($"SELECT count(*) FROM pg_stat_archiver WHERE length(last_archived_wal) = 24 AND left(last_archived_wal, 8) = '{segmento[..8]}' AND last_archived_wal >= '{segmento}'") == "1")
            {
                return segmento;
            }

            await Task.Delay(500);
        }

        Assert.Fail($"el segmento {segmento} no se archivó a tiempo");
        return segmento;
    }

    private async Task Arrancar(string datos, string opciones) =>
        await Exigir($"pg_ctl -D {datos} -o \"-c listen_addresses='' -c hba_file=/tmp/hba.conf {opciones}\" -l /tmp/pg.log -w -t 120 start >/dev/null", comoRoot: false);

    private async Task Parar(string datos) => await Exigir($"pg_ctl -D {datos} -m fast -w stop >/dev/null", comoRoot: false);

    private async Task<string> EnElRestaurado(string sentencia) =>
        (await Exigir($"psql -d ecommerce -v ON_ERROR_STOP=1 -tAc \"{sentencia}\"", comoRoot: false)).Stdout.Trim();

    private async Task<string> Marcas() => await EnElRestaurado("SELECT string_agg(marca, ',' ORDER BY marca) FROM marcas_de_restauracion");

    /// <summary>Baja el respaldo del clúster y lo extrae en <paramref name="datos"/>; devuelve el primer segmento que necesita.</summary>
    private async Task<string> Extraer(string clave, string datos)
    {
        await _destino.CopyAsync(await entorno.Descargar(RespaldosFixture.Diarios, clave), $"/tmp/copia/{Path.GetFileName(clave)}");
        await Exigir($"chmod 0644 /tmp/copia/* && su -s /bin/bash postgres -c 'set -o pipefail; bash /opt/scripts/descifrar-respaldo.sh --entrada /tmp/copia/{Path.GetFileName(clave)} --custodia /tmp/custodia.md < /tmp/identidad.txt | bash /opt/scripts/extraer-respaldo.sh --destino {datos}'");
        var etiqueta = await Exigir($"cat {datos}/backup_label");
        return Regex.Match(etiqueta.Stdout, @"START WAL LOCATION: \S+ \(file ([0-9A-F]{24})\)", RegexOptions.None, Limite).Groups[1].Value;
    }

    /// <summary>
    /// Baja los segmentos entre <paramref name="desde"/> y <paramref name="hasta"/> y todas las historias, y los prepara en
    /// <paramref name="carpeta"/> con <c>descifrar-wal.sh</c> (sin cambios: solo reserva los números de línea ya usados). El tope en
    /// <paramref name="hasta"/> deja fuera los objetos inventados que otras pruebas suben al mismo almacén.
    /// </summary>
    private async Task PrepararElArchivo(string desde, string? hasta, string carpeta, string? restaurada = null)
    {
        // Con la copia extraída se deja fuera la historia de su propia línea, como cuando el almacén ya la retiró.
        var claves = (await entorno.Claves(RespaldosFixture.Wal))
            .Where(k => (k.Contains(".history-", StringComparison.Ordinal) && (restaurada is null || !k.StartsWith(desde[..8] + ".history-", StringComparison.Ordinal)))
                || (hasta is not null && k.Length > 25 && k[24] == '-' && string.CompareOrdinal(k[..24], desde) >= 0 && string.CompareOrdinal(k[..24], hasta) <= 0))
            .ToList();
        await Exigir("rm -rf /tmp/archivo && mkdir -p /tmp/archivo");
        foreach (var clave in claves)
        {
            await _destino.CopyAsync(await entorno.Descargar(RespaldosFixture.Wal, clave), "/tmp/archivo/" + clave);
        }

        var copia = restaurada is null ? string.Empty : $"--restaurada {restaurada} ";
        var opciones = hasta is null ? "--sin-cambios" : $"{copia}--hasta {hasta} --custodia /tmp/custodia.md < /tmp/identidad.txt";
        await Exigir($"chmod -R a+rX /tmp/archivo && su -s /bin/bash postgres -c 'bash /opt/scripts/descifrar-wal.sh --entrada /tmp/archivo --destino {carpeta} --desde {desde} {opciones}'");
    }

    /// <summary>La configuración de recuperación del procedimiento: la decide quien restaura, nunca la configuración del respaldo.</summary>
    private async Task ConfigurarRecuperacion(string datos, string archivo, params string[] ajustes)
    {
        var lineas = new[] { $"restore_command = 'cp {archivo}/%f %p'", "recovery_target_timeline = 'current'" }.Concat(ajustes);
        var texto = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', lineas) + "\n"));
        await Exigir($"echo {texto} | base64 -d >> {datos}/postgresql.auto.conf && touch {datos}/recovery.signal", comoRoot: false);
    }

    private async Task<string> LineaActual() => (await EnElRestaurado("SELECT pg_walfile_name(pg_current_wal_lsn())"))[..8];

    private async Task EsperarElFinDeLaRecuperacion()
    {
        for (var intento = 0; intento < 120; intento++)
        {
            if (await EnElRestaurado("SELECT pg_is_in_recovery()") == "f")
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail("la recuperación no terminó");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-09")]
    [Trait("Criterio", "AC-PLA-17")]
    [Trait("Criterio", "AC-PLA-18")]
    public async Task SinElArchivoQuedaEnSuInstante_ConElLlegaAUnInstanteIntermedio_YElRestauradoSigueArchivandoYSeVuelveARestaurar()
    {
        await EnElPrimario("CREATE TABLE IF NOT EXISTS marcas_de_restauracion(marca text); TRUNCATE marcas_de_restauracion; INSERT INTO marcas_de_restauracion VALUES ('a')");
        var respaldo = await entorno.Respaldar("--bajo-demanda");
        Assert.True(respaldo.Ejecucion.ExitCode == 0, respaldo.Ejecucion.Stderr);
        var copia = respaldo.Diarios.Single(k => !k.Contains("-config-", StringComparison.Ordinal));
        await EnElPrimario("INSERT INTO marcas_de_restauracion VALUES ('b')");
        await EnElPrimario("INSERT INTO marcas_de_restauracion VALUES ('c')");
        await Task.Delay(1500);
        var instante = await EnElPrimario("SELECT now()");
        await Task.Delay(1500);
        await EnElPrimario("INSERT INTO marcas_de_restauracion VALUES ('d')");
        var hasta = await ArchivarHastaAqui(EnElPrimario);

        // 1) Sin el archivo de cambios: el respaldo se restaura por sí solo al instante en que se tomó, y en una línea de tiempo nueva.
        var desde = await Extraer(copia, "/tmp/sin-archivo");
        await PrepararElArchivo(desde, hasta: null, "/tmp/wal-ninguno");
        await ConfigurarRecuperacion("/tmp/sin-archivo", "/tmp/wal-ninguno", "recovery_target = 'immediate'", "recovery_target_action = 'promote'");
        await Arrancar("/tmp/sin-archivo", "-c archive_mode=off");
        await EsperarElFinDeLaRecuperacion();
        Assert.Equal("a", await Marcas());
        Assert.NotEqual(desde[..8], await LineaActual());
        await Parar("/tmp/sin-archivo");

        // Quien tenga la clave pública sube una historia que ocupa la línea 2.
        var falsa = Encoding.ASCII.GetBytes("1\t0/1000000\tajena\n");
        await entorno.S3.PutObjectAsync(new PutObjectRequest { BucketName = RespaldosFixture.Wal, Key = $"00000002.history-{Convert.ToHexStringLower(SHA256.HashData(falsa))}.zst.age", InputStream = new MemoryStream(falsa) });

        // 2) Con el archivo: llega al instante pedido (con «b» y «c», sin «d»), sin seguir la historia ajena ni reutilizar su número, y vuelve
        // a archivar desde ahí.
        await Extraer(copia, "/tmp/primera");
        await PrepararElArchivo(desde, hasta, "/tmp/wal-primera");
        var historiasDeAntes = (await entorno.Claves(RespaldosFixture.Wal)).Where(k => k.Contains(".history", StringComparison.Ordinal)).ToList();
        await ConfigurarRecuperacion("/tmp/primera", "/tmp/wal-primera", $"recovery_target_time = '{instante}'", "recovery_target_action = 'promote'");
        await Arrancar("/tmp/primera", "-c archive_mode=on -c archive_command='/opt/babelhub/archivar-wal.sh %p %f' -c archive_timeout=5");
        await EsperarElFinDeLaRecuperacion();
        Assert.Equal("a,b,c", await Marcas());
        Assert.Equal("00000003", await LineaActual());

        // 3) Respaldo del clúster restaurado, más cambios, y una segunda restauración con ese respaldo y el archivo nuevo.
        var antes = await entorno.Claves(RespaldosFixture.Diarios);
        await Exigir("/opt/babelhub/respaldar.sh --bajo-demanda", comoRoot: false);
        var segundaCopia = (await entorno.Claves(RespaldosFixture.Diarios)).Except(antes).Single(k => !k.Contains("-config-", StringComparison.Ordinal));
        await EnElRestaurado("INSERT INTO marcas_de_restauracion VALUES ('e')");
        var segundoHasta = await ArchivarHastaAqui(EnElRestaurado);
        await Parar("/tmp/primera");
        Assert.Contains((await entorno.Claves(RespaldosFixture.Wal)).Except(historiasDeAntes), k => k.StartsWith("00000003.history-", StringComparison.Ordinal));

        var segundoDesde = await Extraer(segundaCopia, "/tmp/segunda");
        await PrepararElArchivo(segundoDesde, segundoHasta, "/tmp/wal-segunda", restaurada: "/tmp/segunda");
        await ConfigurarRecuperacion("/tmp/segunda", "/tmp/wal-segunda");
        await Arrancar("/tmp/segunda", "-c archive_mode=off");
        await EsperarElFinDeLaRecuperacion();
        Assert.Equal("a,b,c,e", await Marcas());
        await Parar("/tmp/segunda");
    }
}
