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

    /// <summary>Las plantillas de la compra confirmada (NOT-02): el pedido confirmado y, si hay libros digitales con su acceso concedido, el libro digital disponible; solo al cliente.</summary>
    public const string PlantillaDelPedidoConfirmado = "pedido-confirmado";

    public const string PlantillaDelLibroDigital = "libro-digital-disponible";

    /// <summary>
    /// Pide los avisos de una compra recién confirmada (NOT-02, AC-NOT-01): <c>pedido-confirmado</c> y, si el pedido tiene una línea digital (su acceso ya se concedió en la misma transacción), <c>libro-digital-disponible</c>, ambos al cliente con la referencia opaca de su cuenta y
    /// eventos deterministas (<c>pedido:&lt;id&gt;:confirmado:compra</c> y <c>pedido:&lt;id&gt;:libro-digital:compra</c>), de modo que repetir la confirmación no duplica el aviso (NOT-05). Nunca lanza: una falla del Notificador no interrumpe la compra (regla 10).
    /// </summary>
    public async Task SolicitarLaConfirmacion(Pedido pedido, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var campos = new Dictionary<string, string> { ["numeroDePedido"] = pedido.Numero.Valor };
        await PedirALaCuenta(pedido, PlantillaDelPedidoConfirmado, "confirmado", campos, cancelacion).ConfigureAwait(false);
        if (pedido.Lineas.Any(l => l.EsDigital))
        {
            await PedirALaCuenta(pedido, PlantillaDelLibroDigital, "libro-digital", campos, cancelacion).ConfigureAwait(false);
        }
    }

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
    /// Las sucursales responsables de las líneas impresas de la operación (PED-05): la que atiende cada una (<see cref="Pedido.SucursalDeLaLinea"/>: la de recojo de la línea o, a domicilio, su fuente); un pedido solo digital no avisa a ninguna (solo al cliente).
    /// </summary>
    public static IReadOnlyList<string> SucursalesResponsables(Pedido pedido, IEnumerable<Guid>? lineaIds = null)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var pedidas = lineaIds?.ToHashSet();
        return [.. pedido.Lineas
            .Where(l => pedidas is null || pedidas.Contains(l.Id))
            .Select(pedido.SucursalDeLaLinea)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
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

    private async Task PedirALaCuenta(Pedido pedido, string plantilla, string hecho, IReadOnlyDictionary<string, string> campos, CancellationToken cancelacion)
    {
        try
        {
            var referencia = pedido.CuentaId.ToString("D");
            var evento = string.Create(CultureInfo.InvariantCulture, $"pedido:{pedido.Id:N}:{hecho}:compra");
            await notificador.Solicitar(new SolicitudDeAviso(evento, DestinatarioDelAviso.Cliente, referencia, plantilla, campos), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una falla del Notificador nunca interrumpe un caso de uso (regla 10): la compra sigue (el aviso no se crea: una repetición de la confirmación no lo vuelve a pedir, como los demás avisos de Pedidos).
        }
    }

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
