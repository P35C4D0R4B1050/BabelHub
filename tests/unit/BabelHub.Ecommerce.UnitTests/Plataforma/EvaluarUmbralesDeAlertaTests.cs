using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class EvaluarUmbralesDeAlertaTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class ContadorFalso : IContadorDeEventosDeSeguridad
    {
        public Dictionary<string, int> Conteos { get; } = [];

        public List<(string Motivo, DateTimeOffset Desde, DateTimeOffset Hasta)> Consultas { get; } = [];

        public Task<int> Contar(string motivo, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
        {
            Consultas.Add((motivo, desde, hasta));
            return Task.FromResult(Conteos.GetValueOrDefault(motivo));
        }
    }

    private sealed class AlertasEspia : IPublicadorDeAlertasDeSeguridad
    {
        public List<AlertaDeSeguridad> Publicadas { get; } = [];

        public bool Falla { get; set; }

        public Task Publicar(AlertaDeSeguridad alerta, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("auditoría caída");
            }

            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private sealed class EstadosEnMemoria : IEstadoDeUmbrales
    {
        public Dictionary<string, EstadoDeUmbral> Estados { get; } = [];

        public Task<EstadoDeUmbral?> Obtener(string motivo, CancellationToken cancelacion) =>
            Task.FromResult(Estados.TryGetValue(motivo, out var estado) ? estado : null);

        public Task Guardar(string motivo, EstadoDeUmbral estado, CancellationToken cancelacion)
        {
            Estados[motivo] = estado;
            return Task.CompletedTask;
        }
    }

    private readonly RelojFijo _reloj = new(Inicio);
    private readonly ContadorFalso _contador = new();
    private readonly AlertasEspia _alertas = new();
    private readonly EstadosEnMemoria _estados = new();
    private readonly EvaluarUmbralesDeAlerta _evaluar;

    public EvaluarUmbralesDeAlertaTests()
    {
        _evaluar = new EvaluarUmbralesDeAlerta(UmbralesDeAlerta.Todos, _contador, _alertas, _estados, _reloj);
    }

    private async Task<IReadOnlyList<ResultadoDeUmbral>> EvaluarEn(TimeSpan despues)
    {
        _reloj.Ahora = Inicio + despues;
        return await _evaluar.Ejecutar();
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-09")]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public async Task Ejecutar_ConAccesosDenegadosEnElBorde_AlertaSoloConOnce(int denegados, bool alerta)
    {
        _contador.Conteos["accesos-denegados"] = denegados;

        var resultados = await EvaluarEn(TimeSpan.Zero);

        Assert.Equal(alerta, resultados.Single(r => r.Motivo == "accesos-denegados").AlertaPublicada);
        Assert.Equal(alerta ? 1 : 0, _alertas.Publicadas.Count);
        if (alerta)
        {
            var publicada = Assert.Single(_alertas.Publicadas);
            Assert.Equal("accesos-denegados", publicada.Motivo);
            Assert.Equal(11, publicada.Total);
            Assert.Equal(Inicio, publicada.Instante);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-09")]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public async Task IngresosFallidos_ConElBordeDeLaSpec_AlertaSoloConVeintiuno(int fallidos, bool alerta)
    {
        _contador.Conteos["ingresos-fallidos"] = fallidos;

        await EvaluarEn(TimeSpan.Zero);

        Assert.Equal(alerta, _alertas.Publicadas.Any(a => a.Motivo == "ingresos-fallidos"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task Ejecutar_ConsultaLosUltimosCincoMinutosComoVentanaDeslizante()
    {
        await EvaluarEn(TimeSpan.FromMinutes(7));

        Assert.All(_contador.Consultas, c =>
        {
            Assert.Equal(Inicio + TimeSpan.FromMinutes(7), c.Hasta);
            Assert.Equal(c.Hasta - TimeSpan.FromMinutes(5), c.Desde);
        });
        Assert.Equal(["accesos-denegados", "ingresos-fallidos"], _contador.Consultas.Select(c => c.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task Ejecutar_MientrasElUmbralSigueSuperado_NoVuelveAPublicarParaNoInflarLasOcurrencias()
    {
        _contador.Conteos["accesos-denegados"] = 15;

        await EvaluarEn(TimeSpan.Zero);
        await EvaluarEn(TimeSpan.FromMinutes(1));
        await EvaluarEn(TimeSpan.FromMinutes(2));

        Assert.Single(_alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task Ejecutar_SiElUmbralBajaYVuelveACruzarse_PublicaOtraAlerta()
    {
        _contador.Conteos["accesos-denegados"] = 15;
        await EvaluarEn(TimeSpan.Zero);
        _contador.Conteos["accesos-denegados"] = 3;
        await EvaluarEn(TimeSpan.FromMinutes(1));
        _contador.Conteos["accesos-denegados"] = 12;
        await EvaluarEn(TimeSpan.FromMinutes(2));

        Assert.Equal(2, _alertas.Publicadas.Count);
        Assert.Equal(Inicio + TimeSpan.FromMinutes(2), _alertas.Publicadas[1].Instante);
    }

    [Fact]
    public async Task Ejecutar_ConDosCorridasDelMismoInstante_LaSegundaNoCambiaNada()
    {
        _contador.Conteos["accesos-denegados"] = 15;
        await EvaluarEn(TimeSpan.Zero);
        var consultas = _contador.Consultas.Count;

        var repetida = await EvaluarEn(TimeSpan.Zero);

        Assert.Single(_alertas.Publicadas);
        Assert.Equal(consultas, _contador.Consultas.Count);
        Assert.All(repetida, r => Assert.False(r.AlertaPublicada));
    }

    [Fact]
    public async Task Ejecutar_ConLaMarcaDeAguaGuardada_NoRepiteLaAlertaTrasReiniciarElServicio()
    {
        _contador.Conteos["accesos-denegados"] = 15;
        await EvaluarEn(TimeSpan.Zero);

        // Un servicio nuevo con el mismo estado persistido (reinicio).
        var reiniciado = new EvaluarUmbralesDeAlerta(UmbralesDeAlerta.Todos, _contador, _alertas, _estados, _reloj);
        _reloj.Ahora = Inicio + TimeSpan.FromMinutes(1);
        await reiniciado.Ejecutar();

        Assert.Single(_alertas.Publicadas);
        Assert.Equal(Inicio + TimeSpan.FromMinutes(1), _estados.Estados["accesos-denegados"].UltimaEvaluacion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task Ejecutar_ConUnaPublicacionQueFalla_NoAvanzaElEstadoYLaSiguienteCorridaLaReintenta()
    {
        _contador.Conteos["accesos-denegados"] = 15;
        _alertas.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => EvaluarEn(TimeSpan.Zero));
        Assert.False(_estados.Estados.ContainsKey("accesos-denegados"));

        _alertas.Falla = false;
        await EvaluarEn(TimeSpan.FromMinutes(1));

        Assert.Single(_alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task Ejecutar_ConUnSoloUmbralConfigurado_NoEvaluaLosDemas()
    {
        var soloDenegados = new EvaluarUmbralesDeAlerta([UmbralesDeAlerta.AccesosDenegados], _contador, _alertas, _estados, _reloj);
        _contador.Conteos["ingresos-fallidos"] = 100;

        var resultados = await soloDenegados.Ejecutar();

        Assert.Equal("accesos-denegados", Assert.Single(resultados).Motivo);
        Assert.Empty(_alertas.Publicadas);
    }

    [Fact]
    public async Task Ejecutar_ConDosUmbralesSuperados_PublicaUnaAlertaPorCada()
    {
        _contador.Conteos["accesos-denegados"] = 11;
        _contador.Conteos["ingresos-fallidos"] = 21;

        await EvaluarEn(TimeSpan.Zero);

        Assert.Equal(["accesos-denegados", "ingresos-fallidos"], _alertas.Publicadas.Select(a => a.Motivo));
    }
}
