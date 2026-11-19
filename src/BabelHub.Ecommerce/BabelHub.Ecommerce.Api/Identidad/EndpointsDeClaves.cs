using BabelHub.Ecommerce.Api.Auditoria;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Ecommerce.Api.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Identidad;

/// <summary>
/// Los endpoints de claves de Identidad (identidad-seguridad T44 reducido; IDE-35, IDE-36, IDE-61, IDE-85): la <b>publicación</b> de la clave de verificación y de las revocaciones (<c>/.well-known/jwks.json</c> y
/// <c>/.well-known/revocaciones</c>, sin autenticación: son públicas, solo llevan claves públicas y los <c>kid</c> revocados, y el Inventario las renueva cada 5 minutos desde la réplica de gestión de su VM) y la
/// <b>administración</b> (rotar y declarar comprometida, solo el administrador central y con el actor de la solicitud, nunca una cabecera). Ningún endpoint de este módulo devuelve una credencial de usuario ni una
/// renovación (AGENTS.md §3.9): el ingreso, la sesión y la cuenta se consumen en proceso por los contratos de Identidad y no se publican por HTTP.
/// </summary>
public static class EndpointsDeClaves
{
    /// <param name="rutas">Los endpoints.</param>
    /// <param name="conAdministracion">Rotar y declarar comprometida solo se mapean en el host de gestión (D03); la publicación va donde la sirva la réplica.</param>
    public static IEndpointRouteBuilder MapearClavesDeIdentidad(this IEndpointRouteBuilder rutas, bool conAdministracion = true)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        rutas.MapGet(RutasDeClavesDeFirma.Jwks, Jwks).WithMetadata(new RequestSizeLimitAttribute(0));
        rutas.MapGet(RutasDeClavesDeFirma.Revocaciones, Revocaciones).WithMetadata(new RequestSizeLimitAttribute(0));

        if (!conAdministracion)
        {
            return rutas;
        }

        var admin = rutas.MapGroup(string.Empty);
        admin.AddEndpointFilter<ProteccionDeLaAdministracionDeClaves>();
        admin.WithMetadata(new RequestSizeLimitAttribute(1024));
        admin.MapPost(RutasDeClavesDeFirma.Rotar, Rotar);
        admin.MapPost(RutasDeClavesDeFirma.Comprometida, Comprometida);
        return rutas;
    }

    private static async Task<IResult> Jwks(HttpContext c, OpcionesDeErrores e, IClavesDeFirma claves)
    {
        try
        {
            var publicadas = await claves.Publicadas(c.RequestAborted).ConfigureAwait(false);
            c.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Text(DocumentosDeClavesPublicadas.Jwks(publicadas), "application/json");
        }
        catch (Exception) when (!c.RequestAborted.IsCancellationRequested)
        {
            return await NoDisponible(c, e).ConfigureAwait(false);
        }
    }

    private static async Task<IResult> Revocaciones(HttpContext c, OpcionesDeErrores e, IClavesDeFirma claves, IReloj reloj)
    {
        try
        {
            var kids = await claves.Revocadas(c.RequestAborted).ConfigureAwait(false);
            // Sin caché: cada servicio comprueba que el documento se generó hace menos de 5 minutos (IDE-61).
            c.Response.Headers.CacheControl = "no-store";
            return Results.Text(DocumentosDeClavesPublicadas.Revocaciones(kids, reloj.Ahora), "application/json");
        }
        catch (Exception) when (!c.RequestAborted.IsCancellationRequested)
        {
            return await NoDisponible(c, e).ConfigureAwait(false);
        }
    }

    private static async Task<IResult> Rotar(HttpContext c, OpcionesDeErrores e, IClavesDeFirma claves, IActorActual actores)
    {
        if (ActorDeUsuario(actores) is not { } administrador)
        {
            return await RespuestasDeAuditoria.Falla(c, e, SinSesion()).ConfigureAwait(false);
        }

        c.Response.Headers.CacheControl = "no-store";
        var resultado = await claves.Rotar(administrador, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ADto(resultado.Valor)) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Comprometida(HttpContext c, OpcionesDeErrores e, IClavesDeFirma claves, IActorActual actores)
    {
        if (ActorDeUsuario(actores) is not { } administrador)
        {
            return await RespuestasDeAuditoria.Falla(c, e, SinSesion()).ConfigureAwait(false);
        }

        var (cuerpo, clave) = await CuerpoEstrictoDeAuditoria.Leer<DeclararClaveComprometidaDto>(c).ConfigureAwait(false);
        if (cuerpo is null || string.IsNullOrWhiteSpace(cuerpo.Kid) || cuerpo.Kid.Length > 100)
        {
            return await RespuestasDeAuditoria.Falla(c, e, new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("kid", clave ?? (string.IsNullOrWhiteSpace(cuerpo?.Kid) ? "obligatorio" : "demasiado-largo"))])).ConfigureAwait(false);
        }

        c.Response.Headers.CacheControl = "no-store";
        var resultado = await claves.DeclararComprometida(administrador, cuerpo.Kid, c.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? Results.Json(ADto(resultado.Valor)) : await RespuestasDeAuditoria.Falla(c, e, resultado.Error!).ConfigureAwait(false);
    }

    private static Guid? ActorDeUsuario(IActorActual actores) =>
        actores.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: { } id } ? id : null;

    private static ErrorDeAplicacion SinSesion() => new(CategoriaDeError.NoAutenticado, RegistradorDeDenegacionesHttp.MotivoSinSesion);

    private static async Task<IResult> NoDisponible(HttpContext c, OpcionesDeErrores e) =>
        await RespuestasDeAuditoria.Falla(c, e, new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "claves-no-disponibles")).ConfigureAwait(false);

    /// <summary>Solo el <c>kid</c>, el algoritmo y el estado: ni la clave privada ni los parámetros públicos salen de aquí.</summary>
    private static ClaveDeFirmaDto ADto(ClavePublicaDeFirma clave) => new(clave.Kid, clave.Algoritmo, clave.Estado);
}

/// <summary>
/// La protección de los <c>POST</c> de administración de claves contra la falsificación de solicitudes (WEB-05, D07): el actor sale de la cookie de sesión de la web, así que, como en toda operación que cambia estado,
/// se comprueban <b>Origin y Sec-Fetch-Site</b> y el <b>token antifalsificación</b> (cabecera); un formulario de otro origen o sin token no llega a <see cref="IClavesDeFirma"/>. Cada rechazo se registra como acceso denegado
/// y responde sin detalles.
/// </summary>
internal sealed class ProteccionDeLaAdministracionDeClaves : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var c = contexto.HttpContext;
        var errores = c.RequestServices.GetRequiredService<OpcionesDeErrores>();
        var host = c.RequestServices.GetRequiredService<OpcionesDelHostWeb>();
        if (ComprobadorDeOrigen.Comprobar(c.Request, host.HostsPermitidos) != ResultadoDeOrigen.Valido)
        {
            return await RespuestasDeAuditoria.Falla(c, errores, new ErrorDeAplicacion(CategoriaDeError.Prohibido, "origen-no-permitido")).ConfigureAwait(false);
        }

        try
        {
            await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return await RespuestasDeAuditoria.Falla(c, errores, new ErrorDeAplicacion(CategoriaDeError.Prohibido, "token-antifalsificacion-invalido")).ConfigureAwait(false);
        }

        return await siguiente(contexto).ConfigureAwait(false);
    }
}
