using System.Security.Cryptography;

namespace BabelHub.Ecommerce.Domain.Catalogo;

/// <summary>El tipo de un archivo del catálogo, decidido por su contenido y nunca por su nombre ni por el tipo declarado (CAT-16).</summary>
public enum TipoDeArchivo
{
    Epub,
    Pdf,
    Jpeg,
    Png,
    Webp,
}

/// <summary>Para qué se sube un archivo: una portada de obra o el libro de una edición digital. Una referencia solo sirve para su finalidad (una portada no sirve como libro).</summary>
public enum FinalidadDeArchivo
{
    Portada,
    Libro,
}

/// <summary>
/// Los topes de los archivos del catálogo (CAT-16, D09, <c>plan.md</c> §6). Los megabytes de la spec son mebibytes: 50 MB son 52 428 800 bytes y 2 MB son 2 097 152. El tope se
/// cuenta con los bytes <b>realmente leídos</b>, nunca con el tamaño declarado.
/// </summary>
public static class PoliticaDeArchivos
{
    public const long TopeDeLibroEnBytes = 50L * 1024 * 1024;

    public const long TopeDePortadaEnBytes = 2L * 1024 * 1024;

    public const int MaximoLadoDeImagen = 4096;

    public const long MaximoPixelesDeImagen = 16_000_000;

    /// <summary>Una referencia de archivo sin consumir vence a las 24 horas.</summary>
    public static readonly TimeSpan VigenciaDeReferencia = TimeSpan.FromHours(24);

    /// <summary>Las filas <c>consumida</c> de <c>archivos_pendientes</c> se purgan a los 7 días (solo la fila: el objeto ya vive en la edición o la obra).</summary>
    public static readonly TimeSpan RetencionDeReferenciasConsumidas = TimeSpan.FromDays(7);

    public static long TopePara(FinalidadDeArchivo finalidad) =>
        finalidad switch
        {
            FinalidadDeArchivo.Libro => TopeDeLibroEnBytes,
            FinalidadDeArchivo.Portada => TopeDePortadaEnBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(finalidad)),
        };

    /// <summary>Si los bytes leídos superan el tope de la finalidad. Exactamente el tope se acepta.</summary>
    public static bool SuperaElTope(FinalidadDeArchivo finalidad, long bytesLeidos) => bytesLeidos > TopePara(finalidad);

    /// <summary>Las dimensiones de una imagen (leídas de su cabecera antes de escribir): positivas, hasta 4 096 píxeles por lado y 16 millones en total.</summary>
    public static bool DimensionesValidas(int ancho, int alto) =>
        ancho is >= 1 and <= MaximoLadoDeImagen
        && alto is >= 1 and <= MaximoLadoDeImagen
        && (long)ancho * alto <= MaximoPixelesDeImagen;
}

/// <summary>Lo que se sabe de un tipo de archivo: su finalidad, el formato digital al que corresponde, su tipo de medio y su nombre estable (para la base y la auditoría).</summary>
public static class TiposDeArchivo
{
    /// <summary>Los bytes iniciales que bastan para decidir el tipo: el más largo es la cabecera de WebP (12).</summary>
    public const int BytesDeFirma = 12;

    public static FinalidadDeArchivo FinalidadDe(TipoDeArchivo tipo) =>
        tipo switch
        {
            TipoDeArchivo.Epub or TipoDeArchivo.Pdf => FinalidadDeArchivo.Libro,
            TipoDeArchivo.Jpeg or TipoDeArchivo.Png or TipoDeArchivo.Webp => FinalidadDeArchivo.Portada,
            _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
        };

    public static bool EsAcordeALaFinalidad(TipoDeArchivo tipo, FinalidadDeArchivo finalidad) =>
        Enum.IsDefined(tipo) && FinalidadDe(tipo) == finalidad;

    /// <summary>Un libro EPUB corresponde a una licencia de formato EPUB y uno PDF a una de formato PDF; una imagen no corresponde a ningún formato.</summary>
    public static bool EsAcordeAlFormato(TipoDeArchivo tipo, FormatoDigital formato) =>
        (tipo, formato) is (TipoDeArchivo.Epub, FormatoDigital.Epub) or (TipoDeArchivo.Pdf, FormatoDigital.Pdf);

