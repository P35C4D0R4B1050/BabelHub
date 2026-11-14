namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>El resultado de una llamada al Inventario (AUD-18).</summary>
public enum EstadoDeRespuestaDelInventario
{
    /// <summary>Respondió con un valor válido.</summary>
    Disponible,

    /// <summary>No respondió en 10 segundos, respondió con un error 5xx, con un corte o con una respuesta inválida.</summary>
    NoDisponible,

    /// <summary>Respondió que la operación no está permitida o no es válida (401, 403, 404, 409, 400): no es una falla del servicio y se traduce a <c>ECO-PROHIBIDO</c> o al código que corresponda, no a «servicio no disponible».</summary>
    Rechazada,
}

/// <summary>
/// La respuesta de una llamada al Inventario: el valor, o el estado en que no lo hay (<see cref="EstadoDeRespuestaDelInventario"/>). Nunca lleva el mensaje
/// del error remoto.
/// </summary>
public sealed record RespuestaDelInventario<T>
{
    private readonly T? _valor;

    private RespuestaDelInventario(EstadoDeRespuestaDelInventario estado, T? valor)
    {
        Estado = estado;
        _valor = valor;
    }

    public EstadoDeRespuestaDelInventario Estado { get; }

    public bool Disponible => Estado == EstadoDeRespuestaDelInventario.Disponible;

    /// <summary>El valor recibido; pedirlo cuando el Inventario no respondió es un error de programación.</summary>
    public T Valor => Disponible ? _valor! : throw new InvalidOperationException("El Inventario no respondió y no hay valor.");

    public static RespuestaDelInventario<T> ConValor(T valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return new RespuestaDelInventario<T>(EstadoDeRespuestaDelInventario.Disponible, valor);
    }

    public static RespuestaDelInventario<T> NoDisponible { get; } = new(EstadoDeRespuestaDelInventario.NoDisponible, default);

    public static RespuestaDelInventario<T> Rechazada { get; } = new(EstadoDeRespuestaDelInventario.Rechazada, default);
}

/// <summary>
/// El puerto con el que el E-commerce lee la auditoría y las alertas del Inventario por REST (AUD-17, AUD-18, AUD-38): la Infraestructura lo implementa
/// con espera de 10 segundos, el token verificable del usuario (nunca una cabecera de actor) y la credencial de servicio del alcance de cada ruta,
/// conforme al contrato congelado de ECO-0 (<c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c>). Una falla del Inventario nunca es una
/// excepción: la lista unificada muestra lo del E-commerce e indica que faltan los registros o las alertas.
/// </summary>
public interface IClienteDeAuditoriaDelInventario
{
    /// <summary>Una página de la auditoría del Inventario (alcance <c>consulta-de-auditoria</c> y token del administrador central); el cursor es el subcursor del Inventario.</summary>
    Task<RespuestaDelInventario<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default);

    /// <summary>
    /// Las alertas con ocurrencia posterior a <paramref name="desde"/> (alcance <c>lectura-alertas</c>, cada 5 minutos): es la única lectura de alertas del
    /// Inventario que define el contrato (<c>GET /alertas?desde=</c>); la lista unificada de seguridad se compone con ella.
    /// </summary>
    Task<RespuestaDelInventario<IReadOnlyList<AlertaConsultada>>> LeerAlertasNuevas(DateTimeOffset desde, CancellationToken cancelacion = default);

    /// <summary>Reenvía la marca de revisada de una alerta <c>inv:</c> con el token del administrador (alcance <c>marcar-alerta</c>).</summary>
    Task<RespuestaDelInventario<AlertaConsultada>> MarcarAlertaRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default);

    /// <summary>El historial de ajustes y traslados de una sucursal (alcance <c>historial</c>; la sucursal sale del token del usuario).</summary>
    Task<RespuestaDelInventario<PaginaDeRegistros>> ConsultarHistorial(Guid sucursalPedida, string? cursor, CancellationToken cancelacion = default);
}
