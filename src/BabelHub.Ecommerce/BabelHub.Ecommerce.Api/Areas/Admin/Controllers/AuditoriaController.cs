using System.Globalization;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Auditoria;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>Lo que pinta la auditoría unificada: la página ya presentada (hora de Lima, valores como texto), el estado estándar y los filtros que se pidieron, para repetirlos en el formulario y en el enlace «Más».</summary>
public sealed class AuditoriaVista : ModeloBaseDePagina
{
    public PaginaDeAuditoriaVista Pagina { get; set; } = new([], null, null, EstadoVista.Vacio(PresentadorDeAuditoria.TituloDeAuditoriaVacia, PresentadorDeAuditoria.MensajeDeAuditoriaVacia));

    public EstadoVista Estado { get; set; } = EstadoVista.Exito("Auditoría");

    public IReadOnlyDictionary<string, string> Filtros { get; set; } = new Dictionary<string, string>();

    public string EnlaceDeLaSiguiente => Pagina.SiguienteCursor is null ? string.Empty : "/administracion/auditoria?" + string.Join('&', Filtros.Where(f => f.Key != "cursor").Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}").Append($"cursor={Uri.EscapeDataString(Pagina.SiguienteCursor)}"));
}

/// <summary>La auditoría unificada (T31; WEB-30, AC-WEB-25): <c>GET /administracion/auditoria</c> con los filtros de <see cref="IConsultasDeAuditoria"/> (acción, usuario, origen, rango de fechas en hora de Lima y texto) y su cursor, <b>solo para la administración central</b> (la decide la Aplicación; otro rol ve «sin permiso»). Presentación pura: los valores se pintan como texto (nunca como HTML) y, si el Inventario no respondió, se muestra el aviso con lo del E-commerce.</summary>
[Area("Admin")]
public sealed class AuditoriaController(IConsultasDeAuditoria auditoria, ContextoDePagina pagina) : Controller
{
    public const string Ruta = "/administracion/auditoria";

    private static readonly string[] Permitidos = ["accion", "usuario", "origen", "desde", "hasta", "texto", "cursor"];
    private static readonly TimeSpan Lima = TimeSpan.FromHours(-5);

    [HttpGet(Ruta)]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, Permitidos) is { } consulta)
        {
            return await Pintar(new AuditoriaVista { Estado = EstadoVista.ErrorGenerico(Ruta) }, StatusCodes.Status400BadRequest, cancelacion);
        }

        var q = Request.Query;
        if (!TryFiltro(q, out var filtro, out var cursor, out var echo))
        {
            return await Pintar(new AuditoriaVista { Estado = EstadoVista.ErrorGenerico(Ruta) with { Mensaje = "Algún filtro no es válido. Revisa las fechas y el usuario." } }, StatusCodes.Status400BadRequest, cancelacion);
        }

        Resultado<PaginaDeRegistros> resultado;
        try
        {
            resultado = await auditoria.Consultar(new ConsultaDeAuditoria(filtro, cursor), cancelacion);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            resultado = Resultado<PaginaDeRegistros>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }

        if (!resultado.EsExito)
        {
            var estado = PresentadorDeAuditoria.EstadoDelFallo(resultado.Error!, Request.Path + Request.QueryString);
            return await Pintar(new AuditoriaVista { Estado = estado, Filtros = echo }, EstadoHttp(resultado.Error!), cancelacion);
        }

        var presentada = PresentadorDeAuditoria.Presentar(resultado.Valor);
        return await Pintar(new AuditoriaVista { Pagina = presentada, Estado = presentada.Estado, Filtros = echo }, StatusCodes.Status200OK, cancelacion);
    }

    private static int EstadoHttp(ErrorDeAplicacion error) => error.Categoria switch
    {
        CategoriaDeError.Prohibido => StatusCodes.Status403Forbidden,
        CategoriaDeError.NoAutenticado => StatusCodes.Status401Unauthorized,
        CategoriaDeError.NoDisponible => StatusCodes.Status503ServiceUnavailable,
        CategoriaDeError.Validacion => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };

    private async Task<IActionResult> Pintar(AuditoriaVista vista, int estado, CancellationToken cancelacion)
    {
        await pagina.Completar(vista, "Auditoría", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Index", vista);
    }

    /// <summary>Los filtros con su forma estricta (un valor repetido o con otra forma es inválido, no se ignora): fechas <c>aaaa-mm-dd</c> en hora de Lima, el usuario como identificador y textos acotados.</summary>
    private static bool TryFiltro(IQueryCollection q, out FiltroDeAuditoria filtro, out string? cursor, out Dictionary<string, string> echo)
    {
        filtro = new FiltroDeAuditoria();
        cursor = null;
        var eco = new Dictionary<string, string>();
        echo = eco;
        foreach (var clave in Permitidos)
        {
            if (q[clave].Count > 1)
            {
                return false;
            }
        }

        string? Texto(string clave, int maximo)
        {
            var valor = q[clave].ToString();
            if (valor.Length == 0)
            {
                return null;
            }

            if (valor.Length > maximo || valor.Any(char.IsControl))
            {
                throw new FormatException(clave);
            }

            eco[clave] = valor;
            return valor;
        }

        try
        {
            var accion = Texto("accion", 64);
            var origen = Texto("origen", 64);
            var texto = Texto("texto", 100);
            Guid? usuario = null;
            if (Texto("usuario", 36) is { } textoDelUsuario)
            {
                usuario = Guid.TryParseExact(textoDelUsuario, "D", out var id) ? id : throw new FormatException("usuario");
            }

            DateTimeOffset? desde = null;
            DateTimeOffset? hasta = null;
            if (Texto("desde", 10) is { } d)
            {
                desde = DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia) ? new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), Lima) : throw new FormatException("desde");
            }

            if (Texto("hasta", 10) is { } h)
            {
                hasta = DateOnly.TryParseExact(h, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia) ? new DateTimeOffset(dia.AddDays(1).ToDateTime(TimeOnly.MinValue), Lima) : throw new FormatException("hasta");
            }

            if (desde is not null && hasta is not null && desde >= hasta)
            {
                return false;
            }

            cursor = Texto("cursor", PaginaDeRegistros.MaximoDeCaracteresDelCursor);
            filtro = new FiltroDeAuditoria { Accion = accion, Origen = origen, Texto = texto, UsuarioId = usuario, Desde = desde, Hasta = hasta };
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Lo que pinta la pantalla de alertas de seguridad: la página presentada, su estado y, tras marcar una, el aviso de lista cerrada.</summary>
public sealed class AlertasVista : ModeloBaseDePagina
{
    public PaginaDeAlertasVista Pagina { get; set; } = new([], null, null, EstadoVista.Vacio(PresentadorDeAuditoria.TituloDeAlertasVacias, PresentadorDeAuditoria.MensajeDeAlertasVacias));

