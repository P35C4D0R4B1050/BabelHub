using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T09: los repositorios de reservas sobre PostgreSQL real con el rol real de la aplicación y el contenedor de producción: reservar atómico con el stock de <c>Fuentes</c>, la rehidratación, la expiración compartida
/// (<c>IReservasDeStock</c> real en lugar del nulo), la lectura de vencidas y los pedidos que apartan unidades.
/// </summary>
public sealed class ReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Escenario(Guid Lima, Guid Cusco, Guid Ayacucho);

    private async Task<Escenario> TresFuentes() =>
        new(await bd.AgregarFuente("lima", recepcion: true), await bd.AgregarFuente("cusco"), await bd.AgregarFuente("ayacucho"));

    // --- el contenedor ---

    [Theory]
    [Trait("Criterio", "AC-SEL-27")]
    [InlineData(false)]
    [InlineData(true)]
    public void ElContenedor_ResuelveElModuloSinCiclos_YElAdaptadorRealReemplazaAlNuloVengaPrimeroOQueVengaDespues(bool reservasPrimero)
    {
        using var pila = new PilaDeReservas(bd, reservasPrimero: reservasPrimero);
        using var ambito = pila.Proveedor.CreateScope();

        // IReservasDeStock (que Fuentes inyecta en MovimientosDeStock) y IAplicadorDeMovimientos (que Reservas usa al expirar) se resuelven sin ciclo.
        Assert.IsType<ReservasDeStock>(ambito.ServiceProvider.GetRequiredService<IReservasDeStock>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IAplicadorDeMovimientos>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IMovimientosDeStock>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<ICasoDeUso<ReservarEntrada, ReservaDelPedido>>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<ICasoDeUso<ExpirarReservasEntrada, ExpiracionDelPedido>>());
        Assert.Empty(ComposicionDeFuentes.AdaptadoresNulosConModuloReal(pila.Proveedor));
        ComposicionDeFuentes.ExigirAdaptadoresReales(pila.Proveedor, esProduccion: true);
    }


    [Fact]
    [Trait("Criterio", "AC-SEL-10")]
    public void LaEstrategiaConfiguradaConUnNombreDesconocido_FallaAlArrancar()
    {
        using var pila = new PilaDeReservas(bd, configuracion: new Dictionary<string, string?> { ["Inventario:EstrategiaDeSeleccion"] = "no-existe" });

        Assert.Throws<EstrategiaDeSeleccionDesconocidaException>(() => ComposicionDeReservas.ExigirEstrategiaDeSeleccion(pila.Proveedor));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public void LosCasosDeReservasLlevanLaTransaccion_YElReintentoPorSerIdempotentes()
    {
        using var pila = new PilaDeReservas(bd);
        using var ambito = pila.Proveedor.CreateScope();

        var reservar = ambito.ServiceProvider.GetRequiredService<ICasoDeUso<ReservarEntrada, ReservaDelPedido>>().GetType();
        var expirar = ambito.ServiceProvider.GetRequiredService<ICasoDeUso<ExpirarReservasEntrada, ExpiracionDelPedido>>().GetType();

        Assert.Equal("ReintentoPorInterbloqueo`2", reservar.Name);
        Assert.Equal("ReintentoPorInterbloqueo`2", expirar.Name);
        Assert.Equal(["ReservarUnidades", "ExpirarReservasVencidas", "ConfirmarReservas"], ComposicionDeReservas.CasosDeUsoIdempotentes.Select(t => t.Name));
    }

    // --- reservar ---

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_EnPostgres_CreaLaReservaPendienteYSubeLoReservadoEnLaFuenteElegida()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 8, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var entrada = ARecojo("lima", [Linea(libro, 2)]);

        var resultado = await pila.Reservar(cliente, entrada);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var reserva = resultado.Valor;
        Assert.Equal(EstadoDeReserva.Pendiente, reserva.Estado);
        Assert.Equal(pila.Reloj.Ahora.AddMinutes(15), reserva.ExpiraEn);
        var linea = Assert.Single(reserva.Lineas);
        Assert.Equal(("lima", 2, false), (linea.CodigoDeFuente, linea.Cantidad, linea.RequiereTraslado));
        Assert.Equal((8, 2), await bd.StockDe(f.Lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}' AND clave_idempotencia = '{entrada.ClaveDeIdempotencia}'"));
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_GuardaLaHuellaDeLaCarga_ElClienteDelActorYLaExpiracion()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 3, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var entrada = ARecojo("lima", [Linea(libro)]);

        await pila.Reservar(cliente, entrada);

        Assert.Equal(cliente.UsuarioId, await bd.Consultar<Guid>($"SELECT cliente_id FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal(CargaCanonicaDeReservar.Huella(entrada), await bd.Consultar<string>($"SELECT huella_de_carga FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal(f.Lima, await bd.Consultar<Guid>($"SELECT sucursal_recojo_id FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    public async Task Reservar_SiUnaLineaNoTieneStock_DeshaceTodoLoQueHizoLaTransaccion()
    {
        var f = await TresFuentes();
        var hay = await bd.AgregarEdicion();
        var agotado = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, hay, 5, 0);
        using var pila = new PilaDeReservas(bd);

        var resultado = await pila.Reservar(Cliente(), ARecojo("lima", [Linea(hay, 1), Linea(agotado, 1)]));

        Assert.Equal("stock-insuficiente", resultado.Error!.Clave);
        Assert.Equal((5, 0), await bd.StockDe(f.Lima, hay));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas"));

        // Ni siquiera las filas de stock en cero que garantizó Bloquear: la transacción entera se deshizo.
        Assert.Null(await bd.StockDe(f.Cusco, agotado));
        Assert.Null(await bd.StockDe(f.Cusco, hay));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-12")]
    public async Task Reservar_ConUnaEdicionDigital_SeRechazaYNoSeCreaNada()
    {
        var f = await TresFuentes();
        var digital = await bd.AgregarEdicion("digital");
        using var pila = new PilaDeReservas(bd);

        var resultado = await pila.Reservar(Cliente(), ARecojo("lima", [Linea(digital)]));

        Assert.Equal("edicion-no-impresa", resultado.Error!.Clave);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Null(await bd.StockDe(f.Lima, digital));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Reservar_SinActorOConUnActorQueNoEsCliente_SeRechazaSinTocarNada()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 3, 0);
        using var pila = new PilaDeReservas(bd);

        var sinActor = await pila.Reservar(null, ARecojo("lima", [Linea(libro)]));
        var administrador = await pila.Reservar(ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.AdministradorCentral), ARecojo("lima", [Linea(libro)]));

        Assert.Equal(CategoriaDeError.NoAutenticado, sinActor.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, administrador.Error!.Categoria);
        Assert.Equal((3, 0), await bd.StockDe(f.Lima, libro));
        Assert.Equal(["sin-sesion", "fuera-de-rol"], pila.Denegaciones.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task Reservar_UnPedidoDeOtroCliente_EsNoEncontrado_YElDeSuDuenoLoReemplaza()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var duena = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(duena, ARecojo("lima", [Linea(libro)], pedido))).EsExito);

        var ajeno = await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)], pedido));
        var mismoCliente = await pila.Reservar(duena, ARecojo("lima", [Linea(libro)], pedido));

        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (ajeno.Error!.Categoria, ajeno.Error.Clave));
        Assert.True(mismoCliente.EsExito, mismoCliente.Error?.Clave);
        Assert.Equal(["recurso-ajeno"], pila.Denegaciones.Motivos);
        Assert.Equal((5, 1), await bd.StockDe(f.Lima, libro));
    }

    // --- el repositorio ---

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task UltimaDelPedido_RehidrataLaCabeceraYSusLineas()
    {
        var f = await TresFuentes();
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, a, 5, 0);
        await bd.PonerStock(f.Cusco, b, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var entrada = ADomicilio("cusco", [Linea(a, 2), Linea(b, 3)]);
        Assert.True((await pila.Reservar(cliente, entrada)).EsExito);

        var reserva = await pila.EnUnAmbito(cliente, async sp =>
            await sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IRepositorioDeReservas>().UltimaDelPedido(entrada.PedidoId, token)));

        Assert.NotNull(reserva);
        Assert.Equal((entrada.PedidoId, cliente.UsuarioId!.Value, TipoDeEntrega.Domicilio, "cusco", null), (reserva.PedidoId, reserva.ClienteId, reserva.TipoDeEntrega, reserva.RegionDeEntrega, reserva.SucursalDeRecojoId));
        Assert.Equal(pila.Reloj.Ahora.AddMinutes(15), reserva.FechaDeExpiracion);
        Assert.False(reserva.Extendida);
        Assert.Equal(0, reserva.Reemplazos);
        Assert.Equal(2, reserva.Lineas.Count);
        Assert.All(reserva.Lineas, l => Assert.Equal(EstadoDeLinea.Pendiente, l.Estado));
        Assert.Equal(new[] { 2, 3 }.Order(), reserva.Lineas.Select(l => l.Cantidad).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task UltimaDelPedido_SinReserva_EsNulo()
    {
        await TresFuentes();
        using var pila = new PilaDeReservas(bd);

        var reserva = await pila.EnUnAmbito(Cliente(), async sp =>
            await sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IRepositorioDeReservas>().UltimaDelPedido(Guid.NewGuid(), token)));

        Assert.Null(reserva);
    }

    // --- la expiración ---

    private async Task<(PilaDeReservas Pila, Escenario Fuentes, Guid Libro, ReservarEntrada Entrada, Guid ClienteId)> ReservaVencida(int stock = 5, int cantidad = 2)
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, stock, 0);
        var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var entrada = ARecojo("lima", [Linea(libro, cantidad)]);
        Assert.True((await pila.Reservar(cliente, entrada)).EsExito);

        // Pasan 16 minutos: la reserva venció pero nadie la barrió.
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);
        return (pila, f, libro, entrada, cliente.UsuarioId!.Value);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task OtraReserva_LiberaLasVencidasDeLaEdicionYAudita_ConActorSistemaYSinPerderElDelCliente()
    {
        var (pila, f, libro, entrada, _) = await ReservaVencida(stock: 2, cantidad: 2);
        using (pila)
        {
            var otro = Cliente();

            // Sin liberar la vencida no habría stock (2 reservados de 2): la segunda reserva solo cabe porque Bloquear expira la primera.
            var resultado = await pila.Reservar(otro, ARecojo("lima", [Linea(libro, 2)]));

            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            Assert.Equal((2, 2), await bd.StockDe(f.Lima, libro));
            Assert.Equal("expirada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
            Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));

            var auditada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaExpirada");
            Assert.Equal(TipoDeActor.Sistema, auditada.Actor);
            Assert.Equal(entrada.PedidoId.ToString("D"), auditada.ObjetoId);
            Assert.Equal(f.Lima, auditada.Fuente);
            Assert.Equal(libro.ToString("D"), auditada.Detalle["edicionId"]);
            Assert.Equal("2", auditada.Detalle["cantidad"]);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task LaExpiracionNoSeRepite_LaSegundaVezNoLiberaNiAudita()
    {
        var (pila, f, libro, _, _) = await ReservaVencida(stock: 5, cantidad: 2);
        using (pila)
        {
            Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 1)]))).EsExito);
            Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 1)]))).EsExito);

            Assert.Equal((5, 2), await bd.StockDe(f.Lima, libro));
            Assert.Equal(1, pila.Auditoria.Contar("ReservaExpirada"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedidoComoSistema_ExpiraSusVencidasYDevuelveLasUnidades()
    {
        var (pila, f, libro, entrada, _) = await ReservaVencida(stock: 5, cantidad: 3);
        using (pila)
        {
            var resultado = await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId));

            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            Assert.Equal(new ExpiracionDelPedido(entrada.PedidoId, true, 1), resultado.Valor);
            Assert.Equal((5, 0), await bd.StockDe(f.Lima, libro));
            Assert.Equal("expirada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
            Assert.Equal(TipoDeActor.Sistema, Assert.Single(pila.Auditoria.Registros).Actor);

            // Repetir no hace nada.
            var otra = await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId));
            Assert.Equal(new ExpiracionDelPedido(entrada.PedidoId, true, 0), otra.Valor);
            Assert.Single(pila.Auditoria.Registros);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedidoSinActor_EsNoAutenticadoYNoLibera_ElTrabajoSinActorNoPasaPorNadaQueExpirar()
    {
        var (pila, f, libro, entrada, _) = await ReservaVencida(stock: 5, cantidad: 3);
        using (pila)
        {
            var resultado = await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(null, new ExpirarReservasEntrada(entrada.PedidoId));

            Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
            Assert.Equal((5, 3), await bd.StockDe(f.Lima, libro));
            Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task ExpirarUnPedidoConUnCliente_EsProhibidoYNoLibera()
    {
        var (pila, f, libro, entrada, cliente) = await ReservaVencida(stock: 5, cantidad: 3);
        using (pila)
        {
            var resultado = await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(Cliente(cliente), new ExpirarReservasEntrada(entrada.PedidoId));

            Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
            Assert.Equal((5, 3), await bd.StockDe(f.Lima, libro));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-08")]
    public async Task SiLaAuditoriaDeLaExpiracionFalla_NadaSeLiberaYLaReservaQuedaIntacta()
    {
        var (pila, f, libro, entrada, _) = await ReservaVencida(stock: 5, cantidad: 3);
        using (pila)
        {
            pila.Auditoria.Falla = true;

            await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() =>
                pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(entrada.PedidoId)));

            Assert.Equal((5, 3), await bd.StockDe(f.Lima, libro));
            Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task ElRepositorio_ExpirarLineasVencidasDevuelveSoloLasQueCambiaron_YLaVigenteNoSeToca()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 10, 0);
        using var pila = new PilaDeReservas(bd);
        var vencido = Guid.NewGuid();
        var vigente = Guid.NewGuid();
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 2)], vencido))).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(10);
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 3)], vigente))).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(6);

        var (primera, segunda, delVigente) = await pila.EnUnAmbito(ActorActual.DelSistema(), sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            {
                var repo = sp.GetRequiredService<IRepositorioDeReservas>();
                var ahora = pila.Reloj.Ahora;
                var a = await repo.ExpirarLineasVencidas(vencido, null, ahora, token);
                var b = await repo.ExpirarLineasVencidas(vencido, null, ahora, token);
                var c = await repo.ExpirarLineasVencidas(vigente, null, ahora, token);
                return (a, b, c);
            }));

        // En el instante 16 la primera (iniciada en 0) venció; la segunda (iniciada en 10) vence recién en 25.
        Assert.Equal(2, Assert.Single(primera).Cantidad);
        Assert.Empty(segunda);
        Assert.Empty(delVigente);
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{vigente}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task VencidasPorEdicion_UsaLaHoraDelRelojQueSeLePasa_NoLaDeLaBase()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 10, 0);
        using var pila = new PilaDeReservas(bd);
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 4)]))).EsExito);

        async Task<IReadOnlyList<UnidadesVencidas>> Vencidas() => await pila.EnUnAmbito(Cliente(), sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IReservasDeStock>().VencidasPorEdicion([libro], token)));

        Assert.Empty(await Vencidas());

        // La base ve «hoy» (el reloj real); la prueba mueve solo el reloj de la aplicación 15 minutos exactos: en el instante exacto, ya venció.
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(15);

        Assert.Equal(new UnidadesVencidas(f.Lima, libro, 4), Assert.Single(await Vencidas()));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-03")]
    public async Task LaDisponibilidadDeFuentes_SumaLasVencidasQueNadieBarrio_ConLaImplementacionReal()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 10, 0);
        using var pila = new PilaDeReservas(bd);
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 4)]))).EsExito);

        async Task<int> Vendible() => (await pila.Ejecutar<ConsultarDisponibilidadEntrada, DisponibilidadConsultada>(ActorActual.DeIntegracion("e-commerce"), new ConsultarDisponibilidadEntrada([libro]))).Valor.Ediciones.Single().VendibleEnLaRed;

        Assert.Equal(6, await Vendible());

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(20);

        // Vencida y sin barrer: la consulta de solo lectura la devuelve al vendible (FUE-03) sin escribir.
        Assert.Equal(10, await Vendible());
        Assert.Equal((10, 4), await bd.StockDe(f.Lima, libro));
        Assert.Equal("pendiente", await bd.Consultar<string>("SELECT estado FROM reservas.reserva_lineas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task PedidosQueApartan_CuentaLasPendientesVigentesYLasConfirmadas_PeroNoLasVencidas()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 20, 0);
        using var pila = new PilaDeReservas(bd);
        var vencido = Guid.NewGuid();
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)], vencido))).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(10);
        var vigentes = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var pedido = Guid.NewGuid();
            vigentes.Add(pedido);
            Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)], pedido))).EsExito);
        }

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(6);

        var apartan = await pila.EnUnAmbito(Cliente(), sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IReservasDeStock>().PedidosQueApartan(f.Lima, libro, 2, token)));
        var todos = await pila.EnUnAmbito(Cliente(), sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IReservasDeStock>().PedidosQueApartan(f.Lima, libro, 10, token)));
        var ninguno = await pila.EnUnAmbito(Cliente(), sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IReservasDeStock>().PedidosQueApartan(f.Lima, libro, 0, token)));

        Assert.Equal(3, apartan.TotalDePedidos);
        Assert.Equal(2, apartan.PedidoIds.Count);
        Assert.Equal(vigentes.Order(), todos.PedidoIds);
        Assert.DoesNotContain(vencido, todos.PedidoIds);
        Assert.Empty(ninguno.PedidoIds);
        Assert.Equal(3, ninguno.TotalDePedidos);
    }
}

