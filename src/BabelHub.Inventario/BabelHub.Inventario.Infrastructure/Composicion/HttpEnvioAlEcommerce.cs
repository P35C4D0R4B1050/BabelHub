using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos.Ecommerce;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// <see cref="IEnvioAlEcommerce"/> por REST (T19, ECO-0 D04): las dos llamadas del sentido Inventario → E-commerce con la credencial de alcance <c>solicitar-aviso</c> (la única de ese sentido), por HTTPS, con el plazo de 10 segundos y
/// la resiliencia del cliente de servicio. <b>Nunca lanza</b>: clasifica la respuesta como <b>no confiable</b> (solo el código de estado, nunca el cuerpo) en un <see cref="ResultadoDeEnvio"/>: <c>200</c>, <c>202</c> y <c>204</c> entregan;
/// <c>401</c> y <c>403</c> son una credencial rechazada; <c>400</c>, <c>404</c>, <c>409</c> y <c>422</c> (y cualquier otro 4xx salvo <c>408</c> y <c>429</c>) son permanentes; el resto (red caída, plazo, cortacircuitos abierto, 5xx,
/// <c>408</c>, <c>429</c> y un éxito con un código que el contrato no define) se reintenta. Vive en <c>Composicion</c> porque la Infraestructura de un módulo no puede depender de la de <c>Plataforma</c> (regla 13), donde está el manejador
/// que adjunta la credencial. El aviso lleva la plantilla de traslado y solo campos tipados: nunca el motivo ni la guía (D07).
/// </summary>
internal sealed class HttpEnvioAlEcommerce(HttpClient cliente) : IEnvioAlEcommerce
{
    /// <summary>El nombre del cliente HTTP hacia el E-commerce.</summary>
    public const string NombreDelCliente = "ecommerce";

    /// <summary>La plantilla de aviso de traslado (la fija el catálogo de <c>transversal-notificaciones</c> T02; este plan usa esta).</summary>
    public const string PlantillaDeTraslado = "traslado-cambio-de-estado";

    private static readonly string RutaDeAvisos = Ruta("ecommerce.avisos.solicitar-del-inventario");

    private static readonly string RutaDeEstados = Ruta("ecommerce.traslados.informar-estado");

    public Task<ResultadoDeEnvio> EnviarAviso(AvisoDeTrasladoParaEnviar aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        var cuerpo = new SolicitudDeAvisoDeInventarioDto(
            aviso.EventoId,
            PlantillaDeTraslado,
            aviso.CodigoDeSucursal,
            new Dictionary<string, string>
            {
                ["numeroDeTraslado"] = aviso.NumeroDeTraslado.ToString(CultureInfo.InvariantCulture),
                ["estado"] = aviso.Estado,
                ["rolDeLaSucursal"] = aviso.RolDeLaSucursal,
                ["cantidad"] = aviso.Cantidad.ToString(CultureInfo.InvariantCulture),
            });
        return Enviar(RutaDeAvisos, cuerpo, aviso.EventoId, cancelacion);
    }

    public Task<ResultadoDeEnvio> InformarEstadoDelPedido(EstadoDeTrasladoDePedidoParaEnviar estado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(estado);
        var cuerpo = new EstadoDeTrasladoDePedidoDto(estado.EventoId, estado.TrasladoId, estado.PedidoId, estado.EdicionId, estado.Cantidad, estado.Estado, estado.FechaEstimadaDeLlegada, estado.OcurridoEn);
        return Enviar(RutaDeEstados, cuerpo, estado.EventoId, cancelacion);
    }

    private async Task<ResultadoDeEnvio> Enviar<T>(string ruta, T cuerpo, string claveDeIdempotencia, CancellationToken cancelacion)
    {
        try
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
            solicitud.Headers.TryAddWithoutValidation("Idempotency-Key", claveDeIdempotencia);

            // Un POST con clave de idempotencia se puede reintentar: la recepción repetida del mismo evento no cambia nada.
            solicitud.ConAlcance(AlcanceDeServicio.SolicitarAviso).ComoIdempotente();

            using var respuesta = await cliente.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, cancelacion).ConfigureAwait(false);
            return Clasificar(respuesta.StatusCode);
        }
        catch (Exception ex) when (!cancelacion.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or TimeoutRejectedException or BrokenCircuitException or OperationCanceledException or InvalidOperationException)
        {
            // Sin red, sin respuesta a tiempo o con el cortacircuitos abierto: se reintenta. Una excepción nunca interrumpe a quien llama.
            return ResultadoDeEnvio.Reintentable;
        }
    }

    /// <summary>La clasificación de la respuesta por su código de estado: el cuerpo de un servicio externo nunca se lee.</summary>
    internal static ResultadoDeEnvio Clasificar(HttpStatusCode codigo) =>
        (int)codigo switch
        {
            200 or 202 or 204 => ResultadoDeEnvio.Entregado,
            401 or 403 => ResultadoDeEnvio.CredencialRechazada,
            408 or 429 => ResultadoDeEnvio.Reintentable,
            >= 400 and < 500 => ResultadoDeEnvio.Permanente,
            >= 300 and < 400 => ResultadoDeEnvio.Permanente,
            _ => ResultadoDeEnvio.Reintentable,
        };

    private static string Ruta(string nombre) => OperacionesDelEcommerce.Todas.Single(o => o.Nombre == nombre).Ruta;
}
