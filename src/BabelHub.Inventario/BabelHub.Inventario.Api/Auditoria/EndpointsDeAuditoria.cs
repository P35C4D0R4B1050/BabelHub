using System.Globalization;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Auditoria;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Inventario.Api.Auditoria;

/// <summary>
/// Los endpoints de auditoría, alertas e historial del Inventario (T28; AUD-07, AUD-08, AUD-27, AUD-30, AUD-38), bajo <c>/api/inventario/v1</c> y con los cuerpos de <c>BabelHub.Shared.Contratos.Auditoria</c>. No contienen reglas: traducen
/// HTTP, llaman a la Aplicación por su contrato público (<see cref="IConsultasDeAuditoria"/>, <see cref="IConsultaDeAlertas"/>, <see cref="IHistorialDeSucursal"/>) y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c>. Quién
/// puede consultar (administrador central, la sucursal del token, la integración de lectura de alertas) lo decide la Aplicación con el actor que dejaron la credencial de servicio y el token verificable; <b>nunca una cabecera</b>. Un «prohibido»
/// o «no autenticado» de la Aplicación se registra como acceso denegado aquí, un solo punto (AUD-05); el cursor y los filtros inválidos son siempre el mismo 400 uniforme.
/// </summary>
public static class EndpointsDeAuditoria
{
    public static IEndpointRouteBuilder MapearAuditoria(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var api = rutas.MapGroup(RutasDeServicio.Inventario);
        api.WithMetadata(new RequestSizeLimitAttribute(4 * 1024));

        api.MapGet("/auditoria", Auditoria).Operacion("inventario.auditoria.consultar");
        api.MapGet("/alertas", Alertas).Operacion("inventario.alertas.listar");
        api.MapPost("/alertas/{id}/revisada", MarcarRevisada).Operacion("inventario.alertas.marcar-revisada");
        api.MapGet("/historial", Historial).Operacion("inventario.historial.consultar");
        return rutas;
    }

    private static RouteHandlerBuilder Operacion(this RouteHandlerBuilder ruta, string nombre) => ruta.WithMetadata(new OperacionDeServicioAttribute(nombre));

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
        return resultado.EsExito ? Results.Json(MapeoDeAuditoria.APagina(resultado.Valor, conCursorDeLaPagina: true)) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Alertas(HttpContext c, OpcionesDeErrores e, IConsultaDeAlertas alertas)
    {
        var (desde, error) = ConsultaHttp.LeerDesde(c.Request);
        if (desde is null)
        {
            return await RespuestasDeAuditoria.Falla(c, e, error!).ConfigureAwait(false);
        }

        var resultado = await alertas.LeerNuevas(desde.Value, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(resultado.Valor.Select(MapeoDeAuditoria.AAlerta).ToList()) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> MarcarRevisada(string id, HttpContext c, OpcionesDeErrores e, IConsultaDeAlertas alertas)
    {
        if (await SinUsuario(c, e).ConfigureAwait(false) is { } sinUsuario)
        {
            return sinUsuario;
        }

        var (cuerpo, clave) = await CuerpoEstricto.Leer<MarcarAlertaRevisadaDto>(c).ConfigureAwait(false);
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
        return resultado.EsExito ? Results.Json(MapeoDeAuditoria.APagina(resultado.Valor, conCursorDeLaPagina: true)) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    /// <summary>
    /// Las operaciones de usuario exigen un usuario autenticado, no solo la credencial de servicio: sin el token verificable no hay quién actúe y la respuesta es 401 (registrado), no un 403 (AUD-23: la credencial de servicio sola no
    /// suplanta a nadie). El rol y la sucursal los decide la Aplicación con ese usuario.
    /// </summary>
    private static async Task<IResult?> SinUsuario(HttpContext c, OpcionesDeErrores e)
    {
        var actor = c.RequestServices.GetRequiredService<IActorActual>().Actual;
        return actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null }
            ? null
            : await RespuestasDeAuditoria.Falla(c, e, new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, RegistradorDeDenegacionesHttp.MotivoSinSesion)).ConfigureAwait(false);
    }
}

/// <summary>La matriz operación → alcance de la credencial de las operaciones de auditoría, tomada del catálogo de contratos v1 (rechazo por defecto).</summary>
public static class MatrizDeAuditoria
{
    public static IReadOnlyList<string> Operaciones { get; } =
    [
        "inventario.auditoria.consultar", "inventario.alertas.listar", "inventario.alertas.marcar-revisada", "inventario.historial.consultar",
    ];

    public static IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> Declaraciones() =>
        Operaciones.Select(nombre =>
        {
            var contrato = OperacionesDelInventario.Todas.Single(o => o.Nombre == nombre);
            return (new OperacionDeServicio(nombre), AlcanceDe(contrato.Alcance));
        });

    private static AlcanceDeServicio AlcanceDe(string alcance) => alcance switch
    {
        AlcancesDeCredencial.ConsultaDeAuditoria => AlcanceDeServicio.ConsultaDeAuditoria,
        AlcancesDeCredencial.LecturaDeAlertas => AlcanceDeServicio.LecturaDeAlertas,
        AlcancesDeCredencial.MarcarAlerta => AlcanceDeServicio.MarcarAlerta,
        AlcancesDeCredencial.Historial => AlcanceDeServicio.Historial,
        _ => throw new ArgumentOutOfRangeException(nameof(alcance), "Una operación de auditoría usa un alcance que este módulo no conoce."),
    };
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
