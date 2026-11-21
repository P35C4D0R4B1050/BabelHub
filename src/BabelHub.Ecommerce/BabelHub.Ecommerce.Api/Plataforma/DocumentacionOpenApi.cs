using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>Cómo se autentica una ruta que no está en el catálogo de contratos v1 de <c>Shared</c> (sondas, claves públicas, firmas de proveedor, administración).</summary>
public enum SeguridadDeRutaComplementaria
{
    /// <summary>Pública: no lleva ninguna credencial.</summary>
    Ninguna,

    /// <summary>Credencial de servicio en la cabecera <c>X-Credencial-De-Servicio</c>.</summary>
    CredencialDeServicio,

    /// <summary>Firma de proveedor (marca de tiempo y firma HMAC), no credencial de servicio.</summary>
    FirmaDeProveedor,

    /// <summary>Token de usuario verificable (administración).</summary>
    TokenDeUsuario,

    /// <summary>Cookie de la sesión web con origen, <c>Sec-Fetch-Site</c> y token antifalsificación (administración desde la interfaz web).</summary>
    SesionWeb,
}

/// <summary>Una ruta del servicio que no es una operación del contrato v1 entre servicios y se documenta aparte, con su resumen y su seguridad.</summary>
public sealed record RutaComplementaria(string Metodo, string Ruta, string Resumen, SeguridadDeRutaComplementaria Seguridad, int Estado = 200);

/// <summary>
/// El documento OpenAPI 3 del servicio (OAS-1), <b>generado desde el código</b>: las rutas, los métodos y los parámetros de ruta salen de los endpoints mapeados; el alcance, el cuerpo, la respuesta, los parámetros de consulta, la clave de
/// idempotencia y los códigos de error salen del catálogo de contratos v1 de <c>BabelHub.Shared</c> (<see cref="CatalogoDeOperaciones"/>), cuyos DTOs se convierten en esquemas; los errores se describen como <c>application/problem+json</c>
/// (<see cref="ProblemaDto"/>). Una ruta que ni está en el catálogo ni en las rutas complementarias no tiene qué decir de sí misma: la prueba de documentación la rechaza. Se sirve solo en <c>Development</c>.
/// </summary>
public static partial class DocumentacionOpenApi
{
    public const string Documento = "v1";
    public const string RutaDelDocumento = "/openapi/v1.json";

    /// <summary>La ruta de la interfaz interactiva de la documentación (solo en Development).</summary>
    public const string RutaDeLaInterfaz = "/documentacion";

    /// <summary>Si la ruta es de la interfaz interactiva o de sus recursos (no es parte de la API que se documenta).</summary>
    public static bool EsDeLaInterfaz(string ruta) => ruta.Equals(RutaDeLaInterfaz, StringComparison.Ordinal) || ruta.StartsWith(RutaDeLaInterfaz + "/", StringComparison.Ordinal);
    public const string EsquemaCredencialDeServicio = "credencialDeServicio";
    public const string EsquemaTokenDeUsuario = "tokenDeUsuario";
    public const string EsquemaSesionWeb = "sesionWeb";
    public const string TipoDeProblema = "application/problem+json";
    public const string Servicio = "ecommerce";

    private const string Json = "application/json";

    /// <summary>En el E-commerce las operaciones de usuario van solo con el token (la web las consume desde el servidor), sin credencial de servicio.</summary>
    private const bool TokenLlevaCredencialDeServicio = false;

