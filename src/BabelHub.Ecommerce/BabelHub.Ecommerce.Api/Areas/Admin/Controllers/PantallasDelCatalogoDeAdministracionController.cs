using System.Globalization;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;
using EstadoDelCatalogo = BabelHub.Ecommerce.Application.Catalogo.EstadoDeDisponibilidad;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>Una edición de la lista o de la página de una obra, lista para pintar: precio, estado con texto, licencia y los avisos de la administración.</summary>
public sealed record EdicionDeAdministracionVista(
    Guid EdicionId,
    bool EsDigital,
    string Isbn,
    string IsbnIngresado,
    decimal Precio,
    string PrecioTexto,
    string Estado,
    string? MotivoDeRetiro,
    string? MotivoDeNoDisponible,
    bool Activa,
    bool RegistroPendienteEnElInventario,
    bool AgotadaEnLaRed,
    string? LicenciaTexto,
    string? LicenciaTipo,
    string? Titular,
    string? Territorios,
    string? VigenteHasta,
    string? FormatoDeLicencia,
    string? ArchivoTexto);

/// <summary>Los campos de la licencia de una edición digital con su prefijo de nombre, para pintar el formulario (alta y edición).</summary>
public sealed record CamposDeLicenciaVista(string Prefijo, string Tipo, string Formato, string? Titular, string? Territorios, string? Vigencia);

public sealed record ObraDeAdministracionVista(Guid ObraId, string Titulo, string Autor, string Editorial, string Categoria, string PortadaHuella, long Version, bool DadaDeBaja, EdicionDeAdministracionVista? Impresa, EdicionDeAdministracionVista? Digital);

/// <summary>La lista de obras de la administración: búsqueda, páginas y el estado que reemplaza a la tabla si hace falta.</summary>
public sealed class ListaDeObrasVista : ModeloBaseDePagina
{
    public string? Texto { get; set; }

    public bool IncluirDadasDeBaja { get; set; }

    public IReadOnlyList<ObraDeAdministracionVista> Obras { get; set; } = [];

    public int Total { get; set; }

    public int Pagina { get; set; } = 1;

    public int TotalDePaginas { get; set; } = 1;

    public EstadoVista? Estado { get; set; }

    public string? Aviso { get; set; }

    public string RutaDeLaPagina(int pagina) => "/administracion/obras?" + string.Join('&', new[] { Texto is { Length: > 0 } t ? "q=" + Uri.EscapeDataString(t) : null, IncluirDadasDeBaja ? "bajas=1" : null, pagina > 1 ? $"pagina={pagina}" : null }.Where(p => p is not null));
}

/// <summary>El formulario de una obra nueva.</summary>
public sealed class NuevaObraVista : ModeloBaseDePagina;

/// <summary>La página de una obra: sus datos, sus ediciones con sus acciones y los formularios de edición.</summary>
public sealed class ObraDeAdministracionPagina : ModeloBaseDePagina
{
    public ObraDeAdministracionVista Obra { get; set; } = null!;
}

public sealed record FilaDeIngresoVista(Guid? IngresoSolicitadoId, string Cantidad, string? Referencia, string Fecha, string Usuario, string Estado, string EstadoIcono, bool EsPendiente, bool Viejo);

/// <summary>La pantalla de ingresos de una edición impresa: el formulario con su token firmado y el historial con sus acciones.</summary>
public sealed class IngresosDeLaEdicionVista : ModeloBaseDePagina
{
    public Guid EdicionId { get; set; }

    public Guid ObraId { get; set; }

    public string TituloDeLaObra { get; set; } = string.Empty;

