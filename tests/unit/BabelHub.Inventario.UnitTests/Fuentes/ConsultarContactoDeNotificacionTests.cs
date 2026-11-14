using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T16: el contacto de notificación de una sucursal (FUE-01): solo el servicio de avisos o la administración, no encontrado para un código inexistente y sin registrar el número.</summary>
public sealed class ConsultarContactoDeNotificacionTests
{
    private const string Numero = "+51 966 123 456";

    private static ConsultarContactoDeNotificacion Caso(EntornoDeFuentes e) => new(e.Autorizador, new ValidadorDeConsultarContacto(), e.Fuentes);

    private static EntornoDeFuentes ConContacto(ActorDePrueba? actor = null)
    {
        var entorno = new EntornoDeFuentes(actor ?? ActorDePrueba.DeIntegracion("e-commerce"));
        var cusco = entorno.Cusco.Editar(entorno.Cusco.Nombre, entorno.Cusco.Region, Numero).Fuente;
        entorno.Fuentes.Fuentes[entorno.Fuentes.Fuentes.FindIndex(f => f.Id == cusco.Id)] = cusco;
        return entorno;
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task ElServicioDeAvisos_RecibeElNumeroDeLaSucursal()
    {
        var entorno = ConContacto();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarContactoDeNotificacionEntrada("cusco"));

        Assert.Equal(("cusco", Numero), (resultado.Valor.CodigoDeSucursal, resultado.Valor.Numero));
        Assert.Empty(entorno.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-12")]
    [InlineData("sistema")]
    [InlineData("administracion")]
    public async Task ElSistemaYLaAdministracionTambienPuedenLeerlo(string quien)
    {
        var entorno = ConContacto(quien == "sistema" ? ActorDePrueba.DelSistema() : ActorDePrueba.DeAdministradorCentral());

        var resultado = await Caso(entorno).Ejecutar(new ConsultarContactoDeNotificacionEntrada("cusco"));

        Assert.True(resultado.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task UnCodigoInexistente_UnaSucursalSinContactoOUnCodigoInvalido_NoDistinguenQuienPide()
    {
        var entorno = ConContacto();

        var inexistente = await Caso(entorno).Ejecutar(new ConsultarContactoDeNotificacionEntrada("piura"));
        var sinContacto = await Caso(entorno).Ejecutar(new ConsultarContactoDeNotificacionEntrada("lima"));

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, sinContacto.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task UnCodigoConFormaInvalida_EsValidacion()
    {
        var resultado = await Caso(ConContacto()).Ejecutar(new ConsultarContactoDeNotificacionEntrada("Cusco; DROP"));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(["codigoDeSucursal"], resultado.Error.Errores.Select(e => e.Campo));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [InlineData("encargado")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    [InlineData("visitante")]
    [InlineData("sin-contexto")]
    public async Task CualquierOtroActor_SeRechazaConSuEventoYNoLeeElNumero(string quien)
    {
        var actor = quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            "cliente" => ActorDePrueba.DeCliente(),
            "visitante" => ActorDePrueba.Anonimo(),
            _ => ActorDePrueba.SinContexto(),
        };
        var entorno = ConContacto(actor);

        var resultado = await Caso(entorno).Ejecutar(new ConsultarContactoDeNotificacionEntrada("cusco"));

        Assert.False(resultado.EsExito);
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public async Task ElNumeroNoSeRegistra_NiEnLaAuditoriaNiEnLaImpresionDeLoQueSeDevuelve()
    {
        var entorno = ConContacto();

        var resultado = await Caso(entorno).Ejecutar(new ConsultarContactoDeNotificacionEntrada("cusco"));

        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.DoesNotContain("966", resultado.Valor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(entorno.Denegaciones.Eventos, e => (e.Ruta ?? string.Empty).Contains("966", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public void ElCasoDeUsoNoDependeDeLaAuditoriaNiDeNingunaEscrituraDeStock()
    {
        var parametros = typeof(ConsultarContactoDeNotificacion).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(parametros, t => t.Name is "IAuditoria" or "IRegistroDeSeguridad" or "IMovimientosDeStock" or "IAplicadorDeMovimientos");
    }
}
