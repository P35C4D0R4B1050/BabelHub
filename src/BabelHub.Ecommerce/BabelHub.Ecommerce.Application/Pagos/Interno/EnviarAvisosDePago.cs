using System.Globalization;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Los avisos del estado del pago por correo (T21, PAG-22, PAG-23): pendiente, aprobado, rechazado, vencido y reembolso automático. Salen <b>desde la tabla <c>avisos_de_pago</c></b>, una fila por <c>(intención, intento, evento)</c>
/// creada en la transacción de la transición (la de «aprobado» solo en la de <c>completada</c>), por <see cref="IEnviadorDeCorreo"/> con la categoría <c>Pago</c>, <b>sin datos de pago</b> (ni número de tarjeta, token, referencia de la
/// pasarela ni importe de un cargo): solo el motivo de un rechazo, de una lista cerrada. El correo del titular se lee <b>al enviar</b> y no se guarda; una intención desvinculada (PAG-26) o sin correo omite el aviso. Una falla del
/// envío se reintenta con espera creciente y <b>nunca cambia el estado del cobro</b> (regla 10). El WhatsApp con el mismo evento es del módulo de Notificaciones (<c>INotificador</c>, aún sin contrato publicado): se conecta
/// a la misma fila cuando exista.
/// </summary>
internal sealed class EnviarAvisosDePago(
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    ICuentaDelPagador cuentas,
    IEnviadorDeCorreo correo,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj)
{
    public const string MotivoDeAlertaAvisoDePagoFallido = "aviso-de-pago-fallido";

    /// <summary>El nombre de la plantilla del catálogo de <c>Plataforma</c> (su contenido lo define <see cref="PlantillasDeCorreoDePagos"/>).</summary>
    public static string Plantilla(EventoDeAvisoDePago evento) => PlantillasDeCorreoDePagos.Para(evento).Nombre;

    /// <summary>Envía los avisos listos (hasta <paramref name="limite"/>) y devuelve cuántos atendió.</summary>
    public async Task<int> Ejecutar(int limite, CancellationToken cancelacion = default)
    {
        var avisos = await unidad.Ejecutar(token => repositorio.ListarAvisosDePagoListos(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        foreach (var aviso in avisos)
        {
            try
            {
                await Enviar(aviso, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un aviso que falla no detiene a los demás ni cambia el estado del cobro: queda anotado para el reintento.
                await Reintentar(aviso, cancelacion).ConfigureAwait(false);
            }
        }

        return avisos.Count;
    }

    private async Task Enviar(AvisoDePago aviso, CancellationToken cancelacion)
    {
        var (intencion, intento) = await unidad.Ejecutar(
            async token => (
                await repositorio.ObtenerIntencion(aviso.IntencionId, paraActualizar: false, token).ConfigureAwait(false),
                await repositorio.ObtenerIntento(aviso.IntentoId, token).ConfigureAwait(false)),
            cancelacion).ConfigureAwait(false);

        // Una intención desvinculada ya no tiene a quién avisar y no se lee nada de la persona (PAG-26).
        if (intencion?.CuentaId is not { } cuentaId)
        {
            await Guardar(aviso, a => a.Omitir(), cancelacion).ConfigureAwait(false);
            return;
        }

        var destinatario = await cuentas.CorreoDelTitular(cuentaId, cancelacion).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(destinatario))
        {
            await Guardar(aviso, a => a.Omitir(), cancelacion).ConfigureAwait(false);
            return;
        }

        var resultado = await correo.Enviar(new CorreoSaliente(CategoriaDeCorreo.Pago, destinatario, Plantilla(aviso.Evento), Datos(aviso, intencion, intento)), cancelacion).ConfigureAwait(false);
        if (resultado == ResultadoDeEnvio.Suprimido)
        {
            await Reintentar(aviso, cancelacion).ConfigureAwait(false);
            return;
        }

        // Enviado, o encolado por la cuota de Plataforma (que lo reintenta): el aviso ya salió de Pagos.
        await Guardar(aviso, a => a.MarcarEnviado(reloj.Ahora), cancelacion).ConfigureAwait(false);
    }

    /// <summary>Los datos que admite la plantilla: nunca datos de pago. Un rechazo lleva su motivo de la lista cerrada; un reembolso, el importe que se devuelve a quien pagó.</summary>
    private static IReadOnlyDictionary<string, string> Datos(AvisoDePago aviso, IntencionDePago intencion, IntentoDeCobro? intento) => aviso.Evento switch
    {
        EventoDeAvisoDePago.Rechazado => new Dictionary<string, string> { [PlantillasDeCorreoDePagos.Motivo] = PlantillasDeCorreoDePagos.TextoDelMotivo(intento?.MotivoDeRechazo) },
        EventoDeAvisoDePago.ReembolsoAutomatico => new Dictionary<string, string> { [PlantillasDeCorreoDePagos.Importe] = PlantillasDeCorreoDePagos.ImporteEnSoles((intento?.Importe ?? intencion.Total).Valor) },
        _ => new Dictionary<string, string>(),
    };

    private Task Guardar(AvisoDePago aviso, Action<AvisoDePago> cambio, CancellationToken cancelacion)
    {
        cambio(aviso);
        return unidad.Ejecutar(
            async token =>
            {
                await repositorio.GuardarAvisoDePago(aviso, token).ConfigureAwait(false);
                return true;
            },
            cancelacion);
    }

    private async Task Reintentar(AvisoDePago aviso, CancellationToken cancelacion)
    {
        try
        {
            await Guardar(aviso, a => a.RegistrarFalla(reloj.Ahora), cancelacion).ConfigureAwait(false);
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlertaAvisoDePagoFallido, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ni siquiera se pudo anotar la falla: la próxima corrida lo vuelve a ver pendiente.
        }
    }
}
