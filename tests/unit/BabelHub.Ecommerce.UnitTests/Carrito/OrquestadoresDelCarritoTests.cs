using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Soporte;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T10: <c>ObtenerCarrito</c>, <c>AgregarLinea</c>, <c>ModificarLinea</c> y <c>EliminarLinea</c> para el visitante y la cuenta (CAR-01 a CAR-04, CAR-20, CAR-26; AC-CAR-01, 02, 03, 18, 25): la disponibilidad y la posesión se obtienen antes de abrir
/// la transacción, el máximo viaja en el rechazo, una digital queda en una unidad, la ya poseída se rechaza solo con sesión, y se rechaza con el pago pendiente o con el checkout <c>reservando</c>.
/// </summary>
public sealed class OrquestadoresDelCarritoTests
{
    private static async Task<Resultado<ResultadoDeCarrito>> Agregar(EntornoDelCarrito e, Guid edicion, int cantidad, string? cookie = null) =>
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(edicion, cantidad, cookie));

    private static async Task<Resultado<ResultadoDeCarrito>> Modificar(EntornoDelCarrito e, Guid edicion, int cantidad, string? cookie = null) =>
        await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(edicion, cantidad, cookie));

    private static async Task<Resultado<ResultadoDeCarrito>> Eliminar(EntornoDelCarrito e, Guid edicion, string? cookie = null) =>
        await new EliminarLinea(e.Servicios()).Ejecutar(new EliminarLineaEntrada(edicion, cookie));

    private static async Task<Resultado<ResultadoDeCarrito>> Obtener(EntornoDelCarrito e, string? cookie = null) =>
        await new ObtenerCarrito(e.Servicios()).Ejecutar(new SolicitudSobreElCarrito(cookie));

    private static string Clave(ErrorDeAplicacion? error) => error?.Clave ?? "(éxito)";

    // ---------- Agregar, con sesión ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-01")]
    public async Task Un_cliente_agrega_dos_lineas_y_las_ve_iguales_con_el_mismo_total_desde_otro_dispositivo()
    {
        var e = EntornoDelCarrito.DeCliente();
        var a = e.Impreso(10, 37.30m, "Uno");
        var b = e.Impreso(10, 10m, "Dos");

        await Agregar(e, a, 3);
        await Agregar(e, b, 1);
        var otroDispositivo = await Obtener(e);

        var vista = otroDispositivo.Valor.Carrito;
        Assert.Equal(2, vista.Lineas.Count);
        Assert.Equal(121.90m, vista.Subtotal);
        Assert.Equal([a, b], vista.Lineas.Select(l => l.EdicionId));
        Assert.Equal(3, vista.Lineas[0].Cantidad);
        Assert.Equal(111.90m, vista.Lineas[0].Importe);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-02")]
    public async Task Una_digital_con_dos_unidades_queda_en_una_junto_al_impreso()
    {
        var e = EntornoDelCarrito.DeCliente();
        var impreso = e.Impreso(10);
        var digital = e.Digital();

        await Agregar(e, impreso, 1);
        var resultado = await Agregar(e, digital, 2);

        Assert.True(resultado.EsExito);
        Assert.Equal([1, 1], resultado.Valor.Carrito.Lineas.Select(l => l.Cantidad));
        Assert.Equal(FormatoDeEdicion.Digital, resultado.Valor.Carrito.Lineas[1].Formato);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-03")]
    public async Task Pedir_cinco_con_tres_vendibles_se_rechaza_con_el_maximo_de_tres()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(3);

        var resultado = await Agregar(e, libro, 5);

        Assert.Equal(ClavesDelCarrito.SinStock, Clave(resultado.Error));
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("maximo", "3"), resultado.Error.Errores);
        Assert.Empty(e.Carritos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-03")]
    public async Task La_suma_con_lo_que_ya_hay_en_el_carrito_tambien_respeta_el_stock()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(3);
        await Agregar(e, libro, 2);

        var resultado = await Agregar(e, libro, 2);

        Assert.Equal(ClavesDelCarrito.SinStock, Clave(resultado.Error));
        Assert.Contains(new ErrorDeCampo("maximo", "3"), resultado.Error!.Errores);
        Assert.Equal(2, e.Carritos.Todos.Single().Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Un_impreso_agotado_se_rechaza_con_maximo_cero()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(0);

        var resultado = await Agregar(e, libro, 1);

        Assert.Equal(ClavesDelCarrito.SinStock, Clave(resultado.Error));
        Assert.Contains(new ErrorDeCampo("maximo", "0"), resultado.Error!.Errores);
    }

    [Fact]
    public async Task Si_no_se_puede_confirmar_el_stock_la_linea_se_acepta()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = Guid.NewGuid();
        e.Catalogo.Poner(libro, FormatoDeEdicion.Impresa, 40m);

        var resultado = await Agregar(e, libro, 5);

        Assert.True(resultado.EsExito);
        Assert.Equal(5, resultado.Valor.Carrito.Lineas.Single().Cantidad);
        Assert.Equal(FrescuraDeLaDisponibilidad.NoSePuedeConfirmar, resultado.Valor.Carrito.Lineas.Single().Disponibilidad!.Frescura);
    }

    [Fact]
    public async Task Si_la_disponibilidad_falla_la_linea_tambien_se_acepta()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(1);
        e.Disponibilidad.Caida = true;

        var resultado = await Agregar(e, libro, 4);

        Assert.True(resultado.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-18")]
    public async Task Una_digital_que_la_cuenta_ya_posee_se_rechaza_con_sesion()
    {
        var e = EntornoDelCarrito.DeCliente();
        var digital = e.Digital();
        e.Posesion.Poseer(digital);

        var resultado = await Agregar(e, digital, 1);

        Assert.Equal(ClavesDelCarrito.DigitalYaPoseida, Clave(resultado.Error));
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Empty(e.Carritos.Todos);
    }

    [Fact]
    public async Task Si_Biblioteca_no_responde_la_digital_se_acepta_y_se_comprueba_al_pagar()
    {
        var e = EntornoDelCarrito.DeCliente();
        var digital = e.Digital();
        e.Posesion.NoResponde = true;

        Assert.True((await Agregar(e, digital, 1)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-18")]
    public async Task Un_visitante_no_pasa_por_la_posesion_porque_no_tiene_cuenta()
    {
        var e = EntornoDelCarrito.DeVisitante();
        var digital = e.Digital();
        e.Posesion.Poseer(digital);

        var resultado = await Agregar(e, digital, 1);

        Assert.True(resultado.EsExito);
        Assert.Equal(0, e.Posesion.Consultas);
    }

    [Fact]
    public async Task Una_edicion_que_el_catalogo_no_ofrece_se_rechaza()
    {
        var e = EntornoDelCarrito.DeCliente();
        var retirada = Guid.NewGuid();
        e.Catalogo.Poner(retirada, FormatoDeEdicion.Impresa, 10m, estado: EstadoDeEdicionConsultado.RetiradaDeVenta, motivo: MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta);
        var vencida = Guid.NewGuid();
        e.Catalogo.Poner(vencida, FormatoDeEdicion.Digital, 10m, motivo: MotivoDeNoDisponibleDeEdicion.LicenciaVencida);

        Assert.Equal(ClavesDelCarrito.EdicionNoDisponible, Clave((await Agregar(e, retirada, 1)).Error));
        Assert.Equal(ClavesDelCarrito.EdicionNoDisponible, Clave((await Agregar(e, vencida, 1)).Error));
        Assert.Equal(ClavesDelCarrito.EdicionNoDisponible, Clave((await Agregar(e, Guid.NewGuid(), 1)).Error));
    }

    [Fact]
    public async Task Si_el_catalogo_no_responde_no_se_agrega_nada()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);
        e.Catalogo.Caido = true;

        var resultado = await Agregar(e, libro, 1);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(e.Carritos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-32")]
    public async Task La_linea_31_se_rechaza_con_su_motivo()
    {
        var e = EntornoDelCarrito.DeCliente();
        for (var i = 0; i < 30; i++)
        {
            Assert.True((await Agregar(e, e.Impreso(10), 1)).EsExito);
        }

        var resultado = await Agregar(e, e.Impreso(10), 1);

        Assert.Equal(ClavesDelCarrito.LimiteDeLineas, Clave(resultado.Error));
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error!.Categoria);
        Assert.Equal(30, e.Carritos.Todos.Single().Lineas.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-32")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    [InlineData(100)]
    public async Task Una_cantidad_fuera_de_1_a_20_es_una_validacion_que_no_repite_el_valor(int cantidad)
    {
        var e = EntornoDelCarrito.DeCliente();

        var resultado = await Agregar(e, e.Impreso(500), cantidad);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("cantidad", Assert.Single(resultado.Error.Errores).Campo);
        Assert.DoesNotContain(cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(resultado.Error.Errores).Clave, StringComparison.Ordinal);
        Assert.Equal(0, e.Catalogo.Consultas);
    }

    [Fact]
    public async Task Una_edicion_vacia_es_una_validacion()
    {
        var e = EntornoDelCarrito.DeCliente();

        var resultado = await Agregar(e, Guid.Empty, 1);

        Assert.Equal("edicionId", Assert.Single(resultado.Error!.Errores).Campo);
    }

    // ---------- Cuenta y límites ----------

    [Fact]
    public async Task Una_cuenta_cancelada_o_desactivada_no_agrega()
    {
        var e = EntornoDelCarrito.DeCliente();
        e.Cuenta.Cancelada = true;

        var resultado = await Agregar(e, e.Impreso(5), 1);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(ClavesDelCarrito.CuentaNoPuedeOperar, resultado.Error.Clave);
        Assert.Empty(e.Carritos.Todos);
    }

    [Fact]
    public async Task Si_Identidad_no_responde_o_la_cuenta_no_existe_falla_cerrado()
    {
        var sinRespuesta = EntornoDelCarrito.DeCliente();
        sinRespuesta.Cuenta.NoResponde = true;
        var inexistente = EntornoDelCarrito.DeCliente();
        inexistente.Cuenta.NoExiste = true;

        Assert.Equal(CategoriaDeError.NoDisponible, (await Agregar(sinRespuesta, sinRespuesta.Impreso(5), 1)).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await Agregar(inexistente, inexistente.Impreso(5), 1)).Error!.Categoria);
    }

    [Fact]
    public async Task Un_visitante_no_consulta_a_Identidad()
    {
        var e = EntornoDelCarrito.DeVisitante();

        await Agregar(e, e.Impreso(5), 1);

        Assert.Equal(0, e.Cuenta.Consultas);
    }

    [Fact]
    public async Task Un_limite_excedido_responde_limite_con_el_instante_de_reintento_y_uno_sin_almacen_falla_cerrado()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);
        var reintento = EntornoDelCarrito.Inicio.AddSeconds(30);
        e.Limites.Editar = new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, reintento);

        var excedido = await Agregar(e, libro, 1);
        e.Limites.Editar = ResultadoDeLimiteDelCarrito.NoDisponible;
        var sinAlmacen = await Agregar(e, libro, 1);

        Assert.Equal(CategoriaDeError.Limite, excedido.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("disponibleDesde", reintento.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), excedido.Error.Errores);
        Assert.Equal(CategoriaDeError.NoDisponible, sinAlmacen.Error!.Categoria);
        Assert.Empty(e.Carritos.Todos);
    }

    [Theory]
    [InlineData("encargado")]
    [InlineData("sistema")]
    public async Task El_personal_y_el_sistema_se_rechazan_sin_tocar_nada(string tipo)
    {
        var actor = tipo == "encargado" ? ActorDePrueba.DeEncargadoDe(Guid.NewGuid()) : ActorDePrueba.DelSistema();
        var e = new EntornoDelCarrito(actor);

        var resultado = await Agregar(e, e.Impreso(5), 1);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(0, e.Catalogo.Consultas);
        Assert.Empty(e.Carritos.Todos);
        Assert.Single(e.Denegaciones.Eventos);
    }

    // ---------- Visitante ----------

    [Fact]
    public async Task Un_visitante_sin_cookie_crea_su_carrito_y_recibe_la_cookie_nueva()
    {
        var e = EntornoDelCarrito.DeVisitante();

        var resultado = await Agregar(e, e.Impreso(5), 2);

        Assert.NotNull(resultado.Valor.CookieNueva);
        Assert.Equal(HuellaDeLaCookie.LongitudDeLaCookie, resultado.Valor.CookieNueva!.Length);
        Assert.Equal(1, e.Limites.CarritosCreados);
        var carrito = Assert.Single(e.Carritos.Todos);
        Assert.True(carrito.Propietario.EsDeVisitante);
        Assert.True(HuellaDeLaCookie.TryDe(resultado.Valor.CookieNueva, out var huella));
        Assert.True(carrito.Propietario.HuellaDelVisitante.SequenceEqual(huella!));
    }

    [Fact]
    public async Task Con_su_cookie_el_visitante_sigue_en_el_mismo_carrito_y_no_crea_otro()
    {
        var e = EntornoDelCarrito.DeVisitante();
        var primera = await Agregar(e, e.Impreso(5), 1);
        var cookie = primera.Valor.CookieNueva;

        var segunda = await Agregar(e, e.Impreso(5), 1, cookie);

        Assert.Null(segunda.Valor.CookieNueva);
        Assert.Equal(2, segunda.Valor.Carrito.Lineas.Count);
        Assert.Single(e.Carritos.Todos);
        Assert.Equal(1, e.Limites.CarritosCreados);
    }

    [Fact]
    public async Task Un_limite_de_carritos_de_visitante_excedido_no_crea_ninguno()
    {
        var e = EntornoDelCarrito.DeVisitante();
        e.Limites.CrearCarritos = new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, EntornoDelCarrito.Inicio.AddMinutes(10));

        var resultado = await Agregar(e, e.Impreso(5), 1);

        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Empty(e.Carritos.Todos);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("")]
    public async Task Una_cookie_mal_formada_es_sin_carrito_y_se_crea_uno_nuevo(string cookie)
    {
        var e = EntornoDelCarrito.DeVisitante();

        var resultado = await Agregar(e, e.Impreso(5), 1, cookie);

        Assert.NotNull(resultado.Valor.CookieNueva);
        Assert.Single(e.Carritos.Todos);
    }

    [Fact]
    public async Task Una_cookie_desconocida_es_sin_carrito_al_ver_y_se_borra()
    {
        var e = EntornoDelCarrito.DeVisitante();

        var resultado = await Obtener(e, new string('A', 43));

        Assert.Empty(resultado.Valor.Carrito.Lineas);
        Assert.True(resultado.Valor.BorrarLaCookie);
        Assert.Null(resultado.Valor.CookieNueva);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-25")]
    public async Task Un_visitante_que_lleva_31_dias_sin_volver_encuentra_su_carrito_caducado()
    {
        var e = EntornoDelCarrito.DeVisitante();
        var libro = e.Impreso(5);
        var primera = await Agregar(e, libro, 1);
        e.Reloj.Ahora = EntornoDelCarrito.Inicio.AddDays(31);

        var vista = await Obtener(e, primera.Valor.CookieNueva);

        Assert.Empty(vista.Valor.Carrito.Lineas);
        Assert.True(vista.Valor.BorrarLaCookie);
    }

    [Fact]
    public async Task Una_cookie_de_un_carrito_fusionado_es_sin_carrito()
    {
        var e = EntornoDelCarrito.DeVisitante();
        var primera = await Agregar(e, e.Impreso(5), 1);
        e.Carritos.Todos.Single().MarcarFusionado(EntornoDelCarrito.Inicio);

        var vista = await Obtener(e, primera.Valor.CookieNueva);

        Assert.Empty(vista.Valor.Carrito.Lineas);
        Assert.True(vista.Valor.BorrarLaCookie);
    }

    // ---------- Ver ----------

    [Fact]
    public async Task Ver_el_carrito_no_tiene_efectos_no_consume_limites_ni_guarda_nada()
    {
        var e = EntornoDelCarrito.DeCliente();
        await Agregar(e, e.Impreso(5), 1);
        var guardados = e.Carritos.Guardados;
        var ediciones = e.Limites.Ediciones;

        var vista = await Obtener(e);

        Assert.Single(vista.Valor.Carrito.Lineas);
        Assert.Equal(guardados, e.Carritos.Guardados);
        Assert.Equal(ediciones, e.Limites.Ediciones);
        Assert.Empty(e.Bitacora.Bloqueos.Skip(2));
    }

    [Fact]
    public async Task Ver_el_carrito_muestra_el_precio_vigente_del_catalogo_y_la_disponibilidad()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(8, 50m, "Pedro Páramo");
        await Agregar(e, libro, 2);
        e.Catalogo.Poner(libro, FormatoDeEdicion.Impresa, 55m, "Pedro Páramo");

        var vista = (await Obtener(e)).Valor.Carrito;

        Assert.Equal(55m, vista.Lineas.Single().PrecioUnitario);
        Assert.Equal(110m, vista.Subtotal);
        Assert.Equal("Pedro Páramo", vista.Lineas.Single().Titulo);
        Assert.Equal(8, vista.Lineas.Single().Disponibilidad!.VendibleEnLaRed);
    }

    [Fact]
    public async Task Un_cliente_sin_carrito_ve_uno_vacio_y_no_se_crea_nada()
    {
        var e = EntornoDelCarrito.DeCliente();

        var vista = await Obtener(e);

        Assert.Empty(vista.Valor.Carrito.Lineas);
        Assert.Equal(0m, vista.Valor.Carrito.Subtotal);
        Assert.Empty(e.Carritos.Todos);
    }

    [Fact]
    public async Task El_carrito_de_otra_cuenta_nunca_se_ve()
    {
        var e = EntornoDelCarrito.DeCliente();
        var ajeno = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), EntornoDelCarrito.Inicio);
        ajeno.Agregar(e.Impreso(5), false, 1, EntornoDelCarrito.Inicio);
        e.Carritos.Sembrar(ajeno);

        var vista = await Obtener(e);

        Assert.Empty(vista.Valor.Carrito.Lineas);
    }

    // ---------- Modificar y eliminar ----------

    [Fact]
    public async Task Modificar_cambia_la_cantidad_y_el_total()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(10, 20m);
        await Agregar(e, libro, 1);

        var resultado = await Modificar(e, libro, 4);

        Assert.Equal(4, resultado.Valor.Carrito.Lineas.Single().Cantidad);
        Assert.Equal(80m, resultado.Valor.Carrito.Subtotal);
    }

    [Fact]
    public async Task Modificar_por_encima_del_stock_se_rechaza_con_el_maximo_pero_bajar_siempre_se_puede()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);
        await Agregar(e, libro, 5);
        e.Disponibilidad.Vendible(libro, 2);

        var sube = await Modificar(e, libro, 6);
        var baja = await Modificar(e, libro, 4);

        Assert.Equal(ClavesDelCarrito.SinStock, Clave(sube.Error));
        Assert.Contains(new ErrorDeCampo("maximo", "2"), sube.Error!.Errores);
        Assert.True(baja.EsExito);
        Assert.Equal(4, baja.Valor.Carrito.Lineas.Single().Cantidad);
    }

    [Fact]
    public async Task Modificar_una_linea_que_no_esta_es_no_encontrada()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);
        await Agregar(e, e.Impreso(5), 1);

        var resultado = await Modificar(e, libro, 2);

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Equal(ClavesDelCarrito.LineaNoEncontrada, resultado.Error.Clave);
    }

    [Fact]
    public async Task Eliminar_quita_la_linea_y_repetirlo_es_un_exito_sin_cambios()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5, 10m);
        var otro = e.Impreso(5, 30m);
        await Agregar(e, libro, 1);
        await Agregar(e, otro, 1);

        var uno = await Eliminar(e, libro);
        var dos = await Eliminar(e, libro);

        Assert.Equal([otro], uno.Valor.Carrito.Lineas.Select(l => l.EdicionId));
        Assert.True(dos.EsExito);
        Assert.Equal(30m, dos.Valor.Carrito.Subtotal);
    }

    [Fact]
    public async Task Eliminar_sin_carrito_es_un_exito_con_el_carrito_vacio_y_no_crea_nada()
    {
        var e = EntornoDelCarrito.DeVisitante();

        var resultado = await Eliminar(e, Guid.NewGuid());

        Assert.True(resultado.EsExito);
        Assert.Empty(e.Carritos.Todos);
        Assert.Equal(0, e.Limites.CarritosCreados);
    }

    [Fact]
    public async Task Modificar_sin_carrito_es_no_encontrado_y_no_crea_nada()
    {
        var e = EntornoDelCarrito.DeVisitante();

        var resultado = await Modificar(e, e.Impreso(5), 1);

        Assert.Equal(ClavesDelCarrito.LineaNoEncontrada, Clave(resultado.Error));
        Assert.Empty(e.Carritos.Todos);
    }

    // ---------- El checkout vivo (CAR-26) ----------

    private static (EntornoDelCarrito Entorno, Guid Libro) ConCheckoutEn(Func<Checkout, CarritoDeDominio, bool> llevar)
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var libro = e.Impreso(10);
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), EntornoDelCarrito.Inicio);
        carrito.Agregar(libro, false, 1, EntornoDelCarrito.Inicio);
        e.Carritos.Sembrar(carrito);
        var checkout = Checkout.Iniciar(Guid.NewGuid(), cuenta, carrito.Id, [new LineaParaCheckout(libro, false, "Libro", "9780000000019", 1, 50m)], Guid.NewGuid, EntornoDelCarrito.Inicio);
        Assert.True(llevar(checkout, carrito));
        e.Checkouts.Sembrar(checkout);
        return (e, libro);
    }

    private static bool AReservando(Checkout c, CarritoDeDominio k)
    {
        c.Aceptar(TextoLegal.Terminos, "t-1", EntornoDelCarrito.Inicio);
        c.Aceptar(TextoLegal.Privacidad, "p-1", EntornoDelCarrito.Inicio);
        c.ElegirRecojo("lima", EntornoDelCarrito.Inicio);
        return c.PrepararPasoAlPago(Guid.NewGuid(), CostoDeEnvio.Cero, s => s, s => s, k, EntornoDelCarrito.Inicio).Efecto == EfectoDeTransicion.Aplicada;
    }

    private static bool AEnPago(Checkout c, CarritoDeDominio k) =>
        AReservando(c, k) && c.ConfirmarEnPago(c.PedidoId!.Value, c.GeneracionDeReserva, c.HuellaDeInstantanea!, EntornoDelCarrito.Inicio.AddMinutes(15), k).Efecto == EfectoDeTransicion.Aplicada;

    private static bool APagoPendiente(Checkout c, CarritoDeDominio k) =>
        AEnPago(c, k) && c.PagoPendiente(c.PedidoId!.Value, EntornoDelCarrito.Inicio.AddMinutes(30), EntornoDelCarrito.Inicio.AddMinutes(15), EntornoDelCarrito.Inicio).Efecto == EfectoDeTransicion.Aplicada;

    [Fact]
    public async Task Con_el_pago_pendiente_no_se_edita_el_carrito_y_nada_cambia()
    {
        var (e, libro) = ConCheckoutEn(APagoPendiente);

        var agregar = await Agregar(e, e.Impreso(5), 1);
        var modificar = await Modificar(e, libro, 3);
        var eliminar = await Eliminar(e, libro);

        Assert.All([agregar, modificar, eliminar], r => Assert.Equal(ClavesDelCarrito.PagoPendiente, Clave(r.Error)));
        Assert.All([agregar, modificar, eliminar], r => Assert.Equal(CategoriaDeError.Conflicto, r.Error!.Categoria));
        Assert.Equal(1, e.Carritos.Todos.Single().Lineas.Single().Cantidad.Valor);
    }

    [Fact]
    public async Task Con_el_checkout_reservando_se_responde_en_curso()
    {
        var (e, libro) = ConCheckoutEn(AReservando);

        Assert.Equal(ClavesDelCarrito.EnCurso, Clave((await Agregar(e, e.Impreso(5), 1)).Error));
        Assert.Equal(ClavesDelCarrito.EnCurso, Clave((await Modificar(e, libro, 2)).Error));
        Assert.Equal(ClavesDelCarrito.EnCurso, Clave((await Eliminar(e, libro)).Error));
    }

    [Fact]
    public async Task Con_el_checkout_en_edicion_se_edita_con_normalidad_bloqueando_primero_el_checkout_y_despues_el_carrito()
    {
        var (e, libro) = ConCheckoutEn((_, _) => true);

        var resultado = await Modificar(e, libro, 3);

        Assert.True(resultado.EsExito);
        Assert.Equal(["checkout", "carrito"], e.Bitacora.Bloqueos);
    }

    // ---------- Retiros pendientes huérfanos ----------

    [Fact]
    public async Task Una_anotacion_de_retiro_huerfana_la_quita_la_primera_lectura_con_bloqueo_y_deja_el_aviso()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var retirada = e.Impreso(5, 10m, "Libro retirado");
        var otra = e.Impreso(5);
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), EntornoDelCarrito.Inicio);
        carrito.Agregar(retirada, false, 1, EntornoDelCarrito.Inicio);
        carrito.RetirarEdicion(retirada, diferir: true, EntornoDelCarrito.Inicio);
        e.Carritos.Sembrar(carrito);

        var resultado = await Agregar(e, otra, 1);

        Assert.True(resultado.EsExito);
        Assert.Equal([otra], resultado.Valor.Carrito.Lineas.Select(l => l.EdicionId));
        var aviso = Assert.Single(resultado.Valor.Carrito.Avisos);
        Assert.Equal(TipoDeAvisoDelCarrito.EdicionRetirada, aviso.Tipo);
        Assert.Equal(retirada, aviso.EdicionId);
        Assert.Equal("Libro retirado", aviso.Titulo);
    }

    [Fact]
    public async Task Una_linea_con_retiro_pendiente_se_muestra_retirada_y_no_cuenta_en_el_subtotal()
    {
        var (e, libro) = ConCheckoutEn(AEnPago);
        e.Carritos.Todos.Single().RetirarEdicion(libro, diferir: true, EntornoDelCarrito.Inicio);

        var vista = (await Obtener(e)).Valor.Carrito;

        Assert.True(vista.Lineas.Single().Retirada);
        Assert.Equal(0m, vista.Subtotal);
        Assert.True(vista.HayUnaCompraEnCurso);
    }
}
