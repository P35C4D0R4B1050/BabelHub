using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BabelHub.Ecommerce.Domain.Envios;

public enum CampoDelDestinatario
{
    Nombre,
    Direccion,
    Telefono,
}

/// <summary>
/// El contacto que se entrega al operador para generar la guía (ENV-02): solo nombre, dirección y teléfono (ENV-17; sin correo, documento ni datos de pago). Es una <b>clase</b> y no un
/// <c>record</c> para que su texto y su serialización estén siempre redactados: ningún registro, error ni auditoría los repite (RNF23). Viaja de Pedidos al adaptador y no se guarda (ENV-15).
/// </summary>
[DebuggerDisplay("[redactado]")]
[JsonConverter(typeof(ConvertidorDeDestinatarioParaGuia))]
public sealed class DestinatarioParaGuia
{
    public const int LongitudMaximaDelNombre = 120;

    public const int LongitudMaximaDeLaDireccion = 200;

    public const int LongitudMaximaDelTelefono = 20;

    private DestinatarioParaGuia(string nombre, string direccion, string telefono)
    {
        Nombre = nombre;
        Direccion = direccion;
        Telefono = telefono;
    }

    public string Nombre { get; }

    public string Direccion { get; }

    public string Telefono { get; }

    /// <summary>
    /// Valida el contacto leído de Pedidos. Si falta o no es válido un campo, indica <b>cuál</b> (en el orden nombre, dirección, teléfono) y nunca repite su valor (ENV-10). Un valor más largo que el
    /// máximo se trata como no válido para ese campo.
    /// </summary>
    public static ResultadoDeValidacionDelDestinatario Validar(string? nombre, string? direccion, string? telefono)
    {
        var n = nombre?.Trim();
        var d = direccion?.Trim();
        var t = telefono?.Trim();
        if (string.IsNullOrEmpty(n) || n.Length > LongitudMaximaDelNombre || TieneControl(n))
        {
            return ResultadoDeValidacionDelDestinatario.Falta(CampoDelDestinatario.Nombre);
        }

        if (string.IsNullOrEmpty(d) || d.Length > LongitudMaximaDeLaDireccion || TieneControl(d))
        {
            return ResultadoDeValidacionDelDestinatario.Falta(CampoDelDestinatario.Direccion);
        }

        if (string.IsNullOrEmpty(t) || t.Length > LongitudMaximaDelTelefono || !t.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')') || !t.Any(char.IsAsciiDigit))
        {
            return ResultadoDeValidacionDelDestinatario.Falta(CampoDelDestinatario.Telefono);
        }

        return ResultadoDeValidacionDelDestinatario.Completo(new DestinatarioParaGuia(n, d, t));
    }

    private static bool TieneControl(string texto) => texto.Any(char.IsControl);

    public override string ToString() => "[redactado]";
}

/// <summary>El resultado de validar el contacto: el destinatario o el primer campo que falta. Su texto no repite ningún dato.</summary>
public sealed class ResultadoDeValidacionDelDestinatario
{
    private ResultadoDeValidacionDelDestinatario(DestinatarioParaGuia? destinatario, CampoDelDestinatario? campoFaltante)
    {
        Destinatario = destinatario;
        CampoFaltante = campoFaltante;
    }

    [MemberNotNullWhen(true, nameof(Destinatario))]
    public bool EsValido => Destinatario is not null;

    public DestinatarioParaGuia? Destinatario { get; }

    public CampoDelDestinatario? CampoFaltante { get; }

    public static ResultadoDeValidacionDelDestinatario Completo(DestinatarioParaGuia destinatario) => new(destinatario, null);

    public static ResultadoDeValidacionDelDestinatario Falta(CampoDelDestinatario campo) => new(null, campo);

    public override string ToString() => EsValido ? "completo" : $"falta:{CampoFaltante}";
}

/// <summary>Escribe el destinatario siempre redactado; no se lee de un JSON.</summary>
public sealed class ConvertidorDeDestinatarioParaGuia : JsonConverter<DestinatarioParaGuia>
{
    public override DestinatarioParaGuia? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("El destinatario no se lee de un JSON.");

    public override void Write(Utf8JsonWriter writer, DestinatarioParaGuia value, JsonSerializerOptions options) => writer.WriteStringValue("[redactado]");
}

public enum DecisionDeCancelacion
{
    Cancelar,
    YaCancelado,
    Mantener,
}

/// <summary>
/// Qué envíos se cancelan cuando Pedidos anula el pedido (ENV-13, ENV-19): el pendiente de guía y el de guía generada <b>sin el marcador de despacho iniciado</b>; se mantienen el que tiene el
/// marcador (aunque su estado público siga en guía generada), el que está en reparto y el entregado. Un cancelado previo es éxito idempotente.
/// </summary>
public static class PoliticaDeCancelacion
{
    public static DecisionDeCancelacion Evaluar(EstadoDeEnvio estado, bool despachoIniciado) => estado switch
    {
        EstadoDeEnvio.Cancelado => DecisionDeCancelacion.YaCancelado,
        EstadoDeEnvio.PendienteDeGuia => DecisionDeCancelacion.Cancelar,
        EstadoDeEnvio.GuiaGenerada when !despachoIniciado => DecisionDeCancelacion.Cancelar,
        _ => DecisionDeCancelacion.Mantener,
    };
}
