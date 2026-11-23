using BabelHub.Ecommerce.Api.Web.Contacto;
using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>Los nombres de las cookies de preferencia (sin datos sensibles, D16).</summary>
public static class PreferenciasDeLaPagina
{
    public const string CookieDeSucursal = "bh-sucursal";
}

/// <summary>Los temas que la interfaz conoce; cualquier otro valor de la cookie se ignora.</summary>
public static class Temas
{
    public const string Claro = "babelhub";
    public const string Oscuro = "babelhub-dark";
    public const string Cookie = "bh-tema";

    public static string? Validar(string? valor) => valor is Claro or Oscuro ? valor : null;
}

/// <summary>
/// Arma la parte común del modelo de cada página: el tema recordado y el menú. El menú es provisional hasta que la sesión (WEB-1b) y el
/// módulo <c>Web</c> (<c>IMenuDeNavegacion</c>) lo entreguen ya filtrado por el rol y el alcance de quien mira (T15, T18).
/// </summary>
public sealed class ContextoDePagina(IHttpContextAccessor acceso, IPreferenciaDeSucursal sucursales, IMenuDeNavegacion? menu = null, DatosDeContacto? contacto = null)
{
    /// <summary>
    /// Clave de <c>HttpContext.Items</c> con la que la política de textos pendientes (T37) avisa a la página de que la persona debe aceptar
    /// los textos legales: <see cref="Completar{T}"/> la copia al modelo y los tag helpers de acciones dejan de ofrecer cambios y pagos.
    /// </summary>
    public const string ClaveTextosPendientes = "bh.textos-pendientes";

    public async Task<T> Completar<T>(T modelo, string titulo, AreaDeLaPagina area = AreaDeLaPagina.Tienda, CancellationToken cancelacion = default)
        where T : ModeloBaseDePagina
    {
        ArgumentNullException.ThrowIfNull(modelo);
        modelo.Titulo = titulo;
        modelo.Area = area;
        modelo.Tema = Temas.Validar(acceso.HttpContext?.Request.Cookies[Temas.Cookie]);
        modelo.Menu = MenuDe(area);
        modelo.ContactoDelComercio = contacto;
        modelo.TextosPendientes = acceso.HttpContext?.Items[ClaveTextosPendientes] is true;
        // La cabecera ofrece «Mi cuenta» y «Cerrar sesión» en toda página de quien tiene sesión, no solo en las que lo piden a mano (hallazgo del E2E de la sesión).
        modelo.HaySesion |= acceso.HttpContext?.ActorDeLaSolicitud() is not null;
        if (area == AreaDeLaPagina.Tienda)
        {
            // La sucursal preferida solo se usa si está en la lista de sucursales; sin lista conocida no hay selector (WEB-16, D16).
            var elegibles = await sucursales.Resolver(acceso.HttpContext?.Request.Cookies[PreferenciasDeLaPagina.CookieDeSucursal], cancelacion).ConfigureAwait(false);
            modelo.Sucursales = [.. elegibles.Sucursales.Select(s => new SucursalElegible(s.Codigo, s.Nombre))];
            modelo.SucursalPreferida = elegibles.CodigoElegido;
        }

        return modelo;
    }

    /// <summary>El menú del área para quien mira (WEB-02): lo decide el módulo <c>Web</c> por el rol de la sesión (visitante si no hay) y, en una sucursal, por el código de la ruta; sin el módulo registrado no hay menú.</summary>
    private List<ItemDeMenu> MenuDe(AreaDeLaPagina area)
    {
        if (menu is null)
        {
            return [];
        }

        var rol = acceso.HttpContext?.ActorDeLaSolicitud()?.Rol ?? RolWeb.Visitante;
        var areaWeb = area switch
        {
            AreaDeLaPagina.Sucursal => AreaWeb.Sucursal,
            AreaDeLaPagina.Admin => AreaWeb.Administracion,
            _ => AreaWeb.Tienda,
        };
        var codigo = acceso.HttpContext?.Request.RouteValues["codigo"] as string;
        return [.. menu.Resolver(rol, areaWeb, codigo).Select(o => new ItemDeMenu(o.Texto, o.Ruta, o.Icono))];
    }
}

public static class InterfazWebExtensiones
{
    /// <summary>Registra lo común de la interfaz: acceso al contexto HTTP y el armado de las páginas.</summary>
    public static IServiceCollection AgregarInterfazWeb(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddHttpContextAccessor();
        servicios.AddScoped<ContextoDePagina>();
        return servicios;
    }
}

public static class IngresoWebExtensiones
{
    /// <summary>Registra el cierre del ingreso (sesión del servidor, cookies y fusión del carrito): necesita <c>ISesionesWeb</c>, el host de la web, el establecedor de actor y Carrito, que componen el anfitrión.</summary>
    public static IServiceCollection AgregarIngresoWeb(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddScoped<CierreDelIngresoWeb>();
        return servicios;
    }
}
