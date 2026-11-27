using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T23: los trabajos de fondo de Carrito (CAR-29, CAR-34; AC-CAR-25, AC-CAR-30): purgas de visitantes, avisos y contacto, y las recuperaciones de <c>reservando</c>, la liberación y la revisión de pagos pendientes. Todos corren solo como «sistema».
/// </summary>
public sealed class TrabajosDeFondoTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private static EntornoDelCarrito ComoSistema(EntornoDelCarrito e)
    {
        e.Actor.Establecer(ActorActual.DelSistema());
        return e;
    }

    // ---------- Purgar carritos de visitante ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-25")]
    public async Task Los_carritos_de_visitante_se_eliminan_a_los_30_dias_y_los_de_cuenta_no_caducan()
    {
        var e = EntornoDelCarrito.DeVisitante();
        var libro = e.Impreso(5);
        var cuenta = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Inicio);
        cuenta.Agregar(libro, false, 1, Inicio);
        e.Carritos.Sembrar(cuenta);
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, 1, CodigoDeSucursal: Sucursal.Principal));
        e.Reloj.Ahora = Inicio.AddDays(29).AddHours(23);
        ComoSistema(e);
        var antes = await new PurgarCarritosDeVisitante(e.Servicios()).Ejecutar(new TrabajoDelCarritoEntrada());
        e.Reloj.Ahora = Inicio.AddDays(30);

        var resultado = await new PurgarCarritosDeVisitante(e.Servicios()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Equal(0, antes.Valor.Tocados);
        Assert.Equal(1, resultado.Valor.Tocados);
        Assert.Same(cuenta, Assert.Single(e.Carritos.Todos));
        Assert.Contains(("carritos-de-visitante", 1), e.Auditoria.Purgas);
    }

    [Fact]
    public async Task La_purga_de_avisos_elimina_los_vistos_hace_30_dias_o_creados_hace_90()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var viejoVisto = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "A", null, null, Inicio.AddDays(-40));
        var reciente = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "B", null, null, Inicio.AddDays(-1));
        var muyViejo = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "C", null, null, Inicio.AddDays(-91));
        await e.Carritos.AgregarAvisos(cuenta, [viejoVisto, reciente, muyViejo]);
        await e.Carritos.MarcarAvisosComoVistos(cuenta, [viejoVisto.Id], Inicio.AddDays(-35));
        ComoSistema(e);

        var resultado = await new PurgarAvisosVistos(e.Servicios()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Equal(2, resultado.Valor.Tocados);
        Assert.Equal(reciente.Id, Assert.Single(await e.Carritos.AvisosSinVer(cuenta)).Id);
    }

    // ---------- Purgar contacto y checkouts ----------

    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout)> ConCheckout()
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Impreso(10, 40m), 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho")));
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        return (e, e.Checkouts.Todos.Single());
    }

    private static Task<Resultado<ResumenDeTrabajoDelCarrito>> PurgarContacto(EntornoDelCarrito e) =>
        new PurgarContactoYCheckoutsVencidos(e.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public async Task El_contacto_de_un_checkout_en_edicion_se_elimina_a_los_30_dias_de_la_ultima_modificacion_de_la_persona_y_a_los_29_dias_y_23_horas_no()
    {
        var (e, checkout) = await ConCheckout();
        ComoSistema(e);
        e.Reloj.Ahora = Inicio.AddDays(29).AddHours(23);
        var antes = await PurgarContacto(e);
        e.Reloj.Ahora = Inicio.AddDays(30);

        var resultado = await PurgarContacto(e);

        Assert.Equal(0, antes.Valor.Tocados);
        Assert.Equal(1, resultado.Valor.Tocados);
        Assert.Null(checkout.Contacto);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Contains(("contacto-y-checkouts", 1), e.Auditoria.Purgas);
    }

    [Fact]
    public async Task Un_checkout_abandonado_en_pago_se_cancela_despues_de_cancelar_su_intencion_y_se_libera_la_reserva()
    {
        var (e, checkout) = await ConCheckout();
        var carrito = e.Carritos.Todos.Single();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
        ComoSistema(e);
        e.Reloj.Ahora = Inicio.AddDays(31);

        var resultado = await PurgarContacto(e);

        Assert.Equal(1, resultado.Valor.Tocados);
        Assert.Equal(EstadoDelCheckout.Cancelado, checkout.Estado);
        Assert.Null(checkout.Contacto);
        Assert.Equal(1, e.Pagos.Cancelaciones);
        Assert.Single(e.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Con_un_cobro_vivo_o_Pagos_sin_responder_el_checkout_abandonado_no_se_toca_en_esta_corrida()
    {
        var vivo = await ConCheckout();
        var muerto = await ConCheckout();
        foreach (var (e, c) in new[] { vivo, muerto })
        {
            var carrito = e.Carritos.Todos.Single();
            c.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
            c.ConfirmarEnPago(c.PedidoId!.Value, c.GeneracionDeReserva, c.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
            ComoSistema(e);
            e.Reloj.Ahora = Inicio.AddDays(31);
        }

        vivo.Entorno.Pagos.AlCancelar = ResultadoDeCancelarIntencion.NoCancelable;
        muerto.Entorno.Pagos.NoResponde = true;

        var a = await PurgarContacto(vivo.Entorno);
        var b = await PurgarContacto(muerto.Entorno);

        Assert.Equal(0, a.Valor.Tocados);
        Assert.Equal(0, b.Valor.Tocados);
        Assert.Equal(EstadoDelCheckout.EnPago, vivo.Checkout.Estado);
        Assert.Equal(EstadoDelCheckout.EnPago, muerto.Checkout.Estado);
    }

    [Fact]
    public async Task Los_checkouts_terminados_de_mas_de_30_dias_se_eliminan()
    {
        var (e, checkout) = await ConCheckout();
        checkout.Volver(e.Carritos.Todos.Single(), Inicio);
        ComoSistema(e);
        e.Reloj.Ahora = Inicio.AddDays(31);

        var resultado = await PurgarContacto(e);

        Assert.Equal(1, resultado.Valor.Tocados);
        Assert.Empty(e.Checkouts.Todos);
    }

    [Fact]
    public async Task Las_cuentas_que_ya_no_estan_activas_pierden_su_carrito_y_su_checkout_y_si_Identidad_no_responde_no_se_purga()
    {
        var inactiva = await ConCheckout();
        inactiva.Entorno.Cuenta.Cancelada = true;
        var muda = await ConCheckout();
        muda.Entorno.Cuenta.NoResponde = true;
        foreach (var e in new[] { inactiva.Entorno, muda.Entorno })
        {
            ComoSistema(e);
            e.Reloj.Ahora = Inicio.AddDays(31);
        }

        var a = await PurgarContacto(inactiva.Entorno);
        var b = await PurgarContacto(muda.Entorno);

        Assert.True(a.Valor.Tocados >= 1);
        Assert.Empty(inactiva.Entorno.Carritos.Todos);
        Assert.Empty(inactiva.Entorno.Checkouts.Todos);
        Assert.NotEmpty(muda.Entorno.Carritos.Todos);
        Assert.True(b.EsExito);
    }

    [Fact]
    public async Task La_auditoria_de_la_purga_solo_lleva_el_conteo_y_si_falla_la_corrida_falla()
    {
        var (e, _) = await ConCheckout();
        ComoSistema(e);
        e.Auditoria.Caida = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => PurgarContacto(e));
    }

    // ---------- Recuperar checkouts reservando ----------

    [Fact]
    public async Task Un_checkout_reservando_de_mas_de_2_minutos_se_aborta_cancela_la_intencion_y_libera_la_reserva()
    {
        var (e, checkout) = await ConCheckout();
        var carrito = e.Carritos.Todos.Single();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        var pedido = checkout.PedidoId!.Value;
        ComoSistema(e);
        e.Reloj.Ahora = Inicio.AddMinutes(2);

        var resultado = await new RecuperarCheckoutsReservando(e.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Equal(1, resultado.Valor.Tocados);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(pedido, checkout.PedidoDescartado);
        Assert.Equal(1, e.Pagos.Cancelaciones);
        Assert.Single(e.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Un_checkout_reservando_reciente_o_que_ya_paso_a_en_pago_no_se_toca()
    {
        var reciente = await ConCheckout();
        var carrito = reciente.Entorno.Carritos.Todos.Single();
        reciente.Checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        ComoSistema(reciente.Entorno);
        reciente.Entorno.Reloj.Ahora = Inicio.AddMinutes(1);
        var enPago = await ConCheckout();
        var carrito2 = enPago.Entorno.Carritos.Todos.Single();
        enPago.Checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito2, Inicio);
        enPago.Checkout.ConfirmarEnPago(enPago.Checkout.PedidoId!.Value, enPago.Checkout.GeneracionDeReserva, enPago.Checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito2);
        ComoSistema(enPago.Entorno);
        enPago.Entorno.Reloj.Ahora = Inicio.AddMinutes(5);

        var a = await new RecuperarCheckoutsReservando(reciente.Entorno.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());
        var b = await new RecuperarCheckoutsReservando(enPago.Entorno.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Equal(0, a.Valor.Tocados);
        Assert.Equal(0, b.Valor.Tocados);
        Assert.Equal(EstadoDelCheckout.Reservando, reciente.Checkout.Estado);
        Assert.Equal(EstadoDelCheckout.EnPago, enPago.Checkout.Estado);
    }

    [Fact]
    public async Task La_recuperacion_aplica_los_retiros_pendientes_al_volver_a_edicion()
    {
        var (e, checkout) = await ConCheckout();
        var carrito = e.Carritos.Todos.Single();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        carrito.RetirarEdicion(carrito.Lineas.Single().EdicionId, diferir: true, Inicio);
        ComoSistema(e);
        e.Reloj.Ahora = Inicio.AddMinutes(3);

        await new RecuperarCheckoutsReservando(e.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Empty(carrito.Lineas);
        Assert.Single(await e.Carritos.AvisosSinVer(carrito.Propietario.CuentaId!.Value));
    }

    // ---------- Revisar pagos pendientes ----------

    [Fact]
    public async Task Con_pagos_pendientes_de_mas_de_45_minutos_se_publica_una_alerta_tecnica_y_sin_ellos_no()
    {
        var (e, checkout) = await ConCheckout();
        var carrito = e.Carritos.Todos.Single();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Crear(12m), s => s, s => s, carrito, Inicio);
        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
        checkout.PagoPendiente(checkout.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);
        ComoSistema(e);
        e.Reloj.Ahora = Inicio.AddMinutes(44);
        var antes = await new RevisarCheckoutsPendientes(e.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());
        e.Reloj.Ahora = Inicio.AddMinutes(46);

        var despues = await new RevisarCheckoutsPendientes(e.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada());

        Assert.Equal(0, antes.Valor.Tocados);
        Assert.Equal(1, despues.Valor.Tocados);
        Assert.Equal([(TipoDeAlertaDelCarrito.CheckoutsConElPagoPendienteDemasiado, 1)], e.Alertas.Publicadas);
    }

    // ---------- Alcance ----------

    [Fact]
    public async Task Ningun_trabajo_corre_con_un_actor_que_no_sea_el_sistema()
    {
        var cliente = EntornoDelCarrito.DeCliente();
        var anonimo = EntornoDelCarrito.DeVisitante();

        Assert.Equal(CategoriaDeError.Prohibido, (await new PurgarCarritosDeVisitante(cliente.Servicios()).Ejecutar(new TrabajoDelCarritoEntrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await new PurgarAvisosVistos(cliente.Servicios()).Ejecutar(new TrabajoDelCarritoEntrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await PurgarContacto(cliente)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await new RecuperarCheckoutsReservando(cliente.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await new RevisarCheckoutsPendientes(anonimo.ServiciosDelCheckout()).Ejecutar(new TrabajoDelCarritoEntrada())).Error!.Categoria);
    }
}
