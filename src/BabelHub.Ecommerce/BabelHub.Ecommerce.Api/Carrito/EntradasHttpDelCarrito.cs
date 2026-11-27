using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Carrito;

/// <summary>
/// Lee los cuerpos de Carrito y del checkout con <b>esquema estricto</b> (plan §4, CAR-35, AC-CAR-31): un formulario o un objeto JSON con solo los campos que la acción declara más el del token antifalsificación; un campo desconocido, repetido o con otra
/// forma es validación y <b>nunca se ignora en silencio</b>. Un identificador que la persona no puede mandar (<c>carritoId</c>, <c>checkoutId</c>, <c>cuentaId</c>…) en el cuerpo, la consulta o la ruta se rechaza y <b>registra un acceso denegado</b>: la cuenta
/// y el carrito salen del actor y de la cookie, nunca de la entrada. No se devuelve lo recibido ni el valor de ningún campo (SEG-03).
/// </summary>
public static partial class EntradasHttpDelCarrito
{
    private static readonly string[] Prohibidos = ["carritoId", "checkoutId", "cuentaId", "usuarioId", "visitanteId", "visitanteHash", "usuario", "cuenta"];

    private static readonly string[] ClavesDeRutaPermitidas = ["area", "controller", "action", "lineaId"];

    public const int TopeDelCuerpo = 16 * 1024;

    /// <summary>Un <c>GET</c> no admite consulta: cualquier parámetro es validación, y uno que es un identificador prohibido además registra el acceso denegado. <see langword="null"/> si no hay consulta.</summary>
    public static async Task<ErrorDeAplicacion?> RechazarConsulta(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        foreach (var clave in contexto.Request.Query.Keys)
        {
            return await Rechazar(contexto, clave).ConfigureAwait(false) ?? Invalido(Nombre(clave));
        }

        return null;
    }

    /// <summary>Como <see cref="RechazarConsulta(HttpContext)"/> pero admite los parámetros de consulta de <paramref name="permitidos"/> (el pago acepta el <c>pedidoId</c>): cualquier otro es validación, y uno prohibido además registra el acceso denegado.</summary>
    public static async Task<ErrorDeAplicacion?> RechazarConsulta(HttpContext contexto, IReadOnlyCollection<string> permitidos)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(permitidos);
        foreach (var clave in contexto.Request.Query.Keys.Where(k => !permitidos.Contains(k, StringComparer.Ordinal)))
        {
            return await Rechazar(contexto, clave).ConfigureAwait(false) ?? Invalido(Nombre(clave));
        }

