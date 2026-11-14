using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T13: la disponibilidad consolidada y por fuente (FUE-05, FUE-07, FUE-15) y la lista de sucursales.</summary>
public sealed class ConsultarDisponibilidadTests
{
    private static ConsultarDisponibilidad Caso(EntornoDeFuentes e) =>
        new(
            e.Autorizador,
            new ValidadorDeConsultarDisponibilidad(),
            e.Lectura,
            e.Fichas,
            new ConsultaDeExistencias(e.Lectura, e.Fuentes, e.Existencias, e.Reservas),
            e.Reloj);

    private static EntornoDeFuentes ConIntegracion() => new(ActorDePrueba.DeIntegracion("e-commerce"));

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public async Task Consultar_ElTotalDeLaRedYElDesglosePorFuente()
    {
        var entorno = ConIntegracion();
        var libro = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Lima.Id, libro, 8, 0);
        entorno.Existencias.Poner(entorno.Cusco.Id, libro, 3, 0);

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        var edicion = Assert.Single(resultado.Valor.Ediciones);
        Assert.Equal(11, edicion.VendibleEnLaRed);
        Assert.Equal(
            [("ayacucho", 0, EstadoDeExistencia.SinStock), ("cusco", 3, EstadoDeExistencia.Normal), ("lima", 8, EstadoDeExistencia.Normal)],
            edicion.PorFuente.Select(f => (f.CodigoDeFuente, f.Vendible, f.Estado)));
        Assert.Equal(EntornoDeFuentes.Ahora, resultado.Valor.ConsultadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Consultar_LasVencidasSinProcesarNoDescuentan_YElVendibleNuncaEsNegativo()
    {
        var entorno = ConIntegracion();
        var llena = entorno.Impresa();
        var conVencidas = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Lima.Id, llena, 5, 5);
        entorno.Existencias.Poner(entorno.Lima.Id, conVencidas, 5, 5);
        entorno.Reservas.Vencidas.Add(new UnidadesVencidas(entorno.Lima.Id, conVencidas, 2));

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([llena, conVencidas]));

