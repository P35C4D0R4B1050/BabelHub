using System.Globalization;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Auditoria;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Auditoria;

/// <summary>
/// Los endpoints de auditoría, alertas e historial del E-commerce (T28; AUD-07, AUD-08, AUD-09, AUD-13, AUD-18, AUD-27, AUD-34), bajo <c>/api/ecommerce/v1</c> y con los cuerpos de <c>BabelHub.Shared.Contratos.Auditoria</c>. No contienen reglas:
/// traducen HTTP, llaman a la Aplicación por su contrato público (<see cref="IConsultasDeAuditoria"/>, <see cref="IConsultaDeAlertas"/>, <see cref="IHistorialDeSucursal"/>) y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c>.
/// La lista de auditoría y la de alertas son <b>unificadas</b>: la Aplicación fusiona la propia, la de Identidad y la del Inventario y, si este no responde, devuelve 200 con <c>faltanDelInventario</c> (AUD-18). Quién puede consultar lo decide la
/// Aplicación con el actor verificado (administrador central; el historial, la sucursal del token), nunca una cabecera. Un «prohibido» o «no autenticado» se registra como acceso denegado aquí, un solo punto (AUD-05); el cursor inválido
/// y los filtros inválidos son el mismo 400 uniforme.
/// </summary>
public static class EndpointsDeAuditoria
{
    public static IEndpointRouteBuilder MapearAuditoria(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var api = rutas.MapGroup(RutasDeServicio.Ecommerce);
        api.WithMetadata(new RequestSizeLimitAttribute(4 * 1024));

        api.MapGet("/auditoria", Auditoria);
        api.MapGet("/alertas", Alertas);
        api.MapPost("/alertas/{id}/revisada", MarcarRevisada);
        api.MapGet("/historial", Historial);
        return rutas;
    }

