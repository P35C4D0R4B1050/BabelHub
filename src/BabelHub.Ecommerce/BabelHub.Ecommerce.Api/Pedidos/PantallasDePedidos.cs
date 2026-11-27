using System.Globalization;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BabelHub.Ecommerce.Api.Pedidos;

/// <summary>Una fila de un listado de pedidos, lista para pintar: el estado con texto e icono (nunca solo con color) y <b>ningún dato de contacto, cuenta ni usuario</b> (PED-40).</summary>
public sealed record FilaDePedidoVista(Guid Id, string Numero, string FechaTexto, string EstadoTexto, string EstadoIcono, string TipoTexto, string? SucursalesTexto, string TotalTexto, int Lineas, string RutaDelDetalle);

/// <summary>Un listado de pedidos con su filtro por estado, sus páginas, el aviso de la última acción y el estado estándar que reemplaza a la tabla si hace falta.</summary>
public sealed class ListaDePedidosVista : ModeloBaseDePagina
{
    public string Encabezado { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>La ruta del listado (sin consulta): sirve de base a los filtros y a las páginas.</summary>
    public string RutaDeLaLista { get; set; } = string.Empty;

    public string? Filtro { get; set; }

    public IReadOnlyList<FilaDePedidoVista> Pedidos { get; set; } = [];

    public int Total { get; set; }

    public int Pagina { get; set; } = 1;

    public int TotalDePaginas { get; set; } = 1;

    public EstadoVista? Estado { get; set; }

    public IReadOnlyList<(string Clave, string Texto)> Filtros => TextosDePedidos.Filtros;

    public string RutaDeLaPagina(int pagina)
    {
        var consulta = string.Join('&', new[] { Filtro is { Length: > 0 } f ? "estado=" + f : null, pagina > 1 ? $"pagina={pagina}" : null }.Where(x => x is not null));
        return consulta.Length == 0 ? RutaDeLaLista : RutaDeLaLista + "?" + consulta;
    }
}

/// <summary>Una línea de un pedido lista para pintar: precios en soles, el estado con texto e icono, el traslado con su fecha y si se puede marcar en una operación.</summary>
public sealed record LineaDePedidoVista(
    Guid Id,
    string Titulo,
    string? Isbn,
    bool EsDigital,
    int Cantidad,
    string PrecioTexto,
    string ImporteTexto,
    string EstadoTexto,
    string EstadoIcono,
    string? TrasladoTexto,
    string? FuenteTexto,
    string? ListoTexto,
    string? EntregadaTexto,
    string? AnuladaTexto,
    bool Operable,
    string? SucursalTexto = null);

/// <summary>El contacto de entrega: solo lo ven el titular y el personal autorizado (la lectura del personal se audita); nunca se escribe en un registro ni en un aviso.</summary>
public sealed record ContactoDelPedidoVista(string Nombre, string Telefono, string Direccion)
{
    /// <summary>Imprimirlo nunca muestra el contacto (como el de la Aplicación): ni un registro ni un mensaje lo filtran.</summary>
    public override string ToString() => "[redactado]";
}

/// <summary>Una acción sobre líneas del pedido (preparar, marcar listas, entregar): su texto, su ruta y su icono.</summary>
public sealed record AccionSobreLineasVista(string Clave, string Texto, string Ruta, string Icono);

/// <summary>El detalle de un pedido para quien lo mira: el titular (con su contacto), el encargado de sucursal (sin contacto), logística y la administración (con el contacto auditado).</summary>
public sealed class DetalleDePedidoVista : ModeloBaseDePagina
{
    public Guid PedidoId { get; set; }

    public string Numero { get; set; } = string.Empty;

    public string FechaTexto { get; set; } = string.Empty;

    public string EstadoTexto { get; set; } = string.Empty;

    public string EstadoIcono { get; set; } = "reloj";

    public string TipoTexto { get; set; } = string.Empty;

    public string? Sucursal { get; set; }

    public string? TotalTexto { get; set; }

    public string? CostoDeEnvioTexto { get; set; }

