using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T12: <c>FusionarCarritoDeVisitante</c> (CAR-24, AC-CAR-22): idempotente, con la disponibilidad y la posesión obtenidas antes de la transacción, los dos carritos bloqueados en orden fijo, el del visitante
/// <c>fusionado</c> con su constancia, avisos, cookie borrada y auditoría sin identificar al visitante; se aplaza con una compra en curso y no impide el ingreso.
/// </summary>
public sealed class FusionDelCarritoTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly string CookieDelVisitante = new('A', 43);

    private static byte[] Huella()
    {
        Assert.True(HuellaDeLaCookie.TryDe(CookieDelVisitante, out var huella));
        return huella!;
    }

    private static (EntornoDelCarrito Entorno, Guid Cuenta) Escenario()
    {
        var e = EntornoDelCarrito.DeCliente();
        return (e, e.Actor.Actual!.UsuarioId!.Value);
    }

    private static CarritoDeDominio SembrarVisitante(EntornoDelCarrito e, params (Guid Edicion, bool Digital, int Cantidad)[] lineas)
    {
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(Huella()), Inicio);
        foreach (var (edicion, digital, cantidad) in lineas)
        {
            carrito.Agregar(edicion, digital, cantidad, Inicio);
        }

        e.Carritos.Sembrar(carrito);
        return carrito;
    }

    private static CarritoDeDominio SembrarCuenta(EntornoDelCarrito e, Guid cuenta, params (Guid Edicion, bool Digital, int Cantidad)[] lineas)
    {
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        foreach (var (edicion, digital, cantidad) in lineas)
        {
            carrito.Agregar(edicion, digital, cantidad, Inicio);
        }

        e.Carritos.Sembrar(carrito);
        return carrito;
    }

    private static Task<Resultado<ResultadoDeFusionDeCarrito>> Fusionar(EntornoDelCarrito e) => FusionarCon(e, CookieDelVisitante);

    private static Task<Resultado<ResultadoDeFusionDeCarrito>> FusionarCon(EntornoDelCarrito e, string? cookie) =>
        new FusionarCarritoDeVisitante(e.Servicios()).Ejecutar(new FusionarCarritoEntrada(cookie));

    // ---------- La fusión ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task La_fusion_suma_lo_del_visitante_a_lo_de_la_cuenta_marca_el_del_visitante_y_borra_la_cookie()
    {
        var (e, cuenta) = Escenario();
        var a = e.Impreso(10);
        var b = e.Impreso(10);
        var c = e.Digital();
        var delaCuenta = SembrarCuenta(e, cuenta, (a, false, 2));
        var delVisitante = SembrarVisitante(e, (a, false, 3), (b, false, 1), (c, true, 1));

        var resultado = await Fusionar(e);

        Assert.True(resultado.EsExito);
        Assert.Equal([a, b, c], delaCuenta.Lineas.Select(l => l.EdicionId));
        Assert.Equal([5, 1, 1], delaCuenta.Lineas.Select(l => l.Cantidad.Valor));
        Assert.Equal(EstadoDeCarrito.Fusionado, delVisitante.Estado);
        Assert.True(resultado.Valor.BorrarLaCookie);
        Assert.False(resultado.Valor.Aplazada);
        Assert.Equal(3, resultado.Valor.Carrito.Lineas.Count);
        var constancia = Assert.Single(e.Carritos.Fusiones);
        Assert.Equal(delaCuenta.Id, constancia.CarritoId);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task Sin_carrito_de_cuenta_se_crea_con_las_lineas_del_visitante()
    {
        var (e, cuenta) = Escenario();
        var a = e.Impreso(10);
        SembrarVisitante(e, (a, false, 2));

        var resultado = await Fusionar(e);

        Assert.True(resultado.EsExito);
        var deLaCuenta = e.Carritos.Todos.Single(c => c.Propietario.CuentaId == cuenta);
        Assert.Equal(2, deLaCuenta.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task Repetir_la_fusion_no_duplica_nada_y_un_segundo_dispositivo_con_la_misma_cookie_no_suma_otra_vez()
    {
        var (e, cuenta) = Escenario();
        var a = e.Impreso(10);
        var delaCuenta = SembrarCuenta(e, cuenta, (a, false, 1));
        SembrarVisitante(e, (a, false, 2));

        await Fusionar(e);
        var guardados = e.Carritos.Guardados;
        for (var i = 0; i < 19; i++)
        {
            var repetida = await Fusionar(e);
            Assert.True(repetida.EsExito);
            Assert.True(repetida.Valor.BorrarLaCookie);
        }

        Assert.Equal(3, delaCuenta.Lineas.Single().Cantidad.Valor);
        Assert.Equal(guardados, e.Carritos.Guardados);
        Assert.Single(e.Carritos.Fusiones);
        Assert.Single(e.Auditoria.Fusiones);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Una_cookie_mal_formada_o_ausente_no_hay_nada_que_fusionar_y_la_cookie_se_borra(string? cookie)
    {
        var (e, cuenta) = Escenario();
        SembrarCuenta(e, cuenta, (e.Impreso(5), false, 1));

        var resultado = await FusionarCon(e, cookie);

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.BorrarLaCookie);
        Assert.Single(resultado.Valor.Carrito.Lineas);
        Assert.Empty(e.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task Una_cookie_desconocida_o_de_un_carrito_caducado_no_fusiona_nada()
    {
        var (e, cuenta) = Escenario();
        var delaCuenta = SembrarCuenta(e, cuenta, (e.Impreso(5), false, 1));
        var visitante = SembrarVisitante(e, (e.Impreso(5), false, 4));
        e.Reloj.Ahora = Inicio.AddDays(31);

        var resultado = await Fusionar(e);

        Assert.True(resultado.Valor.BorrarLaCookie);
        Assert.Equal(1, delaCuenta.Lineas.Single().Cantidad.Valor);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
    }

    [Fact]
    public async Task Un_visitante_sin_lineas_se_marca_fusionado_sin_tocar_a_la_cuenta()
    {
        var (e, cuenta) = Escenario();
        var delaCuenta = SembrarCuenta(e, cuenta, (e.Impreso(5), false, 1));
        var visitante = SembrarVisitante(e);

        var resultado = await Fusionar(e);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeCarrito.Fusionado, visitante.Estado);
        Assert.Equal(1, delaCuenta.Lineas.Single().Cantidad.Valor);
    }

    // ---------- Los ajustes ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task Los_ajustes_dejan_un_aviso_con_su_motivo_y_se_auditan_sin_identificar_al_visitante()
    {
        var (e, cuenta) = Escenario();
        var poca = e.Impreso(3, titulo: "Poca");
        var agotada = e.Impreso(0, titulo: "Agotada");
        var poseida = e.Digital(titulo: "Poseída");
        e.Posesion.Poseer(poseida);
        var retirada = Guid.NewGuid();
        e.Catalogo.Poner(retirada, FormatoDeEdicion.Impresa, 10m, "Retirada", EstadoDeEdicionConsultado.RetiradaDeVenta, MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta);
        SembrarCuenta(e, cuenta, (poca, false, 2));
        SembrarVisitante(e, (poca, false, 5), (agotada, false, 1), (poseida, true, 1), (retirada, false, 1));

        var resultado = await Fusionar(e);

        Assert.True(resultado.EsExito);
        var porEdicion = resultado.Valor.Ajustes.ToDictionary(a => a.EdicionId);
        Assert.Equal(MotivoDeAjusteDeAviso.RecortadaAlMaximo, porEdicion[poca].Motivo);
        Assert.Equal(3, porEdicion[poca].CantidadFinal);
        Assert.Equal(MotivoDeAjusteDeAviso.SinStock, porEdicion[agotada].Motivo);
        Assert.Equal(MotivoDeAjusteDeAviso.YaPoseida, porEdicion[poseida].Motivo);
        Assert.Equal(MotivoDeAjusteDeAviso.NoVendible, porEdicion[retirada].Motivo);
        Assert.All(resultado.Valor.Ajustes, a => Assert.Equal(TipoDeAvisoDelCarrito.FusionAjustada, a.Tipo));
        Assert.Equal(4, resultado.Valor.Carrito.Avisos.Count);
        var auditado = Assert.Single(e.Auditoria.Fusiones);
        Assert.Equal(cuenta, auditado.CuentaId);
        Assert.Equal(4, auditado.LineasDelVisitante);
        Assert.Equal(4, auditado.Ajustes.Count);
    }

    [Fact]
    public async Task Si_Biblioteca_no_responde_la_digital_se_acepta_y_se_comprobara_al_pagar()
    {
        var (e, cuenta) = Escenario();
        var digital = e.Digital();
        e.Posesion.NoResponde = true;
        var delaCuenta = SembrarCuenta(e, cuenta);
        SembrarVisitante(e, (digital, true, 1));

        var resultado = await Fusionar(e);

        Assert.True(resultado.EsExito);
        Assert.Equal([digital], delaCuenta.Lineas.Select(l => l.EdicionId));
    }

    [Fact]
    public async Task Si_el_Catalogo_no_responde_no_se_fusiona_y_el_carrito_del_visitante_sigue()
    {
        var (e, cuenta) = Escenario();
        var delaCuenta = SembrarCuenta(e, cuenta, (e.Impreso(5), false, 1));
        var visitante = SembrarVisitante(e, (e.Impreso(5), false, 1));
        e.Catalogo.Caido = true;

        var resultado = await Fusionar(e);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
        Assert.Single(delaCuenta.Lineas);
    }

    [Fact]
    public async Task Si_la_auditoria_falla_la_operacion_falla_para_que_la_transaccion_se_revierta()
    {
        var (e, cuenta) = Escenario();
        SembrarCuenta(e, cuenta, (e.Impreso(5), false, 1));
        SembrarVisitante(e, (e.Impreso(5), false, 1));
        e.Auditoria.Caida = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Fusionar(e));
    }

    // ---------- Orden de bloqueo ----------

    [Fact]
    public async Task Los_dos_carritos_se_bloquean_por_id_ascendente_despues_del_checkout()
    {
        var (e, cuenta) = Escenario();
        var a = e.Impreso(5);
        var delaCuenta = SembrarCuenta(e, cuenta, (a, false, 1));
        var visitante = SembrarVisitante(e, (a, false, 1));
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, delaCuenta.Id, [new LineaParaCheckout(a, false, "Libro", "9780000000019", 1, 50m)], Guid.NewGuid, Inicio);
        e.Checkouts.Sembrar(checkout);

        await Fusionar(e);

        var esperados = new[] { delaCuenta.Id, visitante.Id }.Order().Select(id => $"carrito:{id}");
        Assert.Equal(["checkout", .. esperados], e.Bitacora.Bloqueos);
    }

    // ---------- Compra en curso ----------

    private static (EntornoDelCarrito Entorno, Checkout Checkout, CarritoDeDominio DeLaCuenta, CarritoDeDominio DelVisitante) ConCheckout(Action<Checkout, CarritoDeDominio>? llevar = null)
    {
        var (e, cuenta) = Escenario();
        var a = e.Impreso(10);
        var delaCuenta = SembrarCuenta(e, cuenta, (a, false, 1));
        var visitante = SembrarVisitante(e, (a, false, 2));
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, delaCuenta.Id, [new LineaParaCheckout(a, false, "Libro", "9780000000019", 1, 50m)], Guid.NewGuid, Inicio);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio);
        checkout.ElegirRecojo("lima", Inicio);
        llevar?.Invoke(checkout, delaCuenta);
        e.Checkouts.Sembrar(checkout);
        return (e, checkout, delaCuenta, visitante);
    }

    private static void AReservando(Checkout c, CarritoDeDominio k) =>
        Assert.Equal(EfectoDeTransicion.Aplicada, c.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, k, Inicio).Efecto);

    private static void AEnPago(Checkout c, CarritoDeDominio k)
    {
        AReservando(c, k);
        Assert.Equal(EfectoDeTransicion.Aplicada, c.ConfirmarEnPago(c.PedidoId!.Value, c.GeneracionDeReserva, c.HuellaDeInstantanea!, Inicio.AddMinutes(15), k).Efecto);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task Con_el_pago_pendiente_la_fusion_se_aplaza_y_el_carrito_del_visitante_y_su_cookie_siguen()
    {
        var (e, checkout, delaCuenta, visitante) = ConCheckout((c, k) =>
        {
            AEnPago(c, k);
            c.PagoPendiente(c.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);
        });

        var resultado = await Fusionar(e);

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.Aplazada);
        Assert.False(resultado.Valor.BorrarLaCookie);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
        Assert.Equal(1, delaCuenta.Lineas.Single().Cantidad.Valor);
        Assert.Equal(EstadoDelCheckout.PagoPendiente, checkout.Estado);
        Assert.Equal(0, e.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Con_el_checkout_reservando_la_fusion_se_aplaza()
    {
        var (e, _, delaCuenta, visitante) = ConCheckout(AReservando);

        var resultado = await Fusionar(e);

        Assert.True(resultado.Valor.Aplazada);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
        Assert.Equal(1, delaCuenta.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Con_en_pago_y_un_intento_activo_Reabrir_responde_no_cancelable_y_la_fusion_se_aplaza()
    {
        var (e, checkout, _, visitante) = ConCheckout(AEnPago);
        e.Pagos.AlReabrir = ResultadoDeReabrirIntencion.NoCancelable;

        var resultado = await Fusionar(e);

        Assert.True(resultado.Valor.Aplazada);
        Assert.False(resultado.Valor.BorrarLaCookie);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
    }

    [Fact]
    public async Task Con_en_pago_sin_intento_activo_se_reabre_y_la_fusion_se_aplica_dejando_el_checkout_en_edicion()
    {
        var (e, checkout, delaCuenta, visitante) = ConCheckout(AEnPago);

        var resultado = await Fusionar(e);

        Assert.False(resultado.Valor.Aplazada);
        Assert.Equal(1, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(EstadoDeCarrito.Fusionado, visitante.Estado);
        Assert.Equal(3, delaCuenta.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Si_Pagos_no_responde_a_Reabrir_la_fusion_falla_y_el_carrito_del_visitante_sigue()
    {
        var (e, _, _, visitante) = ConCheckout(AEnPago);
        e.Pagos.NoResponde = true;

        var resultado = await Fusionar(e);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
    }

    // ---------- Alcance ----------

    [Fact]
    public async Task Sin_sesion_no_hay_fusion_y_el_personal_se_rechaza()
    {
        var anonimo = EntornoDelCarrito.DeVisitante();
        var encargado = new EntornoDelCarrito(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.NoAutenticado, (await Fusionar(anonimo)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Fusionar(encargado)).Error!.Categoria);
    }

    [Fact]
    public async Task Una_cuenta_cancelada_no_fusiona_y_el_visitante_conserva_su_carrito()
    {
        var (e, cuenta) = Escenario();
        SembrarCuenta(e, cuenta);
        var visitante = SembrarVisitante(e, (e.Impreso(5), false, 1));
        e.Cuenta.Cancelada = true;

        var resultado = await Fusionar(e);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
    }
}
