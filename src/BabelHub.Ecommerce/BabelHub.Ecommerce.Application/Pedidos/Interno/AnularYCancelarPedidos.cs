using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La cancelación del cliente (T19; PED-17, PED-18, PED-21; AC-PED-15, 16, 17): solo el titular, con los textos legales al día (se leen <b>antes</b> de bloquear el pedido) y el límite de gestión. Solo se cancela un pedido de <b>impresas todas confirmadas</b>: con una digital o un impreso en camino se rechaza <b>con su alternativa</b> (política de reembolso digital o devolución) y el rechazo se audita. Libera con
/// <c>cancelado-por-el-cliente</c> (el Inventario cancela los traslados vinculados), reembolsa el total con el envío y no reembolsa dos veces (clave <c>canc:&lt;pedido&gt;</c>). El motor de tres tiempos es <see cref="OrquestadorDeAnulaciones"/>.
/// </summary>
internal sealed class CancelarPedidoDelCliente(AutorizadorDePedidos autorizador, ControlesDeGestionDePedidos controles, OrquestadorDeAnulaciones orquestador)
{
    public async Task<Resultado<ResultadoDeCancelacionDePedido>> Cancelar(Guid pedidoId, CancellationToken cancelacion)
    {
        var quien = await autorizador.ExigirRol(OperacionDePedidos.PedidosDelCliente, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<ResultadoDeCancelacionDePedido>.Fallo(quien.Error!);
        }

        if (await controles.LimiteDeGestion(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<ResultadoDeCancelacionDePedido>.Fallo(limite);
        }

        // Orden de bloqueo cuenta → pedido: los textos se leen sin bloqueo, antes de bloquear el pedido.
        if (await controles.ExigirTextosAlDia(quien.Valor.UsuarioId, cancelacion).ConfigureAwait(false) is { } textos)
        {
            return Resultado<ResultadoDeCancelacionDePedido>.Fallo(textos);
        }

        var iniciada = await orquestador.Iniciar(quien.Valor, pedidoId, OrigenDeLaAnulacion.Cliente, MotivoDeAnulacion.CanceladoPorElCliente, cancelacion).ConfigureAwait(false);
        if (!iniciada.EsExito)
        {
            return Resultado<ResultadoDeCancelacionDePedido>.Fallo(iniciada.Error!);
        }

        var aplicada = await orquestador.Continuar(iniciada.Valor, reanudacion: false, cancelacion).ConfigureAwait(false);
        return aplicada.EsExito
            ? Resultado<ResultadoDeCancelacionDePedido>.Exito(new ResultadoDeCancelacionDePedido(aplicada.Valor.PedidoId, ProyeccionesDePedidos.AEstadoVisible(aplicada.Valor.Estado), aplicada.Valor.Monto))
            : Resultado<ResultadoDeCancelacionDePedido>.Fallo(aplicada.Error!);
    }
}

/// <summary>
/// La anulación de la administración central (T20; PED-13, PED-16, PED-22, PED-23, PED-27; AC-PED-11, 14, 18, 20, 22): solo la administración central, con el motivo <b>obligatorio de la lista cerrada</b> (<c>pago-duplicado</c>, <c>no-recogido</c>, <c>otro</c>; «cancelado por el cliente» no le corresponde) y el límite de gestión. Anula lo que <b>no salió</b>; las digitales solo si ninguna impresa salió; reembolsa antes de revocar los accesos aunque se
/// hayan descargado. Un pedido sin impresas por salir (también uno solo digital) se rechaza y el rechazo se audita. El motor de tres tiempos es <see cref="OrquestadorDeAnulaciones"/>.
/// </summary>
internal sealed class AnularPedido(AutorizadorDePedidos autorizador, ControlesDeGestionDePedidos controles, OrquestadorDeAnulaciones orquestador)
{
    public async Task<Resultado<ResultadoDeAnulacion>> Anular(AnularPedidoEntrada entrada, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var quien = await autorizador.ExigirRol(OperacionDePedidos.Administracion, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(quien.Error!);
        }

        if (await controles.LimiteDeGestion(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(limite);
        }

        if (!MotivosDeAnulacion.TryParse(entrada.Motivo, out var motivo) || !MotivosDeAnulacion.EsDeLaAdministracion(motivo))
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(new ErrorDeAplicacion(
                CategoriaDeError.Validacion,
                ClavesDePedidos.ValidacionFallida,
                [new ErrorDeCampo("motivo", string.IsNullOrWhiteSpace(entrada.Motivo) ? "obligatorio" : "invalido")]));
        }

        var iniciada = await orquestador.Iniciar(quien.Valor, entrada.PedidoId, OrigenDeLaAnulacion.Administracion, motivo, cancelacion).ConfigureAwait(false);
        if (!iniciada.EsExito)
        {
            return Resultado<ResultadoDeAnulacion>.Fallo(iniciada.Error!);
        }

        var aplicada = await orquestador.Continuar(iniciada.Valor, reanudacion: false, cancelacion).ConfigureAwait(false);
        return aplicada.EsExito
            ? Resultado<ResultadoDeAnulacion>.Exito(new ResultadoDeAnulacion(aplicada.Valor.PedidoId, ProyeccionesDePedidos.AEstadoVisible(aplicada.Valor.Estado), aplicada.Valor.Monto, aplicada.Valor.ReembolsoPendiente))
            : Resultado<ResultadoDeAnulacion>.Fallo(aplicada.Error!);
    }
}
