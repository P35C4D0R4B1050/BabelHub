using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>Cómo recibe la persona su compra (CAR-07, CAR-08): <c>recojo</c> en una sucursal, <c>domicilio</c> con dirección y contacto, o <c>digital</c> (un carrito solo digital no tiene entrega).</summary>
public enum TipoDeEntrega
{
    Recojo,
    Domicilio,
    Digital,
}

/// <summary>
/// La forma común de un dato personal de la entrega: el texto sin los espacios de los extremos, sin caracteres de control ni saltos de línea (terminan en la guía del operador, CAR-30) y con su largo máximo. Quita solo el espacio
/// común: un salto de línea al final es un carácter de control y se rechaza, no se recorta.
/// </summary>
internal static class TextoDeEntrega
{
    public static bool TryNormalizar(string? texto, int maximo, [NotNullWhen(true)] out string? normalizado)
    {
        normalizado = null;
        if (texto is null)
        {
            return false;
        }

        var recortado = texto.Trim(' ');
        if (recortado.Length == 0 || recortado.Length > maximo)
        {
            return false;
        }

        foreach (var c in recortado)
        {
            if (EsCaracterNoPermitido(c))
            {
                return false;
            }
        }

        normalizado = recortado;
        return true;
    }

    /// <summary>
    /// Los caracteres de control, los separadores de línea y de párrafo (U+2028, U+2029), los de formato (cero ancho, controles bidireccionales, BOM: sirven para suplantar visualmente un texto en la guía del operador) y todo espacio que no
    /// sea el U+0020 (el espacio duro, por ejemplo).
    /// </summary>
    private static bool EsCaracterNoPermitido(char c) =>
        char.IsControl(c) || (int)c is 0x2028 or 0x2029 || (c != ' ' && char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.SpaceSeparator);
}

/// <summary>
/// Escribe <c>[redactado]</c> en lugar del dato personal: el nombre, el teléfono, la dirección y el contacto de la entrega no salen en una serialización (registro, auditoría, mensaje de error). No se puede leer de vuelta.
/// </summary>
public sealed class ConversorRedactado<T> : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Un dato personal de la entrega no se deserializa.");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(DatosRedactados.Texto);
    }
}

internal static class DatosRedactados
{
    public const string Texto = "[redactado]";
}

/// <summary>El nombre de la persona que recibe (CAR-30): hasta 80 caracteres, sin caracteres de control. Dato personal: <c>ToString</c> y la serialización lo redactan.</summary>
[JsonConverter(typeof(ConversorRedactado<NombreDeContacto>))]
public sealed class NombreDeContacto : IEquatable<NombreDeContacto>
{
    public const int LongitudMaxima = 80;

    private NombreDeContacto(string valor) => Valor = valor;

    public string Valor { get; }

    public static bool TryCrear(string? texto, [NotNullWhen(true)] out NombreDeContacto? nombre)
    {
        nombre = TextoDeEntrega.TryNormalizar(texto, LongitudMaxima, out var normalizado) ? new NombreDeContacto(normalizado) : null;
        return nombre is not null;
    }

    public static NombreDeContacto Crear(string? texto) =>
        TryCrear(texto, out var nombre) ? nombre : throw new ArgumentException("El nombre de contacto no es válido.", nameof(texto));

    public bool Equals(NombreDeContacto? other) => other is not null && string.Equals(Valor, other.Valor, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as NombreDeContacto);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);

    public override string ToString() => DatosRedactados.Texto;
}

/// <summary>
/// El teléfono de contacto de la entrega (CAR-30, D06): un móvil peruano de nueve dígitos que empieza en 9; se aceptan espacios, guiones y paréntesis y el prefijo <c>+51</c>, <c>0051</c> o <c>51</c> (este solo con los 11 dígitos), y se
/// guarda <b>normalizado</b> a los nueve dígitos. Dato personal: <c>ToString</c> y la serialización lo redactan.
/// </summary>
[JsonConverter(typeof(ConversorRedactado<TelefonoDeContacto>))]
public sealed class TelefonoDeContacto : IEquatable<TelefonoDeContacto>
{
    public const int Digitos = 9;

    private TelefonoDeContacto(string valor) => Valor = valor;

    /// <summary>Los nueve dígitos, sin prefijo ni separadores.</summary>
    public string Valor { get; }

    public static bool TryCrear(string? texto, [NotNullWhen(true)] out TelefonoDeContacto? telefono)
    {
        telefono = null;
        if (texto is null)
        {
            return false;
        }

        if (texto.Length > 40)
        {
            return false;
        }

        var sinSeparadores = string.Concat(texto.Trim(' ').Where(c => c is not (' ' or '-' or '(' or ')')));
        var numero = QuitarPrefijo(sinSeparadores);
        if (numero.Length != Digitos || numero[0] != '9' || !numero.All(c => c is >= '0' and <= '9'))
        {
            return false;
        }

        telefono = new TelefonoDeContacto(numero);
        return true;
    }

