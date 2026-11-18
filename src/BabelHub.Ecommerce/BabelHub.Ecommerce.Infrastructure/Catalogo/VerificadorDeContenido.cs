using System.Buffers.Binary;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>Una lectura por rango sobre un objeto ya escrito: nunca el objeto completo (CAT-16, D14).</summary>
public interface ILectorDeRangos
{
    /// <summary>El tamaño real del objeto, en bytes.</summary>
    long Tamano { get; }

    /// <summary>Lee <paramref name="cantidad"/> bytes desde <paramref name="desde"/>; si el objeto termina antes devuelve menos.</summary>
    Task<byte[]> Leer(long desde, int cantidad, CancellationToken cancelacion = default);
}

/// <summary>
/// La verificación del contenido de un archivo del catálogo (CAT-16, plan §6): <b>nunca descomprime ni interpreta el libro</b>. Antes de escribir se decide el tipo por los primeros bytes y, en una imagen, se leen las dimensiones de su cabecera;
/// después de escribir se verifica la estructura con lecturas por rango acotadas: de un EPUB, solo el directorio central del ZIP (hasta 1 MiB y 2 000 entradas) y la entrada <c>mimetype</c> (hasta 64 bytes, de valor
/// <c>application/epub+zip</c>); de un PDF, la cabecera <c>%PDF-</c> y el <c>%%EOF</c> de la cola. Se rechazan los ZIP truncados, con tamaños declarados incoherentes, con más entradas que el tope o con un total descomprimido declarado
/// de más de 1 GiB (una bomba de compresión; el libro no se descomprime aquí pero sí en el lector).
/// </summary>
public static class VerificadorDeContenido
{
    public const int MaximoDeEntradasDeUnEpub = 2000;

    public const int MaximoDelDirectorioCentral = 1024 * 1024;

    public const int MaximoDelMimetype = 64;

    /// <summary>El total descomprimido declarado de un EPUB no pasa de 1 GiB (de 50 MiB comprimidos, una razón de 20 a 1).</summary>
    public const long MaximoDescomprimidoDeclarado = 1024L * 1024 * 1024;

    /// <summary>Los bytes iniciales que se leen de una imagen para encontrar sus dimensiones (una cabecera JPEG puede llevar segmentos <c>APPn</c> de hasta 64 KiB antes del cuadro).</summary>
    public const int PrefijoDeImagen = 256 * 1024;

    public const string TipoDeMedioDelMimetype = "application/epub+zip";

    private const int FirmaFinDelDirectorio = 0x06054b50;
    private const int FirmaDeEntradaCentral = 0x02014b50;
    private const int FirmaLocal = 0x04034b50;
    private const int TamanoFinDelDirectorio = 22;
    private const int TamanoEntradaCentral = 46;
    private const int TamanoCabeceraLocal = 30;
    private const int ColaDeUnPdf = 1024;

    /// <summary>
    /// Las dimensiones de una imagen leídas de los primeros bytes: <see langword="null"/> si la cabecera está truncada o mal formada (no se encuentran). No decodifica los píxeles.
    /// </summary>
    public static (int Ancho, int Alto)? DimensionesDeImagen(TipoDeArchivo tipo, ReadOnlySpan<byte> prefijo) =>
        tipo switch
        {
            TipoDeArchivo.Png => DimensionesDePng(prefijo),
            TipoDeArchivo.Jpeg => DimensionesDeJpeg(prefijo),
            TipoDeArchivo.Webp => DimensionesDeWebp(prefijo),
            _ => null,
        };

