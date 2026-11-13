using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class ValidacionDeEntradaTests
{
    private sealed record CambiarCantidad(string? Cantidad, string? Nota);

    private sealed class ValidadorDeCantidad : IValidador<CambiarCantidad>
    {
        private static readonly Regex SoloLetras = new("^[A-Za-z ]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public ResultadoDeValidacion Validar(CambiarCantidad entrada) =>
            new ConstructorDeValidacion()
                .Entero("cantidad", entrada.Cantidad, 1, 99)
                .Texto("nota", entrada.Nota, 1, 20, SoloLetras)
                .Resultado();
    }

    private sealed class CasoDeUsoEspia : ICasoDeUso<CambiarCantidad, string>
    {
        public int Ejecuciones { get; private set; }

        public Task<Resultado<string>> Ejecutar(CambiarCantidad entrada, CancellationToken cancelacion = default)
        {
            Ejecuciones++;
            return Task.FromResult(Resultado<string>.Exito("hecho"));
        }
    }

    private static (ValidacionDeEntrada<CambiarCantidad, string> Decorado, CasoDeUsoEspia Espia) Armar()
    {
        var espia = new CasoDeUsoEspia();
        return (new ValidacionDeEntrada<CambiarCantidad, string>(espia, new ValidadorDeCantidad()), espia);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public async Task Ejecutar_ConUnTextoEnUnCampoNumerico_RechazaYNoProcesa()
    {
        var (decorado, espia) = Armar();

        var resultado = await decorado.Ejecutar(new CambiarCantidad("diez", "ok"));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("cantidad", ConstructorDeValidacion.NoEsNumero), resultado.Error.Errores);
        Assert.Equal(0, espia.Ejecuciones);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-01")]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("-5")]
    [InlineData("99999999999999999999")]
    public async Task Ejecutar_ConUnNumeroFueraDeRango_RechazaYNoProcesa(string cantidad)
    {
        var (decorado, espia) = Armar();

        var resultado = await decorado.Ejecutar(new CambiarCantidad(cantidad, "ok"));

        Assert.False(resultado.EsExito);
        Assert.Equal(0, espia.Ejecuciones);
        Assert.Contains(resultado.Error!.Errores, e => e.Campo == "cantidad");
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public async Task Ejecutar_ConUnaCadenaDemasiadoLarga_RechazaYNoProcesa()
    {
        var (decorado, espia) = Armar();

        var resultado = await decorado.Ejecutar(new CambiarCantidad("5", new string('a', 21)));

        Assert.False(resultado.EsExito);
        Assert.Equal(new ErrorDeCampo("nota", ConstructorDeValidacion.DemasiadoLargo), Assert.Single(resultado.Error!.Errores));
        Assert.Equal(0, espia.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public async Task Ejecutar_ConUnFormatoInvalido_RechazaYNoProcesa()
    {
        var (decorado, espia) = Armar();

        var resultado = await decorado.Ejecutar(new CambiarCantidad("5", "<script>"));

        Assert.False(resultado.EsExito);
        Assert.Equal(new ErrorDeCampo("nota", ConstructorDeValidacion.FormatoInvalido), Assert.Single(resultado.Error!.Errores));
        Assert.Equal(0, espia.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public async Task Ejecutar_ConVariosCamposInvalidos_ReportaTodosSinEcoDelValor()
    {
        var (decorado, _) = Armar();

        var resultado = await decorado.Ejecutar(new CambiarCantidad("secreto-1234", null));

        Assert.Equal(["cantidad", "nota"], resultado.Error!.Errores.Select(e => e.Campo));
        Assert.DoesNotContain(resultado.Error.Errores, e => e.Clave.Contains("secreto", StringComparison.Ordinal));
        Assert.Equal(ValidacionDeEntrada<CambiarCantidad, string>.ClaveDeValidacion, resultado.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public async Task Ejecutar_ConUnaEntradaValida_EjecutaElCasoDeUsoUnaVez()
    {
        var (decorado, espia) = Armar();

        var resultado = await decorado.Ejecutar(new CambiarCantidad("5", "ok"));

        Assert.True(resultado.EsExito);
        Assert.Equal("hecho", resultado.Valor);
        Assert.Equal(1, espia.Ejecuciones);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-01")]
    [InlineData(null, ConstructorDeValidacion.Obligatorio)]
    [InlineData("  ", ConstructorDeValidacion.Obligatorio)]
    public void Entero_SinValor_EsObligatorio(string? texto, string clave)
    {
        var resultado = new ConstructorDeValidacion().Entero("cantidad", texto, 1, 5).Resultado();

        Assert.Equal(new ErrorDeCampo("cantidad", clave), Assert.Single(resultado.Errores));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public void Texto_ConMenosDelMinimo_EsDemasiadoCorto()
    {
        var resultado = new ConstructorDeValidacion().Texto("nombre", "ab", 3, 10).Resultado();

        Assert.Equal(ConstructorDeValidacion.DemasiadoCorto, Assert.Single(resultado.Errores).Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public void Texto_ConUnaExpresionQueSeExcedeDeTiempo_SeRechazaSinLanzar()
    {
        var catastrofica = new Regex("^(a+)+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20));

        var resultado = new ConstructorDeValidacion().Texto("codigo", new string('a', 40) + "!", 1, 100, catastrofica).Resultado();

        Assert.False(resultado.EsValido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-01")]
    public void Rango_ConValoresEnLosLimites_EsValido()
    {
        var resultado = new ConstructorDeValidacion()
            .Rango("minimo", 1, 1, 99)
            .Rango("maximo", 99, 1, 99)
            .Rango("precio", 10.5m, 0m, 100m)
            .Resultado();

        Assert.True(resultado.EsValido);
    }

    [Fact]
    public void Resultado_DeUnFallo_NoTieneValorYUnExitoNoTieneError()
    {
        var fallo = Resultado<int>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "sin-stock"));

        Assert.False(fallo.EsExito);
        Assert.Throws<InvalidOperationException>(() => fallo.Valor);
        Assert.Null(Resultado<int>.Exito(3).Error);
        Assert.Empty(fallo.Error!.Errores);
        Assert.Throws<ArgumentException>(() => new ErrorDeAplicacion(CategoriaDeError.Validacion, " "));
        Assert.Throws<ArgumentNullException>(() => Resultado<int>.Fallo(null!));
    }
}
