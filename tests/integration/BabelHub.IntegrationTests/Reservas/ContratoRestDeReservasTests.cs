using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T33. Un E-commerce simulado que solo conoce el contrato de ECO-0 (<c>OperacionesDelInventario</c> y los DTOs de <c>BabelHub.Shared</c>) llama al Inventario real (Api en proceso, PostgreSQL real) y comprueba que cada
/// operación de reservas responde con el código de éxito, el DTO exacto (sin miembros de más ni de menos) y, al fallar, con un código de los que la operación declara. Además, de punta a punta, las unidades vendidas de un
/// pedido de recojo con la línea en otra sucursal cuentan una sola vez, en la sucursal de recojo, en cada fase del traslado simulado (confirmado, aprobado, en tránsito, recibido y entregado).
/// </summary>
public sealed class ContratoRestDeReservasTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    // El contrato se lee en los dos sentidos: ni un miembro de más (el Inventario no agrega campos fuera del DTO) ni uno de menos.
    private static readonly JsonSerializerOptions Estricto = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    private ApiDeReservasHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeReservasHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    /// <summary>La respuesta de una operación: el éxito con su DTO o el problema con su código.</summary>
    private sealed record Contestacion<T>(HttpStatusCode Estado, T? Valor, ProblemaDto? Problema, HttpResponseMessage Http)
    {
        public bool EsExito => Problema is null;
    }

    /// <summary>El E-commerce: arma cada llamada solo con lo que dice la operación del contrato (ruta, método, credencial por alcance, token de usuario y clave de idempotencia).</summary>
    private sealed class EcommerceSimulado(ApiDeReservasHost api)
    {
        public HashSet<string> Ejercitadas { get; } = [];

        public async Task<Contestacion<TRespuesta>> Llamar<TSolicitud, TRespuesta>(
            string nombre,
            Guid? pedido = null,
            TSolicitud? solicitud = default,
            string? rol = null,
            Guid? usuario = null,
            Guid? sucursal = null,
            string? clave = null)
        {
            var operacion = OperacionesDelInventario.Todas.Single(o => o.Nombre == nombre);
            Ejercitadas.Add(nombre);
            if (operacion.Solicitud is not null)
            {
                Assert.Equal(typeof(TSolicitud), operacion.Solicitud);
            }

            if (operacion.Respuesta is not null)
            {
                Assert.Equal(typeof(TRespuesta), operacion.Respuesta);
            }

            if (operacion.RequiereClaveDeIdempotencia)
            {
                Assert.NotNull(clave);
            }

            var credencial = operacion.Alcance switch
            {
                AlcancesDeCredencial.OperacionDeUsuario => ApiDeReservasHost.CredencialDeUsuario,
                AlcancesDeCredencial.LecturaDeDisponibilidad => ApiDeReservasHost.CredencialDeDisponibilidad,
                AlcancesDeCredencial.ExpiracionDeReservas => ApiDeReservasHost.CredencialDeExpiracion,
                _ => throw new InvalidOperationException("Alcance no previsto en esta prueba."),
            };
            var ruta = operacion.Ruta[RutasDeServicio.Inventario.Length..].Replace("{pedidoId}", pedido?.ToString(), StringComparison.Ordinal);
            var json = solicitud is null ? null : JsonSerializer.Serialize(solicitud, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var http = await api.Enviar(new HttpMethod(operacion.Metodo), ruta, credencial, operacion.RequiereTokenDeUsuario ? rol : null, sucursal, usuario, json, clave);
            var texto = await http.Content.ReadAsStringAsync();

            if ((int)http.StatusCode == operacion.CodigoDeExito)
            {
                Assert.Equal("application/json", http.Content.Headers.ContentType?.MediaType);
                return new Contestacion<TRespuesta>(http.StatusCode, JsonSerializer.Deserialize<TRespuesta>(texto, Estricto), null, http);
            }

            Assert.Equal("application/problem+json", http.Content.Headers.ContentType?.MediaType);
            ProblemaDto problema = http.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => JsonSerializer.Deserialize<RespuestaDeLimiteDto>(texto, Estricto)!,
                HttpStatusCode.Conflict when texto.Contains(CodigosDeErrorDelInventario.StockInsuficiente, StringComparison.Ordinal) => JsonSerializer.Deserialize<ProblemaDeStockDto>(texto, Estricto)!,
                _ => JsonSerializer.Deserialize<ProblemaDto>(texto, Estricto)!,
            };

            // Lo que el E-commerce puede esperar de un fallo: el estado coincide, hay traza y el código es uno de los que la operación declara.
            Assert.Equal((int)http.StatusCode, problema.Status);
            Assert.NotEmpty(problema.TraceId);
            Assert.Contains(problema.Code, operacion.Errores);
            return new Contestacion<TRespuesta>(http.StatusCode, default, problema, http);
        }
    }

    private static string Clave() => Guid.NewGuid().ToString("N");

    private async Task<(Guid Lima, Guid Cusco, Guid Libro, Guid EnLima)> Escenario()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        var enLima = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 5, 0);
        await bd.PonerStock(lima, enLima, 20, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        return (lima, cusco, libro, enLima);
    }

    private static LineaDeSolicitudDto LineaDto(Guid edicion, int cantidad = 1) => new(Guid.NewGuid(), edicion, cantidad);

    private static CrearReservaDto ReservaARecojo(Guid pedido, params LineaDeSolicitudDto[] lineas) => new(pedido, TiposDeEntrega.Recojo, "lima", null, lineas);

    // ---------- el contrato REST ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ElCheckoutCompleto_ConLosDtosDeEco0_CadaOperacionDeReservasRespondeLoQueElContratoDice()
    {
        var (lima, _, libro, enLima) = await Escenario();
        var digital = await bd.AgregarEdicion("digital");
        var ecommerce = new EcommerceSimulado(_api);
        var cliente = Guid.NewGuid();

        // Consulta previa (sin token): la fuente que se elegiría, con traslado y plazo, la falta de stock y la línea digital ignorada; no reserva nada.
        var sinStock = LineaDto(await bd.AgregarEdicion());
        var previa = await ecommerce.Llamar<ConsultaPreviaDto, ConsultaPreviaRespuestaDto>(
            "inventario.reservas.consulta-previa", solicitud: new ConsultaPreviaDto(TiposDeEntrega.Recojo, "lima", null, [LineaDto(libro), sinStock, LineaDto(digital)]));
        Assert.True(previa.EsExito);
        Assert.Equal(2, previa.Valor!.Lineas.Count);
        var elegida = previa.Valor.Lineas.Single(l => l.EdicionId == libro);
        Assert.Equal((ResultadosDeSeleccion.Disponible, "cusco", true, 2), (elegida.Resultado, elegida.CodigoDeFuente, elegida.RequiereTraslado, elegida.PlazoEstimadoDias));
        Assert.Equal(ResultadosDeSeleccion.SinStock, previa.Valor.Lineas.Single(l => l.LineaId == sinStock.LineaId).Resultado);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));

        // Reservar con la línea en Cusco para recojo en Lima: pendiente, con expiración a los 15 minutos y el traslado por venir.
        var pedido = Guid.NewGuid();
        var linea = LineaDto(libro, 2);
        var creada = await ecommerce.Llamar<CrearReservaDto, ReservaDto>("inventario.reservas.crear", solicitud: ReservaARecojo(pedido, linea), rol: RolesDeUsuario.Cliente, usuario: cliente, clave: Clave());
        Assert.True(creada.EsExito, creada.Problema?.Code);
        Assert.Equal(HttpStatusCode.Created, creada.Estado);
        var reserva = creada.Valor!;
        Assert.Equal((pedido, EstadosDeReserva.Pendiente), (reserva.PedidoId, reserva.Estado));
        Assert.Equal(reserva.CreadaEn.AddMinutes(15), reserva.ExpiraEn);
        var enReserva = Assert.Single(reserva.Lineas);
        Assert.Equal((linea.LineaId, libro, 2, EstadosDeReserva.Pendiente, "cusco", true, 2), (enReserva.LineaId, enReserva.EdicionId, enReserva.Cantidad, enReserva.Estado, enReserva.CodigoDeFuente, enReserva.RequiereTraslado, enReserva.PlazoEstimadoDias));
        Assert.Empty(reserva.Traslados);

        // Consultar (el cliente dueño) y extender por el pago pendiente: una sola vez, a los 30 minutos desde el inicio.
        var consultada = await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.consultar", pedido, rol: RolesDeUsuario.Cliente, usuario: cliente);
        Assert.Equal(reserva.Lineas, consultada.Valor!.Lineas);
        var extendida = await ecommerce.Llamar<object, ExtensionDeReservaDto>("inventario.reservas.extender", pedido);
        var otraVez = await ecommerce.Llamar<object, ExtensionDeReservaDto>("inventario.reservas.extender", pedido);
        Assert.True(extendida.Valor!.Extendida);
        Assert.Equal(reserva.CreadaEn.AddMinutes(30), extendida.Valor.Reserva.ExpiraEn);
        Assert.False(otraVez.Valor!.Extendida);

        // Confirmar el pago: la reserva queda confirmada y trae el traslado vinculado; repetir con otra clave devuelve lo mismo.
        var confirmada = await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.confirmar", pedido, clave: Clave());
        var repetida = await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.confirmar", pedido, clave: Clave());
        Assert.True(confirmada.EsExito, confirmada.Problema?.Code);
        Assert.Equal((EstadosDeReserva.Confirmada, (DateTimeOffset?)null), (confirmada.Valor!.Estado, confirmada.Valor.ExpiraEn));
        var traslado = Assert.Single(confirmada.Valor.Traslados);
        Assert.Equal(("cusco", "lima", 2, libro), (traslado.CodigoDeOrigen, traslado.CodigoDeDestino, traslado.Cantidad, traslado.EdicionId));
        Assert.Equal(confirmada.Valor.Traslados, repetida.Valor!.Traslados);

        // Un pedido de recojo con el stock en Lima: se entrega (cerrar), repetir es «ya-cerrada» y el físico baja una sola vez.
        var pedidoEnLima = Guid.NewGuid();
        var lineaEnLima = LineaDto(enLima, 3);
        Assert.True((await ecommerce.Llamar<CrearReservaDto, ReservaDto>("inventario.reservas.crear", solicitud: ReservaARecojo(pedidoEnLima, lineaEnLima), rol: RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), clave: Clave())).EsExito);
        Assert.True((await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.confirmar", pedidoEnLima, clave: Clave())).EsExito);
        var entrega = new CerrarReservaDto(MotivosDeCierre.Entrega, [lineaEnLima.LineaId], null);
        var cerrada = await ecommerce.Llamar<CerrarReservaDto, CierreDeReservaDto>("inventario.reservas.cerrar", pedidoEnLima, entrega, RolesDeUsuario.EncargadoDeSucursal, Guid.NewGuid(), lima, Clave());
        var yaCerrada = await ecommerce.Llamar<CerrarReservaDto, CierreDeReservaDto>("inventario.reservas.cerrar", pedidoEnLima, entrega, RolesDeUsuario.EncargadoDeSucursal, Guid.NewGuid(), lima, Clave());
        Assert.Equal([new ResultadoDeCierreDto(lineaEnLima.LineaId, ResultadosDeCierre.Cerrada)], cerrada.Valor!.Lineas);
        Assert.Equal([new ResultadoDeCierreDto(lineaEnLima.LineaId, ResultadosDeCierre.YaCerrada)], yaCerrada.Valor!.Lineas);
        Assert.Equal((17, 0), await bd.StockDe(lima, enLima));

        // Cancelar el checkout de otra reserva pendiente: queda liberada y el vendible vuelve; repetirlo no cambia nada. Anular el pedido confirmado con traslado la libera también.
        var pedidoPendiente = Guid.NewGuid();
        Assert.True((await ecommerce.Llamar<CrearReservaDto, ReservaDto>("inventario.reservas.crear", solicitud: ReservaARecojo(pedidoPendiente, LineaDto(enLima, 4)), rol: RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), clave: Clave())).EsExito);
        var cancelada = await ecommerce.Llamar<LiberarReservaDto, ReservaDto>("inventario.reservas.liberar", pedidoPendiente, new LiberarReservaDto(MotivosDeLiberacion.CheckoutCancelado, null));
        var canceladaOtraVez = await ecommerce.Llamar<LiberarReservaDto, ReservaDto>("inventario.reservas.liberar", pedidoPendiente, new LiberarReservaDto(MotivosDeLiberacion.CheckoutCancelado, null));
        Assert.Equal(EstadosDeReserva.Liberada, cancelada.Valor!.Estado);
        Assert.Equal(EstadosDeReserva.Liberada, canceladaOtraVez.Valor!.Estado);
        Assert.Equal((17, 0), await bd.StockDe(lima, enLima));
        var anulada = await ecommerce.Llamar<LiberarReservaDto, ReservaDto>("inventario.reservas.liberar", pedido, new LiberarReservaDto(MotivosDeLiberacion.PedidoAnulado, null));
        Assert.Equal(EstadosDeReserva.Liberada, anulada.Valor!.Estado);

        // Todas las operaciones de reservas del contrato quedaron ejercitadas.
        var delContrato = OperacionesDelInventario.Todas.Where(o => o.Nombre.StartsWith("inventario.reservas.", StringComparison.Ordinal)).Select(o => o.Nombre).ToHashSet();
        Assert.Equal(delContrato.Order(StringComparer.Ordinal), ecommerce.Ejercitadas.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task LosFallos_RespondenConUnCodigoDeLosQueLaOperacionDeclara_ConElDetalleQueElContratoPromete()
    {
        var (_, _, libro, enLima) = await Escenario();
        var digital = await bd.AgregarEdicion("digital");
        var ecommerce = new EcommerceSimulado(_api);
        var cliente = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var lineaDelPedido = LineaDto(enLima, 2);
        var clave = Clave();
        Task<Contestacion<ReservaDto>> Crear(Guid usuario, CrearReservaDto cuerpo, string? k = null) =>
            ecommerce.Llamar<CrearReservaDto, ReservaDto>("inventario.reservas.crear", solicitud: cuerpo, rol: RolesDeUsuario.Cliente, usuario: usuario, clave: k ?? Clave());

        // Falta de stock: 409 con las líneas sin stock y lo que hay en la red.
        var sinStock = await Crear(cliente, ReservaARecojo(Guid.NewGuid(), LineaDto(libro, 50)));
        Assert.Equal(HttpStatusCode.Conflict, sinStock.Estado);
        Assert.Equal(CodigosDeErrorDelInventario.StockInsuficiente, sinStock.Problema!.Code);
        var detalle = Assert.IsType<ProblemaDeStockDto>(sinStock.Problema);
        Assert.Equal((50, 5), (detalle.Lineas.Single().Solicitada, detalle.Lineas.Single().DisponibleEnLaRed));

        // Un libro digital no se reserva (422), una cantidad en cero es 400 con su campo y un pedido ajeno es 404.
        Assert.Equal((HttpStatusCode.UnprocessableEntity, CodigosDeErrorDelInventario.EdicionNoImpresa), ((await Crear(cliente, ReservaARecojo(Guid.NewGuid(), LineaDto(digital)))) is { } d ? (d.Estado, d.Problema!.Code) : default));
        var invalida = await Crear(cliente, ReservaARecojo(Guid.NewGuid(), LineaDto(enLima, 0)));
        Assert.Equal((HttpStatusCode.BadRequest, CodigosDeErrorDelInventario.ValidacionInvalida), (invalida.Estado, invalida.Problema!.Code));
        Assert.NotEmpty(invalida.Problema.Errores);

        var creada = await Crear(cliente, new CrearReservaDto(pedido, TiposDeEntrega.Recojo, "lima", null, [lineaDelPedido]), clave);
        Assert.True(creada.EsExito, creada.Problema?.Code);
        var ajena = await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.consultar", pedido, rol: RolesDeUsuario.Cliente, usuario: Guid.NewGuid());
        Assert.Equal((HttpStatusCode.NotFound, CodigosDeErrorDelInventario.NoEncontrado), (ajena.Estado, ajena.Problema!.Code));

        // La misma clave con otra carga es un conflicto; otro pedido con una pendiente vigente topa con el límite (429 con el motivo y el instante de reintento).
        var conflicto = await Crear(cliente, new CrearReservaDto(pedido, TiposDeEntrega.Recojo, "lima", null, [LineaDto(enLima, 7)]), clave);
        Assert.Equal((HttpStatusCode.Conflict, CodigosDeErrorDelInventario.IdempotenciaEnConflicto), (conflicto.Estado, conflicto.Problema!.Code));
        var limite = await Crear(cliente, ReservaARecojo(Guid.NewGuid(), LineaDto(enLima)));
        Assert.Equal((HttpStatusCode.TooManyRequests, CodigosDeErrorDelInventario.LimiteExcedido), (limite.Estado, limite.Problema!.Code));
        var respuestaDeLimite = Assert.IsType<RespuestaDeLimiteDto>(limite.Problema);
        Assert.Equal(BabelHub.Shared.Contratos.Inventario.MotivosDeLimiteDeReservas.PendientePorCliente, respuestaDeLimite.Motivo);
        Assert.True(respuestaDeLimite.RetryAfterSeconds > 0);
        Assert.True(limite.Http.Headers.Contains("Retry-After"));

        // Estado inválido: confirmar una reserva liberada; no encontrado: confirmar o extender un pedido que no existe; prohibido: un cliente que quiere cerrar.
        Assert.True((await ecommerce.Llamar<LiberarReservaDto, ReservaDto>("inventario.reservas.liberar", pedido, new LiberarReservaDto(MotivosDeLiberacion.CheckoutCancelado, null))).EsExito);
        var liberada = await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.confirmar", pedido, clave: Clave());
        Assert.Equal((HttpStatusCode.Conflict, CodigosDeErrorDelInventario.ReservaEstadoInvalido), (liberada.Estado, liberada.Problema!.Code));
        var inexistente = await ecommerce.Llamar<object, ReservaDto>("inventario.reservas.confirmar", Guid.NewGuid(), clave: Clave());
        Assert.Equal((HttpStatusCode.NotFound, CodigosDeErrorDelInventario.NoEncontrado), (inexistente.Estado, inexistente.Problema!.Code));
        var prohibido = await ecommerce.Llamar<CerrarReservaDto, CierreDeReservaDto>("inventario.reservas.cerrar", pedido, new CerrarReservaDto(MotivosDeCierre.Entrega, [lineaDelPedido.LineaId], null), RolesDeUsuario.Cliente, cliente, null, Clave());
        Assert.Equal((HttpStatusCode.Forbidden, CodigosDeErrorDelInventario.Prohibido), (prohibido.Estado, prohibido.Problema!.Code));
    }

    // ---------- las unidades vendidas de punta a punta ----------

    private sealed class ReceptorSinEfecto : IAlLiberarPedido
    {
        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private static Task<IReadOnlyList<VentaAtribuida>> Ventas(PilaDeReservas pila, Guid? fuente) =>
        pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IOrigenDeUnidadesVendidas>().Ventas(fuente, pila.Reloj.Ahora.AddDays(-30), pila.Reloj.Ahora, token)));

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    [Trait("Criterio", "AC-SEL-21")]
    public async Task LasUnidadesVendidas_CuentanUnaSolaVezEnLaSucursalDeRecojo_EnCadaFaseDelTrasladoSimulado()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 9, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        var traslados = new TrasladosDeIntegracionDoble(bd);
        using var pila = new PilaDeReservas(bd, s =>
        {
            s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => traslados);
            s.AddSingleton<IAlLiberarPedido>(new ReceptorSinEfecto());
        });
        var entrada = EntradaARecojo(libro);
        var venta = new VentaAtribuida(entrada.PedidoId, libro, lima, 1);
        var encargadoDeCusco = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, cusco);
        var encargadoDeLima = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, lima);

        // Pendiente: todavía no es una venta.
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.Empty(await Ventas(pila, null));

        // Confirmado: el pedido cuenta una vez, en la sucursal de recojo, aunque las unidades estén en Cusco.
        var confirmada = await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Clave()));
        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        var traslado = confirmada.Valor.Traslados.Single().TrasladoId;
        await AfirmarUnaVenta(pila, venta, lima, cusco, libro, (9, 1), (0, 0));

        // Aprobado: el traslado cambia de estado en `Traslados`; la reserva y las ventas no se tocan.
        await AfirmarUnaVenta(pila, venta, lima, cusco, libro, (9, 1), (0, 0));

        // En tránsito: el despacho consume la reserva en el origen (el físico de Cusco baja) y la venta sigue siendo una.
        var despacho = await pila.EnUnAmbito(encargadoDeCusco, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IReservasParaTraslados>().ConsumirEnElOrigen(entrada.PedidoId, cusco, libro, token)));
        Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, despacho);
        await AfirmarUnaVenta(pila, venta, lima, cusco, libro, (8, 0), (0, 0));

        // Recibido: la línea recreada en Lima no es otra venta.
        var recepcion = await pila.EnUnAmbito(encargadoDeLima, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IReservasParaTraslados>().RecrearEnElDestino(entrada.PedidoId, lima, libro, 1, traslado, token)));
        Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, recepcion);
        await AfirmarUnaVenta(pila, venta, lima, cusco, libro, (8, 0), (1, 1));

        // Entregado: el físico baja en Lima y la venta sigue contando una sola vez.
        var entrega = await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(encargadoDeLima, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Entrega, [entrada.Lineas[0].LineaId], null, Clave()));
        Assert.True(entrega.EsExito, entrega.Error?.Clave);
        await AfirmarUnaVenta(pila, venta, lima, cusco, libro, (8, 0), (0, 0));
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
    }

    private async Task AfirmarUnaVenta(PilaDeReservas pila, VentaAtribuida venta, Guid lima, Guid cusco, Guid libro, (int, int) stockDeCusco, (int, int) stockDeLima)
    {
        Assert.Equal([venta], await Ventas(pila, null));
        Assert.Equal([venta], await Ventas(pila, lima));
        Assert.Empty(await Ventas(pila, cusco));
        Assert.Equal(stockDeCusco, await bd.StockDe(cusco, libro));
        Assert.Equal(stockDeLima, await bd.StockDe(lima, libro));
    }

    private static ReservarEntrada EntradaARecojo(Guid libro) => PilaDeReservas.ARecojo("lima", [PilaDeReservas.Linea(libro, 1)]);
}
