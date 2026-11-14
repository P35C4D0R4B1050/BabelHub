namespace BabelHub.Ecommerce.Api.Web;

/// <summary>El área de la interfaz a la que pertenece una página (WEB-02). La Presentación solo la usa para pintar; qué ve cada rol lo decide el módulo <c>Web</c>.</summary>
public enum AreaDeLaPagina
{
    Tienda,
    Sucursal,
    Admin,
}

/// <summary>Una opción del menú ya resuelta para la persona que mira la página: la Presentación no decide qué se le permite.</summary>
public sealed record ItemDeMenu(string Texto, string Ruta, string Icono, bool Activo = false);

/// <summary>Una sucursal que la persona puede elegir como preferida (WEB-16).</summary>
public sealed record SucursalElegible(string Codigo, string Nombre);

/// <summary>
/// Modelo base de toda página (layout, T24 y T25). Lo que el layout pinta sale de aquí: el área, el menú ya filtrado por el rol, el tema
/// elegido, el selector de sucursal y el indicador de textos legales pendientes (WEB-42), que los tag helpers de acciones respetan.
/// </summary>
public class ModeloBaseDePagina
{
    public string Titulo { get; set; } = "BabelHub";

    public AreaDeLaPagina Area { get; set; } = AreaDeLaPagina.Tienda;

    public IReadOnlyList<ItemDeMenu> Menu { get; set; } = [];

    /// <summary>El tema elegido por la persona (<c>babelhub</c> o <c>babelhub-dark</c>); nulo cuando no eligió y manda la preferencia del dispositivo (WEB-09).</summary>
    public string? Tema { get; set; }

    public bool HaySesion { get; set; }

    /// <summary>Con los textos legales pendientes la página no ofrece acciones que cambien datos ni pagar (WEB-42, T25 y T37).</summary>
    public bool TextosPendientes { get; set; }

    public IReadOnlyList<SucursalElegible> Sucursales { get; set; } = [];

    public string? SucursalPreferida { get; set; }
}
