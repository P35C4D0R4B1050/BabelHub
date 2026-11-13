using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>
/// Opciones de la señal de «automatizado»: el origen cuyos rechazos en la ventana superan el umbral debe verificarse. Los
/// valores iniciales son provisionales y se ajustan con la medición (riesgo de falsos positivos del plan).
/// </summary>
public sealed record OpcionesDeVerificacionDePersona
{
    public OpcionesDeVerificacionDePersona(int umbralDeRechazos = 20, TimeSpan? ventana = null)
    {
        UmbralDeRechazos = umbralDeRechazos > 0
            ? umbralDeRechazos
            : throw new ArgumentOutOfRangeException(nameof(umbralDeRechazos), "El umbral es positivo.");
        Ventana = ventana is { } v && v <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(ventana), "La ventana es positiva.")
            : ventana ?? EvaluarLimite.VentanaDeRechazosDelOrigen;
    }

    /// <summary>Con más rechazos que este número en la ventana, el origen se trata como automatizado.</summary>
    public int UmbralDeRechazos { get; }

    public TimeSpan Ventana { get; }
}

/// <summary>Implementa <see cref="IExigenciaDeVerificacionDePersona"/> (SEG-31, D09).</summary>
public sealed partial class ExigirVerificacionDePersona(
    OpcionesDeVerificacionDePersona opciones,
    IMotorDeLimites motor,
    IVerificadorDePersona verificador,
    IOrigenDeSolicitud origenes,
    IPublicadorDeEventosDeSeguridad eventos,
    IReloj reloj) : IExigenciaDeVerificacionDePersona
{
    public const string MotivoTokenMalformado = "token-malformado";
    public const string MotivoVerificacionRechazada = "verificacion-rechazada";

    public async Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default)
    {
        var origen = origenes.Actual;
        if (!await EsAutomatizado(origen, cancelacion).ConfigureAwait(false))
        {
            return ResultadoDeExigencia.NoExigida;
        }

        if (string.IsNullOrWhiteSpace(tokenDeVerificacion))
        {
            return ResultadoDeExigencia.Requerida;
        }

        // Validación previa: un token malformado no llega al servicio externo y el token nunca se registra.
        if (!FormaDelToken().IsMatch(tokenDeVerificacion))
        {
            await Registrar(origen, MotivoTokenMalformado, cancelacion).ConfigureAwait(false);
            return ResultadoDeExigencia.Fallida;
        }

        ResultadoDeVerificacionDePersona verificacion;
        try
        {
            verificacion = await verificador.Verificar(tokenDeVerificacion, accion.NombreEstable(), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return ResultadoDeExigencia.NoDisponible;
        }

        switch (verificacion)
        {
            case ResultadoDeVerificacionDePersona.Verificada:
                return ResultadoDeExigencia.Verificada;

            case ResultadoDeVerificacionDePersona.Fallida:
                await Registrar(origen, MotivoVerificacionRechazada, cancelacion).ConfigureAwait(false);
                return ResultadoDeExigencia.Fallida;

            default:
                return ResultadoDeExigencia.NoDisponible;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,2048}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelToken();

    private async Task<bool> EsAutomatizado(OrigenDeSolicitud origen, CancellationToken cancelacion)
    {
        try
        {
            var rechazos = await motor.ContarRechazosDeOrigen(origen.Huella, opciones.Ventana, cancelacion).ConfigureAwait(false);
            return rechazos > opciones.UmbralDeRechazos;
        }
        catch (MotorDeLimitesNoDisponibleException)
        {
            // Sin el contador no se puede juzgar; el límite del propio flujo ya falla cerrado si el almacén no responde.
            return false;
        }
    }

    private Task Registrar(OrigenDeSolicitud origen, string motivo, CancellationToken cancelacion) =>
        eventos.Publicar(
            new EventoDeSeguridad(
                TipoDeEventoDeSeguridad.VerificacionDePersonaFallida,
                reloj.Ahora,
                origen: origen.Huella,
                direccionCompleta: origen.Direccion,
                motivo: motivo),
            cancelacion);
}
