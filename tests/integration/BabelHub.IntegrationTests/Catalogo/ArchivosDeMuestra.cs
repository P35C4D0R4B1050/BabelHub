using System.Buffers.Binary;
using System.Text;
using BabelHub.Ecommerce.Infrastructure.Catalogo;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>Archivos de muestra armados a mano (ZIP, EPUB, PDF y cabeceras de imagen) para probar el verificador y el almacén sin ficheros en el repositorio.</summary>
internal static class ArchivosDeMuestra
{
    /// <summary>Una entrada de un ZIP armado a mano; los tamaños declarados pueden mentir para probar las defensas.</summary>
    public sealed record EntradaZip(string Nombre, byte[] Datos, ushort Metodo = 0, uint? ComprimidoDeclarado = null, uint? DescomprimidoDeclarado = null, int ExtraEnElDirectorio = 0);

    public static byte[] Zip(IReadOnlyList<EntradaZip> entradas, int comentario = 0)
    {
        using var salida = new MemoryStream();
        var inicios = new List<uint>();
        foreach (var entrada in entradas)
        {
            inicios.Add((uint)salida.Position);
            var nombre = Encoding.UTF8.GetBytes(entrada.Nombre);
            var cabecera = new byte[30];
            BinaryPrimitives.WriteInt32LittleEndian(cabecera, 0x04034b50);
            BinaryPrimitives.WriteUInt16LittleEndian(cabecera.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(cabecera.AsSpan(8), entrada.Metodo);
            BinaryPrimitives.WriteUInt32LittleEndian(cabecera.AsSpan(18), entrada.ComprimidoDeclarado ?? (uint)entrada.Datos.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(cabecera.AsSpan(22), entrada.DescomprimidoDeclarado ?? (uint)entrada.Datos.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(cabecera.AsSpan(26), (ushort)nombre.Length);
            salida.Write(cabecera);
            salida.Write(nombre);
            salida.Write(entrada.Datos);
        }

        var inicioDelDirectorio = (uint)salida.Position;
        for (var i = 0; i < entradas.Count; i++)
        {
            var entrada = entradas[i];
            var nombre = Encoding.UTF8.GetBytes(entrada.Nombre);
            var central = new byte[46];
            BinaryPrimitives.WriteInt32LittleEndian(central, 0x02014b50);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(6), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(10), entrada.Metodo);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), entrada.ComprimidoDeclarado ?? (uint)entrada.Datos.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), entrada.DescomprimidoDeclarado ?? (uint)entrada.Datos.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)nombre.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(30), (ushort)entrada.ExtraEnElDirectorio);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(42), inicios[i]);
            salida.Write(central);
            salida.Write(nombre);
            salida.Write(new byte[entrada.ExtraEnElDirectorio]);
        }

        var tamanoDelDirectorio = (uint)(salida.Position - inicioDelDirectorio);
        var fin = new byte[22];
        BinaryPrimitives.WriteInt32LittleEndian(fin, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(fin.AsSpan(8), (ushort)entradas.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(fin.AsSpan(10), (ushort)entradas.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(fin.AsSpan(12), tamanoDelDirectorio);
        BinaryPrimitives.WriteUInt32LittleEndian(fin.AsSpan(16), inicioDelDirectorio);
        BinaryPrimitives.WriteUInt16LittleEndian(fin.AsSpan(20), (ushort)comentario);
        salida.Write(fin);
        salida.Write(new byte[comentario]);
        return salida.ToArray();
    }

    public static EntradaZip Mimetype(string valor = "application/epub+zip") => new("mimetype", Encoding.ASCII.GetBytes(valor));

    /// <summary>Un EPUB mínimo y válido: <c>mimetype</c> almacenado primero, el contenedor y un capítulo.</summary>
    public static byte[] Epub(int rellenoEnBytes = 0)
    {
        var entradas = new List<EntradaZip>
        {
            Mimetype(),
            new("META-INF/container.xml", Encoding.UTF8.GetBytes("<container/>")),
            new("OEBPS/capitulo.xhtml", Encoding.UTF8.GetBytes("<html><body>Hola</body></html>")),
        };
        if (rellenoEnBytes > 0)
        {
            entradas.Add(new EntradaZip("OEBPS/relleno.bin", new byte[rellenoEnBytes]));
        }

        return Zip(entradas);
    }

    /// <summary>Un EPUB válido que pesa exactamente <paramref name="tamano"/> bytes.</summary>
    public static byte[] EpubDe(long tamano)
    {
        var sinRelleno = Epub(1).Length - 1;
        var epub = Epub((int)(tamano - sinRelleno));
        return epub.Length == tamano ? epub : throw new InvalidOperationException("El EPUB de muestra no tiene el tamaño pedido.");
    }

    public static byte[] Pdf(bool conCierre = true) => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n" + (conCierre ? "%%EOF\n" : "startxref\n"));

    public static byte[] Png(int ancho, int alto, int tamanoTotal = 0)
    {
        using var salida = new MemoryStream();
        salida.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var cabecera = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(cabecera, (uint)ancho);
        BinaryPrimitives.WriteUInt32BigEndian(cabecera.AsSpan(4), (uint)alto);
        cabecera[8] = 8;
        cabecera[9] = 2;
        var largo = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(largo, 13);
        salida.Write(largo);
        salida.Write("IHDR"u8);
        salida.Write(cabecera);
        salida.Write(new byte[4]); // CRC (no se verifica)
        salida.Write(new byte[Math.Max(0, tamanoTotal - (int)salida.Length)]);
        return salida.ToArray();
    }

    public static byte[] Jpeg(int ancho, int alto, int tamanoTotal = 0, int bytesDeApp = 0)
    {
        using var salida = new MemoryStream();
        salida.Write([0xFF, 0xD8]);
        if (bytesDeApp > 0)
        {
            salida.Write([0xFF, 0xE1]);
            var largoApp = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(largoApp, (ushort)(bytesDeApp + 2));
            salida.Write(largoApp);
            salida.Write(new byte[bytesDeApp]);
        }

        salida.Write([0xFF, 0xC0, 0x00, 0x0B, 0x08]);
        var dimensiones = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(dimensiones, (ushort)alto);
        BinaryPrimitives.WriteUInt16BigEndian(dimensiones.AsSpan(2), (ushort)ancho);
        salida.Write(dimensiones);
        salida.Write([0x01, 0x01, 0x11, 0x00]);
        salida.Write([0xFF, 0xDA, 0x00, 0x02]);
        salida.Write(new byte[Math.Max(0, tamanoTotal - (int)salida.Length - 2)]);
        salida.Write([0xFF, 0xD9]);
        return salida.ToArray();
    }

    public static byte[] Webp(int ancho, int alto)
    {
        var datos = new byte[30];
        "RIFF"u8.CopyTo(datos);
        BinaryPrimitives.WriteUInt32LittleEndian(datos.AsSpan(4), 22);
        "WEBPVP8X"u8.CopyTo(datos.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(datos.AsSpan(16), 10);
        var a = ancho - 1;
        var h = alto - 1;
        datos[24] = (byte)a;
        datos[25] = (byte)(a >> 8);
        datos[26] = (byte)(a >> 16);
        datos[27] = (byte)h;
        datos[28] = (byte)(h >> 8);
        datos[29] = (byte)(h >> 16);
        return datos;
    }
}

/// <summary>Un lector de rangos sobre bytes en memoria que cuenta cuánto se leyó (para comprobar que la verificación no recorre el archivo).</summary>
internal sealed class LectorDeRangosEnMemoria(byte[] datos) : ILectorDeRangos
{
    public long Tamano => datos.Length;

    public long BytesLeidos { get; private set; }

    public Task<byte[]> Leer(long desde, int cantidad, CancellationToken cancelacion = default)
    {
        if (desde < 0 || desde >= datos.Length || cantidad <= 0)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        var largo = (int)Math.Min(cantidad, datos.Length - desde);
        BytesLeidos += largo;
        return Task.FromResult(datos.AsSpan((int)desde, largo).ToArray());
    }
}
