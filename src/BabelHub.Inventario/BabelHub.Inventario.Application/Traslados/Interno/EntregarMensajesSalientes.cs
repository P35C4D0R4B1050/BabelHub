using System.Globalization;
using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Por qué un mensaje del buzón terminó <c>fallido</c> (columna <c>motivo_de_fallo</c>).</summary>
internal enum MotivoDeFalloDeMensaje
{
    /// <summary>El E-commerce lo rechazó de forma permanente (400, 404, 409 o 422) o la carga guardada no es válida: no se reintenta.</summary>
    Permanente,

    /// <summary>El E-commerce rechazó la credencial (401 o 403): se reencola a mano cuando se rote bien, sin repetir cada hora.</summary>
    Credencial,

    /// <summary>Un aviso con más de 24 horas sin entregar: pierde su utilidad y queda para la persona que opera.</summary>
    Vencido,
}

/// <summary>Un mensaje del buzón reclamado para entregar: lo que hace falta para enviarlo y decidir su reintento.</summary>
internal sealed record MensajeParaEntregar(Guid Id, string Tipo, string ClaveDeEvento, string SucursalCodigo, string Carga, int Intentos, DateTimeOffset CreadoEn, bool YaAlertado);

/// <summary>
/// La cola de entrega del buzón de salida (plan §5). <see cref="Reclamar"/> corre en una <b>transacción corta</b>: toma los pendientes cuya fecha llegó con <c>FOR UPDATE SKIP LOCKED</c> (dos instancias no toman el mismo
/// mensaje), los arrienda (<c>arrendado_hasta</c>) y <b>respeta el orden por traslado</b>: no entrega un estado de pedido mientras haya otro anterior del mismo traslado todavía pendiente. Las demás operaciones registran el resultado
/// en otra transacción corta; el envío se hace <b>fuera de toda transacción</b>.
/// </summary>
internal interface IColaDeMensajesSalientes
{
    Task<IReadOnlyList<MensajeParaEntregar>> Reclamar(int maximo, DateTimeOffset ahora, TimeSpan arrendamiento, CancellationToken cancelacion = default);

    Task MarcarEnviado(Guid mensajeId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    Task Reprogramar(Guid mensajeId, int intentos, DateTimeOffset proximaFecha, bool alertado, CancellationToken cancelacion = default);

    Task MarcarFallido(Guid mensajeId, MotivoDeFalloDeMensaje motivo, int intentos, CancellationToken cancelacion = default);

    /// <summary>Borra los mensajes enviados o fallidos de más de 30 días (plan §3): devuelve cuántos.</summary>
    Task<int> PurgarTerminados(DateTimeOffset antesDe, CancellationToken cancelacion = default);
}

/// <summary>Cuántos mensajes se intentan por corrida.</summary>
internal sealed record EntregarMensajesSalientesEntrada(int Maximo = 50);

/// <summary>Lo que hizo una corrida de entrega.</summary>
internal sealed record ResultadoDeLaEntrega(int Reclamados, int Entregados, int Reprogramados, int Fallidos);

/// <summary>Los plazos de entrega (plan §5, ECO-0 §9).</summary>
internal static class PlanDeEntrega
{
    /// <summary>Un aviso con más de esto sin entregar queda <c>fallido</c> con alerta técnica; el estado del pedido avisa una vez a esa edad y sigue cada hora.</summary>
    public static readonly TimeSpan EdadMaxima = TimeSpan.FromHours(24);

    /// <summary>Cuánto dura el arrendamiento de un mensaje reclamado: si el proceso muere enviando, otro lo retoma pasado este plazo.</summary>
    public static readonly TimeSpan Arrendamiento = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

