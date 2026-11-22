using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Domain.Web;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El borrador de la hoja de reclamación (web-base WEB-25; AC-WEB-20, AC-WEB-26): una persona con sesión que vence mientras escribe el detalle de su reclamo no lo pierde. Por REC-04 y D06 solo se
/// conserva lo que no identifica a la persona (bien contratado, monto, referencia del pedido, tipo y detalle); <b>nunca</b> el documento, el domicilio, el nombre ni el correo, ni la clave de un solo uso
/// ni el token de verificación de persona.
/// </summary>
public sealed class BorradorDeLaHojaDeReclamacionTests
{
    private static ControllerActionDescriptor Accion()
    {
        var metodo = typeof(ReclamacionesController).GetMethod(nameof(ReclamacionesController.Registrar))!;
        return new ControllerActionDescriptor
        {
            MethodInfo = metodo,
            Parameters = [.. metodo.GetParameters().Select(p => (ParameterDescriptor)new ControllerParameterDescriptor { Name = p.Name!, ParameterType = p.ParameterType, ParameterInfo = p })],
        };
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void La_accion_de_registro_declara_conservables_solo_los_campos_que_no_identifican_a_la_persona()
    {
        var campos = ProteccionContraFalsificacionFilter.CamposConservables(Accion());

        Assert.Equal(["BienContratado", "Detalle", "MontoReclamado", "PedidoReferencia", "Tipo"], campos.Order(StringComparer.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-20")]
    [InlineData("Nombre")]
    [InlineData("Documento")]
    [InlineData("Domicilio")]
    [InlineData("Correo")]
    [InlineData("ClaveDelFormulario")]
    public void Un_dato_personal_o_la_clave_de_un_solo_uso_nunca_se_conservan(string campo)
    {
        Assert.DoesNotContain(campo, ProteccionContraFalsificacionFilter.CamposConservables(Accion()));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public void La_plantilla_del_formulario_esta_en_la_lista_cerrada_y_el_borrador_se_crea_con_esos_campos()
    {
        Assert.True(PlantillasDeBorrador.EsConservable(ReclamacionesController.RutaDelFormulario));
        var campos = new Dictionary<string, string> { ["BienContratado"] = "Cien años de soledad", ["Detalle"] = "Llegó con las páginas dañadas", ["Tipo"] = "reclamo" };
        Assert.NotNull(BorradorDePantalla.Crear(Guid.NewGuid(), Guid.NewGuid(), ReclamacionesController.RutaDelFormulario, campos, DateTimeOffset.UtcNow));
    }
}
