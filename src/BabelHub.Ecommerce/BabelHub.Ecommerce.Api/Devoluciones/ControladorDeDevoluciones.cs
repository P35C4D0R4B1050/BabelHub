using System.Globalization;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Envios;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Devoluciones;

/// <summary>Una línea de una devolución lista para pintar: el estado con texto e icono (nunca solo con color) y si el reembolso está pendiente de la pasarela.</summary>
public sealed record LineaDeDevolucionPantalla(Guid EdicionId, int Cantidad, string MontoTexto, string EstadoTexto, string EstadoIcono, bool ReembolsoPendiente);

/// <summary>
/// Una devolución lista para pintar. El motivo y el texto de la persona llegan como <see cref="TextoLibre"/> y <b>solo se imprimen codificados</b> (Razor los codifica siempre); no viajan a ningún mensaje, registro ni error (DEV-29, DEV-31).
/// </summary>
public sealed record DevolucionPantalla(
    Guid Id,
    string IdCorto,
    string Ruta,
    string PedidoCorto,
    string Tipo,
    bool EsReporte,
    string? Sucursal,
    string CreadaTexto,
    TextoLibre Motivo,
    TextoLibre? Texto,
    bool Redactada,
    IReadOnlyList<LineaDeDevolucionPantalla> Lineas);

/// <summary>Una lista de devoluciones (cliente, sucursal o administración): el encabezado, las filas, las páginas y el estado que reemplaza a la tabla si hace falta.</summary>
public sealed class ListaDeDevolucionesVista : ModeloBaseDePagina
{
    public string Descripcion { get; set; } = string.Empty;

    public string RutaDeLaLista { get; set; } = string.Empty;

    public IReadOnlyList<DevolucionPantalla> Devoluciones { get; set; } = [];

    public int Total { get; set; }

    public int Pagina { get; set; } = 1;

    public int TotalDePaginas { get; set; } = 1;

    public EstadoVista? Estado { get; set; }

    public string? Aviso { get; set; }

    /// <summary>Un enlace a otra lista de devoluciones de la misma persona (en la administración, entre todas y los reportes); nulo si no hay.</summary>
    public EnlaceDeDevoluciones? OtraLista { get; set; }

    public string RutaDeLaPagina(int pagina) => pagina > 1 ? $"{RutaDeLaLista}?pagina={pagina}" : RutaDeLaLista;
}

/// <summary>El texto y la ruta de un enlace entre listas de devoluciones.</summary>
public sealed record EnlaceDeDevoluciones(string Texto, string Ruta);

/// <summary>Una acción sobre una devolución: su texto, su ruta y su icono.</summary>
public sealed record AccionDeDevolucionVista(string Texto, string Ruta, string Icono, bool Principal);

/// <summary>El detalle de una devolución con las acciones que su estado y quien la ve permiten.</summary>
public sealed class DetalleDeDevolucionVista : ModeloBaseDePagina
{
    public DevolucionPantalla? Devolucion { get; set; }

    public string RutaDeLaLista { get; set; } = string.Empty;

    public IReadOnlyList<AccionDeDevolucionVista> Acciones { get; set; } = [];

    public EstadoVista? Estado { get; set; }

    public string? Aviso { get; set; }
}

/// <summary>
/// La base de las pantallas y acciones de Devoluciones (T23, T24; DEV-13, DEV-28, DEV-29, DEV-31): lectura estricta de la consulta y del formulario (un campo que la acción no declara es validación y <b>no se repite</b>), <b>rechazo de cualquier adjunto</b>
/// (<c>multipart</c> o archivos), mapeo a vistas y traducción de un fallo a pantalla o a <c>application/problem+json</c>. <b>Presentación pura</b>: ninguna regla de negocio ni identificador del actor; quién puede qué lo decide la Aplicación. Cada respuesta es <c>no-store</c>.
/// </summary>
public abstract class ControladorDeDevoluciones(ContextoDePagina pagina, OpcionesDeErrores errores, AreaDeLaPagina area) : Controller
{
    protected const int TamanoDePagina = 20;

