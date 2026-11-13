using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using StackExchange.Redis;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>La instancia dedicada: configuración, usuario ACL restringido y que ningún dato personal llega a los contadores.</summary>
[Collection(RedisDeContadoresCollection.Nombre)]
public sealed class InstanciaDeContadoresTests(RedisDeContadoresFixture redis)
{
    private const string Numero = "51987654321";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDePrueba(OrigenDeSolicitud origen) : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => origen;
    }

    private sealed class EventosEspia : IPublicadorDeEventosDeSeguridad
    {
        public List<EventoDeSeguridad> Eventos { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            Eventos.Add(evento);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task LaInstancia_NoExpulsaClavesTieneAofCadaSegundoYElUsuarioPorDefectoDesactivado()
    {
        var politica = (await redis.Bd.ExecuteAsync("CONFIG", "GET", "maxmemory-policy")).ToDictionary();
        var aof = (await redis.Bd.ExecuteAsync("CONFIG", "GET", "appendonly")).ToDictionary();
        var fsync = (await redis.Bd.ExecuteAsync("CONFIG", "GET", "appendfsync")).ToDictionary();
        var memoria = (await redis.Bd.ExecuteAsync("CONFIG", "GET", "maxmemory")).ToDictionary();

        Assert.Equal("noeviction", (string?)politica["maxmemory-policy"]);
        Assert.Equal("yes", (string?)aof["appendonly"]);
        Assert.Equal("everysec", (string?)fsync["appendfsync"]);
        Assert.Equal(100L * 1024 * 1024, (long)memoria["maxmemory"]);

        await using var anonimo = await ConnectionMultiplexer.ConnectAsync(
            $"{redis.Administracion.GetEndPoints()[0]},abortConnect=false,connectTimeout=2000,syncTimeout=2000");
        await Assert.ThrowsAnyAsync<RedisException>(() => anonimo.GetDatabase().StringGetAsync("lim:x"));
    }

    [Fact]
    public async Task ElUsuarioRestringido_EjecutaTodosLosScriptsDelMotor()
    {
        var motor = redis.Motor();
        var politica = new PoliticaDeLimite(RedisDeContadoresFixture.Unico("acl"), 2, TimeSpan.FromSeconds(5), TipoDeSujetoDeLimite.Usuario, TimeSpan.FromSeconds(3), liberable: true);
        var idempotencia = new ClaveDeIdempotencia("clave-acl", "/ruta", "cuerpo");
        var origen = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");

        // Reservar (con reserva del llamador e idempotencia), Completar, Liberar, Fallar y los dos usos del script de rechazos.
        var primera = await motor.Reservar(politica, "u:s:acl", "llamador-1", idempotencia, CancellationToken.None);
        await motor.Completar(primera.Reserva!, new ResultadoMinimo("ok", "id-1"), CancellationToken.None);
        var segunda = await motor.Reservar(politica, "u:s:acl", null, null, CancellationToken.None);
        await motor.Liberar(politica, segunda.Reserva!, CancellationToken.None);
        await motor.Fallar(primera.Reserva!, CancellationToken.None);
        await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromSeconds(5), CancellationToken.None);
        var contados = await motor.ContarRechazosDeOrigen(origen, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(primera.EsPermitido);
        Assert.True(segunda.EsPermitido);
        Assert.Equal(1, contados);
    }

    [Fact]
    public async Task ElUsuarioRestringido_NoPuedeAdministrarNiLeerClavesAjenas()
    {
        await using var conexion = await redis.LimitesSinProteccionDelCliente();
        var bd = conexion.GetDatabase();
        await redis.Bd.StringSetAsync("otra:clave", "valor");

        await Assert.ThrowsAsync<RedisServerException>(() => bd.ExecuteAsync("FLUSHALL"));
        await Assert.ThrowsAsync<RedisServerException>(() => bd.ExecuteAsync("CONFIG", "GET", "maxmemory"));
        await Assert.ThrowsAsync<RedisServerException>(() => bd.ExecuteAsync("KEYS", "*"));
        await Assert.ThrowsAsync<RedisServerException>(() => bd.StringGetAsync("otra:clave"));
        await Assert.ThrowsAsync<RedisServerException>(() => bd.StringSetAsync("otra:clave", "pisado"));
        await Assert.ThrowsAsync<RedisServerException>(() => bd.ExecuteAsync("ACL", "SETUSER", "limites", "+@all"));
        Assert.Equal("valor", (string?)await redis.Bd.StringGetAsync("otra:clave"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public async Task UnNumeroSembrado_NoApareceEnNingunaClaveNiValorDeLosContadores()
    {
        var politica = new PoliticaDeLimite("avisos-por-numero-it", 2, TimeSpan.FromSeconds(30), TipoDeSujetoDeLimite.Numero, liberable: true, publicarEvento: false);
        var control = new EvaluarLimite(
            new CatalogoDePoliticas([politica]),
            redis.Motor(),
            respaldoLocal: null,
            new OrigenDePrueba(OrigenDeSolicitud.Desconocido),
            new EventosEspia(),
            new RelojDePrueba());
        var huella = HuellaDeSujeto.Crear(HuellaDelNumero(), "v1");
        var clave = new ClaveDePolitica(politica.Nombre);

        var primera = await control.Reservar(clave, claveDelLlamador: "aviso-1", huellaDelLlamador: huella, idempotencia: new ClaveDeIdempotencia("clave", "/avisos", "cuerpo"));
        await control.Reservar(clave, claveDelLlamador: "aviso-2", huellaDelLlamador: huella);

        Assert.True(primera.EsPermitido);
        foreach (var texto in await TodoElContenido())
        {
            Assert.DoesNotContain(Numero, texto, StringComparison.Ordinal);
        }

        Assert.Contains(await redis.TodasLasClaves(), k => k.StartsWith("lim:avisos-por-numero-it:n:", StringComparison.Ordinal));
        Assert.Contains(await redis.TodasLasClaves(), k => k.StartsWith("rsv:avisos-por-numero-it:", StringComparison.Ordinal));
        Assert.Contains(await redis.TodasLasClaves(), k => k.StartsWith("idem:avisos-por-numero-it:n:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public async Task LaPoliticaPorNumero_ReservaIdempotentePorClaveLiberaUnCupoCanceladoYFallaCerradaSinMotorLocal()
    {
        var politica = new PoliticaDeLimite("avisos-por-numero-it2", 2, TimeSpan.FromSeconds(30), TipoDeSujetoDeLimite.Numero, liberable: true, publicarEvento: false);
        var eventos = new EventosEspia();
        var control = new EvaluarLimite(new CatalogoDePoliticas([politica]), redis.Motor(), null, new OrigenDePrueba(OrigenDeSolicitud.Desconocido), eventos, new RelojDePrueba());
        var huella = HuellaDeSujeto.Crear(HuellaDelNumero(), "v1");
        var clave = new ClaveDePolitica(politica.Nombre);

        var a = await control.Reservar(clave, claveDelLlamador: "aviso-A", huellaDelLlamador: huella);
        var repetida = await control.Reservar(clave, claveDelLlamador: "aviso-A", huellaDelLlamador: huella);
        var b = await control.Reservar(clave, claveDelLlamador: "aviso-B", huellaDelLlamador: huella);
        var excedida = await control.Reservar(clave, claveDelLlamador: "aviso-C", huellaDelLlamador: huella);

        Assert.Equal(a.Reserva!.Identificador, repetida.Reserva!.Identificador);
        Assert.True(b.EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, excedida.Estado);
        Assert.Empty(eventos.Eventos);

        // El aviso B se cancela: su cupo vuelve y el aviso C puede reservarse.
        await control.Liberar(b.Reserva!);
        Assert.True((await control.Reservar(clave, claveDelLlamador: "aviso-C", huellaDelLlamador: huella)).EsPermitido);

        // Sin almacén, una política por número no cae a ningún motor local: falla cerrada.
        var opciones = ConfigurationOptions.Parse("127.0.0.1:1,abortConnect=false,connectTimeout=300,syncTimeout=300,asyncTimeout=300,connectRetry=1");
        await using var sinServidor = await ConnectionMultiplexer.ConnectAsync(opciones);
        var sinAlmacen = new EvaluarLimite(new CatalogoDePoliticas([politica]), new MotorDeLimitesRedis(sinServidor, new OpcionesDeMotorRedis()), null, new OrigenDePrueba(OrigenDeSolicitud.Desconocido), eventos, new RelojDePrueba());
        Assert.Equal(EstadoDeLimite.NoDisponible, (await sinAlmacen.Reservar(clave, huellaDelLlamador: huella)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public async Task LaDireccionDelOrigen_NoApareceEnLosContadoresSoloSuHuella()
    {
        var politica = new PoliticaDeLimite("registros-it", 1, TimeSpan.FromSeconds(30), TipoDeSujetoDeLimite.Origen);
        var origen = new OrigenDeSolicitud(HuellaDeOrigen.Crear("Zq8Lw2Xv5Nc7Rb1Ty4Hu9Jk3Gf6Ds0Ae2Mp8Oi5UyW", "v1"), "203.0.113.77");
        var control = new EvaluarLimite(new CatalogoDePoliticas([politica]), redis.Motor(), null, new OrigenDePrueba(origen), new EventosEspia(), new RelojDePrueba());
        var clave = new ClaveDePolitica(politica.Nombre);

        await control.Reservar(clave);
        await control.Reservar(clave);

        foreach (var texto in await TodoElContenido())
        {
            Assert.DoesNotContain("203.0.113.77", texto, StringComparison.Ordinal);
        }

        Assert.Contains(await redis.TodasLasClaves(), k => k.Contains("Zq8Lw2Xv5Nc7Rb1Ty4Hu9Jk3Gf6Ds0Ae2Mp8Oi5UyW", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public async Task TodaClaveDeLosContadores_ExpiraAntesDeLas24Horas()
    {
        var motor = redis.Motor();
        var politica = new PoliticaDeLimite(RedisDeContadoresFixture.Unico("ttl"), 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen, TimeSpan.FromMinutes(15), liberable: true);
        var origen = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");
        for (var i = 0; i < 5; i++)
        {
            await motor.Reservar(politica, $"o:{origen}", $"aviso-{i}", new ClaveDeIdempotencia($"clave-{i}", "/ruta", "cuerpo"), CancellationToken.None);
        }

        await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None);

        var claves = (await redis.TodasLasClaves()).Where(k => k.StartsWith("lim:", StringComparison.Ordinal) || k.StartsWith("blq:", StringComparison.Ordinal)
            || k.StartsWith("idem:", StringComparison.Ordinal) || k.StartsWith("rech:", StringComparison.Ordinal) || k.StartsWith("rsv:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(claves);
        foreach (var clave in claves)
        {
            var ttl = await redis.Bd.KeyTimeToLiveAsync(clave);
            Assert.True(ttl is { } t && t > TimeSpan.Zero && t <= TimeSpan.FromHours(24), $"{clave} debe expirar en 24 horas o menos (TTL: {ttl}).");
        }
    }

    private static string HuellaDelNumero() =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("avisos:" + Numero))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<List<string>> TodoElContenido()
    {
        var contenido = new List<string>();
        foreach (var clave in await redis.TodasLasClaves())
        {
            contenido.Add(clave);
            var tipo = await redis.Bd.KeyTypeAsync(clave);
            if (tipo == RedisType.String)
            {
                contenido.Add((string?)await redis.Bd.StringGetAsync(clave) ?? string.Empty);
            }
            else if (tipo == RedisType.SortedSet)
            {
                contenido.AddRange((await redis.Bd.SortedSetRangeByRankAsync(clave)).Select(v => v.ToString()));
            }
        }

        return contenido;
    }
}