    public string? Region { get; set; }

    public IReadOnlyList<LineaDePedidoVista> Lineas { get; set; } = [];

    public ContactoDelPedidoVista? Contacto { get; set; }

    /// <summary>El contacto no se pudo mostrar porque su lectura no se pudo auditar (falla cerrada): el resto del detalle sí se muestra.</summary>
    public bool ContactoOculto { get; set; }

    public bool ContactoAnonimizado { get; set; }

    /// <summary>El pedido es a domicilio (lo dice el tipo de entrega del contrato, no la región): solo entonces tiene seguimiento de envío.</summary>
    public bool EsADomicilio { get; set; }

    /// <summary>Se muestra la sucursal de origen de una línea cuando no es la que la atiende (un traslado): solo al personal y al encargado, no al titular (minimización de datos).</summary>
    public bool MostrarLaFuente { get; set; } = true;

    public string RutaDeLaLista { get; set; } = string.Empty;

    public string RutaDelDetalle { get; set; } = string.Empty;

    /// <summary>La acción de cancelar (solo el titular y solo si procede) y, si no procede, la alternativa que le corresponde.</summary>
    public string? RutaDeCancelar { get; set; }

    /// <summary>El enlace a la pantalla de devolver (solo el titular, T26 de Devoluciones).</summary>
    public string? RutaDeDevolver { get; set; }

    public string? AlternativaTexto { get; set; }

    public string? ReembolsoPendienteTexto { get; set; }

    public string? RutaDeReintentarElReembolso { get; set; }

    public string? RutaDeAnular { get; set; }

    public IReadOnlyList<(string Clave, string Texto)> MotivosDeAnulacion => TextosDePedidos.MotivosDeAnulacion;

    public IReadOnlyList<AccionSobreLineasVista> Acciones { get; set; } = [];

    public string? Aviso { get; set; }

    public EstadoVista? Estado { get; set; }
}

/// <summary>Una operación fallida (anulación o entrega) tal como la ve la administración: sin datos personales, con los pasos que alcanzó a hacer (PED-24).</summary>
public sealed record OperacionFallidaVista(string Tipo, string PedidoCorto, string RutaDelPedido, string? Motivo, IReadOnlyList<string> Pasos, string CreadaTexto);

/// <summary>La lista de operaciones fallidas de la administración (PED-24): reintentar es repetir la operación por quien tiene el permiso.</summary>
public sealed class OperacionesFallidasVista : ModeloBaseDePagina
{
    public IReadOnlyList<OperacionFallidaVista> Operaciones { get; set; } = [];

    public int Total { get; set; }

    public int Pagina { get; set; } = 1;

    public int TotalDePaginas { get; set; } = 1;

    public EstadoVista? Estado { get; set; }

    public string RutaDeLaPagina(int pagina) => "/administracion/pedidos/operaciones-fallidas" + (pagina > 1 ? $"?pagina={pagina}" : string.Empty);
}

/// <summary>Los textos de los estados, los tipos de entrega, los filtros y los motivos de Pedidos, en un solo lugar (estado con texto e icono; ningún texto sale de un dato del pedido).</summary>
public static class TextosDePedidos
{
    /// <summary>Los filtros de un listado, en el orden de <see cref="EstadoVisibleDelPedido"/>: la clave de la consulta y su texto.</summary>
    public static readonly IReadOnlyList<(string Clave, string Texto)> Filtros =
    [
        ("confirmado", "Confirmados"),
        ("en-transito", "En tránsito"),
        ("recibido", "Recibidos"),
        ("en-preparacion", "En preparación"),
        ("listo-para-recojo", "Listos para recojo"),
        ("despachado", "Despachados"),
        ("entregado", "Entregados"),
        ("no-recogido", "No recogidos"),
        ("anulado", "Anulados"),
    ];

    /// <summary>Los motivos de la lista cerrada de la anulación (PED-13): no hay nota libre.</summary>
    public static readonly IReadOnlyList<(string Clave, string Texto)> MotivosDeAnulacion =
    [
        ("pago-duplicado", "Pago duplicado"),
        ("no-recogido", "No recogido"),
        ("otro", "Otro"),
    ];

