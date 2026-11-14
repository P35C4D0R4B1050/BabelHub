using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>Las correcciones de la revisión de AUD-3 en el Inventario: el cursor de la página va firmado, el alcance de la integración y el identificador de la alerta.</summary>
public sealed class CorreccionesDeAud3Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private sealed class LocalFalso(FuentePaginadaFalsa fuente) : IConsultaLocalDeRegistros
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => fuente.Consultar(consulta, cancelacion);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public async Task LaPrimeraPaginaTraeSuCursorFirmado_YPedirloDevuelveLaMismaPaginaSinElCursorCrudoDelAlmacen()
    {
        var reloj = new RelojFijo(Ahora);
        var fuente = new FuentePaginadaFalsa();
        for (var i = 0; i < 30; i++)
        {
            fuente.Agregar(RegistrosDePrueba.De(ServicioAuditado.Inventario, Ahora.AddSeconds(-i)));
        }

        var admin = ActorDePrueba.DeAdministradorCentral(Guid.NewGuid());
        var consulta = new ConsultasDeAuditoriaLocales(admin, CatalogoDeAccionesDeAuditoria.Crear(), new FirmadorDeCursor(new ClavesDeCursorFalsas(), reloj), new LocalFalso(fuente));

        var primera = (await consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Valor;

        Assert.NotNull(primera.CursorDeLaPagina);
        Assert.StartsWith("v2.", primera.CursorDeLaPagina, StringComparison.Ordinal);
        Assert.DoesNotContain("|", primera.CursorDeLaPagina, StringComparison.Ordinal);

        var otraVez = (await consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), primera.CursorDeLaPagina))).Valor;
        Assert.Equal(primera.Registros.Select(r => r.Id), otraVez.Registros.Select(r => r.Id));

        // Un cursor de página crudo (sin firmar) no se acepta.
        Assert.Equal(ConsultasDeAuditoriaLocales.CursorInvalido, (await consulta.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria(), "30|"))).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task LeerNuevasSoloLaAceptaLaIntegracionDeAlcanceLecturaDeAlertas()
    {
        foreach (var actor in new[] { ActorDePrueba.DeIntegracion("historial"), ActorDePrueba.DeIntegracion("ecommerce"), ActorDePrueba.DeIntegracion("baja-de-titular"), ActorDePrueba.DelSistema() })
        {
            var consulta = new ConsultaDeAlertasLocales(actor, new FirmadorDeCursor(new ClavesDeCursorFalsas(), new RelojFijo(Ahora)), new AlmacenDeAlertasEnMemoria(), new AuditoriaEspia(), new UnidadDeTrabajoDeAuditoria(), new RelojFijo(Ahora));

            Assert.Equal(CategoriaDeError.Prohibido, (await consulta.LeerNuevas(Ahora.AddDays(-1))).Error!.Categoria);
        }
    }

    [Theory]
    [InlineData("inv:../../etc/passwd")]
    [InlineData("inv:AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("inv:11111111-1111-4111-8111-111111111111/revisada")]
    [InlineData("eco:11111111-1111-4111-8111-111111111111")]
    public async Task UnIdentificadorDeAlertaConOtraFormaOAjenoSeRechazaSinLlegarAlAlmacen(string id)
    {
        var almacen = new AlmacenDeAlertasEnMemoria();
        var consulta = new ConsultaDeAlertasLocales(ActorDePrueba.DeAdministradorCentral(), new FirmadorDeCursor(new ClavesDeCursorFalsas(), new RelojFijo(Ahora)), almacen, new AuditoriaEspia(), new UnidadDeTrabajoDeAuditoria(), new RelojFijo(Ahora));

        var resultado = await consulta.MarcarRevisada(id, 1);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task LaBajaSoloLaEntregaLaIntegracionDeAlcanceBajaDeTitular()
    {
        var baja = BajaDeTitularRecibida.Validar([BajaDeTitularRecibida.CampoDelIdentificador], Guid.NewGuid().ToString("D")).Valor;

        foreach (var actor in new[] { ActorDePrueba.DeIntegracion("lectura-alertas"), ActorDePrueba.DeIntegracion("historial"), ActorDePrueba.DeIntegracion("ecommerce") })
        {
            var auditoria = new AuditoriaEspia();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AuditoriaDeBajasRecibidas(auditoria, actor).Registrar(baja, esNueva: true));
            Assert.Empty(auditoria.Solicitudes);
        }
    }
}
