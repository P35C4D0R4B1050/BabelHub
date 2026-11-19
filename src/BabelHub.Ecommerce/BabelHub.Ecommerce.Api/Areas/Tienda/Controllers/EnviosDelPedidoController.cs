using BabelHub.Ecommerce.Api.Envios;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Un envío del pedido del titular, listo para pintar: el estado con texto e icono (nunca solo con color), la guía y el operador si existen y la fecha de entrega. Sin ningún dato de contacto.</summary>
public sealed record EnvioDelPedidoVista(string Origen, string EstadoTexto, string EstadoIcono, string? Mensaje, string? Guia, string? Operador, string? EntregadoTexto);

/// <summary>El bloque de envío del detalle de un pedido (T17, T19; ENV-05, AC-ENV-05): sus envíos, o el estado estándar que los reemplaza (vacío, error, no encontrado, servicio no disponible).</summary>
public sealed class EnviosDelPedidoVista : ModeloBaseDePagina
{
    public Guid PedidoId { get; set; }

    public string Ruta { get; set; } = string.Empty;

    public IReadOnlyList<EnvioDelPedidoVista> Envios { get; set; } = [];

    public EstadoVista? Estado { get; set; }

    /// <summary>Se sirve como fragmento (htmx) dentro del detalle del pedido, sin el layout.</summary>
    public bool EsFragmento { get; set; }
}

/// <summary>
/// El bloque de envío del detalle del pedido del titular (T17, T19; ENV-05, AC-ENV-05; plan §4 y §7): <c>GET /cuenta/pedidos/{pedidoId}/envios</c>, sin efectos. El detalle del pedido lo compone con htmx
/// (<c>hx-get</c> al cargar) o lo abre como página; Pedidos no depende de Envíos. <b>Presentación pura</b>: llama a <see cref="IEnviosDelCliente"/>, que comprueba la propiedad con el contrato de lectura de Pedidos;
/// un pedido ajeno o inexistente responde <b>igual</b> (<c>ECO-NO-ENCONTRADO</c>, 404) y nunca se ofrece otro dato. Con htmx el estado de un fallo se pinta dentro del bloque con <c>200</c> (htmx no reemplaza un 4xx).
/// </summary>
[Area("Tienda")]
public sealed class EnviosDelPedidoController(IEnviosDelCliente envios, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    private const string Vista = "~/Areas/Tienda/Views/EnviosDelPedido/Index.cshtml";

    private const string Bloque = "~/Areas/Tienda/Views/EnviosDelPedido/_Bloque.cshtml";

    [HttpGet("/cuenta/pedidos/{pedidoId:guid}/envios")]
    [RequiereSesion]
    public async Task<IActionResult> Index(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"/cuenta/pedidos/{pedidoId:D}/envios";
        var modelo = await pagina.Completar(new EnviosDelPedidoVista { PedidoId = pedidoId, Ruta = ruta, EsFragmento = Request.EsHtmx() }, "Seguimiento de tu pedido", AreaDeLaPagina.Tienda, cancelacion);
        if (Request.Query.Count > 0)
        {
            return await Falla(modelo, new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("consulta", "formato-invalido")]), cancelacion);
        }

        var resultado = await envios.ObtenerDeUnPedido(pedidoId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(modelo, resultado.Error!, cancelacion);
        }

        modelo.Envios = [.. resultado.Valor.Envios.Select(AVista)];
        if (modelo.Envios.Count == 0)
        {
            modelo.Estado = EstadoVista.Vacio("Pendiente de guía", "Todavía estamos preparando el envío de tu pedido. Cuando tenga guía la verás aquí y te avisaremos.");
        }

        return Mostrar(modelo);
    }

    private IActionResult Mostrar(EnviosDelPedidoVista modelo) => modelo.EsFragmento ? PartialView(Bloque, modelo) : View(Vista, modelo);

    private async Task<IActionResult> Falla(EnviosDelPedidoVista modelo, ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (!PantallasDeEnvios.PrefiereHtml(Request) && !modelo.EsFragmento)
        {
            await ProblemasDeEnvios.Escribir(HttpContext, errores, error);
            return new EmptyResult();
        }

        var problema = ProblemasDeEnvios.Crear(HttpContext, errores, error);
        await DenegacionesHttp.Registrar(HttpContext, error.Categoria, error.Clave, problema.Code);
        var respuesta = PantallasDeEnvios.Traducir(problema, error, modelo.Ruta, paraElTitular: true);
        if (respuesta.RedirigirA is { } destino)
        {
            return modelo.EsFragmento ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        if (error.Categoria == CategoriaDeError.Limite)
        {
            Response.Headers["Retry-After"] = ProblemasDeEnvios.EsperaEnSegundos(error).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        modelo.Estado = respuesta.Vista;
        Response.StatusCode = modelo.EsFragmento ? StatusCodes.Status200OK : respuesta.EstadoHttp;
        return Mostrar(modelo);
    }

    private static EnvioDelPedidoVista AVista(EnvioDeMiPedido e)
    {
        var (texto, icono, mensaje) = e.Estado switch
        {
            EstadoDelEnvio.PendienteDeGuia => ("Pendiente de guía", "reloj", "Estamos preparando tu envío."),
            EstadoDelEnvio.GuiaGenerada => ("Guía generada", "bandeja", "Tu paquete tiene guía y saldrá a reparto pronto."),
            EstadoDelEnvio.EnReparto => ("En reparto", "camion", "Tu paquete ya salió a reparto."),
            EstadoDelEnvio.Entregado => ("Entregado", "exito", null),
            _ => ("Cancelado", "candado", "Este envío se canceló."),
        };
        return new EnvioDelPedidoVista(e.CodigoDeFuente, texto, icono, mensaje, e.NumeroDeGuia, e.Operador, e.EntregadoEn is { } entregado ? FormatoPeruano.FechaYHora(entregado) : null);
    }
}
