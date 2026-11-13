using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Limites;

public sealed class MotorDeLimitesLocalTests
{
    private sealed class TiempoControlable(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;

        public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;
    }

    private readonly TiempoControlable _tiempo = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));

    private MotorDeLimitesLocal Motor(OpcionesDeMotorLocal? opciones = null) => new(opciones, _tiempo);

    private static PoliticaDeLimite Politica(int maximo, TimeSpan ventana, TimeSpan? bloqueo = null, bool liberable = false) =>
        new("local-" + Guid.NewGuid().ToString("N")[..8], maximo, ventana, TipoDeSujetoDeLimite.Usuario, bloqueo, liberable);

    private static Task<ResultadoDeLimite> Reservar(MotorDeLimitesLocal motor, PoliticaDeLimite politica, string contador = "u:s:a", string? claveDelLlamador = null, ClaveDeIdempotencia? idempotencia = null) =>
        motor.Reservar(politica, contador, claveDelLlamador, idempotencia, CancellationToken.None);

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Reservar_ConElMaximoAlcanzado_RechazaConLaEsperaDesdeLaEntradaMasAntiguaYVuelveAFuncionar()
    {
        var motor = Motor();
        var politica = Politica(3, TimeSpan.FromMinutes(1));

        var primera = await Reservar(motor, politica);
        _tiempo.Avanzar(TimeSpan.FromSeconds(10));
        await Reservar(motor, politica);
        await Reservar(motor, politica);
        var rechazo = await Reservar(motor, politica);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.Equal(primera.Reserva!.ReservadaEn + TimeSpan.FromMinutes(1), rechazo.DisponibleDesde);
        Assert.Equal(50, rechazo.EsperaEnSegundos);

        _tiempo.Avanzar(TimeSpan.FromSeconds(50));
        Assert.True((await Reservar(motor, politica)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Reservar_ConContadoresDistintos_CuentaPorSeparado()
    {
        var motor = Motor();
        var politica = Politica(1, TimeSpan.FromMinutes(1));

        await Reservar(motor, politica, "u:s:a");

        Assert.Equal(EstadoDeLimite.Excedido, (await Reservar(motor, politica, "u:s:a")).Estado);
        Assert.True((await Reservar(motor, politica, "u:s:b")).EsPermitido);
        Assert.True((await Reservar(motor, Politica(1, TimeSpan.FromMinutes(1)), "u:s:a")).EsPermitido);
    }

    [Fact]
    public async Task Reservar_ConCincoFallosEnQuinceMinutos_BloqueaSinExtenderElBloqueoYLiberarQuitaElBloqueo()
    {
        var motor = Motor();
        var politica = Politica(5, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15), liberable: true);

        ReservaDeLimite? quinta = null;
        for (var i = 0; i < 5; i++)
        {
            quinta = (await Reservar(motor, politica)).Reserva;
            _tiempo.Avanzar(TimeSpan.FromMinutes(2));
        }

        var bloqueado = await Reservar(motor, politica);
        _tiempo.Avanzar(TimeSpan.FromMinutes(3));
        var denuevo = await Reservar(motor, politica);

        Assert.Equal(quinta!.ReservadaEn + TimeSpan.FromMinutes(15), bloqueado.DisponibleDesde);
        Assert.Equal(bloqueado.DisponibleDesde, denuevo.DisponibleDesde);

        // Si el quinto intento era el correcto (se libera), el titular no queda bloqueado.
        await motor.Liberar(politica, quinta, CancellationToken.None);
        Assert.True((await Reservar(motor, politica)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-26")]
    public async Task Reservar_ConDiezFallosEnCincuentaMinutosYUnIntentoAlTerminarElBloqueo_SigueRechazandoHastaQueExpireLaMasAntigua()
    {
        var motor = Motor();
        var politica = Politica(10, TimeSpan.FromHours(1), TimeSpan.FromMinutes(15), liberable: true);

        _tiempo.Avanzar(TimeSpan.FromMinutes(20));
        var primero = await Reservar(motor, politica);
        for (var i = 1; i < 10; i++)
        {
            _tiempo.Avanzar(TimeSpan.FromMinutes(30.0 / 9));
            Assert.True((await Reservar(motor, politica)).EsPermitido);
        }

        _tiempo.Avanzar(TimeSpan.FromMinutes(15));
        var alTerminarElBloqueo = await Reservar(motor, politica);

        Assert.Equal(EstadoDeLimite.Excedido, alTerminarElBloqueo.Estado);
        Assert.Equal(primero.Reserva!.ReservadaEn + TimeSpan.FromHours(1), alTerminarElBloqueo.DisponibleDesde);
    }

    [Fact]
    public async Task Reservar_ConLaMismaClaveDelLlamador_DevuelveLaMismaReservaSinContarDeNuevoYLiberarLaBorra()
    {
        var motor = Motor();
        var politica = Politica(2, TimeSpan.FromHours(1), liberable: true);

        var primera = await Reservar(motor, politica, claveDelLlamador: "aviso-1");
        var repetida = await Reservar(motor, politica, claveDelLlamador: "aviso-1");
        await Reservar(motor, politica, claveDelLlamador: "aviso-2");

        Assert.Equal(primera.Reserva!.Identificador, repetida.Reserva!.Identificador);
        Assert.Equal(EstadoDeLimite.Excedido, (await Reservar(motor, politica, claveDelLlamador: "aviso-3")).Estado);

        await motor.Liberar(politica, primera.Reserva, CancellationToken.None);
        Assert.True((await Reservar(motor, politica, claveDelLlamador: "aviso-3")).EsPermitido);
        Assert.NotEqual(primera.Reserva.Identificador, (await Reservar(motor, politica, claveDelLlamador: "aviso-1")).Reserva?.Identificador);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Reservar_ConLaIdempotenciaDelCliente_ControlaEnCursoCompletadaConflictoYFalla()
    {
        var motor = Motor();
        var politica = Politica(5, TimeSpan.FromHours(1));
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-a");

        var primera = await Reservar(motor, politica, idempotencia: clave);
        var enCurso = await Reservar(motor, politica, idempotencia: clave);
        var conflicto = await Reservar(motor, politica, idempotencia: new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-b"));
        await motor.Completar(primera.Reserva!, new ResultadoMinimo("creada", "res-9"), CancellationToken.None);
        var completada = await Reservar(motor, politica, idempotencia: clave);

        Assert.Equal(EstadoDeLimite.RepeticionEnCurso, enCurso.Estado);
        Assert.Equal(EstadoDeLimite.ClaveEnConflicto, conflicto.Estado);
        Assert.Equal("res-9", completada.ResultadoGuardado!.IdDelRecurso);

        var otra = new ClaveDeIdempotencia("reserva-2", "/reservas", "cuerpo-c");
        var fallida = await Reservar(motor, politica, idempotencia: otra);
        await motor.Fallar(fallida.Reserva!, CancellationToken.None);
        Assert.True((await Reservar(motor, politica, idempotencia: otra)).EsPermitido);
    }

    [Fact]
    public async Task Reservar_ConLaIdempotenciaEnCursoVencida_PermiteReintentar()
    {
        var motor = Motor();
        var politica = Politica(5, TimeSpan.FromHours(1));
        var clave = new ClaveDeIdempotencia("reserva-9", "/reservas", "cuerpo");
        await Reservar(motor, politica, idempotencia: clave);

        _tiempo.Avanzar(TimeSpan.FromSeconds(61));

        Assert.True((await Reservar(motor, politica, idempotencia: clave)).EsPermitido);
    }

    [Fact]
    public async Task Reservar_ConSolicitudesSimultaneas_NuncaSuperaElMaximo()
    {
        var motor = Motor();
        var politica = Politica(50, TimeSpan.FromMinutes(5));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 400).Select(_ => Task.Run(() => Reservar(motor, politica))));

        Assert.Equal(50, resultados.Count(r => r.EsPermitido));
        Assert.Equal(350, resultados.Count(r => r.Estado == EstadoDeLimite.Excedido));
    }

    [Fact]
    public async Task Reservar_ConMasContadoresQueElTope_PurgaLosVencidosYDescartaElMasAntiguoSinLanzar()
    {
        var motor = Motor(new OpcionesDeMotorLocal { TopeDeContadores = 20, MargenDeExpiracion = TimeSpan.FromSeconds(1) });
        var politica = Politica(5, TimeSpan.FromSeconds(10));

        for (var i = 0; i < 20; i++)
        {
            await Reservar(motor, politica, $"u:s:{i}");
            _tiempo.Avanzar(TimeSpan.FromMilliseconds(100));
        }

        // Con todos vigentes, uno nuevo descarta el de actividad más antigua y la memoria no crece.
        Assert.True((await Reservar(motor, politica, "u:s:nuevo")).EsPermitido);
        Assert.True(motor.ContadoresEnMemoria <= 20);

        // Pasado el tiempo, los vencidos se purgan al hacer lugar.
        _tiempo.Avanzar(TimeSpan.FromMinutes(1));
        Assert.True((await Reservar(motor, politica, "u:s:otro")).EsPermitido);
        Assert.Equal(1, motor.ContadoresEnMemoria);
    }

    [Fact]
    public async Task RegistrarRechazoDeOrigen_CuentaLosRechazosDeLaVentanaPorOrigen()
    {
        var motor = Motor();
        var origen = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");
        var otro = HuellaDeOrigen.Crear("Zq8Lw2Xv5Nc7Rb1Ty4Hu9Jk3Gf6Ds0Ae2Mp8Oi5UyW", "v1");

        await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None);
        _tiempo.Avanzar(TimeSpan.FromMinutes(3));
        var dos = await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None);
        _tiempo.Avanzar(TimeSpan.FromMinutes(3));

        Assert.Equal(2, dos);
        Assert.Equal(1, await motor.ContarRechazosDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.Equal(0, await motor.ContarRechazosDeOrigen(otro, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    [Fact]
    public async Task Motor_ConArgumentosInvalidos_Lanza()
    {
        var motor = Motor();

        await Assert.ThrowsAsync<ArgumentNullException>(() => motor.Reservar(null!, "u:s:a", null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => motor.Reservar(Politica(1, TimeSpan.FromMinutes(1)), " ", null, null, CancellationToken.None));
    }
}
