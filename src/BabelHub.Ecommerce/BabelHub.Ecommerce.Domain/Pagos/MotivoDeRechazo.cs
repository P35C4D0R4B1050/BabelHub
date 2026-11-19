namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>
/// Por qué la pasarela rechazó un cobro: una lista cerrada propia (D13). El texto que devuelve la pasarela nunca se muestra ni se guarda; el adaptador lo traduce a uno de estos
/// motivos y lo que no entiende es <see cref="Otro"/>. Así un mensaje del tercero (que podría incluir un número de tarjeta) no llega a la pantalla, a los registros ni al almacenamiento.
/// </summary>
public enum MotivoDeRechazo
{
    TarjetaRechazada,
    FondosInsuficientes,
    TarjetaVencida,
    CodigoDeSeguridadInvalido,
    YapeRechazado,
    MedioNoPermitido,

    /// <summary>Un desafío de autenticación (3DS) sin resolver se trata como un rechazo (R03).</summary>
    AutenticacionRequerida,
    Otro,
}

public static class MotivoDeRechazoExtensiones
{
    public static string ATexto(this MotivoDeRechazo motivo) => motivo switch
    {
        MotivoDeRechazo.TarjetaRechazada => "tarjeta_rechazada",
        MotivoDeRechazo.FondosInsuficientes => "fondos_insuficientes",
        MotivoDeRechazo.TarjetaVencida => "tarjeta_vencida",
        MotivoDeRechazo.CodigoDeSeguridadInvalido => "codigo_de_seguridad_invalido",
        MotivoDeRechazo.YapeRechazado => "yape_rechazado",
        MotivoDeRechazo.MedioNoPermitido => "medio_no_permitido",
        MotivoDeRechazo.AutenticacionRequerida => "autenticacion_requerida",
        MotivoDeRechazo.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    /// <summary>El motivo cuyo texto estable coincide exactamente; cualquier otro texto (también uno libre de la pasarela) es <see cref="MotivoDeRechazo.Otro"/>.</summary>
    public static MotivoDeRechazo DesdeTexto(string? texto)
    {
        foreach (var motivo in Enum.GetValues<MotivoDeRechazo>())
        {
            if (string.Equals(motivo.ATexto(), texto, StringComparison.Ordinal))
            {
                return motivo;
            }
        }

        return MotivoDeRechazo.Otro;
    }
}
