using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// Las correcciones de las revisiones de seguridad y de arquitectura de ECO-4b: el checkout que cambia dentro de la edición o la fusión se guarda (también cuando la operación se rechaza tras reabrir), los rechazos y los no-op
/// se deciden antes de reabrir la intención, la carrera al crear el carrito se resuelve rehaciendo la transacción, la fusión no usa un terreno obsoleto, y la vista no confunde un fallo del Catálogo con una edición retirada.
/// </summary>
public sealed class CorreccionesDeLaRevisionDeEco4bTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static (EntornoDelCarrito Entorno, Checkout Checkout, CarritoDeDominio Carrito, Guid Libro) EnPago(int cantidad = 2)
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var libro = e.Impreso(200);
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        carrito.Agregar(libro, false, cantidad, Inicio);
        e.Carritos.Sembrar(carrito);
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, carrito.Id, [new LineaParaCheckout(libro, false, "Libro", "9780000000019", cantidad, 50m)], Guid.NewGuid, Inicio);
        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio);
        checkout.ElegirRecojo("lima", Inicio);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, carrito, Inicio).Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarEnPago(checkout.PedidoId!.Value, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(15), carrito).Efecto);
        e.Checkouts.Sembrar(checkout);
        return (e, checkout, carrito, libro);
    }

    // ---------- El checkout que cambia se guarda ----------

    [Fact]
    public async Task Editar_con_el_checkout_en_pago_guarda_el_checkout_devuelto_a_edicion()
    {
        var (e, checkout, _, libro) = EnPago();

        var resultado = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(libro, 5));

        Assert.True(resultado.EsExito);
        Assert.Same(checkout, Assert.Single(e.Checkouts.Guardados));
    }

    [Fact]
    public async Task El_aborto_desde_en_pago_guarda_el_pedido_descartado_y_la_liberacion_anotada()
    {
        var (e, checkout, _, libro) = EnPago();
        e.Pagos.AlReabrir = ResultadoDeReabrirIntencion.Cerrada;

        await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(libro, 5));

        var guardado = Assert.Single(e.Checkouts.Guardados);
        Assert.Same(checkout, guardado);
        Assert.NotNull(guardado.PedidoDescartado);
        Assert.NotNull(guardado.ReservaALiberarDesde);
    }

    [Fact]
    public async Task Si_la_operacion_se_rechaza_despues_de_reabrir_el_checkout_igual_se_guarda()
    {
        var (e, checkout, carrito, libro) = EnPago(cantidad: 18);

        // Con el stock sin confirmar no se puede prevalidar: el rechazo de las 20 unidades lo da el Dominio ya con la intención reabierta.
        e.Disponibilidad.Digital(libro);
        var resultado = await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, 5));

        Assert.Equal(ClavesDelCarrito.LimiteDeUnidades, resultado.Error!.Clave);
        Assert.Equal(1, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Same(checkout, Assert.Single(e.Checkouts.Guardados));
        Assert.Equal(18, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Un_rechazo_sin_efectos_previos_no_guarda_nada()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(1);
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, 1));
        var guardados = e.Carritos.Guardados;

        var resultado = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(libro, 5));

        Assert.False(resultado.EsExito);
        Assert.Equal(guardados, e.Carritos.Guardados);
        Assert.Empty(e.Checkouts.Guardados);
    }

    // ---------- Antes de reabrir ----------

    [Fact]
    public async Task Un_rechazo_por_stock_se_decide_antes_de_reabrir_la_intencion()
    {
        var (e, checkout, carrito, libro) = EnPago();
        e.Disponibilidad.Vendible(libro, 2);

        var resultado = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(libro, 6));

        Assert.Equal(ClavesDelCarrito.SinStock, resultado.Error!.Clave);
        Assert.Equal(0, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Equal(2, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Los_no_op_no_reabren_la_intencion()
    {
        var (e, checkout, carrito, libro) = EnPago();

        var igual = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(libro, 2));
        var inexistente = await new EliminarLinea(e.Servicios()).Ejecutar(new EliminarLineaEntrada(Guid.NewGuid()));

        Assert.True(igual.EsExito);
        Assert.True(inexistente.EsExito);
        Assert.Equal(0, e.Pagos.Reaperturas);
        Assert.Equal(EstadoDelCheckout.EnPago, checkout.Estado);
        Assert.Equal(2, carrito.Lineas.Single().Cantidad.Valor);
        Assert.Empty(e.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task Agregar_una_digital_que_ya_esta_en_el_carrito_es_un_no_op()
    {
        var e = EntornoDelCarrito.DeCliente();
        var digital = e.Digital();
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(digital, 1));
        var guardados = e.Carritos.Guardados;

        var resultado = await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(digital, 1));

        Assert.True(resultado.EsExito);
        Assert.Equal(guardados, e.Carritos.Guardados);
    }

    // ---------- La carrera al crear el carrito ----------

    [Fact]
    public async Task Si_otro_dispositivo_crea_el_carrito_a_la_vez_la_transaccion_se_rehace_y_la_linea_queda_en_el_que_existe()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var otra = e.Impreso(5);
        var libro = e.Impreso(5);
        var rival = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        rival.Agregar(otra, false, 1, Inicio);
        e.Carritos.AntesDeAgregar = () =>
        {
            e.Carritos.Sembrar(rival);
            throw new CarritoYaExisteException();
        };

        var resultado = await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, 1));

        Assert.True(resultado.EsExito);
        var carrito = Assert.Single(e.Carritos.Todos);
        Assert.Equal([otra, libro], carrito.Lineas.Select(l => l.EdicionId));
        Assert.Equal(2, e.Unidad.Ejecuciones);
    }

    // ---------- La fusión ----------

    [Fact]
    public async Task La_fusion_con_el_checkout_en_pago_guarda_el_checkout()
    {
        var (e, checkout, carrito, libro) = EnPago();
        var cookie = new string('A', 43);
        Assert.True(HuellaDeLaCookie.TryDe(cookie, out var huella));
        var visitante = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(huella!), Inicio);
        visitante.Agregar(libro, false, 1, Inicio);
        e.Carritos.Sembrar(visitante);

        var resultado = await new FusionarCarritoDeVisitante(e.Servicios()).Ejecutar(new FusionarCarritoEntrada(cookie));

        Assert.False(resultado.Valor.Aplazada);
        Assert.Same(checkout, Assert.Single(e.Checkouts.Guardados));
        Assert.Equal(EstadoDelCheckout.EnEdicion, checkout.Estado);
        Assert.Equal(3, carrito.Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Si_el_visitante_agrega_una_linea_despues_de_armar_el_terreno_la_fusion_se_aplaza_sin_marcarlo_fusionado()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var a = e.Impreso(5);
        var b = e.Impreso(5);
        var cookie = new string('A', 43);
        Assert.True(HuellaDeLaCookie.TryDe(cookie, out var huella));
        var delaCuenta = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        e.Carritos.Sembrar(delaCuenta);
        var visitante = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(huella!), Inicio);
        visitante.Agregar(a, false, 1, Inicio);
        e.Carritos.Sembrar(visitante);
        e.Carritos.AlBloquear = () => visitante.Agregar(b, false, 1, Inicio);

        var resultado = await new FusionarCarritoDeVisitante(e.Servicios()).Ejecutar(new FusionarCarritoEntrada(cookie));

        Assert.True(resultado.Valor.Aplazada);
        Assert.False(resultado.Valor.BorrarLaCookie);
        Assert.Equal(EstadoDeCarrito.Abierto, visitante.Estado);
        Assert.Empty(delaCuenta.Lineas);
    }

    // ---------- La vista ----------

    [Fact]
    public async Task Si_el_catalogo_no_responde_la_vista_no_marca_las_lineas_como_retiradas_y_avisa_que_los_precios_no_se_confirman()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(libro, 2));
        e.Catalogo.Caido = true;

        var vista = (await new ObtenerCarrito(e.Servicios()).Ejecutar(new SolicitudSobreElCarrito())).Valor.Carrito;

        Assert.True(vista.PreciosNoConfirmados);
        Assert.False(vista.Lineas.Single().Retirada);
        Assert.Equal(2, vista.Lineas.Single().Cantidad);
    }

    [Fact]
    public async Task Una_edicion_que_el_catalogo_ya_no_conoce_si_se_marca_retirada_cuando_el_catalogo_responde()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        carrito.Agregar(Guid.NewGuid(), false, 1, Inicio);
        e.Carritos.Sembrar(carrito);

        var vista = (await new ObtenerCarrito(e.Servicios()).Ejecutar(new SolicitudSobreElCarrito())).Valor.Carrito;

        Assert.False(vista.PreciosNoConfirmados);
        Assert.True(vista.Lineas.Single().Retirada);
    }

    // ---------- La cookie no se imprime ----------

    [Fact]
    public void Los_modelos_que_llevan_la_cookie_del_visitante_no_la_imprimen()
    {
        var cookie = new string('Z', 43);
        var libro = Guid.NewGuid();
        object[] modelos =
        [
            new AgregarLineaEntrada(libro, 1, cookie),
            new ModificarLineaEntrada(libro, 1, cookie),
            new EliminarLineaEntrada(libro, cookie),
            new SolicitudSobreElCarrito(cookie),
            new FusionarCarritoEntrada(cookie),
            new ResultadoDeCarrito(new VistaDelCarrito([], 0m, [], false), cookie, false),
        ];

        Assert.All(modelos, m => Assert.DoesNotContain(cookie, m.ToString(), StringComparison.Ordinal));
    }

    // ---------- Avisos vistos ----------

    [Fact]
    public async Task Marcar_los_avisos_como_vistos_los_quita_de_la_vista()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var aviso = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "Libro", null, null, Inicio);
        await e.Carritos.AgregarAvisos(cuenta, [aviso]);

        var resultado = await new MarcarAvisosComoVistos(e.Servicios()).Ejecutar(new MarcarAvisosEntrada([aviso.Id]));

        Assert.True(resultado.EsExito);
        Assert.Empty(resultado.Valor.Carrito.Avisos);
    }

    [Fact]
    public async Task Marcar_avisos_exige_sesion_de_cliente_y_una_lista_valida()
    {
        var visitante = EntornoDelCarrito.DeVisitante();
        var cliente = EntornoDelCarrito.DeCliente();

        Assert.Equal(CategoriaDeError.NoAutenticado, (await new MarcarAvisosComoVistos(visitante.Servicios()).Ejecutar(new MarcarAvisosEntrada([Guid.NewGuid()]))).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, (await new MarcarAvisosComoVistos(cliente.Servicios()).Ejecutar(new MarcarAvisosEntrada([]))).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, (await new MarcarAvisosComoVistos(cliente.Servicios()).Ejecutar(new MarcarAvisosEntrada([Guid.Empty]))).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, (await new MarcarAvisosComoVistos(cliente.Servicios()).Ejecutar(new MarcarAvisosEntrada([.. Enumerable.Range(0, 51).Select(_ => Guid.NewGuid())]))).Error!.Categoria);
    }

    [Fact]
    public async Task Un_aviso_ajeno_no_se_marca_ni_se_revela()
    {
        var e = EntornoDelCarrito.DeCliente();
        var otra = Guid.NewGuid();
        var ajeno = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "Libro", null, null, Inicio);
        await e.Carritos.AgregarAvisos(otra, [ajeno]);

        var resultado = await new MarcarAvisosComoVistos(e.Servicios()).Ejecutar(new MarcarAvisosEntrada([ajeno.Id]));

        Assert.True(resultado.EsExito);
        Assert.Single(await e.Carritos.AvisosSinVer(otra));
    }
}