    private static async Task<IResult> Auditoria(HttpContext c, OpcionesDeErrores e, IConsultasDeAuditoria consultas)
    {
        if (await SinUsuario(c, e).ConfigureAwait(false) is { } sinUsuario)
        {
            return sinUsuario;
        }

        var (consulta, error) = ConsultaHttp.LeerAuditoria(c.Request);
        if (consulta is null)
        {
            return await RespuestasDeAuditoria.Falla(c, e, error!).ConfigureAwait(false);
        }

        var resultado = await consultas.Consultar(consulta, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(MapeoDeAuditoria.APagina(resultado.Valor, conCursorDeLaPagina: false)) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Alertas(HttpContext c, OpcionesDeErrores e, IConsultaDeAlertas alertas)
    {
        if (await SinUsuario(c, e).ConfigureAwait(false) is { } sinUsuario)
        {
            return sinUsuario;
        }

        var (cursor, error) = ConsultaHttp.LeerCursor(c.Request);
        if (error is not null)
        {
            return await RespuestasDeAuditoria.Falla(c, e, error).ConfigureAwait(false);
        }

        var resultado = await alertas.Listar(cursor, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito
            ? Results.Json(new PaginaDeAlertasDto([.. resultado.Valor.Alertas.Select(MapeoDeAuditoria.AAlerta)], resultado.Valor.SiguienteCursor, resultado.Valor.FaltanDelInventario))
            : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> MarcarRevisada(string id, HttpContext c, OpcionesDeErrores e, IConsultaDeAlertas alertas)
    {
        if (await SinUsuario(c, e).ConfigureAwait(false) is { } sinUsuario)
        {
            return sinUsuario;
        }

        var (cuerpo, clave) = await CuerpoEstrictoDeAuditoria.Leer<MarcarAlertaRevisadaDto>(c).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return await RespuestasDeAuditoria.Falla(c, e, new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cuerpo", clave!)])).ConfigureAwait(false);
        }

        var resultado = await alertas.MarcarRevisada(id, cuerpo.ContadorVisto, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.NoContent() : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Historial(HttpContext c, OpcionesDeErrores e, IHistorialDeSucursal historial)
    {
        if (await SinUsuario(c, e).ConfigureAwait(false) is { } sinUsuario)
        {
            return sinUsuario;
        }

        var (sucursal, cursor, error) = ConsultaHttp.LeerHistorial(c.Request);
        if (error is not null)
        {
            return await RespuestasDeAuditoria.Falla(c, e, error).ConfigureAwait(false);
        }

        var resultado = await historial.Consultar(sucursal, cursor, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(MapeoDeAuditoria.APagina(resultado.Valor, conCursorDeLaPagina: false)) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    /// <summary>Sin un usuario autenticado no hay quién consulte: 401 registrado (AUD-05). El rol y la sucursal los decide la Aplicación con ese usuario.</summary>
    private static async Task<IResult?> SinUsuario(HttpContext c, OpcionesDeErrores e)
    {
        var actor = c.RequestServices.GetRequiredService<IActorActual>().Actual;
        return actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null }
            ? null
            : await RespuestasDeAuditoria.Falla(c, e, new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, RegistradorDeDenegacionesHttp.MotivoSinSesion)).ConfigureAwait(false);
    }
}

/// <summary>Lee el cuerpo JSON con esquema estricto: un campo desconocido, un cuerpo ausente o mal formado es validación; nunca devuelve el valor recibido.</summary>
public static class CuerpoEstrictoDeAuditoria
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static async Task<(T? Valor, string? Clave)> Leer<T>(HttpContext contexto)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(contexto);
        try
        {
            var valor = await contexto.Request.ReadFromJsonAsync<T>(Opciones, contexto.RequestAborted).ConfigureAwait(false);
            return valor is null ? (null, "obligatorio") : (valor, null);
        }
        catch (Exception falla) when (falla is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return (null, "formato-invalido");
        }
    }
}

/// <summary>Traduce el fallo de la Aplicación al error uniforme y, si es un acceso denegado, lo registra una sola vez (AUD-05).</summary>
public static class RespuestasDeAuditoria
{
    public static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores opciones, ErrorDeAplicacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var problema = ProblemasHttp.Crear(contexto, opciones, error);
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
        return Results.Empty;
    }
}

/// <summary>
/// Lee la consulta de la URL con esquema estricto: un parámetro repetido, un identificador o una fecha con otra forma o un cursor demasiado largo es la misma validación uniforme (nunca se devuelve el valor recibido). El texto y el origen
/// los valida la Aplicación (formato, 3 a 100 caracteres, comodines escapados); el cursor lo firma y abre el módulo.
/// </summary>
public static class ConsultaHttp
{
    private static readonly string[] ParametrosDeAuditoria = ["accion", "usuarioId", "origen", "desde", "hasta", "texto", "cursor"];

    private static ErrorDeAplicacion Invalido(string campo) =>
        new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, "formato-invalido")]);

    public static (ConsultaDeAuditoria? Consulta, ErrorDeAplicacion? Error) LeerAuditoria(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var consulta = solicitud.Query;
        foreach (var nombre in ParametrosDeAuditoria)
        {
            if (consulta[nombre].Count > 1)
            {
                return (null, Invalido(nombre));
            }
        }

        if (!TryGuid(consulta, "usuarioId", out var usuario) || !TryFecha(consulta, "desde", out var desde) || !TryFecha(consulta, "hasta", out var hasta))
        {
            var campo = !TryGuid(consulta, "usuarioId", out _) ? "usuarioId" : !TryFecha(consulta, "desde", out _) ? "desde" : "hasta";
            return (null, Invalido(campo));
        }

        var cursor = Texto(consulta, "cursor");
        if (cursor is { Length: > PaginaDeRegistros.MaximoDeCaracteresDelCursor })
        {
            return (null, Invalido("cursor"));
        }

        var filtro = new FiltroDeAuditoria
        {
            Accion = Texto(consulta, "accion"),
            UsuarioId = usuario,
            Origen = Texto(consulta, "origen"),
            Desde = desde,
            Hasta = hasta,
            Texto = Texto(consulta, "texto"),
        };
        return (new ConsultaDeAuditoria(filtro, cursor), null);
    }

