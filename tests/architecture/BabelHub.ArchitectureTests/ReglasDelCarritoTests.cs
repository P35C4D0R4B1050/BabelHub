using System.Reflection;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Reglas del módulo <c>Carrito</c> que no caben en una prueba unitaria (<c>ecommerce-carrito-checkout</c> T09): la Aplicación no referencia <c>Shared</c> (regla 6), así que sus constantes de rol se comparan
/// aquí con las de <c>RolesDeUsuario</c>.
/// </summary>
public class ReglasDelCarritoTests
{
    private static readonly Assembly Aplicacion = typeof(ICarritoDelCliente).Assembly;

    [Theory]
    [InlineData("Cliente", RolesDeUsuario.Cliente)]
    [InlineData("AdministradorCentral", RolesDeUsuario.AdministradorCentral)]
    public void Las_constantes_de_rol_de_Carrito_son_las_de_Shared(string nombre, string esperado)
    {
        var roles = Aplicacion.GetType("BabelHub.Ecommerce.Application.Carrito.Interno.RolesDelCarrito", throwOnError: true)!;
        var campo = roles.GetField(nombre, BindingFlags.Public | BindingFlags.Static)!;

        Assert.True(campo.IsLiteral, "El rol debe ser una constante.");
        Assert.Equal(esperado, (string?)campo.GetRawConstantValue());
    }
}
