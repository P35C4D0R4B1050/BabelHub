using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El pago de la compra (plan §4, PAG-01 a PAG-09, T28): <c>GET</c> y <c>POST /checkout/pago</c>, <c>GET /checkout/pago/estado</c> y <c>GET /checkout/pago/retorno</c> (de solo lectura). <b>Presentación pura</b>: lee el cuerpo con esquema estricto, llama a
/// <see cref="IPagos"/> y a <see cref="ICheckoutDelCliente"/> por sus contratos y traduce el resultado a una pantalla, una redirección <c>303</c> o un <c>application/problem+json</c>. El <c>pedidoId</c> viaja en la consulta o el formulario y Pagos lo valida contra
/// la cuenta del actor, de modo que el estado y el retorno responden también con el checkout ya <c>pagado</c> o <c>cancelado</c>; sin <c>pedidoId</c> vigente (o con un retiro pendiente de Carrito) se vuelve a <c>/checkout</c> sin emitir ni cobrar. El formulario
/// <b>no marca ningún campo <c>[Conservar]</c></b>: el token del medio nunca se guarda ni se repite. <c>GET /checkout/pago</c> no escribe nada.
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(EntradasHttpDelCarrito.TopeDelCuerpo)]
[RequiereSesion]
public sealed class PagoController(IPagos pagos, ICheckoutDelCliente checkout, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    public const string RutaDelEstado = "/checkout/pago/estado";

    private const int LongitudMaximaDeLaClave = 2000;

    private static readonly string[] CamposDelCobro = ["pedidoId", "claveDeIntento", "medio", "tokenDelMedio"];

    // ---------- Mostrar el pago ----------

    [HttpGet("/checkout/pago")]
    public async Task<IActionResult> Index([FromQuery] string? pedidoId, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["pedidoId"]) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        Guid pedido;
        if (pedidoId is null)
        {
            // Sin pedidoId se usa el checkout vivo: solo en_pago permite pagar; pago_pendiente va al estado; cualquier otra cosa vuelve al checkout.
            var vivo = await checkout.ObtenerParaPagar(cancelacion);
            if (!vivo.EsExito)
            {
                return vivo.Error!.Categoria == CategoriaDeError.NoEncontrado ? Redirigir("/checkout") : await Falla(vivo.Error, cancelacion);
            }

            if (vivo.Valor.Estado == EstadoVisibleDelCheckout.PagoPendiente)
            {
                return Redirigir($"{RutaDelEstado}?pedidoId={vivo.Valor.PedidoId:D}");
            }

            if (vivo.Valor.Estado != EstadoVisibleDelCheckout.EnPago)
            {
                return Redirigir("/checkout");
            }

            pedido = vivo.Valor.PedidoId;
        }
        else if (!Guid.TryParseExact(pedidoId, "D", out pedido) || pedido == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pedidoId"), cancelacion);
        }

        var estado = await pagos.ObtenerEstado(pedido, cancelacion);
        if (!estado.EsExito)
        {
            return await Falla(estado.Error!, cancelacion);
        }

        var vista = await pagina.Completar(new PagoVista { PedidoId = pedido, Estado = estado.Valor }, "Pagar", cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        if (estado.Valor.Estado != EstadoDelPagoDeLaCompra.ListoParaPagar || !estado.Valor.PuedeReintentar)
        {
            return View("Index", vista);
        }

        var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido), cancelacion);
        if (!clave.EsExito)
        {
            // Un carrito que cambió o un retiro pendiente: se vuelve a la revisión sin emitir ni cobrar.
            return clave.Error!.Clave is "instantanea-obsoleta" ? Redirigir("/checkout") : await Falla(clave.Error, cancelacion);
        }

        vista.Clave = clave.Valor;
        return View("Index", vista);
    }

    // ---------- Cobrar ----------

    [HttpPost("/checkout/pago")]
    public async Task<IActionResult> Cobrar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelCobro, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        foreach (var obligatorio in CamposDelCobro.Where(c => !campos.ContainsKey(c)))
        {
            return await Falla(EntradasHttpDelCarrito.Falta(obligatorio), cancelacion);
        }

        if (!Guid.TryParseExact(campos["pedidoId"], "D", out var pedido) || pedido == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pedidoId"), cancelacion);
        }

        var clave = campos["claveDeIntento"];
        if (clave.Length is 0 or > LongitudMaximaDeLaClave || clave.Any(char.IsControl))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("claveDeIntento"), cancelacion);
        }

        MedioDePago? medio = campos["medio"] switch
        {
            "yape" => MedioDePago.Yape,
            "tarjeta" => MedioDePago.Tarjeta,
            _ => null,
        };
        if (medio is null)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("medio"), cancelacion);
        }

        // El token no se repite en ningún error: solo se dice que el campo no es válido.
        if (!TokenDelMedioDePago.TryCrear(campos["tokenDelMedio"], out var token))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("tokenDelMedio"), cancelacion);
        }

        var resultado = await pagos.Cobrar(new CobrarEntrada(pedido, clave, medio.Value, token), cancelacion);
        if (!resultado.EsExito)
        {
            // Un total o un carrito que cambió: se vuelve a la revisión sin cobrar.
            return resultado.Error!.Clave is "instantanea-obsoleta" or "total-cambio" ? Redirigir("/checkout") : await Falla(resultado.Error, cancelacion);
        }

        // Aprobado, rechazado o pendiente de confirmación: el estado dice qué sigue (y el motivo de un rechazo).
        return Redirigir($"{RutaDelEstado}?pedidoId={pedido:D}");
    }

    // ---------- Estado y retorno ----------

    [HttpGet("/checkout/pago/estado")]
    public async Task<IActionResult> Estado([FromQuery] string? pedidoId, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["pedidoId"]) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        Guid pedido;
        if (pedidoId is null)
        {
            var vivo = await checkout.ObtenerParaPagar(cancelacion);
            if (!vivo.EsExito)
            {
                return vivo.Error!.Categoria == CategoriaDeError.NoEncontrado ? Redirigir("/checkout") : await Falla(vivo.Error, cancelacion);
            }

            pedido = vivo.Valor.PedidoId;
        }
        else if (!Guid.TryParseExact(pedidoId, "D", out pedido) || pedido == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("pedidoId"), cancelacion);
        }

        var estado = await pagos.ObtenerEstado(pedido, cancelacion);
        if (!estado.EsExito)
        {
            return await Falla(estado.Error!, cancelacion);
        }

        var vista = await pagina.Completar(new PagoVista { PedidoId = pedido, Estado = estado.Valor }, "Estado de tu pago", cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        return Request.EsHtmx() ? PartialView("_Estado", vista) : View("Estado", vista);
    }

    /// <summary>
    /// El retorno de la pasarela (de solo lectura): nada de lo que llega en la consulta es confiable, así que no cambia nada y solo manda al estado con el <c>pedidoId</c> si es válido. Pagos decide el estado.
    /// </summary>
    [HttpGet("/checkout/pago/retorno")]
    public IActionResult Retorno([FromQuery] string? pedidoId) =>
        Guid.TryParseExact(pedidoId, "D", out var pedido) && pedido != Guid.Empty ? Redirigir($"{RutaDelEstado}?pedidoId={pedido:D}") : Redirigir(RutaDelEstado);

    // ---------- Piezas ----------

    private IActionResult Redirigir(string destino)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await PantallasDelCarrito.Escribir(this, pagina, errores, error, "/checkout/pago", cancelacion);
        }

        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