    public static (DateTimeOffset? Desde, ErrorDeAplicacion? Error) LeerDesde(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var consulta = solicitud.Query;
        if (consulta["desde"].Count > 1 || !TryFecha(consulta, "desde", out var desde))
        {
            return (null, Invalido("desde"));
        }

        // Sin «desde» se leen las de los últimos 90 días (el horizonte de las alertas, AUD-13).
        return (desde ?? DateTimeOffset.UtcNow.AddDays(-90), null);
    }

    public static (string? Cursor, ErrorDeAplicacion? Error) LeerCursor(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var consulta = solicitud.Query;
        if (consulta["cursor"].Count > 1 || Texto(consulta, "cursor") is { Length: > PaginaDeRegistros.MaximoDeCaracteresDelCursor })
        {
            return (null, Invalido("cursor"));
        }

        return (Texto(consulta, "cursor"), null);
    }

    public static (Guid? Sucursal, string? Cursor, ErrorDeAplicacion? Error) LeerHistorial(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var consulta = solicitud.Query;
        if (consulta["sucursal"].Count > 1 || consulta["cursor"].Count > 1)
        {
            return (null, null, Invalido(consulta["sucursal"].Count > 1 ? "sucursal" : "cursor"));
        }

        if (!TryGuid(consulta, "sucursal", out var sucursal))
        {
            return (null, null, Invalido("sucursal"));
        }

        var cursor = Texto(consulta, "cursor");
        return cursor is { Length: > PaginaDeRegistros.MaximoDeCaracteresDelCursor } ? (null, null, Invalido("cursor")) : (sucursal, cursor, null);
    }

    private static string? Texto(IQueryCollection consulta, string nombre) => consulta[nombre].Count == 1 && consulta[nombre][0] is { Length: > 0 } valor ? valor : null;

    private static bool TryGuid(IQueryCollection consulta, string nombre, out Guid? valor)
    {
        valor = null;
        var texto = Texto(consulta, nombre);
        if (texto is null)
        {
            return true;
        }

        if (texto.Length == 36 && Guid.TryParseExact(texto, "D", out var guid) && guid != Guid.Empty)
        {
            valor = guid;
            return true;
        }

        return false;
    }

    private static bool TryFecha(IQueryCollection consulta, string nombre, out DateTimeOffset? valor)
    {
        valor = null;
        var texto = Texto(consulta, nombre);
        if (texto is null)
        {
            return true;
        }

        if (texto.Length <= 40 && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fecha))
        {
            valor = fecha;
            return true;
        }

        return false;
    }
}

/// <summary>Los modelos de la Aplicación a los cuerpos del contrato (<c>Shared</c>): los nombres de los enumerados viajan tal cual (T21).</summary>
public static class MapeoDeAuditoria
{
    public static PaginaDeRegistrosDto APagina(PaginaDeRegistros pagina, bool conCursorDeLaPagina) =>
        new([.. pagina.Registros.Select(ARegistro)], pagina.SiguienteCursor, conCursorDeLaPagina ? pagina.CursorDeLaPagina : null, pagina.FaltanDelInventario);

    public static RegistroDto ARegistro(RegistroAuditado r) =>
        new(
            r.Id,
            r.Servicio.ToString(),
            r.Tipo.ToString(),
            r.ActorTipo.ToString(),
            r.ActorId,
            r.ActorRol,
            r.InstanteUtc,
            r.Accion,
            r.Resultado.ToString(),
            r.ObjetoTipo,
            r.ObjetoId,
            r.SucursalOrigen,
            r.SucursalDestino,
            r.Motivo,
            r.Origen,
            r.Ruta,
            r.Detalle,
            [.. r.ClavesPersonales],
            r.Truncado,
            r.Correlacion,
            r.AnonimizadoEn);

    public static AlertaDto AAlerta(AlertaConsultada a) =>
        new(
            a.Id,
            a.Servicio.ToString(),
            a.Motivo.Valor,
            a.Clase.ToString(),
            a.PrimeraOcurrencia,
            a.UltimaOcurrencia,
            a.Ocurrencias,
            a.RevisadasHasta,
            a.RevisadaPor,
            a.RevisadaEn,
            a.Estado.ToString());
}
