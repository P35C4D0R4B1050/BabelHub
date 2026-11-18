using System.Globalization;
using System.Text.Json;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// Convierte un traslado en la respuesta del contrato, en la entrada de auditoría y en los mensajes del buzón. La auditoría usa solo la lista blanca del catálogo (número, edición, cantidad, estado anterior y nuevo, si es
/// vinculado y el pedido opaco; para la guía, el valor nuevo y si reemplazó a otra) y las sucursales de origen y destino en las columnas del registro: <b>nunca</b> el motivo ni el motivo de cierre (D07, TRA-13). Los mensajes
/// llevan solo códigos, identificadores y campos tipados.
/// </summary>
internal static class ConversionDeTraslados
{
    public static TrasladoRegistrado ARespuesta(Traslado traslado, FuenteDeStock origen, FuenteDeStock destino) =>
        new(
            traslado.Id,
            traslado.Numero,
            traslado.EdicionId,
            origen.Codigo,
            destino.Codigo,
            traslado.Cantidad,
            traslado.Estado.Identificador(),
            traslado.Motivo,
            traslado.MotivoDeCierre,
            traslado.PedidoId,
            traslado.EsVinculado,
            traslado.PedidoAnulado,
            traslado.FechaEstimada,
            traslado.EstimadaPorConfirmar,
            traslado.Guia,
            traslado.SolicitadoPor,
            traslado.SolicitadoEn,
            traslado.AprobadoEn,
            traslado.DespachadoEn,
            traslado.RecibidoEn,
            traslado.CerradoEn,
            traslado.Version);

    /// <summary>La acción del catálogo de una transición: la del estado al que pasó el traslado.</summary>
    public static AccionAuditable AccionDeLaTransicion(EstadoDeTraslado nuevo) =>
        nuevo switch
        {
            EstadoDeTraslado.Solicitado => AccionesAuditables.TrasladoSolicitado,
            EstadoDeTraslado.Aprobado => AccionesAuditables.TrasladoAprobado,
            EstadoDeTraslado.Rechazado => AccionesAuditables.TrasladoRechazado,
            EstadoDeTraslado.Cancelado => AccionesAuditables.TrasladoCancelado,
            EstadoDeTraslado.EnTransito => AccionesAuditables.TrasladoDespachado,
            _ => AccionesAuditables.TrasladoRecibido,
        };

    /// <summary>La entrada de auditoría de un cambio de estado (TRA-13): el actor sale del contexto verificado, no de aquí.</summary>
    public static SolicitudDeRegistro Auditar(Traslado traslado, EstadoDeTraslado? estadoAnterior)
    {
        var detalle = new Dictionary<string, string?>
        {
            ["numero"] = traslado.Numero.ToString(CultureInfo.InvariantCulture),
            ["edicionId"] = traslado.EdicionId.ToString("D"),
            ["cantidad"] = traslado.Cantidad.ToString(CultureInfo.InvariantCulture),
            ["vinculado"] = traslado.EsVinculado ? "true" : "false",
        };

        if (estadoAnterior is { } anterior)
        {
            detalle["estadoAnterior"] = anterior.Identificador();
            detalle["estadoNuevo"] = traslado.Estado.Identificador();
        }

        if (traslado.PedidoId is { } pedido)
        {
            detalle["pedidoId"] = pedido.ToString("D");
        }

        return new SolicitudDeRegistro(AccionDeLaTransicion(traslado.Estado))
        {
            ObjetoTipo = "traslado",
            ObjetoId = traslado.Id.ToString("D"),
            SucursalOrigen = traslado.OrigenId,
            SucursalDestino = traslado.DestinoId,
            Detalle = detalle,
        };
    }

    /// <summary>La entrada de auditoría de una guía registrada: la guía nueva (valor del catálogo) y si reemplazó a otra; el estado no cambia.</summary>
    public static SolicitudDeRegistro AuditarGuia(Traslado traslado, bool reemplazoOtraGuia) =>
        new(AccionesAuditables.GuiaDeTrasladoRegistrada)
        {
            ObjetoTipo = "traslado",
            ObjetoId = traslado.Id.ToString("D"),
            SucursalOrigen = traslado.OrigenId,
            SucursalDestino = traslado.DestinoId,
            Detalle = new Dictionary<string, string?>
            {
                ["numero"] = traslado.Numero.ToString(CultureInfo.InvariantCulture),
                ["guiaNueva"] = traslado.Guia,
                ["reemplazoOtraGuia"] = reemplazoOtraGuia ? "true" : "false",
            },
        };

    /// <summary>
    /// Los mensajes de un cambio de estado (plan §3 y §5): un aviso a la sucursal de origen y otro a la de destino (TRA-10) y, si el traslado está vinculado y pasó a en tránsito, recibido o cancelado, el estado del pedido para el
    /// E-commerce con el código del destino (TRA-11). No se informa un recibido, ni un en tránsito, de un pedido ya anulado: solo el cancelado. La clave del evento es <c>traslado:&lt;id&gt;:&lt;estado&gt;</c>, única por sucursal y tipo.
    /// </summary>
    public static IReadOnlyCollection<MensajeSaliente> Mensajes(Traslado traslado, string codigoDeOrigen, string codigoDeDestino, DateTimeOffset ahora)
    {
        var estado = traslado.Estado.Identificador();
        var clave = $"traslado:{traslado.Id:D}:{estado}";
        var mensajes = new List<MensajeSaliente>
        {
            Aviso(clave, codigoDeOrigen, "origen", traslado, estado, ahora),
            Aviso(clave, codigoDeDestino, "destino", traslado, estado, ahora),
        };

        var informaAlPedido = traslado.Estado switch
        {
            EstadoDeTraslado.Cancelado => true,
            EstadoDeTraslado.EnTransito or EstadoDeTraslado.Recibido => !traslado.PedidoAnulado,
            _ => false,
        };

        if (traslado.EsVinculado && informaAlPedido)
        {
            mensajes.Add(EstadoDePedido(clave, codigoDeDestino, traslado, estado, ahora));
        }

        return mensajes;
    }

    private static MensajeSaliente Aviso(string clave, string sucursal, string rol, Traslado traslado, string estado, DateTimeOffset ahora) =>
        new(Guid.NewGuid(), TiposDeMensaje.Aviso, clave, sucursal, Serializar(
            ("numeroDeTraslado", traslado.Numero),
            ("estado", estado),
            ("rolDeLaSucursal", rol),
            ("cantidad", traslado.Cantidad),
            ("ocurridoEn", ahora.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))), ahora);

    private static MensajeSaliente EstadoDePedido(string clave, string codigoDeDestino, Traslado traslado, string estado, DateTimeOffset ahora) =>
        new(Guid.NewGuid(), TiposDeMensaje.EstadoDePedido, clave, codigoDeDestino, Serializar(
            ("pedidoId", traslado.PedidoId!.Value.ToString("D")),
            ("trasladoId", traslado.Id.ToString("D")),
            ("edicionId", traslado.EdicionId.ToString("D")),
            ("cantidad", traslado.Cantidad),
            ("estado", estado),
            ("fechaEstimadaDeLlegada", traslado.FechaEstimada?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("estimadaPorConfirmar", traslado.EstimadaPorConfirmar),
            ("ocurridoEn", ahora.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))), ahora);

    private static string Serializar(params (string Campo, object? Valor)[] campos) =>
        JsonSerializer.Serialize(campos.ToDictionary(c => c.Campo, c => c.Valor));
}
