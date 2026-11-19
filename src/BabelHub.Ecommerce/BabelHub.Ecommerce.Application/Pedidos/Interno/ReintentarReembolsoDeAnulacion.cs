using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// El reintento del reembolso de una anulación (T21; PED-24; AC-PED-19): solo la administración central. Si la pasarela falló, el pedido sigue <b>anulado con el reembolso pendiente, visible y reintentable</b> y este caso de uso <b>no repite ningún paso</b> de la anulación: busca el reembolso por su clave natural (<c>anul:&lt;pedido&gt;</c> o <c>canc:&lt;pedido&gt;</c>) y pide a Pagos que lo reintente, y Pagos lo reembolsa <b>una sola vez</b>.
/// Orden: rol → límite de gestión → pedido y alcance (uno inexistente es el mismo «no encontrado» con su evento) → reembolso pendiente (si no hay ninguno es estado inválido; uno ya reembolsado responde éxito sin reintentar ni auditar) → <b>auditoría del reintento antes de llamar a Pagos</b> (sin poder auditar no se reintenta) → <c>Reintentar</c>. Si la pasarela sigue sin responder, el resultado es éxito con
/// <c>ReembolsoPendiente</c> verdadero: el estado del pedido no cambia.
/// </summary>
internal sealed class ReintentarReembolsoDeAnulacion(
    AutorizadorDePedidos autorizador,
    ControlesDeGestionDePedidos controles,
    IRepositorioDePedidos pedidos,
    IReembolsos reembolsos,
    IAuditoria auditoria)
{
    public async Task<Resultado<ResultadoDeAnulacion>> Reintentar(Guid pedidoId, CancellationToken cancelacion)
    {
        var quien = await autorizador.ExigirRol(OperacionDePedidos.Administracion, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(quien.Error!);
        }

        if (await controles.LimiteDeGestion(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(limite);
        }

        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (await autorizador.ComprobarAlcance(pedido, quien.Valor, cancelacion).ConfigureAwait(false) is { } ajeno)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(ajeno);
        }

        var noDisponible = Resultado<ResultadoDeAnulacion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        ReembolsoPendienteVista? vista;
        try
        {
            vista = await Buscar(pedidoId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return noDisponible;
        }

        if (vista is null)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.EstadoInvalido));
        }

        var estado = ProyeccionesDePedidos.AEstadoVisible(pedido!.Estado);
        if (vista.Estado == EstadoDelReembolso.Reembolsado)
        {
            return Resultado<ResultadoDeAnulacion>.Exito(new ResultadoDeAnulacion(pedido.Id, estado, vista.Monto, ReembolsoPendiente: false));
        }

        try
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.ReembolsoDePedidoReintentado)
                {
                    ObjetoTipo = "pedido",
                    ObjetoId = pedido.Id.ToString("D"),
                    Detalle = new Dictionary<string, string?> { ["origen"] = vista.Origen == OrigenDelReembolso.CancelacionDelCliente ? "cancelacion" : "anulacion", ["intentosPrevios"] = vista.Intentos.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            return noDisponible;
        }

        var pendiente = true;
        try
        {
            var reintentado = await reembolsos.Reintentar(vista.Id, cancelacion).ConfigureAwait(false);
            pendiente = !(reintentado.EsExito && reintentado.Valor == EstadoDelReembolso.Reembolsado);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // La pasarela sigue sin responder: el reembolso queda pendiente y visible, el pedido sigue anulado.
        }

        return Resultado<ResultadoDeAnulacion>.Exito(new ResultadoDeAnulacion(pedido.Id, estado, vista.Monto, pendiente));
    }

    /// <summary>El reembolso del pedido por su clave natural: el de la anulación o, si no hay, el de la cancelación del cliente.</summary>
    private async Task<ReembolsoPendienteVista?> Buscar(Guid pedidoId, CancellationToken cancelacion)
    {
        foreach (var origen in new[] { OrigenDelReembolso.Anulacion, OrigenDelReembolso.CancelacionDelCliente })
        {
            var consultado = await reembolsos.ConsultarPorOrigen(origen, OrquestadorDeAnulaciones.ClaveDelReembolso(origen == OrigenDelReembolso.Anulacion ? OrigenDeLaAnulacion.Administracion : OrigenDeLaAnulacion.Cliente, pedidoId), cancelacion).ConfigureAwait(false);
            if (!consultado.EsExito)
            {
                throw new InvalidOperationException("Pagos no pudo consultar el reembolso.");
            }

            if (consultado.Valor is { } vista)
            {
                return vista;
            }
        }

        return null;
    }
}
