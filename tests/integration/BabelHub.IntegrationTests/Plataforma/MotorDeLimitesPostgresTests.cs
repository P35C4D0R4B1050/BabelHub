using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class MotorDeLimitesPostgresTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private readonly UnidadDeTrabajoPostgres _delLlamador = new(bd.Acceso);
    private readonly UnidadDeTrabajoPostgres _independiente = new(bd.Acceso);

    private MotorDeLimitesPostgres Motor() => new(_delLlamador, _independiente);

    private static PoliticaDeLimite PorCliente(int maximo, TimeSpan ventana) =>
        new("reservas-por-cliente-" + Guid.NewGuid().ToString("N")[..6], maximo, ventana, TipoDeSujetoDeLimite.Usuario);

    private static PoliticaDeLimite PorOrigen(int maximo) =>
        new("reservas-por-origen-" + Guid.NewGuid().ToString("N")[..6], maximo, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    private static string Contador() => "u:s:" + Guid.NewGuid().ToString("N")[..8];

    private async Task<long> Eventos(PoliticaDeLimite politica, string contador) =>
        await bd.Consultar<long>($"SELECT count(*) FROM plataforma.eventos_limite WHERE clave = '{politica.Nombre}|{contador}'");

    /// <summary>Una reserva completa: cuenta dentro de la transacción del caso de uso y se confirma al terminar.</summary>
    private async Task<ResultadoDeLimite> Reserva(MotorDeLimitesPostgres motor, PoliticaDeLimite politica, string contador, int demoraMs = 0)
    {
        ResultadoDeLimite? resultado = null;
        await _delLlamador.Ejecutar(
            async token =>
            {
                resultado = await motor.Reservar(politica, contador, null, null, token);
                if (demoraMs > 0)
                {
                    await Task.Delay(demoraMs, token);
                }

                return 0;
            });
        return resultado!;
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task Reservar_ConCincoReservasEnLaUltimaHora_RechazaLaSextaConElInstanteEnQuePodraReintentar()
    {
        var motor = Motor();
        var politica = PorCliente(5, TimeSpan.FromHours(1));
        var cliente = Contador();

        var primera = await Reserva(motor, politica, cliente);
        for (var i = 1; i < 5; i++)
        {
            Assert.True((await Reserva(motor, politica, cliente)).EsPermitido);
        }

        var sexta = await Reserva(motor, politica, cliente);

        Assert.Equal(EstadoDeLimite.Excedido, sexta.Estado);
        Assert.InRange((sexta.DisponibleDesde!.Value - (primera.Reserva!.ReservadaEn + TimeSpan.FromHours(1))).TotalMilliseconds, -5, 5);
        Assert.InRange(sexta.EsperaEnSegundos!.Value, 3590, 3600);
        Assert.Equal(5, await Eventos(politica, cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task Reservar_ConUnaReservaRechazadaPorStock_NoCuentaEntreLasCinco()
    {
        var motor = Motor();
        var politica = PorCliente(5, TimeSpan.FromHours(1));
        var cliente = Contador();
        for (var i = 0; i < 4; i++)
        {
            await Reserva(motor, politica, cliente);
        }

        // La quinta pasa el límite pero el stock no alcanza: la transacción de la reserva se revierte y el evento con ella.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _delLlamador.Ejecutar<int>(async token =>
        {
            Assert.True((await motor.Reservar(politica, cliente, null, null, token)).EsPermitido);
            throw new InvalidOperationException("stock insuficiente");
        }));
        Assert.Equal(4, await Eventos(politica, cliente));

        // Un caso de uso que devuelve un fallo esperado también revierte por `TransaccionPorCasoDeUso`.
        var casoDeUso = new TransaccionPorCasoDeUso<int, string>(new ReservaSinStock(motor, politica, cliente), _delLlamador);
        var fallo = await casoDeUso.Ejecutar(1);
        Assert.False(fallo.EsExito);
        Assert.Equal(4, await Eventos(politica, cliente));

        Assert.True((await Reserva(motor, politica, cliente)).EsPermitido);
        Assert.Equal(5, await Eventos(politica, cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task Liberar_ConUnaReservaQueNoLlegoAIniciarseYCuyaTransaccionSeConfirma_QuitaSuEvento()
    {
        var motor = Motor();
        var politica = PorCliente(5, TimeSpan.FromHours(1));
        var cliente = Contador();

        await _delLlamador.Ejecutar(async token =>
        {
            var reserva = (await motor.Reservar(politica, cliente, null, null, token)).Reserva!;
            await motor.Liberar(politica, reserva, token);
            return 0;
        });

        Assert.Equal(0, await Eventos(politica, cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    [Trait("Criterio", "AC-SEG-21")]
    public async Task Reservar_ConDoceCuentasDelMismoOrigenAlMismoTiempo_AceptaExactamenteDiez()
    {
        var motor = Motor();
        var politica = PorOrigen(10);
        var origen = "o:v1:" + Guid.NewGuid().ToString("N");

        var resultados = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Reserva(motor, politica, origen, demoraMs: 150))));

        Assert.Equal(10, resultados.Count(r => r.EsPermitido));
        Assert.Equal(2, resultados.Count(r => r.Estado == EstadoDeLimite.Excedido));
        Assert.Equal(10, await Eventos(politica, origen));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-21")]
    public async Task Reservar_ConVentanaDeslizante_LasEntradasViejasDejanDeContar()
    {
        var motor = Motor();
        var politica = PorCliente(2, TimeSpan.FromMilliseconds(1500));
        var cliente = Contador();

        await Reserva(motor, politica, cliente);
        await Task.Delay(900);
        await Reserva(motor, politica, cliente);
        Assert.Equal(EstadoDeLimite.Excedido, (await Reserva(motor, politica, cliente)).Estado);

        await Task.Delay(700);
        Assert.True((await Reserva(motor, politica, cliente)).EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, (await Reserva(motor, politica, cliente)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public async Task Reservar_ConElPresupuestoDelPersonal_RechazaElSesentaYUnoYNoAfectaAOtroEncargado()
    {
        var motor = Motor();
        var politica = new PoliticaDeLimite("ajustes-del-personal", 60, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);
        var encargado = Contador();

        for (var i = 0; i < 60; i++)
        {
            Assert.True((await motor.Reservar(politica, encargado, null, null, CancellationToken.None)).EsPermitido);
        }

        var rechazo = await motor.Reservar(politica, encargado, null, null, CancellationToken.None);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.True((await motor.Reservar(politica, Contador(), null, null, CancellationToken.None)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public async Task Reservar_ConElPresupuestoDelPersonal_CuentaElIntentoAunqueLaOperacionFalle()
    {
        var motor = Motor();
        var politica = new PoliticaDeLimite("traslados-del-personal", 30, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);
        var encargado = Contador();

        // Transacción independiente: el intento cuenta aunque la transacción del llamador se revierta.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _delLlamador.Ejecutar<int>(async token =>
        {
            await motor.Reservar(politica, encargado, null, null, token);
            throw new InvalidOperationException("la operación falla");
        }));

        Assert.Equal(1, await Eventos(politica, encargado));
    }

    [Fact]
    public async Task Motor_ConIdempotenciaOBloqueoConDuracion_NoLoSoportaYLoDiceClaro()
    {
        var motor = Motor();
        var politica = PorCliente(5, TimeSpan.FromHours(1));
        var conBloqueo = new PoliticaDeLimite("con-bloqueo", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario, TimeSpan.FromMinutes(15));

        await Assert.ThrowsAsync<NotSupportedException>(() => motor.Reservar(politica, Contador(), "aviso-1", null, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => motor.Reservar(politica, Contador(), null, new ClaveDeIdempotencia("c", "/r", "h"), CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => motor.Reservar(conBloqueo, Contador(), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task LaTablaDeEventos_SoloLaEscribeElRolDelEsquemaPlataforma()
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand("SELECT count(*) FROM plataforma.eventos_limite", conexion);

        var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteScalarAsync());

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task PurgarVencidos_BorraLosEventosDeMasDe24HorasYConservaLosRecientes()
    {
        var motor = Motor();
        var politica = PorCliente(5, TimeSpan.FromHours(1));
        var cliente = Contador();
        await Reserva(motor, politica, cliente);
        await bd.Consultar<int>($"INSERT INTO plataforma.eventos_limite VALUES (gen_random_uuid(), '{politica.Nombre}|{cliente}', now() - interval '25 hours') RETURNING 1");

        var borrados = await motor.PurgarVencidos();

        Assert.True(borrados >= 1);
        Assert.Equal(1, await Eventos(politica, cliente));
    }

    [Fact]
    public async Task RegistrarRechazoDeOrigen_NoCuentaNadaEnElInventario()
    {
        var motor = Motor();
        var origen = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");

        Assert.Equal(0, await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.Equal(0, await motor.ContarRechazosDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    private sealed class ReservaSinStock(MotorDeLimitesPostgres motor, PoliticaDeLimite politica, string cliente) : ICasoDeUso<int, string>
    {
        public async Task<Resultado<string>> Ejecutar(int entrada, CancellationToken cancelacion = default)
        {
            Assert.True((await motor.Reservar(politica, cliente, null, null, cancelacion)).EsPermitido);
            return Resultado<string>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "stock-insuficiente"));
        }
    }
}
