using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;
using Microsoft.Extensions.Logging;

namespace BabelHub.Ecommerce.Infrastructure.Notificaciones;

/// <summary>
/// La configuración del envío por n8n (T14): la dirección del webhook y la clave del <b>sentido saliente</b> (la que firma la llamada del servicio; n8n la verifica y es distinta de la que firma sus estados de entrega, NOT-12). El
/// destino lo fija el operador, nunca un dato: <c>https</c>, salvo <c>http</c> hacia la propia máquina o un nombre interno de una sola etiqueta (n8n corre en la misma VM, AD16). La clave es de al menos 32 bytes y no se imprime.
/// </summary>
public sealed class OpcionesDeEnvioPorN8n
{
    public const int LongitudMinimaDeLaClave = 32;

    public OpcionesDeEnvioPorN8n(Uri webhook, byte[] claveSaliente)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        ArgumentNullException.ThrowIfNull(claveSaliente);
        if (!webhook.IsAbsoluteUri || webhook.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(webhook.UserInfo) || !string.IsNullOrEmpty(webhook.Query) || !string.IsNullOrEmpty(webhook.Fragment))
        {
            throw new ArgumentException("El webhook de n8n es una dirección http o https absoluta, sin credenciales, consulta ni fragmento.", nameof(webhook));
        }

        if (webhook.Scheme == "http" && !EsInterna(webhook))
        {
            throw new ArgumentException("El webhook de n8n solo puede ser http hacia la propia máquina o un nombre interno; en otro caso es https.", nameof(webhook));
        }

        if (claveSaliente.Length < LongitudMinimaDeLaClave)
        {
            throw new ArgumentException($"La clave saliente debe tener al menos {LongitudMinimaDeLaClave} bytes.", nameof(claveSaliente));
        }

        Webhook = webhook;
        ClaveSaliente = claveSaliente;
    }

    public Uri Webhook { get; }

    internal byte[] ClaveSaliente { get; }

    public override string ToString() => "[envío por n8n]";

    private static bool EsInterna(Uri direccion) => direccion.IsLoopback || (direccion.HostNameType == UriHostNameType.Dns && !direccion.Host.Contains('.', StringComparison.Ordinal));
}

/// <summary>
/// El envío por el webhook de n8n (T14; AD16, NOT-04, NOT-12): una llamada <c>POST</c> con el identificador del aviso como clave de idempotencia, la plantilla con sus campos cerrados y el número, <b>firmada</b> con HMAC-SHA256 sobre
/// «{marca}.{método}.{ruta}.{cuerpo}» con la clave del sentido saliente (cabeceras <c>X-BabelHub-Marca</c> y <c>X-BabelHub-Firma</c>); n8n verifica la firma y la marca antes de actuar. Espera 10 segundos, no sigue redirecciones y <b>no
/// lanza</b> por una falla del servicio: la traduce a <see cref="ResultadoDelEnvio.Fallido"/>. El 200 significa «aceptado», no entregado, y su cuerpo (<c>{"estado":"aceptado","duplicado":false}</c>) no se registra ni se confía más allá de eso.
/// El número viaja solo aquí y nunca se imprime.
/// </summary>
public sealed class N8nEnvioDeWhatsApp(HttpClient cliente, OpcionesDeEnvioPorN8n opciones, IReloj reloj) : IEnvioDeWhatsApp
{
    public const string NombreDelCliente = "n8n-envio-de-avisos";

    public const string CabeceraDeLaMarca = "X-BabelHub-Marca";

    public const string CabeceraDeLaFirma = "X-BabelHub-Firma";

    private const int TopeDeLaRespuesta = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoDelEnvio> Enviar(SolicitudDeEnvio solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        try
        {
            var cuerpo = JsonSerializer.Serialize(
                new
                {
                    avisoId = solicitud.AvisoId.ToString("D"),
                    plantilla = solicitud.Plantilla,
                    version = solicitud.VersionDeLaPlantilla,
                    campos = solicitud.Campos,
                    numero = solicitud.Numero.E164,
                },
                Json);
            var marca = reloj.Ahora.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var peticion = new HttpRequestMessage(HttpMethod.Post, opciones.Webhook)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            };
            peticion.Headers.TryAddWithoutValidation(CabeceraDeLaMarca, marca);
            peticion.Headers.TryAddWithoutValidation(CabeceraDeLaFirma, Firmar(opciones.ClaveSaliente, marca, HttpMethod.Post.Method, opciones.Webhook.AbsolutePath, cuerpo));

            using var respuesta = await cliente.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, cancelacion).ConfigureAwait(false);
            if (respuesta.StatusCode != HttpStatusCode.OK)
            {
                return ResultadoDelEnvio.Fallido;
            }

