using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El contexto de lectura del asistente (identidad-seguridad T26; AC-IDE-30; IDE-33): 5 minutos, sin renovación, <c>aud=pedidos</c>, <c>typ=lectura-asistente</c>, el alcance único de leer los pedidos de ese cliente, solo para
/// el propio cliente con la cuenta vigente, y todo fallo al verificar es el mismo error.
/// </summary>
public sealed class ContextoDeLecturaDelAsistenteTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class FirmaEnMemoria : IFirmaDeContextosDeLectura
    {
        private readonly Dictionary<string, ReclamosDelContexto> _emitidos = [];

        public bool FirmaInvalida { get; set; }

        public bool Falla { get; set; }

        public int Emisiones => _emitidos.Count;

        public Task<string> Firmar(ReclamosDelContexto reclamos, CancellationToken cancelacion = default)
        {
            var token = $"token-{_emitidos.Count}";
            _emitidos[token] = reclamos;
            return Task.FromResult(token);
        }

        public Task<ReclamosDelContexto?> Verificar(string token, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("Sin claves.");
            }

            return Task.FromResult(FirmaInvalida ? null : _emitidos.GetValueOrDefault(token));
        }

        /// <summary>Un contexto firmado con reclamos arbitrarios (lo que haría un atacante con la clave o un servicio con un error).</summary>
        public string Forjar(ReclamosDelContexto reclamos)
        {
            var token = $"forjado-{_emitidos.Count}";
            _emitidos[token] = reclamos;
            return token;
        }
    }

    private sealed class CuentasDePrueba : IEstadoDeCuenta
    {
        public Dictionary<Guid, EstadoDeCuenta> Cuentas { get; } = [];

        public bool Falla { get; set; }

        public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
            Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Cuentas.GetValueOrDefault(usuarioId));

        public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.NoDisponible);

        public Guid Nueva(bool cancelada = false)
        {
            var id = Guid.NewGuid();
            Cuentas[id] = new EstadoDeCuenta(id, true, EstadoDeAceptacionDeLosTextos.AlDia, true, cancelada, false);
            return id;
        }
    }

    private sealed class Entorno
    {
        public Entorno()
        {
            Contexto = new ContextoDeLecturaDelAsistente(new AlcanceDeLaCuenta(Actor, Denegaciones), Cuentas, Firma, Reloj);
        }

        public RelojFijo Reloj { get; } = new(Inicio);

        public ActorDePrueba Actor { get; } = new();

        public DenegacionesDePrueba Denegaciones { get; } = new();

        public CuentasDePrueba Cuentas { get; } = new();

        public FirmaEnMemoria Firma { get; } = new();

        public ContextoDeLecturaDelAsistente Contexto { get; }

        public Guid ClienteAutenticado()
        {
            var id = Cuentas.Nueva();
            Actor.Actual = ActorActual.DeUsuario(id, "cliente");
            return id;
        }
    }

    private static ReclamosDelContexto Reclamos(Guid cliente, DateTimeOffset emitido, TimeSpan? vida = null, string audiencia = "pedidos", string tipo = "lectura-asistente", string alcance = "leer-pedidos") =>
        new(cliente, audiencia, tipo, alcance, emitido, emitido + (vida ?? TimeSpan.FromMinutes(5)), Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElClienteAutenticado_RecibeUnContextoDe5MinutosParaPedidosYSoloSuyo()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();

        var emitido = await e.Contexto.Emitir(cliente);
        var verificado = await e.Contexto.Verificar(emitido.Valor!);

        Assert.True(emitido.EsExito);
        Assert.Equal(cliente, verificado.Valor!.ClienteId);
        Assert.Equal("[redactado]", emitido.Valor!.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task LosReclamosDelContextoSonLosEsperados_AudienciaTipoAlcanceUnicoYCincoMinutos()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var emitido = await e.Contexto.Emitir(cliente);
        var reclamos = await e.Firma.Verificar(emitido.Valor!.Revelar());

        Assert.Equal("pedidos", reclamos!.Audiencia);
        Assert.Equal("lectura-asistente", reclamos.Tipo);
        Assert.Equal("leer-pedidos", reclamos.Alcance);
        Assert.Equal(Inicio, reclamos.EmitidoEn);
        Assert.Equal(Inicio.AddMinutes(5), reclamos.VenceEn);
        Assert.Equal(cliente, reclamos.ClienteId);
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(301, false)]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElContexto_VenceALos5MinutosExactos(int segundos, bool vigente)
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var emitido = await e.Contexto.Emitir(cliente);
        e.Reloj.Ahora = Inicio.AddSeconds(segundos);

        var resultado = await e.Contexto.Verificar(emitido.Valor!);

        Assert.Equal(vigente, resultado.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task UnContextoNoSeRenueva_NoHayForma_YUnoMasLargoDe5MinutosSeRechaza()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var largo = e.Firma.Forjar(Reclamos(cliente, Inicio, TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1))));
        var dias = e.Firma.Forjar(Reclamos(cliente, Inicio, TimeSpan.FromDays(1)));

        Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, (await e.Contexto.Verificar(new ValorSecreto(largo))).Error);
        Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, (await e.Contexto.Verificar(new ValorSecreto(dias))).Error);
    }

    [Theory]
    [InlineData("biblioteca", "lectura-asistente", "leer-pedidos")]
    [InlineData("cuenta", "lectura-asistente", "leer-pedidos")]
    [InlineData("descargas", "lectura-asistente", "leer-pedidos")]
    [InlineData("pedidos", "acceso", "leer-pedidos")]
    [InlineData("pedidos", "renovacion", "leer-pedidos")]
    [InlineData("pedidos", "lectura-asistente", "escribir-pedidos")]
    [InlineData("pedidos", "lectura-asistente", "leer-pedidos escribir-pedidos")]
    [InlineData("pedidos", "lectura-asistente", "")]
    [InlineData("Pedidos", "lectura-asistente", "leer-pedidos")]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task UnContextoParaOtraAudienciaTipoOAlcance_SeRechaza(string audiencia, string tipo, string alcance)
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var token = e.Firma.Forjar(Reclamos(cliente, Inicio, audiencia: audiencia, tipo: tipo, alcance: alcance));

        var resultado = await e.Contexto.Verificar(new ValorSecreto(token));

        Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, resultado.Error);
    }

    [Fact]
    public async Task UnContextoEmitidoEnElFuturo_OSinVida_SeRechaza()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var futuro = e.Firma.Forjar(Reclamos(cliente, Inicio.AddMinutes(1)));
        var sinVida = e.Firma.Forjar(Reclamos(cliente, Inicio, TimeSpan.Zero));
        var negativo = e.Firma.Forjar(Reclamos(cliente, Inicio, TimeSpan.FromMinutes(-1)));

        foreach (var token in new[] { futuro, sinVida, negativo })
        {
            Assert.False((await e.Contexto.Verificar(new ValorSecreto(token))).EsExito);
        }
    }

    [Fact]
    public async Task UnaFirmaInvalidaOLaFirmaCaida_ElTokenAjenoOElClienteVacio_SeRechazan_ConElMismoError()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var emitido = await e.Contexto.Emitir(cliente);
        var vacio = e.Firma.Forjar(Reclamos(Guid.Empty, Inicio));

        var desconocido = await e.Contexto.Verificar(new ValorSecreto("no-es-un-contexto"));
        var sinCliente = await e.Contexto.Verificar(new ValorSecreto(vacio));
        e.Firma.FirmaInvalida = true;
        var firmaMala = await e.Contexto.Verificar(emitido.Valor!);
        e.Firma.FirmaInvalida = false;
        e.Firma.Falla = true;
        var firmaCaida = await e.Contexto.Verificar(emitido.Valor!);
        var nulo = await e.Contexto.Verificar(null!);

        Assert.All(new[] { desconocido, sinCliente, firmaMala, firmaCaida, nulo }, r => Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, r.Error));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task SoloElPropioClienteLoPide_OtroClienteElPersonalElSistemaOUnVisitante_RecibenProhibido_YQuedaRegistrado()
    {
        var e = new Entorno();
        var cliente = e.Cuentas.Nueva();
        var otros = new[]
        {
            ActorActual.DeUsuario(Guid.NewGuid(), "cliente"),
            ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central"),
            ActorActual.DelSistema(),
            ActorActual.Anonimo(),
        };

        foreach (var actor in otros)
        {
            e.Actor.Actual = actor;
            Assert.Equal(CategoriaDeError.Prohibido, (await e.Contexto.Emitir(cliente)).Error!.Categoria);
        }

        e.Actor.Actual = null;
        Assert.Equal(CategoriaDeError.Prohibido, (await e.Contexto.Emitir(cliente)).Error!.Categoria);
        Assert.Equal(otros.Length + 1, e.Denegaciones.Registradas.Count);
        Assert.Equal(0, e.Firma.Emisiones);
    }

    [Fact]
    public async Task UnaCuentaCanceladaDesactivadaOInexistente_NoRecibeContexto()
    {
        var e = new Entorno();
        var cancelada = e.Cuentas.Nueva(cancelada: true);
        e.Actor.Actual = ActorActual.DeUsuario(cancelada, "cliente");
        Assert.Equal(CategoriaDeError.NoEncontrado, (await e.Contexto.Emitir(cancelada)).Error!.Categoria);

        var inexistente = Guid.NewGuid();
        e.Actor.Actual = ActorActual.DeUsuario(inexistente, "cliente");
        Assert.Equal(CategoriaDeError.NoEncontrado, (await e.Contexto.Emitir(inexistente)).Error!.Categoria);

        e.Actor.Actual = ActorActual.DelSistema();
        Assert.Equal(CategoriaDeError.Prohibido, (await e.Contexto.Emitir(Guid.Empty)).Error!.Categoria);
        Assert.Equal(0, e.Firma.Emisiones);
    }

    [Fact]
    public async Task SiLaCuentaSeCancelaDespuesDeEmitirElContexto_YaNoSirve()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var emitido = await e.Contexto.Emitir(cliente);
        e.Cuentas.Cuentas[cliente] = e.Cuentas.Cuentas[cliente] with { CuentaCanceladaODesactivada = true };

        Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, (await e.Contexto.Verificar(emitido.Valor!)).Error);
    }

    [Fact]
    public async Task SiIdentidadNoResponde_NoSeEmiteNiSeAcepta_FallaCerrada()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();
        var emitido = await e.Contexto.Emitir(cliente);
        e.Cuentas.Falla = true;

        Assert.False((await e.Contexto.Emitir(cliente)).EsExito);
        Assert.Equal(ContextoDeLecturaDelAsistente.ContextoInvalido, (await e.Contexto.Verificar(emitido.Valor!)).Error);
    }

    [Fact]
    public async Task CadaContextoEsDistinto_PorqueSeEmiteUnoNuevoEnCadaConsulta()
    {
        var e = new Entorno();
        var cliente = e.ClienteAutenticado();

        var uno = await e.Contexto.Emitir(cliente);
        var dos = await e.Contexto.Emitir(cliente);
        var reclamosUno = await e.Firma.Verificar(uno.Valor!.Revelar());
        var reclamosDos = await e.Firma.Verificar(dos.Valor!.Revelar());

        Assert.NotEqual(reclamosUno!.Identificador, reclamosDos!.Identificador);
    }
}
