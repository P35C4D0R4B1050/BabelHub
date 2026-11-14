using System.Text.Json;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;

namespace BabelHub.Inventario.Api.Fuentes;

/// <summary>
/// Los códigos de error específicos del módulo (<c>docs/03/08</c>, D11) que la Api traduce desde las claves simbólicas de la Aplicación. <c>INV-RUTA-INVALIDA</c> es una regla de negocio (422) y la declara
/// <see cref="Especificos"/> en <see cref="OpcionesDeErrores"/>; los conflictos (<c>INV-FUENTE-DUPLICADA</c> e <c>INV-FORMATO-EN-CONFLICTO</c>, 409) no exponen su clave en el manejo común (solo la validación,
/// la regla de negocio y el límite lo hacen), así que se traducen aquí, por la clave, sin tocar el archivo de la plataforma.
/// </summary>
public static class ErroresDeFuentes
{
    /// <summary>Los códigos específicos de las reglas de negocio de Fuentes, para <see cref="OpcionesDeErrores"/>.</summary>
    public static IReadOnlyDictionary<string, MensajeEspecifico> Especificos { get; } = new Dictionary<string, MensajeEspecifico>
    {
        ["ruta-invalida"] = new(CodigosDeErrorDelInventario.RutaInvalida, "El plazo debe ser mayor que cero y la ruta debe unir dos fuentes distintas."),
    };

    /// <summary>El problema de un fallo de la Aplicación, con el código específico de los conflictos del módulo.</summary>
    public static ProblemaDto Problema(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var problema = ProblemasHttp.Crear(contexto, opciones, error);

        return error.Clave switch
        {
            "fuente-duplicada" => problema with { Code = CodigosDeErrorDelInventario.FuenteDuplicada, Detail = "Ya existe una fuente con ese código o con ese nombre." },
            "formato-en-conflicto" => problema with { Code = CodigosDeErrorDelInventario.FormatoEnConflicto, Detail = "La edición tiene stock y no puede cambiar de formato." },
            _ => problema,
        };
    }

    /// <summary>La edición no es impresa: no tiene stock físico (422, FUE-07).</summary>
    public static ProblemaDto EdicionNoImpresa(HttpContext contexto, OpcionesDeErrores opciones) =>
        ProblemasHttp.Crear(contexto, opciones, CategoriaDeError.ReglaDeNegocio) with
        {
            Code = CodigosDeErrorDelInventario.EdicionNoImpresa,
            Detail = "La edición no es impresa: no tiene stock físico.",
        };

    /// <summary>Escribe el error y devuelve un resultado vacío (la respuesta ya se escribió).</summary>
    public static async Task<IResult> Escribir(HttpContext contexto, OpcionesDeErrores opciones, ProblemaDto problema)
    {
        await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
        return Results.Empty;
    }

    public static Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error) => Escribir(contexto, opciones, Problema(contexto, opciones, error));

    public static Task<IResult> Validacion(HttpContext contexto, OpcionesDeErrores opciones, string campo, string clave) =>
        Falla(contexto, opciones, new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, clave)]));
}

/// <summary>
/// Lee el cuerpo JSON con un <b>esquema estricto</b> (plan §6): un campo desconocido (por ejemplo <c>id</c> o <c>esFuenteDeRecepcion</c> al registrar una fuente) se rechaza como validación, igual que un cuerpo
/// ausente, mal formado o que no sea JSON. Nunca devuelve el valor recibido.
/// </summary>
public static class CuerpoEstricto
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static async Task<(T? Valor, string? Clave)> Leer<T>(HttpContext contexto)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(contexto);

        try
        {
            var valor = await contexto.Request.ReadFromJsonAsync<T>(Opciones, contexto.RequestAborted).ConfigureAwait(false);
            return valor is null ? (null, "obligatorio") : (valor, null);
        }
        catch (JsonException)
        {
            return (null, "formato-invalido");
        }
        catch (InvalidOperationException)
        {
            // Sin Content-Type JSON o sin cuerpo.
            return (null, "formato-invalido");
        }
        catch (BadHttpRequestException)
        {
            return (null, "formato-invalido");
        }
    }
}
