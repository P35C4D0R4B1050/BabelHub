using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Los tipos de operación cuyo volumen se vigila y los motivos de alerta técnica que les corresponden.</summary>
internal static class VolumenDeOperacionesDeReservas
{
    public const string Confirmaciones = "confirmaciones";

    public const string Liberaciones = "liberaciones";

    public const string MotivoDeConfirmaciones = "confirmaciones-de-reservas-anomalas";

    public const string MotivoDeLiberaciones = "liberaciones-de-reservas-anomalas";

    /// <summary>El umbral por ventana de 5 minutos si la configuración no dice otro (plan §6).</summary>
    public const int UmbralPorDefecto = 200;

    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Cuenta las confirmaciones y liberaciones por ventana de 5 minutos (en memoria, por instancia: un solo servicio, una instancia) para vigilar la residual de las credenciales de <c>expiracion-de-reservas</c>
/// (plan §6): un volumen anómalo no rechaza nada, solo <b>avisa una sola vez por ventana</b> con una alerta técnica. Seguro entre hilos.
/// </summary>
internal sealed class VolumenDeOperaciones(IReloj reloj, int umbral = VolumenDeOperacionesDeReservas.UmbralPorDefecto, TimeSpan? ventana = null)
{
    private readonly object _candado = new();
    private readonly Dictionary<string, (DateTimeOffset Inicio, int Cuenta, bool Avisada)> _ventanas = [];

    /// <summary>Suma una operación (o varias líneas) y devuelve <see langword="true"/> la primera vez que la ventana supera el umbral.</summary>
    public bool RegistrarYDebeAvisar(string tipo, int cantidad = 1)
    {
        var ahora = reloj.Ahora;
        lock (_candado)
        {
            var (inicio, cuenta, avisada) = _ventanas.TryGetValue(tipo, out var actual) && ahora - actual.Inicio < (ventana ?? VolumenDeOperacionesDeReservas.Ventana)
                ? actual
                : (ahora, 0, false);

            cuenta += cantidad;
            var debeAvisar = !avisada && cuenta > umbral;
            _ventanas[tipo] = (inicio, cuenta, avisada || debeAvisar);
            return debeAvisar;
        }
    }
}

/// <summary>Publica la alerta técnica de volumen anómalo por <see cref="IAlertas"/>: nunca interrumpe la operación. Si el motivo aún no consta en el catálogo cerrado, deja el de «motivo no catalogado».</summary>
internal sealed class AlertasDeVolumenDeReservas(VolumenDeOperaciones volumen, IAlertas alertas)
{
    public async Task Registrar(string tipo, string motivo, int cantidad, CancellationToken cancelacion)
    {
        if (!volumen.RegistrarYDebeAvisar(tipo, cantidad))
        {
            return;
        }

        await Publicar(alertas, motivo, cancelacion).ConfigureAwait(false);
    }

    /// <summary>La alerta técnica con el motivo dado (o el de «no catalogado» si aún no consta), sin interrumpir nunca a quien llama.</summary>
    public static async Task Publicar(IAlertas alertas, string motivo, CancellationToken cancelacion)
    {
        try
        {
            try
            {
                await alertas.PublicarTecnica(new CodigoDeAlerta(motivo), cancelacion).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // El motivo todavía no está en el catálogo cerrado de alertas (pendiente de ampliar): el aviso no se pierde.
                await alertas.PublicarTecnica(new CodigoDeAlerta("motivo-de-alerta-no-catalogado"), cancelacion).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Una alerta que no se puede publicar no interrumpe la confirmación ni la liberación (AUD-40).
        }
    }
}