    /// <summary>Las rutas del E-commerce fuera del catálogo de contratos v1.</summary>
    public static IReadOnlyList<RutaComplementaria> Complementarias { get; } =
    [
        new("GET", "/health/live", "Sonda de vida: el proceso responde.", SeguridadDeRutaComplementaria.Ninguna),
        new("GET", "/health/ready", "Sonda de preparación: base de datos y dependencias disponibles.", SeguridadDeRutaComplementaria.Ninguna),
        new("GET", RutasDeClavesDeFirma.Jwks, "Claves públicas de firma de Identidad (JWKS), sin autenticación; el Inventario las renueva cada 5 minutos.", SeguridadDeRutaComplementaria.Ninguna),
        new("GET", RutasDeClavesDeFirma.Revocaciones, "Identificadores de clave (`kid`) revocados, sin autenticación.", SeguridadDeRutaComplementaria.Ninguna),
        new("POST", RutasDeClavesDeFirma.Rotar, "Rota la clave de firma de Identidad (solo el administrador central, con la sesión web).", SeguridadDeRutaComplementaria.SesionWeb, 204),
        new("POST", RutasDeClavesDeFirma.Comprometida, "Declara comprometida la clave de firma de Identidad (solo el administrador central, con la sesión web).", SeguridadDeRutaComplementaria.SesionWeb, 204),
        new("POST", RutasDeServicio.Ecommerce + "/avisos/numeros-desactivados", "Desactiva el aviso por WhatsApp de un número (administración, con la sesión web).", SeguridadDeRutaComplementaria.SesionWeb, 204),
        new("POST", RutasDeServicio.Ecommerce + "/avisos/numeros-desactivados/reactivar", "Reactiva el aviso por WhatsApp de un número (administración, con la sesión web).", SeguridadDeRutaComplementaria.SesionWeb, 204),
        new("GET", RutasDeServicio.Ecommerce + "/avisos", "Consulta de avisos para la administración: estado y fechas, nunca el número ni los campos.", SeguridadDeRutaComplementaria.SesionWeb),
        new("POST", RutasDeServicio.Ecommerce + "/integraciones/n8n/estados", "Estados de entrega de n8n (solo existe si el envío es por n8n; red interna), con firma de proveedor.", SeguridadDeRutaComplementaria.FirmaDeProveedor, 204),
        new("POST", RutasDeServicio.Ecommerce + "/integraciones/pasarela/avisos", "Avisos de cargo de la pasarela de pagos (deshabilitada hasta tener un esquema de firma verificable), con firma de proveedor.", SeguridadDeRutaComplementaria.FirmaDeProveedor, 204),
    ];

    /// <summary>Estado HTTP de cada código de error estable (los genéricos por su categoría; los específicos, el de su documentación en <c>Shared</c>).</summary>
    private static readonly Dictionary<string, int> EstadoDeCodigo = new(StringComparer.Ordinal)
    {
        ["-NO-AUTENTICADO"] = 401,
        ["-PROHIBIDO"] = 403,
        ["-NO-ENCONTRADO"] = 404,
        ["-VALIDACION-INVALIDA"] = 400,
        ["-LIMITE-EXCEDIDO"] = 429,
        ["-SERVICIO-NO-DISPONIBLE"] = 503,
        ["-STOCK-INSUFICIENTE"] = 409,
        ["-FORMATO-EN-CONFLICTO"] = 409,
        ["-RESERVA-ESTADO-INVALIDO"] = 409,
        ["-IDEMPOTENCIA-EN-CONFLICTO"] = 409,
        ["-IDEMPOTENCIA-EN-CURSO"] = 409,
        ["-FUENTE-DUPLICADA"] = 409,
        ["-TRASLADO-ESTADO-INVALIDO"] = 409,
        ["-EDICION-NO-IMPRESA"] = 422,
        ["-RUTA-INVALIDA"] = 422,
        ["-CONTEO-MENOR-QUE-RESERVAS"] = 422,
        ["-SIN-CAMBIOS"] = 422,
        ["-FUENTE-NO-ES-DE-RECEPCION"] = 422,
        ["-TRASLADO-VINCULADO-NO-SE-RECHAZA"] = 422,
    };

    [GeneratedRegex(@"\{(\w+)[^}]*\}")]
    private static partial Regex Parametro();

    /// <summary>Quita las restricciones de los parámetros de una ruta: <c>/x/{id:guid}</c> es <c>/x/{id}</c>.</summary>
    public static string Normalizar(string ruta) => Parametro().Replace("/" + ruta.TrimStart('/'), "{$1}");

    /// <summary>La operación del contrato v1 que publica este servicio en ese método y esa ruta, si la hay.</summary>
    public static OperacionDeContrato? Operacion(string metodo, string ruta) =>
        CatalogoDeOperaciones.V1.FirstOrDefault(o => o.Ruta.StartsWith(RutasDeServicio.Ecommerce, StringComparison.Ordinal)
            && string.Equals(o.Metodo, metodo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Normalizar(o.Ruta), Normalizar(ruta), StringComparison.Ordinal));

    /// <summary>La ruta complementaria de ese método y esa ruta, si la hay.</summary>
    public static RutaComplementaria? Complementaria(string metodo, string ruta) =>
        Complementarias.FirstOrDefault(c => string.Equals(c.Metodo, metodo, StringComparison.OrdinalIgnoreCase) && string.Equals(Normalizar(c.Ruta), Normalizar(ruta), StringComparison.Ordinal));

