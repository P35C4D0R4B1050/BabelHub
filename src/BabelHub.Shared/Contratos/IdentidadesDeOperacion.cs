namespace BabelHub.Shared.Contratos;

/// <summary>
/// Los autores de operación: identificadores constantes de quienes ejecutan una orden de operación del servicio sin ser una cuenta, una sesión ni un rol de Identidad (transversal-carga-inicial,
/// D02). Solo las órdenes de operación (<c>cargar-catalogo</c> y <c>cargar-stock</c>) los construyen como actor; el guardia de credencial de las dos Api trata como anónima una credencial
/// verificada cuyo <c>sub</c> sea uno de ellos, y una prueba de arquitectura exige que ningún otro tipo de <c>src/</c> los use.
/// </summary>
public static class IdentidadesDeOperacion
{
    /// <summary>La identidad «carga inicial» (rol de administración central, sin credencial): la que audita las altas de la carga inicial del catálogo y del inventario. Identidad genera identificadores aleatorios, así que nunca coincide con una cuenta real.</summary>
    public const string CargaInicial = "ca51a111-0000-4000-8000-00000000ca11";
}