    private static readonly Dictionary<string, EstadoVisibleDelPedido> EstadoPorFiltro = new(StringComparer.Ordinal)
    {
        ["confirmado"] = EstadoVisibleDelPedido.Confirmado,
        ["en-transito"] = EstadoVisibleDelPedido.EnTransito,
        ["recibido"] = EstadoVisibleDelPedido.Recibido,
        ["en-preparacion"] = EstadoVisibleDelPedido.EnPreparacion,
        ["listo-para-recojo"] = EstadoVisibleDelPedido.ListoParaRecojo,
        ["despachado"] = EstadoVisibleDelPedido.Despachado,
        ["entregado"] = EstadoVisibleDelPedido.Entregado,
        ["no-recogido"] = EstadoVisibleDelPedido.NoRecogido,
        ["anulado"] = EstadoVisibleDelPedido.Anulado,
    };

    public static bool EstadoDelFiltro(string clave, out EstadoVisibleDelPedido estado) => EstadoPorFiltro.TryGetValue(clave, out estado);

    public static (string Texto, string Icono) Estado(EstadoVisibleDelPedido estado) => estado switch
    {
        EstadoVisibleDelPedido.Confirmado => ("Confirmado", "reloj"),
        EstadoVisibleDelPedido.EnTransito => ("En tránsito", "camion"),
        EstadoVisibleDelPedido.Recibido => ("Recibido en la sucursal", "bandeja"),
        EstadoVisibleDelPedido.EnPreparacion => ("En preparación", "reloj"),
        EstadoVisibleDelPedido.ListoParaRecojo => ("Listo para recojo", "sucursal"),
        EstadoVisibleDelPedido.Despachado => ("Despachado", "camion"),
        EstadoVisibleDelPedido.Entregado => ("Entregado", "exito"),
        EstadoVisibleDelPedido.NoRecogido => ("No recogido", "aviso"),
        _ => ("Anulado", "candado"),
    };

    public static (string Texto, string Icono) Linea(EstadoVisibleDeLaLinea estado) => estado switch
    {
        EstadoVisibleDeLaLinea.Confirmado => ("Confirmado", "reloj"),
        EstadoVisibleDeLaLinea.EnTransito => ("En tránsito", "camion"),
        EstadoVisibleDeLaLinea.Recibido => ("Recibido en la sucursal", "bandeja"),
        EstadoVisibleDeLaLinea.EnPreparacion => ("En preparación", "reloj"),
        EstadoVisibleDeLaLinea.ListoParaRecojo => ("Listo para recojo", "sucursal"),
        EstadoVisibleDeLaLinea.Despachado => ("Despachado", "camion"),
        EstadoVisibleDeLaLinea.Entregado => ("Entregado", "exito"),
        EstadoVisibleDeLaLinea.NoRecogido => ("No recogido", "aviso"),
        EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca => ("Disponible en tu biblioteca", "libro"),
        _ => ("Anulado", "candado"),
    };

    public static string Tipo(TipoDeEntregaVisible tipo) => tipo switch
    {
        TipoDeEntregaVisible.Recojo => "Recojo en sucursal",
        TipoDeEntregaVisible.Domicilio => "Entrega a domicilio",
        _ => "Digital",
    };

    public static string Alternativa(AlternativaDeCancelacion alternativa) => alternativa switch
    {
        AlternativaDeCancelacion.Devolucion => "Este pedido ya no se puede cancelar. Si ya lo recibiste, puedes pedir una devolución.",
        AlternativaDeCancelacion.PoliticaDeReembolsoDigital => "Un libro digital no se cancela una vez comprado. Revisa la política de cambios y devoluciones.",
        _ => string.Empty,
    };
}

/// <summary>
/// Los fallos de Pedidos en <b>pantalla</b> (PED-41, WEB-07): cuando quien pide es un navegador (<c>Accept: text/html</c>) el error se pinta con el componente <c>bh-estado</c> con el texto de su código y, en lo
/// reintentable, el enlace para volver; cuando no lo es sigue saliendo el <c>application/problem+json</c> de <see cref="ProblemasDePedidos"/>. Ningún dato del pedido, de la cuenta ni del contacto llega al texto.
/// </summary>
public static class PantallasDePedidos
{
    private sealed record Texto(string Titulo, string Mensaje);

