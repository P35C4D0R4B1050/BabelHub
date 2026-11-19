using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>Lo que el aviso necesita del pedido: su número visible y la cuenta opaca del titular (nunca un teléfono).</summary>
internal sealed record DatosParaElAviso(string NumeroDePedido, Guid CuentaId);

/// <summary>
/// Los avisos de Envíos (T10; ENV-06): <b>guía generada</b> y <b>en reparto</b>, por <see cref="INotificador"/> en la misma transacción del cambio de estado, con el evento determinista
/// <c>envio:&lt;envioId&gt;:&lt;estado&gt;:cliente</c>, el destinatario por la referencia opaca de la cuenta y una plantilla de campos cerrados (número del pedido, número de guía y estado; <b>ningún dato de
/// contacto</b>). Envíos no avisa en entregado ni en cancelado (el aviso es el de línea de Pedidos). El Notificador nunca interrumpe la operación: una falla deja el aviso sin crear y el estado cambia igual.
/// </summary>
internal sealed class AvisosDeEnvios(INotificador notificador, IPedidosParaEnvios pedidos)
{
    public const string Plantilla = "envio-guia";

    public const string EstadoGuiaGenerada = "guia-generada";

    public const string EstadoEnReparto = "en-reparto";

    /// <summary>Lee del pedido lo que el aviso necesita, <b>antes</b> de cambiar nada: si Pedidos no responde, la operación se rechaza como no disponible y se reintenta (no se pierde el aviso).</summary>
    public async Task<Resultado<DatosParaElAviso>> Preparar(Guid pedidoId, CancellationToken cancelacion)
    {
        var lectura = await pedidos.Obtener(pedidoId, cancelacion).ConfigureAwait(false);
        return lectura.EsExito
            ? Resultado<DatosParaElAviso>.Exito(new DatosParaElAviso(lectura.Valor.Numero, lectura.Valor.CuentaId))
            : FallosDeEnvios.NoDisponible<DatosParaElAviso>();
    }

    /// <summary>Pide el aviso. Nunca lanza.</summary>
    public async Task Solicitar(Envio envio, DatosParaElAviso datos, string estado, CancellationToken cancelacion)
    {
        try
        {
            await notificador.Solicitar(
                new SolicitudDeAviso(
                    $"envio:{envio.Id}:{estado}:cliente",
                    DestinatarioDelAviso.Cliente,
                    datos.CuentaId.ToString("D"),
                    Plantilla,
                    new Dictionary<string, string>
                    {
                        ["numeroDePedido"] = datos.NumeroDePedido,
                        ["numeroDeGuia"] = envio.NumeroDeGuia?.ToString() ?? string.Empty,
                        ["estado"] = estado,
                    }),
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una falla del Notificador nunca interrumpe un caso de uso (regla 10).
        }
    }
}
