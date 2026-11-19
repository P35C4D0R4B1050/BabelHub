using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Shared.Contratos;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// El puerto de la Web para verificar la credencial (<c>IVerificadorDeCredencial</c>) sobre el verificador ES256 de Identidad (T32): la misma verificación que hace la Api. Vive en la composición porque une dos módulos
/// (Web e Identidad) sin que ninguno referencie el interior del otro.
/// </summary>
public sealed class VerificadorDeCredencialDeLaWeb(Es256VerificadorDeCredenciales verificador) : IVerificadorDeCredencial
{
    public VerificacionDeCredencial Verificar(string credencial, DateTimeOffset ahora)
    {
        var leida = verificador.Verificar(credencial, ahora);
        return leida.Estado switch
        {
            EstadoDeLaCredencial.Valida when Rol(leida.Rol) is { } rol && leida.UsuarioId is { } usuario => new VerificacionDeCredencial(EstadoDeVerificacion.Valida, new ActorVerificado(usuario, rol, leida.SucursalId)),
            EstadoDeLaCredencial.Vencida => new VerificacionDeCredencial(EstadoDeVerificacion.Vencida, null),
            EstadoDeLaCredencial.SinClavesVigentes => new VerificacionDeCredencial(EstadoDeVerificacion.SinClavesVigentes, null),
            _ => new VerificacionDeCredencial(EstadoDeVerificacion.Invalida, null),
        };
    }

    private static RolWeb? Rol(string? nombre) => nombre switch
    {
        RolesDeUsuario.Cliente => RolWeb.Cliente,
        RolesDeUsuario.EncargadoDeSucursal => RolWeb.Encargado,
        RolesDeUsuario.PersonalDeLogistica => RolWeb.Logistica,
        RolesDeUsuario.AdministradorCentral => RolWeb.AdministradorCentral,
        _ => null,
    };
}
