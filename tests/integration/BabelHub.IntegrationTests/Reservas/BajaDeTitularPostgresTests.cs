using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T23: <c>ProcesarBajaDeTitular</c> con PostgreSQL real (SEL-34, SEL-35, AC-SEL-36): el identificador anónimo en las reservas y en las claves de límite, la huella de origen anulada, el conteo del origen intacto y la repetición sin efecto.</summary>
public sealed class BajaDeTitularPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    private static readonly ActorActual ConCredencialDeBaja = ActorActual.DeIntegracion("baja-de-titular");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<Resultado<BajaDeTitularProcesada>> Baja(PilaDeReservas pila, Guid titular) =>
        pila.Ejecutar<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada>(ConCredencialDeBaja, new ProcesarBajaDeTitularEntrada(titular));

    private static Guid Anonimo(PilaDeReservas pila, Guid titular) => pila.Proveedor.GetRequiredService<IGeneradorDeIdentificadorAnonimo>().Generar(titular);

    private async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    private Task<long> EventosConSujeto(Guid sujeto) => bd.Consultar<long>($"SELECT count(*) FROM plataforma.eventos_limite WHERE clave LIKE '%|u:s:{sujeto:D}'");

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LaBaja_SustituyeElTitularEnReservasYEnClavesDeLimite_SinTocarStockNiConteoDelOrigen()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 50, 0);
        using var pila = new PilaDeReservas(bd);
        var titular = Guid.NewGuid();
        var otro = Guid.NewGuid();

        // Una confirmada (con huella de origen), una cerrada por entrega, y una pendiente de otro titular.
        var confirmada = ARecojo("lima", [Linea(libro, 2)]);
        var entregada = ARecojo("lima", [Linea(libro, 1)]);
        var deOtro = ARecojo("lima", [Linea(libro, 1)]);

        // Una pendiente por cliente (SEL-13): cada reserva se confirma antes de iniciar la siguiente.
        foreach (var entrada in new[] { confirmada, entregada })
        {
            Assert.True((await pila.Reservar(Cliente(titular), entrada)).EsExito);
            Assert.True((await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")))).EsExito);
        }

        Assert.True((await pila.Reservar(Cliente(otro), deOtro)).EsExito);

        var encargado = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, lima);
        Assert.True((await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(encargado, new CerrarReservasEntrada(entregada.PedidoId, MotivoDeCierreDeReserva.Entrega, [entregada.Lineas[0].LineaId], null, Guid.NewGuid().ToString("N")))).EsExito);

        var stock = await bd.StockDe(lima, libro);
        var lineas = await bd.Consultar<string>("SELECT string_agg(pedido_id || ':' || estado || ':' || cantidad, ',' ORDER BY pedido_id, linea_id) FROM reservas.reserva_lineas");
        var eventosTotales = await bd.Consultar<long>("SELECT count(*) FROM plataforma.eventos_limite");
        var eventosDelOrigen = await bd.Consultar<long>("SELECT count(*) FROM plataforma.eventos_limite WHERE clave LIKE '%|o:%'");
        var eventosDelTitular = await EventosConSujeto(titular);
        Assert.True(eventosDelTitular > 0);
        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE huella_origen IS NOT NULL"));
        var anonimo = Anonimo(pila, titular);

        var resultado = await Baja(pila, titular);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.False(resultado.Valor.EraRepeticion);
        Assert.NotEqual(titular, anonimo);
        Assert.NotEqual(Anonimo(pila, otro), anonimo);

        // Las reservas del titular llevan el anónimo, sin huella de origen; las del otro no cambian.
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{anonimo}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{anonimo}' AND huella_origen IS NOT NULL"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{otro}' AND huella_origen IS NOT NULL"));

        // Las claves de los eventos de límite: el original desaparece, el conteo no cambia (por cliente anónimo ni por origen).
        Assert.Equal(0L, await EventosConSujeto(titular));
        Assert.Equal(eventosDelTitular, await EventosConSujeto(anonimo));
        Assert.Equal(eventosTotales, await bd.Consultar<long>("SELECT count(*) FROM plataforma.eventos_limite"));
        Assert.Equal(eventosDelOrigen, await bd.Consultar<long>("SELECT count(*) FROM plataforma.eventos_limite WHERE clave LIKE '%|o:%'"));

        // Stock, líneas y unidades vendidas intactos.
        Assert.Equal(stock, await bd.StockDe(lima, libro));
        Assert.Equal(lineas, await bd.Consultar<string>("SELECT string_agg(pedido_id || ':' || estado || ':' || cantidad, ',' ORDER BY pedido_id, linea_id) FROM reservas.reserva_lineas"));
        var vendidas = await pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<BabelHub.Inventario.Application.Fuentes.IOrigenDeUnidadesVendidas>().Ventas(null, pila.Reloj.Ahora.AddDays(-30), pila.Reloj.Ahora, token)));
        Assert.Equal(3, vendidas.Sum(v => v.Unidades));

        // La baja quedó registrada y se auditó una sola vez, con el anónimo.
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.bajas_recibidas WHERE identificador_opaco = '{titular}' AND entregada_a_auditoria_en IS NULL"));
        var registro = Assert.Single(pila.Auditoria.Registros, r => r.Accion == AccionesAuditables.BajaDeTitularProcesada.Identificador);
        Assert.Equal(anonimo.ToString("D"), registro.ObjetoId);

        // Reservar con la cuenta dada de baja se rechaza.
        var rechazada = await pila.Reservar(Cliente(titular), ARecojo("lima", [Linea(libro, 1)]));
        Assert.Equal("titular-dado-de-baja", rechazada.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task RepetirLaBaja_NoCambiaNada_NiAudita_YUnTitularSinReservasNoFalla()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var titular = Guid.NewGuid();
        Assert.True((await pila.Reservar(Cliente(titular), ARecojo("lima", [Linea(libro, 1)]))).EsExito);

        var primera = await Baja(pila, titular);
        var estado = await bd.Consultar<string>("SELECT string_agg(cliente_id::text || coalesce(huella_origen, ''), ',' ORDER BY id) FROM reservas.reservas");
        var eventos = await bd.Consultar<string>("SELECT string_agg(clave, ',' ORDER BY clave) FROM plataforma.eventos_limite");
        var segunda = await Baja(pila, titular);
        var sinReservas = await Baja(pila, Guid.NewGuid());

        Assert.False(primera.Valor.EraRepeticion);
        Assert.True(segunda.Valor.EraRepeticion);
        Assert.True(sinReservas.EsExito);
        Assert.Equal(estado, await bd.Consultar<string>("SELECT string_agg(cliente_id::text || coalesce(huella_origen, ''), ',' ORDER BY id) FROM reservas.reservas"));
        Assert.Equal(eventos, await bd.Consultar<string>("SELECT string_agg(clave, ',' ORDER BY clave) FROM plataforma.eventos_limite"));
        Assert.Equal(2, pila.Auditoria.Registros.Count(r => r.Accion == AccionesAuditables.BajaDeTitularProcesada.Identificador));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnaBajaSinLaCredencialDeSuAlcance_SeRechaza_SinEscribirNada()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var titular = Guid.NewGuid();
        Assert.True((await pila.Reservar(Cliente(titular), ARecojo("lima", [Linea(libro, 1)]))).EsExito);

        foreach (var otro in new[] { Cliente(titular), Servicio, ActorActual.DeIntegracion("consulta-de-pendientes") })
        {
            var rechazo = await pila.Ejecutar<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada>(otro, new ProcesarBajaDeTitularEntrada(titular));
            Assert.Equal(CategoriaDeError.Prohibido, rechazo.Error!.Categoria);
        }

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
        Assert.DoesNotContain(pila.Auditoria.Registros, r => r.Accion == AccionesAuditables.BajaDeTitularProcesada.Identificador);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task DiezBajasSimultaneasDelMismoTitular_AplicanUnaSola_ConUnaAuditoria_YSinInterbloqueos()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 20, 0);
        using var pila = new PilaDeReservas(bd);
        var titular = Guid.NewGuid();
        Assert.True((await pila.Reservar(Cliente(titular), ARecojo("lima", [Linea(libro, 1)]))).EsExito);
        var antes = await Interbloqueos();
        var compuerta = new TaskCompletionSource();
        var tareas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await compuerta.Task;
            return await Baja(pila, titular);
        })).ToList();
        compuerta.SetResult();

        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(1, resultados.Count(r => !r.Valor.EraRepeticion));
        Assert.Single(pila.Auditoria.Registros, r => r.Accion == AccionesAuditables.BajaDeTitularProcesada.Identificador);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await Interbloqueos() - antes);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnaBajaYReservasDelMismoClienteALaVez_NuncaDejanElIdentificadorOriginalEnUnaReserva()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 1000, 0);
        using var pila = new PilaDeReservas(bd);
        var titular = Guid.NewGuid();
        var antes = await Interbloqueos();
        var compuerta = new TaskCompletionSource();
        var reservas = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await compuerta.Task;
            return (await pila.Reservar(Cliente(titular), ARecojo("lima", [Linea(libro, 1)]))).Error?.Clave;
        })).ToList();
        var baja = Task.Run(async () =>
        {
            await compuerta.Task;
            return await Baja(pila, titular);
        });
        compuerta.SetResult();

        var claves = await Task.WhenAll(reservas).WaitAsync(TimeSpan.FromSeconds(120));
        var resultado = await baja.WaitAsync(TimeSpan.FromSeconds(120));

        // Las reservas que llegaron antes de la baja quedan anonimizadas; las que llegaron después se rechazan; ninguna conserva el original.
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.All(claves.Where(c => c is not null), c => Assert.Contains(c, new[] { "titular-dado-de-baja", DetalleDeLimiteDeReservas.ClaveDeLimiteExcedido, "reserva-existente" }));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
        Assert.Equal(0L, await EventosConSujeto(titular));
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await Interbloqueos() - antes);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task UnaBajaConUnIdentificadorYaAnonimo_NoLoVuelveAAnonimizar()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var titular = Guid.NewGuid();
        Assert.True((await pila.Reservar(Cliente(titular), ARecojo("lima", [Linea(libro, 1)]))).EsExito);
        Assert.True((await Baja(pila, titular)).EsExito);
        var anonimo = Anonimo(pila, titular);

        // Una baja que llega con el identificador anónimo (un error de quien llama) no cambia lo ya anónimo.
        Assert.True((await Baja(pila, anonimo)).EsExito);

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{anonimo}' AND cliente_anonimizado"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task LasBajasPendientesSeEntreganAAuditoriaYSeMarcan_ConElRolRealDelModulo()
    {
        using var pila = new PilaDeReservas(bd);
        var titulares = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var titular in titulares)
        {
            Assert.True((await Baja(pila, titular)).EsExito);
        }

        var pendientes = await pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IBajasRecibidas>().Pendientes(2, token)));
        await pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
        {
            await sp.GetRequiredService<IBajasRecibidas>().MarcarEntregadas(pendientes, token);
            return 0;
        }));
        var resto = await pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IBajasRecibidas>().Pendientes(10, token)));

        Assert.Equal(2, pendientes.Count);
        Assert.Equal(titulares.Except(pendientes), resto);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas WHERE entregada_a_auditoria_en IS NOT NULL"));
    }
}
