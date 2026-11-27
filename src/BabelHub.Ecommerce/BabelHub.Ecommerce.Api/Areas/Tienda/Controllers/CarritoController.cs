using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Una línea del carrito lista para pintar: textos con formato peruano, la sucursal que la atiende, la disponibilidad en ella con texto e icono y lo que puede hacer la persona.</summary>
public sealed record LineaDeCarritoPintada(
    Guid LineaId,
    string Titulo,
    string? Isbn,
    bool EsDigital,
    string FormatoTexto,
    int Cantidad,
    int CantidadMaxima,
    string PrecioTexto,
    string ImporteTexto,
    DisponibilidadVista? Disponibilidad,
    string? NotaDeFrescura,
    bool Retirada,
    string? Sucursal,
    bool SinSucursal);

/// <summary>Un aviso persistente del carrito (edición retirada o línea ajustada por la fusión) con su texto ya redactado.</summary>
public sealed record AvisoPintado(Guid Id, string Texto);

/// <summary>La página del carrito (visitante o cuenta): sus líneas, el subtotal, los avisos y el estado que reemplace al contenido. Los precios y la disponibilidad ya vienen del servidor; la pantalla solo los pinta.</summary>
public sealed class CarritoVista : ModeloBaseDePagina
{
    public IReadOnlyList<LineaDeCarritoPintada> Lineas { get; set; } = [];

    public IReadOnlyList<AvisoPintado> Avisos { get; set; } = [];

    public string SubtotalTexto { get; set; } = FormatoPeruano.Soles(0m);

    public bool HayImpresos { get; set; }

    public bool HayDigitales { get; set; }

    public bool HayCompraEnCurso { get; set; }

    /// <summary>Alguna línea impide continuar (agotada, retirada, sin sucursal o sin precio confirmado): el botón de continuar queda inactivo y se explica.</summary>
    public bool HayProblema { get; set; }

    public bool PreciosNoConfirmados { get; set; }

    /// <summary>El rechazo de un cambio en una tarjeta, con la cantidad anterior conservada (CAR-44).</summary>
    public string? AlertaTitulo { get; set; }

    public string? AlertaMensaje { get; set; }

    /// <summary>Un estado que reemplaza al contenido (vacío o error).</summary>
    public EstadoVista? Estado { get; set; }
}

