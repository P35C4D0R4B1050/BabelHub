using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Los motivos del catálogo de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDeTraslados
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string FueraDeSucursal = "fuera-de-sucursal";

    public const string SinSesion = "sin-sesion";
}

/// <summary>
/// El alcance de cada caso de uso de <c>Traslados</c>, comprobado en la Aplicación (AGENTS.md §5, TRA-09), con la matriz de <see cref="PermisosDeTraslado"/> (solo rol y sucursal): el origen aprueba, rechaza, cancela y despacha, el
/// destino recibe y solicita, logística registra la guía y consulta, la administración central todo lo permitido, y cliente y cualquier otro rol nada. Un rechazo deja <b>un único evento de acceso denegado</b> (fuera de la
/// transacción del caso de uso, así que persiste aunque esta se revierta). Sin actor, o con un visitante, es «no autenticado»: nunca se concede por omisión (SEG-15). «Vinculado» y el estado no son permisos: son reglas de negocio
/// del Dominio (422 y 409 sin evento de seguridad).
/// </summary>
internal sealed class AutorizadorDeTraslados(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public const string ClaveProhibido = "prohibido";

    public const string ClaveNoAutenticado = "no-autenticado";

    /// <summary>El actor con contexto, o <see langword="null"/> si no hay contexto o es un visitante.</summary>
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>El usuario responsable (con identificador): quien opera un traslado es siempre una persona del personal.</summary>
    public Guid? UsuarioId => Actor is { Tipo: TipoDeActor.Usuario, UsuarioId: { } id } && id != Guid.Empty ? id : null;

    /// <summary>El rol con el que se decide el permiso: todo lo que no es un usuario con un rol conocido del personal es <see cref="RolDeTraslado.Otro"/>.</summary>
    public RolDeTraslado Rol
    {
        get
        {
            if (Actor is not { Tipo: TipoDeActor.Usuario } actor || UsuarioId is null)
            {
                return RolDeTraslado.Otro;
            }

            return actor.Rol switch
            {
                RolesDelActor.AdministradorCentral => RolDeTraslado.AdministracionCentral,
                RolesDelActor.EncargadoDeSucursal => RolDeTraslado.EncargadoDeSucursal,
                RolesDelActor.PersonalDeLogistica => RolDeTraslado.Logistica,
                RolesDelActor.Cliente => RolDeTraslado.Cliente,
                _ => RolDeTraslado.Otro,
            };
        }
    }

    /// <summary>La sucursal del encargado (un identificador vacío no cuenta).</summary>
    public Guid? Sucursal => Actor?.SucursalId is { } s && s != Guid.Empty ? s : null;

    /// <summary>
    /// Lo primero de toda operación: el rol puede intentarla en alguna sucursal. Un cliente, logística fuera de la guía y la consulta o cualquier otro rol reciben el rechazo sin que el sistema lea el traslado: no averiguan si existe
    /// (un 403 con un único evento).
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirRol(OperacionDeTraslado operacion, CancellationToken cancelacion) =>
        Evaluar(() => PermisosDeTraslado.RolPuedeIntentar(operacion, Rol) ? null : MotivosDeDenegacionDeTraslados.FueraDeRol, cancelacion);

    /// <summary>La operación sobre un traslado de <paramref name="origenId"/> a <paramref name="destinoId"/> según rol y sucursal (un único evento si se deniega).</summary>
    public Task<ErrorDeAplicacion?> Exigir(OperacionDeTraslado operacion, Guid origenId, Guid destinoId, CancellationToken cancelacion) =>
        Evaluar(
            () =>
            {
                var decision = PermisosDeTraslado.Evaluar(operacion, Rol, Sucursal, origenId, destinoId);
                return decision.Permitido ? null : decision.Motivo == MotivoDeDenegacionDeTraslado.FueraDeSucursal ? MotivosDeDenegacionDeTraslados.FueraDeSucursal : MotivosDeDenegacionDeTraslados.FueraDeRol;
            },
            cancelacion);

    /// <summary>
    /// El traslado que no se ve: para un encargado, el mismo 403 con un único evento que para uno de otra sucursal (un id inexistente y uno ajeno no se distinguen, AM04); administración y logística ven toda la red y reciben el
    /// 404.
    /// </summary>
    public async Task<ErrorDeAplicacion> TrasladoNoVisible(CancellationToken cancelacion)
    {
        if (Rol != RolDeTraslado.EncargadoDeSucursal)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeTraslados.TrasladoNoEncontrado);
        }

        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeTraslados.FueraDeSucursal, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }

    /// <summary>El resultado «denegado por alcance» de <c>Aplicar</c> o de <c>IReservasParaTraslados</c> (la política de filas) se traduce a «prohibido» con su único evento; quien llama devuelve el fallo y la transacción se revierte.</summary>
    public async Task<ErrorDeAplicacion> DenegadoPorAlcance(CancellationToken cancelacion)
    {
        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeTraslados.FueraDeSucursal, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }

    private async Task<ErrorDeAplicacion?> Evaluar(Func<string?> motivoDelRechazo, CancellationToken cancelacion)
    {
        if (Actor is null)
        {
            await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeTraslados.SinSesion, ruta: null) { CodigoDeError = "INV-NO-AUTENTICADO" }, cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClaveNoAutenticado);
        }

        var motivo = motivoDelRechazo();
        if (motivo is null)
        {
            return null;
        }

        await denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }
}
