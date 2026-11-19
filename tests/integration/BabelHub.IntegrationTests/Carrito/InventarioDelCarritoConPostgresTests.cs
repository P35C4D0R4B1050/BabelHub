using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T32: Carrito frente a un Inventario caído, con límite, sin stock o que deja expirar la reserva, sobre PostgreSQL real (AC-CAR-04, AC-CAR-05, AC-CAR-08, AC-CAR-10, AC-CAR-12, AC-CAR-14, AC-CAR-17, AC-CAR-24): el checkout sin stock validable y sin cobro, la consulta previa con traslado y «a
/// confirmar», la reserva rechazada por límite con <c>disponibleDesde</c> o por falta de stock con el carrito conservado, la expiración a los 15 minutos con reloj controlado, la liberación al volver con el Inventario caído y reintentada después, la licencia vencida con el pago ya
/// pendiente, <c>PagoAprobado</c> y la línea no disponible corregible sin perder las demás.
/// </summary>
[Collection(ColeccionPostgresDelCarrito.Nombre)]
public sealed class InventarioDelCarritoConPostgresTests(PostgresDelCarritoFixture bd) : IAsyncLifetime
{
    private EntornoDelCarritoConPostgres _e = null!;

    public async Task InitializeAsync() => _e = await EntornoDelCarritoConPostgres.Crear(bd);

    public async Task DisposeAsync() => await _e.DisposeAsync();

    private Guid Impreso(int stock = 10, decimal precio = 40m, string fuente = "sede-central", bool traslado = false, int? plazo = null)
    {
        var edicion = _e.Catalogo.Poner(FormatoDeEdicion.Impresa, precio);
        _e.Inventario.FijarStock(edicion, stock);
        _e.Inventario.FijarFuente(edicion, fuente, traslado, plazo);
        _e.Disponibilidad.Vendible(edicion, stock);
        return edicion;
    }

    private Task<string?> Estado(Guid cuenta) => bd.Consultar<string>($"SELECT estado FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' ORDER BY creado_en DESC LIMIT 1");

    private async Task<Guid> ConCarrito(Guid edicion, int cantidad)
    {
        var cuenta = EntornoDelCarritoConPostgres.NuevaCuenta();
        var r = await _e.Agregar(cuenta, edicion, cantidad);
        Assert.True(r.EsExito, r.Error?.Clave);
        return cuenta;
    }

