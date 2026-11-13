using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class AlmacenDeEntradasProcesadasTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private static readonly byte[] Secreto = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
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

    private readonly UnidadDeTrabajoPostgres _unidad = new(bd.Acceso);

    private AlmacenDeEntradasProcesadas Almacen() => new(_unidad);

    private static string Id() => "evt-" + Guid.NewGuid().ToString("N")[..10];

    private async Task<long> Filas(string proveedor, string id) =>
        await bd.Consultar<long>($"SELECT count(*) FROM plataforma.entradas_procesadas WHERE clave = '{proveedor}|{id}'");

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Reclamar_ConUnaEntradaNuevaYSuEfectoConfirmado_LaSiguienteVezDevuelveElMismoResultado()
    {
        var almacen = Almacen();
        var id = Id();

        var primera = await _unidad.Ejecutar(async token =>
        {
            var reclamo = await almacen.Reclamar("pasarela", id, token);
            await almacen.Completar("pasarela", id, new ResultadoMinimo("pago-confirmado", "pedido-9"), token);
            return reclamo;
        });
        var repetida = await almacen.Reclamar("pasarela", id);

        Assert.Equal(EstadoDeEntrada.Nueva, primera.Estado);
        Assert.Equal(EstadoDeEntrada.Procesada, repetida.Estado);
        Assert.Equal("pago-confirmado", repetida.Resultado!.Desenlace);
        Assert.Equal("pedido-9", repetida.Resultado.IdDelRecurso);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Reclamar_ConUnEfectoQueFalla_NoDejaLaEntradaMarcadaComoProcesada()
    {
        var almacen = Almacen();
        var id = Id();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _unidad.Ejecutar<int>(async token =>
        {
            Assert.Equal(EstadoDeEntrada.Nueva, (await almacen.Reclamar("pasarela", id, token)).Estado);
            throw new InvalidOperationException("el efecto falla");
        }));

        Assert.Equal(0, await Filas("pasarela", id));
        Assert.Equal(EstadoDeEntrada.Nueva, (await _unidad.Ejecutar(async token => await almacen.Reclamar("pasarela", id, token))).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Reclamar_ConUnDuplicadoMientrasLaPrimeraSigueEnCurso_RecibeEnCursoSinEsperar()
    {
        var almacen = Almacen();
        var otroAlmacen = new AlmacenDeEntradasProcesadas(new UnidadDeTrabajoPostgres(bd.Acceso));
        var id = Id();
        var primeraReclamo = new TaskCompletionSource();
        var terminar = new TaskCompletionSource();

        var primera = Task.Run(() => _unidad.Ejecutar(async token =>
        {
            var reclamo = await almacen.Reclamar("pasarela", id, token);
            primeraReclamo.SetResult();
            await terminar.Task;
            await almacen.Completar("pasarela", id, new ResultadoMinimo("ok"), token);
            return reclamo.Estado;
        }));
        await primeraReclamo.Task;

        var duplicado = await otroAlmacen.Reclamar("pasarela", id).WaitAsync(TimeSpan.FromSeconds(5));
        terminar.SetResult();
        var estadoDeLaPrimera = await primera;
        var despues = await otroAlmacen.Reclamar("pasarela", id);

        Assert.Equal(EstadoDeEntrada.EnCurso, duplicado.Estado);
        Assert.Equal(EstadoDeEntrada.Nueva, estadoDeLaPrimera);
        Assert.Equal(EstadoDeEntrada.Procesada, despues.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task Reclamar_ConVeinteLlamadasSimultaneasDeLaMismaEntrada_SoloUnaLaProcesa()
    {
        var id = Id();
        var efectos = 0;

        var estados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            var unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
            var almacen = new AlmacenDeEntradasProcesadas(unidad);
            return await unidad.Ejecutar(async token =>
            {
                var reclamo = await almacen.Reclamar("pasarela", id, token);
                if (reclamo.Estado == EstadoDeEntrada.Nueva)
                {
                    Interlocked.Increment(ref efectos);
                    await Task.Delay(100, token);
                    await almacen.Completar("pasarela", id, new ResultadoMinimo("ok"), token);
                }

                return reclamo.Estado;
            });
        })));

        Assert.Equal(1, efectos);
        Assert.Equal(1, estados.Count(e => e == EstadoDeEntrada.Nueva));
        Assert.All(estados.Where(e => e != EstadoDeEntrada.Nueva), e => Assert.True(e is EstadoDeEntrada.EnCurso or EstadoDeEntrada.Procesada));
        Assert.Equal(1, await Filas("pasarela", id));
    }

    [Fact]
    public async Task Reclamar_ConElMismoIdentificadorDeOtroProveedor_EsOtraEntrada()
    {
        var almacen = Almacen();
        var id = Id();

        var uno = await _unidad.Ejecutar(async token => await almacen.Reclamar("pasarela", id, token));
        var otro = await _unidad.Ejecutar(async token => await almacen.Reclamar("avisos", id, token));

        Assert.Equal(EstadoDeEntrada.Nueva, uno.Estado);
        Assert.Equal(EstadoDeEntrada.Nueva, otro.Estado);
    }

    [Fact]
    public async Task Reclamar_ConUnaEntradaCuyoEfectoNoLlamoACompletar_QuedaEnCurso()
    {
        var almacen = Almacen();
        var id = Id();
        await _unidad.Ejecutar(async token => await almacen.Reclamar("pasarela", id, token));

        var repetida = await almacen.Reclamar("pasarela", id);

        Assert.Equal(EstadoDeEntrada.EnCurso, repetida.Estado);
    }

    [Fact]
    public async Task PurgarVencidas_BorraLasEntradasDeMasDeTreintaDiasYConservaLasRecientes()
    {
        var almacen = Almacen();
        var reciente = Id();
        var vieja = Id();
        await _unidad.Ejecutar(async token => await almacen.Reclamar("pasarela", reciente, token));
        await bd.Consultar<int>($"INSERT INTO plataforma.entradas_procesadas VALUES ('pasarela|{vieja}', 'completada', 'ok', null, now() - interval '31 days') RETURNING 1");

        var borradas = await almacen.PurgarVencidas();

        Assert.True(borradas >= 1);
        Assert.Equal(0, await Filas("pasarela", vieja));
        Assert.Equal(1, await Filas("pasarela", reciente));
    }

    [Fact]
    public async Task LaTablaDeEntradas_SoloLaTocaElRolDelEsquemaPlataforma()
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand("SELECT count(*) FROM plataforma.entradas_procesadas", conexion);

        var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteScalarAsync());

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public async Task ProcesarEntradaDeIntegracion_ConElVerificadorYElAlmacenReales_ProcesaUnaVezYResponderElMismoExitoALasRepeticiones()
    {
        var eventos = new EventosDePrueba();
        var firmas = new VerificadorDeFirmaHmac(new OpcionesDeFirmaDeIntegraciones([new("pasarela", [Secreto])]));
        var almacen = Almacen();
        var procesador = new ProcesarEntradaDeIntegracion(
            new OpcionesDeEntradasDeIntegracion(["pasarela"]), firmas, almacen, new OrigenDePrueba(), eventos, new RelojDePrueba());
        var id = Id();
        var marca = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cuerpo = Encoding.UTF8.GetBytes("{\"evento\":\"pago.confirmado\"}");
        var firma = Convert.ToHexString(HMACSHA256.HashData(Secreto, Encoding.UTF8.GetBytes(marca + ".").Concat(cuerpo).ToArray()));
        var entrada = new EntradaDeIntegracion("pasarela", id, marca, cuerpo, firma);
        var efectos = 0;

        // Entrada firmada: se reclama y se aplica el efecto en la misma transacción.
        var primera = await _unidad.Ejecutar(async token =>
        {
            var recibida = await procesador.Recibir(entrada, token);
            if (recibida.Estado == EstadoDeEntradaDeIntegracion.Aceptada)
            {
                efectos++;
                await procesador.Completar("pasarela", id, new ResultadoMinimo("pago-confirmado", "pedido-9"), token);
            }

            return recibida;
        });
        var repetida = await _unidad.Ejecutar(async token => await procesador.Recibir(entrada, token));
        var sinFirma = await procesador.Recibir(new EntradaDeIntegracion("pasarela", Id(), marca, cuerpo, string.Empty));
        var invalida = await procesador.Recibir(new EntradaDeIntegracion("pasarela", Id(), marca, cuerpo, new string('a', 64)));

        Assert.Equal(EstadoDeEntradaDeIntegracion.Aceptada, primera.Estado);
        Assert.Equal(1, efectos);
        Assert.Equal(EstadoDeEntradaDeIntegracion.Repetida, repetida.Estado);
        Assert.Equal("pedido-9", repetida.ResultadoGuardado!.IdDelRecurso);
        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, sinFirma.Estado);
        Assert.Equal(EstadoDeEntradaDeIntegracion.Rechazada, invalida.Estado);
        Assert.Equal(["sin-firma", "firma-invalida"], eventos.Eventos.Select(e => e.Motivo));
        Assert.Equal(1, await Filas("pasarela", id));
    }
}