    /// <summary>La espera tras el intento fallido número <paramref name="intentos"/> (1, 5, 15, 30 y 60 minutos y luego cada hora).</summary>
    public static TimeSpan Espera(int intentos) => Esperas[Math.Clamp(intentos - 1, 0, Esperas.Length - 1)];
}

/// <summary>
/// Entrega los mensajes del buzón al E-commerce (TRA-10, TRA-11, plan §5): <b>reclama</b> un lote en una transacción corta, <b>envía fuera de toda transacción</b> por <see cref="IEnvioAlEcommerce"/> (que nunca lanza: un E-commerce caído
/// deja el cambio aplicado y el mensaje pendiente) y registra el resultado en otra. Un aviso va a su sucursal por código con campos tipados; el estado del traslado de un pedido, con su clave de idempotencia y <b>en orden por traslado</b>.
/// Reintentos de 1, 5, 15, 30 y 60 minutos y luego cada hora; un aviso con más de 24 horas, un error permanente (400, 404, 409 o 422) o una carga inválida quedan <c>fallido</c> con alerta técnica; 401 y 403 dejan <c>fallido</c> por
/// credencial con alerta para reencolar a mano; el estado del pedido avisa a las 24 horas una sola vez y sigue cada hora. La respuesta del E-commerce se trata como no confiable (la valida el adaptador). <b>Exige el actor «sistema»</b>.
/// Las alertas usan el motivo <c>dependencia-no-necesaria-fallida</c> del catálogo (el E-commerce es una dependencia no necesaria): faltan motivos propios (hallazgo).
/// </summary>
internal sealed class EntregarMensajesSalientes(
    AutorizadorDeTraslados autorizador,
    IUnidadDeTrabajo unidad,
    IColaDeMensajesSalientes cola,
    IEnvioAlEcommerce envio,
    IAlertas alertas,
    IReloj reloj) : ICasoDeUso<EntregarMensajesSalientesEntrada, ResultadoDeLaEntrega>
{
    /// <summary>El motivo del catálogo cerrado para toda falla de entrega (no hay motivos propios de traslados).</summary>
    public const string MotivoDeAlerta = "dependencia-no-necesaria-fallida";

    public async Task<Resultado<ResultadoDeLaEntrega>> Ejecutar(EntregarMensajesSalientesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<ResultadoDeLaEntrega>(rechazo);
        }

        var ahora = reloj.Ahora;
        var lote = await unidad.Ejecutar(token => cola.Reclamar(Math.Clamp(entrada.Maximo, 1, 500), ahora, PlanDeEntrega.Arrendamiento, token), cancelacion).ConfigureAwait(false);

        int entregados = 0, reprogramados = 0, fallidos = 0;
        foreach (var mensaje in lote)
        {
            var resultado = await Enviar(mensaje, cancelacion).ConfigureAwait(false);
            var momento = reloj.Ahora;
            var (nuevoEstado, alertar) = await unidad.Ejecutar(token => Registrar(mensaje, resultado, momento, token), cancelacion).ConfigureAwait(false);

            switch (nuevoEstado)
            {
                case EstadoDeLaEntrega.Enviado: entregados++; break;
                case EstadoDeLaEntrega.Reprogramado: reprogramados++; break;
                default: fallidos++; break;
            }

            if (alertar)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }
        }

