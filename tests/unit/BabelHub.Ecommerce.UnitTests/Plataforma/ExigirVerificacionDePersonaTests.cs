using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class ExigirVerificacionDePersonaTests
{
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";
    private const string OtroResumen = "Zq8Lw2Xv5Nc7Rb1Ty4Hu9Jk3Gf6Ds0Ae2Mp8Oi5UyW";
    private const string TokenValido = "0.AbCdEf-1234_token";
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class VerificadorDePersonaFalso : IVerificadorDePersona
    {
        public ResultadoDeVerificacionDePersona Resultado { get; set; } = ResultadoDeVerificacionDePersona.Verificada;

        public bool Lanza { get; set; }

        public List<(string Token, string Accion)> Llamadas { get; } = [];

        public Task<ResultadoDeVerificacionDePersona> Verificar(string token, string accion, CancellationToken cancelacion = default)
        {
            Llamadas.Add((token, accion));
            return Lanza ? throw new HttpRequestException("turnstile 503") : Task.FromResult(Resultado);
        }
    }

    private readonly RelojFijo _reloj = new(Inicio);
    private readonly PublicadorDeEventosEspia _eventos = new();
    private readonly VerificadorDePersonaFalso _verificador = new();
    private readonly MotorDeMemoria _motor;
    private readonly OrigenFijo _origen;
    private readonly ExigirVerificacionDePersona _exigencia;
    private readonly HuellaDeOrigen _huella = HuellaDeOrigen.Crear(Resumen, "v1");

    public ExigirVerificacionDePersonaTests()
    {
        _motor = new MotorDeMemoria(_reloj);
        _origen = new OrigenFijo(new OrigenDeSolicitud(_huella, "203.0.113.7"));
        _exigencia = new ExigirVerificacionDePersona(new OpcionesDeVerificacionDePersona(umbralDeRechazos: 5), _motor, _verificador, _origen, _eventos, _reloj);
    }

    private async Task Rechazar(int veces, HuellaDeOrigen? huella = null)
    {
        for (var i = 0; i < veces; i++)
        {
            await _motor.RegistrarRechazoDeOrigen(huella ?? _huella, EvaluarLimite.VentanaDeRechazosDelOrigen, CancellationToken.None);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConUnaPersonaSinRechazos_ContinuaSinFriccionYSinConsultarAlVerificador()
    {
        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, tokenDeVerificacion: null);

        Assert.Equal(ResultadoDeExigencia.NoExigida, resultado);
        Assert.Empty(_verificador.Llamadas);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConUnOrigenAutomatizadoSinToken_ExigeLaVerificacion()
    {
        await Rechazar(6);

        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, tokenDeVerificacion: null);

        Assert.Equal(ResultadoDeExigencia.Requerida, resultado);
        Assert.Empty(_verificador.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData(5, ResultadoDeExigencia.NoExigida)]
    [InlineData(6, ResultadoDeExigencia.Requerida)]
    public async Task Exigir_EnElBordeDelUmbral_ExigeSoloConMasRechazosQueElUmbral(int rechazos, ResultadoDeExigencia esperado)
    {
        await Rechazar(rechazos);

        Assert.Equal(esperado, await _exigencia.Exigir(AccionProtegida.Ingreso, null));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConUnOrigenAutomatizadoQueSuperaLaVerificacion_ContinuaYSeLaPideConLaAccionDeLaPagina()
    {
        await Rechazar(6);

        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, TokenValido);

        Assert.Equal(ResultadoDeExigencia.Verificada, resultado);
        Assert.Equal((TokenValido, "registro"), Assert.Single(_verificador.Llamadas));
        Assert.Empty(_eventos.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData(AccionProtegida.Ingreso, "ingreso")]
    [InlineData(AccionProtegida.Registro, "registro")]
    [InlineData(AccionProtegida.HojaDeReclamacion, "hoja-de-reclamacion")]
    [InlineData(AccionProtegida.Reserva, "reserva")]
    [InlineData(AccionProtegida.Asistente, "asistente")]
    public async Task Exigir_ParaCadaAccionProtegida_UsaSuNombreEstable(AccionProtegida accion, string nombre)
    {
        await Rechazar(6);

        await _exigencia.Exigir(accion, TokenValido);

        Assert.Equal(nombre, Assert.Single(_verificador.Llamadas).Accion);
        Assert.Equal(nombre, accion.NombreEstable());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConUnTokenQueLaVerificacionRechaza_NoPermiteContinuarYRegistraUnEventoSinElToken()
    {
        await Rechazar(6);
        _verificador.Resultado = ResultadoDeVerificacionDePersona.Fallida;

        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, TokenValido);

        Assert.Equal(ResultadoDeExigencia.Fallida, resultado);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal(TipoDeEventoDeSeguridad.VerificacionDePersonaFallida, evento.Tipo);
        Assert.Equal(ExigirVerificacionDePersona.MotivoVerificacionRechazada, evento.Motivo);
        Assert.Equal("203.0.113.7", evento.DireccionCompleta);
        Assert.DoesNotContain(TokenValido, evento.Motivo, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData("con espacio")]
    [InlineData("<script>")]
    [InlineData("a/b")]
    public async Task Exigir_ConUnTokenMalformado_FallaSinConsultarAlServicioExterno(string token)
    {
        await Rechazar(6);

        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, token);

        Assert.Equal(ResultadoDeExigencia.Fallida, resultado);
        Assert.Empty(_verificador.Llamadas);
        Assert.Equal(ExigirVerificacionDePersona.MotivoTokenMalformado, Assert.Single(_eventos.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConUnTokenDemasiadoLargo_Falla()
    {
        await Rechazar(6);

        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, new string('a', 2049));

        Assert.Equal(ResultadoDeExigencia.Fallida, resultado);
        Assert.Empty(_verificador.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exigir_ConLaVerificacionCaida_FallaCerrado(bool lanza)
    {
        await Rechazar(6);
        _verificador.Lanza = lanza;
        _verificador.Resultado = ResultadoDeVerificacionDePersona.NoDisponible;

        var resultado = await _exigencia.Exigir(AccionProtegida.Registro, TokenValido);

        Assert.Equal(ResultadoDeExigencia.NoDisponible, resultado);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConRechazosDeOtroOrigen_NoAfectaALaPersona()
    {
        await Rechazar(50, HuellaDeOrigen.Crear(OtroResumen, "v1"));

        Assert.Equal(ResultadoDeExigencia.NoExigida, await _exigencia.Exigir(AccionProtegida.Registro, null));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConRechazosYaFueraDeLaVentana_NoCuentan()
    {
        await Rechazar(6);
        _reloj.Ahora = Inicio + EvaluarLimite.VentanaDeRechazosDelOrigen + TimeSpan.FromSeconds(1);

        Assert.Equal(ResultadoDeExigencia.NoExigida, await _exigencia.Exigir(AccionProtegida.Registro, null));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Exigir_ConElContadorCaido_NoPuedeJuzgarYNoExige()
    {
        await Rechazar(6);
        _motor.NoDisponible = true;

        Assert.Equal(ResultadoDeExigencia.NoExigida, await _exigencia.Exigir(AccionProtegida.Registro, null));
    }

    [Fact]
    public void Opciones_ConValoresInvalidos_Lanzan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesDeVerificacionDePersona(umbralDeRechazos: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesDeVerificacionDePersona(ventana: TimeSpan.Zero));
        Assert.Equal(20, new OpcionesDeVerificacionDePersona().UmbralDeRechazos);
    }
}
