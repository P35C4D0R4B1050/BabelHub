using System.Diagnostics;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using StackExchange.Redis;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>
/// Pruebas del motor con un Redis real y el usuario ACL restringido. Las ventanas y los bloqueos son versiones reducidas
/// (segundos) de los de la spec, con la misma forma; cada prueba usa su propia política y su propio contador.
/// </summary>
[Collection(RedisDeContadoresCollection.Nombre)]
public sealed class MotorDeLimitesRedisTests(RedisDeContadoresFixture redis)
{
    private static PoliticaDeLimite Politica(int maximo, int ventanaMs, int? bloqueoMs = null, bool liberable = false) =>
        new(
            RedisDeContadoresFixture.Unico("pol"),
            maximo,
            TimeSpan.FromMilliseconds(ventanaMs),
            TipoDeSujetoDeLimite.Usuario,
            bloqueoMs is { } b ? TimeSpan.FromMilliseconds(b) : null,
            liberable);

    private static async Task<ResultadoDeLimite> Reservar(MotorDeLimitesRedis motor, PoliticaDeLimite politica, string contador, string? claveDelLlamador = null, ClaveDeIdempotencia? idempotencia = null) =>
        await motor.Reservar(politica, contador, claveDelLlamador, idempotencia, CancellationToken.None);

