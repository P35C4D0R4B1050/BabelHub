using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using TextoLegal = BabelHub.Ecommerce.Domain.Carrito.TextoLegal;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T17: <c>PrepararPasoAlPago</c> (D18; AC-CAR-12, 13, 18, 19, 27; CAR-14, 16, 21, 31, 36, 37): comprobaciones en el orden sesión → cuenta → textos → licencia → posesión → entrega → total visto, lo que es red antes de la transacción, la marca de
/// cancelación con bloqueo primero, congela las líneas, el envío y el total, las dos huellas, el <c>pedidoId</c> y la generación, y pasa a <c>reservando</c> sin crear reserva ni intención en ninguna falla.
/// </summary>
public sealed class PrepararPasoAlPagoTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];
    private static readonly TextoVistoDeCompra[] Los3 = [.. Los2, new(TextoLegalDeCompra.ReembolsoDigital, "r-1")];

    private sealed record Escenario(EntornoDelCarrito Entorno, Checkout Checkout, Guid Impreso, Guid? Digital, decimal Total);

    /// <summary>Un checkout listo para pagar: un impreso de 37,30 por 3 (y, si se pide, una digital de 20) con su entrega y las aceptaciones registradas.</summary>
    private static async Task<Escenario> Listo(bool conDigital = false, bool domicilio = false, bool aceptarReembolso = true)
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = e.Impreso(10, 37.30m, "Impreso");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(impreso, 3, CodigoDeSucursal: Sucursal.Principal));
        Guid? digital = null;
        if (conDigital)
        {
            digital = e.Digital(20m, "Digital");
            await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(digital.Value, 1, CodigoDeSucursal: Sucursal.Principal));
        }

        Assert.True((await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada())).EsExito);
        var entrega = domicilio
            ? new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho"))
            : new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo);
        Assert.True((await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(entrega)).EsExito);
        var registro = await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(conDigital && aceptarReembolso ? Los3 : Los2));
        Assert.True(registro.EsExito || !aceptarReembolso);
        var total = 111.90m + (conDigital ? 20m : 0m) + (domicilio ? 12m : 0m);
        return new Escenario(e, e.Checkouts.Todos.Single(), impreso, digital, total);
    }

    private static Task<Resultado<PasoAlPagoPreparado>> Preparar(Escenario s, decimal? total = null, IReadOnlyList<TextoVistoDeCompra>? textos = null, Guid? pedido = null) =>
        new PrepararPasoAlPago(s.Entorno.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(total ?? s.Total, textos ?? Los3, pedido));

    private static void SinEfectos(Escenario s)
    {
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Null(s.Checkout.PedidoId);
        Assert.Empty(s.Entorno.Inventario.Reservas);
        Assert.Equal(0, s.Entorno.Pagos.Creaciones);
    }

    // ---------- El camino feliz ----------

    [Fact]
    public async Task Pasar_al_pago_congela_la_compra_asigna_el_pedido_y_deja_el_checkout_reservando_sin_crear_reserva_ni_intencion()
    {
        var s = await Listo(domicilio: true);

        var resultado = await Preparar(s);

        Assert.True(resultado.EsExito);
        var p = resultado.Valor;
        Assert.Equal(DesenlaceDePrepararPaso.Preparado, p.Desenlace);
        Assert.Equal(EstadoDelCheckout.Reservando, s.Checkout.Estado);
        Assert.Equal(s.Checkout.PedidoId, p.PedidoId);
        Assert.Equal(1, p.Generacion);
        Assert.Equal(Inicio, s.Checkout.ReservandoDesde);
        Assert.Equal((12m, 123.90m), (s.Checkout.EnvioCongelado, s.Checkout.TotalCongelado));
        Assert.StartsWith("v1:", s.Checkout.HuellaDeInstantanea, StringComparison.Ordinal);
        Assert.Equal(64, s.Checkout.HuellaDeReserva!.Length);
        Assert.Empty(s.Entorno.Inventario.Reservas);
        Assert.Equal(0, s.Entorno.Pagos.Creaciones);
        Assert.Contains(s.Checkout, s.Entorno.Checkouts.Guardados);
    }

    [Fact]
    public async Task La_instantanea_lleva_todo_lo_congelado_y_el_contacto_solo_si_es_a_domicilio_redactado()
    {
        var domicilio = await Listo(domicilio: true);
        var recojo = await Listo();

        var conContacto = (await Preparar(domicilio)).Valor.Instantanea!;
        var sinContacto = (await Preparar(recojo)).Valor.Instantanea!;

        Assert.Equal((TipoDeEntregaDeCompra.Domicilio, "ayacucho", 111.90m, 12m, 123.90m), (conContacto.TipoDeEntrega, conContacto.RegionDeEntrega, conContacto.Subtotal, conContacto.Envio, conContacto.Total));
        Assert.Equal("987654321", conContacto.Contacto!.Telefono);
        Assert.Equal("[redactado]", conContacto.Contacto.ToString());
        Assert.Equal(domicilio.Checkout.HuellaDeInstantanea, conContacto.HuellaDeInstantanea);
        Assert.Equal(domicilio.Checkout.Lineas.Single().LineaId, Assert.Single(conContacto.Lineas).LineaId);
        Assert.Equal(["t-1", "p-1"], conContacto.Aceptaciones.Select(a => a.Version));
        Assert.Null(sinContacto.Contacto);
        Assert.Equal((TipoDeEntregaDeCompra.Recojo, (string?)null), (sinContacto.TipoDeEntrega, sinContacto.CodigoDeSucursalDeRecojo));
    }

    [Fact]
    public async Task Lo_que_se_reserva_son_solo_los_impresos_con_su_lineaId_y_la_entrega()
    {
        var s = await Listo(conDigital: true);

        var p = (await Preparar(s)).Valor;

        var linea = Assert.Single(p.LineasImpresas);
        Assert.Equal((s.Impreso, 3), (linea.EdicionId, linea.Cantidad));
        Assert.Equal(s.Checkout.Lineas.Single(l => !l.EsDigital).LineaId, linea.LineaId);
        Assert.Equal((TipoDeEntrega.Recojo, (string?)null), (p.Entrega!.Tipo, p.Entrega.RegionDeEntrega));
        Assert.Equal(Sucursal.Principal, linea.CodigoDeSucursal);
    }

    [Fact]
    public async Task Un_carrito_solo_digital_no_tiene_entrega_ni_reserva_ni_consulta_al_inventario()
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Digital(20m), 1, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los3));
        var s = new Escenario(e, e.Checkouts.Todos.Single(), Guid.Empty, null, 20m);

        var p = (await Preparar(s)).Valor;

        Assert.Equal(DesenlaceDePrepararPaso.Preparado, p.Desenlace);
        Assert.Null(p.Entrega);
        Assert.Empty(p.LineasImpresas);
        Assert.Equal(0, e.Inventario.ConsultasDeDisponibilidad);
        Assert.Equal(TipoDeEntregaDeCompra.Digital, p.Instantanea!.TipoDeEntrega);
    }

    [Fact]
    public async Task La_misma_huella_continua_con_el_mismo_pedido_sin_subir_la_generacion()
    {
        var s = await Listo();
        var primero = (await Preparar(s)).Valor;

        var segundo = await Preparar(s);

        Assert.True(segundo.EsExito, segundo.Error?.Clave);
        Assert.Equal(DesenlaceDePrepararPaso.YaPreparado, segundo.Valor.Desenlace);
        Assert.Equal((primero.PedidoId, primero.Generacion, primero.HuellaDeInstantanea), (segundo.Valor.PedidoId, segundo.Valor.Generacion, segundo.Valor.HuellaDeInstantanea));
    }

    [Fact]
    public async Task Repetir_pagar_con_el_mismo_pedido_no_consume_otro_cupo_del_limite_despues_de_asignarlo()
    {
        var s = await Listo();

        await Preparar(s);
        await Preparar(s);
        await Preparar(s);

        Assert.Equal(2, s.Entorno.Limites.ConsumosDePagar);
    }

    // ---------- El orden de las comprobaciones ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task Un_visitante_sin_sesion_no_puede_pagar()
    {
        var visitante = EntornoDelCarrito.DeVisitante();

        var resultado = await new PrepararPasoAlPago(visitante.ServiciosDelCheckout()).Ejecutar(new PasarAlPagoEntrada(10m, Los2));

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task La_cuenta_se_comprueba_antes_que_los_textos_los_textos_antes_que_la_licencia_y_la_licencia_antes_que_la_posesion()
    {
        var s = await Listo(conDigital: true);
        s.Entorno.Cuenta.CorreoSinVerificar = true;
        s.Entorno.Textos.Vigente(BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal.Terminos, "t-2");
        s.Entorno.Catalogo.Poner(s.Digital!.Value, FormatoDeEdicion.Digital, 20m, motivo: MotivoDeNoDisponibleDeEdicion.LicenciaVencida);
        s.Entorno.Posesion.Poseer(s.Digital.Value);

        Assert.Equal(ClavesDelCarrito.CorreoSinVerificar, (await Preparar(s)).Error!.Clave);
        s.Entorno.Cuenta.CorreoSinVerificar = false;
        Assert.Equal(ClavesDelCarrito.TextoCambio, (await Preparar(s)).Error!.Clave);
        s.Entorno.Textos.Vigente(BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal.Terminos, "t-1");
        Assert.Equal(ClavesDelCarrito.LicenciaNoVigente, (await Preparar(s)).Error!.Clave);
        s.Entorno.Catalogo.Poner(s.Digital.Value, FormatoDeEdicion.Digital, 20m);
        Assert.Equal(ClavesDelCarrito.DigitalYaPoseida, (await Preparar(s)).Error!.Clave);
        SinEfectos(s);
    }

    // ---------- Cuenta ----------

    [Theory]
    [Trait("Criterio", "AC-CAR-27")]
    [InlineData("cancelacion-diferida", ClavesDelCarrito.CuentaNoPuedeComprar)]
    [InlineData("correo-sin-verificar", ClavesDelCarrito.CorreoSinVerificar)]
    [InlineData("textos-pendientes", ClavesDelCarrito.TextosPendientes)]
    [InlineData("cancelada", ClavesDelCarrito.CuentaNoPuedeOperar)]
    public async Task Una_cuenta_que_no_puede_comprar_no_inicia_el_pago_y_el_carrito_se_conserva(string motivo, string clave)
    {
        var s = await Listo();
        switch (motivo)
        {
            case "cancelacion-diferida": s.Entorno.Cuenta.CancelacionDiferida = true; break;
            case "correo-sin-verificar": s.Entorno.Cuenta.CorreoSinVerificar = true; break;
            case "textos-pendientes": s.Entorno.Cuenta.TextosPendientes = true; break;
            default: s.Entorno.Cuenta.Cancelada = true; break;
        }

        var resultado = await Preparar(s);

        Assert.Equal(clave, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error.Categoria);
        SinEfectos(s);
        Assert.Single(s.Entorno.Carritos.Todos.Single().Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Si_Identidad_no_responde_el_pago_no_se_inicia()
    {
        var s = await Listo();
        s.Entorno.Cuenta.NoResponde = true;

        var resultado = await Preparar(s);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        SinEfectos(s);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Una_cancelacion_que_empieza_entre_la_revision_y_el_pago_se_detecta_con_la_marca_leida_con_bloqueo()
    {
        var s = await Listo();
        s.Entorno.Marca.AntesDeLeer = () => s.Entorno.Marca.Estado = EstadoDeLaMarcaDeCancelacion.EnEjecucion;
        s.Entorno.Bitacora.Bloqueos.Clear();

        var resultado = await Preparar(s);

        Assert.Equal(ClavesDelCarrito.CuentaNoPuedeComprar, resultado.Error!.Clave);
        SinEfectos(s);
        Assert.Equal(["marca"], s.Entorno.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task Si_no_se_puede_leer_la_marca_de_cancelacion_se_revierte_y_no_se_asume_libre()
    {
        var s = await Listo();
        s.Entorno.Marca.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Preparar(s));

        SinEfectos(s);
    }

    [Fact]
    public async Task La_marca_se_bloquea_primero_despues_el_checkout_y_despues_el_carrito()
    {
        var s = await Listo();
        s.Entorno.Bitacora.Bloqueos.Clear();

        await Preparar(s);

        Assert.Equal(["marca", "checkout", "carrito"], s.Entorno.Bitacora.Bloqueos);
    }

    // ---------- Textos ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-20")]
    public async Task Si_la_version_vista_ya_no_es_la_vigente_se_rechaza_y_no_se_crea_nada()
    {
        var s = await Listo();
        s.Entorno.Textos.Vigente(BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal.Privacidad, "p-2");

        var resultado = await Preparar(s);

        Assert.Equal(ClavesDelCarrito.TextoCambio, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("privacidad", "p-2"), resultado.Error.Errores);
        SinEfectos(s);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-15")]
    public async Task Sin_aceptar_los_terminos_y_la_privacidad_no_se_puede_pagar()
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Impreso(10, 37.30m), 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo));
        var s = new Escenario(e, e.Checkouts.Todos.Single(), Guid.Empty, null, 111.90m);

        var resultado = await Preparar(s, textos: []);

        Assert.Equal(ClavesDelCarrito.AceptacionRequerida, resultado.Error!.Clave);
        SinEfectos(s);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-11")]
    public async Task Con_una_digital_sin_el_reembolso_digital_no_se_puede_pagar_y_aceptarlo_en_la_misma_solicitud_alcanza()
    {
        var s = await Listo(conDigital: true, aceptarReembolso: false);

        var sinReembolso = await Preparar(s, textos: Los2);
        SinEfectos(s);
        var conReembolso = await Preparar(s, textos: Los3);

        Assert.Equal(ClavesDelCarrito.AceptacionRequerida, sinReembolso.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("reembolsoDigital", ConstructorDeValidacion.Obligatorio), sinReembolso.Error.Errores);
        Assert.True(conReembolso.EsExito);
        Assert.Equal("r-1", s.Checkout.Aceptaciones.Aceptadas.Single(a => a.Texto == TextoLegal.ReembolsoDigital).Version);
    }

    // ---------- Licencia, posesión, entrega, total ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-12")]
    public async Task Una_digital_cuya_licencia_vencio_antes_de_pagar_se_bloquea_y_se_invita_a_quitarla()
    {
        var s = await Listo(conDigital: true);
        s.Entorno.Catalogo.Poner(s.Digital!.Value, FormatoDeEdicion.Digital, 20m, motivo: MotivoDeNoDisponibleDeEdicion.LicenciaVencida);

        var resultado = await Preparar(s);

        Assert.Equal(ClavesDelCarrito.LicenciaNoVigente, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("edicionId", s.Digital.Value.ToString("D")), resultado.Error.Errores);
        SinEfectos(s);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-18")]
    public async Task Una_digital_que_la_cuenta_ya_posee_desde_otro_carrito_se_rechaza_y_si_Biblioteca_no_responde_falla_cerrado()
    {
        var poseida = await Listo(conDigital: true);
        poseida.Entorno.Posesion.Poseer(poseida.Digital!.Value);
        var sinRespuesta = await Listo(conDigital: true);
        sinRespuesta.Entorno.Posesion.NoResponde = true;

        var a = await Preparar(poseida);
        var b = await Preparar(sinRespuesta);

        Assert.Equal(ClavesDelCarrito.DigitalYaPoseida, a.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, b.Error!.Categoria);
        SinEfectos(poseida);
        SinEfectos(sinRespuesta);
    }

    [Fact]
    public async Task Sin_entrega_elegida_o_a_domicilio_sin_contacto_los_datos_estan_incompletos()
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Impreso(10, 37.30m), 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        var s = new Escenario(e, e.Checkouts.Todos.Single(), Guid.Empty, null, 111.90m);

        var resultado = await Preparar(s);

        Assert.Equal(ClavesDelCarrito.DatosDeEntregaIncompletos, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error.Categoria);
        SinEfectos(s);
    }

    [Fact]
    public async Task Si_el_total_que_la_persona_vio_no_es_el_calculado_se_rechaza_y_no_se_crea_nada()
    {
        var s = await Listo();

        var resultado = await Preparar(s, total: 100m);

        Assert.Equal(ClavesDelCarrito.TotalCambio, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("total", "111.90"), resultado.Error.Errores);
        SinEfectos(s);
    }

    [Fact]
    public async Task Un_cambio_de_precio_en_el_catalogo_cambia_el_total_calculado_y_se_pide_confirmarlo()
    {
        var s = await Listo();
        s.Entorno.Catalogo.Poner(s.Impreso, FormatoDeEdicion.Impresa, 40m, "Impreso");

        var resultado = await Preparar(s);

        Assert.Equal(ClavesDelCarrito.TotalCambio, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("total", "120.00"), resultado.Error.Errores);
    }

    [Fact]
    public async Task Con_el_total_correcto_con_el_precio_nuevo_se_congela_ese_precio()
    {
        var s = await Listo();
        s.Entorno.Catalogo.Poner(s.Impreso, FormatoDeEdicion.Impresa, 40m, "Impreso");

        var resultado = await Preparar(s, total: 120m);

        Assert.True(resultado.EsExito);
        Assert.Equal(120m, s.Checkout.TotalCongelado);
        Assert.Equal(40m, s.Checkout.Lineas.Single().PrecioUnitario);
    }

    // ---------- Límites y alcance ----------

    [Fact]
    public async Task El_limite_de_pagar_excedido_o_sin_almacen_falla_antes_de_consultar_a_nadie_mas()
    {
        var s = await Listo();
        s.Entorno.Limites.Pagar = new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, Inicio.AddMinutes(20));
        var excedido = await Preparar(s);
        s.Entorno.Limites.Pagar = ResultadoDeLimiteDelCarrito.NoDisponible;
        var sinAlmacen = await Preparar(s);

        Assert.Equal(CategoriaDeError.Limite, excedido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, sinAlmacen.Error!.Categoria);
        SinEfectos(s);
    }

    [Fact]
    public async Task Un_pedido_oculto_ajeno_responde_no_encontrado_y_deja_el_evento()
    {
        var s = await Listo();

        var resultado = await Preparar(s, pedido: Guid.NewGuid());

        Assert.Equal(ClavesDelCarrito.NoEncontrado, resultado.Error!.Clave);
        Assert.Equal("recurso-ajeno-pedido-id", Assert.Single(s.Entorno.Denegaciones.Eventos).Motivo);
    }

    [Fact]
    public async Task Con_el_pago_pendiente_se_rechaza()
    {
        var s = await Listo();
        var p = (await Preparar(s)).Valor;
        var carrito = s.Entorno.Carritos.Todos.Single();
        s.Checkout.ConfirmarEnPago(p.PedidoId!.Value, p.Generacion, p.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
        s.Checkout.PagoPendiente(p.PedidoId.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);

        var resultado = await Preparar(s);

        Assert.Equal(ClavesDelCarrito.PagoPendiente, resultado.Error!.Clave);
    }

    // ---------- Retiro pendiente ----------

    [Fact]
    public async Task Con_un_retiro_pendiente_en_pago_se_aplica_y_se_devuelve_la_revision_con_el_aviso_sin_entrar_en_bucle_con_Pagos()
    {
        var s = await Listo(conDigital: true);
        var p = (await Preparar(s)).Valor;
        var carrito = s.Entorno.Carritos.Todos.Single();
        s.Checkout.ConfirmarEnPago(p.PedidoId!.Value, p.Generacion, p.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
        carrito.RetirarEdicion(s.Digital!.Value, diferir: true, Inicio);

        var resultado = await Preparar(s);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(DesenlaceDePrepararPaso.RevisionPorRetiro, resultado.Valor.Desenlace);
        Assert.True(resultado.Valor.Vista.CarritoCambio);
        Assert.Equal(EstadoDelCheckout.EnEdicion, s.Checkout.Estado);
        Assert.Equal(1, s.Entorno.Pagos.Reaperturas);
        Assert.Equal(0, s.Entorno.Pagos.Creaciones);
        Assert.DoesNotContain(carrito.Lineas, l => l.EdicionId == s.Digital.Value);
        Assert.Equal(s.Checkout.PedidoId, p.PedidoId);

        // Pagar otra vez ya no encuentra el retiro: no hay bucle.
        var otra = await Preparar(s, total: 111.90m);
        Assert.True(otra.EsExito, otra.Error?.Clave);
        Assert.NotEqual(DesenlaceDePrepararPaso.RevisionPorRetiro, otra.Valor.Desenlace);
    }
}
