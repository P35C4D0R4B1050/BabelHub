using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Diagnostics;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>El código estable y el detalle que la Api da a una clave simbólica de regla de negocio (por ejemplo «stock-insuficiente»).</summary>
public sealed record MensajeEspecifico(string Codigo, string Detalle);

/// <summary>Opciones del manejo de errores: el prefijo del servicio (<c>ECO</c> o <c>INV</c>) y el catálogo de códigos específicos que declara cada feature.</summary>
public sealed class OpcionesDeErrores
{
    public OpcionesDeErrores(string servicio, IReadOnlyDictionary<string, MensajeEspecifico>? especificos = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicio);
        Servicio = servicio;
        Especificos = especificos ?? new Dictionary<string, MensajeEspecifico>();
    }

    public string Servicio { get; }

    public IReadOnlyDictionary<string, MensajeEspecifico> Especificos { get; }
}

/// <summary>Traduce una <see cref="CategoriaDeError"/> o un límite excedido al error uniforme <c>application/problem+json</c> (SEG-03, SEG-07).</summary>
public static class ProblemasHttp
{
    public const string TipoDeContenido = "application/problem+json";
    public const string CabeceraDeSeguimiento = "X-Trace-Id";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int EstadoDe(CategoriaDeError categoria) => categoria switch
    {
        CategoriaDeError.Validacion => StatusCodes.Status400BadRequest,
        CategoriaDeError.NoAutenticado => StatusCodes.Status401Unauthorized,
        CategoriaDeError.Prohibido => StatusCodes.Status403Forbidden,
        CategoriaDeError.NoEncontrado => StatusCodes.Status404NotFound,
        CategoriaDeError.Conflicto => StatusCodes.Status409Conflict,
        CategoriaDeError.ReglaDeNegocio => StatusCodes.Status422UnprocessableEntity,
        CategoriaDeError.Limite => StatusCodes.Status429TooManyRequests,
        CategoriaDeError.NoDisponible => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static string SufijoDe(CategoriaDeError categoria) => categoria switch
    {
        CategoriaDeError.Validacion => CodigosDeError.ValidacionInvalida,
        CategoriaDeError.NoAutenticado => CodigosDeError.NoAutenticado,
        CategoriaDeError.Prohibido => CodigosDeError.Prohibido,
        CategoriaDeError.NoEncontrado => CodigosDeError.NoEncontrado,
        CategoriaDeError.Conflicto => CodigosDeError.Conflicto,
        CategoriaDeError.ReglaDeNegocio => CodigosDeError.ReglaDeNegocioIncumplida,
        CategoriaDeError.Limite => CodigosDeError.LimiteExcedido,
        CategoriaDeError.NoDisponible => CodigosDeError.ServicioNoDisponible,
        _ => CodigosDeError.ErrorInesperado,
    };

    public static string TituloDe(CategoriaDeError categoria) => categoria switch
    {
        CategoriaDeError.Validacion => "Solicitud inválida",
        CategoriaDeError.NoAutenticado => "No autenticado",
        CategoriaDeError.Prohibido => "Acción no permitida",
        CategoriaDeError.NoEncontrado => "No encontrado",
        CategoriaDeError.Conflicto => "Conflicto",
        CategoriaDeError.ReglaDeNegocio => "Regla de negocio incumplida",
        CategoriaDeError.Limite => "Límite excedido",
        CategoriaDeError.NoDisponible => "Servicio no disponible",
        _ => "Error inesperado",
    };

    public static ProblemaDto Crear(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Crear(contexto, opciones, error.Categoria, error.Clave, error.Errores);
    }

    /// <summary>
    /// El problema de una categoría. Solo la validación, la regla de negocio y el límite exponen el motivo (SEG-16): el código específico
    /// de la clave simbólica si la feature lo declaró y los campos inválidos; el resto lleva un mensaje genérico sin datos internos.
    /// </summary>
    public static ProblemaDto Crear(
        HttpContext contexto,
        OpcionesDeErrores opciones,
        CategoriaDeError categoria,
        string clave = "",
        IEnumerable<ErrorDeCampo>? errores = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);

        var expone = categoria.ExponeElMotivo();
        var especifico = expone && opciones.Especificos.TryGetValue(clave, out var m) ? m : null;

        return new ProblemaDto
        {
            Type = "about:blank",
            Title = TituloDe(categoria),
            Status = EstadoDe(categoria),
            Detail = especifico?.Detalle ?? categoria.MensajeGenerico(),
            Code = especifico?.Codigo ?? CodigosDeError.De(opciones.Servicio, SufijoDe(categoria)),
            TraceId = contexto.TraceIdentifier,
            Errores = expone ? (errores ?? []).Select(e => new ErrorDeCampoDto(e.Campo, e.Clave)).ToList() : [],
        };
    }