    private static readonly Dictionary<string, Texto> PorCodigo = new(StringComparer.Ordinal)
    {
        ["ECO-NO-ENCONTRADO"] = new("No encontramos ese pedido", "Puede que ya no exista o que no esté a tu alcance. Vuelve a la lista de pedidos."),
        ["ECO-PEDIDO-ESTADO-INVALIDO"] = new("El pedido ya cambió de estado", "Otra persona o el sistema lo actualizó mientras mirabas la pantalla. Vuelve a la lista para ver su estado actual."),
        ["ECO-PEDIDO-NO-CANCELABLE"] = new("Este pedido ya no se puede cancelar", "Una vez que un libro digital se compró o un impreso salió de la sucursal, el pedido no se cancela. Mira la alternativa que te corresponde en el detalle del pedido."),
        ["ECO-PEDIDO-OPERACION-EN-CURSO"] = new("Ya hay una operación en curso", "Otra operación está actualizando este pedido. Espera unos segundos y vuelve a mirar el detalle."),
        ["ECO-LINEA-ESPERA-TRASLADO"] = new("El libro aún espera su traslado", "Esta línea no se puede preparar hasta que el traslado llegue a la sucursal. El pedido sigue como estaba."),
        ["ECO-PEDIDO-SIN-LINEAS-ANULABLES"] = new("No hay líneas que anular", "Todas las líneas del pedido son digitales con el acceso ya concedido o ya están anuladas."),
        ["ECO-CONFLICTO"] = new("No pudimos completar la operación", "Otra solicitud estaba usando este pedido. El pedido sigue como estaba: vuelve a intentarlo en unos segundos."),
        ["ECO-SERVICIO-NO-DISPONIBLE"] = new("El servicio no respondió", "No se completó la operación y el pedido sigue como estaba. Puedes volver a intentarlo en unos minutos."),
    };