        return Resultado<ResultadoDeLaEntrega>.Exito(new ResultadoDeLaEntrega(lote.Count, entregados, reprogramados, fallidos));
    }

    private enum EstadoDeLaEntrega
    {
        Enviado,
        Reprogramado,
        Fallido,
    }

    private async Task<(EstadoDeLaEntrega Estado, bool Alertar)> Registrar(MensajeParaEntregar mensaje, ResultadoDeEnvio resultado, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        var intentos = mensaje.Intentos + 1;
        switch (resultado)
        {
            case ResultadoDeEnvio.Entregado:
                await cola.MarcarEnviado(mensaje.Id, ahora, cancelacion).ConfigureAwait(false);
                return (EstadoDeLaEntrega.Enviado, false);

            case ResultadoDeEnvio.Permanente:
                await cola.MarcarFallido(mensaje.Id, MotivoDeFalloDeMensaje.Permanente, intentos, cancelacion).ConfigureAwait(false);
                return (EstadoDeLaEntrega.Fallido, true);

            case ResultadoDeEnvio.CredencialRechazada:
                await cola.MarcarFallido(mensaje.Id, MotivoDeFalloDeMensaje.Credencial, intentos, cancelacion).ConfigureAwait(false);
                return (EstadoDeLaEntrega.Fallido, true);

            default:
                var edad = ahora - mensaje.CreadoEn;
                if (mensaje.Tipo == TiposDeMensaje.Aviso && edad >= PlanDeEntrega.EdadMaxima)
                {
                    await cola.MarcarFallido(mensaje.Id, MotivoDeFalloDeMensaje.Vencido, intentos, cancelacion).ConfigureAwait(false);
                    return (EstadoDeLaEntrega.Fallido, true);
                }

                // El estado del pedido sigue cada hora y avisa una sola vez al cumplir las 24 horas.
                var alertarAhora = mensaje.Tipo == TiposDeMensaje.EstadoDePedido && edad >= PlanDeEntrega.EdadMaxima && !mensaje.YaAlertado;
                await cola.Reprogramar(mensaje.Id, intentos, ahora + PlanDeEntrega.Espera(intentos), mensaje.YaAlertado || alertarAhora, cancelacion).ConfigureAwait(false);
                return (EstadoDeLaEntrega.Reprogramado, alertarAhora);
        }
    }

    /// <summary>
    /// Envía fuera de toda transacción. Primero se lee la carga: una carga inválida es un error permanente. Luego se envía: cualquier excepción inesperada del adaptador (que no debería lanzar) es un fallo transitorio y
    /// nunca interrumpe la entrega.
    /// </summary>
    private async Task<ResultadoDeEnvio> Enviar(MensajeParaEntregar mensaje, CancellationToken cancelacion)
    {
        Func<Task<ResultadoDeEnvio>> enviar;
        try
        {
            enviar = Preparar(mensaje, cancelacion);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // La carga guardada no tiene la forma que la entrega espera: nunca se va a poder enviar.
            return ResultadoDeEnvio.Permanente;
        }

        try
        {
            return await enviar().ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return ResultadoDeEnvio.Reintentable;
        }
    }

    private Func<Task<ResultadoDeEnvio>> Preparar(MensajeParaEntregar mensaje, CancellationToken cancelacion)
    {
        using var documento = JsonDocument.Parse(mensaje.Carga);
        var carga = documento.RootElement;
        switch (mensaje.Tipo)
        {
            case TiposDeMensaje.Aviso:
                var aviso = new AvisoDeTrasladoParaEnviar(
                    mensaje.ClaveDeEvento,
                    mensaje.SucursalCodigo,
                    carga.GetProperty("numeroDeTraslado").GetInt64(),
                    carga.GetProperty("estado").GetString()!,
                    carga.GetProperty("rolDeLaSucursal").GetString()!,
                    carga.GetProperty("cantidad").GetInt32(),
                    Instante(carga));
                return () => envio.EnviarAviso(aviso, cancelacion);

            case TiposDeMensaje.EstadoDePedido:
                var estado = new EstadoDeTrasladoDePedidoParaEnviar(
                    mensaje.ClaveDeEvento,
                    carga.GetProperty("pedidoId").GetGuid(),
                    carga.GetProperty("trasladoId").GetGuid(),
                    carga.GetProperty("edicionId").GetGuid(),
                    carga.GetProperty("cantidad").GetInt32(),
                    carga.GetProperty("estado").GetString()!,
                    carga.GetProperty("fechaEstimadaDeLlegada") is { ValueKind: JsonValueKind.String } fecha ? DateOnly.ParseExact(fecha.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    carga.GetProperty("estimadaPorConfirmar").GetBoolean(),
                    Instante(carga));
                return () => envio.InformarEstadoDelPedido(estado, cancelacion);

            default:
                throw new InvalidOperationException("Un tipo de mensaje que la entrega no conoce.");
        }
    }

    private static DateTimeOffset Instante(JsonElement carga) =>
        DateTimeOffset.ParseExact(carga.GetProperty("ocurridoEn").GetString()!, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.PublicarTecnica(new CodigoDeAlerta(MotivoDeAlerta), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La alerta nunca interrumpe la entrega (AUD-40).
        }
    }
}