    private static async Task Esperar(Stopwatch reloj, int milisegundos)
    {
        var pendiente = milisegundos - (int)reloj.ElapsedMilliseconds;
        if (pendiente > 0)
        {
            await Task.Delay(pendiente);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public async Task Reservar_ConElMaximoAlcanzado_RechazaConLaEsperaDesdeLaEntradaMasAntiguaYVuelveAFuncionar()
    {
        var motor = redis.Motor();
        var politica = Politica(3, 2000);
        var contador = "u:s:a";

        var primera = await Reservar(motor, politica, contador);
        await Reservar(motor, politica, contador);
        await Reservar(motor, politica, contador);
        var rechazo = await Reservar(motor, politica, contador);

        Assert.True(primera.EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.Equal(politica.Nombre, rechazo.Motivo);
        var esperado = primera.Reserva!.ReservadaEn + TimeSpan.FromMilliseconds(2000);
        Assert.InRange((rechazo.DisponibleDesde!.Value - esperado).TotalMilliseconds, -2, 2);
        Assert.InRange(rechazo.EsperaEnSegundos!.Value, 1, 2);

        await Task.Delay(TimeSpan.FromMilliseconds(2100));
        Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-21")]
    public async Task Reservar_ConVentanaDeslizante_NoPermiteUnaRafagaEnElCambioDeVentana()
    {
        var motor = redis.Motor();
        var politica = Politica(2, 2000);
        var contador = "u:s:b";
        var reloj = Stopwatch.StartNew();

        await Reservar(motor, politica, contador);
        await Esperar(reloj, 1300);
        await Reservar(motor, politica, contador);

        // 2,3 s después de la primera: una ventana fija habría reiniciado el conteo; la deslizante aún ve la segunda entrada.
        await Esperar(reloj, 2300);
        Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, (await Reservar(motor, politica, contador)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task Reservar_ConCuatroFallosYUnExito_NoBloqueaAlTitularAlLiberarElCupoDelExito()
    {
        var motor = redis.Motor();
        var politica = Politica(5, 6000, bloqueoMs: 3000, liberable: true);
        var contador = "c:s:titular";

        for (var i = 0; i < 4; i++)
        {
            Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
        }

        var exito = await Reservar(motor, politica, contador);
        Assert.True(exito.EsPermitido);
        Assert.True(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));

        await motor.Liberar(politica, exito.Reserva!, CancellationToken.None);

        Assert.False(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));
        Assert.Equal(4, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
        Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task Reservar_ConCincoFallosYUnExito_RechazaAlTitularAunConCredencialesCorrectasSinExtenderElBloqueo()
    {
        var motor = redis.Motor();
        var politica = Politica(5, 6000, bloqueoMs: 2000, liberable: true);
        var contador = "c:s:titular";

        ReservaDeLimite? quinta = null;
        for (var i = 0; i < 5; i++)
        {
            quinta = (await Reservar(motor, politica, contador)).Reserva;
        }

        var duranteElBloqueo = await Reservar(motor, politica, contador);
        var hastaAlPrincipio = duranteElBloqueo.DisponibleDesde;
        await Task.Delay(700);
        var otroIntento = await Reservar(motor, politica, contador);

        Assert.Equal(EstadoDeLimite.Excedido, duranteElBloqueo.Estado);
        Assert.Equal(hastaAlPrincipio, otroIntento.DisponibleDesde);
        var esperado = quinta!.ReservadaEn + TimeSpan.FromMilliseconds(2000);
        Assert.InRange((hastaAlPrincipio!.Value - esperado).TotalMilliseconds, -5, 5);
        Assert.Equal(5, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task Reservar_ConElQuintoIntentoCorrectoEnParaleloConOtros_NoBloqueaAlTitular()
    {
        var motor = redis.Motor();
        var politica = Politica(5, 6000, bloqueoMs: 3000, liberable: true);
        var contador = "c:s:titular";
        for (var i = 0; i < 4; i++)
        {
            await Reservar(motor, politica, contador);
        }

        var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Reservar(motor, politica, contador))));

        var permitido = Assert.Single(resultados, r => r.EsPermitido);
        Assert.Equal(4, resultados.Count(r => r.Estado == EstadoDeLimite.Excedido));

        // El único intento que obtuvo el quinto cupo era el del titular (correcto): al liberarse, nadie queda bloqueado.
        await motor.Liberar(politica, permitido.Reserva!, CancellationToken.None);
        Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-26")]
    public async Task Reservar_ConDiezFallosEnLaVentanaYUnIntentoAlTerminarElBloqueo_SigueRechazandoHastaQueExpireLaMasAntigua()
    {
        // SEG-29 a escala: 10 por «hora» (3 s) con bloqueo de «15 minutos» (0,75 s); la ventana no se vacía al bloquear (D26).
        var motor = redis.Motor();
        var politica = Politica(10, 3000, bloqueoMs: 750, liberable: true);
        var contador = "u:s:cuenta";
        var reloj = Stopwatch.StartNew();

        await Esperar(reloj, 1000);
        var primero = await Reservar(motor, politica, contador);
        for (var i = 1; i < 10; i++)
        {
            await Esperar(reloj, 1000 + (i * 1500 / 9));
            Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
        }

        await Esperar(reloj, 2500 + 750 + 100);
        var alTerminarElBloqueo = await Reservar(motor, politica, contador);

        Assert.Equal(EstadoDeLimite.Excedido, alTerminarElBloqueo.Estado);
        var expiraLaPrimera = primero.Reserva!.ReservadaEn + TimeSpan.FromMilliseconds(3000);
        Assert.InRange((alTerminarElBloqueo.DisponibleDesde!.Value - expiraLaPrimera).TotalMilliseconds, -5, 5);
        Assert.Equal(10, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
        Assert.False(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));

        await Task.Delay(alTerminarElBloqueo.DisponibleDesde.Value - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(150));
        Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task Reservar_ConVentanaYBloqueoIguales_ElTitularRecuperaElCupoCompletoAlTerminarElBloqueo()
    {
        var motor = redis.Motor();
        var politica = Politica(3, 1500, bloqueoMs: 1500, liberable: true);
        var contador = "c:s:igual";
        for (var i = 0; i < 3; i++)
        {
            await Reservar(motor, politica, contador);
        }

        Assert.Equal(EstadoDeLimite.Excedido, (await Reservar(motor, politica, contador)).Estado);
        await Task.Delay(1700);

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await Reservar(motor, politica, contador)).EsPermitido);
        }
    }

    [Fact]
    public async Task Liberar_BorraElBloqueoDeSuReservaSuEntradaYSuClaveDelLlamador()
    {
        var motor = redis.Motor();
        var politica = Politica(2, 5000, bloqueoMs: 4000, liberable: true);
        var contador = "n:v1:abc";
        await Reservar(motor, politica, contador, claveDelLlamador: "aviso-1");
        var segunda = await Reservar(motor, politica, contador, claveDelLlamador: "aviso-2");
        Assert.True(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));

        await motor.Liberar(politica, segunda.Reserva!, CancellationToken.None);

        Assert.False(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));
        Assert.False(await redis.Bd.KeyExistsAsync($"rsv:{politica.Nombre}:aviso-2"));
        Assert.True(await redis.Bd.KeyExistsAsync($"rsv:{politica.Nombre}:aviso-1"));
        Assert.Equal(1, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
    }

    [Fact]
    public async Task Liberar_DeUnaReservaQueNoCreoElBloqueo_NoLoBorra()
    {
        var motor = redis.Motor();
        var politica = Politica(2, 5000, bloqueoMs: 4000, liberable: true);
        var contador = "u:s:otro";
        var primera = await Reservar(motor, politica, contador);
        await Reservar(motor, politica, contador);

        await motor.Liberar(politica, primera.Reserva!, CancellationToken.None);

        Assert.True(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));
        Assert.Equal(1, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
    }

    [Fact]
    public async Task Reservar_ConLaMismaClaveDelLlamador_DevuelveLaMismaReservaYNoCuentaDeNuevo()
    {
        var motor = redis.Motor();
        var politica = Politica(2, 5000);
        var contador = "n:v1:xyz";

        var primera = await Reservar(motor, politica, contador, claveDelLlamador: "aviso-7");
        var repetida = await Reservar(motor, politica, contador, claveDelLlamador: "aviso-7");

        Assert.Equal(primera.Reserva!.Identificador, repetida.Reserva!.Identificador);
        Assert.Equal(primera.Reserva.ReservadaEn, repetida.Reserva.ReservadaEn);
        Assert.Equal(1, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
        Assert.True((await Reservar(motor, politica, contador, claveDelLlamador: "aviso-8")).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Reservar_ConLaClaveDeIdempotenciaDelCliente_ControlaEnCursoCompletadaConflictoYFalla()
    {
        var motor = redis.Motor();
        var politica = Politica(5, 5000);
        var contador = "u:s:reservas";
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-a");

        var primera = await Reservar(motor, politica, contador, idempotencia: clave);
        var enCurso = await Reservar(motor, politica, contador, idempotencia: clave);
        var conflicto = await Reservar(motor, politica, contador, idempotencia: new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-b"));
        await motor.Completar(primera.Reserva!, new ResultadoMinimo("creada", "res-9"), CancellationToken.None);
        var completada = await Reservar(motor, politica, contador, idempotencia: clave);

        Assert.True(primera.EsPermitido);
        Assert.Equal(EstadoDeLimite.RepeticionEnCurso, enCurso.Estado);
        Assert.Equal(EstadoDeLimite.ClaveEnConflicto, conflicto.Estado);
        Assert.Equal(EstadoDeLimite.RepeticionCompletada, completada.Estado);
        Assert.Equal("res-9", completada.ResultadoGuardado!.IdDelRecurso);
        Assert.Equal(1, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));

        // Una operación fallida libera la clave: el cliente puede reintentar y la nueva solicitud cuenta.
        var otra = new ClaveDeIdempotencia("reserva-2", "/reservas", "cuerpo-c");
        var fallida = await Reservar(motor, politica, contador, idempotencia: otra);
        await motor.Fallar(fallida.Reserva!, CancellationToken.None);
        Assert.True((await Reservar(motor, politica, contador, idempotencia: otra)).EsPermitido);
        Assert.Equal(3, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Reservar_ConLaIdempotenciaEnCursoVencida_PermiteReintentar()
    {
        var motor = redis.Motor(new OpcionesDeMotorRedis { VigenciaEnCurso = TimeSpan.FromMilliseconds(400) });
        var politica = Politica(5, 5000);
        var clave = new ClaveDeIdempotencia("reserva-9", "/reservas", "cuerpo");
        await Reservar(motor, politica, "u:s:caido", idempotencia: clave);

        await Task.Delay(600);

        Assert.True((await Reservar(motor, politica, "u:s:caido", idempotencia: clave)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public async Task Reservar_ConSolicitudesSimultaneas_NuncaSuperaElMaximo()
    {
        var motor = redis.Motor();
        var politica = Politica(50, 10_000);
        var contador = "u:s:humo";

        var resultados = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => Reservar(motor, politica, contador))));

        Assert.Equal(50, resultados.Count(r => r.EsPermitido));
        Assert.Equal(150, resultados.Count(r => r.Estado == EstadoDeLimite.Excedido));
        Assert.Equal(50, resultados.Where(r => r.EsPermitido).Select(r => r.Reserva!.Identificador).Distinct().Count());
        Assert.Equal(50, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:{contador}"));
    }

    [Fact]
    public async Task Reservar_ConLosContadoresVencidos_RedisLosBorraSolo()
    {
        var motor = redis.Motor(new OpcionesDeMotorRedis { MargenDeExpiracion = TimeSpan.FromMilliseconds(300) });
        var politica = Politica(2, 600, bloqueoMs: 500);
        var contador = "u:s:expira";
        await Reservar(motor, politica, contador, claveDelLlamador: "k1");
        await Reservar(motor, politica, contador, claveDelLlamador: "k2");

        var ttl = await redis.Bd.KeyTimeToLiveAsync($"lim:{politica.Nombre}:{contador}");
        Assert.InRange(ttl!.Value.TotalMilliseconds, 1, 900);

        await Task.Delay(1300);

        Assert.False(await redis.Bd.KeyExistsAsync($"lim:{politica.Nombre}:{contador}"));
        Assert.False(await redis.Bd.KeyExistsAsync($"blq:{politica.Nombre}:{contador}"));
        Assert.False(await redis.Bd.KeyExistsAsync($"rsv:{politica.Nombre}:k1"));
    }

    [Fact]
    public async Task Reservar_ConElTopeDeClavesAlcanzado_NoCreaContadoresNuevosPeroSigueAtendiendoLosExistentes()
    {
        var motor = redis.Motor(new OpcionesDeMotorRedis { TopeDeClaves = (await redis.Bd.ExecuteAsync("DBSIZE") is { } n ? (long)n : 0) + 2 });
        var politica = Politica(5, 5000);
        await Reservar(motor, politica, "u:s:uno");
        await Reservar(motor, politica, "u:s:dos");

        await Assert.ThrowsAsync<MotorDeLimitesNoDisponibleException>(() => Reservar(motor, politica, "u:s:tres"));

        Assert.True((await Reservar(motor, politica, "u:s:uno")).EsPermitido);
    }

    [Fact]
    public async Task Reservar_ConElServidorQueOlvidoLosScripts_LosRecargaYContinua()
    {
        var motor = redis.Motor();
        var politica = Politica(5, 5000);
        await Reservar(motor, politica, "u:s:noscript");

        var servidor = redis.Administracion.GetServer(redis.Administracion.GetEndPoints()[0]);
        await servidor.ScriptFlushAsync();

        Assert.True((await Reservar(motor, politica, "u:s:noscript")).EsPermitido);
        Assert.Equal(2, await redis.Bd.SortedSetLengthAsync($"lim:{politica.Nombre}:u:s:noscript"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Reservar_ConElAlmacenSinConexion_InformaQueNoEstaDisponible()
    {
        var opciones = ConfigurationOptions.Parse("127.0.0.1:1,abortConnect=false,connectTimeout=300,syncTimeout=300,asyncTimeout=300,connectRetry=1");
        await using var sinServidor = await ConnectionMultiplexer.ConnectAsync(opciones);
        var motor = new MotorDeLimitesRedis(sinServidor, new OpcionesDeMotorRedis());

        await Assert.ThrowsAsync<MotorDeLimitesNoDisponibleException>(() => Reservar(motor, Politica(2, 1000), "u:s:x"));
        await Assert.ThrowsAsync<MotorDeLimitesNoDisponibleException>(() => motor.RegistrarRechazoDeOrigen(HuellaDeOrigen.Desconocido, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task RegistrarRechazoDeOrigen_CuentaLosRechazosDeLaVentanaPorOrigen()
    {
        var motor = redis.Motor();
        var origen = HuellaDeOrigen.Crear(Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "abcdefghijk", "v1");
        var otro = HuellaDeOrigen.Crear(Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "abcdefghijk", "v1");

        await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMilliseconds(800), CancellationToken.None);
        await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMilliseconds(800), CancellationToken.None);
        var tres = await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMilliseconds(800), CancellationToken.None);

        Assert.Equal(3, tres);
        Assert.Equal(3, await motor.ContarRechazosDeOrigen(origen, TimeSpan.FromMilliseconds(800), CancellationToken.None));
        Assert.Equal(0, await motor.ContarRechazosDeOrigen(otro, TimeSpan.FromMilliseconds(800), CancellationToken.None));

        await Task.Delay(1000);
        Assert.Equal(0, await motor.ContarRechazosDeOrigen(origen, TimeSpan.FromMilliseconds(800), CancellationToken.None));
    }
}
