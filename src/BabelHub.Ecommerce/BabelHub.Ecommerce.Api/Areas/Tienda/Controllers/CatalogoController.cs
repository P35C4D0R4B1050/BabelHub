using System.Globalization;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Catalogo;
using Microsoft.AspNetCore.Mvc;
using EstadoDeDisponibilidadDelCatalogo = BabelHub.Ecommerce.Application.Catalogo.EstadoDeDisponibilidad;
using EstadoDeDisponibilidadWeb = BabelHub.Ecommerce.Api.Web.Estados.EstadoDeDisponibilidad;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Los filtros de la búsqueda tal como la persona los escribió (se vuelven a pintar en el formulario).</summary>
public sealed record FiltrosDelCatalogo(string? Texto, string? Autor, string? Editorial, string? Categoria, string? Formato, string? PrecioMinimo, string? PrecioMaximo, bool SoloDisponibles)
{
    public bool HayFiltros => Texto is not null || Autor is not null || Editorial is not null || Categoria is not null || Formato is not null || PrecioMinimo is not null || PrecioMaximo is not null || SoloDisponibles;
}

/// <summary>Una edición de una tarjeta o de la ficha, ya lista para pintar: precio con formato peruano y la disponibilidad con texto, icono y color.</summary>
public sealed record EdicionDeLaVista(Guid EdicionId, bool EsDigital, string Isbn, decimal Precio, string PrecioTexto, DisponibilidadVista Disponibilidad, string? Aviso, IReadOnlyList<FilaDeSucursal> Sucursales);

/// <summary>El stock vendible de una sucursal (fuente) con su estado en texto: nunca solo con color.</summary>
public sealed record FilaDeSucursal(string Codigo, string Nombre, int Vendible, string Estado, string Icono, bool EsLaTuya);

public sealed record TarjetaDeObra(Guid ObraId, string Titulo, string Autor, string Categoria, string PortadaHuella, string DesdeTexto, IReadOnlyList<EdicionDeLaVista> Ediciones);

/// <summary>La página del catálogo: filtros, resultados y, en su lugar, el estado que corresponda.</summary>
public sealed class CatalogoVista : ModeloBaseDePagina
{
    public FiltrosDelCatalogo Filtros { get; set; } = new(null, null, null, null, null, null, null, false);

    public IReadOnlyList<TarjetaDeObra> Tarjetas { get; set; } = [];

    public int Total { get; set; }

    public int Pagina { get; set; } = 1;

    public int TotalDePaginas { get; set; } = 1;

    /// <summary>Un estado que reemplaza a los resultados (vacío, error, límite, servicio no disponible).</summary>
    public EstadoVista? Estado { get; set; }

    /// <summary>Un aviso sobre los filtros escritos (formato inválido), sin trazas.</summary>
    public string? AvisoDeFiltros { get; set; }

    /// <summary>Algún dato de disponibilidad es de hace más de 30 segundos o no se pudo confirmar (AC-CAT-09).</summary>
    public bool DisponibilidadDudosa { get; set; }

    public string RutaDeLaPagina(int pagina) => "/catalogo" + ConsultaDe(pagina);

    internal string ConsultaDe(int pagina)
    {
        var partes = new List<string>();
        void Agregar(string nombre, string? valor)
        {
            if (!string.IsNullOrEmpty(valor))
            {
                partes.Add($"{nombre}={Uri.EscapeDataString(valor)}");
            }
        }

        Agregar("q", Filtros.Texto);
        Agregar("autor", Filtros.Autor);
        Agregar("editorial", Filtros.Editorial);
        Agregar("categoria", Filtros.Categoria);
        Agregar("formato", Filtros.Formato);
        Agregar("precioMin", Filtros.PrecioMinimo);
        Agregar("precioMax", Filtros.PrecioMaximo);
        Agregar("disponible", Filtros.SoloDisponibles ? "1" : null);
        if (pagina > 1)
        {
            partes.Add($"pagina={pagina}");
        }

        return partes.Count == 0 ? string.Empty : "?" + string.Join('&', partes);
    }
}

/// <summary>La ficha de una obra: sus ediciones, el selector y la disponibilidad.</summary>
public sealed class FichaVista : ModeloBaseDePagina
{
    public Guid ObraId { get; set; }

    public string Autor { get; set; } = string.Empty;

    public string Editorial { get; set; } = string.Empty;

    public string Categoria { get; set; } = string.Empty;

    public string PortadaHuella { get; set; } = string.Empty;

