using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class DependenciasExternasTests
{
    private sealed record Cotizacion(decimal Monto, string Moneda);

    private sealed class ValidadorDeCotizacion : IValidadorDeRespuestaExterna<Cotizacion>
    {
        public ResultadoDeValidacion Validar(Cotizacion respuesta) =>
            new ConstructorDeValidacion()
                .Rango("monto", respuesta.Monto, 0m, 100_000m)
                .Texto("moneda", respuesta.Moneda, 3, 3)
                .Resultado();
    }

    private static readonly OpcionesDeDependencias Rapidas = new(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(400));
    private readonly DependenciasExternas _dependencias = new(Rapidas);
    private readonly ValidadorDeCotizacion _validador = new();

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public void Plazos_PorDefecto_SonDiezSegundosYTreintaParaElModeloDeLenguaje()
    {
        var porDefecto = new OpcionesDeDependencias();

        Assert.Equal(TimeSpan.FromSeconds(10), porDefecto.Espera(ClaseDeDependencia.Normal));
        Assert.Equal(TimeSpan.FromSeconds(30), porDefecto.Espera(ClaseDeDependencia.ProveedorDeLlm));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesDeDependencias(TimeSpan.Zero));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-12")]
    public async Task Consultar_ConUnaRespuestaValida_LaDevuelve()
    {
        var respuesta = await _dependencias.Consultar("cotizador", ClaseDeDependencia.Normal, _ => Task.FromResult(new Cotizacion(25m, "PEN")), _validador);

        Assert.True(respuesta.HayRespuesta);
        Assert.Equal(25m, respuesta.Valor.Monto);
        Assert.Null(respuesta.Motivo);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-12")]
    [InlineData(-5, "PEN")]
    [InlineData(999_999, "PEN")]
    [InlineData(10, "SOLES")]
    [InlineData(10, "")]
    public async Task Consultar_ConUnFormatoInesperado_LaDescartaComoSiNoHubieraRespondido(double monto, string moneda)
    {
        var respuesta = await _dependencias.Consultar("cotizador", ClaseDeDependencia.Normal, _ => Task.FromResult(new Cotizacion((decimal)monto, moneda)), _validador);

        Assert.False(respuesta.HayRespuesta);
        Assert.Equal(MotivoDeSinRespuesta.RespuestaInvalida, respuesta.Motivo);
        Assert.Throws<InvalidOperationException>(() => respuesta.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Consultar_ConUnaDependenciaQueNoResponde_CortaLaEsperaYInformaQueNoEstaDisponible()
    {
        var inicio = DateTime.UtcNow;

        var respuesta = await _dependencias.Consultar<Cotizacion>(
            "cotizador", ClaseDeDependencia.Normal, async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new Cotizacion(1m, "PEN");
            }, _validador);

        Assert.True(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(5));
        Assert.Equal(MotivoDeSinRespuesta.TiempoAgotado, respuesta.Motivo);
        var necesaria = respuesta.ComoNecesaria();
        Assert.False(necesaria.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, necesaria.Error!.Categoria);
        Assert.Equal(RespuestaExterna<Cotizacion>.ClaveServicioNoDisponible, necesaria.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Consultar_ConUnaLlamadaQueIgnoraLaCancelacion_IgualCortaAlVencerElPlazo()
    {
        var liberar = new TaskCompletionSource<Cotizacion>();

        var respuesta = await _dependencias.Consultar("cotizador", ClaseDeDependencia.Normal, _ => liberar.Task, _validador);

        Assert.Equal(MotivoDeSinRespuesta.TiempoAgotado, respuesta.Motivo);
        liberar.SetException(new InvalidOperationException("tardía"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Consultar_ConUnaDependenciaNoNecesariaSinRespuesta_LaOperacionPuedeContinuarSinElla()
    {
        var aviso = await _dependencias.Consultar<Cotizacion>(
            "cache", ClaseDeDependencia.Normal, _ => throw new InvalidOperationException("cache caída"), _validador);

        Assert.False(aviso.HayRespuesta);
        Assert.Equal(MotivoDeSinRespuesta.Fallo, aviso.Motivo);

        // Quien no la necesita simplemente sigue: no hay excepción ni error de aplicación.
        var resultadoDeLaOperacion = aviso.HayRespuesta ? "con cache" : "sin cache";
        Assert.Equal("sin cache", resultadoDeLaOperacion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Consultar_ConUnaDependenciaLenta_NoDetieneLasOtrasOperaciones()
    {
        var lenta = _dependencias.Consultar<Cotizacion>(
            "cotizador", ClaseDeDependencia.Normal, async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new Cotizacion(1m, "PEN");
            }, _validador);

        var rapida = await _dependencias.Consultar("catalogo", ClaseDeDependencia.Normal, _ => Task.FromResult(new Cotizacion(5m, "PEN")), _validador);

        Assert.True(rapida.HayRespuesta);
        Assert.False((await lenta).HayRespuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Consultar_ConElProveedorDelModeloDeLenguaje_EsperaMasQueUnaDependenciaNormal()
    {
        async Task<Cotizacion> Responde(CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), token);
            return new Cotizacion(3m, "PEN");
        }

        var normal = await _dependencias.Consultar("cotizador", ClaseDeDependencia.Normal, Responde, _validador);
        var llm = await _dependencias.Consultar("llm", ClaseDeDependencia.ProveedorDeLlm, Responde, _validador);

        Assert.Equal(MotivoDeSinRespuesta.TiempoAgotado, normal.Motivo);
        Assert.True(llm.HayRespuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Consultar_ConUnaExcepcionDeLaDependencia_NoLaPropagaYLaTrataComoFallo()
    {
        var respuesta = await _dependencias.Consultar<Cotizacion>(
            "cotizador", ClaseDeDependencia.Normal, _ => Task.FromException<Cotizacion>(new HttpRequestException("503")), _validador);

        Assert.Equal(MotivoDeSinRespuesta.Fallo, respuesta.Motivo);
    }

    [Fact]
    public async Task Consultar_ConLaCancelacionDelLlamador_SiLaPropaga()
    {
        using var fuente = new CancellationTokenSource();
        await fuente.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _dependencias.Consultar(
            "cotizador", ClaseDeDependencia.Normal, async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new Cotizacion(1m, "PEN");
            }, _validador, fuente.Token));
    }

    [Fact]
    public async Task Consultar_ConArgumentosInvalidos_Lanza()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _dependencias.Consultar(" ", ClaseDeDependencia.Normal, _ => Task.FromResult(new Cotizacion(1m, "PEN")), _validador));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _dependencias.Consultar<Cotizacion>("x", ClaseDeDependencia.Normal, null!, _validador));
    }

    [Fact]
    public void ComoNecesaria_ConRespuesta_EsExito()
    {
        var resultado = RespuestaExterna<Cotizacion>.ConRespuesta(new Cotizacion(1m, "PEN")).ComoNecesaria();

        Assert.True(resultado.EsExito);
    }
}
