namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>El estado de una credencial de acceso al verificarla (identidad-seguridad T41; IDE-10, IDE-61).</summary>
public enum EstadoDeLaCredencial
{
    Valida,
    Vencida,

    /// <summary>La firma, el <c>kid</c> (revocado o desconocido), la audiencia, el tipo o el formato no son válidos.</summary>
    Invalida,

    /// <summary>No hay una copia de las claves de menos de 5 minutos (la réplica no responde): se rechaza toda credencial hasta renovarla (IDE-61, AC-IDE-64).</summary>
    SinClavesVigentes,
}

/// <summary>Lo que el Inventario sabe de una credencial válida: el usuario, su rol (nombre de <c>Shared</c>) y su sucursal si es encargado. Nada más sale de la credencial.</summary>
public sealed record CredencialLeida(EstadoDeLaCredencial Estado, Guid? UsuarioId, string? Rol, Guid? SucursalId)
{
    public bool EsValida => Estado == EstadoDeLaCredencial.Valida;
}

/// <summary>
/// El verificador de la credencial de acceso del Inventario (IDE-10, IDE-35): comprueba firma, vigencia, audiencia y formato <b>sin llamar a Identidad</b> en cada solicitud, solo con la clave pública y las revocaciones que
/// renueva cada 5 minutos como máximo. El Inventario no puede emitir credenciales: este puerto solo lee.
/// </summary>
public interface IVerificadorDeCredenciales
{
    CredencialLeida Verificar(string? credencial, DateTimeOffset ahora);
}
