using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// El espacio local reservado del archivado de cambios (T27; PLA-21, D11; AC-PLA-21): con el almacén de respaldos no disponible la base sigue
/// operando y acumula lo pendiente dentro de una reserva de menos de 5 GB; avisa al 80 %; al agotarse declara roto el archivo de cambios (se
/// descarta lo nuevo para no llenar el disco) y exige un respaldo completo nuevo, que es lo único que retira la marca. Contra la imagen de
/// PostgreSQL de <c>infra/postgres</c> tal como se publica; la parte de arriba con archivos de prueba y la de abajo con PostgreSQL real.
/// </summary>
[Collection(RespaldosCollection.Nombre)]
[Trait("Categoria", Categorias.Guiones)]
public sealed class EspacioReservadoTests(RespaldosFixture entorno)
{
    private const int Mib = 1024 * 1024;
    private const int ReservaDePrueba = 10 * Mib;

    /// <summary>Prepara una carpeta de WAL con N segmentos de 1 MiB pendientes de archivar y otra de espera con lo dado; devuelve ambas rutas.</summary>
    private async Task<(string Wal, string Espera)> Preparar(int segmentosPendientes, int mibEnEspera = 0)
    {
        var resultado = await entorno.Sh(
            "base=$(mktemp -d) && mkdir -p $base/wal/archive_status $base/espera && "
            + $"i=0; while [ $i -lt {segmentosPendientes} ]; do n=$(printf '0000000100000000%08X' $i); dd if=/dev/zero of=$base/wal/$n bs=1048576 count=1 2>/dev/null; : > $base/wal/archive_status/$n.ready; i=$((i+1)); done; "
            + (mibEnEspera > 0 ? $"dd if=/dev/zero of=$base/espera/base.tmp bs=1048576 count={mibEnEspera} 2>/dev/null; " : string.Empty)
            + "echo $base");
        Assert.Equal(0, resultado.ExitCode);
        var @base = resultado.Stdout.Trim();
        return (@base + "/wal", @base + "/espera");
    }

    private async Task<(long Codigo, string Salida)> Vigilar(string wal, string espera, string? reserva = null)
    {
        var ejecucion = await entorno.Sh($"BABELHUB_PGWAL={wal} BABELHUB_ESPERA={espera} BABELHUB_ESPACIO_RESERVADO={reserva ?? ReservaDePrueba.ToString(CultureInfo.InvariantCulture)} /opt/babelhub/vigilar-espacio.sh");
        return (ejecucion.ExitCode ?? -1, ejecucion.Stdout + ejecucion.Stderr);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(7, 0)]
    [InlineData(2, 5)]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task PorDebajoDelOchentaPorCiento_NoHayAvisoNiMarca(int segmentos, int mibEnEspera)
    {
        var (wal, espera) = await Preparar(segmentos, mibEnEspera);

        var (codigo, salida) = await Vigilar(wal, espera);

        Assert.Equal(0, codigo);
        Assert.Contains("ok espacio reservado al ", salida, StringComparison.Ordinal);
        Assert.NotEqual(0, (await entorno.Sh($"test -e {espera}/archivo-roto")).ExitCode);
    }

