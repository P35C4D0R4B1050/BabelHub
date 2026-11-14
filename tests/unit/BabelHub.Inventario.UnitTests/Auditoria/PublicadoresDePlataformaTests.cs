using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;
using AlertaDeSeguridadDePlataforma = BabelHub.Inventario.Application.Plataforma.AlertaDeSeguridad;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// La implementación de los puertos que define <c>Plataforma</c> (T10; AUD-12, AUD-13, AUD-28, AUD-29, AUD-39, AUD-41): las alertas se guardan y agrupan en
/// ventanas de 15 minutos sin interrumpir nunca, el exceso de un límite va al acumulador persistente y el conteo de los umbrales de SEG-09 suma el conteo local y
/// el de Identidad con fallo seguro.
/// </summary>
public sealed class PublicadoresDePlataformaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly CatalogoDeAcciones Catalogo = CatalogoDeAccionesDeAuditoria.Crear();
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Ahora);
            var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Inventario, Catalogo, Reloj, actor ?? ActorDePrueba.Anonimo(), new OrigenFijo(OrigenDeSolicitud.Desconocido));
            Alertas = new PublicadorDeAlertas(ServicioDeOrigen.Inventario, CatalogoDeAlertasDeAuditoria.Crear(), AlmacenDeAlertas, Unidad, Reloj);
            Eventos = new PublicadorDeEventosDeSeguridad(constructor, Registros, Acumulador, Alertas, Reloj);
            Contador = new ContadorDeEventosDeSeguridad(Local);
        }

        public RelojFijo Reloj { get; }

        public UnidadDeTrabajoDeAuditoria Unidad { get; } = new();

        public AlmacenDeAlertasEnMemoria AlmacenDeAlertas { get; } = new();

        public AlmacenDeRegistrosEnMemoria Registros { get; } = new();

        public AcumuladorDeLimitesEnMemoria Acumulador { get; } = new();

        public ConteoLocalFalso Local { get; } = new();

        public PublicadorDeAlertas Alertas { get; }

        public PublicadorDeEventosDeSeguridad Eventos { get; }

        public ContadorDeEventosDeSeguridad Contador { get; }
    }

    // ---------- Alertas: guardar, agrupar, nunca interrumpir ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task TresOcurrenciasDelMismoMotivoEnLaVentana_SonUnaAlertaConTresOcurrencias()
    {
        var e = new Entorno();
        IPublicadorDeAlertasTecnicas puerto = e.Alertas;

        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora));
        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(5)));
        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(14)));

        var alerta = Assert.Single(e.AlmacenDeAlertas.Alertas);
        Assert.Equal(3, alerta.Ocurrencias);
        Assert.Equal(ClaseDeAlerta.Tecnica, alerta.Clase);
        Assert.Equal(Ahora, alerta.PrimeraOcurrencia);
        Assert.Equal(Ahora.AddMinutes(14), alerta.UltimaOcurrencia);
        Assert.False(alerta.EnviadaPorCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task PasadaLaVentanaDeQuinceMinutos_LaRepeticionAbreOtraAlerta()
    {
        var e = new Entorno();
        IPublicadorDeAlertasTecnicas puerto = e.Alertas;

        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora));
        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora.AddMinutes(15)));

        Assert.Equal(2, e.AlmacenDeAlertas.Alertas.Count);
        Assert.All(e.AlmacenDeAlertas.Alertas, a => Assert.Equal(1, a.Ocurrencias));
    }

    [Fact]
    public async Task LaAlertaDeSeguridadDeSeg09_SeGuardaConSuClaveYSinElTotal()
    {
        var e = new Entorno();
        IPublicadorDeAlertasDeSeguridad puerto = e.Alertas;

        await puerto.Publicar(new AlertaDeSeguridadDePlataforma(MotivosDeAlerta.AccesosDenegados, Ahora, total: 11));

        var alerta = Assert.Single(e.AlmacenDeAlertas.Alertas);
        Assert.Equal(ClaseDeAlerta.Seguridad, alerta.Clase);
        Assert.True(alerta.TienePantalla);
        Assert.Equal(1, alerta.Ocurrencias);
        Assert.StartsWith("inv:", alerta.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnMotivoQueElCatalogoNoTiene_ConservaLaAlertaSinElTextoOriginal()
    {
        var e = new Entorno();
        IPublicadorDeAlertasTecnicas puerto = e.Alertas;

        await puerto.Publicar(new AlertaTecnica("falla con cadena Host=db;Password=Sup3r", Ahora));

        var alerta = Assert.Single(e.AlmacenDeAlertas.Alertas);
        Assert.Equal(MotivosDeAlerta.MotivoNoCatalogado, alerta.Motivo);
        Assert.DoesNotContain("Password", alerta.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IAlertas_RechazaUnMotivoSinCatalogarOLaClaseEquivocada_ComoDefectoDeQuienLlama()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => e.Alertas.PublicarTecnica(new CodigoDeAlerta("motivo-inventado")));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlerta.AccesosDenegados)));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Alertas.PublicarDeSeguridad(new CodigoDeAlerta(MotivosDeAlerta.TrabajoDeFondoDetenido)));
        Assert.Empty(e.AlmacenDeAlertas.Alertas);

        await e.Alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlerta.TrasladoVinculadoSinAprobar));
        Assert.Single(e.AlmacenDeAlertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task SiElAlmacenDeAlertasNoResponde_NuncaInterrumpeAQuienPublica()
    {
        var e = new Entorno();
        e.AlmacenDeAlertas.Falla = new InvalidOperationException("Host=db;Password=Sup3r");
        IPublicadorDeAlertasTecnicas tecnica = e.Alertas;
        IPublicadorDeAlertasDeSeguridad seguridad = e.Alertas;

        await tecnica.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora));
        await seguridad.Publicar(new AlertaDeSeguridadDePlataforma(MotivosDeAlerta.AccesosDenegados, Ahora, 11));
        await e.Alertas.PublicarTecnica(new CodigoDeAlerta(MotivosDeAlerta.TrabajoDeFondoDetenido));

        Assert.Empty(e.AlmacenDeAlertas.Alertas);
    }

    [Fact]
    public async Task LaAlertaSeEscribeConUnPlazoPropio_YNoConElTokenDeLaSolicitudCancelada()
    {
        var e = new Entorno();
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();
        IPublicadorDeAlertasTecnicas puerto = e.Alertas;

        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, Ahora), cancelada.Token);

        Assert.Single(e.AlmacenDeAlertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task LaAlertaOriginadaPorElPropioCorreo_QuedaEnElRegistroTecnicoSinCorreoPendiente()
    {
        var e = new Entorno();
        IPublicadorDeAlertasTecnicas puerto = e.Alertas;

        await puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.CorreoDeAlertasNoDisponible, Ahora));

        var alerta = Assert.Single(e.AlmacenDeAlertas.Alertas);
        Assert.True(alerta.EnviadaPorCorreo);
        Assert.Equal(MotivosDeAlerta.CorreoDeAlertasNoDisponible, alerta.Motivo);
    }

    [Fact]
    public async Task OcurrenciasSimultaneasConRelojEnDesorden_NoPierdenLaAlerta()
    {
        var e = new Entorno();
        IPublicadorDeAlertasTecnicas puerto = e.Alertas;

        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i => puerto.Publicar(new AlertaTecnica(MotivosDeAlerta.DependenciaNoNecesariaFallida, Ahora.AddSeconds(-(i % 3))))));

        Assert.True(e.AlmacenDeAlertas.Alertas.Count >= 1);
        Assert.Equal(20, e.AlmacenDeAlertas.Alertas.Sum(a => a.Ocurrencias));
    }

    [Fact]
    public void ElCatalogoDeAlertas_CubreLosMotivosDeLasFallasQueLaPlataformaYLaWebPublican()
    {
        var catalogo = CatalogoDeAlertasDeAuditoria.Crear();
        foreach (var motivo in new[]
        {
            "credencial-de-servicio-anterior-en-uso", "dependencia-no-necesaria-fallida", "trabajo-de-fondo-detenido", "memoria-de-contadores-alta",
            "verificador-de-credenciales-sin-claves", "revocacion-de-sesiones-pendiente", "traslado-vinculado-sin-aprobar", "accesos-denegados", "ingresos-fallidos",
        })
        {
            Assert.True(catalogo.Contiene(motivo), motivo);
        }

        Assert.False(catalogo.Obtener(MotivosDeAlerta.CorreoDeAlertasNoDisponible).EnviablePorCorreo);
        Assert.True(catalogo.Obtener(MotivosDeAlerta.DepuracionDiariaSinExito).EnviablePorCorreo);
    }

    // ---------- Eventos de seguridad ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task CincuentaRechazosYVeinteDespuesDeUnReinicio_SonUnSoloAcumuladoDeSetenta()
    {
        var e = new Entorno();
        var origen = HuellaDeOrigen.Crear(Resumen, "v1");

        for (var i = 0; i < 70; i++)
        {
            await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora.AddSeconds(i), "registros-por-origen", origen, "203.0.113.7"));
        }

        var ventana = Assert.Single(e.Acumulador.Ventanas.Values);
        var fila = Assert.Single(ventana.Filas);
        Assert.Equal(70, fila.Rechazos);
        Assert.Equal("203.0.113.7", fila.DireccionCompleta);
        Assert.Equal(Resumen, fila.Huella);
        Assert.Empty(e.Registros.Registros);
    }

    [Fact]
    public async Task UnLimitePorUsuario_AcumulaPorElIdentificadorDelUsuarioSinElCorreo()
    {
        var e = new Entorno();

        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora, "solicitudes-por-usuario", usuario: "3f1c2a40-aaaa-bbbb-cccc-111122223333"));

        var fila = Assert.Single(Assert.Single(e.Acumulador.Ventanas.Values).Filas);
        Assert.Equal("u-3f1c2a40-aaaa-bbbb-cccc-111122223333", fila.Huella);
        Assert.Null(fila.DireccionCompleta);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task LosEventosQueNoSonLimites_SeEscribenComoEventosDeSeguridadRechazadosConSuMotivo()
    {
        var e = new Entorno(ActorDePrueba.SinContexto());

        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenIndeterminable, Ahora));
        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial, Ahora));
        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.VerificacionDePersonaFallida, Ahora));
        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial, Ahora, origen: HuellaDeOrigen.Crear(Resumen, "v1")));
        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada, Ahora, proveedor: "culqi", motivo: "firma-invalida"));

        Assert.Equal(
            ["OrigenIndeterminable", "LlamadaEntreServiciosSinCredencial", "VerificacionDePersonaFallida", "OrigenReenviadoSinCredencial", "EntradaDeIntegracionRechazada"],
            e.Registros.Registros.Select(r => r.AccionDelCatalogo.Identificador).ToArray());
        Assert.All(e.Registros.Registros, r =>
        {
            Assert.Equal(ResultadoDeOperacion.Rechazado, r.Resultado);
            Assert.Equal(Domain.Auditoria.TipoDeActor.Anonimo, r.Actor.Tipo);
        });
        Assert.Equal("origen-reenviado-sin-credencial", e.Registros.Registros[3].Motivo);
        Assert.Equal($"v1:{Resumen}", e.Registros.Registros[3].Origen);
        Assert.Equal("firma-invalida", e.Registros.Registros[4].Motivo);
        Assert.Contains("culqi", e.Registros.Registros[4].Detalle.Texto, StringComparison.Ordinal);
        Assert.Empty(e.AlmacenDeAlertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task SiElEventoNoSePuedeEscribir_SeMantieneLaProteccionYQuedaUnaAlertaTecnica()
    {
        var e = new Entorno();
        e.Registros.Falla = new InvalidOperationException("Host=db;Password=Sup3r");

        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenIndeterminable, Ahora));

        var alerta = Assert.Single(e.AlmacenDeAlertas.Alertas);
        Assert.Equal(MotivosDeAlerta.AuditoriaNoDisponible, alerta.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task SiElAcumuladorNoResponde_ElRechazoSigueProtegiendoYQuedaUnaAlertaTecnica()
    {
        var e = new Entorno();
        e.Acumulador.Falla = new InvalidOperationException("la base no responde");

        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, Ahora, "registros-por-origen", HuellaDeOrigen.Crear(Resumen, "v1"), "203.0.113.7"));

        Assert.Equal(MotivosDeAlerta.AcumuladoDeLimitesNoDisponible, Assert.Single(e.AlmacenDeAlertas.Alertas).Motivo);
    }

    [Fact]
    public async Task SiTampocoRespondeElRegistroTecnico_NoLanzaNada()
    {
        var e = new Entorno();
        e.Registros.Falla = new InvalidOperationException("uno");
        e.AlmacenDeAlertas.Falla = new InvalidOperationException("dos");

        await e.Eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.OrigenIndeterminable, Ahora));
    }

    // ---------- Conteo de los umbrales de SEG-09 ----------
    // ---------- Conteo del umbral de SEG-09 que evalúa el Inventario ----------

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task OnceAccesosDenegados_SuperanElUmbralYGeneranLaAlertaDeSeguridad()
    {
        var e = new Entorno();
        e.Local.Accesos = 11;
        var evaluar = new EvaluarUmbralesDeAlerta([UmbralesDeAlerta.AccesosDenegados], e.Contador, e.Alertas, new EstadosEnMemoria(), e.Reloj);

        var resultado = Assert.Single(await evaluar.Ejecutar());

        Assert.Equal(11, resultado.Total);
        Assert.True(resultado.AlertaPublicada);
        var alerta = Assert.Single(e.AlmacenDeAlertas.Alertas);
        Assert.Equal(MotivosDeAlerta.AccesosDenegados, alerta.Motivo);
        Assert.StartsWith("inv:", alerta.Id, StringComparison.Ordinal);
        Assert.Equal("AccesoDenegado", Assert.Single(e.Local.Consultas).Accion);
    }

    [Fact]
    public async Task ElConteoRechazaUnaVentanaMayorQueCincoMinutosOUnMotivoQueNoEsUnUmbralDelInventario()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => e.Contador.Contar(MotivosDeAlerta.AccesosDenegados, Ahora.AddMinutes(-6), Ahora));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Contador.Contar(MotivosDeAlerta.AccesosDenegados, Ahora, Ahora));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Contador.Contar(MotivosDeAlerta.IngresosFallidos, Ahora.AddMinutes(-1), Ahora));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Contador.Contar(MotivosDeAlerta.AccesosDenegados, Ahora.AddMinutes(-5).ToOffset(TimeSpan.FromHours(-5)), Ahora));
    }

    private sealed class EstadosEnMemoria : IEstadoDeUmbrales
    {
        private readonly Dictionary<string, EstadoDeUmbral> _estados = [];

        public Task<EstadoDeUmbral?> Obtener(string motivo, CancellationToken cancelacion) =>
            Task.FromResult(_estados.TryGetValue(motivo, out var estado) ? estado : null);

        public Task Guardar(string motivo, EstadoDeUmbral estado, CancellationToken cancelacion)
        {
            _estados[motivo] = estado;
            return Task.CompletedTask;
        }
    }
}
