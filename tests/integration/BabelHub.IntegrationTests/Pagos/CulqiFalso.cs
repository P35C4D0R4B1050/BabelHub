using System.Net;
using System.Text;
using System.Text.Json;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// Un servidor HTTP simulado de la API v2 de Culqi (T37): un manejador de mensajes que atiende por método y ruta con respuestas en la forma de su especificación pública (<c>apiculqi.yaml</c>) y registra cada solicitud recibida. Sin red, sin llaves
/// reales y sin tarjetas. No es Culqi: solo permite comprobar cómo el adaptador arma sus solicitudes y traduce las respuestas y los errores documentados.
/// </summary>
internal sealed class CulqiFalso : HttpMessageHandler
{
    private readonly List<Regla> _reglas = [];
    private readonly Lock _candado = new();

    public List<SolicitudRecibida> Recibidas { get; } = [];

    /// <summary>Atiende las solicitudes <paramref name="metodo"/> cuya ruta con consulta contiene <paramref name="fragmento"/> (la primera regla que coincide, en el orden de registro).</summary>
    public CulqiFalso Cuando(HttpMethod metodo, string fragmento, HttpStatusCode estado, string? json = null)
    {
        _reglas.Add(new Regla(metodo, fragmento, _ => Responder(estado, json)));
        return this;
    }

    public CulqiFalso Cuando(HttpMethod metodo, string fragmento, Func<SolicitudRecibida, HttpResponseMessage> respuesta)
    {
        _reglas.Add(new Regla(metodo, fragmento, respuesta));
        return this;
    }

    public CulqiFalso Falla(HttpMethod metodo, string fragmento, Exception excepcion)
    {
        _reglas.Add(new Regla(metodo, fragmento, _ => throw excepcion));
        return this;
    }

    public IEnumerable<SolicitudRecibida> De(HttpMethod metodo) => Recibidas.Where(r => r.Metodo == metodo);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var recibida = new SolicitudRecibida(
            request.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            cuerpo);
        lock (_candado)
        {
            Recibidas.Add(recibida);
        }

        var regla = _reglas.FirstOrDefault(r => r.Metodo == request.Method && recibida.Ruta.Contains(r.Fragmento, StringComparison.Ordinal));
        return regla is null ? Responder(HttpStatusCode.NotFound, """{"object":"error","type":"resource_error","merchant_message":"sin regla en la prueba"}""") : regla.Respuesta(recibida);
    }

    public static HttpResponseMessage Responder(HttpStatusCode estado, string? json) =>
        new(estado) { Content = json is null ? new StringContent(string.Empty) : new StringContent(json, Encoding.UTF8, "application/json") };

    internal sealed record Regla(HttpMethod Metodo, string Fragmento, Func<SolicitudRecibida, HttpResponseMessage> Respuesta);

    internal sealed record SolicitudRecibida(HttpMethod Metodo, string Ruta, string? Autorizacion, IReadOnlyDictionary<string, string> Cabeceras, string? Cuerpo)
    {
        public JsonDocument? Json => Cuerpo is null ? null : JsonDocument.Parse(Cuerpo);
    }
}

/// <summary>Las respuestas de Culqi en la forma que documenta su especificación pública: cargo, error de tarjeta, lista, devolución. Los identificadores se arman por concatenación (el escáner de secretos no debe ver literales de llave).</summary>
internal static class RespuestasDeCulqi
{
    public const string IdDelCargo = "chr_" + "test_" + "HuVDSH10IoLKtYMa";
    public const string IdDeLaDevolucion = "ref_" + "test_" + "TTfLAgaA8nz8PWbO";

    /// <summary>Un cargo aprobado como lo describe el esquema <c>Charges</c> (importe en céntimos, <c>currency_code</c>, <c>metadata</c>, <c>outcome</c>).</summary>
    public static string Cargo(Guid referencia, long centimos = 14000, string moneda = "PEN", string id = IdDelCargo, long creadoEnMs = 1_700_000_000_000, bool conReferencia = true)
    {
        var metadatos = conReferencia ? "\"referencia\":\"" + referencia.ToString("D") + "\"" : string.Empty;
        return $$$"""
            {"duplicated":false,"object":"charge","id":"{{{id}}}","creation_date":{{{creadoEnMs}}},"amount":{{{centimos}}},"amount_refunded":0,"current_amount":{{{centimos}}},"installments":0,"currency_code":"{{{moneda}}}","email":"cliente@example.test","description":"Compra en BabelHub","outcome":{"type":"venta_exitosa","code":"AUT0000","merchant_message":"La operación de venta ha sido autorizada exitosamente","user_message":"Su compra ha sido exitosa."},"fraud_score":10,"metadata":{ {{{metadatos}}} }}
            """;
    }

    public static string CargoDenegado(Guid referencia, string declinacion, string id = IdDelCargo) =>
        $$$"""
        {"object":"charge","id":"{{{id}}}","creation_date":1700000000000,"amount":14000,"currency_code":"PEN","outcome":{"type":"card_error","code":"{{{declinacion}}}","decline_code":"{{{declinacion}}}"},"metadata":{"referencia":"{{{referencia:D}}}"}}
        """;

    public static string ErrorDeTarjeta(string declinacion) =>
        $$"""
        {"object":"error","type":"card_error","charge_id":"{{IdDelCargo}}","code":"DNGE0015","decline_code":"{{declinacion}}","merchant_message":"La tarjeta no tiene fondos suficientes para realizar la compra.","user_message":"Su tarjeta no tiene fondos suficientes."}
        """;

    public const string ErrorDeParametro = """{"object":"error","type":"parameter_error","code":"invalid_amount","merchant_message":"El campo 'amount' es inválido.","param":"amount"}""";

    public const string ErrorDeAutenticacion = """{"object":"error","type":"authentication_error","code":"DNGA0019","merchant_message":"Código de comercio proporcionado inválido."}""";

    public const string RevisionTresDs = """{"user_message":"El usuario necesita autenticarse","action_code":"REVIEW"}""";

    public static string Lista(params string[] objetos) => "{\"data\":[" + string.Join(",", objetos) + "],\"paging\":{\"previous\":null,\"next\":null,\"cursors\":{\"before\":null,\"after\":null}}}";

    public static string Devolucion(string id, string idDelCargo, long centimos, long creadoEnMs, Guid? marca = null, string estado = "completa")
    {
        var metadatos = marca is null ? string.Empty : "\"reembolso_id\":\"" + marca.Value.ToString("D") + "\"";
        return $$$"""
            {"object":"refund","id":"{{{id}}}","charge_id":"{{{idDelCargo}}}","creation_date":{{{creadoEnMs}}},"amount":{{{centimos}}},"reason":"Devolución solicitada por el comercio","metadata":{ {{{metadatos}}} },"status":"{{{estado}}}","last_modified":{{{creadoEnMs}}}}
            """;
    }
}
