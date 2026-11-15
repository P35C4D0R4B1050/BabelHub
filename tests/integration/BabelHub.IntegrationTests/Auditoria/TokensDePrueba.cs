namespace BabelHub.IntegrationTests.Auditoria;


/// <summary>El token de usuario de las pruebas (D10: solo aquí): <c>test.rol.usuario.sucursal</c>. En producción lo verifica Identidad (T41) y ninguna cabecera de actor vale.</summary>
public static class TokensDePrueba
{
    public static string Crear(string rol, Guid usuario, Guid? sucursal = null) => $"test.{rol}.{usuario:D}.{sucursal?.ToString("D") ?? "-"}";

    public static (string Rol, Guid Usuario, Guid? Sucursal)? Leer(string? token)
    {
        var partes = (token ?? string.Empty).Split('.');
        return partes is ["test", var rol, var usuario, var sucursal] && Guid.TryParse(usuario, out var id)
            ? (rol, id, sucursal == "-" ? null : Guid.Parse(sucursal))
            : null;
    }
}

