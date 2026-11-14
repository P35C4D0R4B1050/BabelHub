using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// Puerto de escritura del acumulado persistente de rechazos por límite (<c>auditoria.acumulado_de_limites</c>, AUD-28, AUD-41, D11, T25): cada rechazo se
/// guarda <b>sin diferirlo</b> (<c>INSERT … ON CONFLICT DO UPDATE</c> sobre la fila de la ventana, bajo su bloqueo), con el tope de 1000 filas por ventana y la
/// agrupación en «otros» que decide <see cref="AcumuladoDeLimite"/>. La dirección completa solo vive aquí hasta escribir el evento del cierre.
/// </summary>
public interface IAcumuladorDeLimites
{
    Task<ResultadoDeRegistroDeRechazo> Registrar(string huella, string limite, DateTimeOffset instante, string? direccionCompleta, CancellationToken cancelacion = default);
}

/// <summary>
/// El conteo local de eventos que la Infraestructura responde con un índice parcial por acción e instante (T25): cuántos registros de una acción hay en
/// <c>auditoria.registros</c> y cuántos rechazos de un límite hay en <c>auditoria.acumulado_de_limites</c> en una ventana. Solo devuelve un número.
/// </summary>
public interface IConteoLocalDeEventos
{
    Task<int> ContarRegistros(AccionAuditable accion, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default);

    /// <summary>Los rechazos acumulados del límite (o de todos si <paramref name="limite"/> es nulo) cuya ventana cae en el rango.</summary>
    Task<int> ContarRechazosDeLimite(string? limite, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default);
}

/// <summary>
/// Implementa <see cref="IPublicadorDeEventosDeSeguridad"/> (puerto de <c>Plataforma</c>, D18): un exceso de límite va al acumulador persistente (un solo evento
/// por usuario u origen, límite y ventana, AUD-28) y el resto son eventos de seguridad que no conceden nada, escritos con un plazo propio. <b>Nunca interrumpe</b>:
/// si no se puede guardar, la protección sigue y queda una alerta técnica (AUD-12). El actor es el del contexto o «anónimo» (estos eventos ocurren antes de
/// autenticar) y el origen es la huella, nunca la dirección (salvo el acumulado, que la borra al escribir el evento, AUD-41).
/// </summary>
public sealed class PublicadorDeEventosDeSeguridad(
    ConstructorDeRegistros constructor,
    IAlmacenDeEventosDeSeguridad almacen,
    IAcumuladorDeLimites acumulador,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IPublicadorDeEventosDeSeguridad
{
    public async Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(evento);
        try
        {
            if (evento.Tipo == TipoDeEventoDeSeguridad.LimiteExcedido)
            {
                await AcumularLimite(evento).ConfigureAwait(false);
                return;
            }

            await EscribirEvento(evento).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await AlertarSinInterrumpir(
                evento.Tipo == TipoDeEventoDeSeguridad.LimiteExcedido ? MotivosDeAlerta.AcumuladoDeLimitesNoDisponible : MotivosDeAlerta.AuditoriaNoDisponible).ConfigureAwait(false);
        }
    }

    private async Task AcumularLimite(EventoDeSeguridad evento)
    {
        // La huella del sujeto: la del origen o, si el límite es por usuario, el identificador del usuario (nunca el correo ni la dirección).
        var huella = evento.Origen is { } origen
            ? origen.Valor
            : "u-" + new string((evento.Usuario ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(126).ToArray());

        var instante = evento.Instante.ToUniversalTime();
        await PlazoDeEscrituraDeSeguridad.Ejecutar(token => acumulador.Registrar(huella, evento.Limite!, instante, evento.DireccionCompleta, token)).ConfigureAwait(false);
    }

    private async Task EscribirEvento(EventoDeSeguridad evento)
    {
        var (accion, motivo) = evento.Tipo switch
        {
            TipoDeEventoDeSeguridad.OrigenIndeterminable => (AccionesAuditables.OrigenIndeterminable, "origen-no-determinable"),
            TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial => (AccionesAuditables.LlamadaEntreServiciosSinCredencial, "sin-credencial-de-servicio"),
            TipoDeEventoDeSeguridad.VerificacionDePersonaFallida => (AccionesAuditables.VerificacionDePersonaFallida, "verificacion-fallida"),
            TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial => (AccionesAuditables.OrigenReenviadoSinCredencial, "origen-reenviado-sin-credencial"),
            TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada => (AccionesAuditables.EntradaDeIntegracionRechazada, evento.Motivo!),
            _ => throw new ArgumentOutOfRangeException(nameof(evento), "El tipo de evento no se escribe como un evento de seguridad."),
        };

        var solicitud = new SolicitudDeRegistro(accion)
        {
            Motivo = motivo,
            Origen = evento.Origen is { EsDesconocido: false } o ? o.ToString() : null,
            Resultado = ResultadoAuditado.Rechazado,
            Detalle = evento.Tipo == TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada && evento.Proveedor is not null
                ? new Dictionary<string, string?> { ["proveedor"] = evento.Proveedor }
                : new Dictionary<string, string?>(),
        };
        var definicion = constructor.Resolver(solicitud, ClaseDeAccion.NoConcede);
        var registro = constructor.Construir(solicitud, definicion, anonimoSinContexto: true);

        await PlazoDeEscrituraDeSeguridad.Ejecutar(token => almacen.Insertar(registro, token)).ConfigureAwait(false);
    }

    private async Task AlertarSinInterrumpir(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // El registro técnico tampoco responde: la protección sigue.
        }
    }
}