    public static async Task Escribir(HttpContext contexto, ProblemaDto problema, IReadOnlyDictionary<string, string>? cabeceras = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(problema);
        if (contexto.Response.HasStarted)
        {
            return;
        }

        contexto.Response.Clear();
        contexto.Response.StatusCode = problema.Status;
        contexto.Response.ContentType = TipoDeContenido;
        foreach (var (nombre, valor) in cabeceras ?? new Dictionary<string, string>())
        {
            contexto.Response.Headers[nombre] = valor;
        }

        await contexto.Response.WriteAsync(JsonSerializer.Serialize(problema, problema.GetType(), Json), contexto.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// La respuesta única de un límite excedido (429 con <c>Retry-After</c>, el motivo, la espera y la nueva disponibilidad) o, si el
    /// almacén de contadores no responde, «servicio no disponible» (503). Nunca lleva datos de cuentas, exista o no la cuenta.
    /// </summary>
    public static Task EscribirLimite(HttpContext contexto, OpcionesDeErrores opciones, ResultadoDeLimite resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        if (resultado.Estado == EstadoDeLimite.NoDisponible)
        {
            return Escribir(contexto, Crear(contexto, opciones, CategoriaDeError.NoDisponible));
        }

        var espera = resultado.EsperaEnSegundos ?? 1;
        var problema = new RespuestaDeLimiteDto
        {
            Title = TituloDe(CategoriaDeError.Limite),
            Status = StatusCodes.Status429TooManyRequests,
            Detail = CategoriaDeError.Limite.MensajeGenerico(),
            Code = CodigosDeError.De(opciones.Servicio, CodigosDeError.LimiteExcedido),
            TraceId = contexto.TraceIdentifier,
            Motivo = resultado.Motivo ?? string.Empty,
            RetryAfterSeconds = espera,
            DisponibleDesde = resultado.DisponibleDesde ?? DateTimeOffset.UtcNow.AddSeconds(espera),
        };
        var retryAfter = new Dictionary<string, string> { ["Retry-After"] = espera.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return Escribir(contexto, problema, retryAfter);
    }
}

/// <summary>
/// <see cref="IExceptionHandler"/> que convierte toda excepción en el error uniforme: una <see cref="ExcepcionDeNegocio"/> con su
/// categoría y clave, el almacén de contadores caído en «no disponible», un contexto de actor ausente en «no autenticado» y todo lo
/// demás en un error inesperado genérico (sin trazas ni datos internos) que se registra con su excepción.
/// </summary>
public sealed class ManejadorDeExcepciones(OpcionesDeErrores opciones, ILogger<ManejadorDeExcepciones> registro) : IExceptionHandler
{
    /// <summary>Un fallo transitorio de la base (conexión perdida, plazo vencido, sin conexiones libres) en cualquier nivel de la cadena de excepciones; un error de base que no es transitorio es un defecto y no entra aquí.</summary>
    /// <summary>Registra la caída (solo el tipo de la excepción y el identificador de seguimiento, nunca su mensaje) y responde el 503 uniforme.</summary>
    private ProblemaDto AvisarBaseNoDisponible(HttpContext httpContext, OpcionesDeErrores opciones, Exception excepcion)
    {
        registro.LogWarning("Base de datos no disponible en {Metodo} {Ruta}: {Tipo} (traza {Traza})", httpContext.Request.Method, httpContext.Request.Path.Value, excepcion.GetType().Name, httpContext.TraceIdentifier);
        return ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.NoDisponible);
    }

    internal static bool EsBaseNoDisponible(Exception excepcion)
    {
        for (var actual = excepcion; actual is not null; actual = actual.InnerException)
        {
            if (actual is System.Data.Common.DbException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // Si quien llamó ya se fue, no hay a quién responder.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var problema = exception switch
        {
            ExcepcionDeNegocio negocio => ProblemasHttp.Crear(httpContext, opciones, negocio.ComoError()),
            MotorDeLimitesNoDisponibleException => ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.NoDisponible),
            ContextoDeActorAusenteException => ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.NoAutenticado),
            // La auditoría caída no deja pasar la operación (AUD-04, la transacción ya revirtió): «servicio no disponible», no un error inesperado (AC-AUD-03).
            AuditoriaNoDisponibleException => ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.NoDisponible),
            // La base de pedidos no responde o el esquema no está listo: 503 uniforme y reintentable, sin el motivo técnico (PED-41).
            PedidosNoDisponiblesException => ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.NoDisponible),
            // La política de filas rechazó el acceso: es un acceso fuera de alcance (403 registrado), nunca «sin stock» ni un 500 (INV-1a).
            AlcanceDeDatosDenegadoException => ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.Prohibido, "alcance-insuficiente"),
            BadHttpRequestException => ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.Validacion),
            // La base inalcanzable, sin conexiones libres o vencida en plazo es «servicio no disponible» (503 uniforme: el cliente reintenta), no un error inesperado (hallazgo de K6-1, resiliencia).
            _ when EsBaseNoDisponible(exception) => AvisarBaseNoDisponible(httpContext, opciones, exception),
            _ => null,
        };

        if (problema is null)
        {
            registro.LogError(exception, "Error inesperado en {Metodo} {Ruta}", httpContext.Request.Method, httpContext.Request.Path.Value);
            problema = ProblemasHttp.Crear(httpContext, opciones, CategoriaDeError.Inesperado);
        }

        // Un 401 o un 403 que sale de una excepción no pasó por el middleware de salida (el manejador responde fuera de él): se registra aquí, antes de responder y sin depender de que el cliente siga conectado (T28, AUD-05).
        var denegada = problema.Status switch
        {
            StatusCodes.Status401Unauthorized => (CategoriaDeError?)CategoriaDeError.NoAutenticado,
            StatusCodes.Status403Forbidden => CategoriaDeError.Prohibido,
            _ => null,
        };
        if (denegada is { } categoria)
        {
            var motivo = exception switch
            {
                ExcepcionDeNegocio negocio => negocio.ComoError().Clave,
                AlcanceDeDatosDenegadoException => "alcance-insuficiente",
                _ => null,
            };
            await DenegacionesHttp.Registrar(httpContext, categoria, motivo, problema.Code).ConfigureAwait(false);
        }

        await ProblemasHttp.Escribir(httpContext, problema).ConfigureAwait(false);
        return true;
    }
}

