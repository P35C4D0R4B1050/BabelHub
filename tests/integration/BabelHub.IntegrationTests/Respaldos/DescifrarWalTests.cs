using System.Text;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.VmAux;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// <c>scripts/descifrar-wal.sh</c> (D28; PLA-17): prepara el archivo de cambios para una restauración. Como cualquiera con la clave pública
/// puede subir candidatos, no elige entre ellos: descarta lo que no coincide con su suma, no se descifra, no se descomprime o no mide un
/// segmento, y se detiene antes de un segmento que falte, no tenga candidato válido o tenga dos distintos. Solo prepara la línea de tiempo de
/// <c>--desde</c>; de las demás deja historias vacías. Un fallo del equipo no se presenta como archivo incompleto. Los segmentos de la prueba
/// miden 1 KiB (<c>BABELHUB_TAMANO_DE_SEGMENTO</c>).
/// </summary>
[Collection(RespaldosCollection.Nombre)]
public sealed class DescifrarWalTests(RespaldosFixture entorno) : IAsyncLifetime
{
    private const string Uno = "000000010000000000000001";
    private const string Dos = "000000010000000000000002";
    private const string Tres = "000000010000000000000003";

    private IContainer _maquina = null!;

    public async Task InitializeAsync()
    {
        var builder = new ContainerBuilder(entorno.Etiqueta)
            .WithEntrypoint("sleep", "3600")
            .WithCreateParameterModifier(p => p.User = "root")
            .WithResourceMapping(Encoding.UTF8.GetBytes(RestauracionDeEnsayoTests.CustodiaDePrueba(entorno.Destinatario)), "/tmp/custodia.md")
            .WithResourceMapping(Encoding.UTF8.GetBytes(entorno.Identidad + "\n"), "/tmp/identidad.txt");
        foreach (var script in new[] { "descifrar-respaldo.sh", "descifrar-wal.sh" })
        {
            builder = builder.WithResourceMapping(File.ReadAllBytes(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", script)), "/opt/scripts/" + script);
        }

        _maquina = builder.Build();
        await _maquina.StartAsync();
        var preparar = await Root($$"""
            cat > /tmp/subir.sh <<'FIN'
            #!/bin/bash
            # subir.sh <nombre> <letra> [bytes] [destinatario]: un objeto como los que sube archivar-wal.sh.
            set -e
            mkdir -p /tmp/e
            head -c "${3:-1024}" /dev/zero | tr '\0' "$2" | zstd -q -c | age -r "${4:-{{entorno.Destinatario}}}" -o /tmp/e/.nuevo
            suma="$(sha256sum /tmp/e/.nuevo | cut -d' ' -f1)"
            mv /tmp/e/.nuevo "/tmp/e/$1-$suma.zst.age"
            FIN
            sed -i 's/\r$//' /tmp/subir.sh && chmod 0755 /tmp/subir.sh
            """.ReplaceLineEndings("\n"));
        Assert.True(preparar.ExitCode == 0, preparar.Stderr);
    }

    public async Task DisposeAsync() => await _maquina.DisposeAsync();

    private async Task<ExecResult> Root(string orden) => await _maquina.ExecAsync(["bash", "-c", orden]);

    private static string Grupo(string texto, string patron) => Regex.Match(texto, patron, RegexOptions.None, TimeSpan.FromSeconds(2)).Groups[1].Value.Trim();

    private async Task<(string Codigo, string Listos, string Errores)> Preparar(string objetos, string opciones = $"--desde {Uno}", string clave = "cat /tmp/identidad.txt", string custodia = "/tmp/custodia.md")
    {
        var resultado = await Root($"rm -rf /tmp/e /tmp/wal && mkdir -p /tmp/e && {objetos} && {clave} | BABELHUB_TAMANO_DE_SEGMENTO=1024 bash /opt/scripts/descifrar-wal.sh --entrada /tmp/e --destino /tmp/wal --custodia {custodia} {opciones}; echo \"codigo=$?\"; echo \"listos=[$(ls -A /tmp/wal 2>/dev/null | tr '\\n' ' ')]\"");
        return (Grupo(resultado.Stdout, @"codigo=(\d+)"), Grupo(resultado.Stdout, @"listos=\[(.*?)\]"), resultado.Stderr);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task DejaCadaSegmentoConSuNombreDePostgreSql_ConSuContenidoEnClaro_SoloParaElDueno()
    {
        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Dos} b && printf x > /tmp/e/{Dos}.00000028.backup");
        var contenido = await Root($"head -c 8 /tmp/wal/{Dos}; echo; wc -c < /tmp/wal/{Dos}; stat -c '%a' /tmp/wal /tmp/wal/{Dos}");

        Assert.True(codigo == "0", errores);
        Assert.Equal($"{Uno} {Dos}", listos);
        Assert.Contains($"seguidos de {Uno} a {Dos}", errores, StringComparison.Ordinal);
        Assert.Equal(["bbbbbbbb", "1024", "700", "600"], contenido.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task DosSubidasDelMismoSegmentoConElMismoContenido_NoDanan()
    {
        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Uno} a && test $(ls /tmp/e | wc -l) = 2");

        Assert.True(codigo == "0", errores);
        Assert.Equal(Uno, listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task SiDosCandidatosDifieren_SeDetieneAntesDeEseSegmento_SinElegirNinguno()
    {
        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Dos} b && /tmp/subir.sh {Dos} x && /tmp/subir.sh {Tres} c");

        Assert.Equal("4", codigo);
        Assert.Contains($"DETENIDO antes de {Dos}", errores, StringComparison.Ordinal);
        Assert.Equal(Uno, listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task LosCandidatosInvalidosSeDescartan_YGanaElUnicoValido()
    {
        // Uno que no coincide con la suma de su nombre, uno cifrado para otra clave, uno que no mide un segmento y uno de relleno enorme.
        var otra = (await Root("age-keygen 2>&1 | grep -o 'age1[0-9a-z]*' | head -n 1")).Stdout.Trim();
        var (codigo, listos, errores) = await Preparar(
            $"/tmp/subir.sh {Uno} a && printf basura > /tmp/e/{Uno}-{new string('0', 64)}.zst.age && /tmp/subir.sh {Uno} x 1024 {otra} && /tmp/subir.sh {Uno} y 500 && /tmp/subir.sh {Uno} z 4000000 && echo ajeno > /tmp/e/{Uno}.partial");
        var contenido = await Root($"head -c 4 /tmp/wal/{Uno}");

        Assert.True(codigo == "0", errores);
        Assert.Equal(Uno, listos);
        Assert.Equal("aaaa", contenido.Stdout);
        Assert.Contains("no coincide con la suma", errores, StringComparison.Ordinal);
        Assert.Contains("no mide un segmento entero", errores, StringComparison.Ordinal);
        Assert.Contains("no se pudo descifrar y descomprimir", errores, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-17")]
    // Sin ningún candidato válido, falta en medio, falta el primero y falta el último pedido.
    [InlineData($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Dos} b 500 && /tmp/subir.sh {Tres} c", $"--desde {Uno}", Dos, "no hay ningún candidato válido", Uno)]
    [InlineData($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Tres} c", $"--desde {Uno}", Dos, "falta en el archivo", Uno)]
    [InlineData($"/tmp/subir.sh {Dos} b", $"--desde {Uno}", Uno, "falta en el archivo", "")]
    [InlineData($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Dos} b", $"--desde {Uno} --hasta {Tres}", Tres, "falta en el archivo", $"{Uno} {Dos}")]
    [InlineData("true", $"--desde {Uno}", Uno, "no hay ningún segmento", "")]
    public async Task AnteUnHuecoOUnSegmentoSinCandidato_SeDetieneAntes_YLoDice(string objetos, string opciones, string antesDe, string motivo, string quedan)
    {
        var (codigo, listos, errores) = await Preparar(objetos, opciones);

        Assert.Equal("4", codigo);
        Assert.Contains($"DETENIDO antes de {antesDe}: ", errores, StringComparison.Ordinal);
        Assert.Contains(motivo, errores, StringComparison.Ordinal);
        Assert.Contains("bórralo", errores, StringComparison.Ordinal);
        Assert.Equal(quedan, listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task LaContinuidadPasaDeUnArchivoLogicoAlSiguiente()
    {
        const string Ultimo = "0000000100000000000000FF";
        const string Siguiente = "000000010000000100000000";

        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Ultimo} a && /tmp/subir.sh {Siguiente} b", $"--desde {Ultimo}");

        Assert.True(codigo == "0", errores);
        Assert.Equal($"{Ultimo} {Siguiente}", listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task DesdeYHasta_LimitanLosSegmentos()
    {
        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Dos} b && /tmp/subir.sh {Tres} c", $"--desde {Dos} --hasta {Dos}");

        Assert.True(codigo == "0", errores);
        Assert.Equal(Dos, listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task DeOtraLineaDeTiempoNoSeLeeNada_YSuHistoriaQuedaVaciaParaNoReutilizarElNumero()
    {
        const string Ajeno = "000000020000000000000002";

        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a && /tmp/subir.sh {Dos} b && /tmp/subir.sh {Ajeno} x && /tmp/subir.sh 00000002.history h 40");
        var historia = await Root("wc -c < /tmp/wal/00000002.history");

        Assert.True(codigo == "0", errores);
        Assert.Equal($"{Uno} {Dos} 00000002.history", listos);
        Assert.Equal("0", historia.Stdout.Trim());
        Assert.Contains("historia de otra línea de tiempo", errores, StringComparison.Ordinal);
    }

    private const string Propio = "000000020000000000000005";
    private const string CopiaConHistoria = "mkdir -p /tmp/copia/pg_wal && head -c 40 /dev/zero | tr '\\0' h > /tmp/copia/pg_wal/00000002.history";

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task UnaLineaPosteriorSinCopiaUsaLaHistoriaDescargada_YSinNingunaSeDetiene()
    {
        var (con, listos, errores) = await Preparar($"/tmp/subir.sh {Propio} a && /tmp/subir.sh 000000010000000000000005 x && /tmp/subir.sh 00000002.history h 40", $"--desde {Propio}");
        var historia = await Root($"cat /tmp/wal/00000002.history; echo; head -c 4 /tmp/wal/{Propio}");
        var (sin, nada, motivo) = await Preparar($"/tmp/subir.sh {Propio} a", $"--desde {Propio}");

        Assert.True(con == "0", errores);
        Assert.Equal($"00000002.history {Propio}", listos);
        Assert.Equal([new string('h', 40), "aaaa"], historia.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        Assert.Equal("4", sin);
        Assert.Contains("falta la historia", motivo, StringComparison.Ordinal);
        Assert.Equal(string.Empty, nada);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-17")]
    // La historia ya retirada del almacén, la misma descargada y una descargada ilegible: manda la que trae la copia.
    [InlineData("true")]
    [InlineData("/tmp/subir.sh 00000002.history h 40")]
    [InlineData("printf basura > /tmp/e/00000002.history-0000000000000000000000000000000000000000000000000000000000000000.zst.age")]
    public async Task ConLaCopiaExtraida_UsaLaHistoriaQueTraeAunqueElAlmacenYaNoLaTenga(string descargado)
    {
        var (codigo, listos, errores) = await Preparar($"{CopiaConHistoria} && /tmp/subir.sh {Propio} a && {descargado}", $"--desde {Propio} --restaurada /tmp/copia");
        var historia = await Root("cat /tmp/wal/00000002.history");

        Assert.True(codigo == "0", errores);
        Assert.Equal($"00000002.history {Propio}", listos);
        Assert.Equal(new string('h', 40), historia.Stdout);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task UnaHistoriaDescargadaQueDifiereDeLaDeLaCopia_Detiene()
    {
        var (codigo, listos, errores) = await Preparar($"{CopiaConHistoria} && /tmp/subir.sh {Propio} a && /tmp/subir.sh 00000002.history x 40", $"--desde {Propio} --restaurada /tmp/copia");

        Assert.Equal("4", codigo);
        Assert.Contains("difiere de la que trae la copia", errores, StringComparison.Ordinal);
        Assert.Equal(string.Empty, listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task SinElArchivoDeCambios_SoloReservaLosNumerosDeLinea_SinPedirLaClave()
    {
        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a && /tmp/subir.sh 00000002.history h 40 && /tmp/subir.sh 00000003.history h 40", $"--desde {Uno} --sin-cambios", clave: "true");

        Assert.True(codigo == "0", errores);
        Assert.Equal("00000002.history 00000003.history", listos);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-17")]
    // Un problema de este equipo se dice como tal (códigos 1, 2 y 3): nunca como archivo incompleto.
    [InlineData("age-keygen 2>/dev/null | grep AGE-SECRET-KEY", "/tmp/custodia.md", "3", "NO corresponde")]
    [InlineData("cat /tmp/identidad.txt", "/tmp/no-existe.md", "2", "no existe /tmp/no-existe.md")]
    [InlineData("echo no-soy-una-clave", "/tmp/custodia.md", "1", "no es una clave privada")]
    public async Task ConLaClaveOLaCustodiaMal_NoSeDejaNada_YElCodigoLoDistingue(string clave, string custodia, string esperado, string mensaje)
    {
        var (codigo, listos, errores) = await Preparar($"/tmp/subir.sh {Uno} a", clave: clave, custodia: custodia);

        Assert.Equal(esperado, codigo);
        Assert.Contains(mensaje, errores, StringComparison.Ordinal);
        Assert.DoesNotContain("DETENIDO", errores, StringComparison.Ordinal);
        Assert.Equal(string.Empty, listos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public async Task NoEscribeSobreUnaCarpetaConContenido_NiAceptaLaClaveComoArgumento_YExigeDesde()
    {
        var ocupada = await Root($"mkdir -p /tmp/ocupada /tmp/f && echo x > /tmp/ocupada/y && bash /opt/scripts/descifrar-wal.sh --entrada /tmp/f --destino /tmp/ocupada --desde {Uno} < /tmp/identidad.txt; echo codigo=$?; ls /tmp/ocupada");
        var argumento = await Root($"bash /opt/scripts/descifrar-wal.sh --entrada /tmp/f --destino /tmp/otra --desde {Uno} --clave x; echo codigo=$?");
        var sinDesde = await Root("mkdir -p /tmp/f && bash /opt/scripts/descifrar-wal.sh --entrada /tmp/f --destino /tmp/otra < /tmp/identidad.txt; echo codigo=$?");

        Assert.Contains("codigo=2\ny", ocupada.Stdout.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("no está vacía", ocupada.Stderr, StringComparison.Ordinal);
        Assert.Contains("codigo=2", argumento.Stdout, StringComparison.Ordinal);
        Assert.Contains("codigo=2", sinDesde.Stdout, StringComparison.Ordinal);
    }
}