        return null;
    }

    public static readonly ErrorDeAplicacion CuerpoInvalido = new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cuerpo", "formato-invalido")]);

    public static ErrorDeAplicacion Falta(string campo) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "obligatorio")]);

    public static ErrorDeAplicacion Invalido(string campo) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "formato-invalido")]);

    /// <summary>
    /// Los campos del cuerpo (como texto) o el error de validación. <paramref name="permitidos"/> son los campos que la acción acepta; el campo del token antifalsificación se descarta. Una consulta con cualquier parámetro también se rechaza: los <c>POST</c>
    /// de Carrito no admiten consulta.
    /// </summary>
    public static async Task<(IReadOnlyDictionary<string, string>? Campos, ErrorDeAplicacion? Error)> Leer(HttpContext contexto, IReadOnlyCollection<string> permitidos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(permitidos);
        var solicitud = contexto.Request;

        // Los POST de Carrito no admiten consulta: cualquier parámetro es validación, y uno que es un identificador prohibido además registra el acceso denegado.
        foreach (var clave in solicitud.Query.Keys)
        {
            return (null, await Rechazar(contexto, clave).ConfigureAwait(false) ?? Invalido(Nombre(clave)));
        }

        foreach (var clave in solicitud.RouteValues.Keys.Where(k => !ClavesDeRutaPermitidas.Contains(k, StringComparer.OrdinalIgnoreCase)))
        {
            if (await Rechazar(contexto, clave).ConfigureAwait(false) is { } error)
            {
                return (null, error);
            }
        }

        var pares = new List<(string Clave, string Valor)>();
        if (solicitud.HasFormContentType)
        {
            foreach (var campo in solicitud.Form)
            {
                if (campo.Value.Count != 1)
                {
                    return (null, Invalido(Nombre(campo.Key)));
                }

                pares.Add((campo.Key, campo.Value[0] ?? string.Empty));
            }
        }
        else if (solicitud.HasJsonContentType())
        {
            var leidos = await LeerJson(solicitud, cancelacion).ConfigureAwait(false);
            if (leidos is null)
            {
                return (null, CuerpoInvalido);
            }

            pares.AddRange(leidos);
        }
        else if ((solicitud.ContentLength ?? 0) > 0)
        {
            return (null, CuerpoInvalido);
        }

        var campos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (clave, valor) in pares)
        {
            if (string.Equals(clave, ProteccionContraFalsificacion.NombreDelCampo, StringComparison.Ordinal))
            {
                continue;
            }

            if (await Rechazar(contexto, clave).ConfigureAwait(false) is { } prohibido)
            {
                return (null, prohibido);
            }

            if (!permitidos.Contains(clave, StringComparer.Ordinal))
            {
                return (null, Invalido(Nombre(clave)));
            }

            if (!campos.TryAdd(clave, valor))
            {
                return (null, Invalido(clave));
            }
        }

        return (campos, null);
    }

    /// <summary>Un identificador que no se admite de afuera: validación y un acceso denegado registrado (un evento por solicitud).</summary>
    private static async Task<ErrorDeAplicacion?> Rechazar(HttpContext contexto, string clave)
    {
        if (!Prohibidos.Contains(clave, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (contexto.RequestServices.GetService<IRegistradorDeDenegacionesHttp>() is { } registrador)
        {
            await registrador.Registrar(contexto, CategoriaDeError.Prohibido, "recurso-ajeno", null).ConfigureAwait(false);
        }

        return Invalido(Nombre(clave));
    }

    private static async Task<List<(string, string)>?> LeerJson(HttpRequest solicitud, CancellationToken cancelacion)
    {
        try
        {
            using var documento = await JsonDocument.ParseAsync(solicitud.Body, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2 }, cancelacion).ConfigureAwait(false);
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var pares = new List<(string, string)>();
            foreach (var propiedad in documento.RootElement.EnumerateObject())
            {
                switch (propiedad.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        pares.Add((propiedad.Name, propiedad.Value.GetString() ?? string.Empty));
                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        pares.Add((propiedad.Name, propiedad.Value.GetRawText()));
                        break;
                    default:
                        return null;
                }
            }

            return pares;
        }
        catch (Exception falla) when (falla is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return null;
        }
    }

    /// <summary>El nombre de un campo solo si es un identificador corto y seguro; si no, un nombre genérico (nunca se repite lo recibido).</summary>
    private static string Nombre(string campo) => NombreSeguro().IsMatch(campo) ? campo : "campo";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex NombreSeguro();

    // ---------- Valores ----------

    public static bool TryGuid(IReadOnlyDictionary<string, string> campos, string campo, out Guid valor)
    {
        valor = Guid.Empty;
        return campos.TryGetValue(campo, out var texto) && Guid.TryParseExact(texto, "D", out valor) && valor != Guid.Empty;
    }

    /// <summary>Un entero en notación decimal simple (sin signo «+», separadores ni exponente), dentro del rango.</summary>
    public static bool TryEntero(IReadOnlyDictionary<string, string> campos, string campo, int minimo, int maximo, out int valor)
    {
        valor = 0;
        return campos.TryGetValue(campo, out var texto)
            && texto.Length is > 0 and <= 9
            && texto.All(char.IsAsciiDigit)
            && int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor)
            && valor >= minimo
            && valor <= maximo;
    }
}
