using System.Reflection;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Reglas del módulo <c>Carrito</c> que no caben en una prueba unitaria (<c>ecommerce-carrito-checkout</c> T09): la Aplicación no referencia <c>Shared</c> (regla 6), así que su constante del rol de cliente se compara
/// aquí con <c>RolesDeUsuario.Cliente</c>.
/// </summary>
public class ReglasDelCarritoTests
{
    private static readonly Assembly Aplicacion = typeof(ICarritoDelCliente).Assembly;

    [Fact]
    public void La_constante_del_rol_de_cliente_de_Carrito_es_la_de_Shared()
    {
        var roles = Aplicacion.GetType("BabelHub.Ecommerce.Application.Carrito.Interno.RolesDelCarrito", throwOnError: true)!;
        var campo = roles.GetField("Cliente", BindingFlags.Public | BindingFlags.Static)!;

        Assert.True(campo.IsLiteral, "El rol debe ser una constante.");
        Assert.Equal(RolesDeUsuario.Cliente, (string?)campo.GetRawConstantValue());
    }
}
