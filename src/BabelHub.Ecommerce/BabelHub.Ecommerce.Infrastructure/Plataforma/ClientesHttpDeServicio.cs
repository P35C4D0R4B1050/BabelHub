using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>Lo que cada llamada entre servicios declara sobre sí misma, como opciones de la solicitud (nunca como cabeceras).</summary>
public static class OpcionesDeLaSolicitudDeServicio
{
    public static readonly HttpRequestOptionsKey<AlcanceDeServicio> Alcance = new("babelhub.alcance");
    public static readonly HttpRequestOptionsKey<string> TokenDeUsuario = new("babelhub.token-de-usuario");
    public static readonly HttpRequestOptionsKey<bool> Idempotente = new("babelhub.idempotente");
    public static readonly HttpRequestOptionsKey<bool> ConOrigenReenviado = new("babelhub.con-origen-reenviado");

    /// <summary>El alcance de la credencial de servicio que autoriza esta operación.</summary>
    public static HttpRequestMessage ConAlcance(this HttpRequestMessage solicitud, AlcanceDeServicio alcance)
    {
        solicitud.Options.Set(Alcance, alcance);
        return solicitud;
    }

    /// <summary>El token del usuario en cuyo nombre se llama (una operación de usuario lo exige); nunca va una cabecera de actor.</summary>
    public static HttpRequestMessage ConTokenDeUsuario(this HttpRequestMessage solicitud, string token)
    {
        solicitud.Options.Set(TokenDeUsuario, token);
        return solicitud;
    }

    /// <summary>Marca una operación que lleva clave de idempotencia: aunque sea un POST, se puede reintentar.</summary>
    public static HttpRequestMessage ComoIdempotente(this HttpRequestMessage solicitud)
    {
        solicitud.Options.Set(Idempotente, true);
        return solicitud;
    }

    /// <summary>Reenvía la dirección del cliente final con la credencial de alcance «origen reenviado» (SEG-17).</summary>
    public static HttpRequestMessage ConOrigenDelCliente(this HttpRequestMessage solicitud)
    {
        solicitud.Options.Set(ConOrigenReenviado, true);
        return solicitud;
    }
}

/// <summary>
/// Credenciales de servicio de salida: un secreto estático por alcance, de al menos 32 bytes y distinto en cada alcance (D10); vienen
/// del entorno y su texto nunca se registra.
/// </summary>
public sealed class OpcionesDeCredencialesDeSalida
{
    public const int LongitudMinimaDelSecreto = 32;

    private readonly Dictionary<AlcanceDeServicio, ValorSecreto> _secretos;

    public OpcionesDeCredencialesDeSalida(IEnumerable<KeyValuePair<AlcanceDeServicio, ValorSecreto>> secretosPorAlcance)
    {
        ArgumentNullException.ThrowIfNull(secretosPorAlcance);
        _secretos = [];
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (alcance, secreto) in secretosPorAlcance)
        {
            var texto = secreto.Revelar();
            if (Encoding.UTF8.GetByteCount(texto) < LongitudMinimaDelSecreto)
            {
                throw new ArgumentException($"El secreto del alcance «{alcance.NombreEstable()}» debe tener al menos {LongitudMinimaDelSecreto} bytes.", nameof(secretosPorAlcance));
            }

            if (!vistos.Add(texto))
            {
                throw new ArgumentException("Cada alcance necesita un secreto distinto.", nameof(secretosPorAlcance));
            }

            if (!_secretos.TryAdd(alcance, secreto))
            {
                throw new ArgumentException($"El alcance «{alcance.NombreEstable()}» está repetido.", nameof(secretosPorAlcance));
            }
        }
    }

    internal string Secreto(AlcanceDeServicio alcance) =>
        _secretos.TryGetValue(alcance, out var secreto)
            ? secreto.Revelar()
            : throw new InvalidOperationException($"No hay una credencial de servicio configurada para el alcance «{alcance.NombreEstable()}».");
}

/// <summary>
/// Adjunta a toda llamada saliente entre servicios la credencial del alcance de su operación (<c>X-Credencial-De-Servicio</c>), el
/// token del usuario cuando la operación actúa por uno (<c>Authorization</c>, nunca una cabecera de actor) y, si se pide, la
/// dirección del cliente final con la credencial de origen. Descarta antes toda cabecera de credencial, de reenvío o de actor que
/// el llamador hubiera puesto, de modo que una cabecera enviada por un cliente no llega al otro servicio (SEG-15, SEG-17). Una llamada
/// que no declara su alcance no sale.
/// </summary>
/// <remarks>
/// <b>El token del usuario sale de la sesión</b> (<c>web-base</c> T14, D13): cuando el servicio registra un <see cref="ITokenDelUsuarioDeLaSesion"/>
/// (el E-commerce, desde la sesión de la solicitud) este manejador es el <b>único</b> que lo consume y el token que un llamador ponga en las opciones se
/// ignora; una operación de usuario sin sesión o sin credencial utilizable no sale. Sin ese puerto (pruebas y servicios sin sesión) vale el token de las
/// opciones. En ningún caso se registra el token.
/// </remarks>
public sealed class CredencialesDeSalidaHandler(
    OpcionesDeCredencialesDeSalida credenciales,
    IOrigenDeSolicitud origenes,
    ITokenDelUsuarioDeLaSesion? tokenDeLaSesion = null) : DelegatingHandler
{
    public const string CabeceraDeCredencial = "X-Credencial-De-Servicio";
    public const string CabeceraDeCredencialDeOrigen = "X-Credencial-De-Origen";
    public const string CabeceraDeOrigenReenviado = "X-Origen-Reenviado";

    private static readonly string[] CabecerasQueElLlamadorNoPuedeFijar =
    [
        CabeceraDeCredencial,
        CabeceraDeCredencialDeOrigen,
        CabeceraDeOrigenReenviado,
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "Forwarded",
        "X-Origen-Cliente",
        "X-Actor",
        "X-Usuario",
        "X-Rol",
        "Authorization",
    ];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Alcance, out var alcance))
        {
            throw new InvalidOperationException("Una llamada entre servicios debe declarar el alcance de su credencial.");
        }

        foreach (var cabecera in CabecerasQueElLlamadorNoPuedeFijar)
        {
            request.Headers.Remove(cabecera);
        }

        request.Headers.TryAddWithoutValidation(CabeceraDeCredencial, credenciales.Secreto(alcance));

        if (alcance == AlcanceDeServicio.OperacionDeUsuario)
        {
            var token = await TokenDelUsuario(request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Una operación de usuario lleva el token del usuario en cuyo nombre se llama.");
            }

            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }

        if (request.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.ConOrigenReenviado, out var conOrigen) && conOrigen
            && origenes.Actual is { Direccion: { } direccion })
        {
            request.Headers.TryAddWithoutValidation(CabeceraDeOrigenReenviado, direccion);
            request.Headers.TryAddWithoutValidation(CabeceraDeCredencialDeOrigen, credenciales.Secreto(AlcanceDeServicio.OrigenReenviado));
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TokenDelUsuario(HttpRequestMessage request, CancellationToken cancelacion)
    {
        if (tokenDeLaSesion is not null)
        {
            return (await tokenDeLaSesion.Actual(cancelacion).ConfigureAwait(false))?.Revelar();
        }

        return request.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.TokenDeUsuario, out var token) ? token : null;
    }
}

