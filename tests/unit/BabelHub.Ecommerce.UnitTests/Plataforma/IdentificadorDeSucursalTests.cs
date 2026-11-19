using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

/// <summary>La equivalencia canónica código de sucursal ↔ identificador (D20 de <c>docs/03/08</c>): valores fijos que no pueden cambiar sin romper el alcance del encargado en Pedidos e Identidad.</summary>
public sealed class IdentificadorDeSucursalTests
{
    [Theory]
    [InlineData("ayacucho", "f1bbf775-a5fd-d22c-19c7-c0e9abe9a86c")]
    [InlineData("lima", "7cd06781-c66a-48f3-d73b-265d20f181fc")]
    [InlineData("central", "06d078f4-a8ef-9632-652f-e6f952ea9370")]
    [InlineData("sede-1", "e2944a4a-1642-bdda-ce53-db09a292c62c")]
    public void ElIdentificadorDeUnCodigo_EsSiempreElMismoValorFijo(string codigo, string esperado)
    {
        Assert.Equal(Guid.Parse(esperado), IdentificadorDeSucursal.DesdeCodigo(codigo));
    }

    [Theory]
    [InlineData("AYACUCHO")]
    [InlineData("  Ayacucho ")]
    public void NoDistingueMayusculasNiEspaciosEnLosExtremos(string escrito)
    {
        Assert.Equal(IdentificadorDeSucursal.DesdeCodigo("ayacucho"), IdentificadorDeSucursal.DesdeCodigo(escrito));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("lima central")]
    [InlineData("lima/central")]
    [InlineData("lima;")]
    [InlineData("a_b")]
    [InlineData("ñandú")]
    [InlineData("Kusco")] // el signo Kelvin se pliega a «k» en minúsculas: no es un código de sucursal
    public void UnCodigoSinLaFormaDeUnCodigoDeSucursal_NoSeResuelve(string? codigo)
    {
        Assert.Null(IdentificadorDeSucursal.DesdeCodigo(codigo));
    }

    [Fact]
    public void ElLargoMaximoEs40_YDosCodigosDistintosDanIdentificadoresDistintos()
    {
        Assert.NotNull(IdentificadorDeSucursal.DesdeCodigo(new string('a', 40)));
        Assert.Null(IdentificadorDeSucursal.DesdeCodigo(new string('a', 41)));
        Assert.NotEqual(IdentificadorDeSucursal.DesdeCodigo("lima"), IdentificadorDeSucursal.DesdeCodigo("lima2"));
    }
}
