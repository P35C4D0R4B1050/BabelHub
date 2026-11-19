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

internal static class AuditoriaDeEnvios
{
    /// <summary>
    /// La entrada de auditoría de un cambio de estado (ENV-02, ENV-13, ENV-14, ENV-17): el estado anterior y el nuevo, el pedido, la fuente y, si existen, el número de guía y el operador; en la entrega, también la fecha.
    /// <b>Nunca lleva nombre, dirección ni teléfono.</b> El responsable lo registra la propia auditoría desde el actor actual.
    /// </summary>
    public static SolicitudDeRegistro Cambio(Envio envio, EstadoDeEnvio anterior, EstadoDeEnvio nuevo, DateTimeOffset? fecha = null)
    {
        var detalle = new Dictionary<string, string?>
        {
            ["estadoAnterior"] = anterior.Codigo(),
            ["estadoNuevo"] = nuevo.Codigo(),
            ["pedidoId"] = envio.PedidoId.ToString(),
            ["fuente"] = envio.CodigoDeFuente,
        };
        if (envio.NumeroDeGuia is { } numero)
        {
            detalle["numeroDeGuia"] = numero.ToString();
        }

        if (envio.Operador is { } operador)
        {
            detalle["operador"] = operador;
        }

        if (fecha is { } f)
        {
            detalle["fecha"] = f.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        return new SolicitudDeRegistro(AccionesAuditables.EstadoDeEnvioCambiado)
        {
            ObjetoTipo = "envio",
            ObjetoId = envio.Id.ToString(),
            Detalle = detalle,
        };
    }
}

/// <summary>
/// Los límites de uso de Envíos (plan §6; SEG-05, SEG-07): consultas (60 por usuario y minuto), operaciones (30 por usuario y minuto) y las solicitudes de guía al operador (30 por minuto en total, con un sujeto
/// constante interno que protege al operador de una ráfaga). Un límite excedido es la respuesta uniforme con el tiempo de espera; el almacén de contadores caído falla cerrado (SEG-26).
/// </summary>
internal sealed class LimitesDeEnvios(IControlDeLimites limites, IActorActual actores)
{
    public const string SujetoDelOperador = "operador-logistico";

    public Task<ErrorDeAplicacion?> Consulta(CancellationToken cancelacion) => Reservar(PoliticasDeLimite.ConsultasDeEnviosPorUsuario, SujetoDelUsuario(), cancelacion);

    public Task<ErrorDeAplicacion?> Operacion(CancellationToken cancelacion) => Reservar(PoliticasDeLimite.OperacionesDeEnviosPorUsuario, SujetoDelUsuario(), cancelacion);

    public Task<ErrorDeAplicacion?> GuiaAlOperador(CancellationToken cancelacion) => Reservar(PoliticasDeLimite.GuiasAlOperador, SujetoDelOperador, cancelacion);

    private string SujetoDelUsuario() => actores.Actual?.UsuarioId?.ToString("N") ?? "sin-usuario";

    private async Task<ErrorDeAplicacion?> Reservar(ClaveDePolitica politica, string sujeto, CancellationToken cancelacion)
    {
        ResultadoDeLimite resultado;
        try
        {
            resultado = await limites.Reservar(politica, sujeto, cancelacion: cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeEnvios.ServicioNoDisponible);
        }

        return resultado.Estado switch
        {
            EstadoDeLimite.Permitido => null,
            EstadoDeLimite.Excedido => new ErrorDeAplicacion(
                CategoriaDeError.Limite,
                ClavesDeEnvios.LimiteExcedido,
                [
                    new ErrorDeCampo(ClavesDeEnvios.EsperaEnSegundos, (resultado.EsperaEnSegundos ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new ErrorDeCampo(ClavesDeEnvios.Motivo, resultado.Motivo ?? politica.Valor),
                    new ErrorDeCampo(ClavesDeEnvios.DisponibleDesde, (resultado.DisponibleDesde ?? DateTimeOffset.UtcNow.AddSeconds(resultado.EsperaEnSegundos ?? 1)).ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ]),
            _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeEnvios.ServicioNoDisponible),
        };
    }
}