    public string ObraTitulo { get; set; } = string.Empty;

    public EdicionDeLaVista? Impresa { get; set; }

    public EdicionDeLaVista? Digital { get; set; }

    /// <summary>La edición que se muestra: <c>impresa</c> o <c>digital</c>.</summary>
    public string EdicionActiva { get; set; } = "impresa";

    public bool DisponibilidadDudosa { get; set; }
}

/// <summary>
/// El catálogo público (WEB-17 y CAT-09, CAT-10): <c>GET /catalogo</c> (búsqueda con filtros) y <c>GET /catalogo/obras/{id}</c> (ficha). Presentación pura: lee los filtros con esquema estricto, llama a <see cref="IConsultaDeCatalogo"/> y pinta lo que devuelve, con la disponibilidad como
/// texto e icono y los estados estándar de pantalla. Funciona sin JavaScript (un formulario <c>GET</c>); con htmx solo reemplaza los resultados. Sin carrito todavía: la ficha no ofrece comprar.
/// </summary>
[Area("Tienda")]
public sealed class CatalogoController(IConsultaDeCatalogo catalogo, ContextoDePagina pagina, OpcionesDeErrores opciones) : Controller
{
    private const int TamanoDePagina = 12;

    private static readonly string[] Parametros = ["q", "autor", "editorial", "categoria", "formato", "precioMin", "precioMax", "disponible", "pagina"];

    [HttpGet("/catalogo")]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        var vista = await pagina.Completar(new CatalogoVista(), "Catálogo", cancelacion: cancelacion);
        var (busqueda, filtros, aviso) = LeerLosFiltros(Request.Query);
        vista.Filtros = filtros;
        vista.Pagina = busqueda?.Pagina ?? 1;
        Response.Headers.CacheControl = "no-store";
        if (busqueda is null)
        {
            vista.AvisoDeFiltros = aviso;
            vista.Estado = EstadoVista.Vacio("Revisa los filtros", aviso ?? "Algún filtro no tiene la forma esperada.", "Limpiar filtros", "/catalogo");
            return Respuesta(vista, StatusCodes.Status400BadRequest);
        }

        var resultado = await catalogo.BuscarObras(busqueda, cancelacion);
        if (!resultado.EsExito)
        {
            var problema = ProblemasHttp.Crear(HttpContext, opciones, resultado.Error!);
            var traduccion = TraductorDeErrores.Traducir(problema, rutaDeReintento: Request.Path + Request.QueryString, parteAfectada: "No podemos mostrar el catálogo ahora.");
            vista.Estado = resultado.Error!.Clave == "busqueda-demasiado-amplia"
                ? EstadoVista.Vacio("Tu búsqueda es muy amplia", "Agrega un filtro (formato, categoría o precio) o escribe parte del título para acotarla.", "Limpiar filtros", "/catalogo")
                : traduccion.Vista;
            return Respuesta(vista, resultado.Error.Clave == "busqueda-demasiado-amplia" ? StatusCodes.Status422UnprocessableEntity : traduccion.EstadoHttp);
        }

        var preferida = vista.SucursalPreferida;
        var pagina_ = resultado.Valor;
        vista.Tarjetas = [.. pagina_.Obras.Select(o => ATarjeta(o, preferida))];
        vista.Total = pagina_.Total;
        vista.Pagina = pagina_.Pagina;
        vista.TotalDePaginas = Math.Max(1, (int)Math.Ceiling(pagina_.Total / (double)Math.Max(1, pagina_.Tamano)));
        vista.DisponibilidadDudosa = pagina_.Obras.SelectMany(o => o.Ediciones).Any(e => e.Disponibilidad.Frescura != FrescuraDeLaDisponibilidad.Reciente && e.Disponibilidad.Estado != EstadoDeDisponibilidadDelCatalogo.DisponibleAlInstante && e.Disponibilidad.Estado != EstadoDeDisponibilidadDelCatalogo.NoSeOfrece);
        if (pagina_.Total == 0)
        {
            vista.Estado = EstadoVista.Vacio("No encontramos libros", "Prueba con otra búsqueda o quita algunos filtros.", filtros.HayFiltros ? "Limpiar filtros" : null, filtros.HayFiltros ? "/catalogo" : null);
        }

