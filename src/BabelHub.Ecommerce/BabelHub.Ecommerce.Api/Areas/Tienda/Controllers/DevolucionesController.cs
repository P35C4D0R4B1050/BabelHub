using System.Globalization;
using BabelHub.Ecommerce.Api.Devoluciones;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// Las devoluciones del cliente (T23, T26; DEV-01, DEV-04, DEV-06, DEV-28, DEV-29, DEV-31; AC-DEV-01, AC-DEV-02, AC-DEV-05, AC-DEV-31): <c>POST /cuenta/pedidos/{pedidoId}/devoluciones</c> (impreso), <c>…/devoluciones/digital</c> (reembolso) y
/// <c>…/devoluciones/reporte</c> (archivo defectuoso) y <c>GET /cuenta/devoluciones</c> con su detalle. <b>Presentación pura</b>: el cliente es el de la sesión (la Aplicación lo toma del actor verificado; ningún identificador de cuenta viaja por la ruta ni por el cuerpo), un
/// pedido ajeno o inexistente responde igual, <b>no admite adjuntos</b> (un <c>multipart</c> o un archivo es validación) y rechaza un cuerpo con campos que no declara sin repetirlo. Llama a <see cref="IDevolucionesDelCliente"/>. Tras solicitar responde <c>303</c> al detalle;
/// un fallo se pinta en pantalla para un navegador y sale como <c>application/problem+json</c> (sin motivo ni texto) para quien no pide HTML. El motivo y el texto solo se imprimen codificados. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(16384)]
public sealed class DevolucionesController(IDevolucionesDelCliente devoluciones, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDeDevoluciones(pagina, errores, AreaDeLaPagina.Tienda)
{
    private const string RutaDeLaLista = "/cuenta/devoluciones";

    private const string Prefijo = "cantidad.";

    private const int TopeDeLineas = 30;

    [HttpGet(RutaDeLaLista)]
    [RequiereSesion]
    public async Task<IActionResult> Lista(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (LeerLaConsulta(Request, ["pagina", "hecho"], out var numero, out var hecho) is { } invalida)
        {
            return await Falla(invalida, RutaDeLaLista, cancelacion);
        }

        var resultado = await devoluciones.ListarMias(new ListarDevolucionesEntrada(numero, TamanoDePagina), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        var vista = await ComoLista(
            resultado.Valor,
            "Mis devoluciones",
            "El estado de cada libro que devolviste o reportaste y de su reembolso.",
            RutaDeLaLista,
            RutaDeLaLista,
            hecho,
            "Cuando devuelvas un libro o reportes un archivo, lo verás aquí.",
            cancelacion);
        return View("~/Areas/Tienda/Views/Devoluciones/Lista.cshtml", vista);
    }

    [HttpGet(RutaDeLaLista + "/{devolucionId:guid}")]
    [RequiereSesion]
    public async Task<IActionResult> Detalle(Guid devolucionId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{devolucionId:D}";
        if (LeerLaConsulta(Request, ["hecho"], out _, out var hecho) is { } invalida)
        {
            return await Falla(invalida, ruta, cancelacion);
        }

        var resultado = await devoluciones.ObtenerMia(devolucionId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        var vista = await Pagina.Completar(new DetalleDeDevolucionVista(), "Devolución", AreaDeLaPagina.Tienda, cancelacion);
        vista.Devolucion = APantalla(resultado.Valor, RutaDeLaLista);
        vista.RutaDeLaLista = RutaDeLaLista;
        vista.Aviso = Aviso(hecho);
        return View("~/Areas/Tienda/Views/Devoluciones/Detalle.cshtml", vista);
    }

    [HttpPost("/cuenta/pedidos/{pedidoId:guid}/devoluciones")]
    [RequiereSesion]
    public async Task<IActionResult> SolicitarImpreso(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var (formulario, error) = await LeerFormulario(c => c is "motivo" or "sucursalReceptora" || c.StartsWith(Prefijo, StringComparison.Ordinal));
        if (error is not null)
        {
            return await Falla(error, RutaDeLaLista, cancelacion);
        }

        var lineas = new List<LineaPedidaParaDevolver>();
        foreach (var (clave, valores) in formulario!)
        {
            if (!clave.StartsWith(Prefijo, StringComparison.Ordinal))
            {
                continue;
            }

            if (valores.Count != 1 || !Guid.TryParse(clave[Prefijo.Length..], out var lineaId) || !int.TryParse(valores[0], NumberStyles.None, CultureInfo.InvariantCulture, out var cantidad))
            {
                return await Falla(Invalido("cantidad"), RutaDeLaLista, cancelacion);
            }

            if (cantidad > 0)
            {
                lineas.Add(new LineaPedidaParaDevolver(lineaId, cantidad));
            }
        }

        if (lineas.Count is 0 or > TopeDeLineas || Unico(formulario, "motivo") is not { } motivo || Unico(formulario, "sucursalReceptora") is not { } sucursal)
        {
            return await Falla(Invalido("formulario"), RutaDeLaLista, cancelacion);
        }

        var resultado = await devoluciones.SolicitarImpreso(new SolicitarDevolucionDeImpresoEntrada(pedidoId, lineas, new TextoLibre(motivo), sucursal), cancelacion);
        return await Terminar(resultado, cancelacion);
    }

    [HttpPost("/cuenta/pedidos/{pedidoId:guid}/devoluciones/digital")]
    [RequiereSesion]
    public async Task<IActionResult> PedirReembolsoDeDigital(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var (formulario, error) = await LeerFormulario(c => c is "lineaId" or "motivo");
        if (error is not null)
        {
            return await Falla(error, RutaDeLaLista, cancelacion);
        }

        if (!Guid.TryParse(Unico(formulario!, "lineaId"), out var lineaId) || Unico(formulario!, "motivo") is not { } motivo)
        {
            return await Falla(Invalido("formulario"), RutaDeLaLista, cancelacion);
        }

        var resultado = await devoluciones.PedirReembolsoDeDigital(new PedirReembolsoDeDigitalEntrada(pedidoId, lineaId, new TextoLibre(motivo)), cancelacion);
        return await Terminar(resultado, cancelacion);
    }

    [HttpPost("/cuenta/pedidos/{pedidoId:guid}/devoluciones/reporte")]
    [RequiereSesion]
    public async Task<IActionResult> ReportarArchivoDefectuoso(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var (formulario, error) = await LeerFormulario(c => c is "lineaId" or "motivo" or "texto");
        if (error is not null)
        {
            return await Falla(error, RutaDeLaLista, cancelacion);
        }

        if (!Guid.TryParse(Unico(formulario!, "lineaId"), out var lineaId) || Unico(formulario!, "motivo") is not { } motivo || Unico(formulario!, "texto") is not { } texto)
        {
            return await Falla(Invalido("formulario"), RutaDeLaLista, cancelacion);
        }

        var resultado = await devoluciones.ReportarArchivoDefectuoso(new ReportarArchivoDefectuosoEntrada(pedidoId, lineaId, new TextoLibre(motivo), new TextoLibre(texto)), cancelacion);
        return await Terminar(resultado, cancelacion);
    }

    private async Task<IActionResult> Terminar(Resultado<DevolucionVista> resultado, CancellationToken cancelacion)
    {
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        return Redirigir($"{RutaDeLaLista}/{resultado.Valor.DevolucionId:D}?hecho=solicitada");
    }
}
