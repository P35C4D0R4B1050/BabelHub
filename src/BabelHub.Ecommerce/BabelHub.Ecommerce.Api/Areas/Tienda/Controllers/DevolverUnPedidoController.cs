using BabelHub.Ecommerce.Api.Devoluciones;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Un libro impreso que se puede devolver desde el pedido: la cantidad máxima y el último día del plazo.</summary>
public sealed record ImpresoParaDevolver(Guid LineaId, string Titulo, int Cantidad, string PlazoTexto);

/// <summary>Un libro digital del pedido: el título y el último día de cada plazo (reembolso o reporte).</summary>
public sealed record DigitalParaDevolver(Guid LineaId, string Titulo, string PlazoDelReembolsoTexto, string PlazoDelReporteTexto);

/// <summary>La pantalla de devolver de un pedido: los impresos entregados con su cantidad, los digitales con su política, el aviso de no escribir datos innecesarios y el estado estándar si el pedido no se puede leer.</summary>
public sealed class DevolverUnPedidoVista : ModeloBaseDePagina
{
    public Guid PedidoId { get; set; }

    public string RutaDelPedido { get; set; } = string.Empty;

    public IReadOnlyList<ImpresoParaDevolver> Impresos { get; set; } = [];

    public IReadOnlyList<DigitalParaDevolver> Digitales { get; set; } = [];

    public EstadoVista? Estado { get; set; }
}

/// <summary>
/// La pantalla de devolver desde el detalle de un pedido (T26; DEV-01, DEV-02, DEV-04, DEV-06, DEV-29; AC-DEV-01, AC-DEV-04, AC-DEV-06): <c>GET /cuenta/pedidos/{pedidoId}/devolver</c>, sin efectos. <b>Presentación pura</b>: lee el pedido del titular con
/// <see cref="IPedidosDelCliente"/> (uno ajeno o inexistente responde igual) y arma los formularios que envían a las acciones de <see cref="DevolucionesController"/>; los plazos que muestra son informativos: los hace cumplir la Aplicación. El formulario advierte que no se escriban
/// datos innecesarios y no admite adjuntos. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Tienda")]
public sealed class DevolverUnPedidoController(IPedidosDelCliente pedidos, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDeDevoluciones(pagina, errores, AreaDeLaPagina.Tienda)
{
    private static readonly TimeSpan PlazoDelImpreso = TimeSpan.FromDays(15);

    private static readonly TimeSpan PlazoDelReembolsoDigital = TimeSpan.FromDays(14);

    private static readonly TimeSpan PlazoDelReporte = TimeSpan.FromDays(30);

    [HttpGet("/cuenta/pedidos/{pedidoId:guid}/devolver")]
    [RequiereSesion]
    public async Task<IActionResult> Index(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"/cuenta/pedidos/{pedidoId:D}";
        if (RechazarConsulta(Request, []) is { } consulta)
        {
            return await Falla(consulta, ruta, cancelacion);
        }

        var resultado = await pedidos.Obtener(pedidoId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, "/cuenta/pedidos", cancelacion);
        }

        var vista = await Pagina.Completar(new DevolverUnPedidoVista { PedidoId = pedidoId, RutaDelPedido = ruta }, "Devolver un pedido", AreaDeLaPagina.Tienda, cancelacion);
        vista.Impresos =
        [
            .. resultado.Valor.Lineas
                .Where(l => !l.EsDigital && l.Estado == EstadoVisibleDeLaLinea.Entregado && l.EntregadaEn is not null)
                .Select(l => new ImpresoParaDevolver(l.LineaId, l.Titulo, l.Cantidad, FormatoPeruano.FechaYHora(l.EntregadaEn!.Value + PlazoDelImpreso))),
        ];
        vista.Digitales =
        [
            .. resultado.Valor.Lineas
                .Where(l => l.EsDigital && l.Estado == EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca)
                .Select(l => new DigitalParaDevolver(
                    l.LineaId,
                    l.Titulo,
                    FormatoPeruano.FechaYHora(resultado.Valor.Resumen.CreadoEn + PlazoDelReembolsoDigital),
                    FormatoPeruano.FechaYHora(resultado.Valor.Resumen.CreadoEn + PlazoDelReporte))),
        ];
        if (vista.Impresos.Count == 0 && vista.Digitales.Count == 0)
        {
            vista.Estado = EstadoVista.Vacio("Nada que devolver por ahora", "Los libros impresos se pueden devolver una vez entregados y los digitales mientras tengas el acceso. Revisa el estado de tu pedido.", "Volver al pedido", ruta);
        }

        return View("~/Areas/Tienda/Views/Devoluciones/Devolver.cshtml", vista);
    }
}
