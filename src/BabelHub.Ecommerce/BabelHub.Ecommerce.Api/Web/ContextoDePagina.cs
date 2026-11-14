namespace BabelHub.Ecommerce.Api.Web;

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
public sealed class ContextoDePagina(IHttpContextAccessor acceso)
{
    public T Completar<T>(T modelo, string titulo, AreaDeLaPagina area = AreaDeLaPagina.Tienda)
        where T : ModeloBaseDePagina
    {
        ArgumentNullException.ThrowIfNull(modelo);
        modelo.Titulo = titulo;
        modelo.Area = area;
        modelo.Tema = Temas.Validar(acceso.HttpContext?.Request.Cookies[Temas.Cookie]);
        modelo.Menu = MenuProvisional(area);
        return modelo;
    }

    private static List<ItemDeMenu> MenuProvisional(AreaDeLaPagina area) => area switch
    {
        AreaDeLaPagina.Tienda =>
        [
            new ItemDeMenu("Catálogo", "/catalogo", "libro"),
            new ItemDeMenu("Carrito", "/carrito", "carrito"),
        ],
        _ => [],
    };
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
