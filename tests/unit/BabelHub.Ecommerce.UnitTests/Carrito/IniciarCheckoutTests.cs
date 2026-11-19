using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T14: <c>IniciarCheckout</c> (CAR-05, CAR-06, CAR-07; AC-CAR-04, AC-CAR-05, AC-CAR-06): solo con sesión y cuenta activa; consulta antes de la transacción la disponibilidad real del Inventario por lotes y, con la sucursal
/// preferida, la consulta previa para mostrar «en su sucursal» o «por traslado»; las <c>noRegistradas</c> son agotadas; un carrito solo digital no consulta; sin respuesta no avanza; con un checkout vivo devuelve ese.
/// </summary>
public sealed class IniciarCheckoutTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static async Task<(EntornoDelCarrito Entorno, Guid Cuenta, CarritoDeDominio Carrito)> ConCarrito(params (Func<EntornoDelCarrito, Guid> Edicion, int Cantidad)[] lineas)
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        foreach (var (edicion, cantidad) in lineas)
        {
            await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(edicion(e), cantidad));
        }

        return (e, cuenta, e.Carritos.Todos.SingleOrDefault()!);
    }

    private static Task<Resultado<VistaDelCheckout>> Iniciar(EntornoDelCarrito e, string? sucursal = null) =>
        new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada(sucursal));

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task Con_un_impreso_se_consulta_el_inventario_real_y_se_crea_el_checkout_en_la_revision()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(8, 40m, "Pedro Páramo"), 2));

        var resultado = await Iniciar(e);

        Assert.True(resultado.EsExito);
        var vista = resultado.Valor;
        Assert.Equal((EstadoVisibleDelCheckout.EnEdicion, 1, 5), (vista.Estado, vista.Paso, vista.TotalDePasos));
        Assert.Equal("Pedro Páramo", vista.Lineas.Single().Titulo);
        Assert.Equal(80m, vista.Subtotal);
        Assert.Equal(1, e.Inventario.ConsultasDeDisponibilidad);
        Assert.Equal(0, e.Inventario.ConsultasPrevias);
        var checkout = Assert.Single(e.Checkouts.Todos);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(2, checkout.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task Con_la_sucursal_preferida_cada_linea_dice_si_esta_en_su_sucursal_o_viene_por_traslado_con_su_plazo()
    {
        var aqui = Guid.Empty;
        var lejos = Guid.Empty;
        var sinRuta = Guid.Empty;
        var (e, _, _) = await ConCarrito((x => aqui = x.Impreso(5), 1), (x => lejos = x.Impreso(5), 1), (x => sinRuta = x.Impreso(5), 1));
        e.Inventario.FijarFuente(aqui, "sede-central", false, null);
        e.Inventario.FijarFuente(lejos, "cusco", true, 3);
        e.Inventario.FijarFuente(sinRuta, "cusco", true, null);

        var vista = (await Iniciar(e, "sede-central")).Valor;

        var porEdicion = vista.Lineas.ToDictionary(l => l.EdicionId);
        Assert.Equal(DisponibilidadDeLineaDeCheckout.EnSuSucursal, porEdicion[aqui].Disponibilidad);
        Assert.Equal((DisponibilidadDeLineaDeCheckout.PorTraslado, 3, false), (porEdicion[lejos].Disponibilidad, porEdicion[lejos].PlazoInformadoDias, porEdicion[lejos].PlazoAConfirmar));
        Assert.Equal((DisponibilidadDeLineaDeCheckout.PorTraslado, (int?)null, true), (porEdicion[sinRuta].Disponibilidad, porEdicion[sinRuta].PlazoInformadoDias, porEdicion[sinRuta].PlazoAConfirmar));
        Assert.Equal(1, e.Inventario.ConsultasPrevias);
        Assert.Equal(TipoDeEntrega.Recojo, e.Inventario.UltimaEntregaPrevia!.Tipo);
        Assert.Equal("sede-central", e.Inventario.UltimaEntregaPrevia.CodigoDeSucursalDeRecojo);
    }

    [Fact]
    public async Task Las_ediciones_que_el_inventario_no_registra_se_tratan_como_agotadas_y_el_checkout_no_se_crea()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        var linea = e.Carritos.Todos.Single().Lineas.Single().EdicionId;
        e.Inventario.Olvidar(linea);

        var resultado = await Iniciar(e);

        Assert.Equal(ClavesDelCarrito.SinStock, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("maximo", "0"), resultado.Error.Errores);
        Assert.Empty(e.Checkouts.Todos);
    }

    [Fact]
    public async Task Si_una_linea_pide_mas_de_lo_que_hay_se_rechaza_con_el_maximo_y_se_conserva_el_carrito()
    {
        var (e, _, carrito) = await ConCarrito((x => x.Impreso(5), 4));
        e.Inventario.FijarStock(carrito.Lineas.Single().EdicionId, 3);

        var resultado = await Iniciar(e);

        Assert.Equal(ClavesDelCarrito.SinStock, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("maximo", "3"), resultado.Error.Errores);
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error.Categoria);
        Assert.Empty(e.Checkouts.Todos);
        Assert.Single(carrito.Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-05")]
    public async Task Si_el_inventario_no_responde_se_informa_que_no_se_puede_validar_el_stock_y_no_se_crea_nada()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        e.Inventario.Caido = true;

        var resultado = await Iniciar(e);

        Assert.Equal(ClavesDelCarrito.StockNoValidable, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error.Categoria);
        Assert.Empty(e.Checkouts.Todos);
    }

    [Fact]
    public async Task Si_falla_la_consulta_previa_con_la_sucursal_preferida_tampoco_se_avanza()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        e.Inventario.PreviaCaida = true;

        var resultado = await Iniciar(e, "sede-central");

        Assert.Equal(ClavesDelCarrito.StockNoValidable, resultado.Error!.Clave);
        Assert.Empty(e.Checkouts.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-06")]
    public async Task Un_carrito_solo_digital_no_consulta_el_inventario_y_tiene_cuatro_pasos_o_menos()
    {
        var (e, _, _) = await ConCarrito((x => x.Digital(), 1), (x => x.Digital(), 1));

        var resultado = await Iniciar(e, "sede-central");

        Assert.True(resultado.EsExito);
        Assert.Equal(0, e.Inventario.ConsultasDeDisponibilidad);
        Assert.Equal(0, e.Inventario.ConsultasPrevias);
        Assert.True(resultado.Valor.TotalDePasos <= 4);
        Assert.Equal(TipoDeEntregaDeCompra.Digital, resultado.Valor.TipoDeEntrega);
        Assert.All(resultado.Valor.Lineas, l => Assert.Equal(DisponibilidadDeLineaDeCheckout.DisponibleAlInstante, l.Disponibilidad));
    }

    [Fact]
    public async Task Un_carrito_mixto_consulta_solo_los_impresos()
    {
        var impreso = Guid.Empty;
        var (e, _, _) = await ConCarrito((x => impreso = x.Impreso(5), 1), (x => x.Digital(), 1));

        var resultado = await Iniciar(e);

        Assert.True(resultado.EsExito);
        Assert.Equal([impreso], Assert.Single(e.Inventario.EdicionesConsultadas));
        Assert.Equal(2, resultado.Valor.Lineas.Count);
    }

    [Fact]
    public async Task Sin_carrito_o_con_el_carrito_vacio_se_rechaza_con_checkout_vacio()
    {
        var sinCarrito = EntornoDelCarrito.DeCliente();
        var (vacio, _, carrito) = await ConCarrito((x => x.Impreso(5), 1));
        await new EliminarLinea(vacio.Servicios()).Ejecutar(new EliminarLineaEntrada(carrito.Lineas.Single().EdicionId));

        var a = await Iniciar(sinCarrito);
        var b = await Iniciar(vacio);

        Assert.Equal(ClavesDelCarrito.CheckoutVacio, a.Error!.Clave);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, a.Error.Categoria);
        Assert.Equal(ClavesDelCarrito.CheckoutVacio, b.Error!.Clave);
        Assert.Empty(vacio.Checkouts.Todos);
        Assert.Equal(0, vacio.Inventario.ConsultasDeDisponibilidad);
    }

    [Fact]
    public async Task Las_lineas_con_retiro_pendiente_o_de_una_edicion_que_el_catalogo_ya_no_ofrece_no_entran_al_checkout()
    {
        var retirada = Guid.Empty;
        var (e, _, carrito) = await ConCarrito((x => x.Impreso(5), 1), (x => retirada = x.Impreso(5), 1));
        carrito.RetirarEdicion(retirada, diferir: true, Inicio);

        var resultado = await Iniciar(e);

        Assert.True(resultado.EsExito);
        Assert.Single(resultado.Valor.Lineas);
        Assert.Single(e.Checkouts.Todos.Single().Lineas);
    }

    [Fact]
    public async Task Si_todas_las_lineas_estan_retiradas_es_un_checkout_vacio()
    {
        var (e, _, carrito) = await ConCarrito((x => x.Impreso(5), 1));
        carrito.RetirarEdicion(carrito.Lineas.Single().EdicionId, diferir: true, Inicio);

        var resultado = await Iniciar(e);

        Assert.Equal(ClavesDelCarrito.CheckoutVacio, resultado.Error!.Clave);
    }

    // ---------- Checkout vivo ----------

    [Fact]
    public async Task Con_un_checkout_vivo_en_edicion_se_devuelve_ese_sin_crear_otro()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        var primero = (await Iniciar(e)).Valor;
        var segundo = await Iniciar(e);

        Assert.True(segundo.EsExito);
        Assert.Single(e.Checkouts.Todos);
        Assert.Equal(primero.Lineas.Single().LineaId, segundo.Valor.Lineas.Single().LineaId);
    }

    [Fact]
    public async Task Con_el_checkout_en_edicion_las_lineas_se_sincronizan_con_el_carrito_conservando_el_lineaId_de_las_que_siguen()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        var primero = (await Iniciar(e)).Valor;
        var otra = e.Impreso(5, 10m, "Otra");
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(otra, 2));

        var segundo = (await Iniciar(e)).Valor;

        Assert.Equal(2, segundo.Lineas.Count);
        Assert.Equal(primero.Lineas.Single().LineaId, segundo.Lineas[0].LineaId);
        Assert.Equal(2, e.Checkouts.Todos.Single().Lineas.Count);
    }

    [Fact]
    public async Task Con_el_checkout_en_pago_se_devuelve_con_su_pedido_y_la_reserva()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var libro = e.Impreso(5);
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        carrito.Agregar(libro, false, 1, Inicio);
        e.Carritos.Sembrar(carrito);
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, carrito.Id, [new LineaParaCheckout(libro, false, "Libro", "9780000000019", 1, 50m)], Guid.NewGuid, Inicio);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio);
        checkout.ElegirRecojo("sede-central", Inicio);
        checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, carrito, Inicio);
        checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito);
        e.Checkouts.Sembrar(checkout);

        var vista = (await Iniciar(e)).Valor;

        Assert.Equal(EstadoVisibleDelCheckout.EnPago, vista.Estado);
        Assert.Equal(checkout.PedidoId, vista.PedidoId);
        Assert.Equal(Inicio.AddMinutes(15), vista.ReservaExpiraEn);
        Assert.Equal(0, e.Inventario.ConsultasDeDisponibilidad);
    }

    [Fact]
    public async Task Si_dos_solicitudes_crean_el_checkout_a_la_vez_la_segunda_devuelve_el_que_existe()
    {
        var (e, cuenta, carrito) = await ConCarrito((x => x.Impreso(5), 1));
        var rival = Checkout.Iniciar(Guid.NewGuid(), cuenta, carrito.Id, [new LineaParaCheckout(carrito.Lineas.Single().EdicionId, false, "Libro", "9780000000019", 1, 50m)], Guid.NewGuid, Inicio);
        e.Checkouts.AntesDeAgregar = () =>
        {
            e.Checkouts.Sembrar(rival);
            throw new CheckoutYaExisteException();
        };

        var resultado = await Iniciar(e);

        Assert.True(resultado.EsExito);
        Assert.Same(rival, Assert.Single(e.Checkouts.Todos));
    }

    [Fact]
    public async Task El_stock_se_revalida_al_bloquear_si_otro_dispositivo_sumo_unidades()
    {
        var (e, _, carrito) = await ConCarrito((x => x.Impreso(3), 2));
        e.Carritos.AlBloquear = () => carrito.Agregar(carrito.Lineas.Single().EdicionId, false, 2, Inicio);

        var resultado = await Iniciar(e);

        Assert.Equal(ClavesDelCarrito.SinStock, resultado.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("maximo", "3"), resultado.Error.Errores);
        Assert.Empty(e.Checkouts.Todos);
    }

    // ---------- Alcance, cuenta y límites ----------

    [Fact]
    public async Task Sin_sesion_se_rechaza_y_el_personal_tambien()
    {
        var visitante = EntornoDelCarrito.DeVisitante();
        var encargado = new EntornoDelCarrito(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.NoAutenticado, (await Iniciar(visitante)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Iniciar(encargado)).Error!.Categoria);
        Assert.Equal(0, visitante.Inventario.ConsultasDeDisponibilidad);
    }

    [Fact]
    public async Task Una_cuenta_cancelada_o_con_Identidad_caida_no_inicia_el_checkout()
    {
        var (cancelada, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        cancelada.Cuenta.Cancelada = true;
        var (caida, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        caida.Cuenta.NoResponde = true;

        Assert.Equal(CategoriaDeError.Prohibido, (await Iniciar(cancelada)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await Iniciar(caida)).Error!.Categoria);
        Assert.Empty(cancelada.Checkouts.Todos);
        Assert.Empty(caida.Checkouts.Todos);
    }

    [Fact]
    public async Task Un_limite_excedido_responde_limite_y_uno_sin_almacen_falla_cerrado_sin_consultar_al_inventario()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));
        e.Limites.Revisar = new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, Inicio.AddSeconds(20));
        var excedido = await Iniciar(e);
        e.Limites.Revisar = ResultadoDeLimiteDelCarrito.NoDisponible;
        var sinAlmacen = await Iniciar(e);

        Assert.Equal(CategoriaDeError.Limite, excedido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, sinAlmacen.Error!.Categoria);
        Assert.Equal(0, e.Inventario.ConsultasDeDisponibilidad);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SEDE CENTRAL")]
    [InlineData("sede/central")]
    public async Task Una_sucursal_preferida_con_formato_invalido_es_una_validacion_que_no_repite_el_valor(string sucursal)
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5), 1));

        var resultado = await Iniciar(e, sucursal);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("codigoDeSucursalPreferida", Assert.Single(resultado.Error.Errores).Campo);
        Assert.DoesNotContain("sede", Assert.Single(resultado.Error.Errores).Clave, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task La_vista_usa_el_precio_vigente_del_catalogo_y_el_total_sin_envio_mientras_no_haya_entrega()
    {
        var (e, _, _) = await ConCarrito((x => x.Impreso(5, 37.30m), 3));

        var vista = (await Iniciar(e)).Valor;

        Assert.Equal(111.90m, vista.Subtotal);
        Assert.Null(vista.TipoDeEntrega);
        Assert.Equal(111.90m, vista.Total);
        Assert.Null(vista.Contacto);
        Assert.Equal([TextoLegalDeCompra.Terminos, TextoLegalDeCompra.Privacidad], vista.TextosPorAceptar);
    }
}
