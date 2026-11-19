using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// Cuándo fue la última corrida correcta de la depuración (T17, DEV-30): el instante del arranque del proceso o de la última vez que <see cref="RedactarDevolucionesVencidas"/> terminó bien. Vive en memoria y se registra como <b>singleton</b>: tras un reinicio, la cuenta de las 24 horas vuelve a empezar
/// (un límite aceptado: el Plataforma registra además cada corrida para la salud del servicio). Es seguro entre hilos.
/// </summary>
internal sealed class ControlDeLaDepuracionDeDevoluciones(IReloj reloj)
{
    private readonly object _candado = new();
    private DateTimeOffset _referencia = reloj.Ahora;

    public DateTimeOffset Referencia
    {
        get
        {
            lock (_candado)
            {
                return _referencia;
            }
        }
    }

    public void RegistrarCorridaCorrecta(DateTimeOffset instante)
    {
        lock (_candado)
        {
            if (instante > _referencia)
            {
                _referencia = instante;
            }
        }
    }
}

/// <summary>
/// <c>RedactarDevolucionesVencidas</c> (T17; DEV-27, DEV-30; AC-DEV-29, AC-DEV-32): el trabajo diario (03:45 de Lima), como «sistema». Redacta el motivo y el texto y quita el responsable cliente de las devoluciones con <b>todas sus líneas en reembolsada o rechazada</b> y <b>dos años por día de Lima</b> desde el último cambio (2 años y un día sí;
/// 2 años menos un día no; una con una línea no final, no), conservando la referencia al acceso de los reportes (DEV-27). Es <b>una sola transacción</b> con su auditoría (el conteo, sin datos personales): si no puede redactar o auditar, <b>no hace nada ese día</b> y reintenta al siguiente. Cada devolución se vuelve a leer bajo su
/// bloqueo y se revalida. Procesa hasta <see cref="MaximoPorCorrida"/> por corrida; el resto espera a la siguiente. Idempotente.
/// </summary>
internal sealed class RedactarDevolucionesVencidas(
    AutorizadorDeDevoluciones autorizador,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    ControlDeLaDepuracionDeDevoluciones control,
    IReloj reloj)
{
    public const int MaximoPorCorrida = 1000;

    public async Task<Resultado<int>> Ejecutar(int maximo = MaximoPorCorrida, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<int>(rechazo);
        }

        var ahora = reloj.Ahora;
        var hoy = reloj.HoyEnLima;
        try
        {
            var candidatas = await repositorio.ObtenerVencidasParaDepurar(PoliticaDeRetencion.CotaDeCandidatas(hoy), Math.Clamp(maximo, 1, MaximoPorCorrida), cancelacion).ConfigureAwait(false);
            var redactadas = await unidad.Ejecutar(
                async token =>
                {
                    var cantidad = 0;
                    // Bloquea en orden de identificador, como la redacción de una cuenta: una cancelación concurrente no se interbloquea con la depuración.
                    foreach (var devolucionId in candidatas.Order())
                    {
                        var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false);
                        if (devolucion is null || !devolucion.EstaVencidaParaDepurar(hoy))
                        {
                            continue;
                        }

                        // La referencia al acceso de los reportes se conserva (DEV-27): solo la cancelación de la cuenta la quita.
                        if (devolucion.Redactar(ahora, quitarElAcceso: false))
                        {
                            await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                            cantidad++;
                        }
                    }

                    if (cantidad > 0)
                    {
                        await auditoria.Registrar(
                            new SolicitudDeRegistro(AccionesAuditables.DevolucionesDepuradas)
                            {
                                Detalle = new Dictionary<string, string?> { ["cantidad"] = cantidad.ToString(CultureInfo.InvariantCulture) },
                            },
                            token).ConfigureAwait(false);
                    }

                    return cantidad;
                },
                cancelacion).ConfigureAwait(false);

            control.RegistrarCorridaCorrecta(ahora);
            return Resultado<int>.Exito(redactadas);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin poder redactar o auditar la transacción se revierte: hoy no se hace nada y se reintenta mañana. Nunca se propaga el texto de la falla.
            return FallosDeDevoluciones.NoDisponible<int>();
        }
    }
}

/// <summary>
/// <c>RevisarDepuracionDeDevoluciones</c> (T17, DEV-30): publica la alerta técnica si pasaron <b>24 horas sin una corrida correcta</b> de la depuración (contadas desde el arranque o la última corrida correcta). Una alerta por revisión, con el motivo del catálogo cerrado y sin datos; una alerta que no sale nunca interrumpe.
/// Devuelve si publicó. Solo el «sistema».
/// </summary>
internal sealed class RevisarDepuracionDeDevoluciones(AutorizadorDeDevoluciones autorizador, ControlDeLaDepuracionDeDevoluciones control, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(24);

    public async Task<Resultado<bool>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<bool>(rechazo);
        }

        var ahora = reloj.Ahora;
        if (ahora - control.Referencia < EsperaMaxima)
        {
            return Resultado<bool>.Exito(false);
        }

        try
        {
            await alertas.Publicar(new AlertaTecnica(ClavesDeDevoluciones.AlertaGenerica, ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale nunca interrumpe la revisión: la próxima hora vuelve a intentarlo.
        }

        return Resultado<bool>.Exito(true);
    }
}