    private static readonly Dictionary<string, string> AvisoPorAccion = new(StringComparer.Ordinal)
    {
        ["solicitada"] = "Tu solicitud quedó registrada. Te avisaremos cuando cambie de estado.",
        ["aprobada"] = "La devolución quedó aprobada.",
        ["rechazada"] = "La devolución quedó rechazada.",
        ["recibida"] = "La recepción quedó confirmada y el reembolso está en curso.",
        ["reporte-aprobado"] = "El reporte quedó aprobado.",
        ["reporte-rechazado"] = "El reporte quedó rechazado y las descargas se reanudaron.",
    };

    protected ContextoDePagina Pagina => pagina;

    protected static ErrorDeAplicacion Invalido(string campo) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "formato-invalido")]);

    protected static string? Aviso(string? clave) => clave is not null && AvisoPorAccion.TryGetValue(clave, out var texto) ? texto : null;

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

    /// <summary>La consulta de una lista o de un detalle: solo las claves admitidas, sin repetidas, y de ellas <c>pagina</c> y <c>hecho</c> leídas con su validación.</summary>
    protected static ErrorDeAplicacion? LeerLaConsulta(HttpRequest solicitud, IReadOnlyCollection<string> admitidas, out int pagina, out string? hecho)
    {
        pagina = 1;
        hecho = null;
        return RechazarConsulta(solicitud, admitidas) ?? LeerPagina(solicitud, out pagina, out hecho);
    }

    /// <summary>La página (entero positivo, tope 10 000) de una lista; <c>hecho</c> es el aviso de la última acción y solo admite las claves conocidas.</summary>
    protected static ErrorDeAplicacion? LeerPagina(HttpRequest solicitud, out int pagina, out string? hecho)
    {
        pagina = 1;
        hecho = null;
        if (solicitud.Query["pagina"].ToString() is { Length: > 0 } p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out pagina) || pagina is < 1 or > 10_000))
        {
            return Invalido("pagina");
        }

        if (solicitud.Query["hecho"].ToString() is { Length: > 0 } h)
        {
            if (!AvisoPorAccion.ContainsKey(h))
            {
                return Invalido("hecho");
            }

            hecho = h;
        }

        return null;
    }

    /// <summary>
    /// El formulario de una acción: <b>ningún adjunto</b> (un <c>multipart</c> o un archivo es validación, DEV-29) y solo los campos que la acción declara (más el token antifalsificación). Devuelve un error si algo sobra; nunca repite lo recibido.
    /// </summary>
    protected async Task<(IFormCollection? Formulario, ErrorDeAplicacion? Error)> LeerFormulario(Func<string, bool> admitido)
    {
        if (Request.ContentType?.Contains("multipart/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (null, Invalido("adjuntos"));
        }

        if (Request.Query.Count > 0)
        {
            return (null, Invalido("consulta"));
        }

        if (!Request.HasFormContentType)
        {
            return Request.ContentLength is > 0 ? (null, Invalido("cuerpo")) : (FormCollection.Empty, null);
        }

        var formulario = await Request.ReadFormAsync();
        if (formulario.Files.Count > 0)
        {
            return (null, Invalido("adjuntos"));
        }

        foreach (var clave in formulario.Keys)
        {
            if (!string.Equals(clave, ProteccionContraFalsificacion.NombreDelCampo, StringComparison.Ordinal) && !admitido(clave))
            {
                return (null, Invalido("cuerpo"));
            }
        }

        return (formulario, null);
    }

    /// <summary>El valor único de un campo del formulario (nulo si falta o viene repetido).</summary>
    protected static string? Unico(IFormCollection formulario, string campo) => formulario.TryGetValue(campo, out var v) && v.Count == 1 ? v[0] : null;

    protected static DevolucionPantalla APantalla(DevolucionVista d, string rutaBase)
    {
        var corto = d.DevolucionId.ToString("N")[..8].ToUpperInvariant();
        return new DevolucionPantalla(
            d.DevolucionId,
            corto,
            $"{rutaBase}/{d.DevolucionId:D}",
            d.PedidoId.ToString("N")[..8].ToUpperInvariant(),
            d.Tipo == TipoDeLaDevolucion.Impreso ? "Libro impreso" : "Libro digital",
            d.Origen == OrigenDeLaDevolucion.Reporte,
            d.CodigoDeSucursalReceptora,
            FormatoPeruano.FechaYHora(d.CreadaEn),
            d.Motivo,
            d.Texto,
            d.Redactada,
            [.. d.Lineas.Select(ALinea)]);
    }

    private static LineaDeDevolucionPantalla ALinea(LineaDeDevolucionVista l)
    {
        var (texto, icono) = l.Estado switch
        {
            EstadoDeLaLineaDeDevolucion.Solicitada => ("Solicitada", "reloj"),
            EstadoDeLaLineaDeDevolucion.Aprobada => ("Aprobada", "exito"),
            EstadoDeLaLineaDeDevolucion.Rechazada => ("Rechazada", "candado"),
            EstadoDeLaLineaDeDevolucion.Recibida => ("Recibida", "sucursal"),
            _ => ("Reembolsada", "exito"),
        };
        return new LineaDeDevolucionPantalla(l.EdicionId, l.Cantidad, FormatoPeruano.Soles(l.Monto), texto, icono, l.ReembolsoPendiente);
    }

    protected async Task<ListaDeDevolucionesVista> ComoLista(PaginaDeDevoluciones paginaDeDevoluciones, string titulo, string descripcion, string rutaDeLaLista, string rutaDelDetalle, string? hecho, string vacio, CancellationToken cancelacion)
    {
        var vista = await Pagina.Completar(new ListaDeDevolucionesVista(), titulo, area, cancelacion);
        vista.Descripcion = descripcion;
        vista.RutaDeLaLista = rutaDeLaLista;
        vista.Aviso = Aviso(hecho);
        vista.Devoluciones = [.. paginaDeDevoluciones.Devoluciones.Select(d => APantalla(d, rutaDelDetalle))];
        vista.Total = paginaDeDevoluciones.Total;
        vista.Pagina = paginaDeDevoluciones.Pagina;
        vista.TotalDePaginas = Math.Max(1, (int)Math.Ceiling(paginaDeDevoluciones.Total / (double)Math.Max(1, paginaDeDevoluciones.Tamano)));
        if (paginaDeDevoluciones.Total == 0 || paginaDeDevoluciones.Devoluciones.Count == 0)
        {
            vista.Estado = EstadoVista.Vacio("Aún no hay devoluciones", vacio);
        }

        return vista;
    }

    /// <summary>Una acción de cambio exitosa: <c>303</c> al destino (o <c>HX-Redirect</c> con htmx).</summary>
    protected IActionResult Redirigir(string destino)
    {
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }

    /// <summary>El fallo: pantalla para un navegador (<c>Accept: text/html</c>) y <c>application/problem+json</c> para quien no lo pide. Nunca repite el motivo ni el texto.</summary>
    protected async Task<IActionResult> Falla(ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion)
    {
        if (!PantallasDeEnvios.PrefiereHtml(Request))
        {
            await ProblemasDeDevoluciones.Escribir(HttpContext, errores, error);
            return new EmptyResult();
        }

        var problema = ProblemasDeDevoluciones.Crear(HttpContext, errores, error);
        await DenegacionesHttp.Registrar(HttpContext, error.Categoria, error.Clave, problema.Code);
        var respuesta = PantallasDeDevoluciones.Traducir(problema, error, rutaDeReintento);
        if (respuesta.RedirigirA is { } destino)
        {
            return Request.EsHtmx() ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        var modelo = await Pagina.Completar(new PantallaDeEstadoVista { Estado = respuesta.Vista }, respuesta.Vista.Titulo, area, cancelacion);
        if (error.Categoria == CategoriaDeError.Limite)
        {
            Response.Headers["Retry-After"] = ProblemasDeEnvios.EsperaEnSegundos(error).ToString(CultureInfo.InvariantCulture);
        }

        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : respuesta.EstadoHttp;
        Response.Headers.CacheControl = "no-store";
        return View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}

/// <summary>Los fallos de Devoluciones en pantalla: el texto de cada código <c>ECO-DEV-…</c>, sin repetir nada de lo que escribió la persona.</summary>
public static class PantallasDeDevoluciones
{
    private sealed record Texto(string Titulo, string Mensaje);

    private static readonly Dictionary<string, Texto> PorCodigo = new(StringComparer.Ordinal)
    {
        ["ECO-NO-ENCONTRADO"] = new("No encontramos esa devolución", "Puede que ya no exista o que no sea tuya. Vuelve a la lista."),
        ["ECO-DEV-TIPOS-MEZCLADOS"] = new("No mezcles libros impresos y digitales", "Haz una solicitud para los impresos y otra para los digitales."),
        ["ECO-DEV-LINEA-NO-ENTREGADA"] = new("Ese libro aún no se entregó", "Solo puedes devolver libros que ya recibiste."),
        ["ECO-DEV-PLAZO-VENCIDO"] = new("El plazo para devolver ya venció", "Los libros impresos se devuelven dentro de los 15 días de la entrega; los digitales, dentro de los 14 días de la compra, o 30 si reportas un archivo defectuoso."),
        ["ECO-DEV-CANTIDAD-EXCEDIDA"] = new("La cantidad supera lo que compraste", "Revisa cuántas unidades ya devolviste de este libro."),
        ["ECO-DEV-LINEA-NO-DEVOLVIBLE"] = new("Este libro ya no se puede devolver", "Ya fue reembolsado, o su acceso digital ya no está vigente."),
        ["ECO-DEV-POLITICA-DIGITAL"] = new("Este libro digital no admite reembolso", "Si ya descargaste el libro, no corresponde reembolso. Si el archivo está defectuoso, repórtalo desde tu biblioteca."),
        ["ECO-DEV-REPORTE-NO-ADMITIDO"] = new("No se puede reportar este archivo", "El acceso está revocado o suspendido, o ya hay una solicitud abierta."),
        ["ECO-DEV-ESTADO-INVALIDO"] = new("La devolución ya cambió de estado", "Otra persona la actualizó. Vuelve a la lista para ver su estado actual."),
        ["ECO-DEV-REINGRESO-RECHAZADO"] = new("El Inventario rechazó el reingreso", "La devolución sigue sin recibirse. Avisa a soporte con el código que ves abajo."),
        ["ECO-CUENTA-EN-CANCELACION"] = new("Tu cuenta se está cancelando", "No se pueden registrar devoluciones mientras la cancelación está en curso."),
        ["ECO-CONFLICTO"] = new("Otra operación está en curso", "Espera unos segundos y vuelve a intentarlo."),
    };

    public static RespuestaDePantalla Traducir(ProblemaDto problema, ErrorDeAplicacion error, string rutaDeReintento)
    {
        ArgumentNullException.ThrowIfNull(problema);
        ArgumentNullException.ThrowIfNull(error);
        if (PorCodigo.TryGetValue(problema.Code, out var texto))
        {
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = texto.Titulo,
                    Mensaje = texto.Mensaje,
                    RutaDeReintento = rutaDeReintento,
                    CodigoDeSoporte = problema.Code,
                    IdentificadorDeSeguimiento = problema.TraceId,
                },
                problema.Status,
                null,
                true,
                []);
        }

        var respuesta = TraductorDeErrores.Traducir(problema, rutaDeReintento: rutaDeReintento, parteAfectada: "No podemos operar las devoluciones ahora. No se cambió nada.");
        return error.Categoria == CategoriaDeError.Limite
            ? respuesta with { Vista = respuesta.Vista with { EsperaSugeridaSegundos = ProblemasDeEnvios.EsperaEnSegundos(error) } }
            : respuesta;
    }
}
