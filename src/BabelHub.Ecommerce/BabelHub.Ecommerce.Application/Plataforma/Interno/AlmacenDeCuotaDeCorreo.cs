using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>
/// La reserva atómica de la cuota diaria de correo (<c>plataforma.cuota_correo</c>, T34): un <c>UPDATE</c> condicional por cupo y día, de modo que dos envíos simultáneos no pasen del tamaño del cupo.
/// El día es el de Lima.
/// </summary>
public interface IAlmacenDeCuotaDeCorreo
{
    /// <summary>Reserva un lugar en el primer cupo candidato con espacio; <see langword="null"/> si ninguno lo tiene.</summary>
    Task<ReservaDeCupoDeCorreo?> Reservar(DateOnly dia, IReadOnlyList<CupoCandidato> candidatos, CancellationToken cancelacion = default);

    /// <summary>Devuelve el lugar de un envío que falló (IDE-66): la cuota solo cuenta lo que salió.</summary>
    Task Devolver(DateOnly dia, CupoDeCorreo cupo, CancellationToken cancelacion = default);

    /// <summary>Reserva, con el tope diario, un correo de alerta a la administración de ese motivo y servicio (IDE-82); falso si ya se llegó al tope.</summary>
    Task<bool> ReservarAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, int tope, CancellationToken cancelacion = default);

    /// <summary>Devuelve el lugar del tope de un correo de alerta que no salió (el envío falló): el tope, como el cupo, solo cuenta lo que salió (IDE-82).</summary>
    Task DevolverAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, CancellationToken cancelacion = default);
}

/// <summary>Un lugar reservado: el cupo, lo enviado en ese cupo y lo enviado en total <b>después</b> de la reserva (para la alerta del umbral).</summary>
public sealed record ReservaDeCupoDeCorreo(CupoDeCorreo Cupo, int EnviadosEnElCupo, int EnviadosEnTotal)
{
    /// <summary>El motivo y el servicio cuyo tope de 24 al día se contó con este envío (solo las alertas a la administración): se devuelven juntos con el cupo si el envío falla.</summary>
    public string? MotivoDelTope { get; init; }

    public string? ServicioDelTope { get; init; }
}
