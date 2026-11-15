using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// Los límites de reservar (SEL-13, SEL-14, SEL-19, SEL-15), evaluados <b>dentro de la transacción de la reserva y bajo el bloqueo del cliente</b>: (1) una sola reserva pendiente vigente por cliente (otra de otro pedido
/// rechaza con el instante en que vence la suya), (2) 5 reservas iniciadas por hora por cliente y (3) 10 por hora por origen (si la solicitud trae uno). Los contadores del motor cuentan en la transacción del llamador:
/// si la reserva se rechaza después por falta de stock o por otro límite, la transacción se revierte y la solicitud <b>no cuenta como iniciada</b> (SEL-14). El evento de seguridad de un rechazo sale una sola vez, con el
/// cliente y el motivo, <b>fuera</b> de la transacción (persiste aunque esta se revierta): el del motor para SEL-14 y SEL-19 y el propio para SEL-13. La respuesta es uniforme para todos los motivos (SEL-15).
/// </summary>
internal sealed class LimitesDeReservar(
    IControlDeLimites control,
    IPublicadorDeEventosDeSeguridad eventos,
    IRepositorioDeReservas reservas,
    IOrigenDeSolicitud origenes,
    IReloj reloj)
{
    public const string ClaveDeServicioNoDisponible = "limites-no-disponibles";

    /// <summary>La huella de origen que se guarda con la reserva (solo si el origen es conocido y rigen los límites por origen); se anula a las 24 horas (D11).</summary>
    public string? HuellaDeOrigen
    {
        get
        {
            var origen = origenes.Actual;
            return origen.AplicaLimitesPorOrigen && !origen.Huella.EsDesconocido ? origen.Huella.ToString() : null;
        }
    }

    /// <summary>Evalúa los tres límites en orden; devuelve el error del primero que rechaza, o <see langword="null"/> si la solicitud puede continuar.</summary>
    public async Task<ErrorDeAplicacion?> Evaluar(Guid clienteId, Guid pedidoId, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var sujeto = clienteId.ToString("D");

        if (await reservas.ExpiracionDeLaPendienteDelCliente(clienteId, pedidoId, ahora, cancelacion).ConfigureAwait(false) is { } vence)
        {
            await PublicarEventoPropio(MotivosDeLimiteDeReservas.PendientePorCliente, sujeto, ahora, cancelacion).ConfigureAwait(false);
            return Rechazo(new RechazoPorLimite(MotivosDeLimiteDeReservas.PendientePorCliente, Segundos(vence - ahora), vence));
        }

        var porCliente = await control.Reservar(PoliticasDeLimite.ReservasPorHoraPorCliente, sujeto, cancelacion: cancelacion).ConfigureAwait(false);
        if (Traducir(porCliente, MotivosDeLimiteDeReservas.PorHoraPorCliente) is { } errorDelCliente)
        {
            return errorDelCliente;
        }

        // Sin origen del cliente (una dirección reenviada ignorada, SEG-32) el motor deja pasar sin contar.
        var porOrigen = await control.Reservar(PoliticasDeLimite.ReservasPorHoraPorOrigen, cancelacion: cancelacion).ConfigureAwait(false);
        return Traducir(porOrigen, MotivosDeLimiteDeReservas.PorHoraPorOrigen);
    }

    /// <summary>
    /// El rechazo por el tope de reemplazos (SEL-36): con el instante de expiración de la reserva vigente, que se conserva, y el evento de seguridad una sola vez. Es la misma respuesta de límite que los demás motivos.
    /// </summary>
    public async Task<ErrorDeAplicacion> RechazarPorTopeDeReemplazos(Guid clienteId, Domain.Reservas.ReservaDePedido vigente, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var vence = vigente.FechaDeExpiracion ?? ahora.AddMinutes(1);
        await PublicarEventoPropio(MotivosDeLimiteDeReservas.ReemplazosPorPedido, clienteId.ToString("D"), ahora, cancelacion).ConfigureAwait(false);
        return Rechazo(new RechazoPorLimite(MotivosDeLimiteDeReservas.ReemplazosPorPedido, Segundos(vence - ahora), vence));
    }

    /// <summary>El rechazo uniforme de un límite: categoría <c>Limite</c>, clave estable y los tres datos del reintento.</summary>
    public static ErrorDeAplicacion Rechazo(RechazoPorLimite rechazo) =>
        new(CategoriaDeError.Limite, DetalleDeLimiteDeReservas.ClaveDeLimiteExcedido, DetalleDeLimiteDeReservas.Codificar(rechazo));

    private ErrorDeAplicacion? Traducir(ResultadoDeLimite resultado, string motivoPorOmision) =>
        resultado.Estado switch
        {
            EstadoDeLimite.Permitido => null,
            EstadoDeLimite.Excedido => Rechazo(new RechazoPorLimite(resultado.Motivo ?? motivoPorOmision, resultado.EsperaEnSegundos ?? 1, resultado.DisponibleDesde ?? reloj.Ahora)),

            // El almacén de contadores falla cerrado (SEG-26): servicio no disponible y nada se reserva. Las demás respuestas del motor no pueden darse sin clave de idempotencia y se tratan igual.
            _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClaveDeServicioNoDisponible),
        };

    private static int Segundos(TimeSpan espera) => Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));

    private async Task PublicarEventoPropio(string motivo, string usuario, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        try
        {
            // El evento lleva también el origen (huella y dirección, solo si es conocido y rigen los límites por origen) para poder investigar un abuso que solo dispare estos límites (SEG-25, AUD-41).
            var origen = origenes.Actual;
            var conocido = origen.AplicaLimitesPorOrigen && !origen.Huella.EsDesconocido;
            await eventos.Publicar(
                new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, ahora, limite: motivo, origen: conocido ? origen.Huella : null, direccionCompleta: conocido ? origen.Direccion : null, usuario: usuario),
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Un evento que no concede nada no interrumpe el rechazo: la protección se mantiene aunque no se pueda registrar (AUD-12).
        }
    }
}
