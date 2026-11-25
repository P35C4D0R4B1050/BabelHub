using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// El cliente HTTP de Culqi con la resiliencia de las dependencias externas (T37; SEG-14, PAG-07): <b>10 segundos por llamada</b>, un cortacircuitos (abre con fallos repetidos y falla al instante unos segundos, y las demás operaciones
/// siguen funcionando) y <b>un solo reintento y solo para lecturas</b> (<c>GET</c>, idempotentes). Un <c>POST</c> (el cobro y el reembolso) <b>nunca se reintenta</b>: no se supone idempotencia de Culqi (DD-96). Sin redirecciones.
/// </summary>
internal static class ClienteHttpDeCulqi
{
    /// <summary>Los reintentos de una lectura (el cobro y el reembolso no los tienen).</summary>
    public const int ReintentosDeLecturas = 1;

    public static IHttpClientBuilder AgregarClienteDeCulqi(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var cliente = servicios.AddHttpClient(OpcionesDeCulqi.NombreDelCliente, http => http.BaseAddress = new Uri(OpcionesDeCulqi.UrlDeLaApi));
        cliente.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5), PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        cliente.AddResilienceHandler("culqi", Configurar);
        return cliente;
    }

    /// <summary>La política, aparte para probarla con un manejador falso.</summary>
    internal static void Configurar(ResiliencePipelineBuilder<HttpResponseMessage> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        constructor.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = ReintentosDeLecturas,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Constant,
            UseJitter = true,
            ShouldHandle = argumentos =>
            {
                var solicitud = argumentos.Context.GetRequestMessage();
                return ValueTask.FromResult(solicitud is not null && solicitud.Method == HttpMethod.Get && HttpClientResiliencePredicates.IsTransient(argumentos.Outcome));
            },
        });
        constructor.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = 5,
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15),
        });
        constructor.AddTimeout(OpcionesDeCulqi.PlazoPorLlamada);
    }
}
