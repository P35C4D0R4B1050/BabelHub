using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

// Las fachadas públicas del módulo (plan §4): cada contrato por rol es una delgada traducción a los casos de uso internos, que ya hacen el rol, el alcance, los límites, la auditoría y los eventos. Aquí no hay reglas.

/// <summary>Los pedidos del cliente (PED-07, PED-17, PED-18, PED-41): listar, ver el detalle y cancelar, solo del titular.</summary>
internal sealed class PedidosDelClienteDeLaAplicacion(ConsultasDePedidos consultas, CancelarPedidoDelCliente cancelacionDelCliente) : IPedidosDelCliente
{
    public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default) => consultas.ListarDelCliente(filtro, cancelacion);

    public Task<Resultado<DetalleDelPedidoDelCliente>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) => consultas.ObtenerParaElCliente(pedidoId, cancelacion);

    public Task<Resultado<ResultadoDeCancelacionDePedido>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default) => cancelacionDelCliente.Cancelar(pedidoId, cancelacion);
}

/// <summary>La gestión de recojo del encargado de su sucursal (PED-08, PED-09, PED-11): listar, ver, preparar, marcar listas y entregar.</summary>
internal sealed class GestionDeRecojoDeLaAplicacion(ConsultasDePedidos consultas, OperarLineasDelPedido operaciones, EntregarLineasDelPedido entregas) : IGestionDeRecojo
{
    public Task<Resultado<PaginaDePedidos>> Listar(string codigoDeSucursal, FiltroDePedidos filtro, CancellationToken cancelacion = default) => consultas.ListarDeRecojo(codigoDeSucursal, filtro, cancelacion);

    public Task<Resultado<DetalleDelPedidoDeSucursal>> Obtener(string codigoDeSucursal, Guid pedidoId, CancellationToken cancelacion = default) => consultas.ObtenerParaLaSucursal(codigoDeSucursal, pedidoId, cancelacion);

    public Task<Resultado<LineasOperadas>> Preparar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default) => operaciones.PrepararDeRecojo(codigoDeSucursal, entrada, cancelacion);

    public Task<Resultado<LineasOperadas>> MarcarListas(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default) => operaciones.MarcarListasDeRecojo(codigoDeSucursal, entrada, cancelacion);

    public Task<Resultado<LineasOperadas>> Entregar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default) => entregas.Entregar(codigoDeSucursal, entrada, cancelacion);
}

/// <summary>La gestión de pedidos a domicilio de logística y la administración central (PED-26, PED-28): listar, ver con el contacto auditado, preparar y marcar listas.</summary>
internal sealed class GestionDeDespachoDeLaAplicacion(ConsultasDePedidos consultas, OperarLineasDelPedido operaciones) : IGestionDeDespacho
{
    public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default) => consultas.ListarADomicilio(filtro, cancelacion);

    public Task<Resultado<DetalleDelPedidoDePersonal>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) => consultas.ObtenerParaElPersonal(OperacionDePedidos.GestionDeDespacho, pedidoId, cancelacion);

    public Task<Resultado<LineasOperadas>> Preparar(OperarLineasEntrada entrada, CancellationToken cancelacion = default) => operaciones.PrepararADomicilio(entrada, cancelacion);

    public Task<Resultado<LineasOperadas>> MarcarListas(OperarLineasEntrada entrada, CancellationToken cancelacion = default) => operaciones.MarcarListasADomicilio(entrada, cancelacion);
}

/// <summary>La administración central (PED-13, PED-14, PED-16, PED-24): todos los pedidos, anular, reintentar el reembolso y las operaciones fallidas.</summary>
internal sealed class AdministracionDePedidosDeLaAplicacion(ConsultasDePedidos consultas, AnularPedido anulacion, ReintentarReembolsoDeAnulacion reintento, OperacionesFallidasDePedidos fallidas) : IAdministracionDePedidos
{
    public Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default) => consultas.ListarDeLaAdministracion(filtro, cancelacion);

    public Task<Resultado<DetalleDelPedidoDePersonal>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) => consultas.ObtenerParaElPersonal(OperacionDePedidos.Administracion, pedidoId, cancelacion);

    public Task<Resultado<ResultadoDeAnulacion>> Anular(AnularPedidoEntrada entrada, CancellationToken cancelacion = default) => anulacion.Anular(entrada, cancelacion);

    public Task<Resultado<ResultadoDeAnulacion>> ReintentarElReembolso(Guid pedidoId, CancellationToken cancelacion = default) => reintento.Reintentar(pedidoId, cancelacion);

    public Task<Resultado<PaginaDeOperacionesFallidas>> ListarOperacionesFallidas(int pagina, int tamano, CancellationToken cancelacion = default) => fallidas.Listar(pagina, tamano, cancelacion);
}
