using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La lista de anulaciones y entregas fallidas de la administración (T21, T40; PED-24; AC-PED-19): solo la administración central (rol → límite de consultas), de la más reciente a la más antigua y <b>sin datos personales</b>: el pedido, el tipo, el motivo cerrado y los pasos que alcanzó a hacer, para decidir si se repite la anulación o se reintenta el reembolso.
/// </summary>
internal sealed class OperacionesFallidasDePedidos(AutorizadorDePedidos autorizador, ControlesDeGestionDePedidos controles, IRepositorioDePedidos pedidos)
{
    public async Task<Resultado<PaginaDeOperacionesFallidas>> Listar(int pagina, int tamano, CancellationToken cancelacion)
    {
        var quien = await autorizador.ExigirRol(OperacionDePedidos.Administracion, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<PaginaDeOperacionesFallidas>.Fallo(quien.Error!);
        }

        if (await controles.LimiteDeConsultas(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<PaginaDeOperacionesFallidas>.Fallo(limite);
        }

        var paginaPedida = Math.Clamp(pagina, 1, 10_000);
        var tamanoPedido = Math.Clamp(tamano, 1, FiltroDePedidos.TamanoMaximo);
        var guardadas = await pedidos.OperacionesFallidas(paginaPedida, tamanoPedido, cancelacion).ConfigureAwait(false);
        return Resultado<PaginaDeOperacionesFallidas>.Exito(new PaginaDeOperacionesFallidas(
            [.. guardadas.Operaciones.Select(o => new OperacionFallidaVisible(o.Id, o.PedidoId, o.Tipo, o.Motivo, o.PasosHechos, o.CreadaEn))],
            paginaPedida,
            tamanoPedido,
            guardadas.Total));
    }
}
