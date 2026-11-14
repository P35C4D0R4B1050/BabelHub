using System.Security.Claims;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Errores;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// La sesión web en el pipeline (WEB-04, WEB-27, WEB-28, WEB-31, WEB-32; plan §1). Va <b>después</b> del tope previo por huella (que acota el costo de una cookie
/// falsa bien formada, D01) y del enrutado, y <b>antes</b> de los límites por usuario y del antiforgery. En orden: (1) lee la cookie del host de este proceso; (2) el
/// módulo valida su forma <i>antes</i> de tocar la base, la carga, rechaza una fila cerrada o de otro host y vence por inactividad o por tope absoluto (solo una acción de
/// la persona renueva la inactividad: una <c>[ConsultaAutomatica]</c> nunca); (3) asegura la credencial (renovarla bajo bloqueo si faltan menos de 60 segundos y verificarla:
/// firma, <c>kid</c>, <c>aud</c> y revocaciones) y <b>de ella sale el actor</b>, nunca de la tabla; (4) fija la identidad de la solicitud y el actor de la Aplicación.
/// </summary>
/// <remarks>
/// Cuando la sesión no sirve, la cookie se borra y la solicitud sigue como visita anónima: quien decide si la pantalla pide ingreso es el filtro de área (T18). Si Identidad
/// o el verificador no responden la sesión <b>no se cierra</b> y la solicitud responde «servicio no disponible» (302 a <c>/no-disponible</c> para una pantalla, <c>HX-Redirect</c>
/// para htmx, <c>503</c> con <c>problem+json</c> para lo demás). En los modos <c>Ensayo</c> y <c>Restauracion</c> no se carga ninguna sesión (D14).
/// El actor se fija en <see cref="InvokeAsync"/> y no en los métodos asíncronos que deciden: un <c>AsyncLocal</c> fijado dentro de un método <c>async</c> que ya terminó no llega
/// a quienes vienen después.
/// </remarks>
public sealed class SesionWebHandler(RequestDelegate siguiente, OpcionesDelHostWeb host, ModoDeArranque modo, OpcionesDeErrores errores)
{
    /// <summary>Las rutas que se sirven sin consultar la sesión: la salud para el reinicio automático y los destinos de las propias redirecciones de error (sin bucle).</summary>
    private static readonly string[] RutasSinSesion = ["/health", "/salud", "/no-disponible", "/error"];

    /// <summary>La ruta que cierra la sesión: sigue su camino aunque el servicio no esté disponible, para que la persona siempre pueda salir.</summary>
    private static readonly PathString RutaDeCierre = new("/sesion/cerrar");

    private readonly record struct Decision(bool Continuar, ActorActual Actor);

    public async Task InvokeAsync(HttpContext contexto, ISesionesWeb sesiones, IEstablecedorDeActor actor)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(sesiones);
        ArgumentNullException.ThrowIfNull(actor);

