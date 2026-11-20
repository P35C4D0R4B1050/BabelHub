using BabelHub.Ecommerce.Api.Pedidos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Sucursal.Controllers;

/// <summary>
/// Los pedidos a domicilio de logística y la administración central (T31, T39; PED-26, PED-28, PED-40, PED-41; AC-PED-06, AC-PED-23, AC-PED-25): <c>GET /logistica/pedidos</c> (lista sin contacto), el detalle (<b>con el contacto,
/// cuya lectura se audita y falla cerrada</b> en la Aplicación) y los <c>POST</c> <c>…/{pedidoId}/preparar</c> y <c>…/listas</c> con <c>lineaIds</c> (al marcar listas se crea el envío). <b>Presentación pura</b>: quién puede
/// (solo logística y la administración central) lo decide la Aplicación con el actor verificado; un encargado de sucursal o un cliente reciben el mismo «no encontrado» o «prohibido» sin pistas. Rechaza un cuerpo con campos que no
/// declara y llama a <see cref="IGestionDeDespacho"/>. Un fallo se pinta en pantalla para un navegador y sale como <c>application/problem+json</c> para quien no pide HTML. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Sucursal")]
[RequestSizeLimit(4096)]
public sealed class PedidosDeLogisticaController(IGestionDeDespacho despacho, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDePedidos(pagina, errores, AreaDeLaPagina.Sucursal)
{
    private const string RutaDeLaLista = "/logistica/pedidos";

    [HttpGet(RutaDeLaLista)]
    public async Task<IActionResult> Lista(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (RechazarConsulta(Request, ["estado", "pagina"]) is { } consulta)
        {
            return await Falla(consulta, RutaDeLaLista, cancelacion);
        }

        if (LeerElFiltro(Request, out var clave, out var filtro) is { } invalido)
        {
            return await Falla(invalido, RutaDeLaLista, cancelacion);
        }

        var resultado = await despacho.Listar(filtro, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        var vista = await ListaComoVista(
            resultado.Valor,
            clave,
            "Pedidos a domicilio",
            "Los pedidos a domicilio de toda la red. La lista no muestra datos de contacto: se ven en el detalle del pedido y cada lectura queda auditada.",
            RutaDeLaLista,
            id => $"{RutaDeLaLista}/{id:D}",
            "Los pedidos a domicilio aparecerán aquí cuando los clientes los compren.",
            cancelacion);
        return View("~/Areas/Sucursal/Views/PedidosDeLogistica/Lista.cshtml", vista);
    }

    [HttpGet(RutaDeLaLista + "/{pedidoId:guid}")]
    public async Task<IActionResult> Detalle(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{pedidoId:D}";
        if (RechazarConsulta(Request, ["hecho", "omitidas"]) is { } consulta)
        {
            return await Falla(consulta, ruta, cancelacion);
        }

        if (LeerElAviso(Request, out var aviso) is { } invalido)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await despacho.Obtener(pedidoId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, ruta, cancelacion);
        }

        var vista = await DetalleDePersonalComoVista(resultado.Valor, RutaDeLaLista, ruta, aviso, cancelacion);
        vista.Acciones = vista.TextosPendientes
            ? []
            :
            [
                new AccionSobreLineasVista("preparar", "Preparar", ruta + "/preparar", "reloj"),
                new AccionSobreLineasVista("listas", "Marcar listas", ruta + "/listas", "bandeja"),
            ];
        return View("~/Areas/Sucursal/Views/PedidosDeLogistica/Detalle.cshtml", vista);
    }

    [HttpPost(RutaDeLaLista + "/{pedidoId:guid}/preparar")]
    public Task<IActionResult> Preparar(Guid pedidoId, CancellationToken cancelacion) =>
        Operar(pedidoId, "preparar", (entrada, c) => despacho.Preparar(entrada, c), cancelacion);

    [HttpPost(RutaDeLaLista + "/{pedidoId:guid}/listas")]
    public Task<IActionResult> MarcarListas(Guid pedidoId, CancellationToken cancelacion) =>
        Operar(pedidoId, "listas", (entrada, c) => despacho.MarcarListas(entrada, c), cancelacion);

    private async Task<IActionResult> Operar(Guid pedidoId, string accion, Func<OperarLineasEntrada, CancellationToken, Task<Resultado<LineasOperadas>>> ejecutar, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{pedidoId:D}";
        var (lineas, invalido) = await LeerLasLineas();
        if (invalido is not null)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await ejecutar(new OperarLineasEntrada(pedidoId, lineas!), cancelacion);
        return resultado.EsExito ? RedirigirTrasOperar(ruta, accion, resultado.Valor) : await Falla(resultado.Error!, ruta, cancelacion);
    }
}
