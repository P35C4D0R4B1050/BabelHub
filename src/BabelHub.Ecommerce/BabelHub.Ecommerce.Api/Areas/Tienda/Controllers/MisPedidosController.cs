using BabelHub.Ecommerce.Api.Pedidos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// Los pedidos del titular (T31, T37; PED-04, PED-07, PED-17, PED-18, PED-40, PED-41): <c>GET /cuenta/pedidos</c> (lista con <c>estado</c> y <c>pagina</c>), <c>GET /cuenta/pedidos/{pedidoId}</c> (detalle con el contacto y el
/// reembolso pendiente) y <c>POST /cuenta/pedidos/{pedidoId}/cancelar</c>. <b>Presentación pura</b>: no lleva el identificador de la cuenta (sale de la sesión verificada), rechaza una consulta o un cuerpo con campos que no
/// declara (validación, sin repetir lo recibido) y llama a <see cref="IPedidosDelCliente"/>, que filtra siempre por el titular: un pedido ajeno o inexistente responde <b>igual</b> (<c>ECO-NO-ENCONTRADO</c>). Las dos lecturas se
/// siguen sirviendo con los textos legales pendientes (<c>[PermitidaConTextosPendientes(Pedidos)]</c>); cancelar no. Un fallo se pinta en pantalla para un navegador y sale como <c>application/problem+json</c> para quien no pide
/// HTML. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(4096)]
public sealed class MisPedidosController(IPedidosDelCliente pedidos, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDePedidos(pagina, errores, AreaDeLaPagina.Tienda)
{
    private const string RutaDeLaLista = "/cuenta/pedidos";

    [HttpGet(RutaDeLaLista)]
    [RequiereSesion]
    [PermitidaConTextosPendientes(CapacidadWeb.Pedidos)]
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

        var resultado = await pedidos.Listar(filtro, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        var vista = await ListaComoVista(resultado.Valor, clave, "Mis pedidos", "Aquí ves el estado de cada compra: cuándo llega tu traslado, cuándo puedes recogerla o cuándo sale a reparto.", RutaDeLaLista, id => $"{RutaDeLaLista}/{id:D}", "Cuando compres, tus pedidos aparecerán aquí.", cancelacion);
        return View("~/Areas/Tienda/Views/MisPedidos/Lista.cshtml", vista);
    }

    [HttpGet(RutaDeLaLista + "/{pedidoId:guid}")]
    [RequiereSesion]
    [PermitidaConTextosPendientes(CapacidadWeb.Pedidos)]
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

        var resultado = await pedidos.Obtener(pedidoId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, ruta, cancelacion);
        }

        var detalle = resultado.Valor;
        var vista = await NuevoDetalle(detalle.Resumen, detalle.Lineas, RutaDeLaLista, ruta, aviso, cancelacion);
        vista.MostrarLaFuente = false;
        vista.RutaDeDevolver = ruta + "/devolver";
        vista.CostoDeEnvioTexto = FormatoPeruano.Soles(detalle.CostoDeEnvio);
        vista.Region = detalle.RegionDeEntrega;
        vista.Contacto = ContactoComoVista(detalle.Contacto);
        vista.ReembolsoPendienteTexto = ReembolsoPendienteComoTexto(detalle.ReembolsoPendiente);
        // Cancelar solo se ofrece donde procede y sin los textos legales pendientes; si no procede, la alternativa que le corresponde (PED-18).
        if (detalle.PuedeCancelar && !vista.TextosPendientes)
        {
            vista.RutaDeCancelar = ruta + "/cancelar";
        }
        else if (!detalle.PuedeCancelar && detalle.Alternativa != AlternativaDeCancelacion.Ninguna)
        {
            vista.AlternativaTexto = TextosDePedidos.Alternativa(detalle.Alternativa);
        }

        return View("~/Areas/Tienda/Views/MisPedidos/Detalle.cshtml", vista);
    }

    [HttpPost(RutaDeLaLista + "/{pedidoId:guid}/cancelar")]
    [RequiereSesion]
    public async Task<IActionResult> Cancelar(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{pedidoId:D}";
        if (await CuerpoVacio() is { } invalido)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await pedidos.Cancelar(pedidoId, cancelacion);
        return resultado.EsExito ? Redirigir($"{ruta}?hecho=cancelar") : await Falla(resultado.Error!, ruta, cancelacion);
    }
}
