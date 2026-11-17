using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// El rol de la administración central. La Aplicación no referencia <c>Shared</c> (regla 6), así que repite el valor de <c>RolesDeUsuario.AdministradorCentral</c>; una prueba de arquitectura
/// (<c>ReglasDelCatalogoTests</c>) compara ambos y falla si divergen.
/// </summary>
internal static class RolesDelCatalogo
{
    public const string AdministradorCentral = "administrador-central";

    /// <summary>Un usuario con sesión y el rol de administración central; un visitante, el sistema, una integración o un usuario sin identificador no lo son.</summary>
    public static bool EsAdministradorCentral(ActorActual? actor) =>
        actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null } && string.Equals(actor.Rol, AdministradorCentral, StringComparison.Ordinal);
}

/// <summary>Los motivos del catálogo de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDelCatalogo
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    /// <summary>Un administrador que intenta reintentar el ingreso pendiente de otro (el ámbito de su clave incluye al usuario).</summary>
    public const string RecursoAjeno = "recurso-ajeno";
}

/// <summary>
/// El alcance de cada caso de uso de administración del módulo, comprobado en la Aplicación y no solo en la Api (AGENTS.md §5, CAT-14, CAT-23): publicar, editar y dar de baja obras y ediciones, subir
/// archivos y registrar ingresos son solo de la administración central. Un rechazo deja <b>un evento de acceso denegado</b> por <see cref="IDenegacionDeAcceso"/> (fuera de la transacción del caso de
/// uso, así que persiste aunque esta se revierta; un solo evento por solicitud) y devuelve el error con su categoría. Sin actor (nadie estableció el contexto) o con un visitante, el resultado es
/// «no autenticado»: nunca se concede por omisión (SEG-15). El rol se comprueba <b>antes</b> de leer el cuerpo, de buscar una fila o de validar: un rechazo no revela nada de lo que se pidió.
/// </summary>
internal sealed class AutorizadorDelCatalogo(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public const string ClaveProhibido = "prohibido";

    public const string ClaveNoAutenticado = "no-autenticado";

    /// <summary>El actor de la solicitud, o <see langword="null"/> si no hay contexto o es un visitante.</summary>
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>El identificador del administrador central actual; solo tiene valor después de <see cref="ExigirAdministracionCentral"/> sin rechazo.</summary>
    public Guid? UsuarioId => RolesDelCatalogo.EsAdministradorCentral(Actor) ? Actor!.UsuarioId : null;

    /// <summary>Solo la administración central. Devuelve el error (y deja el evento) si no lo es, o <see langword="null"/> si puede seguir.</summary>
    public async Task<ErrorDeAplicacion?> ExigirAdministracionCentral(CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await Denegar(MotivosDeDenegacionDelCatalogo.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClaveNoAutenticado);
        }

        if (RolesDelCatalogo.EsAdministradorCentral(actor))
        {
            return null;
        }

        return await Prohibir(MotivosDeDenegacionDelCatalogo.FueraDeRol, cancelacion).ConfigureAwait(false);
    }

    /// <summary>Rechaza un recurso que existe pero es de otro usuario (el reintento de un ingreso pendiente de otro administrador): acceso denegado registrado y «prohibido».</summary>
    public Task<ErrorDeAplicacion> RechazarRecursoAjeno(CancellationToken cancelacion) => Prohibir(MotivosDeDenegacionDelCatalogo.RecursoAjeno, cancelacion);

    private async Task<ErrorDeAplicacion> Prohibir(string motivo, CancellationToken cancelacion)
    {
        await Denegar(motivo, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-…</c> (plan §4).</summary>
internal static class ClavesDelCatalogo
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string ObraNoEncontrada = "obra-no-encontrada";

    public const string EdicionNoEncontrada = "edicion-no-encontrada";

    /// <summary>ISBN repetido (409): el campo <c>tituloExistente</c> lleva el título que ya lo usa, solo tras comprobar el rol.</summary>
    public const string IsbnDuplicado = "isbn-duplicado";

    public const string LicenciaInvalida = "licencia-invalida";

    public const string ReferenciaDeArchivoInvalida = "referencia-de-archivo-invalida";

    public const string EdicionDuplicadaPorTipo = "edicion-duplicada-por-tipo";

    /// <summary>La versión de la obra que leyó quien edita ya no es la actual (409).</summary>
    public const string ConflictoDeVersion = "conflicto-de-version";

    public const string ObraSinEdiciones = "obra-sin-ediciones";

    public const string BusquedaDemasiadoAmplia = "busqueda-demasiado-amplia";

    public const string ArchivoInvalido = "archivo-invalido";

    public const string ArchivoDemasiadoGrande = "archivo-demasiado-grande";

    public const string ClaveDeIngresoInvalida = "clave-de-ingreso-invalida";

    public const string IngresoPendiente = "ingreso-pendiente";

    public const string IngresoNoEncontrado = "ingreso-no-encontrado";

    /// <summary>La fila de ingreso ya no está <c>pendiente</c> (409).</summary>
    public const string IngresoYaResuelto = "ingreso-ya-resuelto";

    public const string ReglaDeNegocio = "regla-de-negocio-incumplida";

    public const string AlmacenNoDisponible = "almacen-no-disponible";

    public const string InventarioNoDisponible = "inventario-no-disponible";

    public const string ObraDadaDeBaja = "obra-dada-de-baja";

    public const string EdicionDadaDeBaja = "edicion-dada-de-baja";

    /// <summary>La operación (licencia, archivo, retiro) es solo de una edición digital (validación, plan §4).</summary>
    public const string SoloEdicionDigital = "solo-edicion-digital";

    public const string EdicionYaRetirada = "edicion-ya-retirada";
}

internal static class FallosDelCatalogo
{
    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> Validacion<T>(ResultadoDeValidacion validacion) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCatalogo.ValidacionFallida, validacion.Errores));

    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));
}
