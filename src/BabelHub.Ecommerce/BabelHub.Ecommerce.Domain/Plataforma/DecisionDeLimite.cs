namespace BabelHub.Ecommerce.Domain.Plataforma;

/// <summary>Resultado de evaluar una solicitud contra una política de límite.</summary>
public sealed record DecisionDeLimite
{
    private DecisionDeLimite(bool permitida, RespuestaDeLimite? respuesta, DateTimeOffset? bloqueoHastaNuevo)
    {
        Permitida = permitida;
        Respuesta = respuesta;
        BloqueoHastaNuevo = bloqueoHastaNuevo;
    }

    public bool Permitida { get; }

    /// <summary>La respuesta de límite excedido; solo existe cuando la solicitud se rechaza.</summary>
    public RespuestaDeLimite? Respuesta { get; }

    /// <summary>Si aceptar esta solicitud alcanza el máximo de una política con bloqueo, el instante hasta el que dura.</summary>
    public DateTimeOffset? BloqueoHastaNuevo { get; }

    public static DecisionDeLimite Permitir(DateTimeOffset? bloqueoHastaNuevo) => new(true, null, bloqueoHastaNuevo);

    public static DecisionDeLimite Rechazar(RespuestaDeLimite respuesta) => new(false, respuesta, null);
}
