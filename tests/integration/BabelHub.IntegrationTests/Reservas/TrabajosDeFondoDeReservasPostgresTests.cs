using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T25: los trabajos de fondo de <c>Reservas</c> con PostgreSQL real y el reloj controlado: expiración exacta con bloqueo de instancia única, registro de la corrida, alerta a las 24 horas, purgas y depuración de los 2 años.</summary>
public sealed class TrabajosDeFondoDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class EstablecedorNulo : IEstablecedorDeActor
    {
        public void Establecer(ActorActual actor)
        {
        }

        public void Limpiar()
        {
        }
    }

    private async Task<(Guid Lima, Guid Libro)> Escenario(int fisico = 10)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, fisico, 0);
        return (lima, libro);
    }

    private static Task<Resultado<ReservaDelPedido>> Confirmar(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(pedido, Guid.NewGuid().ToString("N")));

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ElTrabajoDeExpiracion_ExpiraLasVencidas_ComoSistema_ConLaAuditoria_YRegistraLaCorrida()
    {
        var (lima, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd);
        var vencida = ARecojo("lima", [Linea(libro, 2)]);
        var vigente = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), vencida)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(10);
        Assert.True((await pila.Reservar(Cliente(), vigente)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(6);
        Assert.Equal((10, 3), await bd.StockDe(lima, libro));

        var trabajo = TrabajosDeReservas.Expirar(pila.Proveedor);
        var resultado = await trabajo.EjecutarUnaVez();

        Assert.Equal(ResultadoDeCorrida.Exitosa, resultado);
        Assert.Equal("expirada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{vencida.PedidoId}'"));
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{vigente.PedidoId}'"));
        Assert.Equal((10, 1), await bd.StockDe(lima, libro));
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
        var auditada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaExpirada");
        Assert.Equal(TipoDeActor.Sistema, auditada.Actor);
        Assert.Equal(vencida.PedidoId.ToString("D"), auditada.ObjetoId);
        Assert.Contains(pila.Registro.Corridas, c => c.Trabajo == TrabajosDeReservas.Expiracion && c.Resultado == ResultadoDeTrabajo.Exito);

        // Repetir la corrida no cambia nada.
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaExpirada");
        Assert.Equal((10, 1), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ConElBloqueoDeInstanciaUnicaTomadoPorOtra_LaCorridaSeOmite_SinTocarNiRegistrarNada()
    {
        var (lima, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);
        var trabajo = TrabajosDeReservas.Expirar(pila.Proveedor);

        // Otra instancia tiene el bloqueo del trabajo (una sesión propia con el mismo candado asesor).
        await using (var otra = await bd.Acceso.OpenConnectionAsync())
        {
            await using (var tomar = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended('reservas.trabajo|' || @n, 0))", otra))
            {
                tomar.Parameters.AddWithValue("n", TrabajosDeReservas.Expiracion);
                await tomar.ExecuteNonQueryAsync();
            }

            var omitida = await trabajo.EjecutarUnaVez();

            Assert.Equal(ResultadoDeCorrida.OmitidaPorOtraInstancia, omitida);
            Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
            Assert.Empty(pila.Registro.Corridas);

            // La otra instancia termina (el bloqueo de sesión se libera con su sesión; aquí, con la conexión devuelta al grupo, se suelta explícitamente).
            await using var soltar = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended('reservas.trabajo|' || @n, 0))", otra);
            soltar.Parameters.AddWithValue("n", TrabajosDeReservas.Expiracion);
            await soltar.ExecuteNonQueryAsync();
        }

        // Liberado el bloqueo, la corrida sigue.
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        Assert.Equal("expirada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal((10, 0), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task UnTrabajoQuePerdioSuActor_FallaALaVista_NoHaceNada_YQuedaRegistradoComoFallo()
    {
        var (_, libro) = await Escenario();

        // El ejecutor de esta pila no fija el actor «sistema»: lo que un trabajo mal compuesto haría sin saberlo.
        using var pila = new PilaDeReservas(bd, s => s.AddSingleton<EjecutorDeTrabajoDeFondo>(sp => new EjecutorDeTrabajoDeFondo(new EstablecedorNulo(), sp.GetRequiredService<IActorActual>())));
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);

        var resultado = await TrabajosDeReservas.Expirar(pila.Proveedor).EjecutarUnaVez();

        Assert.Equal(ResultadoDeCorrida.Fallida, resultado);
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Contains(pila.Registro.Corridas, c => c.Trabajo == TrabajosDeReservas.Expiracion && c.Resultado == ResultadoDeTrabajo.Fallo);
        Assert.DoesNotContain(pila.Auditoria.Registros, r => r.Accion == "ReservaExpirada");
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task SinUnaCorridaExitosaEn24Horas_SePublicaUnaAlertaTecnica_UnaVezPorPlazo()
    {
        using var pila = new PilaDeReservas(bd);
        var trabajo = new TrabajoDeFondoDeReservas(
            "reservas-de-prueba",
            TimeSpan.FromSeconds(30),
            (_, _) => throw new InvalidOperationException("falla de prueba"),
            pila.Proveedor.GetRequiredService<IServiceScopeFactory>(),
            pila.Proveedor.GetRequiredService<EjecutorDeTrabajoDeFondo>(),
            pila.Proveedor.GetRequiredService<BloqueoDeInstanciaUnica>(),
            pila.Registro,
            pila.Reloj);

        Assert.Equal(ResultadoDeCorrida.Fallida, await trabajo.EjecutarUnaVez());
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(23).AddMinutes(59);
        Assert.Equal(ResultadoDeCorrida.Fallida, await trabajo.EjecutarUnaVez());
        Assert.Empty(pila.AlertasPublicadas.Tecnicas);

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(1);
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Equal([TrabajoDeFondoDeReservas.MotivoDeTrabajoDetenido], pila.AlertasPublicadas.Tecnicas);

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(24);
        await trabajo.EjecutarUnaVez();
        Assert.Equal(2, pila.AlertasPublicadas.Tecnicas.Count);
        Assert.All(pila.Registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task UnaCorridaExitosa_ReiniciaElPlazoDeLas24Horas()
    {
        using var pila = new PilaDeReservas(bd);
        var fallar = true;
        var trabajo = new TrabajoDeFondoDeReservas(
            "reservas-de-prueba",
            TimeSpan.FromSeconds(30),
            (_, _) => fallar ? throw new InvalidOperationException("falla de prueba") : Task.CompletedTask,
            pila.Proveedor.GetRequiredService<IServiceScopeFactory>(),
            pila.Proveedor.GetRequiredService<EjecutorDeTrabajoDeFondo>(),
            pila.Proveedor.GetRequiredService<BloqueoDeInstanciaUnica>(),
            pila.Registro,
            pila.Reloj);

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(20);
        fallar = false;
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        fallar = true;
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(10);

        Assert.Equal(ResultadoDeCorrida.Fallida, await trabajo.EjecutarUnaVez());
        Assert.Empty(pila.AlertasPublicadas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaPurgaDeHuellas_AnulaLaHuellaDeOrigenAPartirDeLas24Horas_SinTocarLoDemas()
    {
        var (lima, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        var trabajo = TrabajosDeReservas.PurgarHuellas(pila.Proveedor);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas WHERE huella_origen IS NOT NULL"));

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(23).AddMinutes(59);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas WHERE huella_origen IS NOT NULL"));

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(2);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas WHERE huella_origen IS NOT NULL"));
        Assert.Equal((10, 1), await bd.StockDe(lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaPurgaDeRegistros_BorraLasOperacionesIdempotentesA30Dias_YLasBajasConsumidasA30DiasDeLaEntrega_PeroNuncaLasNoConsumidas()
    {
        var (lima, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd);
        // `creada_en` la pone la base con su hora real: el reloj de la prueba parte de la hora real para que los 30 días se midan desde ahí (no desde una fecha fija que se vuelve vieja).
        pila.Reloj.Ahora = DateTimeOffset.UtcNow;
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        // Cerrar guarda su operación idempotente (la clave por pedido y operación).
        var encargado = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, lima);
        Assert.True((await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(encargado, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Entrega, [entrada.Lineas[0].LineaId], null, Guid.NewGuid().ToString("N")))).EsExito);
        var consumida = Guid.NewGuid();
        var pendiente = Guid.NewGuid();
        var baja = ActorActual.DeIntegracion("baja-de-titular");
        foreach (var titular in new[] { consumida, pendiente })
        {
            Assert.True((await pila.Ejecutar<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada>(baja, new ProcesarBajaDeTitularEntrada(titular))).EsExito);
        }

        await pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
        {
            await sp.GetRequiredService<IBajasRecibidas>().MarcarEntregadas([consumida], token);
            return 0;
        }));
        var trabajo = TrabajosDeReservas.PurgarRegistros(pila.Proveedor);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.operaciones_idempotentes"));

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddDays(29);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.operaciones_idempotentes"));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddDays(2);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.operaciones_idempotentes"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.bajas_recibidas WHERE identificador_opaco = '{pendiente}'"));

        // La baja no consumida se conserva por mucho que pase el tiempo.
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddDays(400);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await trabajo.EjecutarUnaVez());
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaDepuracionDiaria_AnonimizaElClienteA2AñosDeLaUltimaTransicion_ConElMismoIdentificadorDeLaBaja_YNoVuelveACalcularlo()
    {
        var (_, libro) = await Escenario(fisico: 50);
        using var pila = new PilaDeReservas(bd);
        var generador = pila.Proveedor.GetRequiredService<IGeneradorDeIdentificadorAnonimo>();
        var antiguo = Guid.NewGuid();
        var vivo = Guid.NewGuid();

        // Una reserva que vence sin pagarse y otra confirmada que sigue viva; el reloj avanza lo que haga falta.
        var vencida = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(antiguo), vencida)).EsExito);
        var confirmada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(vivo), confirmada)).EsExito);
        Assert.True((await Confirmar(pila, confirmada.PedidoId)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await TrabajosDeReservas.Expirar(pila.Proveedor).EjecutarUnaVez());
        var depuracion = TrabajosDeReservas.Depurar(pila.Proveedor);

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddYears(2).AddDays(-1);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await depuracion.EjecutarUnaVez());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas WHERE cliente_anonimizado"));

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddDays(2);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await depuracion.EjecutarUnaVez());

        // Solo la vencida (sin línea viva): la confirmada del otro cliente sigue con su identificador.
        var anonimo = generador.Generar(antiguo);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{anonimo}' AND cliente_anonimizado AND huella_origen IS NULL"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{antiguo}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{vivo}' AND NOT cliente_anonimizado"));

        // Repetible: el identificador ya anónimo no se vuelve a calcular.
        Assert.Equal(ResultadoDeCorrida.Exitosa, await depuracion.EjecutarUnaVez());
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{anonimo}'"));

        // La baja posterior del mismo titular produce el mismo identificador y no cambia lo ya anónimo.
        var baja = await pila.Ejecutar<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada>(ActorActual.DeIntegracion("baja-de-titular"), new ProcesarBajaDeTitularEntrada(antiguo));
        Assert.True(baja.EsExito, baja.Error?.Clave);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{anonimo}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task LaBajaMarcaLasCabecerasComoAnonimas_YLaDepuracionNoLasVuelveACalcular()
    {
        var (_, libro) = await Escenario();
        using var pila = new PilaDeReservas(bd);
        var generador = pila.Proveedor.GetRequiredService<IGeneradorDeIdentificadorAnonimo>();
        var titular = Guid.NewGuid();
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(titular), entrada)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await TrabajosDeReservas.Expirar(pila.Proveedor).EjecutarUnaVez());
        Assert.True((await pila.Ejecutar<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada>(ActorActual.DeIntegracion("baja-de-titular"), new ProcesarBajaDeTitularEntrada(titular))).EsExito);

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddYears(3);
        Assert.Equal(ResultadoDeCorrida.Exitosa, await TrabajosDeReservas.Depurar(pila.Proveedor).EjecutarUnaVez());

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{generador.Generar(titular)}' AND cliente_anonimizado"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public void ElServicioHospedadoSoloSeRegistraEnModoNormal()
    {
        var restauracion = new ServiceCollection().AgregarTrabajosDeReservas(soloEnModoNormal: false);
        var normal = new ServiceCollection().AgregarTrabajosDeReservas(soloEnModoNormal: true);

        Assert.DoesNotContain(restauracion, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(normal, d => d.ServiceType == typeof(IHostedService));
    }
}
