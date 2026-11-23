using System.IO.Compression;
using System.Text;
using System.Xml;
using BabelHub.Ecommerce.Application.Catalogo.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// El archivo de muestra de una edición digital de la carga (CIN-08, D03): un EPUB válido, generado en memoria (nunca en el disco ni en el repositorio) de pocos KB y de <b>contenido fijo</b>: lo único que viene de la fila es el título, escrito
/// siempre como texto XML escapado. Dice, en el propio libro, que no es el libro. La entrada <c>mimetype</c> va primero y sin comprimir, como exige el formato y como verifica <see cref="VerificadorDeContenido"/>.
/// </summary>
public sealed class GeneradorDeLibroDeMuestra : IGeneradorDeLibroDeMuestra
{
    private static readonly DateTimeOffset Instante = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Aviso = "Archivo de muestra de BabelHub. Este archivo no es el libro: la administración lo reemplaza por el libro real.";

    public byte[] GenerarEpub(string titulo)
    {
        ArgumentNullException.ThrowIfNull(titulo);
        var seguro = Limpiar(titulo);
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
        {
            Agregar(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            Agregar(
                zip,
                "META-INF/container.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>\n",
                CompressionLevel.Optimal);
            Agregar(zip, "OEBPS/content.opf", Paquete(seguro), CompressionLevel.Optimal);
            Agregar(zip, "OEBPS/nav.xhtml", Pagina(seguro, "Navegación", "<nav xmlns:epub=\"http://www.idpf.org/2007/ops\" epub:type=\"toc\"><ol><li><a href=\"muestra.xhtml\">Muestra</a></li></ol></nav>"), CompressionLevel.Optimal);
            Agregar(zip, "OEBPS/muestra.xhtml", Pagina(seguro, "Muestra", "<h1>" + Escapar(seguro) + "</h1><p>" + Escapar(Aviso) + "</p>"), CompressionLevel.Optimal);
        }

        return memoria.ToArray();
    }

    private static void Agregar(ZipArchive zip, string nombre, string contenido, CompressionLevel nivel)
    {
        var entrada = zip.CreateEntry(nombre, nivel);
        entrada.LastWriteTime = Instante;
        using var flujo = entrada.Open();
        var bytes = new UTF8Encoding(false).GetBytes(contenido);
        flujo.Write(bytes, 0, bytes.Length);
    }

    private static string Paquete(string titulo) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
        + "<dc:identifier id=\"id\">urn:babelhub:muestra</dc:identifier><dc:title>" + Escapar(titulo) + "</dc:title><dc:language>es</dc:language><meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>"
        + "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/><item id=\"muestra\" href=\"muestra.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>"
        + "<spine><itemref idref=\"muestra\"/></spine></package>\n";

    private static string Pagina(string titulo, string encabezado, string cuerpo) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"es\"><head><meta charset=\"utf-8\"/><title>" + Escapar(titulo) + " — " + Escapar(encabezado) + "</title></head><body>" + cuerpo + "</body></html>\n";

    /// <summary>El título como texto XML: se escapa y se descarta lo que XML 1.0 no admite.</summary>
    private static string Escapar(string texto) => System.Security.SecurityElement.Escape(texto) ?? string.Empty;

    private static string Limpiar(string titulo)
    {
        var sb = new StringBuilder(titulo.Length);
        foreach (var c in titulo)
        {
            if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }

        var limpio = sb.ToString().Trim();
        return limpio.Length > 200 ? limpio[..200] : limpio;
    }
}
