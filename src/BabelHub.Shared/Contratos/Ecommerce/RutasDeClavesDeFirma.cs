namespace BabelHub.Shared.Contratos.Ecommerce;

/// <summary>Las rutas de la publicación de claves y de su administración (identidad-seguridad IDE-35, IDE-61, IDE-85); solo constantes.</summary>
public static class RutasDeClavesDeFirma
{
    public const string Jwks = "/.well-known/jwks.json";

    public const string Revocaciones = "/.well-known/revocaciones";

    public const string Rotar = RutasDeServicio.Ecommerce + "/identidad/admin/claves/rotar";

    public const string Comprometida = RutasDeServicio.Ecommerce + "/identidad/admin/claves/comprometida";
}
