using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

/// <summary>Los motivos del catálogo de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDeAjustes
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string FueraDeSucursal = "fuera-de-sucursal";

    public const string SinSesion = "sin-sesion";
}

/// <summary>
/// El alcance de cada caso de uso de <c>Ajustes</c>, comprobado en la Aplicación (AGENTS.md §5, AJU-09, AJU-14): el encargado ajusta, reingresa y consulta solo la fuente de su sucursal; la administración central, cualquiera;
/// logística y cliente no tienen ningún permiso; el ingreso de mercadería y su lista son solo de la administración central. Un rechazo deja <b>un evento de acceso denegado</b> (fuera de la transacción del caso de uso, así
/// que persiste aunque esta se revierta). Sin actor o con un visitante es «no autenticado»: nunca se concede por omisión (SEG-15).
/// </summary>
internal sealed class AutorizadorDeAjustes(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public const string ClaveProhibido = "prohibido";

    public const string ClaveNoAutenticado = "no-autenticado";

    /// <summary>El actor con contexto, o <see langword="null"/> si no hay contexto o es un visitante.</summary>
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>El usuario responsable (con identificador): quien ajusta es siempre una persona del personal.</summary>
    public Guid? UsuarioId => Actor is { Tipo: TipoDeActor.Usuario, UsuarioId: { } id } && id != Guid.Empty ? id : null;

    /// <summary>El rol que pide un ajuste, un reingreso o el historial: administración central o encargado de sucursal (logística y cliente, rechazados). La fuente se comprueba aparte con <see cref="ExigirFuente"/>.</summary>
    public Task<ErrorDeAplicacion?> ExigirAdministracionOEncargado(CancellationToken cancelacion) =>
        Evaluar(actor => EsUsuario(actor) && (actor.EsAdministracionCentral() || actor.EsEncargado()) ? null : MotivosDeDenegacionDeAjustes.FueraDeRol, cancelacion);

    /// <summary>Solo la administración central (ingreso de mercadería y su lista, AJU-14, AJU-20).</summary>
    public Task<ErrorDeAplicacion?> ExigirAdministracionCentral(CancellationToken cancelacion) =>
        Evaluar(actor => EsUsuario(actor) && actor.EsAdministracionCentral() ? null : MotivosDeDenegacionDeAjustes.FueraDeRol, cancelacion);

    /// <summary>
    /// La administración central, o el encargado cuya sucursal es <paramref name="fuenteId"/> (AJU-09). Una fuente inexistente (<see langword="null"/>) solo la ve la administración: a un encargado se le rechaza igual que si fuera
    /// de otra sucursal, para que el 403 llegue antes que el 404 y no haya forma de preguntar qué códigos existen.
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirFuente(Guid? fuenteId, CancellationToken cancelacion) =>
        Evaluar(
            actor =>
            {
                if (!EsUsuario(actor))
                {
                    return MotivosDeDenegacionDeAjustes.FueraDeRol;
                }

                if (actor.EsAdministracionCentral())
                {
                    return null;
                }

                if (!actor.EsEncargado())
                {
                    return MotivosDeDenegacionDeAjustes.FueraDeRol;
                }

                return fuenteId is { } fuente && actor.EsEncargadoDe(fuente) ? null : MotivosDeDenegacionDeAjustes.FueraDeSucursal;
            },
            cancelacion);

    /// <summary>El resultado «denegado por alcance» de <c>Aplicar</c> (la política de filas) se traduce a «prohibido» con su evento.</summary>
    public async Task<ErrorDeAplicacion> DenegadoPorAlcance(CancellationToken cancelacion)
    {
        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeAjustes.FueraDeSucursal, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }

    private static bool EsUsuario(ActorActual actor) => actor.Tipo == TipoDeActor.Usuario && actor.UsuarioId is { } id && id != Guid.Empty;

    private async Task<ErrorDeAplicacion?> Evaluar(Func<ActorActual, string?> motivoDelRechazo, CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeAjustes.SinSesion, ruta: null) { CodigoDeError = "INV-NO-AUTENTICADO" }, cancelacion).ConfigureAwait(false);
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
