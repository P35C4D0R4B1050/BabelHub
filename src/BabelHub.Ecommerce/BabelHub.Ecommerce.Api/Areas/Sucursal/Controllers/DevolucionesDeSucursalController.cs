using BabelHub.Ecommerce.Api.Devoluciones;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Sucursal.Controllers;

/// <summary>
/// Las devoluciones de impresos de la sucursal receptora (T24, T27; DEV-07, DEV-13, DEV-28; AC-DEV-12, AC-DEV-30): <c>GET /sucursal/{codigo}/devoluciones</c> (lista), el detalle y los <c>POST</c> <c>…/{id}/aprobar</c>, <c>…/rechazar</c> y <c>…/recibir</c>.
/// <b>Presentación pura</b>: el código de la ruta se declara con <c>[ParametroDeSucursal]</c> y lo compara la Aplicación con la sucursal del actor (otra sucursal, sin sucursal resoluble o un rol que no es el del encargado responden <b>igual</b>); un encargado
/// nunca ve un reporte ni un libro digital. Rechaza un cuerpo con campos que no declara y no admite adjuntos. Llama a <see cref="IGestionDeDevoluciones"/>. Tras operar responde <c>303</c> al detalle; con el Inventario caído la recepción no pasa a recibida y se pinta el
/// error reintentable. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Sucursal")]
[RequestSizeLimit(4096)]
public sealed class DevolucionesDeSucursalController(IGestionDeDevoluciones gestion, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDeDevoluciones(pagina, errores, AreaDeLaPagina.Sucursal)
{
    private const string Plantilla = "/sucursal/{codigo:minlength(1):maxlength(40)}/devoluciones";

    private static string Lista(string codigo) => $"/sucursal/{codigo}/devoluciones";

    [HttpGet(Plantilla)]
    [ParametroDeSucursal("codigo")]
    public async Task<IActionResult> ListaDeLaSucursal(string codigo, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var lista = Lista(codigo);
        if (LeerLaConsulta(Request, ["pagina", "hecho"], out var numero, out var hecho) is { } invalida)
        {
            return await Falla(invalida, lista, cancelacion);
        }

        var resultado = await gestion.ListarDeLaSucursal(codigo, new ListarDevolucionesEntrada(numero, TamanoDePagina), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, lista, cancelacion);
        }

        var vista = await ComoLista(
            resultado.Valor,
            "Devoluciones de impresos",
            "Los libros impresos que los clientes devuelven a esta sucursal. Aprueba o rechaza cada solicitud y confirma la recepción cuando el libro llegue.",
            lista,
            lista,
            hecho,
            "Las devoluciones de impresos de esta sucursal aparecerán aquí.",
            cancelacion);
        return View("~/Areas/Tienda/Views/Devoluciones/Lista.cshtml", vista);
    }

    [HttpGet(Plantilla + "/{devolucionId:guid}")]
    [ParametroDeSucursal("codigo")]
    public async Task<IActionResult> Detalle(string codigo, Guid devolucionId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var lista = Lista(codigo);
        if (LeerLaConsulta(Request, ["hecho"], out _, out var hecho) is { } invalida)
        {
            return await Falla(invalida, lista, cancelacion);
        }

        var resultado = await gestion.Obtener(devolucionId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, lista, cancelacion);
        }

        var vista = await Pagina.Completar(new DetalleDeDevolucionVista(), "Devolución", AreaDeLaPagina.Sucursal, cancelacion);
        var devolucion = APantalla(resultado.Valor, lista);
        vista.Devolucion = devolucion;
        vista.RutaDeLaLista = lista;
        vista.Aviso = Aviso(hecho);
        vista.Acciones = [.. AccionesDeLaSucursal(resultado.Valor, devolucion.Ruta)];
        return View("~/Areas/Tienda/Views/Devoluciones/Detalle.cshtml", vista);
    }

    [HttpPost(Plantilla + "/{devolucionId:guid}/aprobar")]
    [ParametroDeSucursal("codigo")]
    public Task<IActionResult> Aprobar(string codigo, Guid devolucionId, CancellationToken cancelacion) =>
        Operar(codigo, devolucionId, "aprobada", c => gestion.Aprobar(devolucionId, c), cancelacion);

    [HttpPost(Plantilla + "/{devolucionId:guid}/rechazar")]
    [ParametroDeSucursal("codigo")]
    public Task<IActionResult> Rechazar(string codigo, Guid devolucionId, CancellationToken cancelacion) =>
        Operar(codigo, devolucionId, "rechazada", c => gestion.Rechazar(devolucionId, c), cancelacion);

    [HttpPost(Plantilla + "/{devolucionId:guid}/recibir")]
    [ParametroDeSucursal("codigo")]
    public Task<IActionResult> ConfirmarRecepcion(string codigo, Guid devolucionId, CancellationToken cancelacion) =>
        Operar(codigo, devolucionId, "recibida", c => gestion.ConfirmarRecepcion(devolucionId, c), cancelacion);

    private async Task<IActionResult> Operar(string codigo, Guid devolucionId, string aviso, Func<CancellationToken, Task<Resultado<DevolucionVista>>> ejecutar, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{Lista(codigo)}/{devolucionId:D}";
        var (_, error) = await LeerFormulario(_ => false);
        if (error is not null)
        {
            return await Falla(error, ruta, cancelacion);
        }

        var resultado = await ejecutar(cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, ruta, cancelacion);
        }

        return Redirigir($"{ruta}?hecho={aviso}");
    }

    private static IEnumerable<AccionDeDevolucionVista> AccionesDeLaSucursal(DevolucionVista devolucion, string ruta)
    {
        if (devolucion.Lineas.Any(l => l.Estado == EstadoDeLaLineaDeDevolucion.Solicitada))
        {
            yield return new AccionDeDevolucionVista("Aprobar", ruta + "/aprobar", "exito", true);
            yield return new AccionDeDevolucionVista("Rechazar", ruta + "/rechazar", "candado", false);
        }

        if (devolucion.Lineas.Any(l => l.Estado == EstadoDeLaLineaDeDevolucion.Aprobada))
        {
            yield return new AccionDeDevolucionVista("Confirmar recepción", ruta + "/recibir", "sucursal", true);
        }
    }
}
