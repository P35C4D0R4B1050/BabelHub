using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T06: la retención del contacto de entrega en el <see cref="Checkout"/> (CAR-34, D08, D09): se elimina al aprobarse el pago, al cancelar o reiniciar, al cambiar a recojo o a solo digital, a los 30 días de la última modificación hecha
/// por la persona y al cancelar o reemplazar la cuenta; y la huella de instantánea se anula junto con el contacto. Un checkout terminado o no a domicilio nunca conserva contacto.
/// </summary>
public sealed class RetencionDelContactoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CarritoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Func<string, string> Identidad = s => s;
    private static readonly CostoDeEnvio Envio = CostoDeEnvio.Crear(12.00m);

    private static Guid Edicion(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static LineaParaCheckout Impreso(int n) => new(Edicion(n), false, $"Libro {n}", "9780000000019", 1, 50m, Edicion(n), Sucursal.Principal);

    private static LineaParaCheckout Digital(int n) => new(Edicion(n), true, $"Digital {n}", "9780000000026", 1, 20m);

    private static ContactoDeEntrega Contacto() =>
        ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez"), TelefonoDeContacto.Crear("987654321"), DireccionDeEntrega.Crear("Jr. Lima 123", "Ayacucho"));

    private static CarritoDeDominio CarritoCon(params LineaParaCheckout[] lineas)
    {
        var carrito = CarritoDeDominio.Crear(CarritoId, PropietarioDelCarrito.DeCuenta(Cuenta), Ahora);
        foreach (var l in lineas)
        {
            carrito.Agregar(l.EdicionId, l.EsDigital, l.Cantidad, Ahora);
        }

        return carrito;
    }

    /// <summary>Un checkout mixto a domicilio con el contacto cargado, en edición.</summary>
    private static (Checkout Checkout, CarritoDeDominio Carrito) ADomicilio()
    {
        var checkout = Checkout.Iniciar(Guid.NewGuid(), Cuenta, CarritoId, [Impreso(1), Digital(2)], Guid.NewGuid, Ahora);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Ahora);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Ahora);
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Ahora);
        checkout.ElegirDomicilio(Contacto(), Ahora);
        return (checkout, CarritoCon(Impreso(1), Digital(2)));
    }

    private static (Checkout Checkout, CarritoDeDominio Carrito) EnPago()
    {
        var (checkout, carrito) = ADomicilio();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, carrito, Ahora);
        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Ahora.AddMinutes(15), carrito);
        return (checkout, carrito);
    }

    private static void ExigirSinContacto(Checkout checkout)
    {
        Assert.Null(checkout.Contacto);
        Assert.Null(checkout.HuellaDeInstantanea);
        Assert.True(checkout.CumpleLaRetencionDelContacto);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Un_checkout_a_domicilio_vivo_conserva_el_contacto_y_cumple_la_regla()
    {
        var (checkout, _) = EnPago();

        Assert.NotNull(checkout.Contacto);
        Assert.NotNull(checkout.HuellaDeInstantanea);
        Assert.True(checkout.CumpleLaRetencionDelContacto);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Cuando_pagos_informa_que_el_pago_se_aprobo_el_contacto_deja_de_existir_desde_en_pago_y_desde_pago_pendiente()
    {
        var (desdeEnPago, carritoE) = EnPago();
        Assert.Equal(EfectoDeTransicion.Aplicada, desdeEnPago.PagoAprobado(desdeEnPago.PedidoId!.Value, carritoE, Ahora.AddMinutes(5)).Efecto);
        ExigirSinContacto(desdeEnPago);

        var (desdePendiente, carritoP) = EnPago();
        desdePendiente.PagoPendiente(desdePendiente.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1));
        Assert.NotNull(desdePendiente.Contacto);
        desdePendiente.PagoAprobado(desdePendiente.PedidoId!.Value, carritoP, Ahora.AddMinutes(5));
        ExigirSinContacto(desdePendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Cuando_el_cliente_cancela_o_reinicia_el_checkout_el_contacto_deja_de_existir()
    {
        var (cancelado, carritoC) = EnPago();
        cancelado.Volver(carritoC, Ahora.AddMinutes(1));
        ExigirSinContacto(cancelado);

        var (reiniciado, carritoR) = EnPago();
        reiniciado.Reiniciar(carritoR, Ahora.AddMinutes(1));
        ExigirSinContacto(reiniciado);

        var (enEdicion, carritoE) = ADomicilio();
        enEdicion.Volver(carritoE, Ahora.AddMinutes(1));
        ExigirSinContacto(enEdicion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Un_pago_compensado_o_resuelto_con_la_reserva_expirada_cancela_el_checkout_sin_contacto()
    {
        var (compensado, carritoC) = EnPago();
        compensado.PagoResuelto(compensado.PedidoId!.Value, MotivoDePagoResuelto.Compensado, carritoC, Ahora.AddMinutes(1));
        ExigirSinContacto(compensado);

        var (expirado, carritoX) = EnPago();
        expirado.PagoPendiente(expirado.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1));
        expirado.PagoResuelto(expirado.PedidoId!.Value, MotivoDePagoResuelto.Vencido, carritoX, Ahora.AddMinutes(31));
        Assert.Equal(EstadoDelCheckout.Cancelado, expirado.Estado);
        ExigirSinContacto(expirado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Al_cambiar_a_recojo_o_quedar_solo_digital_el_contacto_deja_de_existir_y_la_huella_se_anula()
    {
        var (aRecojo, _) = ADomicilio();
        aRecojo.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, CarritoCon(Impreso(1), Digital(2)), Ahora);
        aRecojo.AbortarPasoAlPago(EstadoDelCheckout.Reservando, aRecojo.PedidoId!.Value, aRecojo.GeneracionDeReserva, CarritoCon(Impreso(1), Digital(2)), Ahora);
        Assert.NotNull(aRecojo.Contacto);
        Assert.NotNull(aRecojo.HuellaDeInstantanea);

        aRecojo.ElegirRecojo(Ahora.AddMinutes(1));
        ExigirSinContacto(aRecojo);
        Assert.Equal(TipoDeEntrega.Recojo, aRecojo.TipoDeEntrega);

        var (soloDigital, _) = ADomicilio();
        soloDigital.SincronizarLineas([Digital(2)], Guid.NewGuid, Ahora.AddMinutes(1));
        ExigirSinContacto(soloDigital);
        Assert.Equal(TipoDeEntrega.Digital, soloDigital.TipoDeEntrega);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-30")]
    [InlineData(MotivoDeEliminacionDeContacto.CuentaCancelada)]
    [InlineData(MotivoDeEliminacionDeContacto.CuentaReemplazada)]
    public void Al_cancelar_o_reemplazar_la_cuenta_el_contacto_se_elimina_en_cualquier_estado_vivo(MotivoDeEliminacionDeContacto motivo)
    {
        var (enEdicion, _) = ADomicilio();
        var (enPago, carritoE) = EnPago();
        var (pendiente, _) = EnPago();
        pendiente.PagoPendiente(pendiente.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1));
        var (reservando, _) = ADomicilio();
        reservando.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, carritoE, Ahora);
        Assert.Equal(EstadoDelCheckout.Reservando, reservando.Estado);

        foreach (var checkout in new[] { enEdicion, enPago, pendiente, reservando })
        {
            var resultado = checkout.EliminarContacto(motivo, Ahora.AddMinutes(2));

            Assert.Equal(EfectoDeTransicion.Aplicada, resultado.Efecto);
            ExigirSinContacto(checkout);
        }
    }

    [Fact]
    public void Eliminar_el_contacto_de_un_checkout_sin_contacto_no_cambia_nada()
    {
        var (checkout, carrito) = ADomicilio();
        checkout.Volver(carrito, Ahora);
        var version = checkout.Version;

        var resultado = checkout.EliminarContacto(MotivoDeEliminacionDeContacto.CuentaCancelada, Ahora.AddMinutes(1));

        Assert.Equal(EfectoDeTransicion.SinCambios, resultado.Efecto);
        Assert.Equal(version, checkout.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void A_los_30_dias_de_la_ultima_modificacion_el_contacto_vence_y_a_29_dias_y_23_horas_no()
    {
        var (checkout, _) = ADomicilio();

        Assert.False(checkout.ContactoVencido(Ahora.AddDays(29).AddHours(23)));
        Assert.True(checkout.ContactoVencido(Ahora.AddDays(30)));
        Assert.True(checkout.ContactoVencido(Ahora.AddDays(45)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public void Eliminar_por_vencimiento_quita_el_contacto_deja_la_entrega_por_elegir_y_anula_la_huella()
    {
        var (checkout, _) = ADomicilio();

        var antes = checkout.EliminarContacto(MotivoDeEliminacionDeContacto.Vencido, Ahora.AddDays(29).AddHours(23));
        var despues = checkout.EliminarContacto(MotivoDeEliminacionDeContacto.Vencido, Ahora.AddDays(30));

        Assert.Equal(EfectoDeTransicion.NoAplica, antes.Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, despues.Efecto);
        ExigirSinContacto(checkout);
        Assert.Null(checkout.TipoDeEntrega);
        Assert.Null(checkout.RegionDeEntrega);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
    }

    [Fact]
    public void El_vencimiento_cuenta_desde_la_ultima_modificacion_de_la_persona_y_no_desde_lo_que_informa_pagos_ni_los_trabajos()
    {
        var (checkout, _) = ADomicilio();
        checkout.Aceptar(TextoLegal.Terminos, "t-2", Ahora.AddDays(20));
        Assert.False(checkout.ContactoVencido(Ahora.AddDays(49)));
        Assert.True(checkout.ContactoVencido(Ahora.AddDays(50)));

        checkout.RegistrarLoInformado(checkout.Lineas.Single(l => l.EdicionId == Edicion(1)).LineaId, "lima", requiereTraslado: false, plazoDias: null, Ahora.AddDays(40));
        checkout.RevalidarTextos(new Dictionary<TextoLegal, string>(), Ahora.AddDays(41));
        Assert.True(checkout.ContactoVencido(Ahora.AddDays(50)));
    }

    [Fact]
    public void Un_contacto_vencido_en_un_checkout_con_el_pago_en_marcha_no_se_elimina_por_vencimiento()
    {
        var (checkout, _) = EnPago();

        var resultado = checkout.EliminarContacto(MotivoDeEliminacionDeContacto.Vencido, Ahora.AddDays(40));

        Assert.Equal(EfectoDeTransicion.NoAplica, resultado.Efecto);
        Assert.NotNull(checkout.Contacto);
    }

    [Fact]
    public void Abortar_el_paso_al_pago_no_elimina_el_contacto_porque_el_checkout_sigue_vivo_y_a_domicilio()
    {
        var (checkout, carrito) = ADomicilio();
        checkout.PrepararPasoAlPago(Guid.NewGuid(), Envio, Identidad, Identidad, carrito, Ahora);

        checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, checkout.PedidoId!.Value, checkout.GeneracionDeReserva, carrito, Ahora.AddMinutes(3));

        Assert.NotNull(checkout.Contacto);
        Assert.True(checkout.CumpleLaRetencionDelContacto);
    }

    [Fact]
    public void Un_checkout_con_contacto_fuera_de_un_estado_vivo_a_domicilio_no_cumple_la_regla_pero_ninguna_transicion_lo_deja_asi()
    {
        var (checkout, carrito) = EnPago();
        var estados = new List<EstadoDelCheckout> { checkout.Estado };

        checkout.PagoPendiente(checkout.PedidoId!.Value, Ahora.AddMinutes(31), Ahora.AddMinutes(30), Ahora.AddMinutes(1));
        estados.Add(checkout.Estado);
        Assert.True(checkout.CumpleLaRetencionDelContacto);

        checkout.PagoResuelto(checkout.PedidoId!.Value, MotivoDePagoResuelto.Rechazado, carrito, Ahora.AddMinutes(10));
        estados.Add(checkout.Estado);
        Assert.True(checkout.CumpleLaRetencionDelContacto);

        checkout.Editar(carrito, Ahora.AddMinutes(11));
        estados.Add(checkout.Estado);
        Assert.True(checkout.CumpleLaRetencionDelContacto);

        checkout.Reiniciar(carrito, Ahora.AddMinutes(12));
        estados.Add(checkout.Estado);
        Assert.True(checkout.CumpleLaRetencionDelContacto);
        Assert.Equal([EstadoDelCheckout.EnPago, EstadoDelCheckout.PagoPendiente, EstadoDelCheckout.EnPago, EstadoDelCheckout.EnEdicion, EstadoDelCheckout.Reiniciado], estados);
    }
}
