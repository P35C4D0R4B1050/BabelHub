using System.Net;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Infrastructure.Reservas;
using BabelHub.IntegrationTests.Traslados;
using BabelHub.Shared.Contratos;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T41: la venta por sucursal con PostgreSQL real y el contenedor de producción: 50 clientes simultáneos sobre una sucursal con líneas repetidas (AC-SEL-47, SEL-04), el reemplazo que conserva la reserva anterior
/// (AC-SEL-50), la reserva anterior a esta versión que sigue su curso con su traslado (AC-SEL-48) y la entrega por sucursal de punta a punta por la Api (AC-SEL-46).
/// </summary>
public sealed class VentaPorSucursalPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(120);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ReservarEntrada Recojo(Guid pedido, params LineaSolicitada[] lineas) =>
        new(pedido, TipoDeEntregaDeReserva.Recojo, null, null, lineas, Guid.NewGuid().ToString("N"));

    private static LineaSolicitada En(string sucursal, Guid libro, int cantidad) => new(Guid.NewGuid(), libro, cantidad, sucursal);

    private async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    // --- concurrencia ---

    [Fact]
    [Trait("Criterio", "AC-SEL-47")]
    public async Task CincuentaClientesSobreUnaSucursal_ConUnaODosLineas_NuncaSuperanElVendible_NiQuedaFueraUnaQueCabia()
    {
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");

        // Varias rondas con una edición nueva: cada una es una carrera distinta.
        for (var ronda = 0; ronda < 3; ronda++)
        {
            var libro = await bd.AgregarEdicion();
            await bd.PonerStock(cusco, libro, 20, 0);
            await bd.PonerStock(ayacucho, libro, 40, 0);
            using var pila = new PilaDeReservas(bd);
            var antes = await Interbloqueos();

            // Mitad con una línea de 1 unidad y mitad con dos líneas de 1 unidad de la misma edición en la misma sucursal: 75 unidades pedidas por 20.
            var solicitudes = Enumerable.Range(0, 50)
                .Select(i => Recojo(Guid.NewGuid(), [.. Enumerable.Range(0, 1 + (i % 2)).Select(_ => En("cusco", libro, 1))]))
                .ToList();
            var compuerta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tareas = solicitudes.Select(s => Task.Run(async () =>
            {
                await compuerta.Task;
                return (Solicitud: s, Resultado: await pila.Reservar(Cliente(), s));
            })).ToList();
            compuerta.SetResult();
            var resultados = await Task.WhenAll(tareas).WaitAsync(Limite);

            var (_, reservado) = (await bd.StockDe(cusco, libro))!.Value;
            var aceptadas = resultados.Where(r => r.Resultado.EsExito).ToList();
            var rechazadas = resultados.Where(r => !r.Resultado.EsExito).ToList();

            Assert.Equal(20, reservado);
            Assert.Equal(reservado, aceptadas.Sum(r => r.Solicitud.Lineas.Count));
            Assert.All(rechazadas, r => Assert.Equal((CategoriaDeError.Conflicto, "stock-insuficiente"), (r.Resultado.Error!.Categoria, r.Resultado.Error.Clave)));

            // Ninguna que todavía cabía quedó fuera: lo libre al final es menor que lo que pedía cada rechazada.
            Assert.All(rechazadas, r => Assert.True(20 - reservado < r.Solicitud.Lineas.Count, "Se rechazó una solicitud que cabía."));

            // Cada reserva quedó entera o no apartó nada.
            foreach (var (solicitud, resultado) in resultados)
            {
                var lineas = await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{solicitud.PedidoId}'");
                Assert.Equal(resultado.EsExito ? solicitud.Lineas.Count : 0, lineas);
            }

            // Ni la otra sucursal ni el vendible negativo.
            Assert.Equal((40, 0), await bd.StockDe(ayacucho, libro));
            Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.Equal(antes, await Interbloqueos());
        }
    }

    // --- el reemplazo ---

    [Fact]
    [Trait("Criterio", "AC-SEL-50")]
    public async Task ElReemplazoPorSucursalCuentaLoQueDevuelve_YSiPideDeMas_LaAnteriorSeConservaEnLaBase()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, libro, 6, 0);
        await bd.PonerStock(cusco, libro, 30, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(cliente, Recojo(pedido, En("ayacucho", libro, 6)))).EsExito);

        var reemplazo = await pila.Reservar(cliente, Recojo(pedido, En("ayacucho", libro, 4), En("cusco", libro, 2)));
        Assert.True(reemplazo.EsExito, reemplazo.Error?.Clave);

        var deMas = await pila.Reservar(cliente, Recojo(pedido, En("ayacucho", libro, 7)));

        Assert.Equal("stock-insuficiente", deMas.Error!.Clave);
        Assert.Equal((6, 4), await bd.StockDe(ayacucho, libro));
        Assert.Equal((30, 2), await bd.StockDe(cusco, libro));
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND estado = 'pendiente'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT max(reemplazos)::bigint FROM reservas.reservas WHERE pedido_id = '{pedido}'"));
    }

    // --- la reserva anterior a esta versión ---

    [Fact]
    [Trait("Criterio", "AC-SEL-48")]
    public async Task UnaReservaAnteriorPendiente_TrasActualizarElEsquemaDosVeces_SeConfirmaViajaPorSuTraslado_YSoloLaEntregaLaSucursalDeRecojo()
    {
        // La base tal como la dejó la versión anterior, con una reserva sin sucursal por línea cuya fuente (Cusco) no es la sucursal de recojo (Lima).
        await bd.Ejecutar(
            """
            ALTER TABLE reservas.reservas DROP CONSTRAINT ck_reservas_destino;
            ALTER TABLE reservas.reservas ADD CONSTRAINT ck_reservas_destino CHECK (
                (tipo_entrega = 'recojo' AND sucursal_recojo_id IS NOT NULL AND region_entrega IS NULL)
                OR (tipo_entrega = 'domicilio' AND region_entrega IS NOT NULL AND sucursal_recojo_id IS NULL));
            """);
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 5);
        var entrada = ARecojo("lima", [Linea(escenario.Libro, 1)]);
        Assert.True((await escenario.Pila.Reservar(Cliente(), entrada)).EsExito);

        for (var vez = 0; vez < 2; vez++)
        {
            await bd.Ejecutar(DefinicionDeReservas.Sql);
            await bd.Ejecutar(DefinicionDeReservas.Permisos);
        }

        var confirmada = await escenario.Pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(EscenarioDeTraslados.Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));
        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        var traslado = Assert.Single(confirmada.Valor.Traslados);
        Assert.Equal(("cusco", "lima"), (traslado.CodigoDeOrigen, traslado.CodigoDeDestino));
        var administracion = EscenarioDeTraslados.Administracion();
        Assert.True((await escenario.Ejecutar(administracion, new AprobarTrasladoEntrada(traslado.TrasladoId))).EsExito);
        Assert.True((await escenario.Ejecutar(administracion, new DespacharTrasladoEntrada(traslado.TrasladoId))).EsExito);
        Assert.True((await escenario.Ejecutar(administracion, new RecibirTrasladoEntrada(traslado.TrasladoId))).EsExito);

        Task<Resultado<CierreDeReserva>> Entregar(ActorActual actor) => escenario.Pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(
            actor, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Entrega, [entrada.Lineas[0].LineaId], null, Guid.NewGuid().ToString("N")));

        var delOrigen = await Entregar(EscenarioDeTraslados.Encargado(escenario.Cusco));
        var delRecojo = await Entregar(EscenarioDeTraslados.Encargado(escenario.Lima));

        Assert.Equal(CategoriaDeError.Prohibido, delOrigen.Error!.Categoria);
        Assert.True(delRecojo.EsExito, delRecojo.Error?.Clave);
        Assert.Equal((0, 0), await bd.StockDe(escenario.Lima, escenario.Libro));
        Assert.Equal((4, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
    }

    // --- la entrega por sucursal, de punta a punta ---

    [Fact]
    [Trait("Criterio", "AC-SEL-46")]
    public async Task LaEntregaPorSucursalPorLaApi_CadaEncargadoSoloLaSuya_YLaAdministracionCualquiera()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, libro, 10, 0);
        await bd.PonerStock(cusco, libro, 10, 0);
        await using var api = await ApiDeReservasHost.Crear(bd);
        var pedido = Guid.NewGuid();
        var deAyacucho = Guid.NewGuid();
        var deCusco = Guid.NewGuid();
        var otroDeCusco = Guid.NewGuid();
        string Linea(Guid id, string sucursal, int cantidad) => $$"""{"lineaId":"{{id}}","edicionId":"{{libro}}","cantidad":{{cantidad}},"codigoDeSucursal":"{{sucursal}}"}""";
        var creada = await api.Enviar(
            HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), clave: Guid.NewGuid().ToString("N"),
            json: $$"""{"pedidoId":"{{pedido}}","tipoDeEntrega":"recojo","lineas":[{{Linea(deAyacucho, "ayacucho", 2)}},{{Linea(deCusco, "cusco", 3)}},{{Linea(otroDeCusco, "cusco", 1)}}]}""");
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var confirmada = await api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/confirmar", ApiDeReservasHost.CredencialDeExpiracion, clave: Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);

        Task<HttpResponseMessage> Entregar(Guid sucursal, params Guid[] lineas) => api.Enviar(
            HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, sucursal, Guid.NewGuid(),
            json: $$"""{"motivo":"entrega","lineaIds":[{{string.Join(',', lineas.Select(l => $"\"{l}\""))}}]}""", clave: Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Forbidden, (await Entregar(ayacucho, deCusco)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Entregar(ayacucho, deAyacucho, deCusco)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Entregar(ayacucho, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Entregar(ayacucho)).StatusCode);
        Assert.Equal((10, 2), await bd.StockDe(ayacucho, libro));
        Assert.Equal((10, 4), await bd.StockDe(cusco, libro));

        Assert.Equal(HttpStatusCode.OK, (await Entregar(ayacucho, deAyacucho)).StatusCode);
        Assert.Equal((8, 0), await bd.StockDe(ayacucho, libro));
        Assert.Equal((10, 4), await bd.StockDe(cusco, libro));

        Assert.Equal(HttpStatusCode.OK, (await Entregar(cusco, deCusco)).StatusCode);
        Assert.Equal((7, 1), await bd.StockDe(cusco, libro));

        var administracion = await api.Enviar(
            HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.AdministradorCentral, usuario: Guid.NewGuid(),
            json: $$"""{"motivo":"entrega","lineaIds":["{{otroDeCusco}}"]}""", clave: Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, administracion.StatusCode);
        Assert.Equal((6, 0), await bd.StockDe(cusco, libro));
    }
}
