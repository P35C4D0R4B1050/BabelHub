using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T19b: el orquestador <c>PasarAlPago</c> (D18, D19; AC-CAR-09, 14, 21, 24, 27; CAR-10, CAR-23): 1 preparar → 2 reservar → 3a intención → 3b confirmar, cada uno en su transacción, con el aborto condicional ante toda falla que no sea un duplicado, la repetición
/// con el checkout ya en pago (Crear primero, Reservar solo si Creada) y el retiro pendiente en 3b.
/// </summary>
public sealed class PasarAlPagoTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];
    private static readonly TextoVistoDeCompra[] Los3 = [.. Los2, new(TextoLegalDeCompra.ReembolsoDigital, "r-1")];

    private sealed record Escenario(EntornoDelCarrito Entorno, Checkout Checkout, Guid Impreso, decimal Total, TextoVistoDeCompra[] Textos);

    private static async Task<Escenario> Listo(bool soloDigital = false)
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = soloDigital ? Guid.Empty : e.Impreso(10, 40m, "Impreso");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(soloDigital ? e.Digital(20m) : impreso, soloDigital ? 1 : 3));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        if (!soloDigital)
        {
            e.Inventario.FijarFuente(impreso, "sede-central", false, null);
            await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null));
        }

        var textos = soloDigital ? Los3 : Los2;
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(textos));
        e.Bitacora.Bloqueos.Clear();
        return new Escenario(e, e.Checkouts.Todos.Single(), impreso, soloDigital ? 20m : 120m, textos);
    }

    private static Task<Resultado<ResultadoDePasarAlPago>> Pagar(Escenario s) =>
        new PasarAlPago(s.Entorno.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(s.Total, s.Textos));

    /// <summary>Configura el Inventario para que reserve exactamente lo que se le pida, con la fuente informada y 15 minutos.</summary>
    private static void ReservaCorrecta(Escenario s, int minutos = 15, string fuente = "sede-central")
    {
        var e = s.Entorno;
        e.Inventario.AlReservar = ResultadoDeReservar.Reservada(new ReservaObtenida(
            Guid.Empty,
            Inicio.AddMinutes(minutos),
            [new SeleccionDeLinea(s.Checkout.Lineas.Single().LineaId, s.Impreso, true, fuente, false, null, 10)],
            false));
    }

    // ---------- El camino feliz ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-09")]
    public async Task Pasar_al_pago_con_impresos_reserva_crea_la_intencion_y_deja_el_checkout_en_pago_con_la_expiracion_de_15_minutos()
    {
        var s = await Listo();
        ReservaCorrecta(s);

        var resultado = await Pagar(s);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoVisibleDelCheckout.EnPago, s.Checkout.PedidoId), (resultado.Valor.Estado, resultado.Valor.PedidoId));
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
        Assert.Equal(Inicio.AddMinutes(15), s.Checkout.ReservaExpiraEn);
        var (pedido, generacion, _) = Assert.Single(s.Entorno.Inventario.Reservas);
        Assert.Equal((s.Checkout.PedidoId, s.Checkout.GeneracionDeReserva), (pedido, generacion));
        Assert.Equal(1, s.Entorno.Pagos.Creaciones);
        Assert.Equal([(true, false)], s.Entorno.Auditoria.PasosAlPago);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
        Assert.Equal(0, s.Entorno.Pagos.Cancelaciones);
    }

    [Fact]
    public async Task Un_carrito_solo_digital_no_reserva_y_crea_la_intencion()
    {
        var s = await Listo(soloDigital: true);

        var resultado = await Pagar(s);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
        Assert.Empty(s.Entorno.Inventario.Reservas);
        Assert.Equal(1, s.Entorno.Pagos.Creaciones);
        Assert.Equal([(false, false)], s.Entorno.Auditoria.PasosAlPago);
    }

    [Fact]
    public async Task Las_marcas_se_leen_con_bloqueo_en_cada_paso_corto_y_el_checkout_no_se_retiene_durante_la_llamada_a_Pagos()
    {
        var s = await Listo();
        ReservaCorrecta(s);

        await Pagar(s);

        // Preparar: marca, checkout, carrito. 3a: marca (el checkout se lee sin bloqueo). 3b: marca, checkout, carrito.
        Assert.Equal(["marca", "checkout", "carrito", "marca", "marca", "checkout", "carrito"], s.Entorno.Bitacora.Bloqueos);
    }

    // ---------- Fallas del paso 2 ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-24")]
    public async Task Sin_stock_al_reservar_vuelve_a_la_revision_con_el_carrito_conservado_libera_la_reserva_y_no_crea_intencion()
    {
        var s = await Listo();
        var pedido = Guid.Empty;
        s.Entorno.Inventario.AlReservar = ResultadoDeReservar.Sin([new LineaSinStock(s.Checkout.Lineas.Single().LineaId, s.Impreso, 3, 2)]);

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.ReservaRechazada, resultado.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Null(s.Checkout.PedidoId);
        Assert.NotNull(s.Checkout.PedidoDescartado);
        Assert.Equal(s.Checkout.PedidoDescartado, Assert.Single(s.Entorno.Inventario.Liberaciones).PedidoId);
        Assert.Equal(0, s.Entorno.Pagos.Creaciones);
        Assert.Single(s.Entorno.Carritos.Todos.Single().Lineas);
        _ = pedido;
    }

    [Fact]
    public async Task El_en_curso_del_inventario_no_aborta_y_la_reserva_de_la_primera_solicitud_sigue()
    {
        var s = await Listo();
        s.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.EnCurso, "INV-IDEMPOTENCIA-EN-CURSO");

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.EnCurso, resultado.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.Reservando, s.Checkout.Estado);
        Assert.NotNull(s.Checkout.PedidoId);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task Un_429_muestra_el_motivo_y_el_instante_de_reintento_y_conserva_el_pedido_el_carrito_y_la_reserva_vigente()
    {
        var s = await Listo();
        var desde = Inicio.AddMinutes(9);
        s.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.LimiteExcedido, "INV-LIMITE-EXCEDIDO", desde);
        var pedidoAntes = s.Checkout.PedidoId;

        var resultado = await Pagar(s);

        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("disponibleDesde", desde.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), resultado.Error.Errores);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.NotNull(s.Checkout.PedidoId);
        Assert.Null(s.Checkout.PedidoDescartado);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
        Assert.Single(s.Entorno.Carritos.Todos.Single().Lineas);
        _ = pedidoAntes;
    }

    [Fact]
    public async Task Una_reserva_ya_expirada_aborta_con_un_pedido_nuevo_en_el_siguiente_intento()
    {
        var s = await Listo();
        ReservaCorrecta(s, minutos: -1);

        var resultado = await Pagar(s);
        var pedidoDescartado = s.Checkout.PedidoDescartado;
        ReservaCorrecta(s);
        var otra = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.ReservaRechazada, resultado.Error!.Clave);
        Assert.True(otra.EsExito, otra.Error?.Clave);
        Assert.NotEqual(pedidoDescartado, s.Checkout.PedidoId);
    }

    // ---------- Fallas del paso 3 ----------

    [Fact]
    public async Task Crear_con_Cerrada_aborta_y_el_siguiente_intento_usa_otro_pedido()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        s.Entorno.Pagos.AlCrear = ResultadoDeCrearIntencion.Cerrada;

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.ReservaRechazada, resultado.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.NotNull(s.Checkout.PedidoDescartado);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Crear_con_YaEnCurso_redirige_al_estado_del_pago_sin_abortar_ni_liberar()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        s.Entorno.Pagos.AlCrear = ResultadoDeCrearIntencion.YaEnCurso;

        var resultado = await Pagar(s);

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.PagoEnCurso);
        Assert.Equal(EstadoDelCheckout.Reservando, s.Checkout.Estado);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Crear_con_Conflicto_devuelve_el_estado_actual_sin_abortar()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        s.Entorno.Pagos.AlCrear = ResultadoDeCrearIntencion.Conflicto;

        var resultado = await Pagar(s);

        Assert.True(resultado.EsExito);
        Assert.NotNull(resultado.Valor.Revision);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
        Assert.Equal(0, s.Entorno.Pagos.Cancelaciones);
    }

    [Fact]
    public async Task Si_Pagos_no_responde_a_Crear_se_aborta_se_cancela_por_si_se_creo_y_se_libera_la_reserva()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        s.Entorno.Pagos.NoResponde = true;

        var resultado = await Pagar(s);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Equal(1, s.Entorno.Pagos.Cancelaciones);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Una_cancelacion_de_la_cuenta_entre_3a_y_3b_aborta_cancela_la_intencion_y_libera_la_reserva()
    {
        var s = await Listo();
        ReservaCorrecta(s);

        // Lecturas de la marca: 1 (preparar), 2 (3a), 3 (3b): la cancelación empieza antes de la tercera.
        s.Entorno.Marca.LecturasHastaCambiar = 2;

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.CuentaNoPuedeComprar, resultado.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Equal(1, s.Entorno.Pagos.Cancelaciones);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
        Assert.Empty(s.Entorno.Auditoria.PasosAlPago);
    }

    [Fact]
    public async Task Una_cancelacion_entre_el_paso_1_y_3a_aborta_sin_crear_la_intencion()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        s.Entorno.Marca.LecturasHastaCambiar = 1;

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.CuentaNoPuedeComprar, resultado.Error!.Clave);
        Assert.Equal(0, s.Entorno.Pagos.Creaciones);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
    }

    // ---------- Retiro pendiente en 3b ----------

    [Fact]
    public async Task Con_un_retiro_pendiente_entre_3a_y_3b_reabre_fuera_de_la_transaccion_y_vuelve_a_edicion_conservando_el_pedido_con_la_generacion_nueva()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        var carrito = s.Entorno.Carritos.Todos.Single();
        s.Entorno.Pagos.AlCrearAccion = () => carrito.RetirarEdicion(s.Impreso, diferir: true, Inicio);
        s.Entorno.Pagos.AlReabrir = ResultadoDeReabrirIntencion.Reabierta;

        var resultado = await Pagar(s);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.NotNull(resultado.Valor.Revision);
        Assert.True(resultado.Valor.Revision!.CarritoCambio);
        Assert.Equal(1, s.Entorno.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Equal(Assert.Single(s.Entorno.Inventario.Reservas).PedidoId, s.Checkout.PedidoId);
        Assert.Empty(carrito.Lineas);
        Assert.Empty(s.Entorno.Auditoria.PasosAlPago);
    }

    [Theory]
    [InlineData(ResultadoDeReabrirIntencion.Cerrada)]
    [InlineData(ResultadoDeReabrirIntencion.NoExiste)]
    [InlineData(ResultadoDeReabrirIntencion.NoCancelable)]
    public async Task Si_Reabrir_no_la_deja_abierta_el_checkout_se_queda_reservando_para_que_lo_recoja_la_recuperacion(ResultadoDeReabrirIntencion respuesta)
    {
        var s = await Listo();
        ReservaCorrecta(s);
        var carrito = s.Entorno.Carritos.Todos.Single();
        s.Entorno.Pagos.AlCrearAccion = () => carrito.RetirarEdicion(s.Impreso, diferir: true, Inicio);
        s.Entorno.Pagos.AlReabrir = respuesta;

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.EnCurso, resultado.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.Reservando, s.Checkout.Estado);
    }

    // ---------- Repetir con el checkout ya en pago ----------

    [Fact]
    public async Task Repetir_con_el_checkout_en_pago_llama_primero_a_Crear_y_solo_si_Creada_repite_Reservar_con_la_misma_clave()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        await Pagar(s);
        var pedido = s.Checkout.PedidoId;
        ReservaCorrecta(s);

        var otra = await Pagar(s);

        Assert.True(otra.EsExito, otra.Error?.Clave);
        Assert.Equal(pedido, otra.Valor.PedidoId);
        Assert.Equal(2, s.Entorno.Pagos.Creaciones);
        Assert.Equal(2, s.Entorno.Inventario.Reservas.Count);
        Assert.Equal(s.Entorno.Inventario.Reservas[0].Generacion, s.Entorno.Inventario.Reservas[1].Generacion);
        Assert.Single(s.Entorno.Auditoria.PasosAlPago);
    }

    [Fact]
    public async Task Repetir_con_en_pago_y_YaEnCurso_redirige_sin_repetir_Reservar()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        await Pagar(s);
        s.Entorno.Pagos.AlCrear = ResultadoDeCrearIntencion.YaEnCurso;

        var otra = await Pagar(s);

        Assert.True(otra.Valor.PagoEnCurso);
        Assert.Single(s.Entorno.Inventario.Reservas);
    }

    [Fact]
    public async Task Repetir_con_en_pago_y_la_reserva_expirada_aborta_desde_en_pago_llamando_primero_a_Cancelar()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        await Pagar(s);
        ReservaCorrecta(s, minutos: -1);
        s.Entorno.Bitacora.Bloqueos.Clear();

        var otra = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.ReservaRechazada, otra.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Equal(1, s.Entorno.Pagos.Cancelaciones);
        Assert.Contains("cancelar", s.Entorno.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task Con_un_cobro_vivo_el_estado_invalido_del_inventario_no_aborta_desde_en_pago()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        await Pagar(s);
        s.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.EstadoInvalido, "INV-RESERVA-ESTADO-INVALIDO");
        s.Entorno.Pagos.AlCancelar = ResultadoDeCancelarIntencion.NoCancelable;

        var otra = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, otra.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }

    // ---------- Con un retiro pendiente en pago ----------

    [Fact]
    public async Task Con_un_retiro_pendiente_con_el_checkout_en_pago_se_devuelve_la_revision_sin_pagar()
    {
        var s = await Listo();
        ReservaCorrecta(s);
        await Pagar(s);
        var carrito = s.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(s.Impreso, diferir: true, Inicio);
        var creaciones = s.Entorno.Pagos.Creaciones;

        var otra = await new PasarAlPago(s.Entorno.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(120m, s.Textos));

        Assert.True(otra.EsExito, otra.Error?.Clave);
        Assert.NotNull(otra.Valor.Revision);
        Assert.Equal(creaciones, s.Entorno.Pagos.Creaciones);
    }

    // ---------- Fallas del paso 1 ----------

    [Fact]
    public async Task Una_falla_del_paso_1_no_crea_reserva_ni_intencion()
    {
        var s = await Listo();
        s.Entorno.Cuenta.CorreoSinVerificar = true;

        var resultado = await Pagar(s);

        Assert.Equal(ClavesDelCarrito.CorreoSinVerificar, resultado.Error!.Clave);
        Assert.Empty(s.Entorno.Inventario.Reservas);
        Assert.Equal(0, s.Entorno.Pagos.Creaciones);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
    }
}
