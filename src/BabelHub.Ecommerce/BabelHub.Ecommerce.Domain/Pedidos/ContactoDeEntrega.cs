using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>
/// El contacto de entrega de un pedido a domicilio (PED-32, PED-40): nombre, teléfono y dirección. Es una <b>clase</b> y no un <c>record</c> para que su texto y su serialización estén siempre redactados: ningún registro, error ni auditoría los repite (RNF23).
/// Los límites coinciden con los que Envíos exige a su destinatario, de modo que un contacto guardado siempre sirve para la guía.
/// </summary>
[DebuggerDisplay("[redactado]")]
[JsonConverter(typeof(ConvertidorDeContactoDeEntrega))]
public sealed class ContactoDeEntrega
{
    public const string Redactado = "[redactado]";
    public const int LongitudMaximaDelNombre = 120;
    public const int LongitudMaximaDeLaDireccion = 200;
    public const int LongitudMaximaDelTelefono = 20;

    private ContactoDeEntrega(string nombre, string telefono, string direccion)
    {
        Nombre = nombre;
        Telefono = telefono;
        Direccion = direccion;
    }

    public string Nombre { get; }

    public string Telefono { get; }

    public string Direccion { get; }

    /// <summary>Crea el contacto si los tres datos están y son válidos (sin caracteres de control ni exceder el largo); si no, <see langword="null"/>: el pedido a domicilio no se crea (PED-36).</summary>
    public static ContactoDeEntrega? Crear(string? nombre, string? telefono, string? direccion)
    {
        // Un salto de línea o una tabulación es un carácter de control: se rechaza antes de recortar (solo se recortan los espacios de los extremos).
        var n = nombre?.Trim(' ');
        var t = telefono?.Trim(' ');
        var d = direccion?.Trim(' ');
        if (!Valido(n, LongitudMaximaDelNombre) || !Valido(t, LongitudMaximaDelTelefono) || !Valido(d, LongitudMaximaDeLaDireccion))
        {
            return null;
        }

        return new ContactoDeEntrega(n!, t!, d!);
    }

    private static bool Valido(string? valor, int maximo) => !string.IsNullOrEmpty(valor) && valor.Length <= maximo && !valor.Any(char.IsControl);

    public override string ToString() => Redactado;
}

internal sealed class ConvertidorDeContactoDeEntrega : JsonConverter<ContactoDeEntrega>
{
    public override ContactoDeEntrega Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("El contacto de la entrega no se lee de un texto.");

    public override void Write(Utf8JsonWriter escritor, ContactoDeEntrega valor, JsonSerializerOptions opciones) => escritor.WriteStringValue(ContactoDeEntrega.Redactado);
}