            return await Interpretar(respuesta, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La espera agotada, la red caída o un cuerpo ilegible: un envío fallido que consume un intento; nunca un dato de la solicitud en el mensaje.
            return ResultadoDelEnvio.Fallido;
        }
    }

    /// <summary>La firma de la llamada: HMAC-SHA256 en hexadecimal sobre «{marca}.{método}.{ruta}.{cuerpo}». La usa también la prueba del flujo de n8n, que la recalcula del otro lado.</summary>
    public static string Firmar(byte[] clave, string marca, string metodo, string ruta, string cuerpo) =>
        Convert.ToHexString(HMACSHA256.HashData(clave, Encoding.UTF8.GetBytes($"{marca}.{metodo}.{ruta}.{cuerpo}"))).ToLowerInvariant();

    private static async Task<ResultadoDelEnvio> Interpretar(HttpResponseMessage respuesta, CancellationToken cancelacion)
    {
        if (respuesta.Content.Headers.ContentLength is > TopeDeLaRespuesta)
        {
            return ResultadoDelEnvio.Fallido;
        }

        await using var flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false);
        using var memoria = new MemoryStream();
        var bufer = new byte[1024];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bufer, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > TopeDeLaRespuesta)
            {
                return ResultadoDelEnvio.Fallido;
            }

            memoria.Write(bufer, 0, leidos);
        }

        using var documento = JsonDocument.Parse(memoria.ToArray());
        var raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("estado", out var estado) || estado.ValueKind != JsonValueKind.String || estado.GetString() != "aceptado")
        {
            return ResultadoDelEnvio.Fallido;
        }

        return raiz.TryGetProperty("duplicado", out var duplicado) && duplicado.ValueKind == JsonValueKind.True ? ResultadoDelEnvio.Duplicado : ResultadoDelEnvio.Aceptado;
    }
}

/// <summary>
/// El envío simulado del entorno de desarrollo y de pruebas (T15; NOT-11): <b>no envía a ningún número real</b>; deja constancia (solo el identificador y la plantilla, nunca el número ni los campos) y completa el ciclo como lo haría n8n,
/// registrando el estado de entrega «entregado» por el mismo contrato que usa su devolución firmada. En producción el arranque lo rechaza.
/// </summary>
public sealed class SimuladoEnvioDeWhatsApp(IRegistroDeEstadoDeEntrega estados, IReloj reloj, ILogger<SimuladoEnvioDeWhatsApp> registro) : IEnvioDeWhatsApp
{
    public async Task<ResultadoDelEnvio> Enviar(SolicitudDeEnvio solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        registro.LogInformation("Aviso {AvisoId} de la plantilla {Plantilla} simulado: no se envió a ningún número.", solicitud.AvisoId, solicitud.Plantilla);
        await estados.Registrar(solicitud.AvisoId, EstadoDeEntrega.Entregado, reloj.Ahora, cancelacion).ConfigureAwait(false);
        return ResultadoDelEnvio.Aceptado;
    }
}

/// <summary>
/// El envío nulo del ensayo y de la restauración (T15; NOT-11): nunca llama a nadie y responde que falló. El trabajo de fondo ni siquiera corre en esos modos (PLA-19); este adaptador existe para que, si algo lo invocara, no
/// salga ningún mensaje a un número real (una base restaurada trae personas reales).
/// </summary>
public sealed class NuloEnvioDeWhatsApp : IEnvioDeWhatsApp
{
    public Task<ResultadoDelEnvio> Enviar(SolicitudDeEnvio solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return Task.FromResult(ResultadoDelEnvio.Fallido);
    }
}