    [Theory]
    [InlineData(8, 0, 80)]
    [InlineData(9, 0, 90)]
    [InlineData(3, 6, 90)]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task DelOchentaPorCientoEnAdelante_AvisaYSigueOperando_SinDeclararRoto(int segmentos, int mibEnEspera, int porcentaje)
    {
        var (wal, espera) = await Preparar(segmentos, mibEnEspera);

        var (codigo, salida) = await Vigilar(wal, espera);

        Assert.Equal(1, codigo);
        Assert.Contains($"ALERTA espacio reservado del archivo de cambios al {porcentaje} %", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("ROTO", salida, StringComparison.Ordinal);
        Assert.NotEqual(0, (await entorno.Sh($"test -e {espera}/archivo-roto")).ExitCode);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(14, 0)]
    [InlineData(4, 6)]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task AlAgotarseElEspacio_SeDeclaraRotoElArchivoDeCambios_YSeExigeUnRespaldoCompletoNuevo(int segmentos, int mibEnEspera)
    {
        var (wal, espera) = await Preparar(segmentos, mibEnEspera);

        var (codigo, salida) = await Vigilar(wal, espera);
        var marca = await entorno.Sh($"cat {espera}/archivo-roto");

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA espacio reservado AGOTADO", salida, StringComparison.Ordinal);
        Assert.Contains("se declara ROTO el archivo de cambios", salida, StringComparison.Ordinal);
        Assert.Contains("se exige un respaldo completo nuevo", salida, StringComparison.Ordinal);
        Assert.Equal(0, marca.ExitCode);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", marca.Stdout.Trim());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task ConLaMarcaPuesta_CadaCorridaRepiteLaAlerta_AunqueYaNoHayaNadaPendiente_HastaUnRespaldoCompleto()
    {
        var (wal, espera) = await Preparar(10);
        await Vigilar(wal, espera);
        await entorno.Sh($"rm -f {wal}/*.ready {wal}/archive_status/*.ready {wal}/0*");

        var (codigo, salida) = await Vigilar(wal, espera);

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA el archivo de cambios está ROTO desde 20", salida, StringComparison.Ordinal);
        Assert.Contains("Se exige un respaldo completo nuevo", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task LaMarcaYLaVigilancia_NoCuentanElPropioArchivoDeLaMarca_NiTocanOtroArchivo()
    {
        var (wal, espera) = await Preparar(1);
        await entorno.Sh($"echo x > {espera}/archivo-roto.recuperando");

        var (codigo, salida) = await Vigilar(wal, espera);

        Assert.Equal(0, codigo);
        Assert.Contains("(1 MiB de cambios por archivar y 0 MiB de respaldo en espera)", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task ElRespaldoEnEspera_CuentaContraLaMismaReserva()
    {
        var (wal, espera) = await Preparar(0, 8);

        var (codigo, salida) = await Vigilar(wal, espera);

        Assert.Equal(1, codigo);
        Assert.Contains("al 80 % o más", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task LaAlertaDelOchentaPorCiento_TieneUnTextoEstable_ParaQueAlertarNoMandeUnCorreoPorMinuto()
    {
        var (wal, espera) = await Preparar(8);
        var primera = await Vigilar(wal, espera);
        await entorno.Sh($"dd if=/dev/zero of={espera}/otro.tmp bs=1024 count=300 2>/dev/null");
        var segunda = await Vigilar(wal, espera);

        Assert.Equal(1, primera.Codigo);
        Assert.Equal(primera.Salida, segunda.Salida);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task MientrasCorreUnRespaldoCompleto_SuCopiaCifradaTransitoriaNoCuentaContraLaReserva_PeroLoPendienteSi()
    {
        // Un respaldo de 9 MiB en curso (la carpeta de bloqueo existe) no declara roto el archivo de cambios ni avisa; 7 MiB por archivar, sí cuentan.
        var (wal, espera) = await Preparar(2, 0);
        await entorno.Sh($"mkdir {espera}/respaldo.bloqueo && dd if=/dev/zero of={espera}/base.123.tmp bs=1048576 count=9 2>/dev/null && dd if=/dev/zero of={espera}/config.123.tmp bs=1048576 count=3 2>/dev/null");

        var enCurso = await Vigilar(wal, espera);
        var marcaEnCurso = await entorno.Sh($"test -e {espera}/archivo-roto");
        await entorno.Sh($"rmdir {espera}/respaldo.bloqueo");
        var terminado = await Vigilar(wal, espera);

        Assert.Equal(0, enCurso.Codigo);
        Assert.Contains("ok espacio reservado al 20 %", enCurso.Salida, StringComparison.Ordinal);
        Assert.NotEqual(0, marcaEnCurso.ExitCode);
        Assert.Equal(1, terminado.Codigo);
        Assert.Contains("AGOTADO", terminado.Salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task ConElArchivoRoto_LosArchivosDeHistoriaDeLineaDeTiempoSiguenArchivandose()
    {
        var (_, espera) = await Preparar(0);
        var antes = await entorno.Claves(RespaldosFixture.Wal);
        await entorno.Sh($"echo 2026-11-14T10:00:00Z > {espera}/archivo-roto; echo historia > /tmp/00000002.history");

        var ejecucion = await entorno.Sh($"BABELHUB_ESPERA={espera} /opt/babelhub/archivar-wal.sh /tmp/00000002.history 00000002.history");

        Assert.Equal(0, ejecucion.ExitCode);
        Assert.Single((await entorno.Claves(RespaldosFixture.Wal)).Except(antes));
    }

    // ---------- La reserva: menos de 5 GB ----------

    [Theory]
    [InlineData("5000000000")]
    [InlineData("5368709120")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("cuatro")]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task UnaReservaDeCincoGbOMasONoNumerica_SeRechazaConCodigo2(string reserva)
    {
        var (wal, espera) = await Preparar(0);

        var ejecucion = await entorno.Sh($"BABELHUB_PGWAL={wal} BABELHUB_ESPERA={espera} BABELHUB_ESPACIO_RESERVADO='{reserva}' /opt/babelhub/vigilar-espacio.sh");

        // Con la variable vacía el script usa su valor por defecto; con cualquier otro valor inválido sale con 2.
        Assert.Equal(reserva.Length == 0 ? 0 : 2, ejecucion.ExitCode);
        if (reserva.Length > 0)
        {
            Assert.Contains("respaldos: ", ejecucion.Stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task LaReservaPorDefecto_EsDeCuatroGiB_MenorDeCincoGb_YSeVeEnLaSalida()
    {
        var (wal, espera) = await Preparar(0);

        var ejecucion = await entorno.Sh($"BABELHUB_PGWAL={wal} BABELHUB_ESPERA={espera} /opt/babelhub/vigilar-espacio.sh");

        Assert.Equal(0, ejecucion.ExitCode);
        Assert.Contains("de 4096 MiB reservados", ejecucion.Stdout, StringComparison.Ordinal);
        Assert.True(4L * 1024 * 1024 * 1024 < 5_000_000_000L);
    }

    // ---------- El archivado con la marca puesta ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task ConElArchivoRoto_ElArchivadoDescartaElSegmento_SinSubirNadaYSinNecesitarElAlmacen()
    {
        var antes = await entorno.Claves(RespaldosFixture.Wal);
        var (_, espera) = await Preparar(0);
        await entorno.Sh($"echo 2026-11-14T10:00:00Z > {espera}/archivo-roto; echo contenido > /tmp/seg-roto");

        // Aunque la credencial del almacén esté mala, descartar no la necesita: el disco no debe seguir creciendo.
        var ejecucion = await entorno.Sh($"BABELHUB_ESPERA={espera} RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY=mala /opt/babelhub/archivar-wal.sh /tmp/seg-roto 000000010000000000000077");

        Assert.Equal(0, ejecucion.ExitCode);
        Assert.Contains("el archivo de cambios está roto: se descarta el segmento 000000010000000000000077", ejecucion.Stderr, StringComparison.Ordinal);
        Assert.Equal(antes, await entorno.Claves(RespaldosFixture.Wal));
        Assert.Equal(string.Empty, (await entorno.Sh($"ls {espera} | grep -v archivo-roto")).Stdout.Trim());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task SinLaMarca_ElArchivadoSigueSubiendoComoAntes()
    {
        var (_, espera) = await Preparar(0);
        var antes = await entorno.Claves(RespaldosFixture.Wal);

        var ejecucion = await entorno.Sh($"echo contenido > /tmp/seg-normal && BABELHUB_ESPERA={espera} /opt/babelhub/archivar-wal.sh /tmp/seg-normal 000000010000000000000078");

        Assert.Equal(0, ejecucion.ExitCode);
        Assert.Single((await entorno.Claves(RespaldosFixture.Wal)).Except(antes));
    }

    // ---------- Un respaldo completo nuevo lo repara ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task UnRespaldoCompletoQueTerminaBien_RetiraLaMarca_YLaVigilanciaVuelveAEstarEnOrden()
    {
        var (wal, espera) = await Preparar(0);
        await entorno.Sh($"echo 2026-11-14T10:00:00Z > {espera}/archivo-roto");

        var respaldo = await entorno.Sh($"BABELHUB_ESPERA={espera} /opt/babelhub/respaldar.sh --bajo-demanda");
        var (codigo, salida) = await Vigilar(wal, espera);

        Assert.True(respaldo.ExitCode == 0, respaldo.Stderr + respaldo.Stdout);
        Assert.Contains("el archivo de cambios estaba roto (PLA-21) y este respaldo completo lo repara", respaldo.Stdout, StringComparison.Ordinal);
        Assert.NotEqual(0, (await entorno.Sh($"test -e {espera}/archivo-roto || test -e {espera}/archivo-roto.recuperando")).ExitCode);
        Assert.Equal(0, codigo);
        Assert.Contains("ok espacio reservado", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public async Task UnRespaldoQueFalla_NoRetiraLaMarca_ElArchivoSigueRotoYNoQuedaNadaLocal()
    {
        var (_, espera) = await Preparar(0);
        await entorno.Sh($"echo 2026-11-14T10:00:00Z > {espera}/archivo-roto");

        var respaldo = await entorno.Sh($"BABELHUB_ESPERA={espera} RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY=mala /opt/babelhub/respaldar.sh --bajo-demanda");
        var marca = await entorno.Sh($"cat {espera}/archivo-roto");
        var restos = await entorno.Sh($"ls -A {espera}");

        Assert.Equal(1, respaldo.ExitCode);
        Assert.Equal(0, marca.ExitCode);
        Assert.Equal("2026-11-14T10:00:00Z", marca.Stdout.Trim());
        Assert.Equal("archivo-roto", restos.Stdout.Trim());
    }

    // ---------- Con PostgreSQL real y el almacén caído ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    [Trait("Categoria", Categorias.Lanzamiento)]
    public async Task ConElAlmacenCaido_LaBaseSigueOperandoYAcumulaLoPendiente_AvisaAlOchentaYSeDeclaraRotoAlAgotarse_YUnRespaldoLoRepara()
    {
        // Segmentos de 1 MiB y una reserva de 24 MiB para que unos pocos cambios la agoten; el almacén «no responde» (nadie escucha en el puerto 9).
        const int Reserva = 24 * Mib;
        var configuracion = Path.Combine(Path.GetTempPath(), "espacio-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configuracion);
        File.WriteAllText(Path.Combine(configuracion, "docker-compose.prueba.yml"), "name: prueba\nservices: {}\n");
        try
        {
            await ConElAlmacenCaidoAsync(configuracion, Reserva);
        }
        finally
        {
            Directory.Delete(configuracion, recursive: true);
        }
    }

    private async Task ConElAlmacenCaidoAsync(string configuracion, int reserva)
    {
        await using var base_ = new ContainerBuilder(entorno.Etiqueta)
            .WithNetwork(entorno.Red)
            .WithEnvironment("POSTGRES_PASSWORD", "p" + Guid.NewGuid().ToString("N"))
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--wal-segsize=1")
            .WithEnvironment("BABELHUB_AGE_DESTINATARIO", entorno.Destinatario)
            .WithEnvironment("BABELHUB_ESPACIO_RESERVADO", reserva.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_TYPE", "s3")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_PROVIDER", "Other")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_ENDPOINT", "http://127.0.0.1:9")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_REGION", "us-east-1")
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID", entorno.ClaveDeAccesoDelAlmacen)
            .WithEnvironment("RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY", entorno.ClaveSecretaDelAlmacen)
            .WithBindMount(configuracion.Replace('\\', '/'), "/config", AccessMode.ReadOnly)
            .WithCommand("postgres", "-c", "archive_mode=on", "-c", "archive_command=/opt/babelhub/archivar-wal.sh %p %f", "-c", "archive_timeout=1")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("PostgreSQL init process complete; ready for start up."))
            .Build();
        await base_.StartAsync();
        await EsperarALaBase(base_);

        async Task<ExecResult> Sql(string sentencia) => await base_.ExecAsync(["psql", "-U", "postgres", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-tAc", sentencia]);
        async Task<(long Codigo, string Salida)> Monitor()
        {
            var r = await base_.ExecAsync(["/opt/babelhub/vigilar-espacio.sh"]);
            return (r.ExitCode ?? -1, r.Stdout + r.Stderr);
        }

        async Task CambiarYCerrarSegmentos(int veces)
        {
            for (var i = 0; i < veces; i++)
            {
                var escritura = await Sql("INSERT INTO datos SELECT 'x' FROM generate_series(1, 200); SELECT pg_switch_wal() IS NOT NULL");
                Assert.True(escritura.ExitCode == 0, "la base debe seguir operando: " + escritura.Stderr);
            }
        }

        Assert.Equal(0, (await Sql("CREATE TABLE datos(marca text)")).ExitCode);

        // Con pocos segmentos pendientes todavía hay margen: por debajo del 80 % no se avisa.
        var inicial = await Monitor();
        Assert.Equal(0, inicial.Codigo);

        // Los cambios siguen entrando; como el archivado falla, PostgreSQL conserva los segmentos: se llega al 80 % y se avisa.
        (long Codigo, string Salida) aviso = (0, string.Empty);
        for (var ronda = 0; ronda < 60 && aviso.Codigo == 0; ronda++)
        {
            await CambiarYCerrarSegmentos(1);
            await Task.Delay(500);
            aviso = await Monitor();
        }

        Assert.Equal(1, aviso.Codigo);
        Assert.Contains("ALERTA espacio reservado del archivo de cambios al ", aviso.Salida, StringComparison.Ordinal);

        // Se sigue sin almacén: al agotarse la reserva se declara roto el archivo de cambios.
        (long Codigo, string Salida) agotado = aviso;
        for (var ronda = 0; ronda < 60 && !agotado.Salida.Contains("AGOTADO", StringComparison.Ordinal); ronda++)
        {
            await CambiarYCerrarSegmentos(1);
            await Task.Delay(500);
            agotado = await Monitor();
        }

        Assert.Contains("se declara ROTO el archivo de cambios", agotado.Salida, StringComparison.Ordinal);
        Assert.Equal(0, (await base_.ExecAsync(["test", "-f", "/var/respaldos/archivo-roto"])).ExitCode);

        // La base no se detuvo ni se llenó: sigue aceptando cambios y el archivado descarta lo nuevo, de modo que lo pendiente deja de crecer.
        await CambiarYCerrarSegmentos(3);
        var pendientes = long.MaxValue;
        for (var intento = 0; intento < 60 && pendientes > 0; intento++)
        {
            await Task.Delay(1000);
            var cuenta = await base_.ExecAsync(["sh", "-c", "ls /var/lib/postgresql/data/pg_wal/archive_status/*.ready 2>/dev/null | wc -l"]);
            pendientes = long.Parse(cuenta.Stdout.Trim(), CultureInfo.InvariantCulture);
        }

        Assert.Equal(0, pendientes);
        Assert.Equal(0, (await Sql("INSERT INTO datos VALUES ('sigue operando')")).ExitCode);
        var roto = await Monitor();
        Assert.Equal(1, roto.Codigo);
        Assert.Contains("ALERTA el archivo de cambios está ROTO", roto.Salida, StringComparison.Ordinal);

        // Con el almacén de nuevo disponible, un respaldo completo lo repara y retira la marca.
        var respaldo = await base_.ExecAsync(["sh", "-c", "RCLONE_CONFIG_ALMACEN_ENDPOINT=http://almacen:9000 /opt/babelhub/respaldar.sh --bajo-demanda"]);
        Assert.True(respaldo.ExitCode == 0, respaldo.Stderr + respaldo.Stdout);
        Assert.NotEqual(0, (await base_.ExecAsync(["test", "-f", "/var/respaldos/archivo-roto"])).ExitCode);
        Assert.Equal(0, (await Monitor()).Codigo);
    }

    private static async Task EsperarALaBase(IContainer contenedor)
    {
        for (var intento = 0; intento < 60; intento++)
        {
            var resultado = await contenedor.ExecAsync(["psql", "-U", "postgres", "-d", "postgres", "-tAc", "SELECT 1"]);
            if (resultado.ExitCode == 0)
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("PostgreSQL no quedó listo.");
    }
}
