using System.Globalization;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Inventario.Api.Fuentes;

/// <summary>
/// Los endpoints del módulo <c>Fuentes</c> (T21 y T22), bajo <c>/api/inventario/v1</c> y con los nombres de operación y los DTOs de ECO-0. No contienen reglas: traducen HTTP, leen el cuerpo con esquema estricto,
/// llaman al caso de uso por su contrato de la raíz (<see cref="ICasoDeUso{TEntrada, TSalida}"/>) y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c> con los códigos <c>INV-…</c>. Cada
/// endpoint declara <b>una operación</b> con <c>[OperacionDeServicio]</c>: la matriz de alcances decide qué credencial la autoriza (rechazo por defecto). La Api nunca decide el rol ni la sucursal: es la
/// Aplicación.
/// </summary>
public static class EndpointsDeFuentes
{
    private const int TamanoPorDefecto = 20;

    public static IEndpointRouteBuilder MapearFuentes(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var api = rutas.MapGroup(RutasDeServicio.Inventario);

        // Los cuerpos son pequeños (la consulta de varias ediciones, la más grande, no llega a 4 KB): tope propio en lugar del de 30 MB por defecto.
        api.WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));

        // La auditoría caída no deja pasar la operación (AUD-04, la transacción ya revirtió) y se responde «servicio no disponible» (AC-AUD-03), no un error inesperado.
        api.AddEndpointFilter(async (contexto, siguiente) =>
        {
            try
            {
                return await siguiente(contexto).ConfigureAwait(false);
            }
            catch (AuditoriaNoDisponibleException)
            {
                var http = contexto.HttpContext;
                return await ErroresDeFuentes.Falla(http, http.RequestServices.GetRequiredService<OpcionesDeErrores>(), new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "auditoria-no-disponible")).ConfigureAwait(false);
            }
        });

        // --- lecturas (T21) ---
        api.MapGet("/disponibilidad/{edicionId:guid}", Disponibilidad).Operacion("inventario.disponibilidad.consultar");
        api.MapPost("/disponibilidad/consultas", DisponibilidadDeVarias).Operacion("inventario.disponibilidad.consultar-varias");
        api.MapGet("/sucursales", Sucursales).Operacion("inventario.sucursales.listar");
        api.MapGet("/sucursales/{codigo}/contacto-de-notificacion", Contacto).Operacion("inventario.sucursales.contacto");
        api.MapGet("/fuentes", ListarFuentes).Operacion("inventario.fuentes.listar");
        api.MapGet("/fuentes/{codigo}", ConsultarFuente).Operacion("inventario.fuentes.consultar");
        api.MapGet("/fuentes/{codigo}/existencias", Existencias).Operacion("inventario.fuentes.existencias");
        api.MapGet("/rutas", ListarRutas).Operacion("inventario.rutas.listar");
        api.MapGet("/rutas/{origen}/{destino}", ConsultarRuta).Operacion("inventario.rutas.consultar");
        api.MapGet("/unidades-vendidas", UnidadesVendidas).Operacion("inventario.unidades-vendidas.consultar");

        // --- administración (T22) ---
        api.MapPut("/ediciones/{edicionId:guid}", RegistrarEdicion).Operacion("inventario.ediciones.registrar");
        api.MapPost("/fuentes", RegistrarFuente).Operacion("inventario.fuentes.registrar");
        api.MapPut("/fuentes/{codigo}", EditarFuente).Operacion("inventario.fuentes.editar");
        api.MapPut("/fuentes-de-recepcion", DesignarRecepcion).Operacion("inventario.fuentes.designar-recepcion");
        api.MapPut("/rutas/{origen}/{destino}", DefinirRuta).Operacion("inventario.rutas.definir");
        return rutas;
    }

    private static RouteHandlerBuilder Operacion(this RouteHandlerBuilder ruta, string nombre) => ruta.WithMetadata(new OperacionDeServicioAttribute(nombre));

    // ---------- lecturas ----------

    private static async Task<IResult> Disponibilidad(Guid edicionId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarDisponibilidadEntrada, DisponibilidadConsultada> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarDisponibilidadEntrada([edicionId]), c.RequestAborted).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            return await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
        }

        var valor = resultado.Valor;
        if (valor.NoRegistradas.Count > 0)
        {
            return await ErroresDeFuentes.Falla(c, e, new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada")).ConfigureAwait(false);
        }

        if (valor.NoImpresas.Count > 0)
        {
            return await ErroresDeFuentes.Escribir(c, e, ErroresDeFuentes.EdicionNoImpresa(c, e)).ConfigureAwait(false);
        }

        return Results.Json(ADisponibilidad(valor.Ediciones[0], valor.ConsultadaEn));
    }

    private static async Task<IResult> DisponibilidadDeVarias(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarDisponibilidadEntrada, DisponibilidadConsultada> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<ConsultaDeDisponibilidadDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new ConsultarDisponibilidadEntrada([]), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarDisponibilidadEntrada(cuerpo.EdicionIds ?? []), c.RequestAborted).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            return await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
        }

        var valor = resultado.Valor;

        // El contrato no tiene dónde poner una edición digital dentro de una consulta de varias: el Catálogo conoce el formato y no debe preguntar por digitales (decisiones T13).
        if (valor.NoImpresas.Count > 0)
        {
            return await ErroresDeFuentes.Escribir(c, e, ErroresDeFuentes.EdicionNoImpresa(c, e)).ConfigureAwait(false);
        }

        return Results.Json(new DisponibilidadDeEdicionesDto([.. valor.Ediciones.Select(d => ADisponibilidad(d, valor.ConsultadaEn))], valor.NoRegistradas));
    }

    private static async Task<IResult> Sucursales(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ListarSucursalesEntrada, IReadOnlyList<SucursalPublica>> caso)
    {
        var resultado = await caso.Ejecutar(new ListarSucursalesEntrada(), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new SucursalesDto([.. resultado.Valor.Select(s => new SucursalPublicaDto(s.Codigo, s.Nombre, s.Region))]))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Contacto(string codigo, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarContactoDeNotificacionEntrada, ContactoDeNotificacion> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarContactoDeNotificacionEntrada(codigo), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new ContactoDeSucursalDto(resultado.Valor.CodigoDeSucursal, resultado.Valor.Numero))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> ListarFuentes(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ListarFuentesEntrada, IReadOnlyList<FuenteDeStock>> caso)
    {
        var resultado = await caso.Ejecutar(new ListarFuentesEntrada(), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new ListaDeFuentesDto([.. resultado.Valor.Select(f => new FuenteDto(f.Codigo, f.Nombre, f.Region, f.EsRecepcion))]))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> ConsultarFuente(string codigo, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarFuenteEntrada, DetalleDeFuente> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarFuenteEntrada(codigo), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ADetalle(resultado.Valor)) : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Existencias(string codigo, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarExistenciasDeSucursalEntrada, PaginaDeExistencias> caso)
    {
        var consulta = c.Request.Query;
        var ediciones = new List<Guid>();
        foreach (var texto in consulta["edicionId"])
        {
            if (!Guid.TryParse(texto, out var id))
            {
                return await AutorizarAntes(caso, new ConsultarExistenciasDeSucursalEntrada(codigo, null, null, 0, 0), c, e, "edicionId", "formato-invalido").ConfigureAwait(false);
            }

            ediciones.Add(id);
        }

        EstadoDeExistencia? estado = null;
        if (consulta.TryGetValue("estado", out var textoDeEstado) && textoDeEstado.Count > 0)
        {
            estado = textoDeEstado.ToString() switch
            {
                EstadosDeStock.SinStock => EstadoDeExistencia.SinStock,
                EstadosDeStock.Bajo => EstadoDeExistencia.Bajo,
                EstadosDeStock.Normal => EstadoDeExistencia.Normal,
                _ => null,
            };
            if (estado is null)
            {
                return await AutorizarAntes(caso, new ConsultarExistenciasDeSucursalEntrada(codigo, null, null, 0, 0), c, e, "estado", "formato-invalido").ConfigureAwait(false);
            }
        }

        if (!Entero(consulta["pagina"], 1, out var pagina))
        {
            return await AutorizarAntes(caso, new ConsultarExistenciasDeSucursalEntrada(codigo, null, null, 0, 0), c, e, "pagina", "no-es-numero").ConfigureAwait(false);
        }

        if (!Entero(consulta["tamano"], TamanoPorDefecto, out var tamano))
        {
            return await AutorizarAntes(caso, new ConsultarExistenciasDeSucursalEntrada(codigo, null, null, 0, 0), c, e, "tamano", "no-es-numero").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarExistenciasDeSucursalEntrada(codigo, ediciones.Count == 0 ? null : ediciones, estado, pagina, tamano), c.RequestAborted).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            return await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
        }

        var pag = resultado.Valor;
        return Results.Json(new PaginaDto<ExistenciaDto>(
            [.. pag.Elementos.Select(x => new ExistenciaDto(x.EdicionId, x.Fisico, x.Reservado, x.Vendible, EstadoComoTexto(x.Estado)))],
            pag.Pagina,
            pag.Tamano,
            pag.Total));
    }

    private static async Task<IResult> ListarRutas(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ListarRutasEntrada, IReadOnlyList<RutaListada>> caso)
    {
        var resultado = await caso.Ejecutar(new ListarRutasEntrada(), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new ListaDeRutasDto([.. resultado.Valor.Select(r => new RutaDto(r.CodigoDeOrigen, r.CodigoDeDestino, r.Dias))]))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> ConsultarRuta(string origen, string destino, HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarPlazoDeRutaEntrada, PlazoDeRutaConsultado> caso)
    {
        var resultado = await caso.Ejecutar(new ConsultarPlazoDeRutaEntrada(origen, destino), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new RutaDto(resultado.Valor.CodigoDeOrigen, resultado.Valor.CodigoDeDestino, resultado.Valor.Dias))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> UnidadesVendidas(HttpContext c, OpcionesDeErrores e, ICasoDeUso<ConsultarUnidadesVendidasEntrada, UnidadesVendidasConsultadas> caso)
    {
        var codigos = c.Request.Query["codigoDeFuente"];
        if (codigos.Count > 1)
        {
            return await AutorizarAntes(caso, new ConsultarUnidadesVendidasEntrada(string.Empty), c, e, "codigoDeFuente", "formato-invalido").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new ConsultarUnidadesVendidasEntrada(codigos.Count == 1 ? codigos[0] : null), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new UnidadesVendidasDto(
                resultado.Valor.Dias,
                resultado.Valor.Total,
                [.. resultado.Valor.PorFuente.Select(f => new UnidadesDeFuenteDto(f.CodigoDeFuente, f.Unidades))]))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    // ---------- administración ----------

    private static async Task<IResult> RegistrarEdicion(Guid edicionId, HttpContext c, OpcionesDeErrores e, ICasoDeUso<RegistrarFichaDeEdicionEntrada, FichaDeEdicionGuardada> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<RegistrarEdicionDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new RegistrarFichaDeEdicionEntrada(Guid.Empty, default), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        FormatoDeEdicionRegistrado? formato = cuerpo.Formato switch
        {
            FormatosDeEdicion.Impreso => FormatoDeEdicionRegistrado.Impreso,
            FormatosDeEdicion.Digital => FormatoDeEdicionRegistrado.Digital,
            _ => null,
        };
        if (formato is null)
        {
            return await AutorizarAntes(caso, new RegistrarFichaDeEdicionEntrada(Guid.Empty, default), c, e, "formato", "formato-invalido").ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new RegistrarFichaDeEdicionEntrada(edicionId, formato.Value), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.NoContent() : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> RegistrarFuente(HttpContext c, OpcionesDeErrores e, ICasoDeUso<RegistrarFuenteEntrada, DetalleDeFuente> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<RegistrarFuenteDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new RegistrarFuenteEntrada(string.Empty, string.Empty, string.Empty, null), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new RegistrarFuenteEntrada(cuerpo.Codigo, cuerpo.Nombre, cuerpo.Region, cuerpo.ContactoDeNotificacion), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(ADetalle(resultado.Valor), statusCode: StatusCodes.Status201Created)
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> EditarFuente(string codigo, HttpContext c, OpcionesDeErrores e, ICasoDeUso<EditarFuenteEntrada, DetalleDeFuente> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<EditarFuenteDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new EditarFuenteEntrada(string.Empty, string.Empty, string.Empty, null), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new EditarFuenteEntrada(codigo, cuerpo.Nombre, cuerpo.Region, cuerpo.ContactoDeNotificacion), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ADetalle(resultado.Valor)) : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> DesignarRecepcion(HttpContext c, OpcionesDeErrores e, ICasoDeUso<DesignarFuenteDeRecepcionEntrada, RecepcionDesignada> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<DesignarRecepcionDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new DesignarFuenteDeRecepcionEntrada(string.Empty), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new DesignarFuenteDeRecepcionEntrada(cuerpo.Codigo), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.NoContent() : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> DefinirRuta(string origen, string destino, HttpContext c, OpcionesDeErrores e, ICasoDeUso<RegistrarRutaEntrada, RutaDefinida> caso)
    {
        var (cuerpo, clave) = await CuerpoEstricto.Leer<DefinirRutaDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await AutorizarAntes(caso, new RegistrarRutaEntrada(string.Empty, string.Empty, 0), c, e, "cuerpo", clave!).ConfigureAwait(false);
        }

        var resultado = await caso.Ejecutar(new RegistrarRutaEntrada(origen, destino, cuerpo.Dias), c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new RutaDto(resultado.Valor.CodigoDeOrigen, resultado.Valor.CodigoDeDestino, resultado.Valor.Dias))
            : await ErroresDeFuentes.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    /// <summary>
    /// Un cuerpo o un parámetro mal formado no se responde con 400 antes de autorizar: se pasa a la Aplicación una entrada que su validación rechaza, de modo que quien no tiene alcance recibe su 403 con el evento de acceso
    /// denegado (alcance primero, AGENTS §5, AC-FUE-06) y solo quien sí lo tiene recibe el 400 de forma. La entrada inválida nunca llega a leer ni a escribir nada.
    /// </summary>
    private static async Task<IResult> AutorizarAntes<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> caso, TEntrada entradaInvalida, HttpContext c, OpcionesDeErrores e, string campo, string clave)
    {
        var resultado = await caso.Ejecutar(entradaInvalida, c.RequestAborted).ConfigureAwait(false);
        return resultado.Error is { Categoria: CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado }
            ? await ErroresDeFuentes.Falla(c, e, resultado.Error).ConfigureAwait(false)
            : await ErroresDeFuentes.Validacion(c, e, campo, clave).ConfigureAwait(false);
    }

    // ---------- traducciones ----------

    private static DisponibilidadDeEdicionDto ADisponibilidad(DisponibilidadPublicaDeEdicion d, DateTimeOffset consultadaEn) =>
        new(d.EdicionId, d.VendibleEnLaRed, [.. d.PorFuente.Select(f => new FuenteConVendibleDto(f.CodigoDeFuente, f.NombreDeFuente, f.Vendible, EstadoComoTexto(f.Estado)))], consultadaEn);

    private static FuenteDetalleDto ADetalle(DetalleDeFuente f) =>
        new(f.Codigo, f.Nombre, f.Region, f.ContactoDeNotificacion, f.EsRecepcion, f.FisicoTotal, f.ReservadoTotal, f.VendibleTotal);

    private static string EstadoComoTexto(EstadoDeExistencia estado) => estado switch
    {
        EstadoDeExistencia.SinStock => EstadosDeStock.SinStock,
        EstadoDeExistencia.Bajo => EstadosDeStock.Bajo,
        _ => EstadosDeStock.Normal,
    };

    /// <summary>Un entero de la consulta; ausente es el valor por defecto y un texto que no es un entero se rechaza (el rango lo valida la Aplicación).</summary>
    private static bool Entero(Microsoft.Extensions.Primitives.StringValues valores, int porDefecto, out int valor)
    {
        valor = porDefecto;
        return valores.Count == 0 || (valores.Count == 1 && int.TryParse(valores[0], NumberStyles.None, CultureInfo.InvariantCulture, out valor));
    }
}
