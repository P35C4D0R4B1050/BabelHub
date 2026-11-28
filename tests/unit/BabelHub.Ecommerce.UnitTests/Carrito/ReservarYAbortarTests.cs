using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T18: el paso 2 de pasar al pago, <c>Reservar</c> fuera de toda transacción solo para los impresos (D18; AC-CAR-14, AC-CAR-24; CAR-17, CAR-27), sus traducciones del Inventario (DD-03, DD-04) y el aborto condicional
/// <c>AbortarPasoAlPago</c> que reutilizan T19, T23 y el aborto desde <c>en_pago</c>.
/// </summary>
public sealed class ReservarYAbortarTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private sealed record Contexto(EntornoDelCarrito Entorno, Checkout Checkout, Guid Impreso, PasoAlPagoPreparado Preparado);

    /// <summary>Un checkout ya preparado (reservando) con un impreso de 3 unidades y recojo, a la espera del paso 2.</summary>
    private static async Task<Contexto> Preparado(bool soloDigital = false)
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = soloDigital ? Guid.Empty : e.Impreso(10, 40m, "Impreso");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(soloDigital ? e.Digital(20m) : impreso, soloDigital ? 1 : 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        if (!soloDigital)
        {
            e.Inventario.FijarFuente(impreso, "sede-central", false, null);
            await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo));
        }

        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(soloDigital ? [.. Los2, new TextoVistoDeCompra(TextoLegalDeCompra.ReembolsoDigital, "r-1")] : Los2));
        var checkout = e.Checkouts.Todos.Single();
        var total = soloDigital ? 20m : 120m;
        var preparado = await new PrepararPasoAlPago(e.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(total, soloDigital ? [.. Los2, new TextoVistoDeCompra(TextoLegalDeCompra.ReembolsoDigital, "r-1")] : Los2));
        Assert.True(preparado.EsExito, preparado.Error?.Clave);
        return new Contexto(e, checkout, impreso, preparado.Valor);
    }

    private static ReservaObtenida Reserva(Contexto c, DateTimeOffset? expira = null, string fuente = "sede-central", bool traslado = false, int? plazo = null, bool repeticion = false) =>
        new(c.Preparado.PedidoId!.Value, expira ?? Inicio.AddMinutes(15), [.. c.Preparado.LineasImpresas.Select(l => new SeleccionDeLinea(l.LineaId, l.EdicionId, true, fuente, traslado, plazo, 10))], repeticion);

    private static Task<ResultadoDelPasoDeReserva> Reservar(Contexto c) => new ReservarImpresos(c.Entorno.ServiciosDelCheckout()).Ejecutar(c.Preparado);

    // ---------- Lo que se reserva ----------

    [Fact]
    public async Task Se_reserva_con_la_clave_de_la_generacion_las_lineas_impresas_y_la_entrega_y_se_devuelve_la_expiracion()
    {
        var c = await Preparado();
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.Reservada(Reserva(c));

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.Reservada, resultado.Tipo);
        Assert.Equal(Inicio.AddMinutes(15), resultado.ExpiraEn);
        var (pedido, generacion, lineas) = Assert.Single(c.Entorno.Inventario.Reservas);
        Assert.Equal((c.Preparado.PedidoId, c.Preparado.Generacion), (pedido, generacion));
        Assert.Equal(c.Impreso, Assert.Single(lineas).EdicionId);
    }

    // ---------- Traducciones ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-24")]
    public async Task Falta_de_stock_aborta_y_vuelve_a_la_revision_con_las_lineas_y_el_carrito_conservado()
    {
        var c = await Preparado();
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.Sin([new LineaSinStock(c.Preparado.LineasImpresas[0].LineaId, c.Impreso, 3, 2)]);

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.Abortar, resultado.Tipo);
        Assert.Equal(ClavesDelCarrito.ReservaRechazada, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error.Categoria);
        Assert.Contains(new ErrorDeCampo("edicionId", c.Impreso.ToString("D")), resultado.Error.Errores);
        Assert.Contains(new ErrorDeCampo("maximo", "2"), resultado.Error.Errores);
        Assert.Equal("falta-de-stock", resultado.MotivoDelAborto);
    }

    [Fact]
    public async Task INV_IDEMPOTENCIA_EN_CURSO_responde_en_curso_sin_abortar()
    {
        var c = await Preparado();
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.EnCurso, "INV-IDEMPOTENCIA-EN-CURSO");

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.NoAbortar, resultado.Tipo);
        Assert.Equal(ClavesDelCarrito.EnCurso, resultado.Error!.Clave);
    }

    [Fact]
    public async Task INV_IDEMPOTENCIA_EN_CONFLICTO_es_una_clave_mal_derivada_y_aborta_como_servicio_no_disponible()
    {
        var c = await Preparado();
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.ConflictoDeIdempotencia, "INV-IDEMPOTENCIA-EN-CONFLICTO");

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.Abortar, resultado.Tipo);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("clave-de-reserva-en-conflicto", resultado.MotivoDelAborto);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task El_limite_responde_con_el_instante_de_reintento_y_no_aborta_porque_el_inventario_conserva_la_reserva_vigente()
    {
        var c = await Preparado();
        var desde = Inicio.AddMinutes(9);
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.LimiteExcedido, "INV-LIMITE-EXCEDIDO", desde);

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.VolverALaRevisionPorLimite, resultado.Tipo);
        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("disponibleDesde", desde.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), resultado.Error.Errores);
    }

    [Fact]
    public async Task Una_reserva_ya_expirada_o_un_estado_invalido_abortan_para_empezar_con_un_pedido_nuevo()
    {
        var expirada = await Preparado();
        expirada.Entorno.Inventario.AlReservar = ResultadoDeReservar.Reservada(Reserva(expirada, expira: Inicio.AddSeconds(-1), repeticion: true));
        var invalida = await Preparado();
        invalida.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(TipoDeResultadoDeReservar.EstadoInvalido, "INV-RESERVA-ESTADO-INVALIDO");

        var a = await Reservar(expirada);
        var b = await Reservar(invalida);

        Assert.Equal((TipoDeResultadoDelPaso.Abortar, "reserva-expirada"), (a.Tipo, a.MotivoDelAborto));
        Assert.Equal((TipoDeResultadoDelPaso.Abortar, "reserva-expirada"), (b.Tipo, b.MotivoDelAborto));
        Assert.Equal(ClavesDelCarrito.ReservaRechazada, a.Error!.Clave);
    }

    [Theory]
    [InlineData(TipoDeResultadoDeReservar.NoDisponible, "sin-respuesta", CategoriaDeError.NoDisponible)]
    [InlineData(TipoDeResultadoDeReservar.Rechazada, "entrega-invalida", CategoriaDeError.Conflicto)]
    public async Task Una_falla_del_paso_dos_aborta_de_inmediato_tambien_si_la_reserva_pudo_crearse_antes_del_corte(TipoDeResultadoDeReservar tipo, string motivo, CategoriaDeError categoria)
    {
        var c = await Preparado();
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.De(tipo, "INV-VALIDACION-INVALIDA");

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.Abortar, resultado.Tipo);
        Assert.Equal(motivo, resultado.MotivoDelAborto);
        Assert.Equal(categoria, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-24")]
    public async Task Un_cambio_de_plazo_respecto_de_lo_informado_aborta_con_el_plazo_nuevo_y_se_guarda_como_lo_nuevo_informado()
    {
        var c = await Preparado();
        c.Entorno.Inventario.AlReservar = ResultadoDeReservar.Reservada(Reserva(c, traslado: true, plazo: 5));

        var resultado = await Reservar(c);

        Assert.Equal(TipoDeResultadoDelPaso.Abortar, resultado.Tipo);
        Assert.Equal("plazo-cambiado", resultado.MotivoDelAborto);
        Assert.Contains(new ErrorDeCampo("plazoDias", "5"), resultado.Error!.Errores);
        Assert.Equal(c.Checkout.Lineas.Single(l => l.EdicionId == c.Impreso).LineaId, resultado.NuevoInformado!.Single().LineaId);
    }

    // ---------- El aborto ----------

    private static AbortarElPasoAlPago Abortar(Contexto c) => new(c.Entorno.ServiciosDelCheckout());

    [Fact]
    public async Task Desde_reservando_primero_cambia_lo_local_descarta_el_pedido_sube_la_generacion_anota_la_liberacion_y_despues_libera()
    {
        var c = await Preparado();
        var pedido = c.Preparado.PedidoId!.Value;

        var resultado = await Abortar(c).Ejecutar(new SolicitudDeAborto(c.Preparado.CuentaId, EstadoDelCheckout.Reservando, pedido, c.Preparado.Generacion, IntencionCreada: false, "falta-de-stock", null));

        Assert.Equal(DesenlaceDelAborto.Abortado, resultado);
        Assert.Equal(EstadoDelCheckout.EnEdicion, c.Checkout.Estado);
        Assert.Null(c.Checkout.PedidoId);
        Assert.Equal(pedido, c.Checkout.PedidoDescartado);
        Assert.Equal(c.Preparado.Generacion + 1, c.Checkout.GeneracionDeReserva);
        Assert.Equal((pedido, "checkout-cancelado"), Assert.Single(c.Entorno.Inventario.Liberaciones));
        Assert.Null(c.Checkout.ReservaALiberarDesde);
        Assert.Equal(0, c.Entorno.Pagos.Cancelaciones);
    }

    [Fact]
    public async Task Con_la_intencion_creada_se_cancela_despues_del_cambio_local_y_si_Pagos_no_responde_igual_queda_abortado()
    {
        var c = await Preparado();
        c.Entorno.Pagos.NoResponde = true;

        var resultado = await Abortar(c).Ejecutar(new SolicitudDeAborto(c.Preparado.CuentaId, EstadoDelCheckout.Reservando, c.Preparado.PedidoId!.Value, c.Preparado.Generacion, IntencionCreada: true, "falta-de-stock", null));

        Assert.Equal(DesenlaceDelAborto.Abortado, resultado);
        Assert.Equal(EstadoDelCheckout.EnEdicion, c.Checkout.Estado);
        Assert.Equal(1, c.Entorno.Pagos.Cancelaciones);
    }

    [Fact]
    public async Task El_aborto_es_condicional_si_el_checkout_ya_cambio_de_pedido_o_de_generacion_no_hace_nada_ni_libera()
    {
        var c = await Preparado();

        var otroPedido = await Abortar(c).Ejecutar(new SolicitudDeAborto(c.Preparado.CuentaId, EstadoDelCheckout.Reservando, Guid.NewGuid(), c.Preparado.Generacion, false, "x", null));
        var otraGeneracion = await Abortar(c).Ejecutar(new SolicitudDeAborto(c.Preparado.CuentaId, EstadoDelCheckout.Reservando, c.Preparado.PedidoId!.Value, c.Preparado.Generacion + 1, false, "x", null));

        Assert.Equal(DesenlaceDelAborto.NoAplica, otroPedido);
        Assert.Equal(DesenlaceDelAborto.NoAplica, otraGeneracion);
        Assert.Equal(EstadoDelCheckout.Reservando, c.Checkout.Estado);
        Assert.Empty(c.Entorno.Inventario.Liberaciones);
    }

    [Fact]
    public async Task Una_liberacion_que_no_se_pudo_queda_anotada_para_el_trabajo_de_reintento_y_un_pedido_ya_confirmado_se_limpia_sin_reintento()
    {
        var sinRespuesta = await Preparado();
        sinRespuesta.Entorno.Inventario.AlLiberar = ResultadoDeLiberar.NoSePudo;
        var confirmado = await Preparado();
        confirmado.Entorno.Inventario.AlLiberar = ResultadoDeLiberar.PedidoConfirmado;
        var inexistente = await Preparado();
        inexistente.Entorno.Inventario.AlLiberar = ResultadoDeLiberar.NoExistia;

        foreach (var c in new[] { sinRespuesta, confirmado, inexistente })
        {
            await Abortar(c).Ejecutar(new SolicitudDeAborto(c.Preparado.CuentaId, EstadoDelCheckout.Reservando, c.Preparado.PedidoId!.Value, c.Preparado.Generacion, false, "x", null));
        }

        Assert.NotNull(sinRespuesta.Checkout.ReservaALiberarDesde);
        Assert.Null(confirmado.Checkout.ReservaALiberarDesde);
        Assert.Null(inexistente.Checkout.ReservaALiberarDesde);
    }

    // ---------- El aborto desde en_pago ----------

    private static async Task<Contexto> EnPago()
    {
        var c = await Preparado();
        var carrito = c.Entorno.Carritos.Todos.Single();
        c.Checkout.ConfirmarEnPago(c.Preparado.PedidoId!.Value, c.Preparado.Generacion, c.Preparado.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
        return c;
    }

    [Theory]
    [InlineData(ResultadoDeCancelarIntencion.Cancelada)]
    [InlineData(ResultadoDeCancelarIntencion.Cerrada)]
    [InlineData(ResultadoDeCancelarIntencion.NoExiste)]
    public async Task Desde_en_pago_llama_primero_a_Cancelar_y_solo_con_una_respuesta_final_hace_el_cambio_local(ResultadoDeCancelarIntencion respuesta)
    {
        var c = await EnPago();
        c.Entorno.Pagos.AlCancelar = respuesta;
        c.Entorno.Bitacora.Bloqueos.Clear();

        var resultado = await Abortar(c).Ejecutar(new SolicitudDeAborto(c.Preparado.CuentaId, EstadoDelCheckout.EnPago, c.Preparado.PedidoId!.Value, c.Preparado.Generacion, true, "reserva-expirada", null));

        Assert.Equal(DesenlaceDelAborto.Abortado, resultado);
        Assert.Equal(EstadoDelCheckout.EnEdicion, c.Checkout.Estado);
        Assert.Equal("cancelar", c.Entorno.Bitacora.Bloqueos[0]);
        Assert.NotNull(c.Checkout.PedidoDescartado);
    }

    [Fact]
    public async Task Desde_en_pago_con_un_cobro_vivo_no_aborta_ni_libera_y_si_Pagos_no_responde_tampoco_hay_cambio_local()
    {
        var vivo = await EnPago();
        vivo.Entorno.Pagos.AlCancelar = ResultadoDeCancelarIntencion.NoCancelable;
        var mudo = await EnPago();
        mudo.Entorno.Pagos.NoResponde = true;

        var a = await Abortar(vivo).Ejecutar(new SolicitudDeAborto(vivo.Preparado.CuentaId, EstadoDelCheckout.EnPago, vivo.Preparado.PedidoId!.Value, vivo.Preparado.Generacion, true, "x", null));
        var b = await Abortar(mudo).Ejecutar(new SolicitudDeAborto(mudo.Preparado.CuentaId, EstadoDelCheckout.EnPago, mudo.Preparado.PedidoId!.Value, mudo.Preparado.Generacion, true, "x", null));

        Assert.Equal(DesenlaceDelAborto.NoCancelable, a);
        Assert.Equal(DesenlaceDelAborto.PagosNoRespondio, b);
        Assert.Equal(EstadoDelCheckout.EnPago, vivo.Checkout.Estado);
        Assert.Equal(EstadoDelCheckout.EnPago, mudo.Checkout.Estado);
        Assert.Empty(vivo.Entorno.Inventario.Liberaciones);
        Assert.Empty(mudo.Entorno.Inventario.Liberaciones);
    }

    // ---------- El Dominio: volver a la revisión por el límite ----------

    [Fact]
    public async Task VolverALaRevisionPorLimite_conserva_el_pedido_la_generacion_y_no_anota_liberacion_y_es_condicional()
    {
        var c = await Preparado();
        var pedido = c.Preparado.PedidoId!.Value;

        Assert.Equal(EfectoDeTransicion.NoAplica, c.Checkout.VolverALaRevisionPorLimite(Guid.NewGuid(), c.Preparado.Generacion, Inicio).Efecto);
        Assert.Equal(EfectoDeTransicion.NoAplica, c.Checkout.VolverALaRevisionPorLimite(pedido, c.Preparado.Generacion + 1, Inicio).Efecto);
        var aplicada = c.Checkout.VolverALaRevisionPorLimite(pedido, c.Preparado.Generacion, Inicio);

        Assert.Equal(EfectoDeTransicion.Aplicada, aplicada.Efecto);
        Assert.Equal((EstadoDelCheckout.EnEdicion, pedido, c.Preparado.Generacion), (c.Checkout.Estado, c.Checkout.PedidoId, c.Checkout.GeneracionDeReserva));
        Assert.Null(c.Checkout.ReservaALiberarDesde);
        Assert.Null(c.Checkout.PedidoDescartado);
        Assert.Equal(EfectoDeTransicion.NoAplica, c.Checkout.VolverALaRevisionPorLimite(pedido, c.Preparado.Generacion, Inicio).Efecto);
    }
}
