using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T19a: el paso 3a <c>SolicitarIntencionDePago</c> (lee con bloqueo la marca de cancelación y llama a <c>Crear</c>) y el 3b <c>ConfirmarEnPago</c> (relee la marca, bloquea el checkout y pasa a <c>en_pago</c> con auditoría): condicionales al
/// <c>pedidoId</c>, la generación y la huella, idempotentes y sin llamar a Pagos con un retiro pendiente (D18, D21; AC-CAR-09, AC-CAR-27, CAR-10).
/// </summary>
public sealed class IntencionYConfirmacionTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private sealed record Contexto(EntornoDelCarrito Entorno, Checkout Checkout, PasoAlPagoPreparado Preparado);

    private static async Task<Contexto> Preparado(bool soloDigital = false)
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(soloDigital ? e.Digital(20m) : e.Impreso(10, 40m), soloDigital ? 1 : 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        TextoVistoDeCompra[] textos = soloDigital ? [.. Los2, new TextoVistoDeCompra(TextoLegalDeCompra.ReembolsoDigital, "r-1")] : Los2;
        if (!soloDigital)
        {
            await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo));
        }

        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(textos));
        var preparado = await new PrepararPasoAlPago(e.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(soloDigital ? 20m : 120m, textos));
        Assert.True(preparado.EsExito, preparado.Error?.Clave);
        e.Bitacora.Bloqueos.Clear();
        return new Contexto(e, e.Checkouts.Todos.Single(), preparado.Valor);
    }

    private static Task<ResultadoDeLaIntencion> Solicitar(Contexto c, DateTimeOffset? expira = null) =>
        new SolicitarIntencionDePago(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado, expira ?? Inicio.AddMinutes(15));

    private static Task<ResultadoDeLaConfirmacion> Confirmar(Contexto c, DateTimeOffset? expira = null) =>
        new ConfirmarEnPago(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado, expira ?? Inicio.AddMinutes(15));

    // ---------- 3a ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-09")]
    public async Task La_intencion_se_crea_con_la_cuenta_la_instantanea_la_huella_y_la_expiracion_de_la_reserva_leyendo_primero_la_marca_con_bloqueo()
    {
        var c = await Preparado();

        var resultado = await Solicitar(c, Inicio.AddMinutes(14));

        Assert.Equal(DesenlaceDeLaIntencion.Creada, resultado.Desenlace);
        Assert.Equal(1, c.Entorno.Pagos.Creaciones);
        Assert.Equal(["marca"], c.Entorno.Bitacora.Bloqueos);
        Assert.Equal(EstadoDelCheckout.Reservando, c.Checkout.Estado);
    }

    [Theory]
    [InlineData(ResultadoDeCrearIntencion.Conflicto, DesenlaceDeLaIntencion.Conflicto)]
    [InlineData(ResultadoDeCrearIntencion.YaEnCurso, DesenlaceDeLaIntencion.YaEnCurso)]
    [InlineData(ResultadoDeCrearIntencion.Cerrada, DesenlaceDeLaIntencion.Cerrada)]
    public async Task Los_resultados_de_Crear_se_traducen_sin_abortar_salvo_Cerrada_que_lleva_al_aborto(ResultadoDeCrearIntencion respuesta, DesenlaceDeLaIntencion esperado)
    {
        var c = await Preparado();
        c.Entorno.Pagos.AlCrear = respuesta;

        var resultado = await Solicitar(c);

        Assert.Equal(esperado, resultado.Desenlace);
        Assert.Equal(EstadoDelCheckout.Reservando, c.Checkout.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Si_la_cancelacion_de_la_cuenta_esta_en_marcha_se_aborta_sin_crear_la_intencion()
    {
        var c = await Preparado();
        c.Entorno.Marca.Estado = EstadoDeLaMarcaDeCancelacion.EnEjecucion;

        var resultado = await Solicitar(c);

        Assert.Equal(DesenlaceDeLaIntencion.Abortar, resultado.Desenlace);
        Assert.Equal("cuenta-no-activa", resultado.MotivoDelAborto);
        Assert.Equal(ClavesDelCarrito.CuentaNoPuedeComprar, resultado.Error!.Clave);
        Assert.Equal(0, c.Entorno.Pagos.Creaciones);
        Assert.False(resultado.IntencionCreada);
    }

    [Fact]
    public async Task Si_el_checkout_cambio_de_pedido_generacion_o_huella_se_aborta_sin_crear_nada()
    {
        var otroPedido = await Preparado();
        var otraGeneracion = await Preparado();
        var otraHuella = await Preparado();
        var sinCheckout = await Preparado();

        var a = await new SolicitarIntencionDePago(otroPedido.Entorno.ServiciosDelCheckout()).Ejecutar(otroPedido.Preparado with { PedidoId = Guid.NewGuid() }, Inicio.AddMinutes(15));
        var b = await new SolicitarIntencionDePago(otraGeneracion.Entorno.ServiciosDelCheckout()).Ejecutar(otraGeneracion.Preparado with { Generacion = 9 }, Inicio.AddMinutes(15));
        var d = await new SolicitarIntencionDePago(otraHuella.Entorno.ServiciosDelCheckout()).Ejecutar(otraHuella.Preparado with { HuellaDeInstantanea = "otra" }, Inicio.AddMinutes(15));
        sinCheckout.Checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, sinCheckout.Preparado.PedidoId!.Value, sinCheckout.Preparado.Generacion, sinCheckout.Entorno.Carritos.Todos.Single(), Inicio);
        var f = await Solicitar(sinCheckout);

        Assert.All(new[] { a, b, d, f }, r => Assert.Equal(DesenlaceDeLaIntencion.Abortar, r.Desenlace));
        Assert.All(new[] { otroPedido, otraGeneracion, otraHuella, sinCheckout }, c => Assert.Equal(0, c.Entorno.Pagos.Creaciones));
    }

    [Fact]
    public async Task Si_Pagos_no_responde_a_Crear_se_aborta_y_se_cancelara_la_intencion_por_si_se_creo()
    {
        var c = await Preparado();
        c.Entorno.Pagos.NoResponde = true;

        var resultado = await Solicitar(c);

        Assert.Equal(DesenlaceDeLaIntencion.Abortar, resultado.Desenlace);
        Assert.True(resultado.IntencionCreada);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task Un_carrito_solo_digital_pide_la_intencion_con_una_expiracion_de_15_minutos_desde_ahora()
    {
        var c = await Preparado(soloDigital: true);

        var resultado = await new SolicitarIntencionDePago(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado, null);

        Assert.Equal(DesenlaceDeLaIntencion.Creada, resultado.Desenlace);
        Assert.Equal(1, c.Entorno.Pagos.Creaciones);
    }

    // ---------- 3b ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-09")]
    public async Task Confirmar_pasa_a_en_pago_con_la_expiracion_de_la_reserva_audita_y_bloquea_marca_checkout_y_carrito_en_ese_orden()
    {
        var c = await Preparado();

        var resultado = await Confirmar(c, Inicio.AddMinutes(14));

        Assert.Equal(DesenlaceDeLaConfirmacion.EnPago, resultado.Desenlace);
        Assert.Equal(EstadoDelCheckout.EnPago, c.Checkout.Estado);
        Assert.Equal(Inicio.AddMinutes(14), c.Checkout.ReservaExpiraEn);
        Assert.Equal([(true, false)], c.Entorno.Auditoria.PasosAlPago);
        Assert.Equal(["marca", "checkout", "carrito"], c.Entorno.Bitacora.Bloqueos);
        Assert.Contains(c.Checkout, c.Entorno.Checkouts.Guardados);
    }

    [Fact]
    public async Task Confirmar_con_el_checkout_ya_en_pago_con_el_mismo_pedido_y_huella_es_un_exito_idempotente_que_no_audita_otra_vez()
    {
        var c = await Preparado();
        await Confirmar(c);

        var otra = await Confirmar(c);

        Assert.Equal(DesenlaceDeLaConfirmacion.EnPago, otra.Desenlace);
        Assert.Single(c.Entorno.Auditoria.PasosAlPago);
    }

    [Fact]
    public async Task Confirmar_con_otro_pedido_generacion_o_huella_no_cambia_nada_y_responde_en_curso()
    {
        var c = await Preparado();

        var a = await new ConfirmarEnPago(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado with { PedidoId = Guid.NewGuid() }, Inicio.AddMinutes(15));
        var b = await new ConfirmarEnPago(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado with { Generacion = 9 }, Inicio.AddMinutes(15));
        var d = await new ConfirmarEnPago(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado with { HuellaDeInstantanea = "otra" }, Inicio.AddMinutes(15));

        Assert.All(new[] { a, b, d }, r => Assert.Equal(DesenlaceDeLaConfirmacion.Abortar, r.Desenlace));
        Assert.Equal(EstadoDelCheckout.Reservando, c.Checkout.Estado);
        Assert.Empty(c.Entorno.Auditoria.PasosAlPago);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Confirmar_relee_la_marca_y_si_la_cancelacion_empezo_entre_3a_y_3b_aborta_sin_pasar_a_en_pago()
    {
        var c = await Preparado();
        c.Entorno.Marca.Estado = EstadoDeLaMarcaDeCancelacion.Diferida;

        var resultado = await Confirmar(c);

        Assert.Equal(DesenlaceDeLaConfirmacion.Abortar, resultado.Desenlace);
        Assert.Equal("cuenta-no-activa", resultado.MotivoDelAborto);
        Assert.Equal(EstadoDelCheckout.Reservando, c.Checkout.Estado);
    }

    [Fact]
    public async Task Si_no_se_puede_auditar_confirmar_falla_y_el_checkout_no_pasa_a_en_pago()
    {
        var c = await Preparado();
        c.Entorno.Auditoria.Caida = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Confirmar(c));
    }

    [Fact]
    public async Task Con_un_retiro_pendiente_confirmar_no_cambia_el_estado_ni_llama_a_Pagos_dentro_de_la_transaccion()
    {
        var c = await Preparado();
        var carrito = c.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(carrito.Lineas.Single().EdicionId, diferir: true, Inicio);

        var resultado = await Confirmar(c);

        Assert.Equal(DesenlaceDeLaConfirmacion.ConRetiroPendiente, resultado.Desenlace);
        Assert.Equal(EstadoDelCheckout.Reservando, c.Checkout.Estado);
        Assert.Equal(0, c.Entorno.Pagos.Reaperturas);
        Assert.Empty(c.Entorno.Auditoria.PasosAlPago);
    }

    // ---------- El Dominio: volver a la edicion por un retiro pendiente ----------

    [Fact]
    public async Task VolverALaEdicionPorRetiroPendiente_conserva_el_pedido_sube_la_generacion_aplica_el_retiro_y_es_condicional()
    {
        var c = await Preparado();
        var carrito = c.Entorno.Carritos.Todos.Single();
        var edicion = carrito.Lineas.Single().EdicionId;
        carrito.RetirarEdicion(edicion, diferir: true, Inicio);
        var pedido = c.Preparado.PedidoId!.Value;

        Assert.Equal(EfectoDeTransicion.NoAplica, c.Checkout.VolverALaEdicionPorRetiroPendiente(Guid.NewGuid(), c.Preparado.Generacion, carrito, Inicio).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, c.Checkout.VolverALaEdicionPorRetiroPendiente(pedido, c.Preparado.Generacion + 1, carrito, Inicio).Efecto);
        var aplicada = c.Checkout.VolverALaEdicionPorRetiroPendiente(pedido, c.Preparado.Generacion, carrito, Inicio);

        Assert.Equal(EfectoDeTransicion.Aplicada, aplicada.Efecto);
        Assert.Equal((EstadoDelCheckout.EnEdicion, pedido, c.Preparado.Generacion + 1), (c.Checkout.Estado, c.Checkout.PedidoId, c.Checkout.GeneracionDeReserva));
        Assert.Equal(edicion, Assert.Single(aplicada.Retiradas).EdicionId);
        Assert.Empty(carrito.Lineas);
    }
}
