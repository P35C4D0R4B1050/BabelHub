namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// La respuesta de una llamada al Inventario: el valor, o <see cref="Disponible"/> falso cuando no respondió en 10 segundos, respondió con un error o
/// con una respuesta inválida (AUD-18). Nunca lleva el mensaje del error remoto.
/// </summary>
public sealed record RespuestaDelInventario<T>
{
    private readonly T? _valor;

    private RespuestaDelInventario(bool disponible, T? valor)
    {
        Disponible = disponible;
        _valor = valor;
    }

    public bool Disponible { get; }

    /// <summary>El valor recibido; pedirlo cuando el Inventario no respondió es un error de programación.</summary>
    public T Valor => Disponible ? _valor! : throw new InvalidOperationException("El Inventario no respondió y no hay valor.");

    public static RespuestaDelInventario<T> ConValor(T valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return new RespuestaDelInventario<T>(true, valor);
    }

    public static RespuestaDelInventario<T> NoDisponible { get; } = new(false, default);
}

/// <summary>
/// El puerto con el que el E-commerce lee la auditoría y las alertas del Inventario por REST (AUD-17, AUD-18, AUD-38): la Infraestructura lo implementa
/// con espera de 10 segundos, el token verificable del usuario (nunca una cabecera de actor) y la credencial de servicio del alcance de cada ruta.
/// Una falla del Inventario nunca es una excepción: la lista unificada muestra lo del E-commerce e indica que faltan los registros o las alertas.
/// </summary>
public interface IClienteDeAuditoriaDelInventario
{
    /// <summary>Una página de la auditoría del Inventario (alcance <c>consulta-de-auditoria</c> y token del administrador central); el cursor es el subcursor del Inventario.</summary>
    Task<RespuestaDelInventario<PaginaDeRegistros>> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default);

    /// <summary>Una página de las alertas de seguridad del Inventario.</summary>
    Task<RespuestaDelInventario<PaginaDeAlertas>> ListarAlertas(string? cursor, CancellationToken cancelacion = default);

    /// <summary>Las alertas con ocurrencia posterior a <paramref name="desde"/> (alcance <c>lectura-alertas</c>, cada 5 minutos).</summary>
    Task<RespuestaDelInventario<IReadOnlyList<AlertaConsultada>>> LeerAlertasNuevas(DateTimeOffset desde, CancellationToken cancelacion = default);

    /// <summary>Reenvía la marca de revisada de una alerta <c>inv:</c> con el token del administrador (alcance <c>marcar-alerta</c>).</summary>
    Task<RespuestaDelInventario<AlertaConsultada>> MarcarAlertaRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default);

    /// <summary>El historial de ajustes y traslados de una sucursal (alcance <c>historial</c>; la sucursal sale del token del usuario).</summary>
    Task<RespuestaDelInventario<PaginaDeRegistros>> ConsultarHistorial(Guid sucursalPedida, string? cursor, CancellationToken cancelacion = default);
}
