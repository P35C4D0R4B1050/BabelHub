using BabelHub.Ecommerce.Api.Pedidos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Sucursal.Controllers;

/// <summary>
/// Los pedidos de recojo del encargado de su sucursal (T31, T38; PED-08, PED-09, PED-11, PED-40, PED-41; AC-PED-07, AC-PED-08, AC-PED-09): <c>GET /sucursal/{codigo}/pedidos</c> (lista), el detalle y los <c>POST</c>
/// <c>…/{pedidoId}/preparar</c>, <c>…/listas</c> y <c>…/entregar</c> con <c>lineaIds</c>. <b>Presentación pura</b>: <b>sin contacto ni cuenta</b> (el tipo de detalle del encargado no los tiene), el código de la ruta
/// <b>no declara <c>[ParametroDeSucursal]</c></b> (el atributo de web-base compara un identificador de sucursal y esta ruta lleva el código: con él todo encargado recibiría «sin permiso») y lo compara la Aplicación con la sucursal del actor (otra sucursal, sin sucursal resoluble o un rol que no es el del encargado responden <b>igual</b>: la administración central no figura en esta
/// capacidad). Rechaza un cuerpo con campos que no declara y llama a <see cref="IGestionDeRecojo"/>. Tras operar responde <c>303</c> al detalle con lo hecho y cuántas líneas no cambiaron (esperan su traslado o ya cambiaron);
/// un fallo se pinta en pantalla para un navegador y sale como <c>application/problem+json</c> para quien no pide HTML. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Sucursal")]
[RequestSizeLimit(4096)]
public sealed class PedidosDeSucursalController(IGestionDeRecojo recojo, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDePedidos(pagina, errores, AreaDeLaPagina.Sucursal)
{
    private const string Plantilla = "/sucursal/{codigo:minlength(1):maxlength(32)}/pedidos";

    [HttpGet(Plantilla)]
    public async Task<IActionResult> Lista(string codigo, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var lista = RutaDeLaLista(codigo);
        if (RechazarConsulta(Request, ["estado", "pagina"]) is { } consulta)
        {
            return await Falla(consulta, lista, cancelacion);
        }

        if (LeerElFiltro(Request, out var clave, out var filtro) is { } invalido)
        {
            return await Falla(invalido, lista, cancelacion);
        }

        var resultado = await recojo.Listar(codigo, filtro, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, lista, cancelacion);
        }

        var vista = await ListaComoVista(
            resultado.Valor,
            clave,
            "Pedidos de recojo",
            "Los pedidos que los clientes recogen en esta sucursal. Aquí no se muestran datos de contacto del cliente.",
            lista,
            id => $"{lista}/{id:D}",
            "Los pedidos de recojo de esta sucursal aparecerán aquí.",
            cancelacion);
        return View("~/Areas/Sucursal/Views/PedidosDeSucursal/Lista.cshtml", vista);
    }

    [HttpGet(Plantilla + "/{pedidoId:guid}")]
    public async Task<IActionResult> Detalle(string codigo, Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var lista = RutaDeLaLista(codigo);
        var ruta = $"{lista}/{pedidoId:D}";
        if (RechazarConsulta(Request, ["hecho", "omitidas"]) is { } consulta)
        {
            return await Falla(consulta, ruta, cancelacion);
        }

        if (LeerElAviso(Request, out var aviso) is { } invalido)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await recojo.Obtener(codigo, pedidoId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, ruta, cancelacion);
        }

        var vista = await NuevoDetalle(resultado.Valor.Resumen, resultado.Valor.Lineas, lista, ruta, aviso, cancelacion);
        vista.Acciones = vista.TextosPendientes
            ? []
            :
            [
                new AccionSobreLineasVista("preparar", "Preparar", ruta + "/preparar", "reloj"),
                new AccionSobreLineasVista("listas", "Marcar listas", ruta + "/listas", "sucursal"),
                new AccionSobreLineasVista("entregar", "Entregar", ruta + "/entregar", "exito"),
            ];
        return View("~/Areas/Sucursal/Views/PedidosDeSucursal/Detalle.cshtml", vista);
    }

    [HttpPost(Plantilla + "/{pedidoId:guid}/preparar")]
    public Task<IActionResult> Preparar(string codigo, Guid pedidoId, CancellationToken cancelacion) =>
        Operar(codigo, pedidoId, "preparar", (entrada, c) => recojo.Preparar(codigo, entrada, c), cancelacion);

    [HttpPost(Plantilla + "/{pedidoId:guid}/listas")]
    public Task<IActionResult> MarcarListas(string codigo, Guid pedidoId, CancellationToken cancelacion) =>
        Operar(codigo, pedidoId, "listas", (entrada, c) => recojo.MarcarListas(codigo, entrada, c), cancelacion);

    [HttpPost(Plantilla + "/{pedidoId:guid}/entregar")]
    public Task<IActionResult> Entregar(string codigo, Guid pedidoId, CancellationToken cancelacion) =>
        Operar(codigo, pedidoId, "entregar", (entrada, c) => recojo.Entregar(codigo, entrada, c), cancelacion);

    private async Task<IActionResult> Operar(string codigo, Guid pedidoId, string accion, Func<OperarLineasEntrada, CancellationToken, Task<Resultado<LineasOperadas>>> ejecutar, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista(codigo)}/{pedidoId:D}";
        var (lineas, invalido) = await LeerLasLineas();
        if (invalido is not null)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await ejecutar(new OperarLineasEntrada(pedidoId, lineas!), cancelacion);
        return resultado.EsExito ? RedirigirTrasOperar(ruta, accion, resultado.Valor) : await Falla(resultado.Error!, ruta, cancelacion);
    }

    private static string RutaDeLaLista(string codigo) => $"/sucursal/{Uri.EscapeDataString(codigo)}/pedidos";
}