    public static TelefonoDeContacto Crear(string? texto) =>
        TryCrear(texto, out var telefono) ? telefono : throw new ArgumentException("El teléfono de contacto no es un móvil peruano válido.", nameof(texto));

    private static string QuitarPrefijo(string numero)
    {
        if (numero.StartsWith("+51", StringComparison.Ordinal))
        {
            return numero[3..];
        }

        if (numero.StartsWith("0051", StringComparison.Ordinal))
        {
            return numero[4..];
        }

        return numero.StartsWith("51", StringComparison.Ordinal) && numero.Length == Digitos + 2 ? numero[2..] : numero;
    }

    public bool Equals(TelefonoDeContacto? other) => other is not null && string.Equals(Valor, other.Valor, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as TelefonoDeContacto);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);

    public override string ToString() => DatosRedactados.Texto;
}

/// <summary>
/// La dirección de una entrega a domicilio con su región (CAR-08, CAR-30): el texto hasta 200 caracteres y la región (hasta 60) sin caracteres de control. Que la región sea una de las que lista el Inventario lo comprueba la Aplicación
/// (R05). Dato personal: <c>ToString</c> y la serialización la redactan.
/// </summary>
[JsonConverter(typeof(ConversorRedactado<DireccionDeEntrega>))]
public sealed class DireccionDeEntrega : IEquatable<DireccionDeEntrega>
{
    public const int LongitudMaximaDelTexto = 200;

    public const int LongitudMaximaDeLaRegion = 60;

    private DireccionDeEntrega(string texto, string region)
    {
        Texto = texto;
        Region = region;
    }

    public string Texto { get; }

    public string Region { get; }

    public static bool TryCrear(string? texto, string? region, [NotNullWhen(true)] out DireccionDeEntrega? direccion)
    {
        direccion = TextoDeEntrega.TryNormalizar(texto, LongitudMaximaDelTexto, out var t) && TextoDeEntrega.TryNormalizar(region, LongitudMaximaDeLaRegion, out var r)
            ? new DireccionDeEntrega(t, r)
            : null;
        return direccion is not null;
    }

    public static DireccionDeEntrega Crear(string? texto, string? region) =>
        TryCrear(texto, region, out var direccion) ? direccion : throw new ArgumentException("La dirección de entrega no es válida.", nameof(texto));

    public bool Equals(DireccionDeEntrega? other) =>
        other is not null && string.Equals(Texto, other.Texto, StringComparison.Ordinal) && string.Equals(Region, other.Region, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DireccionDeEntrega);

    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Texto), StringComparer.Ordinal.GetHashCode(Region));

    public override string ToString() => DatosRedactados.Texto;
}

/// <summary>
/// El contacto de una entrega a domicilio (CAR-30, D08, D09): nombre, teléfono y dirección con su región. Es una <b>clase</b> y no un <c>record</c> para que su <c>ToString</c> no vuelque los campos: <c>ToString</c> y la serialización
/// devuelven <c>[redactado]</c> y no se interpola en excepciones. Vive en el checkout mientras haya una compra a domicilio en curso y nunca se guarda en una caché.
/// </summary>
[JsonConverter(typeof(ConversorRedactado<ContactoDeEntrega>))]
public sealed class ContactoDeEntrega : IEquatable<ContactoDeEntrega>
{
    private ContactoDeEntrega(NombreDeContacto nombre, TelefonoDeContacto telefono, DireccionDeEntrega direccion)
    {
        Nombre = nombre;
        Telefono = telefono;
        Direccion = direccion;
    }

    public NombreDeContacto Nombre { get; }

    public TelefonoDeContacto Telefono { get; }

    public DireccionDeEntrega Direccion { get; }

    public static ContactoDeEntrega Crear(NombreDeContacto nombre, TelefonoDeContacto telefono, DireccionDeEntrega direccion)
    {
        ArgumentNullException.ThrowIfNull(nombre);
        ArgumentNullException.ThrowIfNull(telefono);
        ArgumentNullException.ThrowIfNull(direccion);
        return new ContactoDeEntrega(nombre, telefono, direccion);
    }

    public bool Equals(ContactoDeEntrega? other) => other is not null && Nombre.Equals(other.Nombre) && Telefono.Equals(other.Telefono) && Direccion.Equals(other.Direccion);

    public override bool Equals(object? obj) => Equals(obj as ContactoDeEntrega);

    public override int GetHashCode() => HashCode.Combine(Nombre, Telefono, Direccion);

    public override string ToString() => DatosRedactados.Texto;
}
