using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// Un Culqi simulado <b>con estado</b> sobre HTTP (T37): recuerda los cargos y las devoluciones que crea, como la API v2 (importe en céntimos, <c>metadata</c>, lista por rango de fechas, devolución con tope por cargo, <c>PATCH</c> de metadatos), y el
/// sufijo del <c>source_id</c> elige el escenario como las tarjetas de prueba de Culqi. Solo sirve para ejercitar el adaptador y el flujo de Pagos sin Culqi real: no demuestra nada sobre la API verdadera (esa es la reserva declarada del spike T38).
/// </summary>
internal sealed class CulqiSimulado(IReloj reloj) : HttpMessageHandler
{
    public const string Aprobado = "tkn_" + "test_" + "aprobado";
    public const string FondosInsuficientes = "tkn_" + "test_" + "fondos";
    public const string Revision3Ds = "tkn_" + "test_" + "revision3ds";

    /// <summary>El cargo se crea y la respuesta no llega (tiempo agotado): el sistema no sabe que se cobró hasta consultar.</summary>
    public const string RespuestaPerdida = "tkn_" + "test_" + "perdida";

    /// <summary>La red cae antes de llegar a Culqi: no se crea nada.</summary>
    public const string RedCaida = "tkn_" + "test_" + "redcaida";

    /// <summary>Culqi responde 500 sin crear nada.</summary>
    public const string ErrorDelServidor = "tkn_" + "test_" + "error500";

    /// <summary>Aprueba por un sol más que el pedido.</summary>
    public const string OtroImporte = "tkn_" + "test_" + "otroimporte";

    private readonly Lock _candado = new();
    private readonly List<Cargo> _cargos = [];
    private readonly List<Devolucion> _devoluciones = [];
    private int _secuencia;

    /// <summary>Las devoluciones que crean y no responden (el proceso no se entera).</summary>
    public bool PerderLaRespuestaDeLasDevoluciones { get; set; }

    /// <summary>Culqi rechaza toda devolución (422), como podría hacerlo con un pago de Yape.</summary>
    public bool RechazarDevoluciones { get; set; }

    /// <summary>Las llamadas de cada clase que llegaron a Culqi.</summary>
    public int Llamadas(HttpMethod metodo, string rutaIniciaCon)
    {
        lock (_candado)
        {
            return _registro.Count(r => r.Metodo == metodo && r.Ruta.StartsWith(rutaIniciaCon, StringComparison.Ordinal));
        }
    }

    public int CargosCreados
    {
        get
        {
            lock (_candado)
            {
                return _cargos.Count;
            }
        }
    }

    public int DevolucionesCreadas
    {
        get
        {
            lock (_candado)
            {
                return _devoluciones.Count;
            }
        }
    }