    /// <summary>Verifica la estructura de un libro ya escrito por lecturas por rango; <see langword="null"/> si es válida.</summary>
    public static async Task<MotivoDeRechazoDeArchivo?> VerificarEstructura(TipoDeArchivo tipo, ILectorDeRangos lector, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lector);
        return tipo switch
        {
            TipoDeArchivo.Epub => await VerificarEpub(lector, cancelacion).ConfigureAwait(false),
            TipoDeArchivo.Pdf => await VerificarPdf(lector, cancelacion).ConfigureAwait(false),
            _ => null, // las imágenes se verifican antes de escribir
        };
    }

    // ---------- EPUB ----------

    private static async Task<MotivoDeRechazoDeArchivo?> VerificarEpub(ILectorDeRangos lector, CancellationToken cancelacion)
    {
        var tamano = lector.Tamano;
        if (tamano < TamanoFinDelDirectorio)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        // 1. El fin del directorio central, en la cola del archivo (con un comentario de hasta 65 535 bytes).
        var largoDeLaCola = (int)Math.Min(tamano, TamanoFinDelDirectorio + ushort.MaxValue);
        var inicioDeLaCola = tamano - largoDeLaCola;
        var cola = await lector.Leer(inicioDeLaCola, largoDeLaCola, cancelacion).ConfigureAwait(false);
        var posicionEnLaCola = BuscarFinDelDirectorio(cola);
        if (posicionEnLaCola < 0)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        var fin = cola.AsSpan(posicionEnLaCola, TamanoFinDelDirectorio);
        var disco = BinaryPrimitives.ReadUInt16LittleEndian(fin[4..]);
        var discoDelDirectorio = BinaryPrimitives.ReadUInt16LittleEndian(fin[6..]);
        var entradasEnEsteDisco = BinaryPrimitives.ReadUInt16LittleEndian(fin[8..]);
        var entradas = BinaryPrimitives.ReadUInt16LittleEndian(fin[10..]);
        var tamanoDelDirectorio = BinaryPrimitives.ReadUInt32LittleEndian(fin[12..]);
        var inicioDelDirectorio = BinaryPrimitives.ReadUInt32LittleEndian(fin[16..]);
        var posicionDelFin = inicioDeLaCola + posicionEnLaCola;

        // Sin varios discos, sin ZIP64 (un archivo de 50 MiB no lo necesita) y con un directorio que cabe antes del fin.
        var esZip64 = entradas == ushort.MaxValue || tamanoDelDirectorio == uint.MaxValue || inicioDelDirectorio == uint.MaxValue;
        if (disco != 0 || discoDelDirectorio != 0 || entradasEnEsteDisco != entradas || esZip64
            || entradas is 0 or > MaximoDeEntradasDeUnEpub
            || tamanoDelDirectorio > MaximoDelDirectorioCentral || tamanoDelDirectorio < (long)entradas * TamanoEntradaCentral
            || inicioDelDirectorio + (long)tamanoDelDirectorio > posicionDelFin)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        // 2. El directorio central completo (acotado a 1 MiB).
        var directorio = await lector.Leer(inicioDelDirectorio, (int)tamanoDelDirectorio, cancelacion).ConfigureAwait(false);
        if (directorio.Length != tamanoDelDirectorio)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        long descomprimido = 0;
        long? mimetypeLocal = null;
        long mimetypeComprimido = 0;
        long mimetypeDescomprimido = 0;
        var mimetypeMetodo = -1;
        var posicion = 0;
        for (var i = 0; i < entradas; i++)
        {
            if (posicion + TamanoEntradaCentral > directorio.Length)
            {
                return MotivoDeRechazoDeArchivo.EstructuraInvalida;
            }

            var entrada = directorio.AsSpan(posicion);
            if (BinaryPrimitives.ReadInt32LittleEndian(entrada) != FirmaDeEntradaCentral)
            {
                return MotivoDeRechazoDeArchivo.EstructuraInvalida;
            }

            var metodo = BinaryPrimitives.ReadUInt16LittleEndian(entrada[10..]);
            var comprimida = BinaryPrimitives.ReadUInt32LittleEndian(entrada[20..]);
            var sinComprimir = BinaryPrimitives.ReadUInt32LittleEndian(entrada[24..]);
            var largoDelNombre = BinaryPrimitives.ReadUInt16LittleEndian(entrada[28..]);
            var largoDelExtra = BinaryPrimitives.ReadUInt16LittleEndian(entrada[30..]);
            var largoDelComentario = BinaryPrimitives.ReadUInt16LittleEndian(entrada[32..]);
            var inicioLocal = BinaryPrimitives.ReadUInt32LittleEndian(entrada[42..]);
            var siguiente = posicion + TamanoEntradaCentral + largoDelNombre + largoDelExtra + largoDelComentario;
            if (siguiente > directorio.Length)
            {
                return MotivoDeRechazoDeArchivo.EstructuraInvalida;
            }

            // Tamaños declarados coherentes: solo «almacenado» (0) o «deflate» (8); un almacenado pesa lo mismo; los datos caben antes del directorio.
            if (metodo is not (0 or 8) || (metodo == 0 && comprimida != sinComprimir)
                || comprimida == uint.MaxValue || sinComprimir == uint.MaxValue || inicioLocal == uint.MaxValue
                || inicioLocal + (long)TamanoCabeceraLocal + largoDelNombre + comprimida > inicioDelDirectorio)
            {
                return MotivoDeRechazoDeArchivo.EstructuraInvalida;
            }

            descomprimido += sinComprimir;
            if (descomprimido > MaximoDescomprimidoDeclarado)
            {
                return MotivoDeRechazoDeArchivo.EstructuraInvalida;
            }

            if (entrada.Slice(TamanoEntradaCentral, largoDelNombre).SequenceEqual("mimetype"u8))
            {
                mimetypeLocal = inicioLocal;
                mimetypeComprimido = comprimida;
                mimetypeDescomprimido = sinComprimir;
                mimetypeMetodo = metodo;
            }

            posicion = siguiente;
        }

        if (posicion != directorio.Length)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        // 3. La entrada «mimetype»: almacenada, de hasta 64 bytes reales y con el valor del EPUB.
        if (mimetypeLocal is not { } local || mimetypeMetodo != 0 || mimetypeDescomprimido != mimetypeComprimido || mimetypeDescomprimido is < 1 or > MaximoDelMimetype)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        var cabecera = await lector.Leer(local, TamanoCabeceraLocal, cancelacion).ConfigureAwait(false);
        if (cabecera.Length != TamanoCabeceraLocal || BinaryPrimitives.ReadInt32LittleEndian(cabecera) != FirmaLocal)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        var inicioDeLosDatos = local + TamanoCabeceraLocal + BinaryPrimitives.ReadUInt16LittleEndian(cabecera.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(cabecera.AsSpan(28));
        if (inicioDeLosDatos + mimetypeComprimido > inicioDelDirectorio)
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        var datos = await lector.Leer(inicioDeLosDatos, (int)mimetypeComprimido, cancelacion).ConfigureAwait(false);
        return datos.AsSpan().SequenceEqual("application/epub+zip"u8) ? null : MotivoDeRechazoDeArchivo.EstructuraInvalida;
    }

    private static int BuscarFinDelDirectorio(byte[] cola)
    {
        // El último registro de fin de directorio cuyo comentario llega exactamente al final del archivo.
        for (var posicion = cola.Length - TamanoFinDelDirectorio; posicion >= 0; posicion--)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(cola.AsSpan(posicion)) == FirmaFinDelDirectorio
                && posicion + TamanoFinDelDirectorio + BinaryPrimitives.ReadUInt16LittleEndian(cola.AsSpan(posicion + 20)) == cola.Length)
            {
                return posicion;
            }
        }

        return -1;
    }

    // ---------- PDF ----------

    private static async Task<MotivoDeRechazoDeArchivo?> VerificarPdf(ILectorDeRangos lector, CancellationToken cancelacion)
    {
        var cabecera = await lector.Leer(0, 5, cancelacion).ConfigureAwait(false);
        if (!cabecera.AsSpan().SequenceEqual("%PDF-"u8))
        {
            return MotivoDeRechazoDeArchivo.EstructuraInvalida;
        }

        var largo = (int)Math.Min(lector.Tamano, ColaDeUnPdf);
        var cola = await lector.Leer(lector.Tamano - largo, largo, cancelacion).ConfigureAwait(false);
        return cola.AsSpan().LastIndexOf("%%EOF"u8) >= 0 ? null : MotivoDeRechazoDeArchivo.EstructuraInvalida;
    }

    // ---------- Imágenes ----------

    private static (int, int)? DimensionesDePng(ReadOnlySpan<byte> datos)
    {
        // Firma (8), longitud (4), «IHDR» (4), ancho (4), alto (4).
        if (datos.Length < 24 || !datos.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var ancho = BinaryPrimitives.ReadUInt32BigEndian(datos[16..]);
        var alto = BinaryPrimitives.ReadUInt32BigEndian(datos[20..]);
        return ancho > int.MaxValue || alto > int.MaxValue ? (int.MaxValue, int.MaxValue) : ((int)ancho, (int)alto);
    }

    private static (int, int)? DimensionesDeJpeg(ReadOnlySpan<byte> datos)
    {
        var posicion = 2; // tras FF D8
        while (posicion + 4 <= datos.Length)
        {
            if (datos[posicion] != 0xFF)
            {
                return null;
            }

            var marcador = datos[posicion + 1];
            if (marcador == 0xFF)
            {
                posicion++; // relleno
                continue;
            }

            // Marcadores sin longitud: RST0-RST7, SOI, TEM.
            if (marcador is >= 0xD0 and <= 0xD8 or 0x01)
            {
                posicion += 2;
                continue;
            }

            var largo = BinaryPrimitives.ReadUInt16BigEndian(datos[(posicion + 2)..]);
            if (largo < 2)
            {
                return null;
            }

            var esCuadro = marcador is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
            if (esCuadro)
            {
                if (posicion + 9 > datos.Length || largo < 8)
                {
                    return null;
                }

                var alto = BinaryPrimitives.ReadUInt16BigEndian(datos[(posicion + 5)..]);
                var ancho = BinaryPrimitives.ReadUInt16BigEndian(datos[(posicion + 7)..]);
                return (ancho, alto);
            }

            if (marcador == 0xDA)
            {
                return null; // empiezan los datos de imagen sin haber visto el cuadro
            }

            posicion += 2 + largo;
        }

        return null;
    }

    private static (int, int)? DimensionesDeWebp(ReadOnlySpan<byte> datos)
    {
        if (datos.Length < 30)
        {
            return null;
        }

        var trozo = datos.Slice(12, 4);
        if (trozo.SequenceEqual("VP8X"u8))
        {
            // Ancho y alto menos uno, en 24 bits cada uno.
            var ancho = 1 + (datos[24] | (datos[25] << 8) | (datos[26] << 16));
            var alto = 1 + (datos[27] | (datos[28] << 8) | (datos[29] << 16));
            return (ancho, alto);
        }

        if (trozo.SequenceEqual("VP8L"u8))
        {
            if (datos[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(datos[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (trozo.SequenceEqual("VP8 "u8))
        {
            if (datos[23] != 0x9D || datos[24] != 0x01 || datos[25] != 0x2A)
            {
                return null;
            }

            return (BinaryPrimitives.ReadUInt16LittleEndian(datos[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(datos[28..]) & 0x3FFF);
        }

        return null;
    }
}
