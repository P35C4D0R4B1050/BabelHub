using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.Infrastructure.Carrito;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T25: el adaptador <c>RestInventarioDelCarrito</c> sobre <c>IClienteDelInventario</c> (CAR-05, CAR-06; AC-CAR-05): la disponibilidad por lotes de a lo más 50, la validación de la respuesta (SEG-13), la traducción de rechazos, límites y fallas sin
/// excepciones, la última lista de sucursales y (T25b) la consulta previa, reservar y liberar sobre el cliente real.
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

        public Func<ConsultaPrevia, ResultadoDelInventario<RespuestaDeConsultaPrevia>>? Previa { get; set; }

        public Func<SolicitudDeReservaDeStock, string, ResultadoDelInventario<ReservaDelInventario>>? Reserva { get; set; }

        public Func<Guid, string, ResultadoDelInventario<ReservaDelInventario>>? Liberacion { get; set; }

        public List<ConsultaPrevia> Previas { get; } = [];

        public List<(SolicitudDeReservaDeStock Solicitud, string Clave)> Reservas { get; } = [];

        public List<(Guid Pedido, string Motivo)> Liberaciones { get; } = [];

        public Task<ResultadoDelInventario<RespuestaDeConsultaPrevia>> ConsultarPrevia(ConsultaPrevia consulta, CancellationToken cancelacion = default)
        {
            Previas.Add(consulta);
            return Task.FromResult(Previa!(consulta));
        }

        public Task<ResultadoDelInventario<ReservaDelInventario>> Reservar(SolicitudDeReservaDeStock solicitud, string claveDeIdempotencia, CancellationToken cancelacion = default)
        {
            Reservas.Add((solicitud, claveDeIdempotencia));
            return Task.FromResult(Reserva!(solicitud, claveDeIdempotencia));
        }

        public Task<ResultadoDelInventario<ReservaDelInventario>> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid>? lineaIds = null, CancellationToken cancelacion = default)
        {
            Liberaciones.Add((pedidoId, motivo));
            return Task.FromResult(Liberacion!(pedidoId, motivo));
        }
        public Task<ResultadoDelInventario<ReservaDelInventario>> ConsultarReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ExtensionDeLaReserva>> ExtenderReserva(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<ReservaDelInventario>> ConfirmarReserva(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDelInventario<CierreDeLaReserva>> CerrarReserva(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, Guid? envioId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

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

    // ---------- T25b: consulta previa, reservar y liberar sobre el cliente real ----------

    private static readonly EntregaParaElInventario Recojo = new(TipoDeEntrega.Recojo, "sede-central", null);

    private static LineaParaElInventario Pedida(int cantidad = 2) => new(Guid.NewGuid(), Guid.NewGuid(), cantidad);

    private static ReservaDelInventario ReservaPendiente(Guid pedido, IReadOnlyList<LineaParaElInventario> lineas, DateTimeOffset? expira = null) =>
        new(pedido, "pendiente", Inicio, expira ?? Inicio.AddMinutes(15), [.. lineas.Select(l => new LineaReservada(l.LineaId, l.EdicionId, l.Cantidad, "pendiente", "sede-central", false, null))], []);

    [Fact]
    [Trait("Criterio", "AC-CAR-05")]
    public async Task La_consulta_previa_manda_el_tipo_la_sucursal_y_las_lineas_y_devuelve_la_fuente_y_el_plazo_de_cada_una()
    {
        var (adaptador, cliente, _) = Crear();
        var uno = Pedida();
        var dos = Pedida(1);
        cliente.Previa = c => ResultadoDelInventario<RespuestaDeConsultaPrevia>.ConValor(new RespuestaDeConsultaPrevia(
            [new SeleccionDeLineaDelInventario(uno.LineaId, uno.EdicionId, true, "sede-central", false, null, 9), new SeleccionDeLineaDelInventario(dos.LineaId, dos.EdicionId, true, "lima", true, 3, 4)], Inicio.AddSeconds(-2)));

        var respuesta = await adaptador.ConsultarPrevia(Recojo, [uno, dos]);

        Assert.True(respuesta.Exitosa);
        var enviada = Assert.Single(cliente.Previas);
        Assert.Equal((TipoDeEntregaDeLaReserva.Recojo, "sede-central", null), (enviada.Tipo, enviada.CodigoDeSucursalDeRecojo, enviada.RegionDeEntrega));
        Assert.Equal([uno.LineaId, dos.LineaId], enviada.Lineas.Select(l => l.LineaId));
        Assert.Equal((true, "lima", true, 3), (respuesta.Valor.Lineas[1].HayStock, respuesta.Valor.Lineas[1].CodigoDeFuente, respuesta.Valor.Lineas[1].RequiereTraslado, respuesta.Valor.Lineas[1].PlazoEstimadoDias));
        Assert.Equal(Inicio.AddSeconds(-2), respuesta.Valor.ConsultadaEn);
    }

    [Fact]
    public async Task Una_consulta_previa_con_una_linea_de_mas_o_de_menos_es_sin_respuesta_y_nunca_stock()
    {
        var (adaptador, cliente, _) = Crear();
        var uno = Pedida();
        var dos = Pedida();
        cliente.Previa = _ => ResultadoDelInventario<RespuestaDeConsultaPrevia>.ConValor(new RespuestaDeConsultaPrevia([new SeleccionDeLineaDelInventario(uno.LineaId, uno.EdicionId, true, "sede-central", false, null, 9)], Inicio));
        var faltante = await adaptador.ConsultarPrevia(Recojo, [uno, dos]);
        cliente.Previa = _ => ResultadoDelInventario<RespuestaDeConsultaPrevia>.ConValor(new RespuestaDeConsultaPrevia([new SeleccionDeLineaDelInventario(Guid.NewGuid(), uno.EdicionId, true, "sede-central", false, null, 9)], Inicio));
        var ajena = await adaptador.ConsultarPrevia(Recojo, [uno]);

        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, faltante.Estado);
        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, ajena.Estado);
    }

    [Fact]
    public async Task La_consulta_previa_traduce_el_rechazo_el_limite_y_la_caida()
    {
        var (adaptador, cliente, _) = Crear();
        var linea = Pedida();
        var desde = Inicio.AddSeconds(30);

        cliente.Previa = _ => ResultadoDelInventario<RespuestaDeConsultaPrevia>.Rechazo(400, "INV-VALIDACION-INVALIDA");
        var rechazada = await adaptador.ConsultarPrevia(Recojo, [linea]);
        cliente.Previa = _ => ResultadoDelInventario<RespuestaDeConsultaPrevia>.Limitada("INV-LIMITE-EXCEDIDO") with { Limite = new RechazoPorLimiteDelInventario("x", 30, desde) };
        var limitada = await adaptador.ConsultarPrevia(Recojo, [linea]);
        cliente.Previa = _ => throw new HttpRequestException("caído");
        var caida = await adaptador.ConsultarPrevia(Recojo, [linea]);

        Assert.Equal((EstadoDeRespuestaDelInventarioDelCarrito.Rechazada, "INV-VALIDACION-INVALIDA"), (rechazada.Estado, rechazada.Codigo));
        Assert.Equal((EstadoDeRespuestaDelInventarioDelCarrito.LimiteExcedido, desde), (limitada.Estado, limitada.DisponibleDesde));
        Assert.Equal(EstadoDeRespuestaDelInventarioDelCarrito.NoDisponible, caida.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-10")]
    public async Task Reservar_manda_la_clave_derivada_del_pedido_y_la_generacion_y_devuelve_la_reserva_con_su_expiracion()
    {
        var (adaptador, cliente, _) = Crear();
        var pedido = Guid.NewGuid();
        var lineas = new[] { Pedida(), Pedida(1) };
        cliente.Reserva = (s, _) => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaPendiente(s.PedidoId, lineas));

        var resultado = await adaptador.Reservar(pedido, 3, Recojo, lineas);

        Assert.Equal(TipoDeResultadoDeReservar.Reservada, resultado.Tipo);
        var (solicitud, clave) = Assert.Single(cliente.Reservas);
        Assert.Equal(ClaveDeReserva.Para(pedido, 3), clave);
        Assert.Equal((pedido, TipoDeEntregaDeLaReserva.Recojo, "sede-central"), (solicitud.PedidoId, solicitud.Tipo, solicitud.CodigoDeSucursalDeRecojo));
        Assert.Equal(Inicio.AddMinutes(15), resultado.Reserva!.ExpiraEn);
        Assert.Equal(lineas.Select(l => l.LineaId), resultado.Reserva.Lineas.Select(l => l.LineaId));
    }

    [Fact]
    public async Task Una_reserva_con_otro_pedido_otras_lineas_o_sin_expiracion_es_sin_respuesta_y_nunca_una_reserva()
    {
        var (adaptador, cliente, _) = Crear();
        var pedido = Guid.NewGuid();
        var lineas = new[] { Pedida() };

        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaPendiente(Guid.NewGuid(), lineas));
        var otroPedido = await adaptador.Reservar(pedido, 1, Recojo, lineas);
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaPendiente(pedido, [Pedida()]));
        var otrasLineas = await adaptador.Reservar(pedido, 1, Recojo, lineas);
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaPendiente(pedido, lineas) with { ExpiraEn = null });
        var sinExpiracion = await adaptador.Reservar(pedido, 1, Recojo, lineas);

        Assert.All(new[] { otroPedido, otrasLineas, sinExpiracion }, r => Assert.Equal(TipoDeResultadoDeReservar.NoDisponible, r.Tipo));
    }

    [Theory]
    [InlineData("INV-IDEMPOTENCIA-EN-CURSO", TipoDeResultadoDeReservar.EnCurso)]
    [InlineData("INV-IDEMPOTENCIA-EN-CONFLICTO", TipoDeResultadoDeReservar.ConflictoDeIdempotencia)]
    [InlineData("INV-RESERVA-ESTADO-INVALIDO", TipoDeResultadoDeReservar.EstadoInvalido)]
    [InlineData("INV-VALIDACION-INVALIDA", TipoDeResultadoDeReservar.Rechazada)]
    [InlineData("INV-PROHIBIDO", TipoDeResultadoDeReservar.Rechazada)]
    public async Task Reservar_traduce_cada_rechazo_del_contrato_a_su_tipo(string codigo, TipoDeResultadoDeReservar esperado)
    {
        var (adaptador, cliente, _) = Crear();
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Rechazo(409, codigo);

        var resultado = await adaptador.Reservar(Guid.NewGuid(), 1, Recojo, [Pedida()]);

        Assert.Equal((esperado, codigo), (resultado.Tipo, resultado.Codigo));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task Sin_stock_trae_las_lineas_que_no_alcanzan_y_sin_ellas_es_un_rechazo_y_no_un_aviso_de_stock()
    {
        var (adaptador, cliente, _) = Crear();
        var linea = Pedida(5);
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Rechazo(409, "INV-STOCK-INSUFICIENTE") with { LineasSinStock = [new LineaSinStockDelInventario(linea.LineaId, linea.EdicionId, 5, 2)] };
        var con = await adaptador.Reservar(Guid.NewGuid(), 1, Recojo, [linea]);
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Rechazo(409, "INV-STOCK-INSUFICIENTE");
        var sin = await adaptador.Reservar(Guid.NewGuid(), 1, Recojo, [linea]);

        Assert.Equal(TipoDeResultadoDeReservar.StockInsuficiente, con.Tipo);
        Assert.Equal((linea.LineaId, 5, 2), (con.SinStock![0].LineaId, con.SinStock[0].Solicitada, con.SinStock[0].DisponibleEnLaRed));
        Assert.Equal(TipoDeResultadoDeReservar.Rechazada, sin.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task Reservar_con_un_limite_trae_el_instante_de_reintento_y_con_una_caida_es_no_disponible()
    {
        var (adaptador, cliente, _) = Crear();
        var desde = Inicio.AddMinutes(7);
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Limitada("INV-LIMITE-EXCEDIDO") with { Limite = new RechazoPorLimiteDelInventario("reemplazos", 420, desde) };
        var limitada = await adaptador.Reservar(Guid.NewGuid(), 1, Recojo, [Pedida()]);
        cliente.Reserva = (_, _) => throw new TimeoutException();
        var caida = await adaptador.Reservar(Guid.NewGuid(), 1, Recojo, [Pedida()]);
        cliente.Reserva = (_, _) => ResultadoDelInventario<ReservaDelInventario>.SinRespuesta;
        var sinRespuesta = await adaptador.Reservar(Guid.NewGuid(), 1, Recojo, [Pedida()]);

        Assert.Equal((TipoDeResultadoDeReservar.LimiteExcedido, desde), (limitada.Tipo, limitada.DisponibleDesde));
        Assert.Equal(TipoDeResultadoDeReservar.NoDisponible, caida.Tipo);
        Assert.Equal(TipoDeResultadoDeReservar.NoDisponible, sinRespuesta.Tipo);
    }

    [Fact]
    public async Task Una_entrega_a_domicilio_manda_la_region_y_no_la_sucursal()
    {
        var (adaptador, cliente, _) = Crear();
        var lineas = new[] { Pedida() };
        cliente.Reserva = (s, _) => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaPendiente(s.PedidoId, lineas));

        await adaptador.Reservar(Guid.NewGuid(), 1, new EntregaParaElInventario(TipoDeEntrega.Domicilio, null, "ayacucho"), lineas);

        var (solicitud, _) = Assert.Single(cliente.Reservas);
        Assert.Equal((TipoDeEntregaDeLaReserva.Domicilio, null, "ayacucho"), (solicitud.Tipo, solicitud.CodigoDeSucursalDeRecojo, solicitud.RegionDeEntrega));
    }

    [Fact]
    public async Task Liberar_manda_el_motivo_y_es_idempotente_y_cada_resultado_se_traduce()
    {
        var (adaptador, cliente, _) = Crear();
        var pedido = Guid.NewGuid();
        var lineas = new[] { Pedida() };

        cliente.Liberacion = (p, _) => ResultadoDelInventario<ReservaDelInventario>.ConValor(ReservaPendiente(p, lineas) with { Estado = "liberada" });
        var liberada = await adaptador.Liberar(pedido, "checkout-cancelado");
        cliente.Liberacion = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Rechazo(404, "INV-NO-ENCONTRADO");
        var noExistia = await adaptador.Liberar(pedido, "checkout-reiniciado");
        cliente.Liberacion = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Rechazo(409, "INV-RESERVA-ESTADO-INVALIDO");
        var confirmado = await adaptador.Liberar(pedido, "checkout-cancelado");

        Assert.Equal([(pedido, "checkout-cancelado"), (pedido, "checkout-reiniciado"), (pedido, "checkout-cancelado")], cliente.Liberaciones);
        Assert.Equal(ResultadoDeLiberar.Liberada, liberada);
        Assert.Equal(ResultadoDeLiberar.NoExistia, noExistia);
        Assert.Equal(ResultadoDeLiberar.PedidoConfirmado, confirmado);
    }

    [Fact]
    public async Task Liberar_sin_respuesta_con_limite_con_otro_rechazo_o_con_una_excepcion_es_no_se_pudo()
    {
        var (adaptador, cliente, _) = Crear();
        var pedido = Guid.NewGuid();

        cliente.Liberacion = (_, _) => ResultadoDelInventario<ReservaDelInventario>.SinRespuesta;
        var sinRespuesta = await adaptador.Liberar(pedido, "checkout-cancelado");
        cliente.Liberacion = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Limitada("INV-LIMITE-EXCEDIDO");
        var limitada = await adaptador.Liberar(pedido, "checkout-cancelado");
        cliente.Liberacion = (_, _) => ResultadoDelInventario<ReservaDelInventario>.Rechazo(403, "INV-PROHIBIDO");
        var prohibida = await adaptador.Liberar(pedido, "checkout-cancelado");
        cliente.Liberacion = (_, _) => throw new HttpRequestException("caído");
        var excepcion = await adaptador.Liberar(pedido, "checkout-cancelado");

        Assert.All(new[] { sinRespuesta, limitada, prohibida, excepcion }, r => Assert.Equal(ResultadoDeLiberar.NoSePudo, r));
    }
}
