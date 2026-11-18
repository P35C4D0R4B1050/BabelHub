using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Catalogo;

/// <summary>
/// La traducción de los fallos del Catálogo a los códigos <c>ECO-…</c> y los estados HTTP de <c>plan.md</c> §4 (CAT-14, AC-CAT-11): la Aplicación devuelve una categoría y una clave simbólica; aquí se decide el código estable y, para
/// <c>archivo-demasiado-grande</c>, el estado 413 (la categoría de negocio daría 422). El motivo se expone solo en lo que el plan publica (un conflicto de ISBN lleva <c>tituloExistente</c>; un archivo inválido, su motivo); el resto lleva el mensaje genérico.
/// Nunca lleva trazas ni datos internos (SEG-03).
/// </summary>
public static class ProblemasDelCatalogo
{
    /// <summary>Lo que la Api publica de una clave: el sufijo del código (<c>ECO-…</c>), un estado que reemplaza al de la categoría y si se exponen los campos del error.</summary>
    private sealed record Publicada(string Sufijo, int? Estado = null, bool ExponeCampos = false);

    private static readonly Dictionary<string, Publicada> PorClave = new(StringComparer.Ordinal)
    {
        ["obra-no-encontrada"] = new("NO-ENCONTRADO"),
        ["edicion-no-encontrada"] = new("NO-ENCONTRADO"),
        ["ingreso-no-encontrado"] = new("NO-ENCONTRADO"),
        ["isbn-duplicado"] = new("ISBN-DUPLICADO", ExponeCampos: true),
        ["licencia-invalida"] = new("LICENCIA-INVALIDA", ExponeCampos: true),
        ["referencia-de-archivo-invalida"] = new("REFERENCIA-DE-ARCHIVO-INVALIDA"),
        ["edicion-duplicada-por-tipo"] = new("EDICION-DUPLICADA-POR-TIPO"),
        ["conflicto-de-version"] = new("CONFLICTO"),
        ["ingreso-ya-resuelto"] = new("CONFLICTO"),
        ["obra-sin-ediciones"] = new("OBRA-SIN-EDICIONES"),
        ["busqueda-demasiado-amplia"] = new("BUSQUEDA-DEMASIADO-AMPLIA"),
        ["archivo-invalido"] = new("ARCHIVO-INVALIDO", ExponeCampos: true),
        ["archivo-demasiado-grande"] = new("ARCHIVO-DEMASIADO-GRANDE", StatusCodes.Status413PayloadTooLarge),
        ["clave-de-ingreso-invalida"] = new("CLAVE-INVALIDA"),
        ["ingreso-pendiente"] = new("INGRESO-PENDIENTE"),
        ["almacen-no-disponible"] = new("SERVICIO-NO-DISPONIBLE"),
        ["inventario-no-disponible"] = new("SERVICIO-NO-DISPONIBLE"),
    };

    /// <summary>Lo que se sugiere esperar (<c>Retry-After</c>, en segundos) cuando una subida no tiene cupo.</summary>
    public const int EsperaDeSubidasEnSegundos = 30;

    public static ProblemaDto Crear(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(error);

        var generico = ProblemasHttp.Crear(contexto, opciones, error.Categoria, error.Clave, error.Errores);
        if (!PorClave.TryGetValue(error.Clave, out var publicada))
        {
            return generico;
        }

        // Solo se publican los campos de la clave y los de una categoría que ya los expone (la validación); un conflicto no los expone por categoría.
        var errores = publicada.ExponeCampos || error.Categoria.ExponeElMotivo()
            ? error.Errores.Select(e => new ErrorDeCampoDto(e.Campo, e.Clave)).ToList()
            : [];
        return generico with
        {
            Code = CodigosDeError.De(opciones.Servicio, publicada.Sufijo),
            Status = publicada.Estado ?? generico.Status,
            Errores = errores,
        };
    }

    /// <summary>Escribe el fallo: el problema uniforme y, si es una subida sin cupo, <c>Retry-After</c>. El acceso denegado ya lo registró la Aplicación (un evento por solicitud).</summary>
    public static async Task Escribir(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        var problema = Crear(contexto, opciones, error);
        var cabeceras = error.Categoria == CategoriaDeError.Limite
            ? new Dictionary<string, string> { ["Retry-After"] = EsperaDeSubidasEnSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            : null;
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        await ProblemasHttp.Escribir(contexto, problema, cabeceras).ConfigureAwait(false);
    }
}