    public static string TipoDeMedio(TipoDeArchivo tipo) =>
        tipo switch
        {
            TipoDeArchivo.Epub => "application/epub+zip",
            TipoDeArchivo.Pdf => "application/pdf",
            TipoDeArchivo.Jpeg => "image/jpeg",
            TipoDeArchivo.Png => "image/png",
            TipoDeArchivo.Webp => "image/webp",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
        };

    /// <summary>El nombre estable en minúsculas (columna <c>tipo_detectado</c>, detalle de auditoría).</summary>
    public static string Nombre(TipoDeArchivo tipo) =>
        tipo switch
        {
            TipoDeArchivo.Epub => "epub",
            TipoDeArchivo.Pdf => "pdf",
            TipoDeArchivo.Jpeg => "jpeg",
            TipoDeArchivo.Png => "png",
            TipoDeArchivo.Webp => "webp",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
        };

    public static bool TryDesdeNombre(string? nombre, out TipoDeArchivo tipo)
    {
        foreach (var candidato in Enum.GetValues<TipoDeArchivo>())
        {
            if (string.Equals(Nombre(candidato), nombre, StringComparison.Ordinal))
            {
                tipo = candidato;
                return true;
            }
        }

        tipo = default;
        return false;
    }

    /// <summary>
    /// El tipo según los <b>primeros bytes</b>: EPUB (un ZIP, que la Infraestructura confirma después por su estructura), PDF (<c>%PDF-</c>), JPEG, PNG y WebP. Cualquier otro contenido
    /// (un ejecutable renombrado, un texto) devuelve <see langword="null"/>: el tipo declarado o la extensión no cuentan.
    /// </summary>
    public static TipoDeArchivo? PorPrimerosBytes(ReadOnlySpan<byte> inicio)
    {
        if (inicio.StartsWith("PK\x03\x04"u8))
        {
            return TipoDeArchivo.Epub;
        }

        if (inicio.StartsWith("%PDF-"u8))
        {
            return TipoDeArchivo.Pdf;
        }

        if (inicio.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return TipoDeArchivo.Jpeg;
        }

        if (inicio.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return TipoDeArchivo.Png;
        }

        if (inicio.Length >= BytesDeFirma && inicio[..4].SequenceEqual("RIFF"u8) && inicio.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return TipoDeArchivo.Webp;
        }

        return null;
    }
}

/// <summary>La huella SHA-256 de un archivo (32 bytes). Se calcula durante la escritura y se guarda antes de referenciar el archivo (CAT-16).</summary>
public sealed class HuellaDeArchivo : IEquatable<HuellaDeArchivo>
{
    public const int LongitudEnBytes = 32;

    /// <summary>La huella corta de la auditoría: los 16 primeros caracteres (un hexadecimal de 32 o más lo redacta el detalle seguro como si fuera un token).</summary>
    public const int LongitudDeLaHuellaCorta = 16;

    private readonly byte[] _bytes;

    private HuellaDeArchivo(byte[] bytes) => _bytes = bytes;

    /// <summary>Los 64 caracteres hexadecimales en minúsculas.</summary>
    public string Hex => Convert.ToHexStringLower(_bytes);

    public string Corta => Hex[..LongitudDeLaHuellaCorta];

    public static HuellaDeArchivo Desde(ReadOnlySpan<byte> bytes) =>
        bytes.Length == LongitudEnBytes
            ? new HuellaDeArchivo(bytes.ToArray())
            : throw new ArgumentException("La huella SHA-256 tiene 32 bytes.", nameof(bytes));

    /// <summary>Exactamente 64 caracteres hexadecimales <b>en minúsculas</b> (cualquier otra longitud o carácter, incluidas las mayúsculas, se rechaza); es la forma de <c>/portadas/{huella}</c>: una sola URL por portada, para poder purgarla del borde (R06).</summary>
    public static bool TryDesdeHexadecimal(string? texto, out HuellaDeArchivo? huella)
    {
        huella = null;
        if (texto is null || texto.Length != LongitudEnBytes * 2 || !texto.All(c => char.IsAsciiDigit(c) || char.IsAsciiHexDigitLower(c)))
        {
            return false;
        }

        huella = new HuellaDeArchivo(Convert.FromHexString(texto));
        return true;
    }

    /// <summary>Una copia de los bytes (para guardarlos en la base).</summary>
    public byte[] ComoBytes() => (byte[])_bytes.Clone();

    public bool Equals(HuellaDeArchivo? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as HuellaDeArchivo);

    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);

    public override string ToString() => Corta;
}

