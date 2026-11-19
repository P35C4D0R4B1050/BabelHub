using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// T19 con PostgreSQL real y el rol real de la aplicación: los repositorios de fuentes, rutas y fichas, la lectura coherente de la disponibilidad (una sola instantánea aunque una expiración confirme a la mitad)
/// y las existencias de una fuente con filtros y paginación. Las restricciones únicas se devuelven como resultado y las carreras de dos administradores no pierden el valor anterior.
/// </summary>
public sealed class RepositoriosDeFuentesPostgresTests(PostgresDeFuentesFixture bd) : IClassFixture<PostgresDeFuentesFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        await bd.Ejecutar("""
            CREATE TABLE IF NOT EXISTS public.reservas_prueba (fuente_id uuid NOT NULL, edicion_id uuid NOT NULL, unidades int NOT NULL, PRIMARY KEY (fuente_id, edicion_id));
            GRANT SELECT, INSERT, UPDATE, DELETE ON public.reservas_prueba TO acceso;
            TRUNCATE public.reservas_prueba;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Admin => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral);

    private static Task<T> En<T>(PilaDeFuentes pila, Func<CancellationToken, Task<T>> trabajo) => pila.Unidad.Ejecutar(trabajo);

    private static Fuente Nueva(string codigo, string nombre, string region = "lima", bool recepcion = false) => Fuente.Registrar(Fuente.IdentificadorDe(codigo), codigo, nombre, region, null, recepcion);

    // --- fuentes ---

    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public async Task Fuentes_AgregarUnCodigoOUnNombreRepetido_SeDevuelveComoResultado_YUnaSextaFuenteAparece()
    {
        var pila = new PilaDeFuentes(bd, Admin);
        var repo = new NpgsqlRepositorioDeFuentes(pila.Acceso);

        var primera = await En(pila, t => repo.Agregar(Nueva("cusco", "Cusco", "cusco", recepcion: true), t));
        var codigo = await En(pila, t => repo.Agregar(Nueva("cusco", "Otro Cusco", "cusco"), t));
        var nombre = await En(pila, t => repo.Agregar(Nueva("cusco-2", "Cusco", "cusco"), t));

        Assert.Equal(ResultadoDeEscrituraDeFuente.Escrita, primera);
        Assert.Equal(ResultadoDeEscrituraDeFuente.CodigoRepetido, codigo);
        Assert.Equal(ResultadoDeEscrituraDeFuente.NombreRepetido, nombre);
        Assert.Equal(1, await En(pila, t => repo.Contar(t)));
    }


    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task UnaFuenteConUnIdentificadorQueNoEsElDerivado_FallaCerradoAlLeerse_ConUnaExcepcionPropia_SinElIdentificador()
    {
        await bd.Ejecutar("INSERT INTO fuentes.fuentes (id, codigo, nombre, region, es_recepcion) VALUES (gen_random_uuid(), 'antigua', 'Antigua', 'lima', false)");
        var pila = new PilaDeFuentes(bd, Admin);
        var repo = new NpgsqlRepositorioDeFuentes(pila.Acceso);

        var ex = await Assert.ThrowsAsync<FuenteConIdentificadorInconsistenteException>(() => En(pila, t => repo.PorCodigo("antigua", t)));

        Assert.Contains("antigua", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotMatch("[0-9a-f]{8}-[0-9a-f]{4}-", ex.Message);
        await bd.Ejecutar("DELETE FROM fuentes.fuentes WHERE codigo = 'antigua'");
    }

    [Theory]
    [InlineData("ayacucho")]
    [InlineData("sede-1")]
    [Trait("Criterio", "AC-FUE-06")]
    public void ElIdentificadorQueUsaElFixtureParaInsertar_EsElDelDominio_ParaUnCodigoValido(string codigo) =>
        Assert.Equal(Fuente.IdentificadorDe(codigo), PostgresDeFuentesFixture.IdDeFuente(codigo));
    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public async Task Fuentes_LeeLasFuentesPorIdCodigoYRegion_YElContactoSoloSiEsLaSuya()
    {
        var pila = new PilaDeFuentes(bd, Admin);
        var repo = new NpgsqlRepositorioDeFuentes(pila.Acceso);
        var piura = Fuente.Registrar(Fuente.IdentificadorDe("piura"), "piura", "Piura", "piura", "+51 944 555 666", esRecepcion: false);
        await En(pila, t => repo.Agregar(Nueva("lima", "Lima", recepcion: true), t));
        await En(pila, t => repo.Agregar(piura, t));

        var porCodigo = await En(pila, t => repo.PorCodigo("piura", t));
        var porId = await En(pila, t => repo.PorId(piura.Id, t));
        var deLaRegion = await En(pila, t => repo.DeLaRegion("lima", t));
        var inexistente = await En(pila, t => repo.PorCodigo("nunca", t));

        Assert.Equal("+51 944 555 666", porCodigo!.ContactoDeNotificacion);
        Assert.Equal(piura.Id, porId!.Id);
        Assert.Equal(["lima"], deLaRegion.Select(f => f.Codigo));
        Assert.Null(inexistente);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Fuentes_ActualizarCambiaNombreRegionYContacto_PeroNoElCodigoNiLaRecepcion()
    {
        var pila = new PilaDeFuentes(bd, Admin);
        var repo = new NpgsqlRepositorioDeFuentes(pila.Acceso);
        var lima = Nueva("lima", "Lima", recepcion: true);
        await En(pila, t => repo.Agregar(lima, t));
        await En(pila, t => repo.Agregar(Nueva("cusco", "Cusco", "cusco"), t));

        var (editada, _) = lima.Editar("Lima Centro", "lima", "+51 977 111 222");
        var resultado = await En(pila, t => repo.Actualizar(editada, t));
        var repetido = await En(pila, t => repo.Actualizar(lima.Editar("Cusco", "lima", null).Fuente, t));
        var leida = await En(pila, t => repo.PorCodigo("lima", t));

        Assert.Equal(ResultadoDeEscrituraDeFuente.Escrita, resultado);
        Assert.Equal(ResultadoDeEscrituraDeFuente.NombreRepetido, repetido);
        Assert.Equal(("Lima Centro", true, "+51 977 111 222"), (leida!.Nombre, leida.EsRecepcion, leida.ContactoDeNotificacion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Fuentes_CambiarLaRecepcion_QuitaLaMarcaAnteriorAntesDePonerLaNueva_ConUnaSolaRecepcion()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var pila = new PilaDeFuentes(bd, Admin);
        var repo = new NpgsqlRepositorioDeFuentes(pila.Acceso);

        await En(pila, async t =>
        {
            await repo.CambiarRecepcion(lima, cusco, t);
            return 0;
        });

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes WHERE es_recepcion"));
        Assert.Equal(cusco, await bd.Consultar<Guid>("SELECT id FROM fuentes.fuentes WHERE es_recepcion"));
    }

    // --- rutas ---

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Rutas_DefinirDevuelveElPlazoAnteriorYLaRutaEsDireccional()
    {
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var pila = new PilaDeFuentes(bd, Admin);
        var repo = new NpgsqlRepositorioDeRutas(pila.Acceso);

        var alta = await En(pila, t => repo.Definir(RutaEntreFuentes.Definir(cusco, ayacucho, 2), t));
        var cambio = await En(pila, t => repo.Definir(RutaEntreFuentes.Definir(cusco, ayacucho, 3), t));
        var directa = await En(pila, t => repo.Obtener(cusco, ayacucho, t));
        var inversa = await En(pila, t => repo.Obtener(ayacucho, cusco, t));

        Assert.Null(alta);
        Assert.Equal(2, cambio);
        Assert.Equal(3, directa!.PlazoEnDias);
        Assert.Null(inversa);
        Assert.Equal([cusco], (await En(pila, t => repo.HaciaDestino(ayacucho, t))).Select(r => r.OrigenId));
        Assert.Single(await En(pila, t => repo.Todas(t)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public async Task Rutas_DosAdministradoresSimultaneosEnLaPrimeraAlta_NoPierdenElValorAnterior()
    {
        for (var vuelta = 0; vuelta < 10; vuelta++)
        {
            await bd.Limpiar();
            var cusco = await bd.AgregarFuente("cusco");
            var ayacucho = await bd.AgregarFuente("ayacucho");

            var anteriores = await Task.WhenAll(new[] { 2, 3 }.Select(async dias =>
            {
                var pila = new PilaDeFuentes(bd, Admin);
                var repo = new NpgsqlRepositorioDeRutas(pila.Acceso);
                return (Dias: dias, Anterior: await En(pila, t => repo.Definir(RutaEntreFuentes.Definir(cusco, ayacucho, dias), t)));
            }));

            // Uno ve «sin ruta» y el otro ve exactamente el valor que dejó el primero: nunca dos «sin ruta».
            var sinRuta = anteriores.Single(a => a.Anterior is null);
            var conAnterior = anteriores.Single(a => a.Anterior is not null);
            Assert.Equal(sinRuta.Dias, conAnterior.Anterior);
            Assert.Equal(conAnterior.Dias, await bd.Consultar<int>("SELECT plazo_dias FROM fuentes.rutas"));
        }
    }

    // --- fichas ---

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Fichas_GuardarEsUnUpsert_YLaClaveCompuestaImpideDigitalizarUnaEdicionConFilaDeStock()
    {
        var lima = await bd.AgregarFuente("lima");
        var pila = new PilaDeFuentes(bd, Admin);
        var libro = Guid.NewGuid();

        var alta = await En(pila, t => pila.Fichas.Guardar(FichaDeEdicion.Crear(libro, FormatoDeEdicion.Impreso), t));
        var repetida = await En(pila, t => pila.Fichas.Guardar(FichaDeEdicion.Crear(libro, FormatoDeEdicion.Impreso), t));
        await bd.PonerStock(lima, libro, 0, 0);

        // Aun con una fila de ceros: la clave compuesta lo impide (por eso HayExistencias cuenta cualquier fila).
        var conflicto = await En(pila, t => pila.Fichas.Guardar(FichaDeEdicion.Crear(libro, FormatoDeEdicion.Digital), t));
        var hay = await En(pila, t => pila.Existencias.HayExistencias(libro, t));

        Assert.Equal(ResultadoDeGuardadoDeFicha.Guardada, alta);
        Assert.Equal(ResultadoDeGuardadoDeFicha.Guardada, repetida);
        Assert.Equal(ResultadoDeGuardadoDeFicha.FormatoEnConflicto, conflicto);
        Assert.True(hay);
        Assert.Equal("impreso", await bd.Consultar<string>($"SELECT formato FROM fuentes.ediciones WHERE edicion_id = '{libro}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task Fichas_SonTodasImpresas_YPorIds_DistinguenDigitalesYDesconocidas()
    {
        var pila = new PilaDeFuentes(bd, Admin);
        var impresa = await bd.AgregarEdicion();
        var digital = await bd.AgregarEdicion("digital");

        Assert.True(await En(pila, t => pila.Fichas.SonTodasImpresas([impresa, impresa], t)));
        Assert.False(await En(pila, t => pila.Fichas.SonTodasImpresas([impresa, digital], t)));
        Assert.False(await En(pila, t => pila.Fichas.SonTodasImpresas([impresa, Guid.NewGuid()], t)));
        Assert.Equal(2, (await En(pila, t => pila.Fichas.PorIds([impresa, digital, Guid.NewGuid()], t))).Count);
    }

    // --- disponibilidad y existencias ---

    private static ConsultaDeExistencias Consulta(PilaDeFuentes pila, IReservasDeStock reservas) =>
        new(pila.Unidad, new NpgsqlRepositorioDeFuentes(pila.Acceso), pila.Existencias, reservas);

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public async Task Disponibilidad_DaElTotalDeLaRedYElDesglose_YUnaSextaFuenteAparecenConCero()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        for (var i = 0; i < 4; i++)
        {
            await bd.AgregarFuente($"sucursal-{i}", $"Sucursal {i}");
        }

        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 8, 0);
        await bd.PonerStock(cusco, libro, 3, 0);
        var pila = new PilaDeFuentes(bd, ActorActual.DeIntegracion("e-commerce"));

        var resultado = (await Consulta(pila, new ReservasDeStockFijo()).Disponibilidad([libro]))[0];

        Assert.Equal(11, resultado.VendibleDeLaRed);
        Assert.Equal(6, resultado.PorFuente.Count);
        Assert.Equal(8, resultado.PorFuente.Single(f => f.CodigoDeFuente == "lima").Vendible);
        Assert.Equal(4, resultado.PorFuente.Count(f => f.Vendible == 0 && f.CodigoDeFuente.StartsWith("sucursal-", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Disponibilidad_LasVencidasSinProcesarNoDescuentan_Fisico5Reservado5Vencidas2_DaDos()
    {
        var lima = await bd.AgregarFuente("lima");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 5);
        var reservas = new ReservasDeStockFijo();
        reservas.Vencidas.Add(new UnidadesVencidas(lima, libro, 2));
        var pila = new PilaDeFuentes(bd, ActorActual.DelSistema());

        var resultado = (await Consulta(pila, reservas).Disponibilidad([libro]))[0];

        Assert.Equal(2, resultado.VendibleDeLaRed);
    }

    /// <summary>Las reservas vencidas de la prueba, leídas de una tabla con la misma conexión y la misma instantánea que el stock.</summary>
    private sealed class ReservasEnTabla(PilaDeFuentes pila) : IReservasDeStock
    {
        public async Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
        {
            await using var comando = new NpgsqlCommand(
                "SELECT fuente_id, edicion_id, unidades FROM public.reservas_prueba WHERE edicion_id = ANY(@ids) AND unidades > 0",
                (NpgsqlConnection)pila.Unidad.Conexion,
                (NpgsqlTransaction)pila.Unidad.Transaccion);
            comando.Parameters.AddWithValue("ids", edicionIds.ToArray());
            var filas = new List<UnidadesVencidas>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            while (await lector.ReadAsync(cancelacion))
            {
                filas.Add(new UnidadesVencidas(lector.GetGuid(0), lector.GetGuid(1), lector.GetInt32(2)));
            }

            return filas;
        }

        public Task<IReadOnlyList<UnidadesLiberadas>> LiberarVencidas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task Disponibilidad_UnaExpiracionQueConfirmaALaMitad_NuncaProduceUnValorInvalido()
    {
        var lima = await bd.AgregarFuente("lima");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 5);
        await bd.Ejecutar($"INSERT INTO public.reservas_prueba VALUES ('{lima}', '{libro}', 2)");

        // El estado A es (reservado 5, vencidas 2) y el B, (reservado 3, vencidas 0): en los dos el vendible es 2. Mezclar la mitad de cada uno daría 4 o 0.
        var terminado = false;
        var escritor = Task.Run(async () =>
        {
            var alternar = true;
            while (!Volatile.Read(ref terminado))
            {
                var pila = new PilaDeFuentes(bd, ActorActual.DelSistema());
                await pila.Unidad.Ejecutar(async t =>
                {
                    await pila.Movimientos.Bloquear([libro], t);
                    await pila.Movimientos.Aplicar(new MovimientoDeStock(lima, libro, 0, alternar ? -2 : 2), t);
                    await using var comando = new NpgsqlCommand(
                        "UPDATE public.reservas_prueba SET unidades = @u",
                        (NpgsqlConnection)pila.Unidad.Conexion,
                        (NpgsqlTransaction)pila.Unidad.Transaccion);
                    comando.Parameters.AddWithValue("u", alternar ? 0 : 2);
                    await comando.ExecuteNonQueryAsync(t);
                    return 0;
                });
                alternar = !alternar;
            }
        });

        var vistos = new List<int>();
        for (var i = 0; i < 150; i++)
        {
            var pila = new PilaDeFuentes(bd, ActorActual.DeIntegracion("e-commerce"));
            vistos.Add((await Consulta(pila, new ReservasEnTabla(pila)).Disponibilidad([libro]))[0].VendibleDeLaRed);
        }

        Volatile.Write(ref terminado, true);
        await escritor;
        Assert.All(vistos, v => Assert.Equal(2, v));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-15")]
    [Trait("Criterio", "AC-FUE-07")]
    public async Task Existencias_FiltraPorEdicionYEstado_ConLosEstadosSinStockBajoYNormal_YPagina()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cero = await bd.AgregarEdicion();
        var dos = await bd.AgregarEdicion();
        var tres = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, cero, 3, 3);
        await bd.PonerStock(ayacucho, dos, 2, 0);
        await bd.PonerStock(ayacucho, tres, 3, 0);
        var pila = new PilaDeFuentes(bd, ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, ayacucho));
        var consulta = Consulta(pila, new ReservasDeStockFijo());

        var todas = await consulta.DeLaFuente(new ConsultaDeExistenciasDeFuente(ayacucho, null, null, 1, 10));
        var bajo = await consulta.DeLaFuente(new ConsultaDeExistenciasDeFuente(ayacucho, null, EstadoDeExistencia.Bajo, 1, 10));
        var filtradas = await consulta.DeLaFuente(new ConsultaDeExistenciasDeFuente(ayacucho, [tres], null, 1, 10));
        var vacia = await consulta.DeLaFuente(new ConsultaDeExistenciasDeFuente(ayacucho, [], null, 1, 10));
        var segunda = await consulta.DeLaFuente(new ConsultaDeExistenciasDeFuente(ayacucho, null, null, 2, 2));

        Assert.Equal(3, todas.Total);
        Assert.Equal(
            [(cero, EstadoDeExistencia.SinStock), (dos, EstadoDeExistencia.Bajo), (tres, EstadoDeExistencia.Normal)],
            todas.Elementos.OrderBy(e => new[] { cero, dos, tres }.ToList().IndexOf(e.EdicionId)).Select(e => (e.EdicionId, e.Estado)));
        Assert.Equal([dos], bajo.Elementos.Select(e => e.EdicionId));
        Assert.Equal([tres], filtradas.Elementos.Select(e => e.EdicionId));
        Assert.Empty(vacia.Elementos);
        Assert.Equal((3, 1), (segunda.Total, segunda.Elementos.Count));
    }
}
