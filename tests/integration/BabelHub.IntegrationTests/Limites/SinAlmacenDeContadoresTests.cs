using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using StackExchange.Redis;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>Redis con la memoria llena: toda escritura se rechaza con OOM y el motor debe tratarlo como «almacén no disponible».</summary>
public sealed class RedisSinMemoriaFixture : RedisDeContadoresFixture
{
    protected override string MemoriaMaxima => "1mb";
}

/// <summary>SEG-26: sin el almacén de contadores (caído o con la memoria llena) las políticas que fallan cerradas no atienden y las demás usan el límite local.</summary>
public sealed class SinAlmacenDeContadoresTests(RedisSinMemoriaFixture sinMemoria) : IClassFixture<RedisSinMemoriaFixture>
{
    private static readonly ClaveDePolitica General = new("solicitudes-por-origen");

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual { get; } = new(HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1"), "203.0.113.7");
    }

    private sealed class EventosDePrueba : IPublicadorDeEventosDeSeguridad
    {
        public List<EventoDeSeguridad> Eventos { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            Eventos.Add(evento);
            return Task.CompletedTask;
        }
    }

    private sealed class AlertasTecnicasDePrueba : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Publicadas { get; } = [];

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private static EvaluarLimite Control(IMotorDeLimites principal, MotorDeLimitesLocal local) =>
        new(
            new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Append(
                new PoliticaDeLimite(General.Valor, 3, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true))),
            principal,
            local,
            new OrigenDePrueba(),
            new EventosDePrueba(),
            new RelojDePrueba());

    private static async Task<MotorDeLimitesRedis> MotorSinServidor()
    {
        var opciones = ConfigurationOptions.Parse("127.0.0.1:1,abortConnect=false,connectTimeout=300,syncTimeout=300,asyncTimeout=300,connectRetry=1");
        var conexion = await ConnectionMultiplexer.ConnectAsync(opciones);
        return new MotorDeLimitesRedis(conexion, new OpcionesDeMotorRedis());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task ConElAlmacenCaido_ElIngresoElRegistroLosCodigosYLasReservasResponderServicioNoDisponible()
    {
        var control = Control(await MotorSinServidor(), new MotorDeLimitesLocal());

        Assert.Equal(EstadoDeLimite.NoDisponible, (await control.Reservar(PoliticasDeLimite.IngresosFallidos, sujeto: "c-1")).Estado);
        Assert.Equal(EstadoDeLimite.NoDisponible, (await control.Reservar(PoliticasDeLimite.RegistrosPorOrigen)).Estado);
        Assert.Equal(EstadoDeLimite.NoDisponible, (await control.Reservar(PoliticasDeLimite.CodigosPorOrigen)).Estado);
        Assert.Equal(EstadoDeLimite.NoDisponible, (await control.Reservar(PoliticasDeLimite.SegundoFactorPorCuenta, sujeto: "c-1")).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task ConElAlmacenCaido_ElCatalogoSigueFuncionandoConElLimiteLocal()
    {
        var control = Control(await MotorSinServidor(), new MotorDeLimitesLocal());

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await control.Reservar(General)).EsPermitido);
        }

        // El límite local sigue vigente: la cuarta solicitud se rechaza aunque no haya Redis.
        Assert.Equal(EstadoDeLimite.Excedido, (await control.Reservar(General)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task ConLaMemoriaDeRedisLlena_LasEscriturasSeTratanComoAlmacenNoDisponibleYNuncaComoPermitir()
    {
        var motor = sinMemoria.Motor();
        var politica = new PoliticaDeLimite(RedisDeContadoresFixture.Unico("llena"), 1000, TimeSpan.FromMinutes(5), TipoDeSujetoDeLimite.Usuario);

        var llena = false;
        for (var i = 0; i < 20_000 && !llena; i++)
        {
            try
            {
                var resultado = await motor.Reservar(politica, $"u:s:{i}", $"clave-de-relleno-{i}-con-algo-de-largo-para-ocupar-memoria", null, CancellationToken.None);
                Assert.True(resultado.EsPermitido);
            }
            catch (MotorDeLimitesNoDisponibleException)
            {
                llena = true;
            }
        }

        Assert.True(llena, "La instancia con 1 MB debía llenarse.");
        await Assert.ThrowsAsync<MotorDeLimitesNoDisponibleException>(() => motor.Reservar(politica, "u:s:despues", null, null, CancellationToken.None));
        await Assert.ThrowsAsync<MotorDeLimitesNoDisponibleException>(() => motor.RegistrarRechazoDeOrigen(HuellaDeOrigen.Desconocido, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task ConLaMemoriaDeRedisLlena_LasPoliticasCerradasRechazanYLasConRespaldoUsanElMotorLocal()
    {
        var principal = sinMemoria.Motor();
        var control = Control(principal, new MotorDeLimitesLocal());
        await LlenarMemoria(principal);

        Assert.Equal(EstadoDeLimite.NoDisponible, (await control.Reservar(PoliticasDeLimite.IngresosFallidos, sujeto: "c-lleno")).Estado);
        Assert.Equal(EstadoDeLimite.NoDisponible, (await control.Reservar(PoliticasDeLimite.RegistrosPorOrigen)).Estado);

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await control.Reservar(General)).EsPermitido);
        }

        Assert.Equal(EstadoDeLimite.Excedido, (await control.Reservar(General)).Estado);
    }

    [Fact]
    public async Task VigilanteDeMemoria_AlcanzadoElSetentaPorCientoDelTope_PublicaUnaAlertaTecnicaUnaSolaVezPorCruce()
    {
        var alertas = new AlertasTecnicasDePrueba();
        await using var conexionDelVigilante = await sinMemoria.LimitesSinProteccionDelCliente();
        var vigilante = new VigilanteDeMemoriaDeContadores(conexionDelVigilante, alertas, new RelojDePrueba());
        var servidor = sinMemoria.Administracion.GetServer(sinMemoria.Administracion.GetEndPoints()[0]);

        // El tope se mueve alrededor del uso actual: a 10 veces, la instancia queda al 10 %; a uso / 0,8, al 80 %.
        var bytesUsados = long.Parse((await servidor.InfoAsync("memory")).SelectMany(g => g).First(p => p.Key == "used_memory").Value, System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await servidor.ConfigSetAsync("maxmemory", bytesUsados * 10);
            var inicial = await vigilante.Evaluar();
            Assert.InRange(inicial!.Value, 0.0, 0.7);
            Assert.Empty(alertas.Publicadas);

            await servidor.ConfigSetAsync("maxmemory", (long)(bytesUsados / 0.8));
            var alto = await vigilante.Evaluar();
            await vigilante.Evaluar();

            Assert.InRange(alto!.Value, 0.7, 1.2);
            Assert.Equal(MotorMotivo(), Assert.Single(alertas.Publicadas).Motivo);

            await servidor.ConfigSetAsync("maxmemory", bytesUsados * 10);
            var bajo = await vigilante.Evaluar();
            Assert.InRange(bajo!.Value, 0.0, 0.7);

            await servidor.ConfigSetAsync("maxmemory", (long)(bytesUsados / 0.8));
            await vigilante.Evaluar();
            Assert.Equal(2, alertas.Publicadas.Count);
        }
        finally
        {
            await servidor.ConfigSetAsync("maxmemory", "1mb");
        }
    }

    private static string MotorMotivo() => VigilanteDeMemoriaDeContadores.MotivoDeAlerta;

    private static async Task LlenarMemoria(MotorDeLimitesRedis motor)
    {
        var politica = new PoliticaDeLimite(RedisDeContadoresFixture.Unico("relleno"), 1000, TimeSpan.FromMinutes(5), TipoDeSujetoDeLimite.Usuario);
        for (var i = 0; i < 20_000; i++)
        {
            try
            {
                await motor.Reservar(politica, $"u:s:{i}", $"clave-de-relleno-{i}-con-algo-de-largo-para-ocupar-memoria", null, CancellationToken.None);
            }
            catch (MotorDeLimitesNoDisponibleException)
            {
                return;
            }
        }

        Assert.Fail("La instancia con 1 MB debía llenarse.");
    }
}
