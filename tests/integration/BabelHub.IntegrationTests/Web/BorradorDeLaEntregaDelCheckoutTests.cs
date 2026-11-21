using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Domain.Web;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El borrador de los datos de entrega del checkout (web-base WEB-25; AC-WEB-24): la sesión que vence a mitad de la acción conserva en el servidor, ligado a la persona y por 30 minutos como máximo, el contacto del despacho a domicilio
/// (nombre, celular, dirección y región) y nada más: la acción <c>POST /checkout/entrega</c> declara exactamente esos campos como conservables, la plantilla está en la lista cerrada del Dominio y un campo con aire de contraseña o código no se conserva.
/// </summary>
public sealed class BorradorDeLaEntregaDelCheckoutTests
{
    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public void La_accion_de_entrega_declara_conservables_solo_los_cuatro_campos_del_contacto()
    {
        var metodo = typeof(CheckoutController).GetMethod(nameof(CheckoutController.ElegirEntrega))!;
        var accion = new ControllerActionDescriptor
        {
            MethodInfo = metodo,
            Parameters = [.. metodo.GetParameters().Select(p => (ParameterDescriptor)new ControllerParameterDescriptor { Name = p.Name!, ParameterType = p.ParameterType, ParameterInfo = p })],
        };

        var campos = ProteccionContraFalsificacionFilter.CamposConservables(accion);

        Assert.Equal(["Direccion", "Nombre", "Region", "Telefono"], campos.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public void La_plantilla_de_la_entrega_esta_en_la_lista_cerrada_y_un_campo_de_contrasena_o_codigo_no_se_conserva()
    {
        Assert.True(PlantillasDeBorrador.EsConservable("/checkout/entrega"));
        Assert.False(PlantillasDeBorrador.EsConservable("/checkout/pago"));
        Assert.False(PlantillasDeBorrador.EsConservable("/ingreso"));

        var contacto = new Dictionary<string, string> { ["Nombre"] = "Rosa Quispe Ficticia", ["Telefono"] = "987654321", ["Direccion"] = "Jr. Ficticio 123", ["Region"] = "ayacucho" };
        var borrador = BorradorDePantalla.Crear(Guid.NewGuid(), Guid.NewGuid(), "/checkout/entrega", contacto, DateTimeOffset.UtcNow);
        Assert.NotNull(borrador);
        Assert.ThrowsAny<Exception>(() => BorradorDePantalla.Crear(Guid.NewGuid(), Guid.NewGuid(), "/checkout/entrega", new Dictionary<string, string> { ["contrasena"] = "x" }, DateTimeOffset.UtcNow));
        Assert.ThrowsAny<Exception>(() => BorradorDePantalla.Crear(Guid.NewGuid(), Guid.NewGuid(), "/checkout/pago", contacto, DateTimeOffset.UtcNow));
    }
}