/// <summary>
/// Asigna a cada solicitud su identificador de seguimiento propio (<c>traceId</c> de los errores y cabecera <c>X-Trace-Id</c>) y lo
/// pone en el alcance de los registros. Un identificador de correlación que envíe el cliente no lo reemplaza: solo se registra si
/// tiene una forma segura, así cien solicitudes con el mismo valor siguen siendo cien solicitudes distintas.
/// </summary>
public sealed class CorrelacionMiddleware(RequestDelegate siguiente, ILogger<CorrelacionMiddleware> registro)
{
    public const string CabeceraDelCliente = "X-Correlacion-Id";

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var id = Guid.NewGuid().ToString("N");
        contexto.TraceIdentifier = id;
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers[ProblemasHttp.CabeceraDeSeguimiento] = id;
            return Task.CompletedTask;
        });

        var alcance = new Dictionary<string, object?> { ["TraceId"] = id };
        var delCliente = contexto.Request.Headers[CabeceraDelCliente].ToString();
        if (delCliente.Length is > 0 and <= 64 && delCliente.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            alcance["CorrelacionDelCliente"] = delCliente;
        }

        using (registro.BeginScope(alcance))
        {
            await siguiente(contexto).ConfigureAwait(false);
        }
    }
}

public static class ManejoDeErroresExtensiones
{
    public static IServiceCollection AgregarManejoDeErrores(this IServiceCollection servicios, OpcionesDeErrores opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AddSingleton(opciones);
        servicios.AddExceptionHandler<ManejadorDeExcepciones>();
        return servicios;
    }

    /// <summary>El manejador de errores va primero (también cubre al resto del pipeline) y la correlación enseguida; los códigos de estado sin cuerpo se vuelven el error uniforme.</summary>
    public static IApplicationBuilder UsarManejoDeErrores(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler(rama => rama.Run(contexto =>
        {
            var opciones = contexto.RequestServices.GetRequiredService<OpcionesDeErrores>();
            return ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, opciones, CategoriaDeError.Inesperado));
        }));
        app.UseMiddleware<CorrelacionMiddleware>();
        app.UseStatusCodePages(contexto =>
        {
            var opciones = contexto.HttpContext.RequestServices.GetRequiredService<OpcionesDeErrores>();
            var estado = contexto.HttpContext.Response.StatusCode;
            var categoria = estado switch
            {
                StatusCodes.Status400BadRequest or StatusCodes.Status405MethodNotAllowed or StatusCodes.Status415UnsupportedMediaType => CategoriaDeError.Validacion,
                StatusCodes.Status401Unauthorized => CategoriaDeError.NoAutenticado,
                StatusCodes.Status403Forbidden => CategoriaDeError.Prohibido,
                StatusCodes.Status404NotFound => CategoriaDeError.NoEncontrado,
                StatusCodes.Status409Conflict => CategoriaDeError.Conflicto,
                StatusCodes.Status422UnprocessableEntity => CategoriaDeError.ReglaDeNegocio,
                StatusCodes.Status429TooManyRequests => CategoriaDeError.Limite,
                StatusCodes.Status503ServiceUnavailable => CategoriaDeError.NoDisponible,
                _ => CategoriaDeError.Inesperado,
            };
            var problema = ProblemasHttp.Crear(contexto.HttpContext, opciones, categoria) with { Status = estado };
            return ProblemasHttp.Escribir(contexto.HttpContext, problema);
        });
        return app;
    }
}