    /// <summary>Un navegador pide una página: <c>Accept</c> incluye <c>text/html</c>. Una prueba o una integración (sin ese tipo) recibe el problema en JSON.</summary>
    public static bool PrefiereHtml(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.GetTypedHeaders().Accept.Any(a => a.MediaType.HasValue && string.Equals(a.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    public static RespuestaDePantalla Traducir(ProblemaDto problema, ErrorDeAplicacion error, string rutaDeReintento)
    {
        ArgumentNullException.ThrowIfNull(problema);
        ArgumentNullException.ThrowIfNull(error);
        if (PorCodigo.TryGetValue(problema.Code, out var texto))
        {
            // El reintento solo se ofrece donde reintentar sirve: no donde el pedido ya no está o ya cambió.
            var reintentable = problema.Code is "ECO-CONFLICTO" or "ECO-SERVICIO-NO-DISPONIBLE" or "ECO-PEDIDO-OPERACION-EN-CURSO";
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = texto.Titulo,
                    Mensaje = texto.Mensaje,
                    RutaDeReintento = reintentable ? rutaDeReintento : null,
                    CodigoDeSoporte = problema.Code,
                    IdentificadorDeSeguimiento = problema.TraceId,
                },
                problema.Status,
                null,
                true,
                []);
        }

        var respuesta = TraductorDeErrores.Traducir(problema, rutaDeReintento: rutaDeReintento, parteAfectada: "No podemos mostrar los pedidos ahora. Inténtalo de nuevo en unos minutos.");
        return error.Categoria == CategoriaDeError.Limite
            ? respuesta with { Vista = respuesta.Vista with { EsperaSugeridaSegundos = ProblemasDePedidos.EsperaEnSegundos(error) } }
            : respuesta;
    }

    /// <summary>Escribe el fallo como pantalla completa (el estado dentro del layout) con el código HTTP del módulo (<c>200</c> con htmx), <c>Retry-After</c> en un límite y <c>no-store</c>.</summary>
    public static async Task<IActionResult> Escribir(Controller controlador, ContextoDePagina pagina, OpcionesDeErrores errores, AreaDeLaPagina area, ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(controlador);
        var contexto = controlador.HttpContext;
        var problema = ProblemasDePedidos.Crear(contexto, errores, error);
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        var respuesta = Traducir(problema, error, rutaDeReintento);
        if (respuesta.RedirigirA is { } destino)
        {
            return controlador.Request.EsHtmx() ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = respuesta.Vista }, respuesta.Vista.Titulo, area, cancelacion).ConfigureAwait(false);
        if (error.Categoria == CategoriaDeError.Limite)
        {
            controlador.Response.Headers["Retry-After"] = ProblemasDePedidos.EsperaEnSegundos(error).ToString(CultureInfo.InvariantCulture);
        }

        controlador.Response.StatusCode = controlador.Request.EsHtmx() ? StatusCodes.Status200OK : respuesta.EstadoHttp;
        controlador.Response.Headers.CacheControl = "no-store";
        return controlador.View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}

/// <summary>
/// La base de las pantallas de Pedidos (T31): la validación estricta de la consulta y del cuerpo (lo que la acción no declara es validación y no se repite), el mapeo de un listado y de un detalle a su vista y la
/// traducción de un fallo a pantalla o a <c>application/problem+json</c>. <b>Presentación pura</b>: ninguna regla de negocio ni identificador del actor; quién puede qué lo decide la Aplicación. Cada respuesta es <c>no-store</c>.
/// </summary>
public abstract class ControladorDePedidos(ContextoDePagina pagina, OpcionesDeErrores errores, AreaDeLaPagina area) : Controller
{
    /// <summary>Lo más que una operación acepta por solicitud: el tope de líneas de un pedido (30).</summary>
    protected const int TopeDeLineasPorOperacion = 30;

    protected const int TamanoDePagina = 20;

    /// <summary>La página más lejana que se pide (20 por página: 10 000 filas): un offset mayor no lo necesita ninguna pantalla y lo acota también el límite de consultas por usuario.</summary>
    protected const int PaginaMaxima = FiltroDePedidos.PaginaMaxima;

    private static readonly Dictionary<string, string> AvisoPorAccion = new(StringComparer.Ordinal)
    {
        ["preparar"] = "Las líneas pasaron a preparación.",
        ["listas"] = "Las líneas se marcaron como listas.",
        ["entregar"] = "La entrega quedó registrada.",
        ["cancelar"] = "El pedido se canceló y el reembolso quedó registrado.",
        ["anular"] = "El pedido se anuló y el reembolso quedó registrado.",
        ["reembolso"] = "El reembolso se reintentó.",
    };

    protected ContextoDePagina Pagina => pagina;

