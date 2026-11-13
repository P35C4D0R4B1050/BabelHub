namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Contrato público de los límites de uso. El módulo llamador reserva un cupo <b>antes</b> de verificar la contraseña o el
/// código, y lo devuelve (<see cref="Liberar"/>) cuando la política es liberable y la operación termina bien o se cancela.
/// </summary>
public interface IControlDeLimites
{
    /// <summary>
    /// Reserva un cupo en la política. <paramref name="sujeto"/> es un identificador interno seguro (el usuario del personal); el origen sale de <see cref="IOrigenDeSolicitud"/>.
    /// <paramref name="claveDelLlamador"/> hace idempotente la reserva (por ejemplo, el identificador de un aviso) y
    /// <paramref name="idempotencia"/> es la clave del cliente de SEG-22.
    /// </summary>
    Task<ResultadoDeLimite> Reservar(
        ClaveDePolitica politica,
        string? sujeto = null,
        string? claveDelLlamador = null,
        ClaveDeIdempotencia? idempotencia = null,
        CancellationToken cancelacion = default);

    /// <summary>Devuelve el cupo si la política es liberable; también sirve a un cupo cancelado u omitido.</summary>
    Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default);

    /// <summary>Marca como completada la operación idempotente y guarda su resultado mínimo.</summary>
    Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default);

    /// <summary>Marca como fallida la operación idempotente: libera la clave para que el cliente pueda reintentar.</summary>
    Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default);
}
