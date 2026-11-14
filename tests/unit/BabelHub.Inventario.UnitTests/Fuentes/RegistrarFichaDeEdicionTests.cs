using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T12: la ficha de una edición (FUE-07, FUE-15, D03): alta o actualización idempotente, también para digitales, y rechazo de pasar a digital una edición con stock.</summary>
public sealed class RegistrarFichaDeEdicionTests
{
    private static RegistrarFichaDeEdicion Caso(EntornoDeFuentes e) => new(e.Autorizador, new ValidadorDeRegistrarFicha(), e.Fichas, e.Existencias);

    [Theory]
    [Trait("Criterio", "AC-FUE-05")]
    [InlineData(FormatoDeEdicionRegistrado.Impreso)]
    [InlineData(FormatoDeEdicionRegistrado.Digital)]
    public async Task Registrar_UnaEdicionNueva_ImpresaODigital_QuedaConSuFormato(FormatoDeEdicionRegistrado formato)
    {
        var entorno = new EntornoDeFuentes();
        var edicion = Guid.NewGuid();

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(edicion, formato));

        Assert.Equal(new FichaDeEdicionGuardada(edicion, formato, SeEscribio: true), resultado.Valor);
        Assert.Equal(formato == FormatoDeEdicionRegistrado.Impreso, entorno.Fichas.Fichas[edicion].PuedeTenerStock);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Registrar_ElMismoFormatoOtraVez_EsIdempotenteYNoEscribe()
    {
        var entorno = new EntornoDeFuentes();
        var ficha = entorno.Fichas.Agregar(FormatoDeEdicion.Impreso);

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(ficha.EdicionId, FormatoDeEdicionRegistrado.Impreso));

        Assert.True(resultado.EsExito);
        Assert.False(resultado.Valor.SeEscribio);
        Assert.Empty(entorno.Fichas.Guardadas);
        Assert.DoesNotContain("existencias.HayExistencias", entorno.Bitacora.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Registrar_CambiarAUnaEdicionImpresaSinStockADigital_SeActualiza()
    {
        var entorno = new EntornoDeFuentes();
        var ficha = entorno.Fichas.Agregar(FormatoDeEdicion.Impreso);
        entorno.Existencias.Poner(entorno.Lima.Id, ficha.EdicionId, 0, 0);

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(ficha.EdicionId, FormatoDeEdicionRegistrado.Digital));

        Assert.True(resultado.Valor.SeEscribio);
        Assert.False(entorno.Fichas.Fichas[ficha.EdicionId].PuedeTenerStock);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-05")]
    [InlineData(3, 0)]
    [InlineData(5, 2)]
    public async Task Registrar_PasarADigitalUnaEdicionConStock_EsConflictoYNoCambiaLaFicha(int fisico, int reservado)
    {
        var entorno = new EntornoDeFuentes();
        var ficha = entorno.Fichas.Agregar(FormatoDeEdicion.Impreso);
        entorno.Existencias.Poner(entorno.Cusco.Id, ficha.EdicionId, fisico, reservado);

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(ficha.EdicionId, FormatoDeEdicionRegistrado.Digital));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("formato-en-conflicto", resultado.Error.Clave);
        Assert.True(entorno.Fichas.Fichas[ficha.EdicionId].PuedeTenerStock);
        Assert.Empty(entorno.Fichas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Registrar_SiLaClaveCompuestaDeFormatoRechazaElCambio_EsConflicto()
    {
        var entorno = new EntornoDeFuentes();
        var ficha = entorno.Fichas.Agregar(FormatoDeEdicion.Impreso);
        entorno.Fichas.RespuestaForzada = ResultadoDeGuardadoDeFicha.FormatoEnConflicto;

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(ficha.EdicionId, FormatoDeEdicionRegistrado.Digital));

        Assert.Equal("formato-en-conflicto", resultado.Error!.Clave);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [MemberData(nameof(GestionDeFuentesTests.ActoresSinPermisoDeAdministracion), MemberType = typeof(GestionDeFuentesTests))]
    public async Task Registrar_ConCualquierOtroRol_SeRechazaConSuEventoYSinTocarLaFicha(string etiqueta, ActorDePrueba actor, CategoriaDeError categoria, string motivo)
    {
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(Guid.NewGuid(), FormatoDeEdicionRegistrado.Impreso));

        Assert.Equal(categoria, resultado.Error!.Categoria);
        Assert.Equal([motivo], entorno.MotivosDeDenegacion);
        Assert.Empty(entorno.Fichas.Guardadas);
        Assert.NotNull(etiqueta);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Registrar_ConUnaEdicionVaciaOUnFormatoDesconocido_DevuelveLosCampos()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(Guid.Empty, (FormatoDeEdicionRegistrado)7));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(["edicionId", "formato"], resultado.Error.Errores.Select(e => e.Campo));
        Assert.Empty(entorno.Fichas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task LaFichaRegistrada_SeLeePorIFichasDeEdiciones_YSinFichaLaEdicionNoExiste()
    {
        var entorno = new EntornoDeFuentes();
        var edicion = Guid.NewGuid();
        await Caso(entorno).Ejecutar(new RegistrarFichaDeEdicionEntrada(edicion, FormatoDeEdicionRegistrado.Digital));
        IFichasDeEdiciones lectura = new DirectorioDeFuentes(entorno.Lectura, entorno.Fuentes, entorno.Rutas, entorno.Fichas);

        var registrada = await lectura.PorId(edicion);
        var desconocida = await lectura.PorId(Guid.NewGuid());

        Assert.Equal(new FichaDeEdicionRegistrada(edicion, FormatoDeEdicionRegistrado.Digital), registrada);
        Assert.Null(desconocida);
    }
}