/// <summary>
/// El carrito (plan §4, CAR-01 a CAR-04, CAR-16, CAR-35): <c>GET /carrito</c> (sin efectos), <c>POST /carrito/lineas</c> (agregar, con la sucursal de un impreso), <c>POST /carrito/lineas/{lineaId}</c> (cambiar la cantidad), <c>POST /carrito/lineas/{lineaId}/quitar</c> y <c>POST /carrito/avisos/{avisoId}/visto</c>.
/// <b>Presentación pura</b>: lee el cuerpo con esquema estricto (un campo desconocido, repetido o un identificador de carrito, checkout o cuenta se rechaza y el último registra un acceso denegado), llama a <see cref="ICarritoDelCliente"/> y traduce su resultado: una pantalla, una redirección
/// <c>303</c> (o <c>HX-Redirect</c> con htmx) tras un <c>POST</c> exitoso, o el error: en pantalla para un navegador y <c>application/problem+json</c> para quien no pide HTML. La cookie <c>__Host-bh-carrito</c> solo se escribe aquí y se rota al fusionar. El token antifalsificación de todo
/// <c>POST</c> lo valida el filtro global antes de la acción. Con una cuenta y una cookie de visitante todavía presente (una fusión aplazada por una compra en curso), cada <c>POST</c> reintenta la fusión primero y nunca la deja fallar la acción.
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(EntradasHttpDelCarrito.TopeDelCuerpo)]
public sealed class CarritoController(ICarritoDelCliente carrito, IActorActual actor, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    private static readonly string[] CamposDeAgregar = ["edicionId", "cantidad", "sucursal"];

    private static readonly string[] CamposDeCambiar = ["cantidad"];

    private static readonly string[] SinCampos = [];

    internal const int MaximoDeUnidades = 20;

    /// <summary>El mayor número que se lee al cambiar una línea: más de 20 lo rechaza el tope de unidades (CAR-38), no la forma.</summary>
    private const int MaximoLegible = 1000;

    [HttpGet("/carrito")]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        var resultado = await carrito.Obtener(new SolicitudSobreElCarrito(CookieDelCarrito.Leer(Request)), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        AplicarLaCookie(resultado.Valor.CookieNueva, resultado.Valor.BorrarLaCookie || (CookieDelCarrito.HayCookie(Request) && CookieDelCarrito.Leer(Request) is null));
        var modelo = await pagina.Completar(new CarritoVista(), "Tu carrito", cancelacion: cancelacion);
        modelo.HaySesion = actor.Actual is { Tipo: TipoDeActor.Usuario };
        var vista = Construir(modelo, resultado.Valor.Carrito);
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    [HttpPost("/carrito/lineas")]
    public async Task<IActionResult> Agregar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeAgregar, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!EntradasHttpDelCarrito.TryGuid(campos, "edicionId", out var edicionId))
        {
            return await Falla(campos.ContainsKey("edicionId") ? EntradasHttpDelCarrito.Invalido("edicionId") : EntradasHttpDelCarrito.Falta("edicionId"), cancelacion);
        }

        if (!EntradasHttpDelCarrito.TryEntero(campos, "cantidad", 1, MaximoDeUnidades, out var cantidad))
        {
            return await Falla(campos.ContainsKey("cantidad") ? EntradasHttpDelCarrito.Invalido("cantidad") : EntradasHttpDelCarrito.Falta("cantidad"), cancelacion);
        }

        var sucursal = campos.GetValueOrDefault("sucursal") is { Length: > 0 } elegida ? elegida : null;
        await ReintentarLaFusion(cancelacion);
        var agregada = await carrito.AgregarLinea(new AgregarLineaEntrada(edicionId, cantidad, CookieDelCarrito.Leer(Request), sucursal), cancelacion);
        RecordarLaSucursal(agregada, edicionId, sucursal);
        return await Terminar(agregada, cancelacion);
    }

    [HttpPost("/carrito/lineas/{lineaId}")]
    public async Task<IActionResult> Cambiar(string lineaId, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeCambiar, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!Guid.TryParseExact(lineaId, "D", out var linea) || linea == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("lineaId"), cancelacion);
        }

        if (!EntradasHttpDelCarrito.TryEntero(campos, "cantidad", 1, MaximoLegible, out var cantidad))
        {
            return await Falla(campos.ContainsKey("cantidad") ? EntradasHttpDelCarrito.Invalido("cantidad") : EntradasHttpDelCarrito.Falta("cantidad"), cancelacion);
        }

        await ReintentarLaFusion(cancelacion);
        var cambiada = await carrito.ModificarLinea(new ModificarLineaEntrada(linea, cantidad, CookieDelCarrito.Leer(Request)), cancelacion);
        return cambiada.EsExito ? await Terminar(cambiada, cancelacion) : await FallaEnElCarrito(cambiada.Error!, cancelacion);
    }

    [HttpPost("/carrito/lineas/{lineaId}/quitar")]
    public async Task<IActionResult> Quitar(string lineaId, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, SinCampos, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!Guid.TryParseExact(lineaId, "D", out var linea) || linea == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("lineaId"), cancelacion);
        }

        await ReintentarLaFusion(cancelacion);
        return await Terminar(await carrito.EliminarLinea(new EliminarLineaEntrada(linea, CookieDelCarrito.Leer(Request)), cancelacion), cancelacion);
    }

    /// <summary>La sucursal con que se agregó un impreso queda como preselección de la ficha (WEB-16): solo cuando el alta se aceptó y la línea quedó en ella.</summary>
    private void RecordarLaSucursal(Resultado<ResultadoDeCarrito> agregada, Guid edicionId, string? sucursal)
    {
        if (sucursal is not null && agregada.EsExito && agregada.Valor.Carrito.Lineas.Any(l => l.EdicionId == edicionId && l.CodigoDeSucursal == sucursal))
        {
            Response.Cookies.Append(PreferenciasDeLaPagina.CookieDeSucursal, sucursal, PreferenciasDeLaPagina.OpcionesDeCookie());
        }
    }

    /// <summary>Un cambio rechazado en una tarjeta: un navegador ve el carrito, con su cantidad anterior, y el motivo arriba (CAR-44).</summary>
    private async Task<IActionResult> FallaEnElCarrito(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (!PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await Falla(error, cancelacion);
        }

        var actual = await carrito.Obtener(new SolicitudSobreElCarrito(CookieDelCarrito.Leer(Request)), cancelacion);
        if (!actual.EsExito)
        {
            return await Falla(error, cancelacion);
        }

        var problema = ProblemasDelCarrito.Crear(HttpContext, errores, error);
        await DenegacionesHttp.Registrar(HttpContext, error.Categoria, error.Clave, problema.Code);
        var modelo = await pagina.Completar(new CarritoVista(), "Tu carrito", cancelacion: cancelacion);
        modelo.HaySesion = actor.Actual is { Tipo: TipoDeActor.Usuario };
        var vista = Construir(modelo, actual.Valor.Carrito);
        var texto = PantallasDelCarrito.Traducir(problema, "/carrito", DetalleDeLaFalta(error, actual.Valor.Carrito)).Vista;
        vista.AlertaTitulo = texto.Titulo;
        vista.AlertaMensaje = texto.Mensaje;
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : problema.Status;
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    /// <summary>El nombre de la sucursal sin stock suficiente, tomado de las líneas que ya están en el carrito.</summary>
    private static DetalleDelStock? DetalleDeLaFalta(ErrorDeAplicacion error, VistaDelCarrito vista) =>
        error.Errores.FirstOrDefault(e => e.Campo == "sucursal") is { } campo && vista.Lineas.FirstOrDefault(l => l.CodigoDeSucursal == campo.Clave) is { } linea
            ? new DetalleDelStock(linea.NombreDeSucursal, null)
            : null;

    /// <summary>La persona leyó un aviso («Entendido»): se marca como visto y no vuelve a salir. Solo con sesión (los avisos son de la cuenta).</summary>
    [HttpPost("/carrito/avisos/{avisoId}/visto")]
    public async Task<IActionResult> AvisoVisto(string avisoId, CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, SinCampos, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!Guid.TryParseExact(avisoId, "D", out var aviso) || aviso == Guid.Empty)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("avisoId"), cancelacion);
        }

        return await Terminar(await carrito.MarcarAvisosComoVistos([aviso], cancelacion), cancelacion);
    }

    // ---------- La vista ----------

    private static CarritoVista Construir(CarritoVista modelo, VistaDelCarrito vista)
    {
        modelo.Avisos = [.. vista.Avisos.Select(a => new AvisoPintado(a.Id, TextoDelAviso(a)))];
        modelo.HayCompraEnCurso = vista.HayUnaCompraEnCurso;
        modelo.PreciosNoConfirmados = vista.PreciosNoConfirmados;
        if (vista.Lineas.Count == 0)
        {
            modelo.Estado = EstadoVista.Vacio("Tu carrito está vacío", "Agrega libros desde el catálogo para comenzar tu compra.", "Explorar el catálogo", "/catalogo");
            return modelo;
        }

        modelo.Lineas = [.. vista.Lineas.Select(l => ALaLinea(l, vista.PreciosNoConfirmados))];
        modelo.HayImpresos = vista.Lineas.Any(l => l.Formato == FormatoDeEdicion.Impresa);
        modelo.HayDigitales = vista.Lineas.Any(l => l.Formato == FormatoDeEdicion.Digital);
        modelo.SubtotalTexto = vista.PreciosNoConfirmados ? "No se puede confirmar" : FormatoPeruano.Soles(vista.Subtotal);
        modelo.HayProblema = vista.PreciosNoConfirmados || modelo.Lineas.Any(l => l.Retirada || l.SinSucursal || l.Disponibilidad?.Estado == Web.Estados.EstadoDeDisponibilidad.Agotado);
        return modelo;
    }

    private static LineaDeCarritoPintada ALaLinea(LineaDelCarritoVista l, bool sinPrecios)
    {
        var digital = l.Formato == FormatoDeEdicion.Digital;
        return new LineaDeCarritoPintada(
            l.LineaId,
            l.Titulo,
            l.Isbn,
            digital,
            digital ? "Libro digital" : "Libro impreso",
            l.Cantidad,
            digital ? 1 : l.MaximoEditable ?? MaximoDeUnidades,
            sinPrecios ? "Precio no disponible" : FormatoPeruano.Soles(l.PrecioUnitario),
            sinPrecios ? "Precio no disponible" : FormatoPeruano.Soles(l.Importe),
            ADisponibilidad(l),
            l.Disponibilidad is { Frescura: not FrescuraDeLaDisponibilidad.Reciente } ? "Este dato puede estar desactualizado." : null,
            l.Retirada,
            l.NombreDeSucursal,
            l.SinSucursal);
    }

    /// <summary>La disponibilidad de la línea: de una digital, al instante; de un impreso, lo vendible en la sucursal que la atiende (nunca el de la red).</summary>
    private static DisponibilidadVista? ADisponibilidad(LineaDelCarritoVista l)
    {
        if (l.Formato == FormatoDeEdicion.Digital)
        {
            return DisponibilidadVista.DisponibleAlInstante();
        }

        if (l.Retirada || l.SinSucursal)
        {
            return null;
        }

        return l.VendibleEnLaSucursal switch
        {
            null => DisponibilidadVista.NoSePuedeConfirmar(),
            <= 0 => DisponibilidadVista.Agotado(),
            { } hay => DisponibilidadVista.EnLaSucursal(hay),
        };
    }

    private static string TextoDelAviso(AvisoDelCarrito a) => a.Tipo == TipoDeAvisoDelCarrito.EdicionRetirada
        ? $"«{a.Titulo}» fue retirada del catálogo y se quitó de tu carrito."
        : a.Motivo switch
        {
            MotivoDeAjusteDeAviso.SinStock => $"Quitamos «{a.Titulo}» de tu carrito porque ya no hay stock.",
            MotivoDeAjusteDeAviso.YaPoseida => $"Quitamos «{a.Titulo}» de tu carrito porque ya está en tu biblioteca.",
            MotivoDeAjusteDeAviso.NoVendible => $"Quitamos «{a.Titulo}» de tu carrito porque ya no está a la venta.",
            MotivoDeAjusteDeAviso.RecortadaAlMaximo => $"Ajustamos «{a.Titulo}» a {a.CantidadFinal} unidades, el máximo disponible en su sucursal.",
            MotivoDeAjusteDeAviso.DigitalAUnaUnidad => $"«{a.Titulo}» es digital: dejamos una sola licencia en tu carrito.",
            MotivoDeAjusteDeAviso.TopeDeLineas => $"No pudimos pasar «{a.Titulo}» a tu carrito: llegó al máximo de 30 líneas.",
            MotivoDeAjusteDeAviso.TopeDeUnidades => $"Ajustamos «{a.Titulo}»: llegaste al máximo de unidades por libro.",
            _ => $"Ajustamos «{a.Titulo}» en tu carrito.",
        };

    // ---------- Piezas ----------

    /// <summary>
    /// Con una cuenta y la cookie de un visitante todavía presente (una fusión aplazada), la reintenta antes de la acción: es idempotente y <b>nunca</b> impide la acción. La cookie se borra solo cuando la Aplicación lo dice.
    /// </summary>
    private async Task ReintentarLaFusion(CancellationToken cancelacion)
    {
        if (actor.Actual is not { Tipo: TipoDeActor.Usuario } || CookieDelCarrito.Leer(Request) is not { } cookie)
        {
            return;
        }

        try
        {
            var fusion = await carrito.FusionarCarritoDeVisitante(cookie, cancelacion);
            if (fusion.EsExito)
            {
                AplicarLaCookie(null, fusion.Valor.BorrarLaCookie);
            }
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // La fusión nunca impide la acción de la persona: se reintenta en el siguiente POST.
        }
    }

    private async Task<IActionResult> Terminar(Resultado<ResultadoDeCarrito> resultado, CancellationToken cancelacion)
    {
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        AplicarLaCookie(resultado.Valor.CookieNueva, resultado.Valor.BorrarLaCookie);
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult("/carrito");
        }

        // POST/Redirect/GET: 303 See Other, para que el navegador pida la pantalla con GET.
        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = "/carrito";
        return new EmptyResult();
    }

    private void AplicarLaCookie(string? cookieNueva, bool borrar) => CookieDelCarrito.Aplicar(Response, cookieNueva, borrar);

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await PantallasDelCarrito.Escribir(this, pagina, errores, error, "/carrito", cancelacion);
        }

        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