        try
        {
            var decision = await Resolver(contexto, sesiones).ConfigureAwait(false);
            if (!decision.Continuar)
            {
                return;
            }

            actor.Establecer(decision.Actor);
            await siguiente(contexto).ConfigureAwait(false);
        }
        finally
        {
            actor.Limpiar();
        }
    }

    private async Task<Decision> Resolver(HttpContext contexto, ISesionesWeb sesiones)
    {
        if (modo != ModoDeArranque.Normal || SinSesion(contexto.Request.Path))
        {
            return Anonimo(contexto, EstadoDeLaSesionEnLaSolicitud.NoSeCargo);
        }

        var valor = CookieDeSesionWeb.Leer(contexto.Request, host.Host);
        if (valor is null)
        {
            return Anonimo(contexto, EstadoDeLaSesionEnLaSolicitud.SinCookie);
        }

        var origen = contexto.EsConsultaAutomatica() ? OrigenDeLaAccion.ConsultaAutomatica : OrigenDeLaAccion.AccionDelUsuario;
        var carga = await sesiones.Cargar(valor, host.Host, origen, contexto.RequestAborted).ConfigureAwait(false);
        switch (carga.Estado)
        {
            case EstadoDeCarga.Valida:
                break;
            case EstadoDeCarga.ServicioNoDisponible:
                return await NoDisponible(contexto).ConfigureAwait(false);
            default:
                return NoValida(contexto, carga.Estado switch
                {
                    EstadoDeCarga.Cerrada => EstadoDeLaSesionEnLaSolicitud.Cerrada,
                    EstadoDeCarga.OtroHost => EstadoDeLaSesionEnLaSolicitud.OtroHost,
                    EstadoDeCarga.VencidaPorInactividad or EstadoDeCarga.VencidaPorTopeAbsoluto => EstadoDeLaSesionEnLaSolicitud.Vencida,
                    _ => EstadoDeLaSesionEnLaSolicitud.Inexistente,
                });
        }

        var credencial = await sesiones.AsegurarCredencial(valor, host.Host, contexto.RequestAborted).ConfigureAwait(false);
        contexto.EstablecerCredencial(credencial);
        return credencial.Estado switch
        {
            EstadoDeCredencial.Asegurada when credencial.Actor is { } verificado => Autenticar(contexto, verificado),
            EstadoDeCredencial.ServicioNoDisponible => await NoDisponible(contexto).ConfigureAwait(false),
            EstadoDeCredencial.SesionCerrada => NoValida(contexto, EstadoDeLaSesionEnLaSolicitud.RenovacionRechazada),
            _ => NoValida(contexto, EstadoDeLaSesionEnLaSolicitud.Inexistente),
        };
    }

    private static bool SinSesion(PathString ruta) => RutasSinSesion.Any(r => ruta.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase));

    private static Decision Anonimo(HttpContext contexto, EstadoDeLaSesionEnLaSolicitud estado)
    {
        contexto.EstablecerEstadoDeLaSesion(estado);
        return new Decision(true, ActorActual.Anonimo());
    }

    /// <summary>La cookie no sirve: se borra (una sesión cerrada o vencida nunca vuelve a usarse) y sigue como visita anónima.</summary>
    private Decision NoValida(HttpContext contexto, EstadoDeLaSesionEnLaSolicitud estado)
    {
        CookieDeSesionWeb.Borrar(contexto.Response, host.Host);
        return Anonimo(contexto, estado);
    }

    private static Decision Autenticar(HttpContext contexto, ActorVerificado verificado)
    {
        var rol = RolComoTexto(verificado.Rol);
        var reclamos = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, verificado.UsuarioId.ToString("D")),
            new(ClaimTypes.Role, rol),
        };
        if (verificado.SucursalId is { } sucursal)
        {
            reclamos.Add(new Claim("bh:sucursal", sucursal.ToString("D")));
        }

        contexto.User = new ClaimsPrincipal(new ClaimsIdentity(reclamos, "SesionWeb"));
        contexto.EstablecerEstadoDeLaSesion(EstadoDeLaSesionEnLaSolicitud.Asegurada);
        return new Decision(true, ActorActual.DeUsuario(verificado.UsuarioId, rol, verificado.SucursalId));
    }

    private async Task<Decision> NoDisponible(HttpContext contexto)
    {
        var anonimo = Anonimo(contexto, EstadoDeLaSesionEnLaSolicitud.ServicioNoDisponible);
        if (contexto.Request.Path.StartsWithSegments(RutaDeCierre, StringComparison.OrdinalIgnoreCase))
        {
            return anonimo;
        }

        contexto.Response.Headers.CacheControl = "no-store";
        if (contexto.Request.EsHtmx())
        {
            contexto.Response.StatusCode = StatusCodes.Status200OK;
            contexto.Response.Headers["HX-Redirect"] = "/no-disponible";
        }
        else if (HttpMethods.IsGet(contexto.Request.Method))
        {
            contexto.Response.StatusCode = StatusCodes.Status302Found;
            contexto.Response.Headers.Location = "/no-disponible";
        }
        else
        {
            await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.NoDisponible)).ConfigureAwait(false);
        }

        return anonimo with { Continuar = false };
    }

    internal static string RolComoTexto(RolWeb rol) => rol switch
    {
        RolWeb.Cliente => RolesDeUsuario.Cliente,
        RolWeb.Encargado => RolesDeUsuario.EncargadoDeSucursal,
        RolWeb.Logistica => RolesDeUsuario.PersonalDeLogistica,
        RolWeb.AdministradorCentral => RolesDeUsuario.AdministradorCentral,
        _ => throw new ArgumentOutOfRangeException(nameof(rol), "Una credencial verificada siempre trae el rol de una persona con cuenta."),
    };
}

public static class SesionWebExtensiones
{
    /// <summary>
    /// Agrega la sesión web al pipeline: va <b>después de <c>UseRouting</c></b> (necesita el endpoint para saber si es una consulta automática) y del tope previo, y antes
    /// de los límites generales y del antiforgery. Quien la use registra <see cref="OpcionesDelHostWeb"/>, <see cref="ModoDeArranque"/>, <see cref="OpcionesDeErrores"/>,
    /// <see cref="ISesionesWeb"/> y <see cref="IEstablecedorDeActor"/>.
    /// </summary>
    public static IApplicationBuilder UsarSesionWeb(this IApplicationBuilder app) => app.UseMiddleware<SesionWebHandler>();
}
