using System.Globalization;
using BabelHub.Ecommerce.Api.Envios;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Sucursal.Controllers;

/// <summary>Una acción que logística puede hacer sobre un envío: su texto, su ruta y su icono.</summary>
public sealed record AccionDeEnvioVista(string Texto, string Ruta, string Icono);

/// <summary>Un envío de la lista de logística, listo para pintar: <b>sin ningún dato de contacto</b> (ENV-08); el estado con texto e icono, nunca solo con color.</summary>
public sealed record FilaDeEnvioVista(
    Guid Id,
    string IdCorto,
    string PedidoCorto,
    string Fuente,
    string EstadoTexto,
    string EstadoIcono,
    string? Guia,
    string? Operador,
    int Lineas,
    string CreadoTexto,
    string? UltimaFechaTexto,
    AccionDeEnvioVista? Accion);

/// <summary>La lista de envíos de logística: el filtro por estado, las páginas, el aviso de la última acción y el estado que reemplaza a la tabla si hace falta.</summary>
public sealed class ListaDeEnviosVista : ModeloBaseDePagina
{
    public string? Filtro { get; set; }

    public IReadOnlyList<FilaDeEnvioVista> Envios { get; set; } = [];

    public int Total { get; set; }

    public int Pagina { get; set; } = 1;

    public int TotalDePaginas { get; set; } = 1;

    public EstadoVista? Estado { get; set; }

    public string? Aviso { get; set; }

    public IReadOnlyList<(string Clave, string Texto)> Filtros => LogisticaController.Filtros;

    public string RutaDeLaPagina(int pagina) => "/logistica/envios?" + string.Join('&', new[] { Filtro is { Length: > 0 } f ? "estado=" + f : null, pagina > 1 ? $"pagina={pagina}" : null }.Where(x => x is not null));
}