/// <summary>La clave de un objeto en el almacén: 128 bits aleatorios en 32 hexadecimales, nunca el nombre del archivo recibido (CAT-16).</summary>
public static class ClaveEnAlmacen
{
    public const int Longitud = 32;

    public static string Nueva() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Longitud / 2));

    public static bool EsValida(string? clave) => clave is { Length: Longitud } && clave.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
}

/// <summary>El libro de una edición digital: solo la referencia a su objeto en el almacén (el archivo nunca se guarda en la base, regla 11). Se conserva aunque la edición se dé de baja o se retire (BIB-09).</summary>
public sealed record ArchivoDeEdicion
{
    public ArchivoDeEdicion(string claveEnAlmacen, TipoDeArchivo tipoDetectado, long tamanoEnBytes, HuellaDeArchivo huella, DateTimeOffset subidoEn, Guid subidoPor)
    {
        ArgumentNullException.ThrowIfNull(huella);
        if (!ClaveEnAlmacen.EsValida(claveEnAlmacen))
        {
            throw new ArgumentException("La clave del almacén es de 32 hexadecimales aleatorios.", nameof(claveEnAlmacen));
        }

        if (!TiposDeArchivo.EsAcordeALaFinalidad(tipoDetectado, FinalidadDeArchivo.Libro))
        {
            throw new ArgumentException("El archivo de una edición digital es un EPUB o un PDF.", nameof(tipoDetectado));
        }

        if (tamanoEnBytes is < 1 or > PoliticaDeArchivos.TopeDeLibroEnBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(tamanoEnBytes), "El libro pesa entre 1 byte y 50 MiB.");
        }

        ClaveDelAlmacen = claveEnAlmacen;
        TipoDetectado = tipoDetectado;
        TamanoEnBytes = tamanoEnBytes;
        Huella = huella;
        SubidoEn = subidoEn;
        SubidoPor = subidoPor;
    }

    public string ClaveDelAlmacen { get; }

    public TipoDeArchivo TipoDetectado { get; }

    public long TamanoEnBytes { get; }

    public HuellaDeArchivo Huella { get; }

    public DateTimeOffset SubidoEn { get; }

    public Guid SubidoPor { get; }

    /// <summary>El formato digital al que corresponde el archivo.</summary>
    public FormatoDigital Formato => TipoDetectado == TipoDeArchivo.Epub ? FormatoDigital.Epub : FormatoDigital.Pdf;

    /// <summary>La clave del almacén y quien subió el archivo nunca se imprimen (un registro de diagnóstico no debe volcarlos, SEG-10).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        constructor.Append("TipoDetectado = ").Append(TipoDetectado).Append(", TamanoEnBytes = ").Append(TamanoEnBytes);
        return true;
    }
}

/// <summary>La portada de una obra: su objeto en el almacén, su tipo y su huella (la ruta pública es <c>/portadas/{huella}</c>, D06).</summary>
public sealed record PortadaDeObra
{
    public PortadaDeObra(string claveEnAlmacen, TipoDeArchivo tipoDetectado, HuellaDeArchivo huella)
    {
        ArgumentNullException.ThrowIfNull(huella);
        if (!ClaveEnAlmacen.EsValida(claveEnAlmacen))
        {
            throw new ArgumentException("La clave del almacén es de 32 hexadecimales aleatorios.", nameof(claveEnAlmacen));
        }

        if (!TiposDeArchivo.EsAcordeALaFinalidad(tipoDetectado, FinalidadDeArchivo.Portada))
        {
            throw new ArgumentException("La portada es una imagen JPEG, PNG o WebP.", nameof(tipoDetectado));
        }

        ClaveDelAlmacen = claveEnAlmacen;
        TipoDetectado = tipoDetectado;
        Huella = huella;
    }

    public string ClaveDelAlmacen { get; }

    public TipoDeArchivo TipoDetectado { get; }

    public HuellaDeArchivo Huella { get; }

    /// <summary>La clave del almacén nunca se imprime (SEG-10).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        constructor.Append("TipoDetectado = ").Append(TipoDetectado);
        return true;
    }
}

public enum EstadoDeReferencia
{
    /// <summary>La fila nace antes de escribir el objeto: si el proceso cae, el trabajo de fondo parte de ella.</summary>
    Subiendo,

    Lista,

    Consumida,

    /// <summary>El objeto ya no se usa (reemplazado o sin consumir): el trabajo de fondo lo borra.</summary>
    Descarte,
}

