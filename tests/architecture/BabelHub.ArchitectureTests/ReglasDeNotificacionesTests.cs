using System.Reflection;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Reglas del módulo <c>Notificaciones</c> que no caben en una prueba unitaria (<c>transversal-notificaciones</c> T07): la Aplicación no referencia <c>Shared</c> (regla 6), así que su constante del rol de la
/// administración central se compara aquí con <c>RolesDeUsuario.AdministradorCentral</c>.
/// </summary>
public class ReglasDeNotificacionesTests
{
    [Fact]
    public void La_constante_de_rol_de_la_desactivacion_de_numeros_es_la_de_Shared()
    {
        var campo = typeof(DesactivarAvisosDeUnNumero).GetField(nameof(DesactivarAvisosDeUnNumero.RolDeAdministracionCentral), BindingFlags.Public | BindingFlags.Static)!;

        Assert.True(campo.IsLiteral, "El rol debe ser una constante.");
        Assert.Equal(RolesDeUsuario.AdministradorCentral, (string?)campo.GetRawConstantValue());
        Assert.NotNull(typeof(IDesactivacionDeNumeros));
    }
}