/// <summary>
/// Las pantallas y acciones de <b>logística</b> sobre los envíos (T16, T18; ENV-02, ENV-08, ENV-10, ENV-11, ENV-14; plan §4): <c>GET /logistica/envios</c> (sin efectos, con <c>estado</c> y <c>pagina</c>), y los <c>POST</c>
/// <c>/logistica/envios/{id}/guia</c>, <c>/reparto</c> y <c>/entregado</c>. <b>Presentación pura</b>: no lleva identificadores del actor, rechaza una consulta o un cuerpo con campos que no declara (validación, sin repetir lo
/// recibido) y llama a <see cref="IEnviosDeLogistica"/>; quién puede qué lo decide la Aplicación con el actor verificado. Tras un <c>POST</c> exitoso responde <c>303</c> a la lista (o <c>HX-Redirect</c>); un fallo se pinta en
/// pantalla para un navegador y sale como <c>application/problem+json</c> para quien no pide HTML, con el código <c>ECO-ENV-…</c> del plan y sin datos de contacto. Cada respuesta es <c>no-store</c>.
/// </summary>
[Area("Sucursal")]
[RequestSizeLimit(4096)]
public sealed class LogisticaController(IEnviosDeLogistica envios, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    private const string RutaDeLaLista = "/logistica/envios";

    private const int TamanoDePagina = 20;

    private static readonly string[] ClavesDeConsulta = ["estado", "pagina", "hecho"];

    /// <summary>Los filtros de la lista, en el orden de <see cref="EstadoDelEnvio"/>: la clave de la ruta y su texto.</summary>
    public static readonly IReadOnlyList<(string Clave, string Texto)> Filtros =
    [
        ("pendiente-de-guia", "Sin guía"),
        ("guia-generada", "Con guía"),
        ("en-reparto", "En reparto"),
        ("entregado", "Entregados"),
        ("cancelado", "Cancelados"),
    ];

    private static readonly Dictionary<string, EstadoDelEnvio> EstadoPorFiltro = new(StringComparer.Ordinal)
    {
        ["pendiente-de-guia"] = EstadoDelEnvio.PendienteDeGuia,
        ["guia-generada"] = EstadoDelEnvio.GuiaGenerada,
        ["en-reparto"] = EstadoDelEnvio.EnReparto,
        ["entregado"] = EstadoDelEnvio.Entregado,
        ["cancelado"] = EstadoDelEnvio.Cancelado,
    };

    private static readonly Dictionary<string, string> AvisoPorAccion = new(StringComparer.Ordinal)
    {
        ["guia"] = "Guía generada.",
        ["reparto"] = "Envío pasado a reparto.",
        ["entregado"] = "Envío marcado como entregado.",
    };

    [HttpGet(RutaDeLaLista)]
    public async Task<IActionResult> Lista(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (RechazarConsulta(Request, ClavesDeConsulta) is { } invalida)
        {
            return await Falla(invalida, cancelacion);
        }

        EstadoDelEnvio? estado = null;
        string? filtro = null;
        if (Request.Query["estado"].ToString() is { Length: > 0 } pedido)
        {
            if (!EstadoPorFiltro.TryGetValue(pedido, out var elegido))
            {
                return await Falla(Invalido("estado"), cancelacion);
            }

            estado = elegido;
            filtro = pedido;
        }

        var numeroDePagina = 1;
        if (Request.Query["pagina"].ToString() is { Length: > 0 } p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out numeroDePagina) || numeroDePagina < 1))
        {
            return await Falla(Invalido("pagina"), cancelacion);
        }

        var hecho = Request.Query["hecho"].ToString();
        if (hecho.Length > 0 && !AvisoPorAccion.ContainsKey(hecho))
        {
            return await Falla(Invalido("hecho"), cancelacion);
        }

        var resultado = await envios.Listar(new ListarEnviosEntrada(estado, numeroDePagina, TamanoDePagina), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pagina.Completar(new ListaDeEnviosVista(), "Envíos", AreaDeLaPagina.Sucursal, cancelacion);
        vista.Filtro = filtro;
        vista.Aviso = AvisoPorAccion.GetValueOrDefault(hecho);
        vista.Envios = [.. resultado.Valor.Envios.Select(AFila)];
        vista.Total = resultado.Valor.Total;
        vista.Pagina = resultado.Valor.Pagina;
        vista.TotalDePaginas = Math.Max(1, (int)Math.Ceiling(resultado.Valor.Total / (double)Math.Max(1, resultado.Valor.Tamano)));
        if (resultado.Valor.Total == 0 || resultado.Valor.Envios.Count == 0)
        {
            vista.Estado = filtro is null
                ? EstadoVista.Vacio("Aún no hay envíos", "Los pedidos a domicilio aparecerán aquí cuando sus libros estén listos para despachar.")
                : EstadoVista.Vacio("No hay envíos en este estado", "Prueba con otro estado.", "Ver todos", RutaDeLaLista);
        }

        return View("~/Areas/Sucursal/Views/Logistica/Lista.cshtml", vista);
    }

    [HttpPost(RutaDeLaLista + "/{id:guid}/guia")]
    public async Task<IActionResult> SolicitarGuia(Guid id, CancellationToken cancelacion) =>
        await Operar("guia", () => envios.SolicitarGuia(id, cancelacion), cancelacion);

    [HttpPost(RutaDeLaLista + "/{id:guid}/reparto")]
    public async Task<IActionResult> PasarAEnReparto(Guid id, CancellationToken cancelacion) =>
        await Operar("reparto", () => envios.PasarAEnReparto(id, cancelacion), cancelacion);

    [HttpPost(RutaDeLaLista + "/{id:guid}/entregado")]
    public async Task<IActionResult> MarcarEntregado(Guid id, CancellationToken cancelacion) =>
        await Operar("entregado", () => envios.MarcarEntregado(id, cancelacion), cancelacion);

    private async Task<IActionResult> Operar(string accion, Func<Task<Resultado<DetalleDeEnvio>>> ejecutar, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (await EntradaInvalida() is { } invalida)
        {
            return await Falla(invalida, cancelacion);
        }

        var resultado = await ejecutar();
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var destino = $"{RutaDeLaLista}?hecho={accion}";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        // POST/Redirect/GET: 303 See Other, para que el navegador pida la lista con GET.
        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }

    /// <summary>Un <c>POST</c> de logística no lleva consulta ni más campos que el token antifalsificación: lo demás es validación y no se repite.</summary>
    private async Task<ErrorDeAplicacion?> EntradaInvalida()
    {
        if (RechazarConsulta(Request, []) is { } consulta)
        {
            return consulta;
        }

        if (Request.HasFormContentType)
        {
            var formulario = await Request.ReadFormAsync();
            if (formulario.Keys.Any(k => !string.Equals(k, ProteccionContraFalsificacion.NombreDelCampo, StringComparison.Ordinal)))
            {
                return Invalido("cuerpo");
            }
        }
        else if (Request.ContentLength is > 0)
        {
            return Invalido("cuerpo");
        }

        return null;
    }

    private static ErrorDeAplicacion? RechazarConsulta(HttpRequest solicitud, IReadOnlyCollection<string> admitidas)
    {
        foreach (var (clave, valores) in solicitud.Query)
        {
            if (!admitidas.Contains(clave, StringComparer.Ordinal))
            {
                return Invalido("consulta");
            }

            if (valores.Count > 1)
            {
                return Invalido(clave);
            }
        }

        return null;
    }

    private static ErrorDeAplicacion Invalido(string campo) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "formato-invalido")]);

    private static FilaDeEnvioVista AFila(ResumenDeEnvio e)
    {
        var (texto, icono, accion) = e.Estado switch
        {
            EstadoDelEnvio.PendienteDeGuia => ("Sin guía", "reloj", new AccionDeEnvioVista("Generar guía", $"{RutaDeLaLista}/{e.Id:D}/guia", "bandeja")),
            EstadoDelEnvio.GuiaGenerada => ("Con guía", "bandeja", new AccionDeEnvioVista("Pasar a en reparto", $"{RutaDeLaLista}/{e.Id:D}/reparto", "camion")),
            EstadoDelEnvio.EnReparto => ("En reparto", "camion", new AccionDeEnvioVista("Marcar entregado", $"{RutaDeLaLista}/{e.Id:D}/entregado", "exito")),
            EstadoDelEnvio.Entregado => ("Entregado", "exito", (AccionDeEnvioVista?)null),
            _ => ("Cancelado", "candado", (AccionDeEnvioVista?)null),
        };
        var ultima = e.EntregadoEn ?? e.EnRepartoEn ?? e.GuiaGeneradaEn;
        return new FilaDeEnvioVista(e.Id, Corto(e.Id), Corto(e.PedidoId), e.CodigoDeFuente, texto, icono, e.NumeroDeGuia, e.Operador, e.CantidadDeLineas, FormatoPeruano.FechaYHora(e.CreadoEn), ultima is { } u ? FormatoPeruano.FechaYHora(u) : null, accion);
    }

    private static string Corto(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDeEnvios.PrefiereHtml(Request))
        {
            return await PantallasDeEnvios.Escribir(this, pagina, errores, error, RutaDeLaLista, cancelacion);
        }

        await ProblemasDeEnvios.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
