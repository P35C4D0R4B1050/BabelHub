using System.IO.Compression;
using System.Xml;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Catalogo;

namespace BabelHub.IntegrationTests.CargaInicial;

/// <summary>
/// La carpeta de portadas y el archivo de muestra de la carga inicial (T04; CIN-05, CIN-08, CIN-09; AC-CIN-05, AC-CIN-07): solo un archivo regular simple dentro de la carpeta, con lectura acotada a 2 MB más un byte, y un EPUB de muestra de
/// contenido fijo que pasa la verificación de contenido del catálogo (CAT-16) y dice que no es el libro.
/// </summary>
public sealed class CarpetaYGeneradorDeLaCargaTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "babelhub-carpeta-" + Guid.NewGuid().ToString("N"));

    public CarpetaYGeneradorDeLaCargaTests()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "buena.png"), [1, 2, 3, 4, 5]);
        File.WriteAllBytes(Path.Combine(_carpeta, "grande.png"), new byte[3 * 1024 * 1024]);
        Directory.CreateDirectory(Path.Combine(_carpeta, "carpeta.png"));
    }

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    [Fact]
    [Trait("Criterio", "AC-CIN-05")]
    public async Task Un_archivo_regular_simple_se_abre_de_solo_lectura_y_la_lectura_se_acota_a_2_MB_mas_un_byte()
    {
        var origen = new CarpetaDePortadasDeLaCarga(_carpeta);

        await using (var pequeno = await origen.AbrirPortada("buena.png"))
        {
            Assert.NotNull(pequeno);
            Assert.False(pequeno!.CanWrite);
            using var copia = new MemoryStream();
            await pequeno.CopyToAsync(copia);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, copia.ToArray());
        }

        await using var grande = await origen.AbrirPortada("grande.png");
        using var leido = new MemoryStream();
        await grande!.CopyToAsync(leido);
        Assert.Equal((2L * 1024 * 1024) + 1, leido.Length);
    }

    [Theory]
    [InlineData("../buena.png")]
    [InlineData("..\\buena.png")]
    [InlineData("sub/buena.png")]
    [InlineData("sub\\buena.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:buena.png")]
    [InlineData("buena.png:flujo")]
    [InlineData("buena.png::$DATA")]
    [InlineData("CON")]
    [InlineData("con.png")]
    [InlineData("NUL.txt")]
    [InlineData("COM1.png")]
    [InlineData("lpt9")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("buena.")]
    [InlineData(".oculta.png")]
    [InlineData("con espacio.png")]
    [InlineData("carpeta.png")]
    [InlineData("no-existe.png")]
    [Trait("Criterio", "AC-CIN-05")]
    public async Task Un_nombre_que_no_es_el_de_un_archivo_regular_simple_de_la_carpeta_se_rechaza(string nombre)
    {
        var origen = new CarpetaDePortadasDeLaCarga(_carpeta);

        Assert.Null(await origen.AbrirPortada(nombre));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-05")]
    public async Task Un_nulo_o_un_nombre_demasiado_largo_se_rechaza()
    {
        var origen = new CarpetaDePortadasDeLaCarga(_carpeta);

        Assert.Null(await origen.AbrirPortada(null!));
        Assert.Null(await origen.AbrirPortada(new string('a', 101) + ".png"));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-05")]
    public async Task Un_enlace_simbolico_a_un_archivo_fuera_de_la_carpeta_se_rechaza()
    {
        var fuera = Path.Combine(Path.GetTempPath(), "babelhub-fuera-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(fuera, [9, 9, 9]);
        try
        {
            try
            {
                File.CreateSymbolicLink(Path.Combine(_carpeta, "enlace.png"), fuera);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Sin privilegio para crear enlaces simbólicos en este equipo: la comprobación queda para el CI de Linux.
                return;
            }

            Assert.Null(await new CarpetaDePortadasDeLaCarga(_carpeta).AbrirPortada("enlace.png"));
        }
        finally
        {
            File.Delete(fuera);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-07")]
    public async Task El_epub_de_muestra_pasa_la_verificacion_de_contenido_del_catalogo_y_dice_que_no_es_el_libro()
    {
        var bytes = new GeneradorDeLibroDeMuestra().GenerarEpub("Obra de muestra");

        var motivo = await VerificadorDeContenido.VerificarEstructura(TipoDeArchivo.Epub, new LectorEnMemoria(bytes));

        Assert.Null(motivo);
        Assert.True(bytes.Length < 20 * 1024);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(CompressionLevel.NoCompression, EsAlmacenada(zip.Entries[0]) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
        using var lector = new StreamReader(zip.GetEntry("OEBPS/muestra.xhtml")!.Open());
        var xhtml = await lector.ReadToEndAsync();
        Assert.Contains("no es el libro", xhtml, StringComparison.Ordinal);
        Assert.Contains("Obra de muestra", xhtml, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-07")]
    public void El_titulo_se_escapa_como_xml_y_el_contenido_sigue_siendo_xml_bien_formado_y_el_mismo_siempre()
    {
        const string hostil = "<script>alert(1)</script> & \"comillas\" ]]>";
        var generador = new GeneradorDeLibroDeMuestra();

        var uno = generador.GenerarEpub(hostil);
        var otro = generador.GenerarEpub(hostil);

        Assert.Equal(uno, otro);
        using var zip = new ZipArchive(new MemoryStream(uno));
        foreach (var entrada in zip.Entries.Where(e => e.FullName != "mimetype"))
        {
            using var flujo = entrada.Open();
            var documento = new XmlDocument();
            documento.Load(flujo);
        }

        using var lector = new StreamReader(zip.GetEntry("OEBPS/muestra.xhtml")!.Open());
        var texto = lector.ReadToEnd();
        Assert.DoesNotContain("<script>", texto, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", texto, StringComparison.Ordinal);
    }

    private static bool EsAlmacenada(ZipArchiveEntry entrada) => entrada.CompressedLength == entrada.Length;

    private sealed class LectorEnMemoria(byte[] bytes) : ILectorDeRangos
    {
        public long Tamano => bytes.Length;

        public Task<byte[]> Leer(long desde, int cantidad, CancellationToken cancelacion = default)
        {
            var restante = (int)Math.Max(0, Math.Min(cantidad, bytes.Length - desde));
            return Task.FromResult(bytes.AsSpan((int)desde, restante).ToArray());
        }
    }
}
