using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.Infrastructure.Carrito;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T25: el adaptador <c>RestInventarioDelCarrito</c> sobre <c>IClienteDelInventario</c> (CAR-05, CAR-06; AC-CAR-05): la disponibilidad por lotes de a lo más 50, la validación de la respuesta (SEG-13), la traducción de rechazos, límites y fallas sin
/// excepciones, la última lista de sucursales y que la consulta previa, reservar y liberar fallen cerrado mientras no existan en el cliente de <c>Plataforma</c>.
/// </summary>
public sealed class RestInventarioDelCarritoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojMovil : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = Inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ClienteFalso : IClienteDelInventario
    {
        public List<int> Lotes { get; } = [];

        public Func<IReadOnlyList<Guid>, ResultadoDelInventario<DisponibilidadDeVariasEdiciones>>? Disponibilidad { get; set; }

        public Func<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>>? Sucursales { get; set; }

        public Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default)
        {
            Lotes.Add(edicionIds.Count);
            return Task.FromResult(Disponibilidad!(edicionIds));
        }

public Task<ResultadoDelInventario<RespuestaDeConsultaPrevia>> ConsultarPrevia(ConsultaPrevia consulta, CancellationToken cancelacion = default) => throw new NotSupportedException();        public Task<ResultadoDelInventario<ReservaDelInventario>> Reservar(SolicitudDeReservaDeStock solicitud, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();        public Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default) => throw new NotSupportedException();
        public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) => Task.FromResult(Sucursales!());

        public Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<PendientesDeTitular>> ConsultarPendientes(Guid titularId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario> DarDeBajaAlTitular(Guid titularId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private static ResultadoDelInventario<DisponibilidadDeVariasEdiciones> TodasConStock(IReadOnlyList<Guid> pedidas, int vendible = 7) =>
        ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(
            new DisponibilidadDeVariasEdiciones([.. pedidas.Select(id => new DisponibilidadDeLaEdicion(id, vendible, [], Inicio.AddSeconds(-3)))], []));

    private static (RestInventarioDelCarrito Adaptador, ClienteFalso Cliente, RelojMovil Reloj) Crear()
    {
        var cliente = new ClienteFalso { Disponibilidad = pedidas => TodasConStock(pedidas) };
        var reloj = new RelojMovil();
        return (new RestInventarioDelCarrito(cliente, reloj), cliente, reloj);
    }

    [Fact]
    public async Task La_disponibilidad_se_pide_por_lotes_de_a_lo_mas_50_y_se_junta()
    {
        var (adaptador, cliente, _) = Crear();
        var ids = Enumerable.Range(0, 120).Select(_ => Guid.NewGuid()).ToList();

        var respuesta = await adaptador.ConsultarDisponibilidad(ids);

        Assert.True(respuesta.Exitosa);
        Assert.Equal([50, 50, 20], cliente.Lotes);
        Assert.Equal(120, respuesta.Valor.Ediciones.Count);
        Assert.All(respuesta.Valor.Ediciones, e => Assert.Equal(7, e.VendibleEnLaRed));
        Assert.Equal(Inicio.AddSeconds(-3), respuesta.Valor.ConsultadaEn);
    }

    [Fact]
    public async Task Una_edicion_repetida_en_la_peticion_se_consulta_una_sola_vez_y_sin_ediciones_no_se_llama()
    {
        var (adaptador, cliente, _) = Crear();
        var id = Guid.NewGuid();

        var repetida = await adaptador.ConsultarDisponibilidad([id, id, id]);
        var vacia = await adaptador.ConsultarDisponibilidad([]);

        Assert.Equal([1], cliente.Lotes);
        Assert.Single(repetida.Valor.Ediciones);
        Assert.Empty(vacia.Valor.Ediciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-05")]
    public async Task Las_ediciones_no_registradas_se_devuelven_aparte()
    {
        var (adaptador, cliente, _) = Crear();
        var (conocida, desconocida) = (Guid.NewGuid(), Guid.NewGuid());
        cliente.Disponibilidad = _ => ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(
            new DisponibilidadDeVariasEdiciones([new DisponibilidadDeLaEdicion(conocida, 2, [], Inicio)], [desconocida]));

        var respuesta = await adaptador.ConsultarDisponibilidad([conocida, desconocida]);

        Assert.Equal(desconocida, Assert.Single(respuesta.Valor.NoRegistradas));
        Assert.Equal(conocida, Assert.Single(respuesta.Valor.Ediciones).EdicionId);
    }

    [Fact]
    public async Task Una_respuesta_que_el_contrato_no_admite_es_sin_respuesta_y_nunca_stock()
    {
        var (adaptador, cliente, _) = Crear();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var casos = new Func<IReadOnlyList<Guid>, DisponibilidadDeVariasEdiciones>[]
        {
            _ => new DisponibilidadDeVariasEdiciones([new DisponibilidadDeLaEdicion(Guid.NewGuid(), 1, [], Inicio)], []),
            _ => new DisponibilidadDeVariasEdiciones([new DisponibilidadDeLaEdicion(a, 1, [], Inicio), new DisponibilidadDeLaEdicion(a, 1, [], Inicio)], [b]),
            _ => new DisponibilidadDeVariasEdiciones([new DisponibilidadDeLaEdicion(a, -1, [], Inicio)], [b]),
            _ => new DisponibilidadDeVariasEdiciones([new DisponibilidadDeLaEdicion(a, 1, [], Inicio)], []),
            _ => new DisponibilidadDeVariasEdiciones([new DisponibilidadDeLaEdicion(a, 1, [], Inicio)], [a, b]),
        };

        foreach (var caso in casos)
        {
            cliente.Disponibilidad = pedidas => ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(caso(pedidas));
            var respuesta = await adaptador.ConsultarDisponibilidad([a, b]);
            Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, respuesta.Estado);
        }
    }

    [Fact]
    public async Task Rechazos_limites_y_fallas_se_traducen_sin_excepcion_y_una_falla_en_un_lote_falla_toda_la_consulta()
    {
        var (adaptador, cliente, _) = Crear();
        var ids = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToList();

        cliente.Disponibilidad = _ => ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.Rechazo(422, "INV-VALIDACION");
        var rechazo = await adaptador.ConsultarDisponibilidad(ids);
        cliente.Disponibilidad = _ => ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.Limitada("INV-LIMITE-EXCEDIDO");
        var limite = await adaptador.ConsultarDisponibilidad(ids);
        var llamada = 0;
        cliente.Disponibilidad = pedidas => ++llamada == 2 ? ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.SinRespuesta : TodasConStock(pedidas);
        var caido = await adaptador.ConsultarDisponibilidad(ids);
        cliente.Disponibilidad = _ => throw new InvalidOperationException("no debería salir");
        var lanza = await adaptador.ConsultarDisponibilidad(ids);

        Assert.Equal((EstadoDeRespuestaDelInventarioDelCarrito.Rechazada, "INV-VALIDACION"), (rechazo.Estado, rechazo.Codigo));
        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.LimiteExcedido, limite.Estado);
        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, caido.Estado);
        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, lanza.Estado);
    }

    [Fact]
    public async Task Las_sucursales_se_devuelven_con_su_frescura_y_si_el_Inventario_falla_se_usa_la_ultima_lista_hasta_10_minutos()
    {
        var (adaptador, cliente, reloj) = Crear();
        cliente.Sucursales = () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.ConValor([new SucursalPublica("sede-central", "Sede central", "ayacucho")]);
        var fresca = await adaptador.ListarSucursales();
        cliente.Sucursales = () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.SinRespuesta;
        reloj.Ahora = Inicio.AddMinutes(10);
        var deRespaldo = await adaptador.ListarSucursales();
        reloj.Ahora = Inicio.AddMinutes(10).AddSeconds(1);
        var vencida = await adaptador.ListarSucursales();

        Assert.False(fresca!.EsDeRespaldo);
        Assert.Equal(Inicio, fresca.ObtenidaEn);
        Assert.True(deRespaldo!.EsDeRespaldo);
        Assert.Equal((Inicio, "sede-central"), (deRespaldo.ObtenidaEn, deRespaldo.Sucursales.Single().Codigo));
        Assert.Null(vencida);
    }

    [Fact]
    public async Task Sin_lista_previa_ni_respuesta_no_hay_sucursales_y_una_lista_invalida_no_se_guarda()
    {
        var (adaptador, cliente, _) = Crear();
        cliente.Sucursales = () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.SinRespuesta;
        var sinNada = await adaptador.ListarSucursales();
        cliente.Sucursales = () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.ConValor([new SucursalPublica("", "Sin código", "ayacucho")]);
        var invalida = await adaptador.ListarSucursales();

        Assert.Null(sinNada);
        Assert.Null(invalida);
    }

    [Fact]
    public async Task La_consulta_previa_reservar_y_liberar_fallan_cerrado_hasta_que_existan_en_el_cliente_de_Plataforma()
    {
        var (adaptador, _, _) = Crear();
        var entrega = new EntregaParaElInventario(TipoDeEntrega.Recojo, "sede-central", null);
        var lineas = new[] { new LineaParaElInventario(Guid.NewGuid(), Guid.NewGuid(), 1) };

        var previa = await adaptador.ConsultarPrevia(entrega, lineas);
        var reserva = await adaptador.Reservar(Guid.NewGuid(), 1, entrega, lineas);
        var liberada = await adaptador.Liberar(Guid.NewGuid(), "checkout-cancelado");

        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, previa.Estado);
        Assert.Equal(TipoDeResultadoDeReservar.NoDisponible, reserva.Tipo);
        Assert.Equal(ResultadoDeLiberar.NoSePudo, liberada);
    }
}