    protected static ErrorDeAplicacion Invalido(string campo) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "formato-invalido")]);

    /// <summary>Rechaza una consulta con claves que la pantalla no declara o con valores repetidos.</summary>
    protected static ErrorDeAplicacion? RechazarConsulta(HttpRequest solicitud, IReadOnlyCollection<string> admitidas)
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

    /// <summary>El filtro de un listado: <c>estado</c> (una clave conocida) y <c>pagina</c> (un entero positivo). Un valor desconocido es validación.</summary>
    protected static ErrorDeAplicacion? LeerElFiltro(HttpRequest solicitud, out string? clave, out FiltroDePedidos filtro)
    {
        clave = null;
        filtro = new FiltroDePedidos();
        EstadoVisibleDelPedido? estado = null;
        if (solicitud.Query["estado"].ToString() is { Length: > 0 } pedido)
        {
            if (!TextosDePedidos.EstadoDelFiltro(pedido, out var elegido))
            {
                return Invalido("estado");
            }

            estado = elegido;
            clave = pedido;
        }

        var numero = 1;
        if (solicitud.Query["pagina"].ToString() is { Length: > 0 } p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out numero) || numero is < 1 or > PaginaMaxima))
        {
            return Invalido("pagina");
        }

        filtro = new FiltroDePedidos(estado, numero, TamanoDePagina);
        return null;
    }

    /// <summary>El aviso de la última acción (<c>hecho</c>) y cuántas líneas no cambiaron (<c>omitidas</c>): ambos de una lista cerrada o un entero acotado, nunca texto libre.</summary>
    protected static ErrorDeAplicacion? LeerElAviso(HttpRequest solicitud, out string? aviso)
    {
        aviso = null;
        var hecho = solicitud.Query["hecho"].ToString();
        if (hecho.Length == 0)
        {
            return solicitud.Query.ContainsKey("omitidas") ? Invalido("omitidas") : null;
        }

        if (!AvisoPorAccion.TryGetValue(hecho, out var texto))
        {
            return Invalido("hecho");
        }

        var omitidas = 0;
        if (solicitud.Query["omitidas"].ToString() is { Length: > 0 } o && (!int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out omitidas) || omitidas > TopeDeLineasPorOperacion))
        {
            return Invalido("omitidas");
        }

        aviso = omitidas > 0
            ? $"{texto} {omitidas} {(omitidas == 1 ? "línea no cambió" : "líneas no cambiaron")}: esperan su traslado o ya habían cambiado de estado."
            : texto;
        return null;
    }

    /// <summary>Un <c>POST</c> sin cuerpo (cancelar, reintentar): no lleva consulta ni más campos que el token antifalsificación.</summary>
    protected async Task<ErrorDeAplicacion?> CuerpoVacio()
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

    /// <summary>
    /// El cuerpo de una operación sobre líneas: solo <c>lineaIds</c> (identificadores válidos, sin repetir y a lo más <see cref="TopeDeLineasPorOperacion"/>) y el token. Lo demás es validación y no se repite; los
    /// identificadores ajenos no se detectan aquí: los rechaza la Aplicación con el mismo «no encontrado» (PED-41).
    /// </summary>
    protected async Task<(IReadOnlyList<Guid>? Lineas, ErrorDeAplicacion? Error)> LeerLasLineas()
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
        if (formulario.Keys.Any(k => !string.Equals(k, "lineaIds", StringComparison.Ordinal) && !string.Equals(k, ProteccionContraFalsificacion.NombreDelCampo, StringComparison.Ordinal)))
        {
            return (null, Invalido("cuerpo"));
        }

        var valores = formulario["lineaIds"];
        if (valores.Count is 0 or > TopeDeLineasPorOperacion)
        {
            return (null, Invalido("lineaIds"));
        }

        var lineas = new List<Guid>(valores.Count);
        foreach (var valor in valores)
        {
            if (!Guid.TryParseExact(valor, "D", out var id) || id == Guid.Empty || lineas.Contains(id))
            {
                return (null, Invalido("lineaIds"));
            }

            lineas.Add(id);
        }

        return (lineas, null);
    }

    /// <summary>Tras un <c>POST</c> exitoso: 303 (o <c>HX-Redirect</c>) al destino, para que el navegador lo pida con GET (POST/Redirect/GET).</summary>
    protected IActionResult Redirigir(string destino)
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

    /// <summary>El destino tras operar líneas: el detalle del pedido con la acción hecha y cuántas líneas no cambiaron.</summary>
    protected IActionResult RedirigirTrasOperar(string rutaDelDetalle, string accion, LineasOperadas operadas)
    {
        var omitidas = operadas.Lineas.Count(l => l.Resultado != ResultadoVisibleDeLinea.Aplicada);
        return Redirigir($"{rutaDelDetalle}?hecho={accion}" + (omitidas > 0 ? $"&omitidas={omitidas}" : string.Empty));
    }

    protected async Task<IActionResult> Falla(ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        if (PantallasDePedidos.PrefiereHtml(Request))
        {
            return await PantallasDePedidos.Escribir(this, pagina, errores, area, error, rutaDeReintento, cancelacion);
        }

        await ProblemasDePedidos.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }

    protected static string Corto(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    /// <summary>El listado de un conjunto de pedidos como vista: sin contacto ni cuenta; vacío con el estado estándar y el filtro conservado.</summary>
    protected async Task<ListaDePedidosVista> ListaComoVista(PaginaDePedidos resultado, string? filtro, string encabezado, string descripcion, string rutaDeLaLista, Func<Guid, string> rutaDelDetalle, string textoVacio, CancellationToken cancelacion)
    {
        var vista = await pagina.Completar(new ListaDePedidosVista(), encabezado, area, cancelacion);
        vista.Encabezado = encabezado;
        vista.Descripcion = descripcion;
        vista.RutaDeLaLista = rutaDeLaLista;
        vista.Filtro = filtro;
        vista.Total = resultado.Total;
        vista.Pagina = resultado.Pagina;
        vista.TotalDePaginas = Math.Max(1, (int)Math.Ceiling(resultado.Total / (double)Math.Max(1, resultado.Tamano)));
        vista.Pedidos = [.. resultado.Pedidos.Select(p =>
        {
            var (estado, icono) = TextosDePedidos.Estado(p.Estado);
            var codigos = p.CodigosDeSucursales is { Count: > 0 } suyas ? suyas : p.CodigoDeSucursalDeRecojo is { } comun ? [comun] : [];
            var sucursales = codigos.Count == 0 ? null : string.Join(", ", codigos.Select(c => NombreDeSucursal(vista, c)));
            return new FilaDePedidoVista(p.PedidoId, p.Numero, FormatoPeruano.FechaYHora(p.CreadoEn), estado, icono, TextosDePedidos.Tipo(p.TipoDeEntrega), sucursales, FormatoPeruano.Soles(p.Total), p.CantidadDeLineas, rutaDelDetalle(p.PedidoId));
        })];
        if (resultado.Total == 0 || resultado.Pedidos.Count == 0)
        {
            vista.Estado = filtro is null
                ? EstadoVista.Vacio("Aún no hay pedidos", textoVacio)
                : EstadoVista.Vacio("No hay pedidos en este estado", "Prueba con otro estado.", "Ver todos", rutaDeLaLista);
        }

        return vista;
    }

    /// <summary>La base del detalle de un pedido: el resumen y las líneas ya traducidos; cada pantalla añade lo suyo (contacto, acciones, cancelación, anulación).</summary>
    protected async Task<DetalleDePedidoVista> NuevoDetalle(ResumenDePedido resumen, IReadOnlyList<LineaVisibleDelPedido> lineas, string rutaDeLaLista, string rutaDelDetalle, string? aviso, CancellationToken cancelacion)
    {
        var vista = await pagina.Completar(new DetalleDePedidoVista(), $"Pedido {resumen.Numero}", area, cancelacion);
        var (estado, icono) = TextosDePedidos.Estado(resumen.Estado);
        vista.PedidoId = resumen.PedidoId;
        vista.Numero = resumen.Numero;
        vista.FechaTexto = FormatoPeruano.FechaYHora(resumen.CreadoEn);
        vista.EstadoTexto = estado;
        vista.EstadoIcono = icono;
        vista.TipoTexto = TextosDePedidos.Tipo(resumen.TipoDeEntrega);
        vista.EsADomicilio = resumen.TipoDeEntrega == TipoDeEntregaVisible.Domicilio;
        vista.Sucursal = resumen.CodigoDeSucursalDeRecojo is { } comun ? NombreDeSucursal(vista, comun) : null;
        vista.TotalTexto = FormatoPeruano.Soles(resumen.Total);
        vista.Lineas = LineasComoVista(lineas, codigo => NombreDeSucursal(vista, codigo));
        vista.RutaDeLaLista = rutaDeLaLista;
        vista.RutaDelDetalle = rutaDelDetalle;
        // Un aviso de «cancelado» o «anulado» solo se muestra si el pedido lo está: un enlace con `?hecho=` no puede afirmar lo que no pasó.
        vista.Aviso = Request.Query["hecho"].ToString() is "cancelar" or "anular" && resumen.Estado != EstadoVisibleDelPedido.Anulado ? null : aviso;
        return vista;
    }

    /// <summary>El detalle de logística y de la administración: con el contacto (cuya lectura ya se auditó en la Aplicación) o con el aviso de que no se pudo mostrar; el reembolso pendiente se enlaza solo a quien puede reintentarlo.</summary>
    protected async Task<DetalleDePedidoVista> DetalleDePersonalComoVista(DetalleDelPedidoDePersonal detalle, string rutaDeLaLista, string rutaDelDetalle, string? aviso, CancellationToken cancelacion)
    {
        var vista = await NuevoDetalle(detalle.Resumen, detalle.Lineas, rutaDeLaLista, rutaDelDetalle, aviso, cancelacion);
        vista.CostoDeEnvioTexto = FormatoPeruano.Soles(detalle.CostoDeEnvio);
        vista.Region = detalle.RegionDeEntrega;
        vista.Contacto = ContactoComoVista(detalle.Contacto);
        vista.ContactoOculto = detalle.ContactoOculto;
        vista.ContactoAnonimizado = detalle.ContactoAnonimizado;
        vista.ReembolsoPendienteTexto = ReembolsoPendienteComoTexto(detalle.ReembolsoPendiente);
        return vista;
    }

    /// <summary>El nombre de la sucursal si la página trae la lista de sucursales (la tienda); si no, o si ya no figura, su código.</summary>
    private static string NombreDeSucursal(ModeloBaseDePagina vista, string codigo) =>
        vista.Sucursales.FirstOrDefault(s => string.Equals(s.Codigo, codigo, StringComparison.Ordinal))?.Nombre ?? codigo;

    protected static IReadOnlyList<LineaDePedidoVista> LineasComoVista(IReadOnlyList<LineaVisibleDelPedido> lineas, Func<string, string> nombreDeSucursal) =>
    [
        .. lineas.Select(l =>
        {
            var (estado, icono) = TextosDePedidos.Linea(l.Estado);
            var traslado = l.Traslado is null
                ? null
                : l.Traslado.EstaEnTransito
                    ? "Traslado en camino" + (l.Traslado.FechaEstimadaDeLlegada is { } f ? $", llega el {FormatoPeruano.Fecha(f)}" : string.Empty)
                    : l.Traslado.FechaEstimadaDeLlegada is { } estimada ? $"Traslado en preparación, llega el {FormatoPeruano.Fecha(estimada)}" : "Traslado por confirmar";
            return new LineaDePedidoVista(
                l.LineaId,
                l.Titulo,
                l.Isbn,
                l.EsDigital,
                l.Cantidad,
                FormatoPeruano.Soles(l.PrecioUnitario),
                FormatoPeruano.Soles(l.Importe),
                estado,
                icono,
                traslado,
                l.CodigoDeFuente == l.CodigoDeSucursal ? null : l.CodigoDeFuente,
                l.ListoEn is { } listo ? FormatoPeruano.FechaYHora(listo) : null,
                l.EntregadaEn is { } entregada ? FormatoPeruano.FechaYHora(entregada) : null,
                l.AnuladaEn is { } anulada ? FormatoPeruano.FechaYHora(anulada) : null,
                l.Operable,
                l.CodigoDeSucursal is { } sucursal ? nombreDeSucursal(sucursal) : null);
        }),
    ];

    protected static ContactoDelPedidoVista? ContactoComoVista(ContactoDeEntrega? contacto) =>
        contacto is null ? null : new ContactoDelPedidoVista(contacto.Nombre, contacto.Telefono, contacto.Direccion);

    protected static string? ReembolsoPendienteComoTexto(ReembolsoPendienteDelPedido? pendiente) =>
        pendiente is null ? null : $"Hay un reembolso de {FormatoPeruano.Soles(pendiente.Monto)} pendiente: la pasarela no lo confirmó todavía y se reintentará.";
}
