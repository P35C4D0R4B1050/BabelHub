using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// La raíz del host de gestión (<c>GET /</c>; WEB-35, O-5): quien no tiene sesión va al ingreso y quien la tiene, a lo que su rol usa (<see cref="CierreDelIngresoWeb.DestinoPorRol"/>). La tienda tiene su portada (<c>InicioController</c>), así que esto
/// <b>solo se mapea en el host de gestión</b>. Es un endpoint y no un controlador para que nunca compita con la portada. No decide permisos: el destino aplica los suyos, y quien tiene un paso del ingreso pendiente lo ve en el propio ingreso (WEB-34).
/// </summary>
public static class InicioDeLaGestion
{
    public const string Ruta = "/";

    public static IEndpointRouteBuilder MapearElInicioDeLaGestion(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        rutas.MapGet(Ruta, (HttpContext contexto) =>
        {
            if (contexto.ActorDeLaSolicitud() is not { } actor)
            {
                // Sin sesión (o con una vencida o rechazada, y entonces con el navegador limpio): al ingreso.
                contexto.IrAlIngreso();
                return Results.Empty;
            }

            contexto.Response.Headers.CacheControl = "no-store";
            return Results.LocalRedirect(Destino(actor.Rol));
        }).ExcludeFromDescription();
        return rutas;
    }

    /// <summary>Solo el personal tiene sesión en este host (WEB-28); si apareciera otro rol no se le manda a la raíz (sería un bucle) sino a «sin permiso».</summary>
    public static string Destino(RolWeb rol) => rol is RolWeb.Encargado or RolWeb.Logistica or RolWeb.AdministradorCentral
        ? CierreDelIngresoWeb.DestinoPorRol(rol)
        : RespuestasDeSesion.RutaSinPermiso;
}
