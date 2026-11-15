using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Los motivos del catálogo de <c>AccesoDenegado</c> que usa el módulo (AUD-05).</summary>
internal static class MotivosDeDenegacionDeReservas
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    public const string RecursoAjeno = "recurso-ajeno";
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

    /// <summary>Cualquier actor con contexto (la consulta previa sin token la hace el E-commerce con su credencial de servicio): no un visitante ni la ausencia de actor.</summary>
    public Task<ErrorDeAplicacion?> ExigirContexto(CancellationToken cancelacion) => Evaluar(_ => null, cancelacion);

    /// <summary>Registra el acceso a un pedido de otro cliente como «no encontrado» con su evento (D16): no revela que existe.</summary>
    public async Task<ErrorDeAplicacion> PedidoAjeno(CancellationToken cancelacion)
    {
        await denegaciones.Registrar(new DenegacionDeAcceso(MotivosDeDenegacionDeReservas.RecursoAjeno, ruta: null) { CodigoDeError = "INV-NO-ENCONTRADO" }, cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "reserva-no-encontrada");
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
