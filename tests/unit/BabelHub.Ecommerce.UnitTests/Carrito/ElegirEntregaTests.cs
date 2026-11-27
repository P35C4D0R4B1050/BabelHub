using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T15: <c>ElegirEntrega</c> (CAR-08, CAR-09, CAR-23, CAR-30; AC-CAR-07, AC-CAR-08, AC-CAR-21, AC-CAR-26): recojo en una sucursal de la lista o domicilio con dirección, región de la lista, nombre y teléfono; la consulta previa va
/// fuera de la transacción y guarda por línea la fuente y el plazo informados; envío y total; el contacto se vacía al pasar a recojo; con el checkout <c>en_pago</c> el cambio reabre la intención (y un cambio sin variación no).
/// </summary>
public sealed class ElegirEntregaTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static ContactoDeCompra Contacto(string region = "ayacucho") => new("Ana Pérez", "987654321", "Jr. Lima 123", region);

    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout, Guid Libro)> ConCheckout(decimal precio = 37.30m, int cantidad = 3)
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(10, precio, "Libro");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, cantidad, CodigoDeSucursal: Sucursal.Principal));
        var inicio = await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        Assert.True(inicio.EsExito);
        e.Inventario.ReiniciarContadores();
        return (e, e.Checkouts.Todos.Single(), libro);
    }

    private static Task<Resultado<VistaDelCheckout>> Elegir(EntornoDelCarrito e, ElegirEntregaEntrada entrada) =>
        new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(entrada);

    private static ElegirEntregaEntrada Recojo(Guid? pedido = null) => new(TipoDeEntregaDeCompra.Recojo, null, pedido);

    private static ElegirEntregaEntrada Domicilio(ContactoDeCompra? contacto = null, Guid? pedido = null) => new(TipoDeEntregaDeCompra.Domicilio, contacto ?? Contacto(), pedido);

    // ---------- Recojo ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-08")]
    public async Task El_recojo_muestra_la_sucursal_de_cada_linea_sin_sucursal_comun_ni_traslado_y_la_guarda_por_linea()
    {
        var (e, checkout, libro) = await ConCheckout();
        var lineaId = checkout.Lineas.Single().LineaId;

        var resultado = await Elegir(e, Recojo());

        Assert.True(resultado.EsExito);
        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal((DisponibilidadDeLineaDeCheckout.EnSuSucursal, (int?)null, false), (linea.Disponibilidad, linea.PlazoInformadoDias, linea.PlazoAConfirmar));
        Assert.Equal((Sucursal.Principal, "Sede central"), (linea.CodigoDeSucursal, linea.NombreDeSucursal));
        Assert.Null(resultado.Valor.Envio);
        Assert.Equal(TipoDeEntregaDeCompra.Recojo, resultado.Valor.TipoDeEntrega);
        Assert.Null(resultado.Valor.CodigoDeSucursalDeRecojo);
        var guardada = Assert.Single(checkout.Lineas);
        Assert.Equal((lineaId, Sucursal.Principal, false, (int?)null), (guardada.LineaId, guardada.CodigoDeFuenteInformado, guardada.RequiereTrasladoInformado, guardada.PlazoInformadoDias));
        Assert.Equal(TipoDeEntrega.Recojo, e.Inventario.UltimaEntregaPrevia!.Tipo);
        Assert.Equal(Sucursal.Principal, Assert.Single(e.Inventario.UltimasLineasPrevias).CodigoDeSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-08")]
    public async Task Si_la_ruta_no_tiene_plazo_definido_se_ve_a_confirmar()
    {
        var (e, _, libro) = await ConCheckout();
        e.Inventario.FijarFuente(libro, "cusco", true, null);

        var linea = Assert.Single((await Elegir(e, Recojo())).Valor.Lineas);

        Assert.True(linea.PlazoAConfirmar);
        Assert.Null(linea.PlazoInformadoDias);
        Assert.Equal(DisponibilidadDeLineaDeCheckout.PorTraslado, linea.Disponibilidad);
    }

    [Fact]
    public async Task Una_region_que_no_esta_en_la_lista_es_una_validacion_y_no_consulta_el_inventario()
    {
        var (e, checkout, _) = await ConCheckout();

        var resultado = await Elegir(e, Domicilio(new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "no-existe")));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("region", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Equal(0, e.Inventario.ConsultasPrevias);
        Assert.Null(checkout.TipoDeEntrega);
    }

    [Fact]
    public async Task Sin_lista_de_sucursales_no_se_puede_validar_el_domicilio()
    {
        var (e, checkout, _) = await ConCheckout();
        e.Inventario.Sucursales = null;

        var resultado = await Elegir(e, Domicilio());

        Assert.Equal(ClavesDelCarrito.EntregaNoValidable, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error.Categoria);
        Assert.Null(checkout.TipoDeEntrega);
    }

    [Fact]
    public async Task Un_recojo_con_contacto_lo_ignora_y_no_guarda_nada_personal()
    {
        var (e, checkout, _) = await ConCheckout();

        var resultado = await Elegir(e, new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, Contacto()));

        Assert.True(resultado.EsExito);
        Assert.Null(checkout.Contacto);
        Assert.Null(resultado.Valor.Contacto);
    }

    // ---------- Domicilio ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-21")]
    public async Task Un_carrito_de_111_90_con_domicilio_suma_el_envio_de_12_y_totaliza_123_90_y_con_recojo_111_90()
    {
        var (e, _, _) = await ConCheckout(37.30m, 3);

        var domicilio = (await Elegir(e, Domicilio())).Valor;
        var recojo = (await Elegir(e, Recojo())).Valor;

        Assert.Equal((111.90m, 12.00m, 123.90m), (domicilio.Subtotal, domicilio.Envio, domicilio.Total));
        Assert.Equal((111.90m, (decimal?)null, 111.90m), (recojo.Subtotal, recojo.Envio, recojo.Total));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-26")]
    public async Task El_domicilio_guarda_el_contacto_la_region_y_consulta_el_inventario_por_la_region()
    {
        var (e, checkout, _) = await ConCheckout();

        var resultado = await Elegir(e, Domicilio());

        Assert.True(resultado.EsExito);
        Assert.Equal("ayacucho", checkout.RegionDeEntrega);
        Assert.Equal("987654321", checkout.Contacto!.Telefono.Valor);
        Assert.Equal((TipoDeEntrega.Domicilio, "ayacucho", null), (e.Inventario.UltimaEntregaPrevia!.Tipo, e.Inventario.UltimaEntregaPrevia.RegionDeEntrega, e.Checkouts.Todos.Single().CodigoDeSucursalDeRecojo));
        Assert.Equal("[redactado]", resultado.Valor.Contacto!.ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-07")]
    [InlineData(null, "987654321", "Jr. Lima 123", "ayacucho", "nombre", "obligatorio")]
    [InlineData("Ana", null, "Jr. Lima 123", "ayacucho", "telefono", "obligatorio")]
    [InlineData("Ana", "123", "Jr. Lima 123", "ayacucho", "telefono", "formato-invalido")]
    [InlineData("Ana", "987654321", null, "ayacucho", "direccion", "obligatorio")]
    [InlineData("Ana", "987654321", "   ", "ayacucho", "direccion", "obligatorio")]
    [InlineData("Ana", "987654321", "Jr. Lima\n123", "ayacucho", "direccion", "formato-invalido")]
    [InlineData("Ana\tPérez", "987654321", "Jr. Lima 123", "ayacucho", "nombre", "formato-invalido")]
    [InlineData("Ana", "987654321", "Jr. Lima 123", null, "region", "obligatorio")]
    [InlineData("Ana", "987654321", "Jr. Lima 123", "atlantida", "region", "formato-invalido")]
    public async Task El_domicilio_sin_dirección_o_con_datos_invalidos_no_puede_continuar_y_el_error_no_repite_el_valor(string? nombre, string? telefono, string? direccion, string? region, string campo, string clave)
    {
        var (e, checkout, _) = await ConCheckout();

        var resultado = await Elegir(e, Domicilio(new ContactoDeCompra(nombre, telefono, direccion, region)));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        var error = Assert.Single(resultado.Error.Errores);
        Assert.Equal((campo, clave), (error.Campo, error.Clave));
        Assert.Null(checkout.TipoDeEntrega);
        Assert.Null(checkout.Contacto);
        Assert.Equal(0, e.Inventario.ConsultasPrevias);
    }

    [Fact]
    public async Task Un_domicilio_sin_contacto_es_una_validacion()
    {
        var (e, _, _) = await ConCheckout();

        var resultado = await Elegir(e, new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null));

        Assert.Equal("contacto", Assert.Single(resultado.Error!.Errores).Campo);
    }

    [Fact]
    public async Task Pasar_de_domicilio_a_recojo_vacia_el_contacto_y_la_region()
    {
        var (e, checkout, _) = await ConCheckout();
        await Elegir(e, Domicilio());

        var resultado = await Elegir(e, Recojo());

        Assert.True(resultado.EsExito);
        Assert.Null(checkout.Contacto);
        Assert.Null(checkout.RegionDeEntrega);
        Assert.Null(resultado.Valor.Contacto);
    }

    // ---------- Stock y respuesta del Inventario ----------

    [Fact]
    public async Task Si_la_consulta_previa_dice_que_no_hay_stock_en_esa_entrega_se_rechaza_con_el_maximo_y_no_se_guarda_nada()
    {
        var (e, checkout, libro) = await ConCheckout(cantidad: 3);
        e.Inventario.FijarStock(libro, 2);

        var resultado = await Elegir(e, Recojo());

        Assert.Equal(ClavesDelCarrito.SinStock, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("maximo", "2"), resultado.Error.Errores);
        Assert.Null(checkout.TipoDeEntrega);
    }

    [Fact]
    public async Task Si_el_inventario_no_responde_no_se_avanza_y_no_se_reabre_nada()
    {
        var (e, checkout, _) = await ConCheckout();
        e.Inventario.PreviaCaida = true;

        var resultado = await Elegir(e, Recojo());

        Assert.Equal(ClavesDelCarrito.StockNoValidable, resultado.Error!.Clave);
        Assert.Null(checkout.TipoDeEntrega);
        Assert.Equal(0, e.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Un_carrito_solo_digital_no_tiene_entrega_y_no_se_consulta_nada()
    {
        var e = EntornoDelCarrito.DeCliente();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(e.Digital(), 1, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());

        var resultado = await Elegir(e, Recojo());

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("tipoDeEntrega", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Equal(0, e.Inventario.ConsultasPrevias);
    }

    // ---------- El checkout vivo ----------

    private static async Task<(EntornoDelCarrito Entorno, Checkout Checkout, Guid Libro)> EnPago()
    {
        var (e, checkout, libro) = await ConCheckout();
        await Elegir(e, Recojo());
        var carrito = e.Carritos.Todos.Single();
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, carrito, Inicio).Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito).Efecto);
        e.Pagos.AlReabrir = ResultadoDeReabrirIntencion.Reabierta;
        return (e, checkout, libro);
    }

    [Fact]
    public async Task Con_el_checkout_en_pago_un_cambio_reabre_la_intencion_antes_de_bloquear_y_conserva_el_pedido()
    {
        var (e, checkout, _) = await EnPago();
        var pedido = checkout.PedidoId;
        e.Bitacora.Bloqueos.Clear();

        var resultado = await Elegir(e, Domicilio());

        Assert.True(resultado.EsExito);
        Assert.Equal(["reabrir", "checkout", "carrito"], e.Bitacora.Bloqueos);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(pedido, checkout.PedidoId);
        Assert.Equal(TipoDeEntrega.Domicilio, checkout.TipoDeEntrega);
        Assert.Contains(checkout, e.Checkouts.Guardados);
    }

    [Fact]
    public async Task Elegir_lo_mismo_es_un_no_op_que_no_reabre_la_intencion()
    {
        var (e, checkout, _) = await EnPago();

        var resultado = await Elegir(e, Recojo());

        Assert.True(resultado.EsExito);
        Assert.Equal(0, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
    }

    [Fact]
    public async Task Con_un_intento_activo_se_rechaza_y_con_el_pago_pendiente_o_reservando_tambien()
    {
        var (activo, checkoutActivo, _) = await EnPago();
        activo.Pagos.AlReabrir = ResultadoDeReabrirIntencion.NoCancelable;
        var (pendiente, checkoutPendiente, _) = await EnPago();
        checkoutPendiente.PagoPendiente(checkoutPendiente.PedidoId!.Value, Inicio.AddMinutes(30), Inicio.AddMinutes(15), Inicio);

        var a = await Elegir(activo, Domicilio());
        var b = await Elegir(pendiente, Domicilio());

        Assert.Equal(ClavesDelCarrito.PagoPendiente, a.Error!.Clave);
        Assert.Equal(ClavesDelCarrito.PagoPendiente, b.Error!.Clave);
        Assert.Equal(EstadoDelCheckout.EnPago, checkoutActivo.Estado);
        Assert.Equal(0, pendiente.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Si_la_intencion_ya_no_admite_cobro_se_aborta_desde_en_pago_y_la_eleccion_sigue()
    {
        var (e, checkout, _) = await EnPago();
        var pedido = checkout.PedidoId;
        e.Pagos.AlReabrir = ResultadoDeReabrirIntencion.Cerrada;

        var resultado = await Elegir(e, Domicilio());

        Assert.True(resultado.EsExito);
        Assert.Null(checkout.PedidoId);
        Assert.Equal(pedido, checkout.PedidoDescartado);
        Assert.Equal(TipoDeEntrega.Domicilio, checkout.TipoDeEntrega);
    }

    [Fact]
    public async Task Sin_checkout_vivo_es_no_encontrado()
    {
        var e = EntornoDelCarrito.DeCliente();

        var resultado = await Elegir(e, Recojo());

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Fact]
    public async Task Un_pedido_oculto_ajeno_es_no_encontrado_con_su_evento_y_uno_propio_descartado_un_error_amable_sin_evento()
    {
        var (e, checkout, _) = await ConCheckout();
        var propioDescartado = Guid.NewGuid();
        typeof(Checkout).GetProperty(nameof(Checkout.PedidoDescartado))!.SetValue(checkout, propioDescartado);

        var ajeno = await Elegir(e, Recojo(pedido: Guid.NewGuid()));
        var obsoleto = await Elegir(e, Recojo(pedido: propioDescartado));

        Assert.Equal(ClavesDelCarrito.NoEncontrado, ajeno.Error!.Clave);
        Assert.Equal(ClavesDelCarrito.PedidoObsoleto, obsoleto.Error!.Clave);
        Assert.Equal("recurso-ajeno-pedido-id", Assert.Single(e.Denegaciones.Eventos).Motivo);
    }

    // ---------- Alcance y límites ----------

    [Fact]
    public async Task Sin_sesion_o_con_personal_se_rechaza_y_una_cuenta_cancelada_tambien()
    {
        var visitante = EntornoDelCarrito.DeVisitante();
        var encargado = new EntornoDelCarrito(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var (cancelada, _, _) = await ConCheckout();
        cancelada.Cuenta.Cancelada = true;

        Assert.Equal(CategoriaDeError.NoAutenticado, (await Elegir(visitante, Recojo())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Elegir(encargado, Recojo())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Elegir(cancelada, Recojo())).Error!.Categoria);
    }

    [Fact]
    public async Task Un_limite_excedido_responde_limite_antes_de_consultar_al_inventario()
    {
        var (e, _, _) = await ConCheckout();
        e.Limites.Revisar = new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, Inicio.AddSeconds(30));
        var antes = e.Inventario.ConsultasPrevias;

        var resultado = await Elegir(e, Recojo());

        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Equal(antes, e.Inventario.ConsultasPrevias);
    }
}
