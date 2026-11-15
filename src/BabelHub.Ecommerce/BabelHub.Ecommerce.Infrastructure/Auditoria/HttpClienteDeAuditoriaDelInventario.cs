using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos.Auditoria;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// Las opciones de solicitud que <c>Plataforma</c> lee en su manejador de salida (<c>CredencialesDeSalidaHandler</c>): el módulo <c>Auditoria</c> no puede depender de la Infraestructura de otro módulo
/// (regla 13), así que declara las mismas llaves por su <b>nombre</b> y tipo, que son parte del contrato del manejador; una prueba de integración comprueba que coinciden con las de <c>Plataforma</c>.
/// </summary>
internal static class OpcionesDeSolicitudDeAuditoria
{
    public static readonly HttpRequestOptionsKey<AlcanceDeServicio> Alcance = new("babelhub.alcance");

    public static readonly HttpRequestOptionsKey<string> TokenDeUsuario = new("babelhub.token-de-usuario");
}

/// <summary>
/// <see cref="IClienteDeAuditoriaDelInventario"/> sobre HTTP (T26; AUD-17, AUD-18, AUD-38): lee la auditoría, las alertas y el historial del Inventario por el contrato congelado de ECO-0 con <b>el token verificable del
/// usuario</b> (de <see cref="ITokenDelUsuarioDeLaSesion"/>, nunca una cabecera de actor) y la credencial de servicio del alcance de cada ruta (que adjunta el manejador de salida de <c>Plataforma</c>). La espera es de
/// 10 segundos y <b>una falla del Inventario nunca es una excepción</b>: un corte, un 5xx, el vencimiento del plazo, una respuesta demasiado grande, que no es JSON o con campos que el contrato rechaza son
/// <see cref="EstadoDeRespuestaDelInventario.NoDisponible"/>; un 400, 401, 403, 404 o 409 es <see cref="EstadoDeRespuestaDelInventario.Rechazada"/> (no es una falla del servicio). Nunca se registra el cuerpo, el token ni el
/// mensaje del error remoto, y el valor de cada filtro viaja escapado.
/// </summary>
public sealed class HttpClienteDeAuditoriaDelInventario(IHttpClientFactory fabrica, ITokenDelUsuarioDeLaSesion tokens, TimeProvider? reloj = null) : IClienteDeAuditoriaDelInventario
{
    public const string NombreDelCliente = "inventario";

    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);

    public const int MaximoDeBytesDeLaRespuesta = 1_048_576;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.Strict, AllowTrailingCommas = false };

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<RespuestaDelInventario<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var filtro = consulta.Filtro ?? new FiltroDeAuditoria();
        var consultaHttp = new List<string>();
        Agregar(consultaHttp, "accion", filtro.Accion);
        Agregar(consultaHttp, "usuarioId", filtro.UsuarioId?.ToString("D"));
        Agregar(consultaHttp, "origen", filtro.Origen);
        Agregar(consultaHttp, "desde", filtro.Desde?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Agregar(consultaHttp, "hasta", filtro.Hasta?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Agregar(consultaHttp, "texto", filtro.Texto);
        Agregar(consultaHttp, "cursor", consulta.Cursor);
        return await Pedir(HttpMethod.Get, "/api/inventario/v1/auditoria" + Unir(consultaHttp), AlcanceDeServicio.ConsultaDeAuditoria, conToken: true, cuerpo: null, LeerPagina, cancelacion).ConfigureAwait(false);
    }

    public async Task<RespuestaDelInventario<IReadOnlyList<AlertaConsultada>>> LeerAlertasNuevas(DateTimeOffset desde, CancellationToken cancelacion = default)
    {
        var consultaHttp = new List<string>();
        Agregar(consultaHttp, "desde", desde.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return await Pedir(HttpMethod.Get, "/api/inventario/v1/alertas" + Unir(consultaHttp), AlcanceDeServicio.LecturaDeAlertas, conToken: false, cuerpo: null, LeerAlertas, cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Reenvía la marca con el token del administrador (contrato de ECO-0: <c>204</c> sin cuerpo) y, como el contrato de la Aplicación devuelve la alerta ya actualizada, la vuelve a leer con la lectura de
    /// alertas de los últimos 90 días; si esa lectura no responde, la marca ya está aplicada pero no se puede mostrar el estado: se informa «no disponible» y repetir la marca es inocuo (no retrocede lo revisado).
    /// </summary>
    public async Task<RespuestaDelInventario<AlertaConsultada>> MarcarAlertaRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default)
    {
        if (!IdentificadorDeAlerta.EsValido(alertaId, "inv:"))
        {
            throw new ArgumentException("El identificador de la alerta no es de una alerta del Inventario.", nameof(alertaId));
        }

        var cuerpo = JsonSerializer.Serialize(new { contadorVisto }, Json);
        var marcada = await Pedir(
            HttpMethod.Post,
            "/api/inventario/v1/alertas/" + Uri.EscapeDataString(alertaId) + "/revisada",
            AlcanceDeServicio.MarcarAlerta,
            conToken: true,
            cuerpo,
            _ => true,
            cancelacion).ConfigureAwait(false);
        if (!marcada.Disponible)
        {
            return marcada.Estado == EstadoDeRespuestaDelInventario.Rechazada ? RespuestaDelInventario<AlertaConsultada>.Rechazada : RespuestaDelInventario<AlertaConsultada>.NoDisponible;
        }

        var lectura = await LeerAlertasNuevas(_reloj.GetUtcNow().AddDays(-90), cancelacion).ConfigureAwait(false);
        var alerta = lectura.Disponible ? lectura.Valor.FirstOrDefault(a => a.Id == alertaId) : null;
        return alerta is null ? RespuestaDelInventario<AlertaConsultada>.NoDisponible : RespuestaDelInventario<AlertaConsultada>.ConValor(alerta);
    }

    public async Task<RespuestaDelInventario<PaginaDeRegistros>> ConsultarHistorial(Guid sucursalPedida, string? cursor, CancellationToken cancelacion = default)
    {
        var consultaHttp = new List<string>();
        Agregar(consultaHttp, "sucursal", sucursalPedida.ToString("D"));
        Agregar(consultaHttp, "cursor", cursor);
        return await Pedir(HttpMethod.Get, "/api/inventario/v1/historial" + Unir(consultaHttp), AlcanceDeServicio.Historial, conToken: true, cuerpo: null, LeerPagina, cancelacion).ConfigureAwait(false);
    }

    private async Task<RespuestaDelInventario<T>> Pedir<T>(
        HttpMethod metodo,
        string ruta,
        AlcanceDeServicio alcance,
        bool conToken,
        string? cuerpo,
        Func<string, T> leer,
        CancellationToken cancelacion)
        where T : notnull
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta);
        solicitud.Options.Set(OpcionesDeSolicitudDeAuditoria.Alcance, alcance);
        if (conToken)
        {
            var token = await tokens.Actual(cancelacion).ConfigureAwait(false);
            if (token is null)
            {
                // Sin sesión no hay quien actúe: el Inventario lo rechazaría, y no se llama sin el token (nunca una cabecera de actor).
                return RespuestaDelInventario<T>.Rechazada;
            }

            solicitud.Options.Set(OpcionesDeSolicitudDeAuditoria.TokenDeUsuario, token.Revelar());
        }

        if (cuerpo is not null)
        {
            solicitud.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");
        }

        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(Espera);
        try
        {
            var http = fabrica.CreateClient(NombreDelCliente);
            using var respuesta = await http.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, plazo.Token).ConfigureAwait(false);

            if (respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                return RespuestaDelInventario<T>.Rechazada;
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                return RespuestaDelInventario<T>.NoDisponible;
            }

            if (respuesta.StatusCode == HttpStatusCode.NoContent)
            {
                return typeof(T) == typeof(bool) ? (RespuestaDelInventario<T>)(object)RespuestaDelInventario<bool>.ConValor(true) : RespuestaDelInventario<T>.NoDisponible;
            }

            var texto = await LeerAcotado(respuesta, plazo.Token).ConfigureAwait(false);
            return texto is null ? RespuestaDelInventario<T>.NoDisponible : RespuestaDelInventario<T>.ConValor(leer(texto));
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Un corte, un plazo vencido, un cortacircuitos abierto o una respuesta que no cumple el contrato: «no disponible», sin el texto del error remoto (AUD-18).
            return RespuestaDelInventario<T>.NoDisponible;
        }
    }

    /// <summary>El cuerpo como texto, con un tope de tamaño; <see langword="null"/> si lo supera.</summary>
    private static async Task<string?> LeerAcotado(HttpResponseMessage respuesta, CancellationToken cancelacion)
    {
        if (respuesta.Content.Headers.ContentLength is > MaximoDeBytesDeLaRespuesta)
        {
            return null;
        }

        await using var flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false);
        using var memoria = new MemoryStream();
        var bloque = new byte[8192];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bloque, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > MaximoDeBytesDeLaRespuesta)
            {
                return null;
            }

            memoria.Write(bloque, 0, leidos);
        }

        return Encoding.UTF8.GetString(memoria.GetBuffer(), 0, (int)memoria.Length);
    }

    private static void Agregar(List<string> partes, string nombre, string? valor)
    {
        if (!string.IsNullOrEmpty(valor))
        {
            partes.Add(nombre + "=" + Uri.EscapeDataString(valor));
        }
    }

    private static string Unir(List<string> partes) => partes.Count == 0 ? string.Empty : "?" + string.Join('&', partes);

    // ---------- Cuerpos del contrato (BabelHub.Shared, T21) ----------

    private static PaginaDeRegistros LeerPagina(string texto)
    {
        var dto = JsonSerializer.Deserialize<PaginaDeRegistrosDto>(texto, Json) ?? throw new JsonException("La respuesta no es una página.");
        var registros = (dto.Registros ?? throw new JsonException("Faltan los registros.")).Select(ARegistro).ToList();
        return new PaginaDeRegistros(registros, dto.SiguienteCursor) { CursorDeLaPagina = dto.CursorDeLaPagina };
    }

    private static IReadOnlyList<AlertaConsultada> LeerAlertas(string texto)
    {
        var dtos = JsonSerializer.Deserialize<List<AlertaDto>>(texto, Json) ?? throw new JsonException("La respuesta no es una lista de alertas.");
        return [.. dtos.Select(AAlerta)];
    }

    /// <summary>Un valor del contrato: solo el nombre de un miembro definido (ni un número ni un texto con otra forma).</summary>
    private static T Analizar<T>(string texto)
        where T : struct, Enum =>
        texto.Length is > 0 and <= 32 && !char.IsDigit(texto[0]) && Enum.TryParse<T>(texto, ignoreCase: true, out var valor) && Enum.IsDefined(valor)
            ? valor
            : throw new JsonException("El valor no es un miembro del contrato.");

    private static RegistroAuditado ARegistro(RegistroDto d) =>
        new(
            d.Id,
            Analizar<ServicioAuditado>(d.Servicio ?? string.Empty),
            Analizar<TipoDeRegistroAuditado>(d.Tipo ?? string.Empty),
            Analizar<TipoDeActor>(d.ActorTipo ?? string.Empty),
            d.ActorId,
            d.Rol,
            d.InstanteUtc,
            d.Accion ?? string.Empty,
            Analizar<ResultadoAuditado>(d.Resultado ?? string.Empty))
        {
            ObjetoTipo = d.ObjetoTipo,
            ObjetoId = d.ObjetoId,
            SucursalOrigen = d.SucursalOrigen,
            SucursalDestino = d.SucursalDestino,
            Motivo = d.Motivo,
            Origen = d.Origen,
            Ruta = d.Ruta,
            Detalle = d.Detalle,
            ClavesPersonales = d.ClavesPersonales ?? [],
            Truncado = d.Truncado,
            Correlacion = d.Correlacion,
            AnonimizadoEn = d.AnonimizadoEn,
        };

    private static AlertaConsultada AAlerta(AlertaDto d)
    {
        var servicio = Analizar<ServicioAuditado>(d.Servicio ?? string.Empty);
        return new AlertaConsultada(
            d.Id ?? string.Empty,
            servicio,
            new CodigoDeAlerta(d.Motivo ?? string.Empty),
            Analizar<ClaseDeAlertaConsultada>(d.Clase ?? string.Empty),
            d.PrimeraOcurrencia,
            d.UltimaOcurrencia,
            d.Ocurrencias,
            d.RevisadasHasta)
        {
            RevisadaPor = d.RevisadaPor,
            RevisadaEn = d.RevisadaEn,
        };
    }
}
