using BabelHub.Ecommerce.Api.Devoluciones;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>
/// Las devoluciones de la administración central (T24, T28; DEV-13, DEV-14, DEV-20, DEV-24, DEV-28; AC-DEV-20, AC-DEV-23, AC-DEV-24): <c>GET /administracion/devoluciones</c> (todas, con filtro por tipo y página), <c>…/reportes</c>, el detalle y los <c>POST</c>
/// <c>…/{id}/aprobar</c>, <c>…/rechazar</c>, <c>…/recibir</c>, <c>…/aprobar-reporte</c> y <c>…/rechazar-reporte</c>. <b>Presentación pura</b>: quién puede (solo la administración central; un encargado no ve los reportes) lo decide la Aplicación con el actor verificado. Rechaza un cuerpo con campos
/// que no declara y no admite adjuntos. Llama a <see cref="IGestionDeDevoluciones"/>. Un fallo se pinta en pantalla para un navegador y sale como <c>application/problem+json</c> para quien no pide HTML. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Admin")]
[RequestSizeLimit(4096)]
public sealed class AdministracionDeDevolucionesController(IGestionDeDevoluciones gestion, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDeDevoluciones(pagina, errores, AreaDeLaPagina.Admin)
{
    private const string RutaDeLaLista = "/administracion/devoluciones";

    private const string RutaDeLosReportes = RutaDeLaLista + "/reportes";

    [HttpGet(RutaDeLaLista)]
    public async Task<IActionResult> Lista(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (LeerLaConsulta(Request, ["tipo", "pagina", "hecho"], out var numero, out var hecho) is { } invalida)
        {
            return await Falla(invalida, RutaDeLaLista, cancelacion);
        }

        TipoDeLaDevolucion? tipo = null;
        if (Request.Query["tipo"].ToString() is { Length: > 0 } pedido)
        {
            tipo = pedido switch { "impreso" => TipoDeLaDevolucion.Impreso, "digital" => TipoDeLaDevolucion.Digital, _ => null };
            if (tipo is null)
            {
                return await Falla(Invalido("tipo"), RutaDeLaLista, cancelacion);
            }
        }

        return await Listar(new ListarDevolucionesDeLaAdministracionEntrada(tipo, false, numero, TamanoDePagina), "Devoluciones", RutaDeLaLista, hecho, cancelacion);
    }

    [HttpGet(RutaDeLosReportes)]
    public async Task<IActionResult> Reportes(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (LeerLaConsulta(Request, ["pagina", "hecho"], out var numero, out var hecho) is { } invalida)
        {
            return await Falla(invalida, RutaDeLosReportes, cancelacion);
        }

        return await Listar(new ListarDevolucionesDeLaAdministracionEntrada(TipoDeLaDevolucion.Digital, true, numero, TamanoDePagina), "Reportes de archivos defectuosos", RutaDeLosReportes, hecho, cancelacion);
    }

    [HttpGet(RutaDeLaLista + "/{devolucionId:guid}")]
    public async Task<IActionResult> Detalle(Guid devolucionId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{devolucionId:D}";
        if (LeerLaConsulta(Request, ["hecho"], out _, out var hecho) is { } invalida)
        {
            return await Falla(invalida, RutaDeLaLista, cancelacion);
        }

        var resultado = await gestion.Obtener(devolucionId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        var vista = await Pagina.Completar(new DetalleDeDevolucionVista(), "Devolución", AreaDeLaPagina.Admin, cancelacion);
        vista.Devolucion = APantalla(resultado.Valor, RutaDeLaLista);
        vista.RutaDeLaLista = RutaDeLaLista;
        vista.Aviso = Aviso(hecho);
        vista.Acciones = [.. Acciones(resultado.Valor, ruta)];
        return View("~/Areas/Tienda/Views/Devoluciones/Detalle.cshtml", vista);
    }

    [HttpPost(RutaDeLaLista + "/{devolucionId:guid}/aprobar")]
    public Task<IActionResult> Aprobar(Guid devolucionId, CancellationToken cancelacion) => Operar(devolucionId, "aprobada", c => gestion.Aprobar(devolucionId, c), cancelacion);

    [HttpPost(RutaDeLaLista + "/{devolucionId:guid}/rechazar")]
    public Task<IActionResult> Rechazar(Guid devolucionId, CancellationToken cancelacion) => Operar(devolucionId, "rechazada", c => gestion.Rechazar(devolucionId, c), cancelacion);

    [HttpPost(RutaDeLaLista + "/{devolucionId:guid}/recibir")]
    public Task<IActionResult> ConfirmarRecepcion(Guid devolucionId, CancellationToken cancelacion) => Operar(devolucionId, "recibida", c => gestion.ConfirmarRecepcion(devolucionId, c), cancelacion);

    [HttpPost(RutaDeLaLista + "/{devolucionId:guid}/aprobar-reporte")]
    public Task<IActionResult> AprobarReporte(Guid devolucionId, CancellationToken cancelacion) => Operar(devolucionId, "reporte-aprobado", c => gestion.AprobarReporte(devolucionId, c), cancelacion);

    [HttpPost(RutaDeLaLista + "/{devolucionId:guid}/rechazar-reporte")]
    public Task<IActionResult> RechazarReporte(Guid devolucionId, CancellationToken cancelacion) => Operar(devolucionId, "reporte-rechazado", c => gestion.RechazarReporte(devolucionId, c), cancelacion);

    private async Task<IActionResult> Listar(ListarDevolucionesDeLaAdministracionEntrada entrada, string titulo, string ruta, string? hecho, CancellationToken cancelacion)
    {
        var resultado = await gestion.ListarDeLaAdministracion(entrada, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, ruta, cancelacion);
        }

        var vista = await ComoLista(
            resultado.Valor,
            titulo,
            "Todas las devoluciones y reportes, con el estado de cada línea y su reembolso. Un reembolso pendiente se concilia con la pasarela; la vista de reembolsos de Pagos muestra su estado.",
            ruta,
            RutaDeLaLista,
            hecho,
            "Las devoluciones y los reportes aparecerán aquí.",
            cancelacion);
        return View("~/Areas/Tienda/Views/Devoluciones/Lista.cshtml", vista);
    }

    private async Task<IActionResult> Operar(Guid devolucionId, string aviso, Func<CancellationToken, Task<Resultado<DevolucionVista>>> ejecutar, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{devolucionId:D}";
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

    private static IEnumerable<AccionDeDevolucionVista> Acciones(DevolucionVista devolucion, string ruta)
    {
        var abierta = devolucion.Lineas.Any(l => l.Estado == EstadoDeLaLineaDeDevolucion.Solicitada);
        if (devolucion.Origen == OrigenDeLaDevolucion.Reporte)
        {
            if (abierta)
            {
                yield return new AccionDeDevolucionVista("Aprobar el reporte", ruta + "/aprobar-reporte", "exito", true);
                yield return new AccionDeDevolucionVista("Rechazar el reporte", ruta + "/rechazar-reporte", "candado", false);
            }

            yield break;
        }

        if (devolucion.Tipo != TipoDeLaDevolucion.Impreso)
        {
            yield break;
        }

        if (abierta)
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