        Assert.Equal([0, 2], resultado.Valor.Ediciones.Select(e => e.VendibleEnLaRed));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task Consultar_UnaFuenteNuevaSinFilasAparece_ConStockCero()
    {
        var entorno = ConIntegracion();
        var libro = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Lima.Id, libro, 4, 0);
        var sexta = entorno.Fuentes.Agregar("piura", "piura");

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        var piura = Assert.Single(resultado.Valor.Ediciones[0].PorFuente, f => f.CodigoDeFuente == sexta.Codigo);
        Assert.Equal((0, EstadoDeExistencia.SinStock), (piura.Vendible, piura.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Consultar_UnaEdicionDigital_SeSeparaComoNoImpresaYNoTieneDisponibilidad()
    {
        var entorno = ConIntegracion();
        var digital = entorno.Fichas.Agregar(FormatoDeEdicion.Digital).EdicionId;
        var impresa = entorno.Impresa();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([digital, impresa]));

        Assert.Equal([digital], resultado.Valor.NoImpresas);
        Assert.Equal([impresa], resultado.Valor.Ediciones.Select(e => e.EdicionId));
        Assert.Empty(resultado.Valor.NoRegistradas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task Consultar_UnaEdicionSinFicha_VaComoNoRegistrada()
    {
        var entorno = ConIntegracion();
        var desconocida = Guid.NewGuid();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([desconocida]));

        Assert.True(resultado.EsExito);
        Assert.Equal([desconocida], resultado.Valor.NoRegistradas);
        Assert.Empty(resultado.Valor.Ediciones);
        Assert.Empty(resultado.Valor.NoImpresas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Consultar_VariasEdiciones_VanEnElOrdenPedidoYSinRepetirse()
    {
        var entorno = ConIntegracion();
        var a = entorno.Impresa();
        var b = entorno.Impresa();
        var c = entorno.Impresa();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([c, a, c, b]));

        Assert.Equal([c, a, b], resultado.Valor.Ediciones.Select(e => e.EdicionId));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Consultar_NoRevelaFisicoNiReservado()
    {
        var entorno = ConIntegracion();
        var libro = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Lima.Id, libro, 9, 1);

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([libro]));

        Assert.DoesNotContain(typeof(VendibleEnFuente).GetProperties(), p => p.Name is "Fisico" or "Reservado");
        Assert.Equal(8, resultado.Valor.Ediciones[0].PorFuente.Single(f => f.CodigoDeFuente == "lima").Vendible);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-02")]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Consultar_ConCeroOMasDeCincuentaEdiciones_EsValidacion(int cantidad)
    {
        var entorno = ConIntegracion();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([.. Enumerable.Range(0, cantidad).Select(_ => Guid.NewGuid())]));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(resultado.Error.Errores, e => e.Campo == "edicionIds");
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Consultar_ConHastaCincuentaEdiciones_EsValida()
    {
        var entorno = ConIntegracion();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([.. Enumerable.Range(0, 50).Select(_ => Guid.NewGuid())]));

        Assert.True(resultado.EsExito);
        Assert.Equal(50, resultado.Valor.NoRegistradas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Consultar_ConUnaEdicionVacia_EsValidacion()
    {
        var resultado = await Caso(ConIntegracion()).Ejecutar(new ConsultarDisponibilidadEntrada([Guid.Empty]));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-02")]
    [InlineData("integracion")]
    [InlineData("sistema")]
    [InlineData("cliente")]
    [InlineData("administracion")]
    public async Task Consultar_ConCualquierActorConContexto_Responde(string tipo)
    {
        var actor = tipo switch
        {
            "integracion" => ActorDePrueba.DeIntegracion("e-commerce"),
            "sistema" => ActorDePrueba.DelSistema(),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.DeAdministradorCentral(),
        };
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([entorno.Impresa()]));

        Assert.True(resultado.EsExito);
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-02")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consultar_SinActorOConUnVisitante_FallaCerradaYDejaSuEventoEnVezDeLeerComoSiNada(bool visitante)
    {
        var entorno = new EntornoDeFuentes(visitante ? ActorDePrueba.Anonimo() : ActorDePrueba.SinContexto());

        var resultado = await Caso(entorno).Ejecutar(new ConsultarDisponibilidadEntrada([entorno.Impresa()]));

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal(["sin-sesion"], entorno.MotivosDeDenegacion);
        Assert.Equal(0, entorno.Lectura.Instantaneas);
    }

    // --- Sucursales ---

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task ListarSucursales_DevuelveCodigoNombreYRegion_OrdenadosPorCodigo_SinContactoNiStock()
    {
        var entorno = ConIntegracion();
        entorno.Fuentes.Agregar("piura", "piura");

        var resultado = await new ListarSucursales(entorno.Autorizador, entorno.Lectura, entorno.Fuentes).Ejecutar(new ListarSucursalesEntrada());

        Assert.Equal(
            [new SucursalPublica("ayacucho", "Ayacucho", "ayacucho"), new SucursalPublica("cusco", "Cusco", "cusco"), new SucursalPublica("lima", "Lima", "lima"), new SucursalPublica("piura", "Piura", "piura")],
            resultado.Valor);
        Assert.Equal(["Codigo", "Nombre", "Region"], typeof(SucursalPublica).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task ListarSucursales_SinActor_FallaCerrada()
    {
        var entorno = new EntornoDeFuentes(ActorDePrueba.SinContexto());

        var resultado = await new ListarSucursales(entorno.Autorizador, entorno.Lectura, entorno.Fuentes).Ejecutar(new ListarSucursalesEntrada());

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal(["sin-sesion"], entorno.MotivosDeDenegacion);
    }
}
