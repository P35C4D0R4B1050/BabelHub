using BabelHub.Inventario.Application.Fuentes;

namespace BabelHub.Inventario.UnitTests.Fuentes;

public sealed class ModelosDelContratoDeFuentesTests
{
    private static readonly Guid Lima = Guid.NewGuid();
    private static readonly Guid Cusco = Guid.NewGuid();
    private static readonly Guid Libro = Guid.NewGuid();

    [Fact]
    public void ResultadoDeAplicacion_Exito_TraeLasCantidadesAnteriorYPosterior()
    {
        var resultado = ResultadoDeAplicacion.Exito(new CantidadesDeStock(9, 1), new CantidadesDeStock(9, 2));

        Assert.True(resultado.Aplicado);
        Assert.Equal(ResultadoDeMovimiento.Aplicado, resultado.Resultado);
        Assert.Equal(new CantidadesDeStock(9, 1), resultado.Anterior);
        Assert.Equal(new CantidadesDeStock(9, 2), resultado.Posterior);
    }

    [Theory]
    [InlineData(ResultadoDeMovimiento.NoAplicable)]
    [InlineData(ResultadoDeMovimiento.DeniegadoPorAlcance)]
    [InlineData(ResultadoDeMovimiento.EdicionInexistenteONoImpresa)]
    public void ResultadoDeAplicacion_Fallo_NoTraeCantidadesYNoEsAplicado(ResultadoDeMovimiento motivo)
    {
        var resultado = ResultadoDeAplicacion.Fallo(motivo);

        Assert.False(resultado.Aplicado);
        Assert.Equal(motivo, resultado.Resultado);
        Assert.Null(resultado.Anterior);
        Assert.Null(resultado.Posterior);
    }

    [Fact]
    public void ResultadoDeAplicacion_FalloConAplicado_Lanza()
    {
        Assert.Throws<ArgumentException>(() => ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.Aplicado));
    }

    [Fact]
    public void BloqueoDeStock_Fila_DevuelveLaPareja()
    {
        var bloqueo = BloqueoDeStock.Con([new ExistenciaBloqueada(Lima, Libro, 9, 1), new ExistenciaBloqueada(Cusco, Libro, 0, 0)]);

        Assert.True(bloqueo.EstaBloqueado);
        Assert.Equal(9, bloqueo.Fila(Lima, Libro).Fisico);
        Assert.Equal(0, bloqueo.Fila(Cusco, Libro).Reservado);
        Assert.Throws<InvalidOperationException>(() => bloqueo.Fila(Lima, Guid.NewGuid()));
    }

    [Fact]
    public void BloqueoDeStock_Rechazado_NoTieneFilas()
    {
        var bloqueo = BloqueoDeStock.Rechazado();

        Assert.False(bloqueo.EstaBloqueado);
        Assert.Equal(ResultadoDeBloqueo.EdicionInexistenteONoImpresa, bloqueo.Resultado);
        Assert.Empty(bloqueo.Filas);
    }

    [Fact]
    public void FichaDeEdicionRegistrada_EsImpresa_SoloConFormatoImpreso()
    {
        Assert.True(new FichaDeEdicionRegistrada(Libro, FormatoDeEdicionRegistrado.Impreso).EsImpresa);
        Assert.False(new FichaDeEdicionRegistrada(Libro, FormatoDeEdicionRegistrado.Digital).EsImpresa);
    }

    [Fact]
    public void ElEstadoDeExistencia_TieneLosTresEstadosDeFue18()
    {
        Assert.Equal(["SinStock", "Bajo", "Normal"], Enum.GetNames<EstadoDeExistencia>());
    }
}