/// <summary>Plazos y cortacircuitos de un cliente hacia otro servicio o proveedor (SEG-14).</summary>
public sealed record OpcionesDelClienteDeServicio
{
    /// <summary>Espera total de una llamada: 10 segundos (30 para el proveedor del modelo de lenguaje).</summary>
    public TimeSpan Espera { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Reintentos de las operaciones idempotentes.</summary>
    public int ReintentosMaximos { get; init; } = 2;

    public TimeSpan RetrasoDelReintento { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Cuántas llamadas (como mínimo) se miden antes de poder abrir el cortacircuitos.</summary>
    public int LlamadasMinimasParaCortar { get; init; } = 5;

    public double ProporcionDeFallosParaCortar { get; init; } = 0.5;

    public TimeSpan VentanaDeMedicion { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cuánto dura abierto: durante ese tiempo falla al instante y las demás operaciones siguen funcionando.</summary>
    public TimeSpan DuracionDelCorte { get; init; } = TimeSpan.FromSeconds(15);

    public static OpcionesDelClienteDeServicio ParaLaClase(ClaseDeDependencia clase) =>
        new() { Espera = clase == ClaseDeDependencia.ProveedorDeLlm ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10) };
}

public static class ClientesHttpDeServicio
{
    /// <summary>
    /// Registra un cliente HTTP hacia otro servicio con el plazo, los reintentos solo de operaciones idempotentes y el cortacircuitos
    /// de <c>Microsoft.Extensions.Http.Resilience</c>, y con el manejador de salida que adjunta las credenciales (que queda dentro
    /// de la resiliencia, de modo que cada reintento lleva credenciales recién puestas).
    /// </summary>
    public static IHttpClientBuilder AgregarClienteDeServicio(
        this IServiceCollection servicios,
        string nombre,
        Uri direccion,
        OpcionesDelClienteDeServicio? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(direccion);
        var configuracion = opciones ?? new OpcionesDelClienteDeServicio();

        servicios.TryAddTransient<CredencialesDeSalidaHandler>();
        var cliente = servicios.AddHttpClient(nombre, http => http.BaseAddress = direccion);
        cliente.AddResilienceHandler($"servicio-{nombre}", constructor => Configurar(constructor, configuracion));
        cliente.AddHttpMessageHandler<CredencialesDeSalidaHandler>();
        return cliente;
    }

    /// <summary>Reintenta solo lo idempotente: los métodos que el protocolo define así o una solicitud marcada con clave de idempotencia.</summary>
    internal static bool EsIdempotente(HttpRequestMessage solicitud) =>
        solicitud.Method == HttpMethod.Get
        || solicitud.Method == HttpMethod.Head
        || solicitud.Method == HttpMethod.Options
        || solicitud.Method == HttpMethod.Put
        || solicitud.Method == HttpMethod.Delete
        || (solicitud.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Idempotente, out var idempotente) && idempotente);

    private static void Configurar(ResiliencePipelineBuilder<HttpResponseMessage> constructor, OpcionesDelClienteDeServicio opciones)
    {
        constructor.AddTimeout(opciones.Espera);

        if (opciones.ReintentosMaximos > 0)
        {
            constructor.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = opciones.ReintentosMaximos,
                Delay = opciones.RetrasoDelReintento,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = argumentos =>
                {
                    var solicitud = argumentos.Context.GetRequestMessage();
                    return ValueTask.FromResult(solicitud is not null && EsIdempotente(solicitud) && HttpClientResiliencePredicates.IsTransient(argumentos.Outcome));
                },
            });
        }

        constructor.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = opciones.LlamadasMinimasParaCortar,
            FailureRatio = opciones.ProporcionDeFallosParaCortar,
            SamplingDuration = opciones.VentanaDeMedicion,
            BreakDuration = opciones.DuracionDelCorte,
        });

        constructor.AddTimeout(TimeSpan.FromTicks(Math.Max(opciones.Espera.Ticks / 2, TimeSpan.TicksPerMillisecond)));
    }
}
