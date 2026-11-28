using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T21: <c>IProgresoDelCheckout</c> (D13, D16, D21; AC-CAR-23, AC-CAR-30; CAR-25, CAR-34): lo que Pagos informa después de confirmar, condicional al <c>pedidoId</c>, idempotente, con «aún no» mientras el checkout está <c>reservando</c>, éxito idempotente
/// si el checkout ya no existe, retiros aplazados solo al llegar a <c>en_edicion</c>, <c>cancelado</c> o <c>pagado</c>, y <c>Reabrir</c> solo con un retiro pendiente, fuera de transacción y antes de bloquear.
/// </summary>
public sealed class ProgresoDelCheckoutTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private sealed record Escenario(EntornoDelCarrito Entorno, Checkout Checkout, Guid Impreso, Guid Digital, Guid Pedido);

    /// <summary>Un checkout en pago a domicilio con un impreso (3 unidades) y una digital: el contacto existe.</summary>
    private static async Task<Escenario> EnPago()
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = e.Impreso(10, 40m, "Impreso");
        var digital = e.Digital(20m, "Digital");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(impreso, 3, CodigoDeSucursal: Sucursal.Principal));
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(digital, 1, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho")));
        TextoVistoDeCompra[] textos = [.. Los2, new TextoVistoDeCompra(TextoLegalDeCompra.ReembolsoDigital, "r-1")];
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(textos));
        var checkout = e.Checkouts.Todos.Single();
        e.Inventario.AlReservar = ResultadoDeReservar.Reservada(new ReservaObtenida(Guid.Empty, Inicio.AddMinutes(15), [new SeleccionDeLinea(checkout.Lineas.Single(l => !l.EsDigital).LineaId, impreso, true, "sede-central", false, null, 10)], false));
        var pago = await new PasarAlPago(e.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(152m, textos));
        Assert.True(pago.EsExito, pago.Error?.Clave);
        e.Bitacora.Bloqueos.Clear();
        return new Escenario(e, checkout, impreso, digital, checkout.PedidoId!.Value);
    }

    private static ProgresoDelCheckout Progreso(Escenario s) => new(s.Entorno.ServiciosDelCheckout());

    private static async Task<Escenario> Pendiente()
    {
        var s = await EnPago();
        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, await Progreso(s).PagoPendiente(s.Pedido, Inicio.AddMinutes(30), Inicio.AddMinutes(15)));
        s.Entorno.Bitacora.Bloqueos.Clear();
        return s;
    }

    // ---------- PagoPendiente ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-23")]
    public async Task PagoPendiente_pasa_a_pago_pendiente_con_los_instantes_de_Pagos_y_la_cuenta_regresiva_hasta_los_30_minutos()
    {
        var s = await EnPago();

        var resultado = await Progreso(s).PagoPendiente(s.Pedido, Inicio.AddMinutes(30), Inicio.AddMinutes(14));

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, resultado);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, s.Checkout.Estado);
        Assert.Equal(Inicio.AddMinutes(30), s.Checkout.LimitePendienteEn);
        Assert.Equal(Inicio.AddMinutes(14), s.Checkout.ReservaExpiraEn);
        Assert.Equal(TimeSpan.FromMinutes(30), s.Checkout.CuentaRegresiva(Inicio));
        Assert.Equal(["checkout", "carrito"], s.Entorno.Bitacora.Bloqueos);
        Assert.Contains(s.Checkout, s.Entorno.Checkouts.Guardados);
    }

    [Fact]
    public async Task PagoPendiente_es_idempotente_es_condicional_al_pedido_y_si_el_checkout_ya_no_existe_es_un_exito()
    {
        var s = await EnPago();
        var progreso = Progreso(s);
        await progreso.PagoPendiente(s.Pedido, Inicio.AddMinutes(30), Inicio.AddMinutes(14));

        var repetido = await progreso.PagoPendiente(s.Pedido, Inicio.AddMinutes(31), Inicio.AddMinutes(14));
        var otroPedido = await progreso.PagoPendiente(Guid.NewGuid(), Inicio.AddMinutes(30), Inicio.AddMinutes(14));

        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, repetido);
        Assert.Equal(Inicio.AddMinutes(30), s.Checkout.LimitePendienteEn);
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, otroPedido);
    }

    [Fact]
    public async Task Mientras_el_checkout_esta_reservando_responde_aun_no_para_que_Pagos_reintente()
    {
        var s = await EnPago();
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5, 10m);
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, 1, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo));
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        var prep = await new PrepararPasoAlPago(e.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(10m, Los2));
        var pedido = prep.Valor.PedidoId!.Value;
        _ = s;

        var progreso = new ProgresoDelCheckout(e.ServiciosDelCheckout());
        Assert.Equal(ResultadoDelAvisoDePago.AunNo, await progreso.PagoPendiente(pedido, Inicio.AddMinutes(30), Inicio.AddMinutes(15)));
        Assert.Equal(ResultadoDelAvisoDePago.AunNo, await progreso.PagoAprobado(pedido));
        Assert.Equal(ResultadoDelAvisoDePago.AunNo, await progreso.PagoResuelto(pedido, MotivoDePagoResueltoDeCompra.Vencido));
        Assert.Equal(EstadoDelCheckout.Reservando, e.Checkouts.Todos.Single().Estado);
    }

    // ---------- PagoAprobado ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public async Task PagoAprobado_desde_pago_pendiente_elimina_el_contacto_y_quita_las_lineas_pagadas_del_carrito()
    {
        var s = await Pendiente();

        var resultado = await Progreso(s).PagoAprobado(s.Pedido);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, resultado);
        Assert.Equal(EstadoDelCheckout.Pagado, s.Checkout.Estado);
        Assert.Null(s.Checkout.Contacto);
        Assert.Null(s.Checkout.HuellaDeInstantanea);
        Assert.Empty(s.Entorno.Carritos.Todos.Single().Lineas);
        Assert.Equal(["checkout", "carrito"], s.Entorno.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task PagoAprobado_desde_en_pago_sin_pendiente_tambien_paga_y_repetirlo_es_idempotente()
    {
        var s = await EnPago();
        var progreso = Progreso(s);

        var primero = await progreso.PagoAprobado(s.Pedido);
        var repetido = await progreso.PagoAprobado(s.Pedido);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, primero);
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, repetido);
        Assert.Equal(EstadoDelCheckout.Pagado, s.Checkout.Estado);
    }

    [Fact]
    public async Task PagoAprobado_quita_solo_la_cantidad_comprada_y_conserva_lo_no_pagado()
    {
        var s = await Pendiente();
        var carrito = s.Entorno.Carritos.Todos.Single();
        // Mientras se pagaba, el cliente sumó dos unidades desde otro dispositivo al carrito (el checkout compró 3).
        carrito.Modificar(s.Entorno.LineaDe(s.Impreso), 5, Inicio);

        await Progreso(s).PagoAprobado(s.Pedido);

        var restante = Assert.Single(carrito.Lineas);
        Assert.Equal((s.Impreso, 2), (restante.EdicionId, restante.Cantidad.Valor));
    }

    [Fact]
    public async Task PagoAprobado_aplica_los_retiros_pendientes_dejando_su_aviso()
    {
        var s = await Pendiente();
        var carrito = s.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(s.Digital, diferir: true, Inicio);

        await Progreso(s).PagoAprobado(s.Pedido);

        Assert.Single(await s.Entorno.Carritos.AvisosSinVer(s.Entorno.Actor.Actual!.UsuarioId!.Value));
        Assert.Empty(carrito.Lineas);
    }

    // ---------- PagoResuelto ----------

    [Theory]
    [InlineData(MotivoDePagoResueltoDeCompra.Vencido)]
    [InlineData(MotivoDePagoResueltoDeCompra.Rechazado)]
    public async Task Un_pago_vencido_o_rechazado_con_la_reserva_vigente_vuelve_a_en_pago_sin_consultar_a_Pagos(MotivoDePagoResueltoDeCompra motivo)
    {
        var s = await Pendiente();
        var creaciones = s.Entorno.Pagos.Creaciones;

        var resultado = await Progreso(s).PagoResuelto(s.Pedido, motivo);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, resultado);
        Assert.Equal(EstadoDelCheckout.EnPago, s.Checkout.Estado);
        Assert.Equal((0, 0, creaciones), (s.Entorno.Pagos.Reaperturas, s.Entorno.Pagos.Cancelaciones, s.Entorno.Pagos.Creaciones));
    }

    [Fact]
    public async Task Con_la_reserva_expirada_un_pago_vencido_cancela_elimina_el_contacto_y_anota_la_liberacion()
    {
        var s = await Pendiente();
        s.Entorno.Reloj.Ahora = Inicio.AddMinutes(20);

        var resultado = await Progreso(s).PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Vencido);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, resultado);
        Assert.Equal(EstadoDelCheckout.Cancelado, s.Checkout.Estado);
        Assert.Null(s.Checkout.Contacto);
        Assert.NotNull(s.Checkout.ReservaALiberarDesde);
    }

    [Fact]
    public async Task Compensado_cancela_elimina_el_contacto_y_es_idempotente()
    {
        var s = await Pendiente();
        var progreso = Progreso(s);

        var primero = await progreso.PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Compensado);
        var repetido = await progreso.PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Compensado);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, primero);
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, repetido);
        Assert.Equal(EstadoDelCheckout.Cancelado, s.Checkout.Estado);
        Assert.Null(s.Checkout.Contacto);
    }

    [Theory]
    [InlineData(ResultadoDeReabrirIntencion.Reabierta, EstadoDelCheckout.EnEdicion)]
    [InlineData(ResultadoDeReabrirIntencion.NoCancelable, EstadoDelCheckout.EnPago)]
    [InlineData(ResultadoDeReabrirIntencion.Cerrada, EstadoDelCheckout.Cancelado)]
    [InlineData(ResultadoDeReabrirIntencion.NoExiste, EstadoDelCheckout.Cancelado)]
    public async Task Con_un_retiro_pendiente_un_rechazo_pasa_por_Reabrir_fuera_de_transaccion_y_antes_de_bloquear(ResultadoDeReabrirIntencion respuesta, EstadoDelCheckout esperado)
    {
        var s = await Pendiente();
        var carrito = s.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(s.Digital, diferir: true, Inicio);
        s.Entorno.Pagos.AlReabrir = respuesta;

        var resultado = await Progreso(s).PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Rechazado);

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, resultado);
        Assert.Equal(esperado, s.Checkout.Estado);
        Assert.Equal(1, s.Entorno.Pagos.Reaperturas);
        Assert.Equal(["reabrir", "checkout", "carrito"], s.Entorno.Bitacora.Bloqueos);
        if (esperado == EstadoDelCheckout.EnPago)
        {
            // Hay un intento activo: no hay estado atascado, un cobro nuevo exige en_pago sin retiro, y el retiro sigue anotado.
            Assert.Contains(carrito.Lineas, l => l.RetiroPendiente);
        }
        else
        {
            Assert.DoesNotContain(carrito.Lineas, l => l.EdicionId == s.Digital);
        }
    }

    [Fact]
    public async Task Si_Reabrir_falla_se_devuelve_un_error_para_que_Pagos_reintente_y_no_cambia_nada()
    {
        var s = await Pendiente();
        var carrito = s.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(s.Digital, diferir: true, Inicio);
        s.Entorno.Pagos.NoResponde = true;

        await Assert.ThrowsAsync<NoSePudoAplicarElAvisoException>(() => Progreso(s).PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Vencido));

        Assert.Equal(EstadoDelCheckout.PagoPendiente, s.Checkout.Estado);
        Assert.Contains(carrito.Lineas, l => l.RetiroPendiente);
    }

    [Fact]
    public async Task El_retiro_se_aplica_con_un_cancelado_o_pagado_pero_un_pago_pendiente_o_en_pago_lo_conserva()
    {
        var s = await Pendiente();
        var carrito = s.Entorno.Carritos.Todos.Single();
        carrito.RetirarEdicion(s.Digital, diferir: true, Inicio);

        await Progreso(s).PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Compensado);

        Assert.DoesNotContain(carrito.Lineas, l => l.EdicionId == s.Digital);
        Assert.Single(await s.Entorno.Carritos.AvisosSinVer(s.Entorno.Actor.Actual!.UsuarioId!.Value));
    }

    // ---------- Cuenta cancelada y pedido descartado ----------

    [Fact]
    public async Task Si_el_checkout_ya_no_existe_por_una_cuenta_cancelada_todos_los_avisos_son_un_exito_idempotente()
    {
        var s = await Pendiente();
        var progreso = Progreso(s);
        var inexistente = Guid.NewGuid();

        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, await progreso.PagoPendiente(inexistente, Inicio, Inicio));
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, await progreso.PagoAprobado(inexistente));
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, await progreso.PagoResuelto(inexistente, MotivoDePagoResueltoDeCompra.Vencido));
    }

    [Fact]
    public async Task Un_aviso_rezagado_de_un_pedido_descartado_no_toca_el_checkout_nuevo()
    {
        var s = await EnPago();
        s.Entorno.Pagos.AlCancelar = ResultadoDeCancelarIntencion.Cancelada;
        var carrito = s.Entorno.Carritos.Todos.Single();
        s.Checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, s.Pedido, s.Checkout.GeneracionDeReserva, carrito, Inicio);

        var aprobado = await Progreso(s).PagoAprobado(s.Pedido);
        var resuelto = await Progreso(s).PagoResuelto(s.Pedido, MotivoDePagoResueltoDeCompra.Compensado);

        var desconocido = await Progreso(s).PagoAprobado(Guid.NewGuid());

        // El último pedido descartado es un éxito idempotente (Pagos deja de reintentar); uno desconocido no aplica. Ninguno toca el checkout.
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, aprobado);
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, resuelto);
        Assert.Equal(ResultadoDelAvisoDePago.SinCambios, desconocido);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
    }
}