/// <summary>Las defensas de la revisión de INV-2a: la espera de bloqueos acotada, la unicidad como conflicto, y el acceso que falla cerrado sin actor.</summary>
public sealed class DefensasDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task UnaFilaDeStockRetenidaPorOtraTransaccion_NoDejaEsperandoParaSiempre_EsBloqueoOcupado()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd, servicios => servicios.AddSingleton(sp => new FabricaDeUnidad(sp).Crear()));

        // Una transacción colgada sostiene la fila (una auditoría lenta, una conexión perdida…).
        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var retenida = await conexion.BeginTransactionAsync();
        await using (var bloquear = new Npgsql.NpgsqlCommand($"SELECT 1 FROM fuentes.stock WHERE edicion_id = '{libro}' FOR UPDATE", conexion, retenida))
        {
            await bloquear.ExecuteNonQueryAsync();
        }

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var resultado = await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)])).WaitAsync(TimeSpan.FromSeconds(30));
        reloj.Stop();

        Assert.Equal((CategoriaDeError.NoDisponible, "bloqueo-ocupado"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(20), $"Esperó {reloj.Elapsed}: la espera debía acotarse a la lock_timeout de la transacción.");
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));

        await retenida.RollbackAsync();
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)]))).EsExito);
    }

    /// <summary>Una unidad de trabajo con una <c>lock_timeout</c> corta, para que la prueba no espere el valor de producción.</summary>
    private sealed class FabricaDeUnidad(IServiceProvider sp)
    {
        public UnidadDeTrabajoPostgres Crear() => new(
            sp.GetRequiredService<Npgsql.NpgsqlDataSource>(),
            new OpcionesDeUnidadDeTrabajo { EsperaMaximaDeBloqueos = TimeSpan.FromSeconds(1) },
            sp.GetRequiredService<IActorActual>());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task InsertarUnaClaveDeIdempotenciaRepetidaDelMismoCliente_LlegaComoReservaDuplicadaNoComoErrorDelServidor()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var entrada = ARecojo("lima", [Linea(libro)]);
        Assert.True((await pila.Reservar(cliente, entrada)).EsExito);

        await Assert.ThrowsAsync<ReservaDuplicadaException>(() => pila.EnUnAmbito(cliente, sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            {
                var otra = ReservaDePedido.Iniciar(
                    Guid.NewGuid(), Guid.NewGuid(), cliente.UsuarioId!.Value, TipoDeEntrega.Recojo, lima, null, pila.Reloj.Ahora,
                    [LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), libro, lima, 1, pila.Reloj.Ahora)]);
                await sp.GetRequiredService<IRepositorioDeReservas>().Insertar(otra, new DatosDeLaSolicitud(entrada.ClaveDeIdempotencia, CargaCanonicaDeReservar.Huella(entrada), null), token);
                return 0;
            })));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task ElAccesoAReservasSinActorFallaCerrado_NoPasaEnSilencio()
    {
        using var pila = new PilaDeReservas(bd);

        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => pila.EnUnAmbito(null, sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IRepositorioDeReservas>().UltimaDelPedido(Guid.NewGuid(), token))));
        await Assert.ThrowsAsync<ContextoDeActorAusenteException>(() => pila.EnUnAmbito(ActorActual.Anonimo(), sp =>
            sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IRepositorioDeReservas>().UltimaDelPedido(Guid.NewGuid(), token))));
    }
}
