using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T20: <c>VolverAlCarrito</c>, <c>ReiniciarCheckout</c> y <c>LiberarReservasPendientes</c> (CAR-11, CAR-19, CAR-26; AC-CAR-10, AC-CAR-17): cancelan la intención con el orden de D10, eliminan el contacto, pasan a <c>cancelado</c> o <c>reiniciado</c>, anotan la
/// reserva y la liberan después de confirmar; con el pago pendiente o <c>reservando</c> se rechazan; los retiros pendientes se aplican; un pedido ya confirmado no se reintenta sin fin.
/// </summary>
public sealed class VolverYReiniciarTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private sealed record Escenario(EntornoDelCarrito Entorno, Checkout Checkout, Guid Impreso);

    /// <summary>Un checkout en pago a domicilio, con el contacto y la reserva hechos.</summary>
    private static async Task<Escenario> EnPago(bool conPagoCompleto = true)
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = e.Impreso(10, 40m, "Impreso");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(impreso, 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho")));
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        var checkout = e.Checkouts.Todos.Single();
        if (conPagoCompleto)
        {
            e.Inventario.AlReservar = ResultadoDeReservar.Reservada(new ReservaObtenida(Guid.Empty, Inicio.AddMinutes(15), [new SeleccionDeLinea(checkout.Lineas.Single().LineaId, impreso, true, "sede-central", false, null, 10)], false));
            var pago = await new PasarAlPago(e.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(132m, Los2));
            Assert.True(pago.EsExito, pago.Error?.Clave);
        }

        e.Bitacora.Bloqueos.Clear();
        return new Escenario(e, checkout, impreso);
    }

    private static Task<Resultado<VistaDelCarrito>> Volver(Escenario s, Guid? pedido = null) => new SalirDelCheckout(s.Entorno.ServiciosDelCheckout(), reiniciar: false).Ejecutar(new SalirDelCheckoutEntrada(pedido));

    private static Task<Resultado<VistaDelCarrito>> Reiniciar(Escenario s, Guid? pedido = null) => new SalirDelCheckout(s.Entorno.ServiciosDelCheckout(), reiniciar: true).Ejecutar(new SalirDelCheckoutEntrada(pedido));

    // ---------- Volver ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-17")]
    public async Task Volver_cancela_la_intencion_primero_elimina_el_contacto_conserva_el_carrito_y_libera_de_inmediato_las_unidades()
    {
        var s = await EnPago();
        var pedido = s.Checkout.PedidoId!.Value;

        var resultado = await Volver(s);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoDelCheckout.Cancelado, s.Checkout.Estado);
        Assert.Null(s.Checkout.Contacto);
        Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(["cancelar", "checkout", "carrito"], s.Entorno.Bitacora.Bloqueos.Take(3));
        Assert.Equal((pedido, "checkout-cancelado"), Assert.Single(s.Entorno.Inventario.Liberaciones));
        Assert.Null(s.Checkout.ReservaALiberarDesde);
        Assert.Equal(["volver"], s.Entorno.Auditoria.Terminados);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-17")]
    public async Task Con_el_inventario_caido_el_carrito_se_conserva_el_contacto_ya_se_elimino_y_la_liberacion_queda_anotada()
    {
        var s = await EnPago();
        s.Entorno.Inventario.AlLiberar = ResultadoDeLiberar.NoSePudo;

        var resultado = await Volver(s);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCheckout.Cancelado, s.Checkout.Estado);
        Assert.Null(s.Checkout.Contacto);
        Assert.NotNull(s.Checkout.ReservaALiberarDesde);
        Assert.Single(resultado.Valor.Lineas);
    }

    [Fact]
    public async Task Volver_en_edicion_sin_pedido_no_llama_a_Pagos_ni_al_inventario()
    {
        var s = await EnPago(conPagoCompleto: false);

        var resultado = await Volver(s);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCheckout.Cancelado, s.Checkout.Estado);
        Assert.Equal(0, s.Entorno.Pagos.Cancelaciones);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-23")]
    public async Task Con_el_pago_pendiente_no_se_puede_volver_ni_reiniciar_y_con_reservando_responde_en_curso()
    {
        var pendiente = await EnPago();
        pendiente.Checkout.PagoPendiente(pendiente.Checkout.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);
        var reservando = await EnPago(conPagoCompleto: false);
        var carrito = reservando.Entorno.Carritos.Todos.Single();
        Assert.Equal(EfectoDeTransicion.Aplicada, reservando.Checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, carrito, Inicio).Efecto);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, (await Volver(pendiente)).Error!.Clave);
        Assert.Equal(ClavesDelCarrito.PagoPendiente, (await Reiniciar(pendiente)).Error!.Clave);
        Assert.Equal(ClavesDelCarrito.EnCurso, (await Volver(reservando)).Error!.Clave);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, pendiente.Checkout.Estado);
        Assert.Equal(0, pendiente.Entorno.Pagos.Cancelaciones);
    }

    [Fact]
    public async Task Con_un_intento_activo_Cancelar_responde_no_cancelable_y_no_cambia_nada()
    {
        var s = await EnPago();
        s.Entorno.Pagos.AlCancelar = ResultadoDeCancelarIntencion.NoCancelable;

        var resultado = await Volver(s);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, resultado.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
        Assert.NotNull(s.Checkout.Contacto);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Si_Pagos_no_responde_a_Cancelar_con_el_checkout_en_pago_no_hay_cambio_local()
    {
        var s = await EnPago();
        s.Entorno.Pagos.NoResponde = true;

        var resultado = await Volver(s);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
    }

    [Fact]
    public async Task Los_retiros_pendientes_se_aplican_al_cancelar_con_su_aviso_y_el_carrito_se_bloquea_despues_del_checkout()
    {
        var s = await EnPago();
        var carrito = s.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(s.Impreso, diferir: true, Inicio);

        var resultado = await Volver(s);

        Assert.True(resultado.EsExito);
        Assert.Empty(carrito.Lineas);
        Assert.Single(await s.Entorno.Carritos.AvisosSinVer(s.Entorno.Actor.Actual!.UsuarioId!.Value));
        Assert.Equal(["cancelar", "checkout", "carrito"], s.Entorno.Bitacora.Bloqueos.Take(3));
    }

    [Fact]
    public async Task Repetir_volver_sobre_un_checkout_terminado_es_un_exito_idempotente_con_el_carrito()
    {
        var s = await EnPago();
        await Volver(s);
        var liberaciones = s.Entorno.Inventario.Liberaciones.Count;

        var otra = await Volver(s);

        Assert.True(otra.EsExito);
        Assert.Single(otra.Valor.Lineas);
        Assert.Equal(liberaciones, s.Entorno.Inventario.Liberaciones.Count);
        Assert.Single(s.Entorno.Auditoria.Terminados);
    }

    [Fact]
    public async Task Un_pedido_oculto_ajeno_es_no_encontrado_con_el_evento_y_uno_propio_descartado_un_error_amable()
    {
        var s = await EnPago();

        var ajeno = await Volver(s, Guid.NewGuid());

        Assert.Equal(ClavesDelCarrito.NoEncontrado, ajeno.Error!.Clave);
        Assert.Equal("recurso-ajeno-pedido-id", Assert.Single(s.Entorno.Denegaciones.Eventos).Motivo);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
    }

    [Fact]
    public async Task Sin_sesion_con_personal_o_cuenta_cancelada_se_rechaza_y_un_limite_excedido_responde_limite()
    {
        var visitante = EntornoDelCarrito.DeVisitante();
        var encargado = new EntornoDelCarrito(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var limitado = await EnPago();
        limitado.Entorno.Limites.Volver = new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, Inicio.AddMinutes(5));

        Assert.Equal(CategoriaDeError.NoAutenticado, (await new SalirDelCheckout(visitante.ServiciosDelCheckout(), false).Ejecutar(new SalirDelCheckoutEntrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await new SalirDelCheckout(encargado.ServiciosDelCheckout(), false).Ejecutar(new SalirDelCheckoutEntrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Limite, (await Volver(limitado)).Error!.Categoria);
        Assert.Equal(EstadoDelCheckout.EnPago, limitado.Checkout.Estado);
    }

    // ---------- Reiniciar ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-10")]
    public async Task Reiniciar_deja_el_checkout_reiniciado_libera_la_reserva_y_la_persona_empieza_otro_con_un_pedido_nuevo()
    {
        var s = await EnPago();
        var pedido = s.Checkout.PedidoId;

        var resultado = await Reiniciar(s);
        var nuevo = await new IniciarCheckout(s.Entorno.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelCheckout.Reiniciado, s.Checkout.Estado);
        Assert.Equal(["reiniciar"], s.Entorno.Auditoria.Terminados);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
        Assert.True(nuevo.EsExito);
        var otro = s.Entorno.Checkouts.Todos.Single(c => c.EsVivo);
        Assert.NotSame(s.Checkout, otro);
        Assert.Null(otro.PedidoId);
        Assert.NotEqual(pedido, otro.PedidoId);
    }

    // ---------- LiberarReservasPendientes ----------

    private static async Task<Escenario> ConLiberacionPendiente(ResultadoDeLiberar alLiberar)
    {
        var s = await EnPago();
        s.Entorno.Inventario.AlLiberar = ResultadoDeLiberar.NoSePudo;
        await Volver(s);
        Assert.NotNull(s.Checkout.ReservaALiberarDesde);
        s.Entorno.Inventario.Liberaciones.Clear();
        s.Entorno.Inventario.AlLiberar = alLiberar;
        return s;
    }

    private static async Task<Resultado<ResumenDeLiberaciones>> Trabajo(EntornoDelCarrito e)
    {
        e.Actor.Establecer(BabelHub.Ecommerce.Application.Plataforma.ActorActual.DelSistema());
        return await new LiberarReservasPendientes(e.ServiciosDelCheckout()).Ejecutar(new LiberarReservasPendientesEntrada());
    }

    [Theory]
    [InlineData(ResultadoDeLiberar.Liberada)]
    [InlineData(ResultadoDeLiberar.NoExistia)]
    public async Task El_trabajo_reintenta_la_liberacion_y_la_da_por_hecha_ante_un_exito_o_un_pedido_inexistente(ResultadoDeLiberar respuesta)
    {
        var s = await ConLiberacionPendiente(respuesta);

        var resumen = await Trabajo(s.Entorno);

        Assert.Equal((1, 1, 0, 0), (resumen.Valor.Encontradas, resumen.Valor.Liberadas, resumen.Valor.PedidosConfirmados, resumen.Valor.SinRespuesta));
        Assert.Null(s.Checkout.ReservaALiberarDesde);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Si_el_inventario_sigue_sin_responder_la_anotacion_queda_para_la_corrida_siguiente()
    {
        var s = await ConLiberacionPendiente(ResultadoDeLiberar.NoSePudo);

        var primera = await Trabajo(s.Entorno);
        var segunda = await Trabajo(s.Entorno);

        Assert.Equal(1, primera.Valor.SinRespuesta);
        Assert.Equal(1, segunda.Valor.SinRespuesta);
        Assert.NotNull(s.Checkout.ReservaALiberarDesde);
        Assert.Equal(2, s.Entorno.Inventario.Liberaciones.Count);
    }

    [Fact]
    public async Task Un_pedido_ya_confirmado_se_limpia_sin_reintento_y_se_alerta_de_modo_que_no_reintenta_sin_fin()
    {
        var s = await ConLiberacionPendiente(ResultadoDeLiberar.PedidoConfirmado);

        var primera = await Trabajo(s.Entorno);
        var segunda = await Trabajo(s.Entorno);

        Assert.Equal(1, primera.Valor.PedidosConfirmados);
        Assert.Equal(0, segunda.Valor.Encontradas);
        Assert.Null(s.Checkout.ReservaALiberarDesde);
        Assert.Single(s.Entorno.Inventario.Liberaciones);
        Assert.Equal([(TipoDeAlertaDelCarrito.LiberacionDeUnPedidoConfirmado, 1)], s.Entorno.Alertas.Publicadas);
    }

    [Fact]
    public async Task El_trabajo_solo_corre_como_sistema_y_un_cliente_o_el_personal_se_rechazan()
    {
        var s = await ConLiberacionPendiente(ResultadoDeLiberar.Liberada);

        var cliente = await new LiberarReservasPendientes(s.Entorno.ServiciosDelCheckout()).Ejecutar(new LiberarReservasPendientesEntrada());
        s.Entorno.Actor.Establecer(BabelHub.Ecommerce.Application.Plataforma.ActorActual.Anonimo());
        var anonimo = await new LiberarReservasPendientes(s.Entorno.ServiciosDelCheckout()).Ejecutar(new LiberarReservasPendientesEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, cliente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, anonimo.Error!.Categoria);
        Assert.Empty(s.Entorno.Inventario.Liberaciones);
    }
}
