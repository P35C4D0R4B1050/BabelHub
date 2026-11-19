using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// Los avisos de Devoluciones (T14; DEV-11, DEV-29; AC-DEV-10): al cliente, por <see cref="INotificador"/> y <b>en la misma transacción de cada cambio posterior a la solicitud</b> (aprobada, rechazada —también por plazo—, recibida y reembolsada), con el evento determinista
/// <c>devolucion:&lt;devolucionId&gt;:&lt;estado&gt;:&lt;huella de las líneas&gt;</c> (la huella distingue el reembolso de cada línea y hace que repetir un cambio no avise dos veces), el destinatario por la <b>cuenta opaca que se lee de Pedidos</b> en cada cambio (Devoluciones no la guarda, ni siquiera en los trabajos
/// del «sistema») y la plantilla <c>devolucion-estado</c> de campos cerrados (la devolución y el estado): <b>ni el motivo ni el texto</b> llegan jamás aquí (DEV-29). El Notificador nunca interrumpe un cambio (regla 10): una falla deja el aviso sin crear y el estado cambia igual.
/// </summary>
internal sealed class AvisosDeDevoluciones(INotificador notificador, IPedidosParaDevoluciones pedidos, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string Plantilla = "devolucion-estado";

    /// <summary>
    /// La cuenta que recibe el aviso, leída de Pedidos <b>antes</b> de abrir la transacción del cambio: si Pedidos no responde, el cambio se pospone (la operación responde «no disponible» o el trabajo lo reintenta) y el aviso no se pierde. Nunca lanza.
    /// </summary>
    public async Task<Guid?> Destinatario(Guid pedidoId, CancellationToken cancelacion)
    {
        try
        {
            var lectura = await pedidos.Obtener(pedidoId, cancelacion).ConfigureAwait(false);
            return lectura.EsExito ? lectura.Valor.CuentaId : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Pide el aviso del estado al que acaban de pasar las líneas. Nunca lanza.</summary>
    public async Task Solicitar(Devolucion devolucion, Guid cuentaId, EstadoDeLineaDeDevolucion estado, CancellationToken cancelacion)
    {
        try
        {
            var resultado = await notificador.Solicitar(
                new SolicitudDeAviso(
                    $"devolucion:{devolucion.Id:D}:{estado.Codigo()}:{Huella(devolucion, estado)}",
                    DestinatarioDelAviso.Cliente,
                    cuentaId.ToString("D"),
                    Plantilla,
                    new Dictionary<string, string>
                    {
                        ["devolucionId"] = devolucion.Id.ToString("D"),
                        ["estado"] = estado.Codigo(),
                    }),
                cancelacion).ConfigureAwait(false);

            if (resultado == ResultadoDeLaSolicitudDeAviso.Rechazado)
            {
                // La plantilla o los campos no son válidos: es un defecto de programación que debe verse (el cambio sigue).
                await alertas.Publicar(new AlertaTecnica(ClavesDeDevoluciones.AlertaGenerica, reloj.Ahora), cancelacion).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una falla del Notificador nunca interrumpe un caso de uso (regla 10).
        }
    }

    /// <summary>Ocho caracteres hexadecimales de las líneas que están en ese estado (ordenadas): el mismo cambio da el mismo evento, y el reembolso de otra línea da otro.</summary>
    private static string Huella(Devolucion devolucion, EstadoDeLineaDeDevolucion estado)
    {
        var lineas = string.Join(',', devolucion.Lineas.Where(l => l.Estado == estado).Select(l => l.LineaDePedidoId.ToString("N")).Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lineas)))[..8].ToLowerInvariant();
    }
}
