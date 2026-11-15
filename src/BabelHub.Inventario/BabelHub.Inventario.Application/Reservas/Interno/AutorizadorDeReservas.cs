using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Los motivos del catálogo de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDeReservas
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    public const string RecursoAjeno = "recurso-ajeno";

    public const string FueraDeSucursal = "fuera-de-sucursal";
}

/// <summary>
/// El alcance de los casos de uso de <c>Reservas</c>, comprobado en la Aplicación (AGENTS.md §5, D16): quien reserva es un <b>cliente</b> y su identificador sale del actor verificado, nunca del cuerpo. Un rechazo deja
/// <b>un evento</b> por <see cref="IDenegacionDeAcceso"/> (fuera de la transacción del caso de uso, así que persiste aunque esta se revierta). Sin actor o con un visitante el resultado es «no autenticado»: nunca se concede
/// por omisión (SEG-15); el alcance de datos de la transacción tampoco ve nada sin actor, así que dejar pasar daría un «sin stock» silencioso.
/// </summary>
internal sealed class AutorizadorDeReservas(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public const string ClaveProhibido = "prohibido";

    public const string ClaveNoAutenticado = "no-autenticado";

    /// <summary>El actor con contexto, o <see langword="null"/> si no hay contexto o es un visitante.</summary>
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    /// <summary>Solo un cliente (con su identificador de usuario): reservar y consultar su reserva.</summary>
    public Task<ErrorDeAplicacion?> ExigirCliente(CancellationToken cancelacion) =>
        Evaluar(actor => actor.Tipo == TipoDeActor.Usuario && actor.Rol == RolesDelActor.Cliente && actor.UsuarioId is { } id && id != Guid.Empty ? null : MotivosDeDenegacionDeReservas.FueraDeRol, cancelacion);

    /// <summary>
    /// Solo el actor «sistema»: la expiración la hace el propio Inventario por su cuenta, con el actor explícito del ejecutor de trabajos de fondo (D08). Un trabajo que corre sin actor responde «no autenticado» y no
    /// toca nada: el alcance de datos sin actor devolvería cero filas y la expiración pasaría por «nada que expirar» sin error (hallazgo del 2026-11-14).
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirSistema(CancellationToken cancelacion) =>
        Evaluar(actor => actor.Tipo == TipoDeActor.Sistema ? null : MotivosDeDenegacionDeReservas.FueraDeRol, cancelacion);

    /// <summary>
    /// Solo la integración <c>expiracion-de-reservas</c> (sin token de usuario): confirmar, extender y liberar son llamadas de servicio del E-commerce. El alcance de la credencial lo verifica la Api; aquí se exige el actor
    /// de integración con ese nombre. Un cliente o cualquier otra integración es «prohibido» con su evento.
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirIntegracionDeReservas(CancellationToken cancelacion) => ExigirIntegracion(AlcanceDeServicio.ExpiracionDeReservas, cancelacion);

    /// <summary>
    /// Solo la integración con el alcance dado, sin token de usuario (la consulta de pendientes y la baja del titular tienen cada una el suyo, SEL-33, SEL-34). El alcance de la credencial lo verifica la Api; aquí se exige el
    /// actor de integración con ese nombre: un cliente, un usuario o la integración de otro alcance es «prohibido» con su evento, antes de leer nada.
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirIntegracion(AlcanceDeServicio alcance, CancellationToken cancelacion) =>
        Evaluar(actor => actor.Tipo == TipoDeActor.Integracion && actor.Integracion == alcance.NombreEstable() ? null : MotivosDeDenegacionDeReservas.FueraDeRol, cancelacion);

    /// <summary>Consultar una reserva: el cliente (la dueña, lo comprueba el caso de uso) o la administración central; el personal de sucursal y de logística reciben «prohibido» antes de leer nada (SEL-20).</summary>
    public Task<ErrorDeAplicacion?> ExigirClienteOAdministracion(CancellationToken cancelacion) =>
        Evaluar(
            actor => actor.EsAdministracionCentral() || (actor.Tipo == TipoDeActor.Usuario && actor.Rol == RolesDelActor.Cliente && actor.UsuarioId is { } id && id != Guid.Empty)
                ? null
                : MotivosDeDenegacionDeReservas.FueraDeRol,
            cancelacion);

    /// <summary>
    /// El rol que cerrar exige, <b>antes de leer nada</b> (SEL-39): la entrega la hace un encargado de sucursal o la administración central; el paso a en reparto, el personal de logística o la administración central. Quien no
    /// tiene ese rol recibe «prohibido» con su evento sin que se mire la reserva, la clave ni <c>ya-cerrada</c>.
    /// </summary>
    public Task<ErrorDeAplicacion?> ExigirRolParaCerrar(MotivoDeCierreDeReserva motivo, CancellationToken cancelacion) =>
        Evaluar(
            actor => actor.EsAdministracionCentral()
                || (motivo == MotivoDeCierreDeReserva.Entrega && actor.EsEncargado())
                || (motivo == MotivoDeCierreDeReserva.Reparto && actor.Tipo == TipoDeActor.Usuario && actor.Rol == RolesDelActor.PersonalDeLogistica)
                ? null
                : MotivosDeDenegacionDeReservas.FueraDeRol,
            cancelacion);

    /// <summary>
    /// La entrega solo en la sucursal de recojo de la reserva (SEL-39): un encargado de otra sucursal, o que consulta una reserva que no es de recojo o que no existe, recibe «prohibido» con el evento
    /// <c>fuera-de-sucursal</c> y no se le revela si la reserva existe. La administración central pasa siempre.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirSucursalDeRecojo(Guid? sucursalDeRecojoId, CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is not null && (actor.EsAdministracionCentral() || (sucursalDeRecojoId is { } sucursal && actor.EsEncargadoDe(sucursal))))
        {
            return null;
        }

        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeReservas.FueraDeSucursal, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }

    /// <summary>Quien puede ver que una reserva no existe: la administración y logística; un encargado recibe «prohibido» como si fuera de otra sucursal.</summary>
    public bool PuedeSaberSiExiste => Actor is { } actor && !actor.EsEncargado();

    /// <summary>Cualquier actor con contexto (la consulta previa sin token la hace el E-commerce con su credencial de servicio): no un visitante ni la ausencia de actor.</summary>
    public Task<ErrorDeAplicacion?> ExigirContexto(CancellationToken cancelacion) => Evaluar(_ => null, cancelacion);

    /// <summary>Registra el acceso a un pedido de otro cliente como «no encontrado» con su evento (D16): no revela que existe.</summary>
    public async Task<ErrorDeAplicacion> PedidoAjeno(CancellationToken cancelacion)
    {
        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeReservas.RecursoAjeno, ruta: null) { CodigoDeError = "INV-NO-ENCONTRADO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "reserva-no-encontrada");
    }

    /// <summary>Una cuenta cancelada no reserva (SEL-35): «prohibido» con el evento, sin revelar nada más.</summary>
    public async Task<ErrorDeAplicacion> TitularDadoDeBaja(CancellationToken cancelacion)
    {
        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeReservas.FueraDeRol, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeReservas.TitularDadoDeBaja);
    }

    /// <summary>El resultado «denegado por alcance» de <c>Aplicar</c> (la política de filas) se traduce a «prohibido» con su evento.</summary>
    public async Task<ErrorDeAplicacion> DenegadoPorAlcance(CancellationToken cancelacion)
    {
        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeReservas.FueraDeRol, ruta: null) { CodigoDeError = "INV-PROHIBIDO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClaveProhibido);
    }

    private async Task<ErrorDeAplicacion?> Evaluar(Func<ActorActual, string?> motivoDelRechazo, CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeReservas.SinSesion, ruta: null) { CodigoDeError = "INV-NO-AUTENTICADO" }, cancelacion).ConfigureAwait(false);
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
