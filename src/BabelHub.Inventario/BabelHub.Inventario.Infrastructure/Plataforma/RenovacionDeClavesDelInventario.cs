using System.Net;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// De dónde obtiene el Inventario las claves públicas y las revocaciones (identidad-seguridad T41; IDE-85): las URL internas de la <b>réplica de gestión de su propia VM</b>, configuradas por entorno; nunca se derivan
/// de una credencial ni de una respuesta. Absolutas, HTTPS (o HTTP solo hacia la propia máquina, loopback) y sin usuario ni contraseña en la dirección.
/// </summary>
public sealed class OpcionesDeClavesPublicadas
{
    public OpcionesDeClavesPublicadas(Uri jwks, Uri revocaciones)
    {
        Validar(jwks, nameof(jwks));
        Validar(revocaciones, nameof(revocaciones));
        Jwks = jwks;
        Revocaciones = revocaciones;
    }

    public Uri Jwks { get; }

    public Uri Revocaciones { get; }

    /// <summary>Tiempo máximo de cada petición; mucho menor que los 5 minutos de la caché para dejar reintentos.</summary>
    public TimeSpan Espera { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Un documento más grande que esto no es el JWKS ni la lista de revocaciones y se descarta.</summary>
    public int MaximoDeBytes { get; init; } = 64 * 1024;

    private static void Validar(Uri url, string nombre)
    {
        ArgumentNullException.ThrowIfNull(url);
        // La raíz de confianza (el JWKS) no viaja en claro por la red: HTTPS, o HTTP solo hacia la propia máquina (la réplica de gestión de la misma VM).
        var seguro = url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback));
        if (!seguro || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new ArgumentException("La dirección de las claves publicadas es HTTPS (o HTTP hacia la propia máquina) y sin usuario.", nombre);
        }
    }
}

/// <summary>
/// El cliente HTTP que trae el JWKS y las revocaciones de la réplica de gestión (T41; IDE-85): una petición GET con plazo, sin seguir redirecciones (la respuesta debe venir de la dirección configurada), solo con
/// respuesta 200 y un documento acotado. Si algo falla lanza, y la caché conserva la copia anterior hasta cumplir 5 minutos. No lleva credenciales: lo publicado es público.
/// </summary>
public sealed class HttpObtenedorDeClavesPublicadas(HttpClient cliente, OpcionesDeClavesPublicadas opciones) : IObtenedorDeClavesPublicadas
{
    public Task<string> ObtenerJwks(CancellationToken cancelacion = default) => Obtener(opciones.Jwks, cancelacion);

    public Task<string> ObtenerRevocaciones(CancellationToken cancelacion = default) => Obtener(opciones.Revocaciones, cancelacion);

    private async Task<string> Obtener(Uri url, CancellationToken cancelacion)
    {
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(opciones.Espera);
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        using var respuesta = await cliente.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, plazo.Token).ConfigureAwait(false);
        if (respuesta.StatusCode != HttpStatusCode.OK || respuesta.RequestMessage?.RequestUri != url)
        {
            throw new InvalidOperationException("La réplica de gestión no entregó las claves publicadas.");
        }

        if (respuesta.Content.Headers.ContentLength is { } largo && largo > opciones.MaximoDeBytes)
        {
            throw new InvalidOperationException("El documento de claves publicadas es demasiado grande.");
        }

        await using var flujo = await respuesta.Content.ReadAsStreamAsync(plazo.Token).ConfigureAwait(false);
        var buffer = new byte[opciones.MaximoDeBytes + 1];
        var leidos = 0;
        int n;
        while (leidos < buffer.Length && (n = await flujo.ReadAsync(buffer.AsMemory(leidos), plazo.Token).ConfigureAwait(false)) > 0)
        {
            leidos += n;
        }

        if (leidos > opciones.MaximoDeBytes)
        {
            throw new InvalidOperationException("El documento de claves publicadas es demasiado grande.");
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, leidos);
    }
}

public sealed record OpcionesDeLaRenovacionDeClaves
{
    /// <summary>Cada cuánto se renuevan las claves: bastante menos que los 5 minutos de vigencia, para que una renovación fallida tenga reintentos antes de cerrar (IDE-61).</summary>
    public TimeSpan Intervalo { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Con tantas renovaciones fallidas seguidas se publica una alerta técnica (la copia está por vencer y el Inventario cerrará).</summary>
    public int FallosSeguidosParaAlertar { get; init; } = 3;
}

/// <summary>
/// El trabajo de fondo que renueva la caché de claves del Inventario (T41; IDE-61, IDE-85): una renovación al empezar y otra cada intervalo, como actor «sistema». Una renovación fallida no detiene el trabajo (la copia
/// anterior vale hasta cumplir 5 minutos y después se rechaza toda credencial) y, si se repite, avisa por una alerta técnica. Lo ejecuta un servicio hospedado de la Api con <see cref="Correr"/>.
/// </summary>
public sealed class TrabajoDeRenovacionDeClaves(
    CacheDeClavesDelInventario cache,
    IPublicadorDeAlertasTecnicas alertas,
    IEstablecedorDeActor actor,
    IReloj reloj,
    OpcionesDeLaRenovacionDeClaves? opciones = null,
    TimeProvider? tiempo = null)
{
    public const string Nombre = "renovar-claves-de-verificacion";

    private readonly OpcionesDeLaRenovacionDeClaves _opciones = opciones ?? new OpcionesDeLaRenovacionDeClaves();
    private int _fallosSeguidos;

    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        actor.Establecer(ActorActual.DelSistema());
        try
        {
            if (await cache.Renovar(cancelacion).ConfigureAwait(false))
            {
                _fallosSeguidos = 0;
                return;
            }

            _fallosSeguidos++;
            if (_fallosSeguidos == _opciones.FallosSeguidosParaAlertar)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }
        }
        finally
        {
            actor.Limpiar();
        }
    }

    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(_opciones.Intervalo, tiempo ?? TimeProvider.System);
        try
        {
            do
            {
                await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
            }
            while (await temporizador.WaitForNextTickAsync(cancelacion).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado del servicio.
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(TrabajoDeUmbralesDeAlerta.MotivoDeAlerta, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, la próxima renovación fallida lo vuelve a intentar.
        }
    }
}