/// <summary>Por qué una referencia no se pudo consumir. Hacia fuera todos responden lo mismo (<c>ECO-REFERENCIA-DE-ARCHIVO-INVALIDA</c>): quien llama no distingue una ajena de una vencida o usada.</summary>
public enum ResultadoDeConsumo
{
    Consumida,
    NoEsDelUsuario,
    FinalidadDistinta,
    TipoNoCoincideConElFormato,
    Vencida,
    NoDisponible,
}

/// <summary>
/// Un archivo subido que aún no pertenece a ninguna obra o edición (D05): se sube antes y se referencia después. Está ligada al usuario que la subió, a una finalidad y vence a las 24 horas; se
/// <b>consume una sola vez</b>, solo por su dueño, para su finalidad y, si es un libro, con el formato de la licencia. La clave del almacén es aleatoria.
/// </summary>
public sealed class ReferenciaDeArchivo
{
    private ReferenciaDeArchivo(
        Guid id,
        Guid usuarioId,
        FinalidadDeArchivo finalidad,
        string claveEnAlmacen,
        EstadoDeReferencia estado,
        TipoDeArchivo? tipoDetectado,
        long? tamanoEnBytes,
        HuellaDeArchivo? huella,
        DateTimeOffset creadoEn,
        DateTimeOffset venceEn,
        DateTimeOffset? consumidoEn)
    {
        Id = id;
        UsuarioId = usuarioId;
        Finalidad = finalidad;
        ClaveDelAlmacen = claveEnAlmacen;
        Estado = estado;
        TipoDetectado = tipoDetectado;
        TamanoEnBytes = tamanoEnBytes;
        Huella = huella;
        CreadoEn = creadoEn;
        VenceEn = venceEn;
        ConsumidoEn = consumidoEn;
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public FinalidadDeArchivo Finalidad { get; }

    public string ClaveDelAlmacen { get; }

    public EstadoDeReferencia Estado { get; private set; }

    public TipoDeArchivo? TipoDetectado { get; private set; }

    public long? TamanoEnBytes { get; private set; }

    public HuellaDeArchivo? Huella { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset VenceEn { get; }

    public DateTimeOffset? ConsumidoEn { get; private set; }

    /// <summary>La fila <c>subiendo</c> que se inserta antes de escribir el objeto, con una clave del almacén aleatoria nueva.</summary>
    public static ReferenciaDeArchivo Iniciar(Guid id, Guid usuarioId, FinalidadDeArchivo finalidad, DateTimeOffset ahora)
    {
        if (id == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La referencia y el usuario necesitan un identificador.");
        }

        if (!Enum.IsDefined(finalidad))
        {
            throw new ArgumentOutOfRangeException(nameof(finalidad));
        }

        return new ReferenciaDeArchivo(id, usuarioId, finalidad, ClaveEnAlmacen.Nueva(), EstadoDeReferencia.Subiendo, null, null, null, ahora, ahora + PoliticaDeArchivos.VigenciaDeReferencia, null);
    }

    /// <summary>Reconstruye una fila guardada.</summary>
    public static ReferenciaDeArchivo Rehidratar(
        Guid id,
        Guid usuarioId,
        FinalidadDeArchivo finalidad,
        string claveEnAlmacen,
        EstadoDeReferencia estado,
        TipoDeArchivo? tipoDetectado,
        long? tamanoEnBytes,
        HuellaDeArchivo? huella,
        DateTimeOffset creadoEn,
        DateTimeOffset venceEn,
        DateTimeOffset? consumidoEn)
    {
        if (!ClaveEnAlmacen.EsValida(claveEnAlmacen))
        {
            throw new ArgumentException("La clave del almacén guardada no tiene la forma esperada.", nameof(claveEnAlmacen));
        }

        // Una fila guardada es coherente con su estado: lista o consumida tiene tipo, tamaño y huella (y el tipo corresponde a la finalidad); consumida tiene su instante.
        var completa = tipoDetectado is not null && tamanoEnBytes is > 0 && huella is not null && TiposDeArchivo.EsAcordeALaFinalidad(tipoDetectado.Value, finalidad);
        if (estado is EstadoDeReferencia.Lista or EstadoDeReferencia.Consumida && !completa)
        {
            throw new ArgumentException("Una referencia lista o consumida guarda su tipo, su tamaño y su huella.", nameof(estado));
        }

        // Una fila en descarte puede venir de una consumida (conserva su instante) o de una que nunca se consumió.
        if ((estado == EstadoDeReferencia.Consumida && consumidoEn is null) || (estado is EstadoDeReferencia.Subiendo or EstadoDeReferencia.Lista && consumidoEn is not null))
        {
            throw new ArgumentException("El instante de consumo corresponde solo a una referencia consumida (o a una descartada que lo fue).", nameof(consumidoEn));
        }

        return new ReferenciaDeArchivo(id, usuarioId, finalidad, claveEnAlmacen, estado, tipoDetectado, tamanoEnBytes, huella, creadoEn, venceEn, consumidoEn);
    }

    /// <summary>El objeto ya está escrito y verificado: pasa a <c>lista</c> con su tipo, su tamaño real y su huella.</summary>
    public void MarcarLista(TipoDeArchivo tipo, long tamanoEnBytes, HuellaDeArchivo huella)
    {
        ArgumentNullException.ThrowIfNull(huella);
        if (Estado != EstadoDeReferencia.Subiendo)
        {
            throw new InvalidOperationException("Solo una referencia que se está subiendo pasa a lista.");
        }

        if (!TiposDeArchivo.EsAcordeALaFinalidad(tipo, Finalidad))
        {
            throw new ArgumentException("El tipo detectado no corresponde a la finalidad de la subida.", nameof(tipo));
        }

        if (tamanoEnBytes < 1 || PoliticaDeArchivos.SuperaElTope(Finalidad, tamanoEnBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(tamanoEnBytes), "El tamaño leído está fuera del tope de la finalidad.");
        }

        TipoDetectado = tipo;
        TamanoEnBytes = tamanoEnBytes;
        Huella = huella;
        Estado = EstadoDeReferencia.Lista;
    }

    /// <summary>
    /// La consume una sola vez (D05): debe estar <c>lista</c>, ser del mismo usuario, de la finalidad pedida, no vencida según el reloj y, si es un libro, del tipo que corresponde al formato de la licencia.
    /// Si no se cumple algo, no cambia nada. Un vencimiento a las 24 horas exactas ya es vencida.
    /// </summary>
    public ResultadoDeConsumo Consumir(Guid usuarioId, FinalidadDeArchivo finalidad, FormatoDigital? formato, DateTimeOffset ahora)
    {
        if (Estado != EstadoDeReferencia.Lista || ConsumidoEn is not null)
        {
            return ResultadoDeConsumo.NoDisponible;
        }

        if (usuarioId != UsuarioId)
        {
            return ResultadoDeConsumo.NoEsDelUsuario;
        }

        if (finalidad != Finalidad)
        {
            return ResultadoDeConsumo.FinalidadDistinta;
        }

        if (ahora >= VenceEn)
        {
            return ResultadoDeConsumo.Vencida;
        }

        if (finalidad == FinalidadDeArchivo.Libro && (formato is null || !TiposDeArchivo.EsAcordeAlFormato(TipoDetectado!.Value, formato.Value)))
        {
            return ResultadoDeConsumo.TipoNoCoincideConElFormato;
        }

        Estado = EstadoDeReferencia.Consumida;
        ConsumidoEn = ahora;
        return ResultadoDeConsumo.Consumida;
    }

    /// <summary>El objeto ya no se usa: el trabajo de fondo lo borra y luego la fila.</summary>
    public void PasarADescarte() => Estado = EstadoDeReferencia.Descarte;

    /// <summary>El libro de la edición digital que nace de esta referencia ya consumida (clave, tipo, tamaño, huella y quién y cuándo lo subió).</summary>
    public ArchivoDeEdicion ComoArchivoDeEdicion()
    {
        ExigirConsumida(FinalidadDeArchivo.Libro);
        return new ArchivoDeEdicion(ClaveDelAlmacen, TipoDetectado!.Value, TamanoEnBytes!.Value, Huella!, CreadoEn, UsuarioId);
    }

    /// <summary>La portada de la obra que nace de esta referencia ya consumida.</summary>
    public PortadaDeObra ComoPortada()
    {
        ExigirConsumida(FinalidadDeArchivo.Portada);
        return new PortadaDeObra(ClaveDelAlmacen, TipoDetectado!.Value, Huella!);
    }

    private void ExigirConsumida(FinalidadDeArchivo finalidad)
    {
        if (Estado != EstadoDeReferencia.Consumida || Finalidad != finalidad)
        {
            throw new InvalidOperationException("Solo una referencia ya consumida de la finalidad correspondiente se convierte en archivo de la edición o portada de la obra.");
        }
    }
}