        return Respuesta(vista, StatusCodes.Status200OK);
    }

    [HttpGet("/catalogo/obras/{id:guid}")]
    public async Task<IActionResult> Obra(Guid id, [FromQuery] string? edicion, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var resultado = await catalogo.ObtenerObra(id, cancelacion);
        if (!resultado.EsExito)
        {
            var problema = ProblemasHttp.Crear(HttpContext, opciones, resultado.Error!);
            var traduccion = TraductorDeErrores.Traducir(problema, rutaDeReintento: Request.Path, parteAfectada: "No podemos mostrar este libro ahora.");
            var estado = resultado.Error!.Categoria == BabelHub.Ecommerce.Application.Plataforma.CategoriaDeError.NoEncontrado
                ? EstadoVista.Vacio("No encontramos este libro", "Puede que ya no esté en el catálogo.", "Ver el catálogo", "/catalogo")
                : traduccion.Vista;
            var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = estado }, "Libro", cancelacion: cancelacion);
            Response.StatusCode = resultado.Error.Categoria == BabelHub.Ecommerce.Application.Plataforma.CategoriaDeError.NoEncontrado ? StatusCodes.Status404NotFound : traduccion.EstadoHttp;
            return View("~/Views/Estados/Pantalla.cshtml", modelo);
        }

        var obra = resultado.Valor;
        var vista = await pagina.Completar(new FichaVista(), obra.Titulo, cancelacion: cancelacion);
        vista.ObraId = obra.ObraId;
        vista.ObraTitulo = obra.Titulo;
        vista.Autor = obra.Autor;
        vista.Editorial = obra.Editorial;
        vista.Categoria = obra.Categoria;
        vista.PortadaHuella = obra.PortadaHuella;
        vista.Impresa = obra.Ediciones.Where(e => e.Formato == FormatoDeEdicion.Impresa).Select(e => AEdicion(e, vista.SucursalPreferida)).FirstOrDefault();
        vista.Digital = obra.Ediciones.Where(e => e.Formato == FormatoDeEdicion.Digital).Select(e => AEdicion(e, vista.SucursalPreferida)).FirstOrDefault();
        vista.EdicionActiva = edicion switch
        {
            "digital" when vista.Digital is not null => "digital",
            "impresa" when vista.Impresa is not null => "impresa",
            _ => vista.Impresa is not null ? "impresa" : "digital",
        };
        vista.DisponibilidadDudosa = obra.Ediciones.Any(e => e.Disponibilidad.Frescura != FrescuraDeLaDisponibilidad.Reciente && e.Formato == FormatoDeEdicion.Impresa);
        return View("Obra", vista);
    }

    private IActionResult Respuesta(CatalogoVista vista, int estado)
    {
        Response.StatusCode = estado;
        return Request.Headers.ContainsKey("HX-Request") ? PartialView("_Resultados", vista) : View("Index", vista);
    }

    // ---------- Los filtros ----------

    /// <summary>Lee la consulta con esquema estricto: un parámetro repetido o un valor con otra forma es un aviso (nunca se devuelve el valor recibido) y no se llama al catálogo.</summary>
    internal static (BusquedaDeObras? Busqueda, FiltrosDelCatalogo Filtros, string? Aviso) LeerLosFiltros(IQueryCollection consulta)
    {
        foreach (var nombre in Parametros)
        {
            if (consulta[nombre].Count > 1)
            {
                return (null, new FiltrosDelCatalogo(null, null, null, null, null, null, null, false), "Algún filtro está repetido.");
            }
        }

        string? Texto(string nombre) => consulta[nombre].ToString() is { Length: > 0 } valor && !string.IsNullOrWhiteSpace(valor) ? valor.Trim() : null;
        var filtros = new FiltrosDelCatalogo(Texto("q"), Texto("autor"), Texto("editorial"), Texto("categoria"), Texto("formato"), Texto("precioMin"), Texto("precioMax"), Texto("disponible") == "1");

        if (filtros.Formato is not null and not ("impresa" or "digital"))
        {
            return (null, filtros, "El formato debe ser impreso o digital.");
        }

        if (!TryPrecio(filtros.PrecioMinimo, out var minimo) || !TryPrecio(filtros.PrecioMaximo, out var maximo))
        {
            return (null, filtros, "El precio debe ser un número en soles, por ejemplo 100 o 49,90.");
        }

        if (minimo is { } a && maximo is { } b && a > b)
        {
            return (null, filtros, "El precio mínimo no puede ser mayor que el máximo.");
        }

        var paginaPedida = 1;
        if (Texto("pagina") is { } texto && (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out paginaPedida) || paginaPedida < 1))
        {
            return (null, filtros, "La página debe ser un número positivo.");
        }

        FormatoDeEdicion? formato = filtros.Formato switch { "impresa" => FormatoDeEdicion.Impresa, "digital" => FormatoDeEdicion.Digital, _ => null };
        return (new BusquedaDeObras(filtros.Texto, filtros.Autor, filtros.Editorial, filtros.Categoria, formato, minimo, maximo, filtros.SoloDisponibles, paginaPedida, TamanoDePagina), filtros, null);
    }

    private static bool TryPrecio(string? texto, out decimal? valor)
    {
        valor = null;
        if (texto is null)
        {
            return true;
        }

        if (decimal.TryParse(texto.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numero) && numero >= 0 && numero <= 9_999_999.99m)
        {
            valor = numero;
            return true;
        }

        return false;
    }

    // ---------- Del contrato a la vista ----------

    private static TarjetaDeObra ATarjeta(ResumenDeObra obra, string? sucursal)
    {
        var ediciones = obra.Ediciones.Select(e => AEdicion(e, sucursal)).ToList();
        var desde = ediciones.Count == 0 ? string.Empty : "Desde " + FormatoPeruano.Soles(ediciones.Min(e => e.Precio));
        return new TarjetaDeObra(obra.ObraId, obra.Titulo, obra.Autor, obra.Categoria, obra.PortadaHuella, desde, ediciones);
    }

    internal static EdicionDeLaVista AEdicion(EdicionDeObra edicion, string? sucursal)
    {
        var d = edicion.Disponibilidad;
        var esDigital = edicion.Formato == FormatoDeEdicion.Digital;
        var filas = d.PorFuente.Select(f => new FilaDeSucursal(f.CodigoDeFuente, f.NombreDeFuente, f.Vendible, EstadoDeStock(f.Vendible), IconoDeStock(f.Vendible), string.Equals(f.CodigoDeFuente, sucursal, StringComparison.Ordinal))).ToList();
        string? aviso = null;
        DisponibilidadVista vista;
        switch (d.Estado)
        {
            case EstadoDeDisponibilidadDelCatalogo.DisponibleAlInstante:
                vista = DisponibilidadVista.DisponibleAlInstante();
                break;
            case EstadoDeDisponibilidadDelCatalogo.NoSeOfrece:
                vista = new DisponibilidadVista(EstadoDeDisponibilidadWeb.NoSePuedeConfirmar, "No se ofrece", "candado", "badge-neutral");
                aviso = MotivoDeNoOfrecer(d.Motivo);
                break;
            case EstadoDeDisponibilidadDelCatalogo.NoSePuedeConfirmar:
                vista = DisponibilidadVista.NoSePuedeConfirmar();
                aviso = "No podemos confirmar el stock ahora. Intenta de nuevo en unos minutos.";
                break;
            case EstadoDeDisponibilidadDelCatalogo.Agotada:
                vista = DisponibilidadVista.Agotado();
                break;
            default:
                var tuya = filas.FirstOrDefault(f => f.EsLaTuya && f.Vendible > 0);
                var otras = filas.Count(f => !f.EsLaTuya && f.Vendible > 0);
                vista = tuya is not null ? DisponibilidadVista.EnTuSucursal() : DisponibilidadVista.EnOtrasSucursales(Math.Max(1, otras));
                break;
        }

        if (d.Frescura == FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada)
        {
            aviso = "La disponibilidad puede estar desactualizada (último dato de hace menos de 10 minutos).";
        }

        return new EdicionDeLaVista(edicion.EdicionId, esDigital, edicion.Isbn, edicion.Precio, FormatoPeruano.Soles(edicion.Precio), vista, aviso, filas);
    }

    private static string EstadoDeStock(int vendible) => vendible <= 0 ? "Sin stock" : vendible <= 2 ? "Pocas unidades" : "Disponible";

    private static string IconoDeStock(int vendible) => vendible <= 0 ? "agotado" : vendible <= 2 ? "aviso" : "exito";

    private static string MotivoDeNoOfrecer(MotivoDeNoDisponibleDeEdicion? motivo) => motivo switch
    {
        MotivoDeNoDisponibleDeEdicion.LicenciaVencida => "La licencia de esta edición digital venció y ya no se vende.",
        MotivoDeNoDisponibleDeEdicion.SinPeru => "La licencia de esta edición digital no incluye Perú.",
        MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta => "Esta edición digital se retiró de la venta.",
        MotivoDeNoDisponibleDeEdicion.DadaDeBaja => "Esta edición ya no está en el catálogo.",
        _ => "Esta edición digital no está disponible por ahora.",
    };
}
