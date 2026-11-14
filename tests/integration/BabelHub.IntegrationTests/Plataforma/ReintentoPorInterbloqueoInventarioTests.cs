using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// Reintento por interbloqueo y lectura coherente del Inventario (inventario-fuentes T03), con un interbloqueo real de PostgreSQL y
/// con transacciones <c>REPEATABLE READ</c> de solo lectura.
/// </summary>
public sealed class ReintentoPorInterbloqueoInventarioTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private static readonly OpcionesDeReintentoPorInterbloqueo SinEspera = new() { EsperaAntesDeRepetir = TimeSpan.Zero };

    private UnidadDeTrabajoPostgres Unidad() => new(bd.Acceso);

    private static PostgresException Error(string estado) => new("falla simulada", "ERROR", "ERROR", estado);

    // --- el decorador, con fallos simulados ---

    [Theory]
    [InlineData("40P01")]
    [InlineData("40001")]
    public async Task Ejecutar_ConUnInterbloqueoOUnFalloDeSerializacion_RepiteUnaVezYDevuelveElResultado(string estado)
    {
        var interno = new CasoIdempotente(Error(estado), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        var resultado = await decorador.Ejecutar(7);

        Assert.True(resultado.EsExito);
        Assert.Equal("ok-7", resultado.Valor);
        Assert.Equal(2, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_SiLaRepeticionTambienFalla_DejaSubirElErrorOriginalSinMasIntentos()
    {
        var interno = new CasoIdempotente(Error("40P01"), fallos: 5);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        var error = await Assert.ThrowsAsync<PostgresException>(() => decorador.Ejecutar(1));

        Assert.Equal("40P01", error.SqlState);
        Assert.Equal(2, interno.Intentos);
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("55P03")]
    [InlineData("57014")]
    public async Task Ejecutar_ConOtroErrorDePostgreSql_NoRepite(string estado)
    {
        var interno = new CasoIdempotente(Error(estado), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        await Assert.ThrowsAsync<PostgresException>(() => decorador.Ejecutar(1));

        Assert.Equal(1, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConUnaExcepcionQueNoEsDeBaseDeDatos_NoRepite()
    {
        var interno = new CasoIdempotente(new InvalidOperationException("otra cosa"), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        await Assert.ThrowsAsync<InvalidOperationException>(() => decorador.Ejecutar(1));

        Assert.Equal(1, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConElInterbloqueoEnvueltoEnOtraExcepcion_TambienRepite()
    {
        var interno = new CasoIdempotente(new InvalidOperationException("envuelta", Error("40P01")), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        var resultado = await decorador.Ejecutar(3);

        Assert.True(resultado.EsExito);
        Assert.Equal(2, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConUnCasoQueNoEsIdempotente_NoRepiteAunqueSeaUnInterbloqueo()
    {
        var interno = new Caso(Error("40P01"), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        await Assert.ThrowsAsync<PostgresException>(() => decorador.Ejecutar(1));

        Assert.Equal(1, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConUnCasoNoMarcadoPeroDeclaradoIdempotenteALaComposicion_Repite()
    {
        var interno = new Caso(Error("40001"), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera, esIdempotente: true);

        var resultado = await decorador.Ejecutar(1);

        Assert.True(resultado.EsExito);
        Assert.Equal(2, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConUnaTransaccionYaActiva_NoRepiteYElErrorSubeAlDeAfuera()
    {
        var unidad = Unidad();
        var interno = new CasoIdempotente(Error("40P01"), fallos: 1);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, unidad, SinEspera);

        await unidad.Ejecutar<int>(async token =>
        {
            await Assert.ThrowsAsync<PostgresException>(() => decorador.Ejecutar(1, token));
            return 1;
        });

        Assert.Equal(1, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConUnFalloEsperadoDelCasoDeUso_NoLoRepite()
    {
        var interno = new CasoConFalloDeNegocio();
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        var resultado = await decorador.Ejecutar(1);

        Assert.False(resultado.EsExito);
        Assert.Equal(1, interno.Intentos);
    }

    [Fact]
    public async Task Ejecutar_ConLaOperacionCancelada_NoRepite()
    {
        using var cancelacion = new CancellationTokenSource();
        var interno = new CasoIdempotente(Error("40P01"), fallos: 1, alFallar: cancelacion.Cancel);
        var decorador = new ReintentoPorInterbloqueo<int, string>(interno, Unidad(), SinEspera);

        await Assert.ThrowsAsync<PostgresException>(() => decorador.Ejecutar(1, cancelacion.Token));

        Assert.Equal(1, interno.Intentos);
    }

    // --- un interbloqueo real de PostgreSQL ---

    [Fact]
    public async Task Ejecutar_ConUnInterbloqueoRealEntreDosCasosDeUso_UnoLoRepiteYAmbosTerminan()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var sincronia = new Sincronia();

        var primero = Decorado(new CasoQueTomaDosBloqueos(Unidad(), a, b, sincronia));
        var segundo = Decorado(new CasoQueTomaDosBloqueos(Unidad(), b, a, sincronia));

        var resultados = await Task.WhenAll(
            Task.Run(() => primero.Ejecutar(1)),
            Task.Run(() => segundo.Ejecutar(2)));

        Assert.All(resultados, r => Assert.True(r.EsExito));
        Assert.Equal(1, sincronia.Interbloqueos);
        Assert.Equal(3, sincronia.Intentos);
    }

    private ReintentoPorInterbloqueo<int, string> Decorado(CasoQueTomaDosBloqueos caso)
    {
        var unidad = caso.Unidad;
        var conTransaccion = new TransaccionPorCasoDeUso<int, string>(caso, unidad);
        return new ReintentoPorInterbloqueo<int, string>(conTransaccion, unidad, SinEspera, esIdempotente: true);
    }

    // --- lectura coherente ---

    [Fact]
    public async Task EjecutarLectura_UsaRepeatableReadYSoloLectura()
    {
        var unidad = Unidad();

        var (aislamiento, soloLectura) = await unidad.EjecutarLectura(async token =>
            (await Escalar(unidad, "SELECT current_setting('transaction_isolation')"), await Escalar(unidad, "SELECT current_setting('transaction_read_only')")));

        Assert.Equal("repeatable read", aislamiento);
        Assert.Equal("on", soloLectura);
        Assert.False(unidad.HayTransaccion);
    }

    [Fact]
    public async Task EjecutarLectura_VeLaMismaInstantaneaAunqueOtraTransaccionConfirme()
    {
        var unidad = Unidad();
        const int id = 9001;

        var (antes, despues) = await unidad.EjecutarLectura(async token =>
        {
            var primera = await ContarPedidos(unidad, id);
            await bd.Consultar<long>($"INSERT INTO pedidos.pedidos VALUES ({id}, 1) RETURNING 1::bigint");
            var segunda = await ContarPedidos(unidad, id);
            return (primera, segunda);
        });

        Assert.Equal(0, antes);
        Assert.Equal(0, despues);
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM pedidos.pedidos WHERE id = {id}"));
    }

    [Fact]
    public async Task Ejecutar_ComunConReadCommitted_SiVeLoQueOtraTransaccionConfirma()
    {
        var unidad = Unidad();
        const int id = 9002;

        var (antes, despues) = await unidad.Ejecutar(async token =>
        {
            var primera = await ContarPedidos(unidad, id);
            await bd.Consultar<long>($"INSERT INTO pedidos.pedidos VALUES ({id}, 1) RETURNING 1::bigint");
            var segunda = await ContarPedidos(unidad, id);
            return (primera, segunda);
        });

        Assert.Equal(0, antes);
        Assert.Equal(1, despues);
    }

    [Fact]
    public async Task EjecutarLectura_NoPermiteEscribir()
    {
        var unidad = Unidad();

        var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.EjecutarLectura(async token =>
        {
            await Escalar(unidad, "CREATE TEMP TABLE ensayo_de_lectura (x int)");
            return 1;
        }));

        Assert.Equal("25006", error.SqlState); // read_only_sql_transaction
        Assert.False(unidad.HayTransaccion);
    }

    [Fact]
    public async Task EjecutarLectura_DentroDeUnaTransaccionActiva_SeUneAElla()
    {
        var unidad = Unidad();

        var valor = await unidad.Ejecutar(async token =>
        {
            var aislamiento = await unidad.EjecutarLectura(_ => Escalar(unidad, "SELECT current_setting('transaction_isolation')"), token);
            Assert.True(unidad.HayTransaccion);
            return aislamiento;
        });

        Assert.Equal("read committed", valor);
    }

    private static async Task<long> ContarPedidos(UnidadDeTrabajoPostgres unidad, int id)
    {
        long cuenta = 0;
        await unidad.EjecutarComo(
            EsquemaDeModulo.Crear("pedidos"),
            async _ => cuenta = long.Parse((await Escalar(unidad, $"SELECT count(*) FROM pedidos.pedidos WHERE id = {id}"))!, System.Globalization.CultureInfo.InvariantCulture));
        return cuenta;
    }

    private static async Task<string?> Escalar(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? null : Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture);
    }

    private class Caso(Exception error, int fallos, Action? alFallar = null) : ICasoDeUso<int, string>
    {
        public int Intentos { get; private set; }

        public Task<Resultado<string>> Ejecutar(int entrada, CancellationToken cancelacion = default)
        {
            Intentos++;
            if (Intentos <= fallos)
            {
                alFallar?.Invoke();
                throw error;
            }

            return Task.FromResult(Resultado<string>.Exito($"ok-{entrada}"));
        }
    }

    private sealed class CasoIdempotente(Exception error, int fallos, Action? alFallar = null) : Caso(error, fallos, alFallar), ICasoDeUsoIdempotente;

    private sealed class CasoConFalloDeNegocio : ICasoDeUso<int, string>, ICasoDeUsoIdempotente
    {
        public int Intentos { get; private set; }

        public Task<Resultado<string>> Ejecutar(int entrada, CancellationToken cancelacion = default)
        {
            Intentos++;
            return Task.FromResult(Resultado<string>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, "stock-insuficiente")));
        }
    }

    private sealed class Sincronia
    {
        private readonly TaskCompletionSource _primeroListo = new();
        private readonly TaskCompletionSource _segundoListo = new();
        private int _llegadas;

        public int Intentos;
        public int Interbloqueos;

        /// <summary>En el primer intento de cada caso, espera a que el otro ya tenga su primer bloqueo (así el cruce es seguro).</summary>
        public async Task Cruzar(bool primeroDeLosDos)
        {
            var numero = Interlocked.Increment(ref _llegadas);
            if (numero > 2)
            {
                return; // los reintentos ya no necesitan sincronía
            }

            (primeroDeLosDos ? _primeroListo : _segundoListo).SetResult();
            await (primeroDeLosDos ? _segundoListo : _primeroListo).Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>Toma dos bloqueos asesores de pedido en el orden dado: dos casos con el orden contrario se interbloquean.</summary>
    private sealed class CasoQueTomaDosBloqueos(UnidadDeTrabajoPostgres unidad, Guid primero, Guid segundo, Sincronia sincronia) : ICasoDeUso<int, string>, ICasoDeUsoIdempotente
    {
        public UnidadDeTrabajoPostgres Unidad { get; } = unidad;

        public async Task<Resultado<string>> Ejecutar(int entrada, CancellationToken cancelacion = default)
        {
            Interlocked.Increment(ref sincronia.Intentos);
            var bloqueos = new NpgsqlBloqueosAsesores(Unidad, new OpcionesDeBloqueosAsesores { EsperaMaxima = TimeSpan.FromSeconds(30) });
            try
            {
                await bloqueos.Pedido(primero, cancelacion);
                await sincronia.Cruzar(entrada == 1);
                await bloqueos.Pedido(segundo, cancelacion);
                return Resultado<string>.Exito("ok");
            }
            catch (PostgresException ex) when (ex.SqlState == "40P01")
            {
                Interlocked.Increment(ref sincronia.Interbloqueos);
                throw;
            }
        }
    }
}
