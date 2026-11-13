using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>
/// Puerto interno de la Aplicación de Plataforma que implementan los motores de Infraestructura (Redis, memoria local).
/// Es <c>public</c> solo para que la Infraestructura lo implemente sin salir del contrato (D25); ningún otro módulo lo usa.
/// <paramref name="contador"/> es la clave opaca del contador, ya compuesta con huellas.
/// </summary>
public interface IMotorDeLimites
{
    Task<ResultadoDeLimite> Reservar(
        PoliticaDeLimite politica,
        string contador,
        string? claveDelLlamador,
        ClaveDeIdempotencia? idempotencia,
        CancellationToken cancelacion);

    Task Liberar(PoliticaDeLimite politica, ReservaDeLimite reserva, CancellationToken cancelacion);

    Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion);

    Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion);

    /// <summary>Cuenta un rechazo del origen en su ventana corta y devuelve el total (señal de «automatizado», SEG-31).</summary>
    Task<int> RegistrarRechazoDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion);

    /// <summary>Rechazos del origen dentro de la ventana, sin contar uno nuevo.</summary>
    Task<int> ContarRechazosDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion);
}
