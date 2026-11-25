using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using Microsoft.Extensions.Logging;
using MedioDelDominio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Lo que el adaptador de Culqi necesita para hablar con la API (T37): la llave privada (<c>sk_test_…</c> o <c>sk_live_…</c>, solo de la configuración del entorno, PAG-12) con su texto siempre redactado. La dirección base
/// es la misma para integración y producción (<see cref="UrlDeLaApi"/>): Culqi decide el ambiente por la llave.
/// </summary>
internal sealed record OpcionesDeCulqi(ValorSecreto LlavePrivada)
{
    /// <summary>La API v2 de Culqi (<c>servers</c> de su especificación OpenAPI y <c>url_api_base</c> de su SDK oficial): una sola para integración y producción.</summary>
    public const string UrlDeLaApi = "https://api.culqi.com/v2/";

    /// <summary>Nombre del cliente HTTP con la resiliencia (plazo, circuito y reintento solo de lecturas).</summary>
    public const string NombreDelCliente = "culqi";

    /// <summary>Cuánto se espera a Culqi en cada llamada: el plazo de SEG-14 y PAG-07.</summary>
    public static readonly TimeSpan PlazoPorLlamada = TimeSpan.FromSeconds(10);

    public override string ToString() => "OpcionesDeCulqi { LlavePrivada = [redactado] }";
}

/// <summary>
/// La pasarela real, Culqi (T37; D05, D13): una capa anticorrupción sobre la API v2 que traduce a los tipos propios y nunca deja pasar su texto. <b>No supone idempotencia de Culqi</b> (la especificación pública no documenta una clave de idempotencia:
/// DD-96): la corrección descansa en el intento persistido antes de llamar y en la referencia opaca del intento en <c>metadata.referencia</c> del cargo, por la que <see cref="ConsultarEstado"/> lo reconoce. <see cref="Cobrar"/> y
/// <see cref="Reembolsar"/> nunca se reintentan a ciegas (el cliente HTTP solo reintenta lecturas) y ante una respuesta que no se entiende, un tiempo agotado, un circuito abierto o un error del servicio responden «sin respuesta»; un rechazo definitivo
/// (<c>402 card_error</c>, <c>400</c>, <c>404</c> o <c>422</c>) es rechazo. Un importe, una moneda o una referencia que no coinciden se devuelven tal cual para que <see cref="ResultadoDeCobro.Validar"/> los trate como dudosos. Solo viajan el importe en
/// céntimos, <c>PEN</c>, el correo del titular (que Culqi exige), una descripción fija y la referencia opaca: ni nombre, ni teléfono, ni dirección (PAG-30; no se envía <c>antifraud_details</c>). El token del medio solo se revela para el cuerpo de la
/// solicitud; ni el token, ni el correo, ni la llave, ni el cuerpo de una respuesta llegan a un registro (solo el estado HTTP y el tipo y código de error).
/// </summary>
internal sealed partial class CulqiPasarelaDePago(HttpClient http, OpcionesDeCulqi opciones, IReloj reloj, ILogger<CulqiPasarelaDePago>? registro = null) : IPasarelaDePago
{
    private const int TopeDeLaRespuesta = 256 * 1024;
    private const int TamanoDePagina = 100;
    private const string RazonDelReembolso = "solicitud_comprador";
    private const string DescripcionDelCargo = "Compra en BabelHub";
    private const string MetadataReferencia = "referencia";
    private const string MetadataReembolso = "reembolso_id";

    private readonly string _entorno = opciones.LlavePrivada.Revelar().StartsWith("sk_live_", StringComparison.Ordinal) ? "live" : "test";

    public async Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        // Culqi exige el correo del cargo: sin él no hay llamada y no hay cobro (rechazo, no «sin respuesta»: nada se envió).
        if (string.IsNullOrWhiteSpace(solicitud.CorreoDelTitular) || solicitud.CorreoDelTitular.Length > 50)
        {
            return ResultadoDeCobro.Rechazado(MotivoDeRechazo.Otro);
        }

