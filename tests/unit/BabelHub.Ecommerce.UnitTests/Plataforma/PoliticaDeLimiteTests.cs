using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class PoliticaDeLimiteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static PoliticaDeLimite PorMinuto(int maximo, TipoDeSujetoDeLimite tipo = TipoDeSujetoDeLimite.Usuario) =>
        new("solicitudes-por-minuto", maximo, TimeSpan.FromMinutes(1), tipo);

    private static List<DateTimeOffset> Entradas(int cantidad, TimeSpan desde, TimeSpan hasta)
    {
        // Reparte las entradas de forma uniforme entre «ahora - desde» y «ahora - hasta».
        var lista = new List<DateTimeOffset>();
        for (var i = 0; i < cantidad; i++)
        {
            var fraccion = cantidad == 1 ? 0 : (double)i / (cantidad - 1);
            lista.Add(Ahora - desde + ((desde - hasta) * fraccion));
        }

        return lista;
    }

    [Fact]
    public void Constructor_ConValoresPorDefecto_UsaLosSegurosDeLaEspecificacion()
    {
        var politica = PorMinuto(100);

        Assert.False(politica.Liberable);
        Assert.True(politica.PublicarEvento);
        Assert.False(politica.RespaldoLocal);
        Assert.Null(politica.DuracionDeBloqueo);
    }

    [Theory]
    [InlineData("", 1, 60, false)]
    [InlineData("  ", 1, 60, false)]
    [InlineData("x", 0, 60, false)]
    [InlineData("x", -1, 60, false)]
    [InlineData("x", 1, 0, false)]
    [InlineData("x", 1, 60, true)]
    public void Constructor_ConDatosInvalidos_Lanza(string nombre, int maximo, int segundosDeVentana, bool bloqueoCero)
    {
        TimeSpan? bloqueo = bloqueoCero ? TimeSpan.Zero : null;

        Assert.Throws<ArgumentException>(() =>
            new PoliticaDeLimite(nombre, maximo, TimeSpan.FromSeconds(segundosDeVentana), TipoDeSujetoDeLimite.Usuario, bloqueo));
    }

    [Fact]
    public void Constructor_PorNumeroConRespaldoLocal_Lanza()
    {
        Assert.Throws<ArgumentException>(() =>
            new PoliticaDeLimite("avisos-por-numero", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Numero, respaldoLocal: true));
    }

    [Fact]
    public void Constructor_SinPublicarEventoSoloSePermitePorNumero()
    {
        Assert.Throws<ArgumentException>(() =>
            new PoliticaDeLimite("x", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario, publicarEvento: false));

        var porNumero = new PoliticaDeLimite("avisos-por-numero", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Numero, publicarEvento: false);
        Assert.False(porNumero.PublicarEvento);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public void Evaluar_ConCienSolicitudesEnElUltimoMinuto_RechazaLaSiguienteYVuelveAFuncionarAlTiempoSugerido()
    {
        var politica = PorMinuto(100);
        var entradas = Entradas(100, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(1));

        var decision = politica.Evaluar(entradas, bloqueoHasta: null, Ahora);

        Assert.False(decision.Permitida);
        var respuesta = Assert.IsType<RespuestaDeLimite>(decision.Respuesta);
        Assert.Equal(entradas.Min() + TimeSpan.FromMinutes(1), respuesta.DisponibleDesde);
        Assert.Equal(10, respuesta.EsperaEnSegundos);
        Assert.Equal("solicitudes-por-minuto", respuesta.Motivo);

        // Pasado el tiempo sugerido, la solicitud se atiende.
        var despues = politica.Evaluar(entradas, bloqueoHasta: null, respuesta.DisponibleDesde);
        Assert.True(despues.Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-16")]
    public void Evaluar_ConSesentaSolicitudesDeUnVisitanteEnElUltimoMinuto_RechazaLaSiguiente()
    {
        var politica = new PoliticaDeLimite("solicitudes-sin-sesion-por-minuto", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen);
        var entradas = Entradas(60, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(1));

        var decision = politica.Evaluar(entradas, bloqueoHasta: null, Ahora);

        Assert.False(decision.Permitida);
        Assert.NotNull(decision.Respuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public void Evaluar_ConMenosDelMaximo_Permite()
    {
        var politica = PorMinuto(100);

        var decision = politica.Evaluar(Entradas(99, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(1)), bloqueoHasta: null, Ahora);

        Assert.True(decision.Permitida);
        Assert.Null(decision.Respuesta);
        Assert.Null(decision.BloqueoHastaNuevo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public void Evaluar_ConMasEntradasQueElMaximo_SugiereEsperarHastaQueBajeDelMaximo()
    {
        var politica = PorMinuto(3);
        var entradas = new List<DateTimeOffset>
        {
            Ahora - TimeSpan.FromSeconds(50),
            Ahora - TimeSpan.FromSeconds(40),
            Ahora - TimeSpan.FromSeconds(30),
            Ahora - TimeSpan.FromSeconds(20),
            Ahora - TimeSpan.FromSeconds(10),
        };

        var decision = politica.Evaluar(entradas, bloqueoHasta: null, Ahora);

        // Hay 5 vivas con máximo 3: deben salir 3 para poder aceptar una más (la tercera en vencer).
        Assert.False(decision.Permitida);
        Assert.Equal(entradas[2] + TimeSpan.FromMinutes(1), decision.Respuesta!.DisponibleDesde);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public void Evaluar_ConUnaEntradaJustoEnElBordeDeLaVentana_YaNoLaCuenta()
    {
        var politica = PorMinuto(1);
        var entradas = new List<DateTimeOffset> { Ahora - TimeSpan.FromMinutes(1) };

        Assert.True(politica.Evaluar(entradas, bloqueoHasta: null, Ahora).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public void Evaluar_ConVentanaDeslizante_NoPermiteUnaRafagaDobleEnElCambioDeVentana()
    {
        // 100 solicitudes en los últimos 10 segundos del «minuto anterior» y otra a los 10 segundos del siguiente:
        // una ventana fija las dejaría pasar; la deslizante rechaza.
        var politica = PorMinuto(100);
        var entradas = Entradas(100, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10));

        Assert.False(politica.Evaluar(entradas, bloqueoHasta: null, Ahora).Permitida);
    }

    [Fact]
    public void Evaluar_ConLaUltimaEntradaQueAlcanzaElMaximoYBloqueo_CreaElBloqueo()
    {
        var politica = new PoliticaDeLimite(
            "ingresos-fallidos", 5, TimeSpan.FromMinutes(15), TipoDeSujetoDeLimite.Combinacion,
            duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);
        var entradas = Entradas(4, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1));

        var decision = politica.Evaluar(entradas, bloqueoHasta: null, Ahora);

        Assert.True(decision.Permitida);
        Assert.Equal(Ahora + TimeSpan.FromMinutes(15), decision.BloqueoHastaNuevo);
    }

    [Fact]
    public void Evaluar_ConUnaPoliticaSinBloqueo_NuncaCreaBloqueo()
    {
        var politica = PorMinuto(5);

        var decision = politica.Evaluar(Entradas(4, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(1)), bloqueoHasta: null, Ahora);

        Assert.True(decision.Permitida);
        Assert.Null(decision.BloqueoHastaNuevo);
    }

    [Fact]
    public void Evaluar_ConBloqueoVigente_RechazaHastaElFinDelBloqueoSinExtenderlo()
    {
        var politica = new PoliticaDeLimite(
            "ingresos-fallidos", 5, TimeSpan.FromMinutes(15), TipoDeSujetoDeLimite.Combinacion,
            duracionDeBloqueo: TimeSpan.FromMinutes(15));
        var bloqueoHasta = Ahora + TimeSpan.FromMinutes(7);

        var decision = politica.Evaluar(new List<DateTimeOffset>(), bloqueoHasta, Ahora);

        Assert.False(decision.Permitida);
        Assert.Equal(bloqueoHasta, decision.Respuesta!.DisponibleDesde);
        Assert.Equal(7 * 60, decision.Respuesta.EsperaEnSegundos);
        Assert.Null(decision.BloqueoHastaNuevo);
    }

    [Fact]
    public void Evaluar_ConUnBloqueoYaVencido_NoLoTieneEnCuenta()
    {
        var politica = PorMinuto(5);

        var decision = politica.Evaluar(new List<DateTimeOffset>(), Ahora - TimeSpan.FromSeconds(1), Ahora);

        Assert.True(decision.Permitida);
    }

    [Fact]
    public void Evaluar_ConVentanaDeUnaHoraTrasElBloqueo_SigueContandoYNoRecreaElBloqueo()
    {
        // SEG-29: 10 fallos por hora con bloqueo de 15 minutos; la ventana no se vacía (D26).
        var politica = new PoliticaDeLimite(
            "segundo-factor-por-cuenta", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario,
            duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);
        var inicio = Ahora - TimeSpan.FromMinutes(40);
        var entradas = Enumerable.Range(0, 10).Select(i => inicio + TimeSpan.FromMinutes(i * 2)).ToList();
        var bloqueoVencido = entradas[^1] + TimeSpan.FromMinutes(15); // 33 minutos después del inicio: ya pasó

        var decision = politica.Evaluar(entradas, bloqueoVencido, Ahora);

        Assert.False(decision.Permitida);
        Assert.Equal(entradas[0] + TimeSpan.FromHours(1), decision.Respuesta!.DisponibleDesde);
        Assert.Null(decision.BloqueoHastaNuevo);
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(1.0, 1)]
    [InlineData(1.01, 2)]
    [InlineData(59.5, 60)]
    public void RespuestaDeLimite_RedondeaLaEsperaHaciaArribaConUnMinimoDeUnSegundo(double segundos, int esperado)
    {
        var respuesta = RespuestaDeLimite.Crear("motivo", Ahora, Ahora + TimeSpan.FromSeconds(segundos));

        Assert.Equal(esperado, respuesta.EsperaEnSegundos);
        Assert.Equal(Ahora + TimeSpan.FromSeconds(segundos), respuesta.DisponibleDesde);
    }

    [Fact]
    public void RespuestaDeLimite_ConUnInstanteQueYaPaso_Lanza()
    {
        Assert.Throws<ArgumentException>(() => RespuestaDeLimite.Crear("motivo", Ahora, Ahora));
    }
}