    // ---------- Iniciar y revisar ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-05")]
    public async Task Con_el_Inventario_caido_no_se_puede_iniciar_el_checkout_ni_se_crea_nada_y_nunca_hay_cobro()
    {
        var cuenta = await ConCarrito(Impreso(), 3);
        _e.InventarioDeLasPruebas.RespuestaDeDisponibilidad = () => RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.SinRespuesta;

        var inicio = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()));

        Assert.False(inicio.EsExito);
        Assert.Equal("checkout-stock-no-validable", inicio.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, inicio.Error.Categoria);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.checkouts"));
        Assert.Equal(0, _e.Pagos.Creaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task La_consulta_previa_informa_el_traslado_con_su_plazo_o_a_confirmar_y_un_Inventario_sin_respuesta_no_deja_avanzar()
    {
        var (conPlazo, aConfirmar) = (Impreso(fuente: "sede-huanta", traslado: true, plazo: 3), Impreso(fuente: "sede-huanta", traslado: true, plazo: null));
        var cuenta = EntornoDelCarritoConPostgres.NuevaCuenta();
        await _e.Agregar(cuenta, conPlazo, 1);
        await _e.Agregar(cuenta, aConfirmar, 1);
        await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()));

        var vista = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null)));

        Assert.True(vista.EsExito, vista.Error?.Clave);
        var lineas = vista.Valor.Lineas;
        Assert.Equal(DisponibilidadDeLineaDeCheckout.PorTraslado, lineas[0].Disponibilidad);
        Assert.Equal(3, lineas[0].PlazoInformadoDias);
        Assert.True(lineas[1].PlazoAConfirmar);
        Assert.Null(lineas[1].PlazoInformadoDias);

        _e.InventarioDeLasPruebas.RespuestaDePrevia = () => RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta;
        var sinRespuesta = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null)));
        Assert.False(sinRespuesta.EsExito);
        Assert.Equal("checkout-stock-no-validable", sinRespuesta.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-08")]
    public async Task Una_linea_sin_stock_se_corrige_quitandola_sin_perder_las_demas()
    {
        var (buena, agotada) = (Impreso(), Impreso(stock: 5));
        var cuenta = EntornoDelCarritoConPostgres.NuevaCuenta();
        await _e.Agregar(cuenta, buena, 2);
        await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(agotada, 1)));
        _e.Inventario.FijarStock(agotada, 0);

        var inicio = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()));

        Assert.False(inicio.EsExito);
        Assert.Equal("carrito-sin-stock", inicio.Error!.Clave);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.checkouts"));
        await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().EliminarLinea(new EliminarLineaEntrada(agotada)));
        var corregido = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()));
        Assert.True(corregido.EsExito, corregido.Error?.Clave);
        Assert.Equal(buena, Assert.Single(corregido.Valor.Lineas).EdicionId);
    }

    // ---------- Reservar ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task Un_limite_del_Inventario_al_reservar_devuelve_el_instante_de_reintento_conserva_el_pedido_y_no_libera_ni_cobra()
    {
        var cuenta = await ConCarrito(Impreso(), 3);
        await _e.PrepararCheckout(cuenta);
        var hasta = _e.Reloj.Ahora.AddMinutes(10);
        _e.InventarioDeLasPruebas.RespuestaDeReserva = () => ResultadoDeReservar.De(TipoDeResultadoDeReservar.LimiteExcedido, "INV-LIMITE-EXCEDIDO", hasta);

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(pago.EsExito);
        Assert.Equal(CategoriaDeError.Limite, pago.Error!.Categoria);
        Assert.Contains(pago.Error.Errores, e => e.Campo == "disponibleDesde");
        Assert.Equal(0, _e.Pagos.Creaciones);
        Assert.Equal(0, _e.InventarioDeLasPruebas.Liberaciones);
        Assert.NotNull(await bd.Consultar<Guid?>($"SELECT pedido_id FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-12")]
    public async Task Sin_stock_al_reservar_se_rechaza_con_las_lineas_se_conserva_el_carrito_y_el_checkout_vuelve_a_edicion()
    {
        var impreso = Impreso(stock: 10);
        var cuenta = await ConCarrito(impreso, 3);
        await _e.PrepararCheckout(cuenta);
        _e.Inventario.FijarStock(impreso, 1);

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(pago.EsExito);
        Assert.Equal("checkout-reserva-rechazada", pago.Error!.Clave);
        Assert.Equal("en_edicion", await Estado(cuenta));
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
        Assert.Equal(3, (await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito()))).Valor.Carrito.Lineas[0].Cantidad);
    }

    [Fact]
    public async Task Un_Inventario_sin_respuesta_o_con_una_respuesta_invalida_al_reservar_no_deja_pasar_al_pago()
    {
        var cuenta = await ConCarrito(Impreso(), 3);
        await _e.PrepararCheckout(cuenta);
        _e.InventarioDeLasPruebas.RespuestaDeReserva = () => ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible);

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(pago.EsExito);
        Assert.Equal(0, _e.Pagos.Creaciones);
        Assert.NotEqual("en_pago", await Estado(cuenta));
    }

    // ---------- Expiración y liberación ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-24")]
    public async Task La_reserva_expira_a_los_15_minutos_con_reloj_controlado_y_repetir_el_pago_vuelve_a_reservar_con_un_pedido_nuevo()
    {
        var (impreso, cuenta) = (Impreso(), EntornoDelCarritoConPostgres.NuevaCuenta());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        var primera = await _e.Pagar(cuenta, 120m);
        var pedido = primera.Valor.PedidoId!.Value;
        Assert.Equal(_e.Reloj.Ahora.AddMinutes(15), await bd.Consultar<DateTime?>($"SELECT reserva_expira_en FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'") is { } e ? new DateTimeOffset(e, TimeSpan.Zero) : default);
        _e.Reloj.Adelantar(TimeSpan.FromMinutes(16));

        var repetida = await _e.Pagar(cuenta, 120m);

        Assert.False(repetida.EsExito);
        Assert.Equal("checkout-reserva-rechazada", repetida.Error!.Clave);
        Assert.Equal("en_edicion", await Estado(cuenta));
        Assert.NotEqual(pedido, await bd.Consultar<Guid?>($"SELECT pedido_id FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-17")]
    public async Task Volver_con_el_Inventario_caido_cancela_elimina_el_contacto_anota_la_liberacion_y_el_trabajo_la_libera_despues()
    {
        var (impreso, cuenta) = (Impreso(), EntornoDelCarritoConPostgres.NuevaCuenta());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta, domicilio: true);
        var pago = await _e.Pagar(cuenta, 132m);
        Assert.True(pago.EsExito, pago.Error?.Clave);
        _e.InventarioDeLasPruebas.RespuestaDeLiberar = () => ResultadoDeLiberar.NoSePudo;

        var volver = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().VolverAlCarrito(new SalirDelCheckoutEntrada()));

        Assert.True(volver.EsExito, volver.Error?.Clave);
        Assert.Equal("cancelado", await Estado(cuenta));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' AND contacto_nombre IS NOT NULL"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' AND reserva_a_liberar_desde IS NOT NULL AND pedido_a_liberar IS NOT NULL"));
        Assert.Equal(3, _e.Inventario.Reservado(impreso));

        _e.InventarioDeLasPruebas.RespuestaDeLiberar = null;
        var liberadas = await _e.ComoSistema(sp => new LiberarReservasPendientes(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new LiberarReservasPendientesEntrada()));

        Assert.Equal(1, liberadas.Valor.Liberadas);
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.checkouts WHERE reserva_a_liberar_desde IS NOT NULL"));
    }

    // ---------- Pago aprobado ----------

    [Fact]
    public async Task Con_el_pago_pendiente_y_la_licencia_vencida_despues_PagoAprobado_concede_la_compra_y_deja_el_carrito_vacio()
    {
        var (impreso, digital) = (Impreso(), _e.Catalogo.Poner(FormatoDeEdicion.Digital, 20m));
        var cuenta = EntornoDelCarritoConPostgres.NuevaCuenta();
        await _e.Agregar(cuenta, impreso, 2);
        await _e.Agregar(cuenta, digital, 1);
        await _e.PrepararCheckout(cuenta, conDigitales: true);
        var pago = await _e.Pagar(cuenta, 100m, conDigitales: true);
        Assert.True(pago.EsExito, pago.Error?.Clave);
        var pedido = pago.Valor.PedidoId!.Value;
        await _e.Como(cuenta, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoPendiente(pedido, _e.Reloj.Ahora.AddMinutes(30), _e.Reloj.Ahora.AddMinutes(15)));
        _e.Catalogo.Poner(FormatoDeEdicion.Digital, 20m, estado: EstadoDeEdicionConsultado.RetiradaDeVenta);

        var aprobado = await _e.Como(cuenta, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoAprobado(pedido));

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, aprobado);
        Assert.Equal("pagado", await Estado(cuenta));
        Assert.Empty((await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito()))).Valor.Carrito.Lineas);
    }
}
