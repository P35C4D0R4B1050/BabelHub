using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Web;
using Contrato = BabelHub.Shared.Contratos.Auditoria;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El evento propio del bloqueo por textos legales pendientes (WEB-47, AC-WEB-40; hallazgo de <c>web-base</c> WEB-1e): una acción de seguridad distinta de <c>AccesoDenegado</c> (no cuenta para SEG-09), con motivo
/// de catálogo, plantilla de ruta sin consulta, método y cuenta opaca; contada en ventanas fijas de 15 minutos con una alerta técnica por ventana (AUD-29).
/// </summary>
public sealed class RegistroDeBloqueosPorTextosPendientesTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);

    internal sealed class RegistroEspia : IRegistroDeSeguridad
    {
        public List<SolicitudDeRegistro> Solicitudes { get; } = [];

        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
        {
            Solicitudes.Add(solicitud);
            return Task.CompletedTask;
        }
    }

    internal sealed class AlertasEspia : IAlertas
    {
        public List<string> Tecnicas { get; } = [];

        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
        {
            Tecnicas.Add(motivo.Valor);
            return Task.CompletedTask;
        }

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    internal sealed class RelojManual(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private static ClaveDeBloqueoPorTextos Clave(MotivoDeBloqueoPorTextos motivo = MotivoDeBloqueoPorTextos.AccionNoPermitida, string metodo = "POST", Guid? cuenta = null) =>
        ClaveDeBloqueoPorTextos.Normalizar("/carrito/pagar", metodo, motivo, cuenta ?? Guid.NewGuid());

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task UnBloqueo_SeRegistraComoEventoPropio_ConMotivoRutaMetodoYCuentaOpaca_SinConsultaNiSesion()
    {
        var registro = new RegistroEspia();
        var cuenta = Guid.NewGuid();
        var sut = new RegistroDeBloqueosPorTextosPendientes(registro, new AlertasEspia(), new RelojManual(Inicio));

        await sut.Registrar(Clave(MotivoDeBloqueoPorTextos.AccionNoPermitida, "POST", cuenta));

        var solicitud = Assert.Single(registro.Solicitudes);
        Assert.Equal(AccionesAuditables.AccesoBloqueadoPorTextosPendientes, solicitud.Accion);
        Assert.NotEqual(AccionesAuditables.AccesoDenegado, solicitud.Accion);
        Assert.Equal("accion-no-permitida", solicitud.Motivo);
        Assert.Equal(ResultadoAuditado.Rechazado, solicitud.Resultado);
        Assert.Equal("usuario", solicitud.ObjetoTipo);
        Assert.Equal(cuenta.ToString("D"), solicitud.ObjetoId);
        Assert.Equal("POST", solicitud.Detalle["metodo"]);
        Assert.Equal("/carrito/pagar", solicitud.Detalle["plantilla"]);
        Assert.Equal(["metodo", "plantilla"], solicitud.Detalle.Keys.Order());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task OnceBloqueosEnCincoMinutos_SeRegistranTodos_SinAlertaYSinContarComoAccesoDenegado()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();
        var reloj = new RelojManual(Inicio);
        var sut = new RegistroDeBloqueosPorTextosPendientes(registro, alertas, reloj);

        for (var i = 0; i < 11; i++)
        {
            reloj.Ahora = Inicio.AddSeconds(i * 25);
            await sut.Registrar(Clave());
        }

        Assert.Equal(11, registro.Solicitudes.Count);
        Assert.All(registro.Solicitudes, s => Assert.Equal(AccionesAuditables.AccesoBloqueadoPorTextosPendientes, s.Accion));
        Assert.Empty(alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task SuperarElUmbralEnUnaVentanaDe15Minutos_PublicaUnaSolaAlertaTecnica_YLaVentanaSiguienteVuelveAEmpezar()
    {
        var alertas = new AlertasEspia();
        var reloj = new RelojManual(Inicio);
        var sut = new RegistroDeBloqueosPorTextosPendientes(new RegistroEspia(), alertas, reloj);

        for (var i = 0; i < RegistroDeBloqueosPorTextosPendientes.UmbralPorVentana; i++)
        {
            await sut.Registrar(Clave());
        }

        Assert.Empty(alertas.Tecnicas);

        await sut.Registrar(Clave());
        await sut.Registrar(Clave());
        Assert.Equal([Contrato.MotivosDeAlertaDeAuditoria.BloqueosPorTextosPendientes], alertas.Tecnicas);

        reloj.Ahora = Inicio.AddMinutes(15);
        for (var i = 0; i < RegistroDeBloqueosPorTextosPendientes.UmbralPorVentana + 1; i++)
        {
            await sut.Registrar(Clave());
        }

        Assert.Equal(2, alertas.Tecnicas.Count);
    }

    [Fact]
    public async Task LaVentanaEsFijaYAlineadaAlReloj_UnaRafagaQueCruzaElLimiteNoSeSumaEntreVentanas()
    {
        var alertas = new AlertasEspia();
        var reloj = new RelojManual(Inicio.AddMinutes(14).AddSeconds(30));
        var sut = new RegistroDeBloqueosPorTextosPendientes(new RegistroEspia(), alertas, reloj);

        for (var i = 0; i < 40; i++)
        {
            await sut.Registrar(Clave());
        }

        reloj.Ahora = Inicio.AddMinutes(15).AddSeconds(10);
        for (var i = 0; i < 40; i++)
        {
            await sut.Registrar(Clave());
        }

        Assert.Empty(alertas.Tecnicas);
    }

    [Fact]
    public async Task LaFallaDeIdentidadNoEsEsteEvento_EsLaAlertaDeWeb48_YNoSeRegistraNada()
    {
        var registro = new RegistroEspia();
        var sut = new RegistroDeBloqueosPorTextosPendientes(registro, new AlertasEspia(), new RelojManual(Inicio));

        await sut.Registrar(Clave(MotivoDeBloqueoPorTextos.IdentidadNoDisponible));
        await sut.Registrar(Clave(MotivoDeBloqueoPorTextos.Ninguno));

        Assert.Empty(registro.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public void ElCatalogoDeAuditoria_TieneLaAccionComoEventoDeSeguridadQueNoConcedeYConTodosSusMotivos()
    {
        var accion = CatalogoDeAccionesDeAuditoria.Crear().Obtener(nameof(AccionesAuditables.AccesoBloqueadoPorTextosPendientes));

        Assert.Equal(Contrato.AccionesDeAuditoria.AccesoBloqueadoPorTextosPendientes, accion.Identificador);
        foreach (var motivo in Enum.GetValues<MotivoDeBloqueoPorTextos>().Select(RegistroDeBloqueosPorTextosPendientes.MotivoDelCatalogo).OfType<string>())
        {
            Assert.Contains(motivo, accion.Motivos);
        }

        Assert.True(CatalogoDeAlertasDeAuditoria.Crear().Contiene(Contrato.MotivosDeAlertaDeAuditoria.BloqueosPorTextosPendientes));
    }

    [Fact]
    public async Task ElRegistroDeLaWeb_LlevaElBloqueoAlEventoPropio_YNoALaDenegacionDeAcceso()
    {
        var registro = new RegistroEspia();
        var denegaciones = new DenegacionesQueFallanSiSeUsan();
        var adaptador = new RegistroDeSeguridadDeLaWebEnAuditoria(denegaciones, new RegistroDeBloqueosPorTextosPendientes(registro, new AlertasEspia(), new RelojManual(Inicio)));

        await adaptador.TextosPendientesBloqueado(Clave(MotivoDeBloqueoPorTextos.RutaNoDeclarada, "DELETE"));

        Assert.Equal("ruta-no-declarada", Assert.Single(registro.Solicitudes).Motivo);
    }

    internal static RegistroDeBloqueosPorTextosPendientes SinBloqueos() => new(new RegistroEspia(), new AlertasEspia(), new RelojManual(Inicio));

    private sealed class DenegacionesQueFallanSiSeUsan : IDenegacionDeAcceso
    {
        public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default) =>
            throw new InvalidOperationException("Un bloqueo por textos pendientes no es un acceso denegado (cuenta para SEG-09).");
    }
}