        var cuerpo = new Dictionary<string, object?>
        {
            ["amount"] = solicitud.Importe.EnCentimos,
            ["currency_code"] = Importe.Moneda,
            ["email"] = solicitud.CorreoDelTitular,
            ["source_id"] = solicitud.Token.Revelar(),
            ["capture"] = true,
            ["description"] = DescripcionDelCargo,
            ["metadata"] = new Dictionary<string, string> { [MetadataReferencia] = solicitud.ReferenciaOpaca.ToString("D") },
        };

        try
        {
            using var respuesta = await Enviar(HttpMethod.Post, "charges", cuerpo, cancelacion).ConfigureAwait(false);
            return TraducirElCobro(respuesta, solicitud.Medio);
        }
        catch (Exception ex) when (EsDeLaLlamada(ex))
        {
            Anotar("cobrar", ex);
            return ResultadoDeCobro.SinRespuesta();
        }
    }

    public async Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default, DateTimeOffset? iniciadoEn = null)
    {
        // Culqi no filtra los cargos por metadatos: se lista una ventana corta alrededor del inicio del intento (el cargo se crea segundos después de registrarlo) y se reconoce por la referencia opaca (DD-95).
        var desde = (iniciadoEn ?? reloj.Ahora.AddHours(-2)).AddMinutes(-1);
        var hasta = (iniciadoEn ?? reloj.Ahora).AddMinutes(10);
        var ruta = $"charges?creation_date_from={desde.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}&creation_date_to={hasta.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}&limit={TamanoDePagina.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            using var respuesta = await Enviar(HttpMethod.Get, ruta, null, cancelacion).ConfigureAwait(false);
            if (respuesta.Estado != HttpStatusCode.OK || respuesta.Json is not { RootElement: { ValueKind: JsonValueKind.Object } raiz } || !raiz.TryGetProperty("data", out var datos) || datos.ValueKind != JsonValueKind.Array)
            {
                return ResultadoDeCobro.SinRespuesta();
            }

            var referencia = referenciaOpaca.ToString("D");
            foreach (var cargo in datos.EnumerateArray())
            {
                if (cargo.ValueKind != JsonValueKind.Object || !string.Equals(Metadato(cargo, MetadataReferencia), referencia, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var exitoso = EsExitoso(cargo);
                if (exitoso && LeerElCargo(cargo, referenciaOpaca) is { } aprobado)
                {
                    return aprobado;
                }

                if (!exitoso && DeclinacionDelCargo(cargo) is { } declinacion)
                {
                    return ResultadoDeCobro.Rechazado(MotivoDeLaDeclinacion(declinacion, medio: null));
                }
            }

            // No se rechaza jamás por la ausencia del cargo: una página llena sin el cargo o un cargo aún no listado son «sin respuesta».
            return ResultadoDeCobro.SinRespuesta();
        }
        catch (Exception ex) when (EsDeLaLlamada(ex))
        {
            Anotar("consultar", ex);
            return ResultadoDeCobro.SinRespuesta();
        }
    }

    public async Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default)
    {
        // La referencia entra en la ruta: solo la forma exacta de un id de cargo de Culqi.
        if (!EsIdDeCargo(referenciaDelCargo) || monto.Valor <= 0m)
        {
            return ResultadoDeReembolsoEnPasarela.Rechazado;
        }

        try
        {
            var existentes = await ListarReembolsosDelCargo(referenciaDelCargo, cancelacion).ConfigureAwait(false);
            if (existentes is null)
            {
                return ResultadoDeReembolsoEnPasarela.SinRespuesta;
            }

            if (existentes.FirstOrDefault(e => e.Marca == reembolsoId && e.Monto == monto) is { } hecho)
            {
                return ResultadoDeReembolsoEnPasarela.Aplicado(hecho.Referencia);
            }

            // Un reembolso de la misma cuantía y sin la marca del sistema puede ser el de una llamada cuyo proceso cayó o se cortó antes de marcarlo (o uno hecho a mano en el CulqiPanel): no se pide otro, **sin vencimiento**; queda pendiente y visible
            // para que la administración lo verifique en el panel (DD-97).
            if (existentes.Any(e => e.Marca is null && e.Monto == monto))
            {
                return ResultadoDeReembolsoEnPasarela.SinRespuesta;
            }

            var cuerpo = new Dictionary<string, object?>
            {
                ["amount"] = monto.EnCentimos,
                ["charge_id"] = referenciaDelCargo,
                ["reason"] = RazonDelReembolso,
            };
            using var respuesta = await Enviar(HttpMethod.Post, "refunds", cuerpo, cancelacion).ConfigureAwait(false);
            var resultado = TraducirElReembolso(respuesta, out var idDelReembolso);
            if (resultado.Tipo == TipoDeResultadoDeReembolsoEnPasarela.Aplicado)
            {
                await MarcarElReembolso(idDelReembolso!, reembolsoId, cancelacion).ConfigureAwait(false);
            }

            return resultado;
        }
        catch (Exception ex) when (EsDeLaLlamada(ex))
        {
            Anotar("reembolsar", ex);
            return ResultadoDeReembolsoEnPasarela.SinRespuesta;
        }
    }

    public async Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default)
    {
        if (!EsIdDeCargo(referenciaDelCargo))
        {
            return null;
        }

        try
        {
            var reembolsos = await ListarReembolsosDelCargo(referenciaDelCargo, cancelacion).ConfigureAwait(false);
            return reembolsos?.Select(r => new ReembolsoEnPasarela(r.Marca, r.Referencia, r.Monto)).ToArray();
        }
        catch (Exception ex) when (EsDeLaLlamada(ex))
        {
            Anotar("consultar_reembolsos", ex);
            return null;
        }
    }

    /// <summary>
    /// Lee <c>{"object":"event","id":"evt_…","type":"charge.…","data":"{…el cargo…}"}</c> (el <c>data</c> del aviso es el JSON del recurso como texto, o como objeto): el identificador del evento y la referencia opaca de <c>metadata.referencia</c>.
    /// Sin red y sin confiar en nada: lo que no se entiende es <c>null</c>. <b>La entrada de avisos sigue deshabilitada</b> mientras Culqi no documente un esquema de firma o autenticación verificable (DD-95); esto solo deja lista la lectura.
    /// </summary>
    public AvisoDeLaPasarela? InterpretarAviso(ReadOnlyMemory<byte> cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 4 and <= 100 } idDeEntrada || !idDeEntrada.StartsWith("evt_", StringComparison.Ordinal)
                || !raiz.TryGetProperty("type", out var tipo) || tipo.ValueKind != JsonValueKind.String || tipo.GetString() is not { } nombre || !nombre.StartsWith("charge.", StringComparison.Ordinal)
                || !raiz.TryGetProperty("data", out var datos))
            {
                return null;
            }

            JsonDocument? interno = null;
            try
            {
                var cargo = datos;
                if (datos.ValueKind == JsonValueKind.String)
                {
                    interno = JsonDocument.Parse(datos.GetString() ?? string.Empty);
                    cargo = interno.RootElement;
                }

                if (cargo.ValueKind == JsonValueKind.Object && Guid.TryParse(Metadato(cargo, MetadataReferencia), out var opaca) && opaca != Guid.Empty)
                {
                    return new AvisoDeLaPasarela(idDeEntrada, opaca);
                }
            }
            finally
            {
                interno?.Dispose();
            }
        }
        catch (JsonException)
        {
            // Un cuerpo que no es JSON no se entiende.
        }

        return null;
    }

    // ---------- Traducción de las respuestas ----------

    private ResultadoDeCobro TraducirElCobro(RespuestaDeCulqi respuesta, MedioDelDominio medio)
    {
        switch ((int)respuesta.Estado)
        {
            case 200 or 201:
                if (respuesta.Json?.RootElement is { ValueKind: JsonValueKind.Object } raiz)
                {
                    // 200 con action_code REVIEW: Culqi pide autenticar al titular (3DS); sin resolver se trata como un rechazo (R03).
                    if (raiz.TryGetProperty("action_code", out var accion) && accion.ValueKind == JsonValueKind.String)
                    {
                        return string.Equals(accion.GetString(), "REVIEW", StringComparison.OrdinalIgnoreCase)
                            ? ResultadoDeCobro.Rechazado(MotivoDeRechazo.AutenticacionRequerida)
                            : ResultadoDeCobro.SinRespuesta();
                    }

                    if (respuesta.Estado == HttpStatusCode.Created && NoEsContradictorio(raiz) && LeerElCargo(raiz, referenciaEsperada: null) is { } aprobado)
                    {
                        return aprobado;
                    }
                }

                AnotarEstado("cobrar", respuesta);
                return ResultadoDeCobro.SinRespuesta();

            case 402:
                // card_error: Culqi no pudo cobrar. Un rechazo definitivo con su motivo de la lista cerrada; el texto del tercero se descarta.
                return respuesta.Json?.RootElement is { ValueKind: JsonValueKind.Object } error
                    ? ResultadoDeCobro.Rechazado(MotivoDeLaDeclinacion(Texto(error, "decline_code") ?? Texto(error, "code"), medio))
                    : ResultadoDeCobro.SinRespuesta();

            case 400 or 404 or 422:
                // La solicitud no se aceptó (token vencido o ya usado, importe fuera de rango, formato): nada se cobró.
                return ResultadoDeCobro.Rechazado(MotivoDeRechazo.Otro);

            default:
                // 401 (llave), 429 (límite), 5xx y todo lo demás: no se sabe si se cobró. Se anota el estado (sin cuerpo) para que una llave revocada o un límite no dejen todos los cobros «pendientes» en silencio.
                AnotarEstado("cobrar", respuesta);
                return ResultadoDeCobro.SinRespuesta();
        }
    }

    /// <summary>Un cargo con importe, moneda y referencia legibles; los que no coinciden con el intento los marca <see cref="ResultadoDeCobro.Validar"/>. <c>null</c> si falta algo para reconocerlo.</summary>
    private static ResultadoDeCobro? LeerElCargo(JsonElement cargo, Guid? referenciaEsperada)
    {
        if (Texto(cargo, "id") is not { } id || !EsIdDeCargo(id)
            || !cargo.TryGetProperty("amount", out var monto) || monto.ValueKind != JsonValueKind.Number || !monto.TryGetInt64(out var centimos) || centimos < 0
            || (Texto(cargo, "currency_code") ?? Texto(cargo, "currency")) is not { Length: > 0 } moneda
            || !Guid.TryParse(Metadato(cargo, MetadataReferencia), out var opaca) || opaca == Guid.Empty)
        {
            return null;
        }

        if (referenciaEsperada is { } esperada && esperada != opaca)
        {
            return null;
        }

        return ResultadoDeCobro.Aprobado(id, Importe.DesdeCentimos(centimos), moneda, opaca);
    }

    private ResultadoDeReembolsoEnPasarela TraducirElReembolso(RespuestaDeCulqi respuesta, out string? idDelReembolso)
    {
        idDelReembolso = null;
        switch ((int)respuesta.Estado)
        {
            case 200 or 201:
                if (respuesta.Json?.RootElement is { ValueKind: JsonValueKind.Object } raiz && Texto(raiz, "id") is { } id && EsIdDeReembolso(id))
                {
                    if (string.Equals(Texto(raiz, "status"), "rechazado", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResultadoDeReembolsoEnPasarela.Rechazado;
                    }

                    idDelReembolso = id;
                    return ResultadoDeReembolsoEnPasarela.Aplicado(id);
                }

                return ResultadoDeReembolsoEnPasarela.SinRespuesta;

            case 400 or 402 or 404 or 422:
                // Un reembolso que Culqi no acepta (más que lo cobrado, cargo no reembolsable o, tal vez, un pago de Yape: DD-98): queda pendiente y visible para la administración.
                return ResultadoDeReembolsoEnPasarela.Rechazado;

            default:
                return ResultadoDeReembolsoEnPasarela.SinRespuesta;
        }
    }

    // ---------- Reembolsos del cargo ----------

    private sealed record ReembolsoLeido(Guid? Marca, string Referencia, Importe Monto, DateTimeOffset CreadoEn);

    /// <summary>
    /// Los reembolsos que Culqi tiene de un cargo (sin los rechazados): la API no filtra por cargo, así que se lista desde la creación del cargo y se separan por <c>charge_id</c>. Una página llena (100) se toma por incompleta y es <c>null</c>
    /// (no se sabe: nunca se pide otro a ciegas).
    /// </summary>
    private async Task<IReadOnlyList<ReembolsoLeido>?> ListarReembolsosDelCargo(string idDelCargo, CancellationToken cancelacion)
    {
        using var cargo = await Enviar(HttpMethod.Get, $"charges/{idDelCargo}", null, cancelacion).ConfigureAwait(false);
        if (cargo.Estado == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (cargo.Estado != HttpStatusCode.OK || cargo.Json?.RootElement is not { ValueKind: JsonValueKind.Object } objeto || !objeto.TryGetProperty("creation_date", out var creacion) || !creacion.TryGetInt64(out var creadoEnMilisegundos))
        {
            return null;
        }

        var desde = DateTimeOffset.FromUnixTimeMilliseconds(creadoEnMilisegundos).AddMinutes(-1).ToUnixTimeSeconds();
        using var lista = await Enviar(HttpMethod.Get, $"refunds?creation_date_from={desde.ToString(CultureInfo.InvariantCulture)}&limit={TamanoDePagina.ToString(CultureInfo.InvariantCulture)}", null, cancelacion).ConfigureAwait(false);
        if (lista.Estado != HttpStatusCode.OK || lista.Json?.RootElement is not { ValueKind: JsonValueKind.Object } raiz || !raiz.TryGetProperty("data", out var datos) || datos.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (datos.GetArrayLength() >= TamanoDePagina)
        {
            return null;
        }

        var resultado = new List<ReembolsoLeido>();
        foreach (var reembolso in datos.EnumerateArray())
        {
            if (reembolso.ValueKind != JsonValueKind.Object
                || !string.Equals(Texto(reembolso, "charge_id"), idDelCargo, StringComparison.Ordinal)
                || string.Equals(Texto(reembolso, "status"), "rechazado", StringComparison.OrdinalIgnoreCase)
                || Texto(reembolso, "id") is not { } id || !EsIdDeReembolso(id)
                || !reembolso.TryGetProperty("amount", out var monto) || !monto.TryGetInt64(out var centimos) || centimos < 0)
            {
                continue;
            }

            var creado = reembolso.TryGetProperty("creation_date", out var fecha) && fecha.TryGetInt64(out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;
            Guid? marca = Guid.TryParse(Metadato(reembolso, MetadataReembolso), out var guid) && guid != Guid.Empty ? guid : null;
            resultado.Add(new ReembolsoLeido(marca, id, Importe.DesdeCentimos(centimos), creado));
        }

        return resultado;
    }

    /// <summary>Etiqueta el reembolso con el identificador del sistema (<c>PATCH /refunds/{id}</c>: <c>POST</c> no admite metadatos). Un fallo aquí no deshace nada: el reembolso ya está aplicado y se reconoce por cuantía y fecha (DD-97).</summary>
    private async Task MarcarElReembolso(string idDelReembolso, Guid reembolsoId, CancellationToken cancelacion)
    {
        try
        {
            var cuerpo = new Dictionary<string, object?> { ["metadata"] = new Dictionary<string, string> { [MetadataReembolso] = reembolsoId.ToString("D") } };
            using var _ = await Enviar(HttpMethod.Patch, $"refunds/{idDelReembolso}", cuerpo, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (EsDeLaLlamada(ex))
        {
            Anotar("marcar_reembolso", ex);
        }
    }

    // ---------- La llamada HTTP ----------

    private sealed class RespuestaDeCulqi(HttpStatusCode estado, JsonDocument? json) : IDisposable
    {
        public HttpStatusCode Estado { get; } = estado;

        public JsonDocument? Json { get; } = json;

        public void Dispose() => Json?.Dispose();
    }

    private async Task<RespuestaDeCulqi> Enviar(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken cancelacion)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta);
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opciones.LlavePrivada.Revelar());
        solicitud.Headers.TryAddWithoutValidation("x-api-version", "2");
        solicitud.Headers.TryAddWithoutValidation("x-culqi-env", _entorno);
        solicitud.Headers.TryAddWithoutValidation("x-culqi-client", "babelhub");
        solicitud.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (cuerpo is not null)
        {
            solicitud.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
        }

        // El plazo de 10 s cubre la llamada completa, también la lectura del cuerpo (aunque quien llama no ponga el suyo).
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(OpcionesDeCulqi.PlazoPorLlamada);
        cancelacion = plazo.Token;
        using var respuesta = await http.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, cancelacion).ConfigureAwait(false);
        var estado = respuesta.StatusCode;
        if (respuesta.Content.Headers.ContentLength is > TopeDeLaRespuesta)
        {
            return new RespuestaDeCulqi(estado, null);
        }

        await using var flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false);
        using var memoria = new MemoryStream();
        var buffer = new byte[8192];
        int leidos;
        while ((leidos = await flujo.ReadAsync(buffer, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > TopeDeLaRespuesta)
            {
                return new RespuestaDeCulqi(estado, null);
            }

            memoria.Write(buffer, 0, leidos);
        }

        if (memoria.Length == 0)
        {
            return new RespuestaDeCulqi(estado, null);
        }

        try
        {
            memoria.Position = 0;
            return new RespuestaDeCulqi(estado, await JsonDocument.ParseAsync(memoria, cancellationToken: cancelacion).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return new RespuestaDeCulqi(estado, null);
        }
    }

    // ---------- Utilidades de lectura ----------

    private static string? Texto(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static string? Metadato(JsonElement objeto, string clave) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty("metadata", out var metadatos) ? Texto(metadatos, clave) : null;

    private static bool EsIdDeCargo(string? id) => id is not null && IdDeCargo().IsMatch(id);

    private static bool EsIdDeReembolso(string? id) => id is not null && IdDeReembolso().IsMatch(id);

    /// <summary>Marcas de éxito que Culqi usa en el cargo (su documentación las muestra de dos formas: <c>state</c> y <c>response_code</c>, y <c>outcome</c>).</summary>
    private static bool EsExitoso(JsonElement cargo)
    {
        if (cargo.TryGetProperty("paid", out var pagado) && pagado.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return string.Equals(Texto(cargo, "state"), "exitosa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Texto(cargo, "response_code"), "venta_exitosa", StringComparison.OrdinalIgnoreCase)
            || (cargo.TryGetProperty("outcome", out var resultado) && (Texto(resultado, "type") is "venta_exitosa" or "succesfull_request" or "successful_request" || Texto(resultado, "code") is "venta_exitosa" or "succesfull_charge" or "successful_charge"));
    }

    /// <summary>
    /// Un <c>201</c> (cargo creado: Culqi responde <c>402</c> a una denegación) es éxito salvo que el propio cargo traiga <b>evidencia en contra</b>: una marca de éxito reconocida lo confirma; sin ella, cualquier <c>outcome</c>, <c>state</c>, <c>response_code</c> o
    /// <c>decline_code</c> presentes lo vuelven dudoso («sin respuesta»: decide la consulta). Solo un cargo sin ninguna marca (una forma que la documentación no muestra) se acepta por su <c>201</c>.
    /// </summary>
    private static bool NoEsContradictorio(JsonElement cargo)
    {
        if (EsExitoso(cargo))
        {
            return true;
        }

        return !cargo.TryGetProperty("outcome", out _) && Texto(cargo, "state") is null && Texto(cargo, "response_code") is null && Texto(cargo, "decline_code") is null;
    }

    /// <summary>El código de denegación de un cargo, solo con <b>evidencia positiva</b> de que se denegó (<c>decline_code</c>, o <c>outcome.type</c> <c>card_error</c>); un código cualquiera (también uno de éxito) no es una denegación.</summary>
    private static string? DeclinacionDelCargo(JsonElement cargo)
    {
        if (cargo.TryGetProperty("outcome", out var resultado) && resultado.ValueKind == JsonValueKind.Object)
        {
            if (Texto(resultado, "decline_code") is { } declinacion)
            {
                return declinacion;
            }

            if (Texto(resultado, "type") == "card_error")
            {
                return Texto(resultado, "code") ?? "card_declined";
            }
        }

        return Texto(cargo, "decline_code");
    }

    /// <summary>
    /// Traduce el <c>decline_code</c> de Culqi (lista de su documentación) a la lista cerrada propia; lo que no se conoce es <see cref="MotivoDeRechazo.Otro"/>. Con Yape cualquier denegación es <see cref="MotivoDeRechazo.YapeRechazado"/>
    /// (Culqi no publica códigos propios de Yape, DD-98).
    /// </summary>
    internal static MotivoDeRechazo MotivoDeLaDeclinacion(string? codigo, MedioDelDominio? medio)
    {
        if (medio == MedioDelDominio.Yape)
        {
            return MotivoDeRechazo.YapeRechazado;
        }

        return codigo switch
        {
            "insufficient_funds" => MotivoDeRechazo.FondosInsuficientes,
            "expired_card" => MotivoDeRechazo.TarjetaVencida,
            "invalid_cvv" or "incorrect_cvv" or "too_many_attempts_cvv" => MotivoDeRechazo.CodigoDeSeguridadInvalido,
            "stolen_card" or "lost_card" or "contact_issuer" or "issuer_decline_operation" or "invalid_card" or "fraudulent" or "soft_block" or "culqi_card" or "card_declined" => MotivoDeRechazo.TarjetaRechazada,
            _ => MotivoDeRechazo.Otro,
        };
    }

    /// <summary>Las excepciones de una llamada que se reducen a «sin respuesta»: red, tiempo agotado, circuito abierto, flujo cortado y formato. Las del propio programa no se esconden.</summary>
    private static bool EsDeLaLlamada(Exception ex) => ex is HttpRequestException or OperationCanceledException or TimeoutException or IOException or JsonException or Polly.CircuitBreaker.BrokenCircuitException or Polly.CircuitBreaker.IsolatedCircuitException or Polly.Timeout.TimeoutRejectedException;

    /// <summary>Anota solo el estado HTTP y el <c>type</c> y <c>code</c> del error de Culqi (ni el cuerpo, ni mensajes con datos del cliente).</summary>
    private void AnotarEstado(string operacion, RespuestaDeCulqi respuesta) =>
        registro?.LogWarning("Culqi {Operacion} respondió {Estado} ({Tipo}/{Codigo})", operacion, (int)respuesta.Estado, respuesta.Json is { } json ? Texto(json.RootElement, "type") : null, respuesta.Json is { } json2 ? Texto(json2.RootElement, "code") : null);

    private void Anotar(string operacion, Exception ex) =>
        registro?.LogWarning("Culqi {Operacion} sin respuesta ({Tipo})", operacion, ex.GetType().Name);

    [GeneratedRegex(@"\Achr_(test|live)_[A-Za-z0-9]{8,40}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex IdDeCargo();

    [GeneratedRegex(@"\Aref_(test|live)_[A-Za-z0-9]{8,40}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex IdDeReembolso();
}