    public EstadoVista Estado { get; set; } = EstadoVista.Exito("Alertas de seguridad");

    public string? Mensaje { get; set; }

    public bool EsError { get; set; }
}

/// <summary>
/// Las alertas de seguridad (T31; WEB-33, AC-WEB-27): <c>GET /administracion/alertas</c> (solo las de la clase «seguridad», con su estado) y <c>POST /administracion/alertas/revisada</c>, que marca como revisadas las ocurrencias <b>hasta el contador que la persona vio</b>
/// (<c>contadorVisto</c> viaja en el formulario: una ocurrencia nueva no se da por revisada). Solo la administración central (lo decide la Aplicación). Los motivos son textos fijos del catálogo, nunca el de una excepción.
/// </summary>
[Area("Admin")]
public sealed class AlertasController(IConsultaDeAlertas alertas, ContextoDePagina pagina) : Controller
{
    public const string Ruta = "/administracion/alertas";

    [HttpGet(Ruta)]
    public async Task<IActionResult> Index([FromQuery] string? cursor, [FromQuery] string? aviso, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["cursor", "aviso"]) is not null || cursor?.Length > PaginaDeRegistros.MaximoDeCaracteresDelCursor)
        {
            return await Pintar(new AlertasVista { Estado = EstadoVista.ErrorGenerico(Ruta) }, StatusCodes.Status400BadRequest, cancelacion);
        }

        Resultado<PaginaDeAlertas> resultado;
        try
        {
            resultado = await alertas.Listar(string.IsNullOrEmpty(cursor) ? null : cursor, cancelacion);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            resultado = Resultado<PaginaDeAlertas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }

        if (!resultado.EsExito)
        {
            return await Pintar(new AlertasVista { Estado = PresentadorDeAuditoria.EstadoDelFallo(resultado.Error!, Ruta) }, Estado(resultado.Error!), cancelacion);
        }

        var presentada = PresentadorDeAuditoria.Presentar(resultado.Valor);
        var vista = new AlertasVista { Pagina = presentada, Estado = presentada.Estado };
        if (aviso == "revisada")
        {
            vista.Mensaje = "Marcamos la alerta como revisada.";
        }

        return await Pintar(vista, StatusCodes.Status200OK, cancelacion);
    }

    [HttpPost(Ruta + "/revisada")]
    public async Task<IActionResult> Revisada(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, ["alertaId", "contadorVisto"], cancelacion);
        if (campos is null || !campos.TryGetValue("alertaId", out var id) || id.Length is 0 or > 100 || id.Any(char.IsControl)
            || !campos.TryGetValue("contadorVisto", out var texto) || texto.Length is 0 or > 9 || !texto.All(char.IsAsciiDigit) || !int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var contador))
        {
            _ = error;
            return await Pintar(new AlertasVista { Estado = EstadoVista.ErrorGenerico(Ruta) }, StatusCodes.Status400BadRequest, cancelacion);
        }

        Resultado<AlertaConsultada> resultado;
        try
        {
            resultado = await alertas.MarcarRevisada(id, contador, cancelacion);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            resultado = Resultado<AlertaConsultada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }

        if (!resultado.EsExito)
        {
            return await Pintar(new AlertasVista { Estado = PresentadorDeAuditoria.EstadoDelFallo(resultado.Error!, Ruta), Mensaje = "No pudimos marcar la alerta como revisada.", EsError = true }, Estado(resultado.Error!), cancelacion);
        }

        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(Ruta + "?aviso=revisada");
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = Ruta + "?aviso=revisada";
        return new EmptyResult();
    }

    private static int Estado(ErrorDeAplicacion error) => error.Categoria switch
    {
        CategoriaDeError.Prohibido => StatusCodes.Status403Forbidden,
        CategoriaDeError.NoAutenticado => StatusCodes.Status401Unauthorized,
        CategoriaDeError.NoDisponible => StatusCodes.Status503ServiceUnavailable,
        CategoriaDeError.NoEncontrado => StatusCodes.Status404NotFound,
        CategoriaDeError.Validacion => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };

    private async Task<IActionResult> Pintar(AlertasVista vista, int estado, CancellationToken cancelacion)
    {
        await pagina.Completar(vista, "Alertas de seguridad", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Index", vista);
    }
}
