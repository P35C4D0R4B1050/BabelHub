using System.Globalization;
using BabelHub.Ecommerce.Api.Pedidos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>
/// Los pedidos de la administración central (T31, T40; PED-13, PED-14, PED-16, PED-24, PED-40, PED-41; AC-PED-11, AC-PED-12, AC-PED-19): <c>GET /administracion/pedidos</c> (todos, con filtro por estado y página, sin
/// contacto), el detalle (con el contacto auditado), <c>POST …/{pedidoId}/anular</c> con el <c>motivo</c> de la lista cerrada, <c>POST …/{pedidoId}/reembolso/reintentar</c> y <c>GET …/operaciones-fallidas</c> (sin datos
/// personales). <b>Presentación pura</b>: quién puede (solo la administración central) lo decide la Aplicación con el actor verificado; el motivo se valida allí contra la lista cerrada y no hay nota libre. Rechaza un cuerpo con
/// campos que no declara y llama a <see cref="IAdministracionDePedidos"/>. Un fallo se pinta en pantalla para un navegador y sale como <c>application/problem+json</c> para quien no pide HTML. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Admin")]
[RequestSizeLimit(4096)]
public sealed class AdministracionDePedidosController(IAdministracionDePedidos administracion, ContextoDePagina pagina, OpcionesDeErrores errores) : ControladorDePedidos(pagina, errores, AreaDeLaPagina.Admin)
{
    private const string RutaDeLaLista = "/administracion/pedidos";

    private const string RutaDeLasOperacionesFallidas = RutaDeLaLista + "/operaciones-fallidas";

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

        var resultado = await administracion.Listar(filtro, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLaLista, cancelacion);
        }

        var vista = await ListaComoVista(
            resultado.Valor,
            clave,
            "Pedidos",
            "Todos los pedidos de la red. La lista no muestra datos de contacto: se ven en el detalle del pedido y cada lectura queda auditada.",
            RutaDeLaLista,
            id => $"{RutaDeLaLista}/{id:D}",
            "Los pedidos aparecerán aquí cuando los clientes compren.",
            cancelacion);
        return View("~/Areas/Admin/Views/AdministracionDePedidos/Lista.cshtml", vista);
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

        var resultado = await administracion.Obtener(pedidoId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, ruta, cancelacion);
        }

        var detalle = resultado.Valor;
        var vista = await DetalleDePersonalComoVista(detalle, RutaDeLaLista, ruta, aviso, cancelacion);
        if (!vista.TextosPendientes)
        {
            if (detalle.Resumen.Estado != EstadoVisibleDelPedido.Anulado)
            {
                vista.RutaDeAnular = ruta + "/anular";
            }

            if (detalle.ReembolsoPendiente is not null)
            {
                vista.RutaDeReintentarElReembolso = ruta + "/reembolso/reintentar";
            }
        }

        return View("~/Areas/Admin/Views/AdministracionDePedidos/Detalle.cshtml", vista);
    }

    [HttpPost(RutaDeLaLista + "/{pedidoId:guid}/anular")]
    public async Task<IActionResult> Anular(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{pedidoId:D}";
        var (motivo, invalido) = await LeerElMotivo();
        if (invalido is not null)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await administracion.Anular(new AnularPedidoEntrada(pedidoId, motivo!), cancelacion);
        return resultado.EsExito ? Redirigir($"{ruta}?hecho=anular") : await Falla(resultado.Error!, ruta, cancelacion);
    }

    [HttpPost(RutaDeLaLista + "/{pedidoId:guid}/reembolso/reintentar")]
    public async Task<IActionResult> ReintentarElReembolso(Guid pedidoId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = $"{RutaDeLaLista}/{pedidoId:D}";
        if (await CuerpoVacio() is { } invalido)
        {
            return await Falla(invalido, ruta, cancelacion);
        }

        var resultado = await administracion.ReintentarElReembolso(pedidoId, cancelacion);
        return resultado.EsExito ? Redirigir($"{ruta}?hecho=reembolso") : await Falla(resultado.Error!, ruta, cancelacion);
    }

    [HttpGet(RutaDeLasOperacionesFallidas)]
    public async Task<IActionResult> OperacionesFallidas(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (RechazarConsulta(Request, ["pagina"]) is { } consulta)
        {
            return await Falla(consulta, RutaDeLasOperacionesFallidas, cancelacion);
        }

        var numero = 1;
        if (Request.Query["pagina"].ToString() is { Length: > 0 } p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out numero) || numero is < 1 or > 10_000))
        {
            return await Falla(Invalido("pagina"), RutaDeLasOperacionesFallidas, cancelacion);
        }

        var resultado = await administracion.ListarOperacionesFallidas(numero, TamanoDePagina, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, RutaDeLasOperacionesFallidas, cancelacion);
        }

        var vista = await Pagina.Completar(new OperacionesFallidasVista(), "Operaciones fallidas", AreaDeLaPagina.Admin, cancelacion);
        vista.Total = resultado.Valor.Total;
        vista.Pagina = resultado.Valor.Pagina;
        vista.TotalDePaginas = Math.Max(1, (int)Math.Ceiling(resultado.Valor.Total / (double)Math.Max(1, resultado.Valor.Tamano)));
        vista.Operaciones =
        [
            .. resultado.Valor.Operaciones.Select(o => new OperacionFallidaVista(
                o.Tipo == "anulacion" ? "Anulación" : o.Tipo == "entrega" ? "Entrega" : "Operación",
                Corto(o.PedidoId),
                $"{RutaDeLaLista}/{o.PedidoId:D}",
                o.Motivo,
                o.PasosHechos,
                FormatoPeruano.FechaYHora(o.CreadaEn))),
        ];
        if (resultado.Valor.Total == 0 || vista.Operaciones.Count == 0)
        {
            vista.Estado = BabelHub.Ecommerce.Api.Web.Estados.EstadoVista.Vacio("No hay operaciones fallidas", "Cuando una anulación o una entrega quede a medias, aparecerá aquí con los pasos que alcanzó a hacer.");
        }

        return View("~/Areas/Admin/Views/AdministracionDePedidos/OperacionesFallidas.cshtml", vista);
    }

    /// <summary>El cuerpo de la anulación: solo <c>motivo</c> y el token. El motivo (lista cerrada) lo valida la Aplicación; aquí solo se exige que venga una vez y acotado.</summary>
    private async Task<(string? Motivo, ErrorDeAplicacion? Error)> LeerElMotivo()
    {
        if (RechazarConsulta(Request, []) is { } consulta)
        {
            return (null, consulta);
        }

        if (!Request.HasFormContentType)
        {
            return (null, Invalido("cuerpo"));
        }

        var formulario = await Request.ReadFormAsync();
        if (formulario.Keys.Any(k => !string.Equals(k, "motivo", StringComparison.Ordinal) && !string.Equals(k, ProteccionContraFalsificacion.NombreDelCampo, StringComparison.Ordinal)))
        {
            return (null, Invalido("cuerpo"));
        }

        var valores = formulario["motivo"];
        return valores.Count == 1 && valores[0] is { Length: > 0 and <= 32 } motivo ? (motivo, null) : (null, Invalido("motivo"));
    }
}
