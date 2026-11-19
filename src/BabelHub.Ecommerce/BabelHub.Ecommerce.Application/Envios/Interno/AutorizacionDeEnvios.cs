using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

internal static class RolesDeEnvios
{
    public const string PersonalDeLogistica = "personal-de-logistica";

    public const string AdministradorCentral = "administrador-central";

    public const string Cliente = "cliente";

    /// <summary>Ver los envíos, pedir la guía, pasar a en reparto y marcar entregado son solo de logística y de la administración central (ENV-08).</summary>
    public static bool PuedeOperarEnvios(ActorActual? actor) =>
        actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null } && (actor.Rol == PersonalDeLogistica || actor.Rol == AdministradorCentral);
}

internal static class MotivosDeDenegacionDeEnvios
{
    public const string FueraDeRol = "fuera-de-rol";

    public const string SinSesion = "sin-sesion";

    public const string RecursoAjeno = "recurso-ajeno";
}

/// <summary>
/// La comprobación del rol, siempre con el mismo orden: sesión, rol, envío, estado (plan §4). Cada intento rechazado por rol deja un acceso denegado (SEG-09); logística y la administración central tienen alcance
/// global, así que un identificador inexistente solo les responde «no encontrado», sin evento.
/// </summary>
internal sealed class AutorizadorDeEnvios(IActorActual actores, IDenegacionDeAcceso denegaciones)
{
    public ActorActual? Actor => actores.Actual is { Tipo: not TipoDeActor.Anonimo } actor ? actor : null;

    public async Task<ErrorDeAplicacion?> ExigirLogisticaOAdministracion(CancellationToken cancelacion)
    {
        var actor = Actor;
        if (actor is null)
        {
            await Denegar(MotivosDeDenegacionDeEnvios.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, ClavesDeEnvios.NoAutenticado);
        }

        if (RolesDeEnvios.PuedeOperarEnvios(actor))
        {
            return null;
        }

        await Denegar(MotivosDeDenegacionDeEnvios.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDeEnvios.Prohibido);
    }

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}

internal static class MapeoDeEnvios
{
    public static EstadoDelEnvio Estado(EstadoDeEnvio estado) => estado switch
    {
        EstadoDeEnvio.PendienteDeGuia => EstadoDelEnvio.PendienteDeGuia,
        EstadoDeEnvio.GuiaGenerada => EstadoDelEnvio.GuiaGenerada,
        EstadoDeEnvio.EnReparto => EstadoDelEnvio.EnReparto,
        EstadoDeEnvio.Entregado => EstadoDelEnvio.Entregado,
        EstadoDeEnvio.Cancelado => EstadoDelEnvio.Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static DetalleDeEnvio Detalle(Envio e) =>
        new(
            e.Id,
            e.PedidoId,
            e.CodigoDeFuente,
            Estado(e.Estado),
            e.NumeroDeGuia?.ToString(),
            e.Operador,
            e.Lineas.Select(l => l.LineaId).ToList(),
            e.CreadoEn,
            e.GuiaGeneradaEn,
            e.EnRepartoEn,
            e.EntregadoEn,
            e.CanceladoEn);
}
