namespace BabelHub.Shared.Contratos;

/// <summary>Roles de usuario que viajan en los contratos y en la credencial (IDE-04).</summary>
public static class RolesDeUsuario
{
    public const string Cliente = "cliente";

    public const string EncargadoDeSucursal = "encargado-de-sucursal";

    public const string PersonalDeLogistica = "personal-de-logistica";

    public const string AdministradorCentral = "administrador-central";

    public static readonly IReadOnlyList<string> Todos = [Cliente, EncargadoDeSucursal, PersonalDeLogistica, AdministradorCentral];
}