    public string Isbn { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string TokenVenceEn { get; set; } = string.Empty;

    public bool AgotadaEnLaRed { get; set; }

    public int? VendibleEnLaRed { get; set; }

    public bool InventarioNoRespondio { get; set; }

    public bool HayPendiente { get; set; }

    public IReadOnlyList<FilaDeIngresoVista> Ingresos { get; set; } = [];
}

/// <summary>
/// Las pantallas de la administración del catálogo (T25, T26; AC-CAT-01, AC-CAT-03, AC-CAT-05, AC-CAT-12, AC-CAT-13, AC-CAT-16, AC-CAT-17, AC-CAT-18): lista de obras, obra nueva, página de una obra con sus ediciones y la pantalla de ingresos de una impresa. Presentación pura: lee con los contratos
/// públicos <see cref="IAdministracionDelCatalogo"/>, <see cref="IIngresosDeMercaderia"/>, <see cref="IConsultaDeCatalogo"/> y <see cref="IDisponibilidadDeEdiciones"/>; las escrituras las hacen las acciones JSON de <see cref="AdministracionDelCatalogoController"/>
/// desde <c>admin-catalogo.js</c> (subida previa de archivos, cuerpo JSON y errores de campo). Quién puede qué lo decide la Aplicación con el actor verificado, nunca esta clase.
/// </summary>
[Area("Admin")]
public sealed class PantallasDelCatalogoDeAdministracionController(
    IAdministracionDelCatalogo administracion,
    IIngresosDeMercaderia ingresos,
    IConsultaDeCatalogo catalogo,
    IDisponibilidadDeEdiciones disponibilidad,
    ContextoDePagina pagina,
    OpcionesDeErrores opciones,
    IAdministracionDeUsuarios? usuarios = null) : Controller
{
    private const int TamanoDePagina = 20;

    private const string Vistas = "~/Areas/Admin/Views/Catalogo/";

    // ---------- Lista ----------

    [HttpGet("/administracion/obras")]
    public async Task<IActionResult> Lista(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var vista = await pagina.Completar(new ListaDeObrasVista(), "Catálogo nacional", AreaDeLaPagina.Admin, cancelacion);
        if (Request.Query["q"].Count > 1 || Request.Query["bajas"].Count > 1 || Request.Query["pagina"].Count > 1)
        {
            vista.Aviso = "Algún filtro está repetido.";
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(Vistas + "Lista.cshtml", vista);
        }

        vista.Texto = Request.Query["q"].ToString() is { Length: > 0 } texto && !string.IsNullOrWhiteSpace(texto) ? texto.Trim() : null;
        vista.IncluirDadasDeBaja = Request.Query["bajas"].ToString() == "1";
        var paginaPedida = 1;
        if (Request.Query["pagina"].ToString() is { Length: > 0 } p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out paginaPedida) || paginaPedida < 1))
        {
            vista.Aviso = "La página debe ser un número positivo.";
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(Vistas + "Lista.cshtml", vista);
        }

        var resultado = await administracion.ListarObras(new ListarObrasDeAdministracionEntrada(vista.Texto, vista.IncluirDadasDeBaja, paginaPedida, TamanoDePagina), cancelacion);
        if (!resultado.EsExito)
        {
            vista.Estado = EstadoDeLaFalla(resultado.Error!, "No podemos mostrar el catálogo ahora.", Request.Path + Request.QueryString, out var codigo);
            Response.StatusCode = codigo;
            return View(Vistas + "Lista.cshtml", vista);
        }

        var obras = resultado.Valor.Obras;
        var agotadas = await EdicionesAgotadas(obras.SelectMany(o => o.Ediciones).Where(e => e.Formato == FormatoDeEdicion.Impresa && e.Estado == EstadoDeEdicionConsultado.Activa).Select(e => e.EdicionId).ToList(), cancelacion);
        vista.Obras = [.. obras.Select(o => AObra(o, agotadas))];
        vista.Total = resultado.Valor.Total;
        vista.Pagina = resultado.Valor.Pagina;
        vista.TotalDePaginas = Math.Max(1, (int)Math.Ceiling(resultado.Valor.Total / (double)Math.Max(1, resultado.Valor.Tamano)));
        if (resultado.Valor.Total == 0)
        {
            vista.Estado = vista.Texto is null && !vista.IncluirDadasDeBaja
                ? EstadoVista.Vacio("Aún no hay obras", "Publica la primera obra del catálogo nacional.", "Nueva obra", "/administracion/obras/nueva")
                : EstadoVista.Vacio("Sin resultados", "Ninguna obra coincide con el filtro.", "Limpiar filtros", "/administracion/obras");
        }

