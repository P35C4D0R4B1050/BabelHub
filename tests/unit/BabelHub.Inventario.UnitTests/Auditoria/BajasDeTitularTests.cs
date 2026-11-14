using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// La auditoría de la baja de un titular recibida en el Inventario (T20; AUD-32, AUD-23, AUD-04, AUD-19): esquema estricto del identificador opaco, actor «integración»,
/// una sola auditoría por baja y la repetición sin una segunda; si no se puede auditar, la baja no se guarda.
/// </summary>
public sealed class BajasDeTitularTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Titular = Guid.Parse("5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e30");

    private static readonly string[] SoloElIdentificador = [BajaDeTitularRecibida.CampoDelIdentificador];

    private static BajaDeTitularRecibida Valida() => BajaDeTitularRecibida.Validar(SoloElIdentificador, Titular.ToString("D")).Valor;

    // ---------- Esquema estricto ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public void ElAvisoValidoTieneUnSoloCampoConUnGuidCanonicoEnMinusculas()
    {
        var resultado = BajaDeTitularRecibida.Validar(SoloElIdentificador, Titular.ToString("D"));

        Assert.True(resultado.EsExito);
        Assert.Equal(Titular, resultado.Valor.Identificador);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-29")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("5C1F0A9E-6D3B-4A52-8F7E-2B9D4C6A1E30")]
    [InlineData("5c1f0a9e6d3b4a528f7e2b9d4c6a1e30")]
    [InlineData("{5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e30}")]
    [InlineData("(5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e30)")]
    [InlineData("5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e30 ")]
    [InlineData(" 5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e30")]
    [InlineData("5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e3")]
    [InlineData("5c1f0a9e-6d3b-4a52-8f7e-2b9d4c6a1e30\n")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("cuenta@example.com")]
    [InlineData("no-es-un-guid-pero-tiene-treinta-y-seis")]
    public void UnIdentificadorConOtraFormaSeRechazaComoValidacion(string valor)
    {
        var resultado = BajaDeTitularRecibida.Validar(SoloElIdentificador, valor);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("identificador-invalido", resultado.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public void ElIdentificadorNuloSeRechaza()
    {
        Assert.Equal("identificador-invalido", BajaDeTitularRecibida.Validar(SoloElIdentificador, null).Error!.Clave);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-29")]
    [InlineData("identificadorOpaco", "correo")]
    [InlineData("identificadorOpaco", "nombre", "telefono")]
    [InlineData("correo")]
    [InlineData("identificadoropaco")]
    [InlineData("IdentificadorOpaco")]
    public void UnCampoDeMasOUnNombreDistintoSeRechazaSinIgnorarlo(params string[] campos)
    {
        var resultado = BajaDeTitularRecibida.Validar(campos, Titular.ToString("D"));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("esquema-invalido", resultado.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public void UnAvisoSinCamposSeRechaza()
    {
        Assert.Equal("esquema-invalido", BajaDeTitularRecibida.Validar([], Titular.ToString("D")).Error!.Clave);
        Assert.Equal("esquema-invalido", BajaDeTitularRecibida.Validar(["identificadorOpaco", "identificadorOpaco"], Titular.ToString("D")).Error!.Clave);
    }

    // ---------- Auditoría de la baja ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task CadaBajaNuevaSeAudita_ConElIdentificadorOpacoComoObjetoYSinMasDatos()
    {
        var auditoria = new AuditoriaEspia();
        var servicio = new AuditoriaDeBajasRecibidas(auditoria, ActorDePrueba.DeIntegracion("ecommerce"));

        await servicio.Registrar(Valida(), esNueva: true);

        var solicitud = Assert.Single(auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.BajaDeTitularProcesada, solicitud.Accion);
        Assert.Equal("titular", solicitud.ObjetoTipo);
        Assert.Equal(Titular.ToString("D"), solicitud.ObjetoId);
        Assert.Empty(solicitud.Detalle);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task LaRepeticionDeLaMismaBajaNoEscribeUnaSegundaAuditoria()
    {
        var auditoria = new AuditoriaEspia();
        var servicio = new AuditoriaDeBajasRecibidas(auditoria, ActorDePrueba.DeIntegracion("ecommerce"));

        await servicio.Registrar(Valida(), esNueva: true);
        await servicio.Registrar(Valida(), esNueva: false);
        await servicio.Registrar(Valida(), esNueva: false);

        Assert.Single(auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task SiNoSePuedeAuditar_LaBajaNoSeGuarda_PorqueLaExcepcionPropagaYRevierteLaTransaccionDeReservas()
    {
        var auditoria = new AuditoriaEspia { Caida = true };
        var servicio = new AuditoriaDeBajasRecibidas(auditoria, ActorDePrueba.DeIntegracion("ecommerce"));

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => servicio.Registrar(Valida(), esNueva: true));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-23")]
    public async Task SoloLaIntegracionPuedeEntregarLaBaja_UnUsuarioUnAnonimoOLaAusenciaDeContextoNo()
    {
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.DelSistema(), ActorDePrueba.SinContexto() })
        {
            var auditoria = new AuditoriaEspia();
            var servicio = new AuditoriaDeBajasRecibidas(auditoria, actor);

            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.Registrar(Valida(), esNueva: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.Registrar(Valida(), esNueva: false));
            Assert.Empty(auditoria.Solicitudes);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task ElRegistroReal_TieneElActorIntegracionYLaDepuracionSiguienteAnonimizaAlTitular()
    {
        var catalogo = CatalogoDeAccionesDeAuditoria.Crear();
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Inventario, catalogo, new RelojFijo(Ahora), ActorDePrueba.DeIntegracion("ecommerce"), new OrigenFijo(OrigenDeSolicitud.Desconocido));
        var servicio = new AuditoriaDeBajasRecibidas(new AuditoriaDeAplicacion(constructor, almacen), ActorDePrueba.DeIntegracion("ecommerce"));

        await servicio.Registrar(Valida(), esNueva: true);

        var registro = Assert.Single(almacen.Registros);
        Assert.Equal(Domain.Auditoria.TipoDeActor.Integracion, registro.Actor.Tipo);
        Assert.Equal(Titular.ToString("D"), registro.ObjetoId);
        Assert.Equal("titular", registro.ObjetoTipo);
        Assert.Equal(ResultadoDeOperacion.Exitoso, registro.Resultado);

        var depurado = PoliticaDeAnonimizacion.Depurar(registro, Ahora.AddDays(1), [Titular.ToString("D")]);

        Assert.NotNull(depurado);
        Assert.Null(depurado.ObjetoId);
        Assert.Equal("BajaDeTitularProcesada", depurado.AccionDelCatalogo.Identificador);
    }
}