    private readonly List<(HttpMethod Metodo, string Ruta)> _registro = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var ruta = request.RequestUri!.AbsolutePath.Replace("/v2/", string.Empty, StringComparison.Ordinal);
        var consulta = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        lock (_candado)
        {
            _registro.Add((request.Method, ruta));
            if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: { Length: > 10 } })
            {
                return Json(HttpStatusCode.Unauthorized, """{"object":"error","type":"authentication_error","code":"DNGA0019"}""");
            }

            if (request.Method == HttpMethod.Post && ruta == "charges")
            {
                return CrearCargo(JsonDocument.Parse(cuerpo!).RootElement);
            }

            if (request.Method == HttpMethod.Get && ruta == "charges")
            {
                return ListarCargos(consulta);
            }

            if (request.Method == HttpMethod.Get && ruta.StartsWith("charges/", StringComparison.Ordinal))
            {
                var cargo = _cargos.FirstOrDefault(c => c.Id == ruta["charges/".Length..]);
                return cargo is null ? Json(HttpStatusCode.NotFound, """{"object":"error","type":"resource_error"}""") : Json(HttpStatusCode.OK, cargo.Json());
            }

            if (request.Method == HttpMethod.Post && ruta == "refunds")
            {
                return CrearDevolucion(JsonDocument.Parse(cuerpo!).RootElement);
            }

            if (request.Method == HttpMethod.Patch && ruta.StartsWith("refunds/", StringComparison.Ordinal))
            {
                var devolucion = _devoluciones.FirstOrDefault(d => d.Id == ruta["refunds/".Length..]);
                if (devolucion is null)
                {
                    return Json(HttpStatusCode.NotFound, """{"object":"error","type":"resource_error"}""");
                }

                foreach (var par in JsonDocument.Parse(cuerpo!).RootElement.GetProperty("metadata").EnumerateObject())
                {
                    devolucion.Metadatos[par.Name] = par.Value.GetString()!;
                }

                return Json(HttpStatusCode.OK, devolucion.Json());
            }

            if (request.Method == HttpMethod.Get && ruta == "refunds")
            {
                var desde = long.Parse(consulta["creation_date_from"] ?? "0", CultureInfo.InvariantCulture) * 1000;
                var lista = _devoluciones.Where(d => d.CreadaEnMs >= desde).Select(d => d.Json());
                return Json(HttpStatusCode.OK, "{\"data\":[" + string.Join(",", lista) + "]}");
            }
        }

        return Json(HttpStatusCode.NotFound, """{"object":"error","type":"resource_error"}""");
    }

    private HttpResponseMessage CrearCargo(JsonElement pedido)
    {
        var token = pedido.GetProperty("source_id").GetString()!;
        var centimos = pedido.GetProperty("amount").GetInt64();
        if (token == RedCaida)
        {
            throw new HttpRequestException("La red cayó.");
        }

        if (token == ErrorDelServidor)
        {
            return Json(HttpStatusCode.InternalServerError, """{"object":"error","type":"api_error"}""");
        }

        if (token == FondosInsuficientes)
        {
            return Json(HttpStatusCode.PaymentRequired, """{"object":"error","type":"card_error","code":"DNGE0015","decline_code":"insufficient_funds","user_message":"Su tarjeta no tiene fondos suficientes."}""");
        }

        if (token == Revision3Ds)
        {
            return Json(HttpStatusCode.OK, """{"user_message":"El usuario necesita autenticarse","action_code":"REVIEW"}""");
        }

        if (pedido.GetProperty("currency_code").GetString() != "PEN" || !pedido.GetProperty("capture").GetBoolean())
        {
            return Json(HttpStatusCode.UnprocessableEntity, """{"object":"error","type":"parameter_error"}""");
        }

        var cargo = new Cargo($"chr_test_Sim{(++_secuencia).ToString("D13", CultureInfo.InvariantCulture)}", token == OtroImporte ? centimos + 100 : centimos, pedido.GetProperty("metadata").GetProperty("referencia").GetString()!, reloj.Ahora.ToUnixTimeMilliseconds());
        _cargos.Add(cargo);
        if (token == RespuestaPerdida)
        {
            throw new TaskCanceledException("Tiempo agotado.");
        }

        return Json(HttpStatusCode.Created, cargo.Json());
    }

    private HttpResponseMessage ListarCargos(System.Collections.Specialized.NameValueCollection consulta)
    {
        var desde = long.Parse(consulta["creation_date_from"] ?? "0", CultureInfo.InvariantCulture) * 1000;
        var hasta = long.Parse(consulta["creation_date_to"] ?? long.MaxValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) * 1000;
        var lista = _cargos.Where(c => c.CreadoEnMs >= desde && c.CreadoEnMs <= hasta).Reverse().Select(c => c.Json());
        return Json(HttpStatusCode.OK, "{\"data\":[" + string.Join(",", lista) + "]}");
    }

    private HttpResponseMessage CrearDevolucion(JsonElement pedido)
    {
        var cargo = _cargos.FirstOrDefault(c => c.Id == pedido.GetProperty("charge_id").GetString());
        var centimos = pedido.GetProperty("amount").GetInt64();
        if (cargo is null || RechazarDevoluciones || centimos <= 0 || _devoluciones.Where(d => d.IdDelCargo == cargo.Id).Sum(d => d.Centimos) + centimos > cargo.Centimos)
        {
            return Json(HttpStatusCode.UnprocessableEntity, """{"object":"error","type":"parameter_error"}""");
        }

        var devolucion = new Devolucion($"ref_test_Sim{(++_secuencia).ToString("D13", CultureInfo.InvariantCulture)}", cargo.Id, centimos, reloj.Ahora.ToUnixTimeMilliseconds());
        _devoluciones.Add(devolucion);
        if (PerderLaRespuestaDeLasDevoluciones)
        {
            throw new TaskCanceledException("Tiempo agotado.");
        }

        return Json(HttpStatusCode.Created, devolucion.Json());
    }

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) => new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private sealed record Cargo(string Id, long Centimos, string Referencia, long CreadoEnMs)
    {
        public string Json() =>
            $"{{\"object\":\"charge\",\"id\":\"{Id}\",\"creation_date\":{CreadoEnMs},\"amount\":{Centimos},\"currency_code\":\"PEN\",\"outcome\":{{\"type\":\"venta_exitosa\"}},\"metadata\":{{\"referencia\":\"{Referencia}\"}}}}";
    }

    private sealed class Devolucion(string id, string idDelCargo, long centimos, long creadaEnMs)
    {
        public string Id { get; } = id;

        public string IdDelCargo { get; } = idDelCargo;

        public long Centimos { get; } = centimos;

        public long CreadaEnMs { get; } = creadaEnMs;

        public Dictionary<string, string> Metadatos { get; } = [];

        public string Json() =>
            $"{{\"object\":\"refund\",\"id\":\"{Id}\",\"charge_id\":\"{IdDelCargo}\",\"creation_date\":{CreadaEnMs},\"amount\":{Centimos},\"status\":\"completa\",\"metadata\":{{{string.Join(",", Metadatos.Select(m => $"\"{m.Key}\":\"{m.Value}\""))}}}}}";
    }
}