        return View(Vistas + "Lista.cshtml", vista);
    }

    // ---------- Obra nueva ----------

    [HttpGet("/administracion/obras/nueva")]
    public async Task<IActionResult> Nueva(CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        return View(Vistas + "Nueva.cshtml", await pagina.Completar(new NuevaObraVista(), "Nueva obra", AreaDeLaPagina.Admin, cancelacion));
    }

    // ---------- Una obra ----------

    [HttpGet("/administracion/obras/{obraId:guid}")]
    public async Task<IActionResult> Obra(Guid obraId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var resultado = await administracion.ObtenerObra(obraId, cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, "Obra", "No podemos mostrar esta obra ahora.", Request.Path, cancelacion);
        }

        var agotadas = await EdicionesAgotadas(resultado.Valor.Ediciones.Where(e => e.Formato == FormatoDeEdicion.Impresa && e.Estado == EstadoDeEdicionConsultado.Activa).Select(e => e.EdicionId).ToList(), cancelacion);
        var vista = await pagina.Completar(new ObraDeAdministracionPagina { Obra = AObra(resultado.Valor, agotadas) }, resultado.Valor.Titulo, AreaDeLaPagina.Admin, cancelacion);
        return View(Vistas + "Obra.cshtml", vista);
    }

    // ---------- Ingresos de una edición impresa ----------

    [HttpGet("/administracion/ediciones/{edicionId:guid}/ingreso")]
    public async Task<IActionResult> Ingresos(Guid edicionId, CancellationToken cancelacion)
    {
        Response.Headers.CacheControl = "no-store";
        var ruta = Request.Path.Value ?? string.Empty;
        var edicion = await catalogo.ObtenerEdiciones([edicionId], cancelacion);
        if (!edicion.EsExito)
        {
            return await Falla(edicion.Error!, "Ingresos", "No podemos mostrar esta edición ahora.", ruta, cancelacion);
        }

        if (edicion.Valor.Count == 0)
        {
            return await Falla(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada"), "Ingresos", "No podemos mostrar esta edición ahora.", ruta, cancelacion);
        }

        var datos = edicion.Valor[0];
        if (datos.Formato != FormatoDeEdicion.Impresa || datos.Estado != EstadoDeEdicionConsultado.Activa)
        {
            var pantalla = await pagina.Completar(new PantallaDeEstadoVista { Estado = EstadoVista.Vacio("Esta edición no recibe ingresos", "Solo una edición impresa activa recibe mercadería; una digital nunca tiene stock.", "Volver a la obra", $"/administracion/obras/{datos.ObraId}") }, "Ingresos", AreaDeLaPagina.Admin, cancelacion);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return View("~/Views/Estados/Pantalla.cshtml", pantalla);
        }

        // El rol lo comprueba la Aplicación: sin permiso, estas dos llamadas responden «prohibido» y se registra el acceso denegado.
        var clave = await ingresos.EmitirClave(new EmitirClaveDeIngresoEntrada(edicionId), cancelacion);
        if (!clave.EsExito)
        {
            return await Falla(clave.Error!, "Ingresos", "No podemos preparar el ingreso ahora.", ruta, cancelacion);
        }

        var historial = await ingresos.Listar(new ListarIngresosEntrada(edicionId), cancelacion);
        if (!historial.EsExito)
        {
            return await Falla(historial.Error!, "Ingresos", "No podemos mostrar los ingresos ahora.", ruta, cancelacion);
        }

        var disp = await disponibilidad.Consultar([edicionId], cancelacion);
        var estado = disp.EsExito ? disp.Valor.FirstOrDefault() : null;
        var ahora = DateTimeOffset.UtcNow;
        var nombres = await NombresDeLosAutores(historial.Valor.Ingresos.Select(i => i.UsuarioId), cancelacion);
        var vista = await pagina.Completar(
            new IngresosDeLaEdicionVista
            {
                EdicionId = edicionId,
                ObraId = datos.ObraId,
                TituloDeLaObra = datos.Titulo,
                Isbn = datos.Isbn,
                Token = clave.Valor.Token,
                TokenVenceEn = FormatoPeruano.Hora(clave.Valor.VenceEn),
                AgotadaEnLaRed = estado is { Estado: EstadoDelCatalogo.Agotada },
                VendibleEnLaRed = estado?.VendibleEnLaRed,
                InventarioNoRespondio = historial.Valor.InventarioNoRespondio,
                HayPendiente = historial.Valor.Ingresos.Any(i => i.Estado == EstadoDeIngresoSolicitado.Pendiente),
                Ingresos = [.. historial.Valor.Ingresos.Select(i => AFila(i, ahora, nombres))],
            },
            $"Ingresos de {datos.Titulo}",
            AreaDeLaPagina.Admin,
            cancelacion);
        return View(Vistas + "Ingresos.cshtml", vista);
    }

    // ---------- Del contrato a la vista ----------

    private async Task<HashSet<Guid>> EdicionesAgotadas(IReadOnlyList<Guid> ids, CancellationToken cancelacion)
    {
        var agotadas = new HashSet<Guid>();
        if (ids.Count == 0)
        {
            return agotadas;
        }

        foreach (var lote in ids.Chunk(50))
        {
            var resultado = await disponibilidad.Consultar(lote, cancelacion);
            if (resultado.EsExito)
            {
                // Sin dato confirmado no se afirma que esté agotada (solo «Agotada» es un cero conocido).
                agotadas.UnionWith(resultado.Valor.Where(d => d.Estado == EstadoDelCatalogo.Agotada).Select(d => d.EdicionId));
            }
        }

        return agotadas;
    }

    private static ObraDeAdministracionVista AObra(ObraDeAdministracion obra, HashSet<Guid> agotadas)
    {
        var vigentes = obra.Ediciones.Where(e => e.Estado != EstadoDeEdicionConsultado.DadaDeBaja).ToList();
        EdicionDeAdministracionVista? Una(FormatoDeEdicion formato) => vigentes.Where(e => e.Formato == formato).Select(e => AEdicion(e, agotadas.Contains(e.EdicionId))).FirstOrDefault();
        return new ObraDeAdministracionVista(obra.ObraId, obra.Titulo, obra.Autor, obra.Editorial, obra.Categoria, obra.PortadaHuella, obra.Version, obra.DadaDeBaja, Una(FormatoDeEdicion.Impresa), Una(FormatoDeEdicion.Digital));
    }

    internal static EdicionDeAdministracionVista AEdicion(EdicionDeAdministracion e, bool agotada)
    {
        var licencia = e.Licencia;
        var estado = e.Estado switch
        {
            EstadoDeEdicionConsultado.Activa => e.MotivoDeNoDisponible is null ? "A la venta" : "No se ofrece",
            EstadoDeEdicionConsultado.RetiradaDeVenta => "Retirada de la venta",
            _ => "Dada de baja",
        };
        return new EdicionDeAdministracionVista(
            e.EdicionId,
            e.Formato == FormatoDeEdicion.Digital,
            e.Isbn,
            e.IsbnIngresado,
            e.Precio,
            FormatoPeruano.Soles(e.Precio),
            estado,
            e.MotivoDeRetiro,
            e.MotivoDeNoDisponible is { } m ? MotivoTexto(m) : null,
            e.Estado == EstadoDeEdicionConsultado.Activa,
            e.RegistroPendienteEnElInventario == true,
            agotada,
            licencia is null ? null : $"{(licencia.Tipo == TipoDeLicenciaDeEdicion.Comercial ? "Licencia comercial" : "Dominio público")} · {licencia.Titular}",
            licencia is null ? null : (licencia.Tipo == TipoDeLicenciaDeEdicion.Comercial ? "comercial" : "dominioPublico"),
            licencia?.Titular,
            licencia is null ? null : string.Join(", ", licencia.Territorios),
            licencia?.VigenteHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            licencia is null ? null : (licencia.Formato == FormatoDeArchivoDigital.Epub ? "epub" : "pdf"),
            e.Archivo is null ? null : $"{e.Archivo.Tipo.ToUpperInvariant()} · {Tamano(e.Archivo.TamanoEnBytes)} · huella {e.Archivo.HuellaCorta}");
    }

    /// <summary>
    /// Los nombres de quienes registraron los ingresos (CAT-22), por el contrato público de Identidad: se recorren hasta cuatro páginas de usuarios del personal (200) hasta encontrar a todos. Es un favor a la pantalla: si Identidad no responde o
    /// falta alguien, esa fila muestra el identificador abreviado y el ingreso se ve igual.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> NombresDeLosAutores(IEnumerable<Guid?> ids, CancellationToken cancelacion)
    {
        var buscados = ids.Where(i => i is { } id && id != Guid.Empty).Select(i => i!.Value).ToHashSet();
        var nombres = new Dictionary<Guid, string>();
        if (usuarios is null || buscados.Count == 0 || HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return nombres;
        }

        try
        {
            string? despues = null;
            for (var pagina = 0; pagina < 4 && nombres.Count < buscados.Count; pagina++)
            {
                var lista = await usuarios.Listar(actor.UsuarioId, new FiltroDeUsuarios(Despues: despues, Tamano: FiltroDeUsuarios.TamanoMaximo), cancelacion);
                if (!lista.EsExito)
                {
                    break;
                }

                foreach (var usuario in lista.Valor.Usuarios.Where(u => buscados.Contains(u.UsuarioId)))
                {
                    nombres[usuario.UsuarioId] = usuario.Nombre;
                }

                despues = lista.Valor.Siguiente;
                if (despues is null)
                {
                    break;
                }
            }
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Sin Identidad los nombres no se muestran: el identificador abreviado basta.
        }

        return nombres;
    }

    private static FilaDeIngresoVista AFila(IngresoDeLaLista i, DateTimeOffset ahora, IReadOnlyDictionary<Guid, string> nombres)
    {
        var estado = i.Estado switch
        {
            EstadoDeIngresoSolicitado.Pendiente => ("Pendiente", "aviso"),
            EstadoDeIngresoSolicitado.Fallido => ("Fallido", "error"),
            _ => ("Registrado", "exito"),
        };
        // El nombre del autor lo resuelve Identidad (CAT-22); sin él, el identificador abreviado.
        var usuario = i.UsuarioId is { } id && id != Guid.Empty ? (nombres.TryGetValue(id, out var nombre) ? nombre : "Usuario " + id.ToString("N")[..8]) : "Sin autor";
        return new FilaDeIngresoVista(i.IngresoSolicitadoId, i.Cantidad.ToString(CultureInfo.InvariantCulture), i.Referencia, FormatoPeruano.FechaYHora(i.Instante), usuario, estado.Item1, estado.Item2, i.Estado == EstadoDeIngresoSolicitado.Pendiente, ahora - i.Instante > TimeSpan.FromHours(24));
    }

    private static string MotivoTexto(MotivoDeNoDisponibleDeEdicion motivo) => motivo switch
    {
        MotivoDeNoDisponibleDeEdicion.LicenciaVencida => "Licencia vencida",
        MotivoDeNoDisponibleDeEdicion.SinPeru => "La licencia no incluye Perú",
        MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta => "Retirada de la venta",
        _ => "Dada de baja",
    };

    private static string Tamano(long bytes) => bytes >= 1024 * 1024 ? (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";

    // ---------- Fallos ----------

    private EstadoVista EstadoDeLaFalla(ErrorDeAplicacion error, string parteAfectada, string rutaDeReintento, out int codigo)
    {
        if (error.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado)
        {
            codigo = error.Categoria == CategoriaDeError.Prohibido ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
            return EstadoVista.SinPermiso([new EnlaceDeSalida("Ir al inicio", "/")]);
        }

        if (error.Categoria == CategoriaDeError.NoEncontrado)
        {
            codigo = StatusCodes.Status404NotFound;
            return EstadoVista.Vacio("No encontramos lo que buscas", "Puede que ya no exista.", "Volver al catálogo", "/administracion/obras");
        }

        var traduccion = TraductorDeErrores.Traducir(ProblemasHttp.Crear(HttpContext, opciones, error), rutaDeReintento: rutaDeReintento, parteAfectada: parteAfectada);
        codigo = traduccion.EstadoHttp;
        return traduccion.Vista;
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, string titulo, string parteAfectada, string rutaDeReintento, CancellationToken cancelacion)
    {
        var estado = EstadoDeLaFalla(error, parteAfectada, rutaDeReintento, out var codigo);
        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = estado }, titulo, AreaDeLaPagina.Admin, cancelacion);
        Response.StatusCode = codigo;
        Response.Headers.CacheControl = "no-store";
        return View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}
