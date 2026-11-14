using System.Net;
using System.Net.Sockets;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.Primitives;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// Cómo se determina el origen de una solicitud que llega por el borde (SEG-17): los proxies cuya cabecera se acepta (direcciones o
/// redes en notación CIDR; en producción la red dedicada Nginx → Api, nunca la de n8n ni Evolution) y si se admiten direcciones de
/// redes internas como dirección de un cliente.
/// </summary>
public sealed class OpcionesDeOrigenDelBorde
{
    public const string CabeceraDeOrigen = "X-Origen-Cliente";

    private readonly IReadOnlyList<IPNetwork> _proxies;

    public OpcionesDeOrigenDelBorde(IEnumerable<string> proxiesConocidos, bool aceptarDireccionesInternas = false)
    {
        ArgumentNullException.ThrowIfNull(proxiesConocidos);
        _proxies = proxiesConocidos.Select(Analizar).ToList();
        if (_proxies.Count == 0)
        {
            throw new ArgumentException("Se necesita al menos un proxy conocido.", nameof(proxiesConocidos));
        }

        AceptarDireccionesInternas = aceptarDireccionesInternas;
    }

    /// <summary>Solo para desarrollo local, donde todo ocurre dentro de la red de Docker.</summary>
    public bool AceptarDireccionesInternas { get; }

    public bool EsProxyConocido(IPAddress? direccion) =>
        direccion is not null && _proxies.Any(red => red.Contains(Normalizar(direccion)));

    internal static IPAddress Normalizar(IPAddress direccion) => direccion.IsIPv4MappedToIPv6 ? direccion.MapToIPv4() : direccion;

    private static IPNetwork Analizar(string texto)
    {
        var limpio = texto.Trim();
        if (IPNetwork.TryParse(limpio, out var red))
        {
            return red;
        }

        if (IPAddress.TryParse(limpio, out var direccion))
        {
            var normalizada = Normalizar(direccion);
            return new IPNetwork(normalizada, normalizada.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        }

        throw new ArgumentException($"«{limpio}» no es una dirección ni una red válida.", nameof(texto));
    }
}

/// <summary>
/// Determina el origen de cada solicitud como la dirección del cliente final que entrega el borde de confianza (SEG-17, SEG-23):
/// solo se lee <c>X-Origen-Cliente</c>, y solo si la conexión viene de un proxy conocido; se analiza con <c>IPAddress.TryParse</c>; una
/// cabecera ausente, duplicada, con varias direcciones, inválida o con la dirección de un componente interno, y una conexión que no
/// viene de un proxy conocido, dejan el origen como «desconocido» y registran un evento de seguridad. Se descartan además las
/// cabeceras de reenvío del cliente: nada más en la Api puede leerlas. Una llamada entre servicios (con credencial de servicio) no
/// trae origen del borde: lo resuelve la verificación de la credencial (T25) y aquí no se registra nada.
/// </summary>
public sealed class OrigenDelBordeMiddleware(
    RequestDelegate siguiente,
    OpcionesDeOrigenDelBorde opciones)
{
    public const string CabeceraDeCredencialDeServicio = "X-Credencial-De-Servicio";

    public const string MotivoSinProxyConocido = "conexion-sin-proxy-conocido";
    public const string MotivoSinCabecera = "cabecera-ausente";
    public const string MotivoCabeceraDuplicada = "cabecera-duplicada";
    public const string MotivoDireccionInvalida = "direccion-invalida";
    public const string MotivoDireccionInterna = "direccion-interna";

    private static readonly string[] CabecerasDeReenvioQueSeDescartan =
    [
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "Forwarded",
        "X-Real-IP",
        "X-Client-IP",
        "True-Client-IP",
        "CF-Connecting-IP",
        "X-Origen-Reenviado",
        "X-Credencial-De-Origen",
    ];

    public async Task InvokeAsync(
        HttpContext contexto,
        ICalculadorDeHuella huellas,
        IPublicadorDeEventosDeSeguridad eventos,
        IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var esLlamadaDeServicio = contexto.Request.Headers.ContainsKey(CabeceraDeCredencialDeServicio);
        var (origen, motivo) = Determinar(contexto, huellas);
        foreach (var cabecera in CabecerasDeReenvioQueSeDescartan)
        {
            contexto.Request.Headers.Remove(cabecera);
        }

        contexto.Request.Headers.Remove(OpcionesDeOrigenDelBorde.CabeceraDeOrigen);
        OrigenDeLaSolicitudHttp.Establecer(contexto, origen);

        if (motivo is not null && !esLlamadaDeServicio)
        {
            await eventos.Publicar(
                new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenIndeterminable, reloj.Ahora, motivo: motivo),
                contexto.RequestAborted).ConfigureAwait(false);
        }

        await siguiente(contexto).ConfigureAwait(false);
    }

    private (OrigenDeSolicitud Origen, string? Motivo) Determinar(HttpContext contexto, ICalculadorDeHuella huellas)
    {
        if (!opciones.EsProxyConocido(contexto.Connection.RemoteIpAddress))
        {
            return (OrigenDeSolicitud.Desconocido, MotivoSinProxyConocido);
        }

        var valores = contexto.Request.Headers[OpcionesDeOrigenDelBorde.CabeceraDeOrigen];
        if (StringValues.IsNullOrEmpty(valores))
        {
            return (OrigenDeSolicitud.Desconocido, MotivoSinCabecera);
        }

        if (valores.Count != 1)
        {
            return (OrigenDeSolicitud.Desconocido, MotivoCabeceraDuplicada);
        }

        var texto = valores[0]!.Trim();
        if (!IPAddress.TryParse(texto, out var direccion) || texto.Contains(',', StringComparison.Ordinal) || texto.Any(char.IsWhiteSpace))
        {
            return (OrigenDeSolicitud.Desconocido, MotivoDireccionInvalida);
        }

        direccion = OpcionesDeOrigenDelBorde.Normalizar(direccion);
        if (!opciones.AceptarDireccionesInternas && EsDireccionInterna(direccion))
        {
            return (OrigenDeSolicitud.Desconocido, MotivoDireccionInterna);
        }

        var canonica = direccion.ToString();
        return (new OrigenDeSolicitud(huellas.DeOrigen(canonica), canonica), null);
    }

    /// <summary>La dirección de un componente interno, no la de un cliente: loopback, redes privadas, locales de enlace, no especificada o de multidifusión.</summary>
    internal static bool EsDireccionInterna(IPAddress direccion)
    {
        if (IPAddress.IsLoopback(direccion) || direccion.Equals(IPAddress.Any) || direccion.Equals(IPAddress.IPv6Any) || direccion.Equals(IPAddress.None))
        {
            return true;
        }

        if (direccion.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = direccion.GetAddressBytes();
            return direccion.IsIPv6LinkLocal || direccion.IsIPv6SiteLocal || direccion.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC;
        }

        var b = direccion.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || b[0] >= 224;
    }
}

public static class OrigenDelBordeExtensiones
{
    public static IServiceCollection AgregarOrigenDelBorde(this IServiceCollection servicios, OpcionesDeOrigenDelBorde opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);
        servicios.AddSingleton(opciones);
        return servicios;
    }

    /// <summary>Va después de la correlación y antes del tope previo.</summary>
    public static IApplicationBuilder UsarOrigenDelBorde(this IApplicationBuilder app) => app.UseMiddleware<OrigenDelBordeMiddleware>();
}
