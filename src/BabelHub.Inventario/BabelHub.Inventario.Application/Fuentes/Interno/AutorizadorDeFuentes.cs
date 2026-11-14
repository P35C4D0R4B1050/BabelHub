using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>Los motivos del catálogo de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDeFuentes
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string FueraDeSucursal = "fuera-de-sucursal";

    public const string SinSesion = "sin-sesion";
}

/// <summary>
/// El alcance de cada caso de uso del módulo, comprobado en la Aplicación y no solo en la Api (AGENTS.md §5, FUE-08): quién puede pedir qué. Un rechazo deja
/// <b>un evento de acceso denegado</b> por <see cref="IDenegacionDeAcceso"/> (que se escribe fuera de la transacción del caso de uso, así que persiste aunque
/// esta se revierta) y devuelve el error con su categoría. Sin actor (nadie estableció el contexto) o con un visitante, el resultado es «no autenticado»: nunca
/// se concede por omisión (SEG-15). Un <c>SucursalId</c> nulo no es un comodín: el alcance se decide por rol.
/// </summary>
internal sealed class AutorizadorDeFuentes(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public const string ClaveProhibido = "prohibido";

    public const string ClaveNoAutenticado = "no-autenticado";

    /// <summary>Solo la administración central (registrar y editar fuentes, rutas y fichas, designar la recepción, consultar una fuente).</summary>
    public Task<ErrorDeAplicacion?> ExigirAdministracionCentral(CancellationToken cancelacion) =>
        Evaluar(actor => actor.EsAdministracionCentral() ? null : MotivosDeDenegacionDeFuentes.FueraDeRol, cancelacion);

    /// <summary>La administración central o un encargado de sucursal (rutas y lista de fuentes); logística y cliente quedan fuera.</summary>
    public Task<ErrorDeAplicacion?> ExigirAdministracionOEncargado(CancellationToken cancelacion) =>
        Evaluar(actor => actor.EsAdministracionCentral() || actor.EsEncargado() ? null : MotivosDeDenegacionDeFuentes.FueraDeRol, cancelacion);

    /// <summary>
    /// La administración central, o el encargado cuya sucursal es <paramref name="fuenteId"/> (FUE-08). <paramref name="fuenteId"/> nulo (la fuente pedida no existe)
    /// solo lo puede ver la administración central: a un encargado se le rechaza igual que si fuera de otra sucursal, para que el 403 llegue antes que el 404
    /// y no haya forma de preguntar qué códigos existen.
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirFuente(Guid? fuenteId, CancellationToken cancelacion) =>
        Evaluar(
            actor =>
            {
                if (actor.EsAdministracionCentral())
                {
                    return null;
                }

                if (!actor.EsEncargado())
                {
                    return MotivosDeDenegacionDeFuentes.FueraDeRol;
                }

                return fuenteId is { } fuente && actor.EsEncargadoDe(fuente) ? null : MotivosDeDenegacionDeFuentes.FueraDeSucursal;
            },
            cancelacion);

    /// <summary>El servicio de avisos (integración o sistema) o la administración central: lo que pide el contacto de notificación de una sucursal.</summary>
    public Task<ErrorDeAplicacion?> ExigirServicioDeAvisosOAdministracion(CancellationToken cancelacion) =>
        Evaluar(
            actor => actor.Tipo is TipoDeActor.Integracion or TipoDeActor.Sistema || actor.EsAdministracionCentral() ? null : MotivosDeDenegacionDeFuentes.FueraDeRol,
            cancelacion);

    /// <summary>El actor de la solicitud, o <see langword="null"/> si no hay contexto o es un visitante (el llamador ya recibió el error de autenticación).</summary>
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    private async Task<ErrorDeAplicacion?> Evaluar(Func<ActorActual, string?> motivoDelRechazo, CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeFuentes.SinSesion, ruta: null) { CodigoDeError = "INV-NO-AUTENTICADO" }, cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClaveNoAutenticado);
        }

        var motivo = motivoDelRechazo(actor);
        if (motivo is null)
        {
            return null;
        }

        await denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }
}
