using System.Text.Json;
using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Api.Catalogo;

// Los cuerpos de la administración del catálogo (plan §4). Son contratos de la web con el servidor, no una API pública: cada uno rechaza los campos que no declara (un campo desconocido es validación,
// nunca se ignora en silencio) y los números, las fechas y los enumerados tienen una sola forma. Lo que falta no toma un valor por omisión: un campo obligatorio ausente es validación.

public sealed record LicenciaHttp(TipoDeLicenciaDeEdicion? Tipo, string? Titular, string[]? Territorios, DateOnly? VigenteHasta, FormatoDeArchivoDigital? Formato);

public sealed record EdicionHttp(FormatoDeEdicion? Formato, string? Isbn, decimal? Precio, LicenciaHttp? Licencia, Guid? ArchivoRef, int? StockInicial);

public sealed record RegistrarObraHttp(string? Titulo, string? Autor, string? Editorial, string? Categoria, Guid? PortadaRef, EdicionHttp[]? Ediciones);

public sealed record EditarObraHttp(long? Version, string? Titulo, string? Autor, string? Editorial, string? Categoria, Guid? PortadaRef);

public sealed record VersionHttp(long? Version);

public sealed record RegistrarEdicionHttp(long? Version, EdicionHttp? Edicion);

public sealed record EditarEdicionHttp(long? Version, decimal? Precio, LicenciaHttp? Licencia, Guid? ArchivoRef);

public sealed record RetirarHttp(string? Motivo);

public sealed record RegistrarIngresoHttp(int? Cantidad, string? Referencia, string? Nota, string? Token)
{
    /// <summary>El token firmado nunca se imprime (SEG-10).</summary>
    public override string ToString() => $"RegistrarIngresoHttp {{ Cantidad = {Cantidad} }}";
}

public sealed record ConciliarHttp(ResolucionDeIngreso? Resultado, bool? ConfirmaFallido);

/// <summary>Lee el cuerpo JSON con esquema estricto y traduce los cuerpos a las entradas de la Aplicación; un campo obligatorio ausente se informa como validación con su nombre.</summary>
public static class CuerpoDelCatalogo
{
    public static readonly JsonSerializerOptions Opciones = Crear();

    private static JsonSerializerOptions Crear()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
        };
        opciones.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return opciones;
    }

    /// <summary>El cuerpo o <see langword="null"/> si falta, está mal formado o trae un campo desconocido (nunca se devuelve lo recibido).</summary>
    public static async Task<T?> Leer<T>(HttpRequest solicitud, CancellationToken cancelacion)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (!solicitud.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await solicitud.ReadFromJsonAsync<T>(Opciones, cancelacion).ConfigureAwait(false);
        }
        catch (Exception falla) when (falla is JsonException or InvalidOperationException or BadHttpRequestException or NotSupportedException)
        {
            return null;
        }
    }

    public static ErrorDeAplicacion CuerpoInvalido { get; } = new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cuerpo", "formato-invalido")]);

    public static ErrorDeAplicacion Falta(string campo) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "obligatorio")]);

    public static LicenciaDeEdicionEntrada? ALicencia(LicenciaHttp? licencia, out ErrorDeAplicacion? error)
    {
        error = null;
        if (licencia is null)
        {
            return null;
        }

        if (licencia.Tipo is null || licencia.Formato is null)
        {
            error = Falta(licencia.Tipo is null ? "licencia.tipo" : "licencia.formato");
            return null;
        }

        return new LicenciaDeEdicionEntrada(licencia.Tipo.Value, licencia.Titular, licencia.Territorios, licencia.VigenteHasta, licencia.Formato.Value);
    }

    public static NuevaEdicionEntrada? AEdicion(EdicionHttp? edicion, out ErrorDeAplicacion? error)
    {
        error = null;
        if (edicion is null)
        {
            error = Falta("edicion");
            return null;
        }

        if (edicion.Formato is null || edicion.Precio is null)
        {
            error = Falta(edicion.Formato is null ? "formato" : "precio");
            return null;
        }

        var licencia = ALicencia(edicion.Licencia, out error);
        return error is not null ? null : new NuevaEdicionEntrada(edicion.Formato.Value, edicion.Isbn, edicion.Precio.Value, licencia, edicion.ArchivoRef, edicion.StockInicial);
    }
}