    public static IServiceCollection AgregarDocumentacionOpenApi(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddOpenApi(Documento, opciones =>
        {
            // Solo la superficie HTTP del servicio: no las acciones de vistas de la web (el E-commerce) ni el propio documento.
            opciones.ShouldInclude = descripcion => descripcion.ActionDescriptor is not ControllerActionDescriptor && !(descripcion.RelativePath ?? string.Empty).StartsWith("desarrollo/", StringComparison.Ordinal);
            opciones.AddDocumentTransformer(TransformarDocumento);
            opciones.AddOperationTransformer(TransformarOperacion);
        });
        return servicios;
    }

    /// <summary>Sirve el documento en <c>/openapi/v1.json</c> y su interfaz interactiva en <c>/documentacion</c>, solo en <c>Development</c>.</summary>
    public static WebApplication MapearDocumentacionOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi(RutaDelDocumento);
            // La UI interactiva (Scalar) sobre el mismo documento, con sus recursos embebidos en el paquete (funciona sin internet) y solo en Development: en producción no se expone (SEG-11).
            app.MapScalarApiReference(RutaDeLaInterfaz, opciones => opciones.WithTitle("BabelHub — API del E-commerce").WithOpenApiRoutePattern(RutaDelDocumento));
        }

        return app;
    }

    private static Task TransformarDocumento(OpenApiDocument documento, OpenApiDocumentTransformerContext contexto, CancellationToken cancelacion)
    {
        documento.Info = new OpenApiInfo
        {
            Title = "BabelHub — API del E-commerce",
            Version = VersionDeContrato.V1,
            Description = "Contrato v1 del E-commerce (`/api/ecommerce/v1`), generado desde el código. Lo llama el Inventario con la credencial de servicio del alcance `solicitar-aviso` (`X-Credencial-De-Servicio`); la administración de avisos va con la sesión web y la de auditoría, alertas e historial con el token del usuario verificado por Identidad. Solo describe las rutas que el host mapea. La interfaz web (vistas Razor) no forma parte de este contrato. Los errores son `application/problem+json` con un `code` estable y un `traceId`.",
        };
        // Sin las etiquetas que la plantilla agrega por omisión (el nombre del ensamblado y de la clase de endpoints): solo las que asigna este documento.
        documento.Tags = null;
        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        documento.Components.SecuritySchemes[EsquemaCredencialDeServicio] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = CabecerasDeServicio.CredencialDeServicio,
            Description = "Credencial de servicio del alcance de la operación (el alcance exigido va en `x-alcance`). Solo por HTTPS entre servicios.",
        };
        documento.Components.SecuritySchemes[EsquemaTokenDeUsuario] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT (ES256, emitido por Identidad)",
            Description = "Credencial de acceso del usuario, firmada por Identidad; el servicio la verifica con la clave pública publicada. Nunca se acepta una cabecera de actor.",
        };
        documento.Components.SecuritySchemes[EsquemaSesionWeb] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "__Host-bh-gestion",
            Description = "Cookie de la sesión web (`__Host-`, `Secure`, `HttpOnly`), con comprobación de origen, `Sec-Fetch-Site` y token antifalsificación en las operaciones que cambian algo.",
        };
        return Task.CompletedTask;
    }

    private static async Task TransformarOperacion(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto, CancellationToken cancelacion)
    {
        var descripcion = contexto.Description;
        var metodo = descripcion.HttpMethod ?? "GET";
        var ruta = Normalizar(descripcion.RelativePath ?? string.Empty);
        operacion.Extensions ??= new Dictionary<string, IOpenApiExtension>();

        if (Operacion(metodo, ruta) is { } contrato)
        {
            await DescribirContrato(operacion, contexto, contrato, cancelacion).ConfigureAwait(false);
            return;
        }

        if (Complementaria(metodo, ruta) is { } complementaria)
        {
            DescribirComplementaria(operacion, contexto, complementaria);
            if (complementaria.Seguridad != SeguridadDeRutaComplementaria.Ninguna)
            {
                await AgregarErroresGenericos(operacion, contexto, cancelacion).ConfigureAwait(false);
            }

            return;
        }

        // Sin contrato ni descripción complementaria: no se inventa nada; la prueba de documentación lo rechaza.
        operacion.Summary = null;
    }

    private static async Task DescribirContrato(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto, OperacionDeContrato contrato, CancellationToken cancelacion)
    {
        operacion.OperationId = contrato.Nombre;
        operacion.Summary = contrato.Nombre;
        operacion.Description = (contrato.RequiereTokenDeUsuario && !TokenLlevaCredencialDeServicio ? "Operación de usuario (alcance `{contrato.Alcance}` del token verificado por Identidad; sin credencial de servicio). " : $"Alcance de la credencial: `{contrato.Alcance}`. ")
            + (contrato.RequiereTokenDeUsuario ? "Lleva el token del usuario. " : "No lleva token de usuario. ")
            + (contrato.RequiereClaveDeIdempotencia ? "Exige `Idempotency-Key`. " : string.Empty)
            + (contrato.EsLectura ? "Es una lectura: el cliente puede reintentarla." : "Cambia el estado.");
        operacion.Tags = new HashSet<OpenApiTagReference> { new(Etiqueta(contrato.Ruta), contexto.Document) };
        operacion.Extensions!["x-operacion"] = new JsonNodeExtension(JsonValue.Create(contrato.Nombre));
        operacion.Extensions["x-alcance"] = new JsonNodeExtension(JsonValue.Create(contrato.Alcance));

        AgregarSeguridad(operacion, contexto, contrato.RequiereTokenDeUsuario);
        AgregarParametros(operacion, contrato);

        if (contrato.Solicitud is { } solicitud)
        {
            operacion.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType> { [Json] = new() { Schema = await Esquema(contexto, solicitud, cancelacion).ConfigureAwait(false) } },
            };
        }

        operacion.Responses = new OpenApiResponses();
        var exito = new OpenApiResponse { Description = "Resultado normal de la operación." };
        if (contrato.Respuesta is { } respuesta)
        {
            exito.Content = new Dictionary<string, OpenApiMediaType> { [Json] = new() { Schema = await Esquema(contexto, respuesta, cancelacion).ConfigureAwait(false) } };
        }

        operacion.Responses[contrato.CodigoDeExito.ToString(CultureInfo.InvariantCulture)] = exito;

        var problema = await Esquema(contexto, typeof(ProblemaDto), cancelacion).ConfigureAwait(false);
        foreach (var grupo in contrato.Errores.GroupBy(EstadoDe).OrderBy(g => g.Key))
        {
            operacion.Responses[grupo.Key.ToString(CultureInfo.InvariantCulture)] = new OpenApiResponse
            {
                Description = "Error. Códigos estables posibles: " + string.Join(", ", grupo.Select(c => $"`{c}`")) + ".",
                Content = new Dictionary<string, OpenApiMediaType> { [TipoDeProblema] = new() { Schema = problema } },
            };
        }
    }

    private static void DescribirComplementaria(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto, RutaComplementaria ruta)
    {
        operacion.Summary = ruta.Resumen;
        operacion.Tags = new HashSet<OpenApiTagReference> { new("plataforma", contexto.Document) };
        operacion.Extensions!["x-alcance"] = new JsonNodeExtension(JsonValue.Create(ruta.Seguridad.ToString()));
        switch (ruta.Seguridad)
        {
            case SeguridadDeRutaComplementaria.CredencialDeServicio:
                AgregarSeguridad(operacion, contexto, false);
                break;
            case SeguridadDeRutaComplementaria.TokenDeUsuario:
                AgregarSeguridad(operacion, contexto, true);
                break;
            case SeguridadDeRutaComplementaria.SesionWeb:
                operacion.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(EsquemaSesionWeb, contexto.Document)] = [] }];
                break;
            case SeguridadDeRutaComplementaria.FirmaDeProveedor:
                operacion.Description = "Autenticada por firma de proveedor (cabeceras `X-Marca-De-Tiempo` y `X-Firma`), no por credencial de servicio.";
                break;
            default:
                operacion.Security = [];
                break;
        }

        operacion.Responses = new OpenApiResponses
        {
            [ruta.Estado.ToString(CultureInfo.InvariantCulture)] = new OpenApiResponse { Description = ruta.Resumen },
        };
    }

    private static string AlcanceDeComplementaria(SeguridadDeRutaComplementaria seguridad) => seguridad switch
    {
        SeguridadDeRutaComplementaria.TokenDeUsuario => "operacion-de-usuario",
        SeguridadDeRutaComplementaria.CredencialDeServicio => "credencial-de-servicio",
        SeguridadDeRutaComplementaria.FirmaDeProveedor => "firma-de-proveedor",
        SeguridadDeRutaComplementaria.SesionWeb => "sesion-web",
        _ => "publica",
    };

    private static async Task AgregarErroresGenericos(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto, CancellationToken cancelacion)
    {
        var problema = await Esquema(contexto, typeof(ProblemaDto), cancelacion).ConfigureAwait(false);
        operacion.Responses ??= new OpenApiResponses();
        foreach (var estado in new[] { 401, 429, 503 })
        {
            operacion.Responses[estado.ToString(CultureInfo.InvariantCulture)] = new OpenApiResponse
            {
                Description = "Error (`application/problem+json`).",
                Content = new Dictionary<string, OpenApiMediaType> { [TipoDeProblema] = new() { Schema = problema } },
            };
        }
    }

    private static void AgregarSeguridad(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto, bool conToken)
    {
        var requisito = new OpenApiSecurityRequirement();
        if (!conToken || TokenLlevaCredencialDeServicio)
        {
            requisito[new OpenApiSecuritySchemeReference(EsquemaCredencialDeServicio, contexto.Document)] = [];
        }

        if (conToken)
        {
            requisito[new OpenApiSecuritySchemeReference(EsquemaTokenDeUsuario, contexto.Document)] = [];
        }

        operacion.Security = [requisito];
    }

    private static void AgregarParametros(OpenApiOperation operacion, OperacionDeContrato contrato)
    {
        operacion.Parameters ??= [];
        if (contrato.RequiereClaveDeIdempotencia)
        {
            operacion.Parameters.Add(new OpenApiParameter
            {
                Name = CabecerasDeServicio.ClaveDeIdempotencia,
                In = ParameterLocation.Header,
                Required = true,
                Description = "Clave de idempotencia: repetir la misma clave con la misma carga devuelve el mismo resultado; con otra carga, un conflicto.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        foreach (var texto in contrato.Parametros)
        {
            operacion.Parameters.Add(ParametroDeConsulta(texto));
        }
    }

    /// <summary>Convierte <c>nombre:tipo(rango)</c> o <c>nombre:tipo[min-max]</c> del catálogo en un parámetro de consulta.</summary>
    private static OpenApiParameter ParametroDeConsulta(string texto)
    {
        var partes = texto.Split(':', 2);
        var tipo = partes.Length > 1 ? partes[1] : "texto";
        var esLista = tipo.Contains('[', StringComparison.Ordinal);
        var basico = tipo.Split('(', '[')[0];
        var esquema = basico switch
        {
            "guid" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "int" => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            "instante" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
            _ => new OpenApiSchema { Type = JsonSchemaType.String },
        };

        return new OpenApiParameter
        {
            Name = partes[0],
            In = ParameterLocation.Query,
            Required = false,
            Description = $"{tipo}",
            Schema = esLista ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = esquema } : esquema,
        };
    }

    /// <summary>El esquema de un DTO: se genera una vez por tipo en <c>components/schemas</c> y cada operación lo referencia (el documento no repite el mismo DTO en cada ruta).</summary>
    private static async Task<IOpenApiSchema> Esquema(OpenApiOperationTransformerContext contexto, Type tipo, CancellationToken cancelacion)
    {
        var documento = contexto.Document ?? throw new InvalidOperationException("El contexto de la operación no trae el documento.");
        documento.Components ??= new OpenApiComponents();
        documento.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        var nombre = NombreDeEsquema(tipo);
        if (!documento.Components.Schemas.ContainsKey(nombre))
        {
            documento.Components.Schemas[nombre] = await contexto.GetOrCreateSchemaAsync(tipo, null, cancelacion).ConfigureAwait(false);
        }

        return new OpenApiSchemaReference(nombre, documento);
    }

    private static string NombreDeEsquema(Type tipo) =>
        tipo.IsGenericType ? tipo.Name.Split('`')[0] + "De" + string.Join("Y", tipo.GetGenericArguments().Select(NombreDeEsquema)) : tipo.Name;

    private static string Etiqueta(string ruta)
    {
        var segmentos = ruta.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segmentos.Length > 3 ? segmentos[3] : "general";
    }

    /// <summary>El estado HTTP de un código de error estable; un código sin estado conocido es un defecto del catálogo y falla la generación.</summary>
    private static int EstadoDe(string codigo)
    {
        var sufijo = codigo[codigo.IndexOf('-', StringComparison.Ordinal)..];
        return EstadoDeCodigo.TryGetValue(sufijo, out var estado)
            ? estado
            : throw new InvalidOperationException($"El código de error {codigo} no tiene un estado HTTP declarado en DocumentacionOpenApi.");
    }
}
