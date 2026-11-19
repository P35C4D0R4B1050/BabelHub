using System.Globalization;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

public enum DecisionDeLimiteDePedidos
{
    Permitido,

    /// <summary>Se pasó del límite: <see cref="ResultadoDeLimiteDePedidos.ReintentarEn"/> dice desde cuándo.</summary>
    Excedido,

    /// <summary>El almacén de contadores no responde y la política falla cerrada (SEG-26).</summary>
    NoDisponible,
}

public sealed record ResultadoDeLimiteDePedidos(DecisionDeLimiteDePedidos Decision, DateTimeOffset? ReintentarEn = null)
{
    public static ResultadoDeLimiteDePedidos Permitido { get; } = new(DecisionDeLimiteDePedidos.Permitido);

    public static ResultadoDeLimiteDePedidos NoDisponible { get; } = new(DecisionDeLimiteDePedidos.NoDisponible);
}

/// <summary>
/// Los límites de uso de Pedidos (T10; SEG-05, SEG-07, SEG-26), valores iniciales y configurables: <b>consultas</b>, 60 por usuario y minuto (también por identificador: pedir muchos identificadores ajenos agota el cupo); <b>operaciones de gestión</b>, 30 por usuario y minuto; y <b>lecturas del
/// detalle con contacto</b>, 200 por persona y hora, que <b>falla cerrado</b> (sin respuesta del contador se oculta el contacto). Un puerto propio hasta que <c>Plataforma</c> defina las políticas (hallazgo para el carril de plataforma): la Infraestructura lo implementa sobre <c>IControlDeLimites</c> y,
/// mientras tanto, con un doble que falla cerrado en lo que toca stock, reembolsos o contacto.
/// </summary>
public interface ILimitadorDePedidos
{
    Task<ResultadoDeLimiteDePedidos> Consultar(Guid usuarioId, CancellationToken cancelacion = default);

    Task<ResultadoDeLimiteDePedidos> Gestionar(Guid usuarioId, CancellationToken cancelacion = default);

    Task<ResultadoDeLimiteDePedidos> LeerContacto(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>
/// Los controles previos de la gestión de pedidos (T10): los límites de uso, el rechazo de una segunda operación mientras haya una <c>en_curso</c> sobre las mismas líneas (PED-21) y los textos legales pendientes en la cancelación del cliente (R13 de <c>web-base</c>). Los límites se piden
/// <b>antes</b> de abrir una transacción y los textos pendientes se leen <b>antes</b> de bloquear el pedido (orden de bloqueo cuenta → pedido).
/// </summary>
internal sealed class ControlesDeGestionDePedidos(ILimitadorDePedidos limites, IEstadoDeCuenta cuentas, IRepositorioDePedidos pedidos)
{
    public async Task<ErrorDeAplicacion?> LimiteDeConsultas(SolicitanteDePedidos solicitante, CancellationToken cancelacion) =>
        ComoError(await Pedir(() => limites.Consultar(solicitante.UsuarioId, cancelacion)).ConfigureAwait(false));

    public async Task<ErrorDeAplicacion?> LimiteDeGestion(SolicitanteDePedidos solicitante, CancellationToken cancelacion) =>
        ComoError(await Pedir(() => limites.Gestionar(solicitante.UsuarioId, cancelacion)).ConfigureAwait(false));

    /// <summary>
    /// El presupuesto de lecturas del detalle con contacto (200 por persona y hora, falla cerrado): no es un error, porque el resto del detalle se muestra y solo se <b>oculta el contacto</b> (D09). Devuelve <see langword="true"/> si se puede leer.
    /// </summary>
    public async Task<bool> PuedeLeerElContacto(SolicitanteDePedidos solicitante, CancellationToken cancelacion) =>
        (await Pedir(() => limites.LeerContacto(solicitante.UsuarioId, cancelacion)).ConfigureAwait(false)).Decision == DecisionDeLimiteDePedidos.Permitido;

    /// <summary>
    /// La cancelación del cliente exige los textos legales al día (IDE-58, R13): se lee <b>sin bloqueo</b> y antes de bloquear el pedido. Una cuenta que no existe, cancelada o con textos pendientes es «textos pendientes» o «no disponible» según el caso; si Identidad no responde, falla cerrado.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirTextosAlDia(Guid cuentaId, CancellationToken cancelacion)
    {
        EstadoDeCuenta? estado;
        try
        {
            estado = await cuentas.Consultar(cuentaId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible);
        }

        return estado is { Textos: EstadoDeAceptacionDeLosTextos.AlDia, CuentaCanceladaODesactivada: false }
            ? null
            : new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDePedidos.TextosPendientes);
    }

    /// <summary>
    /// Rechaza una operación si otra anulación o entrega <b>en curso</b> toca alguna de las líneas (<paramref name="lineaIds"/>) o, sin líneas, el pedido (una anulación): «la primera que confirma gana» (PED-21). Se llama <b>bajo el bloqueo del pedido</b>; la fila en curso la deja el tiempo 1 de la operación
    /// que llegó primero.
    /// </summary>
    public async Task<ErrorDeAplicacion?> ExigirSinOperacionEnCurso(Guid pedidoId, IReadOnlyCollection<Guid>? lineaIds, CancellationToken cancelacion)
    {
        var anulaciones = await pedidos.AnulacionesEnCurso(pedidoId, cancelacion).ConfigureAwait(false);
        var entregas = await pedidos.EntregasEnCurso(pedidoId, cancelacion).ConfigureAwait(false);
        var hayAnulacion = anulaciones.Any(a => a.BloqueaLasLineas && (lineaIds is null || lineaIds.Count == 0 || a.LineaIds.Any(lineaIds.Contains)));
        var hayEntrega = entregas.Any(e => e.BloqueaLasLineas && (lineaIds is null || lineaIds.Count == 0 || e.Lineas.Any(l => lineaIds.Contains(l.LineaId))));
        return hayAnulacion || hayEntrega ? new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.OperacionEnCurso) : null;
    }

    private static async Task<ResultadoDeLimiteDePedidos> Pedir(Func<Task<ResultadoDeLimiteDePedidos>> llamada)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLimiteDePedidos.NoDisponible;
        }
    }

    private static ErrorDeAplicacion? ComoError(ResultadoDeLimiteDePedidos resultado) => resultado.Decision switch
    {
        DecisionDeLimiteDePedidos.Permitido => null,
        DecisionDeLimiteDePedidos.Excedido => new ErrorDeAplicacion(
            CategoriaDeError.Limite,
            ClavesDePedidos.LimiteExcedido,
            resultado.ReintentarEn is { } desde ? [new ErrorDeCampo("disponibleDesde", desde.ToString("O", CultureInfo.InvariantCulture))] : null),
        _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible),
    };
}
