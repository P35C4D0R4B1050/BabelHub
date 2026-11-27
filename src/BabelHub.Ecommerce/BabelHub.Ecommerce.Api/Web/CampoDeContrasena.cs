namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Un campo de contraseña de la interfaz (web-acceso): lo dibuja <c>_CampoDeContrasena</c>, el único lugar que escribe un <c>input</c> de este tipo, para que todos lleven las ayudas de escritura desactivadas (ACC-17) y el
/// botón de ver u ocultar que agrega el guion (ACC-07). <paramref name="NombreAccesible"/> es cómo lo nombra ese botón; <paramref name="EsNueva"/> marca el primer campo de una contraseña nueva (ayuda y barra, ACC-01, ACC-03) y
/// <paramref name="RepiteA"/> el nombre del campo que repite (aviso de coincidencia, ACC-06).
/// </summary>
public sealed record CampoDeContrasena(string Nombre, string Etiqueta, string NombreAccesible, string Autocompletar, bool EsNueva = false, string? RepiteA = null)
{
    /// <summary>La contraseña con la que ya se ingresa.</summary>
    public static CampoDeContrasena Actual(string nombre, string etiqueta = "Contraseña actual") => new(nombre, etiqueta, "la contraseña actual", "current-password");

    /// <summary>El primer campo de una contraseña nueva.</summary>
    public static CampoDeContrasena Nueva(string nombre, string etiqueta, string nombreAccesible = "la contraseña nueva") => new(nombre, etiqueta, nombreAccesible, "new-password", EsNueva: true);

    /// <summary>La repetición de la contraseña nueva que está en <paramref name="repiteA"/>.</summary>
    public static CampoDeContrasena Repeticion(string nombre, string repiteA) => new(nombre, "Repite la contraseña nueva", "la repetición de la contraseña nueva", "new-password", RepiteA: repiteA);
}
