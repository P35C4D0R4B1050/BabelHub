using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// Los avisos de Pedidos (T13; PED-05, PED-06): cada <b>operación</b> (no cada línea) solicita por <see cref="INotificador"/>, <b>en la misma transacción</b>, <b>un aviso al cliente y uno por sucursal responsable distinta</b> (la de recojo o cada fuente de envío; solo el cliente en un pedido solo digital), con el
/// evento determinista <c>pedido:&lt;id&gt;:&lt;estado destino&gt;:&lt;identificador de la operación&gt;:&lt;destinatario&gt;</c>. El destinatario es la referencia opaca de la cuenta o el código de la sucursal (nunca un teléfono) y los campos son cerrados (número del pedido y estado): <b>ningún dato de contacto</b>.
/// El identificador de la operación es el de la anulación o la entrega, la huella de los <c>lineaIds</c> de una preparación, de las listas o de las no recogidas, el <c>envioId</c> o el <c>eventoId</c> del traslado; así dos operaciones del mismo tipo sobre el mismo pedido <b>no</b> se deduplican, preparar y luego
/// marcar listas las mismas líneas dan dos avisos (el estado destino va en el evento) y un pedido de cinco líneas no agota el tope por destinatario. El Notificador nunca interrumpe la operación: una falla deja el aviso pendiente (o sin crear) y el estado cambia igual (PED-06).
/// </summary>
internal sealed class AvisosDePedidos(INotificador notificador)
{
    /// <summary>La plantilla del cliente; cada plantilla del catálogo cerrado tiene un solo tipo de destinatario, así que la sucursal usa la suya.</summary>
    public const string Plantilla = "pedido-estado";

    public const string PlantillaDeLaSucursal = "pedido-estado-sucursal";

    /// <summary>Pide los avisos de una operación. Nunca lanza.</summary>
    public async Task Solicitar(Pedido pedido, string estadoDestino, string identificadorDeLaOperacion, IReadOnlyCollection<string> sucursalesResponsables, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentException.ThrowIfNullOrWhiteSpace(estadoDestino);
        ArgumentException.ThrowIfNullOrWhiteSpace(identificadorDeLaOperacion);
        var campos = new Dictionary<string, string> { ["numeroDePedido"] = pedido.Numero.Valor, ["estado"] = estadoDestino };
        await Pedir(pedido, estadoDestino, identificadorDeLaOperacion, DestinatarioDelAviso.Cliente, pedido.CuentaId.ToString("D"), campos, cancelacion).ConfigureAwait(false);
        foreach (var sucursal in sucursalesResponsables.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
        {
            await Pedir(pedido, estadoDestino, identificadorDeLaOperacion, DestinatarioDelAviso.Sucursal, sucursal, campos, cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// La sucursal responsable: la de recojo o, a domicilio, la fuente de cada línea de la operación; un pedido solo digital no avisa a ninguna (solo al cliente, PED-05).
    /// </summary>
    public static IReadOnlyList<string> SucursalesResponsables(Pedido pedido, IEnumerable<Guid>? lineaIds = null)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return pedido.Tipo switch
        {
            TipoDeEntregaDelPedido.Recojo when pedido.CodigoDeSucursalDeRecojo is { } codigo => [codigo],
            TipoDeEntregaDelPedido.Domicilio => [.. pedido.Lineas
                .Where(l => !l.EsDigital && l.CodigoDeFuente is not null && (lineaIds is null || lineaIds.Contains(l.Id)))
                .Select(l => l.CodigoDeFuente!)
                .Distinct(StringComparer.Ordinal)],
            _ => [],
        };
    }

    /// <summary>El identificador de una operación sobre varias líneas: una huella estable de sus <c>lineaIds</c> (sin importar el orden).</summary>
    public static string HuellaDeLineas(IEnumerable<Guid> lineaIds)
    {
        ArgumentNullException.ThrowIfNull(lineaIds);
        var texto = string.Join(',', lineaIds.Select(i => i.ToString("N")).Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..16].ToLowerInvariant();
    }

    public static string NombreDelEstado(EstadoDePedido estado) => estado switch
    {
        EstadoDePedido.Confirmado => "confirmado",
        EstadoDePedido.EnTransito => "en-transito",
        EstadoDePedido.Recibido => "recibido",
        EstadoDePedido.EnPreparacion => "en-preparacion",
        EstadoDePedido.ListoParaRecojo => "listo-para-recojo",
        EstadoDePedido.Despachado => "despachado",
        EstadoDePedido.Entregado => "entregado",
        EstadoDePedido.NoRecogido => "no-recogido",
        EstadoDePedido.Anulado => "anulado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    private async Task Pedir(Pedido pedido, string estadoDestino, string operacion, DestinatarioDelAviso destinatario, string referencia, IReadOnlyDictionary<string, string> campos, CancellationToken cancelacion)
    {
        try
        {
            var evento = string.Create(CultureInfo.InvariantCulture, $"pedido:{pedido.Id:N}:{estadoDestino}:{operacion}:{referencia}");
            await notificador.Solicitar(new SolicitudDeAviso(evento, destinatario, referencia, destinatario == DestinatarioDelAviso.Cliente ? Plantilla : PlantillaDeLaSucursal, campos), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una falla del Notificador nunca interrumpe un caso de uso (regla 10): el aviso queda pendiente y el estado cambia igual.
        }
    }
}
