using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T39 y T41 (venta por sucursal): la línea es un libro en una sucursal con identidad propia (CAR-39, CAR-40, CAR-41, CAR-45), el stock se mide por sucursal sumando las líneas de la pareja (CAR-04) y una línea se busca solo en el
/// carrito de quien pide.
/// </summary>
public sealed class VentaPorSucursalDelCarritoTests
{
    private static readonly DateTimeOffset Ahora = EntornoDelCarrito.Inicio;

    private static readonly Guid Libro = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CarritoDeDominio Nuevo() => CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Ahora);

    private static ResultadoDeLinea Anadir(CarritoDeDominio carrito, string sucursal, int cantidad, int minuto = 0, Guid? edicion = null) =>
        carrito.Agregar(Guid.NewGuid(), edicion ?? Libro, esDigital: false, sucursal, cantidad, Ahora.AddMinutes(minuto));

    private static (string?, int)[] Lineas(CarritoDeDominio carrito) => [.. carrito.Lineas.Select(l => (l.CodigoDeSucursal, l.Cantidad.Valor))];

    [Fact]
    [Trait("Criterio", "AC-CAR-35")]
    public void Cada_sucursal_tiene_su_linea_y_una_adicion_que_no_cabe_abre_otra_con_la_cantidad_completa()
    {
        var carrito = Nuevo();

        Anadir(carrito, Sucursal.Principal, 18);
        Anadir(carrito, Sucursal.Cusco, 6, 1);
        var tercera = Anadir(carrito, Sucursal.Principal, 5, 2);
        var sumada = Anadir(carrito, Sucursal.Cusco, 2, 3);

        Assert.Equal([(Sucursal.Principal, 18), (Sucursal.Cusco, 6 + 2), (Sucursal.Principal, 5)], Lineas(carrito));
        Assert.Equal(5, tercera.CantidadFinal);
        Assert.Equal(8, sumada.CantidadFinal);
        Assert.Equal(23, carrito.Unidades(Libro, Sucursal.Principal));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-35")]
    public void La_adicion_va_a_la_linea_mas_antigua_que_la_admite_entera_y_no_se_reparte()
    {
        var carrito = Nuevo();
        Anadir(carrito, Sucursal.Principal, 19);
        Anadir(carrito, Sucursal.Principal, 10, 1);
        Anadir(carrito, Sucursal.Principal, 12, 2);

        Anadir(carrito, Sucursal.Principal, 2, 3);
        Anadir(carrito, Sucursal.Principal, 1, 4);

        Assert.Equal([(Sucursal.Principal, 20), (Sucursal.Principal, 12), (Sucursal.Principal, 12)], Lineas(carrito));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-32")]
    public void Treinta_lineas_como_maximo_modificar_a_21_pasa_el_tope_de_unidades_y_agregar_a_una_llena_abre_otra()
    {
        var carrito = Nuevo();
        Anadir(carrito, Sucursal.Principal, 20);
        var llena = carrito.Lineas.Single().LineaId;

        Assert.Equal(RechazoDeCarrito.TopeDeUnidades, carrito.Modificar(llena, 21, Ahora).Rechazo);
        Assert.Equal(RechazoDeCarrito.CantidadInvalida, Anadir(carrito, Sucursal.Principal, 21).Rechazo);
        Assert.True(Anadir(carrito, Sucursal.Principal, 1, 1).EsExito);
        Assert.Equal(2, carrito.Lineas.Count);

        for (var i = 3; i <= 30; i++)
        {
            Assert.True(Anadir(carrito, Sucursal.Principal, 1, i, Guid.NewGuid()).EsExito);
        }

        Assert.Equal(RechazoDeCarrito.TopeDeLineas, Anadir(carrito, Sucursal.Cusco, 1, 40).Rechazo);
        Assert.Equal(30, carrito.Lineas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-34")]
    public void Un_impreso_exige_su_sucursal_y_una_digital_no_la_guarda()
    {
        var carrito = Nuevo();

        Assert.Equal(RechazoDeCarrito.SucursalRequerida, carrito.Agregar(Guid.NewGuid(), Libro, false, null, 1, Ahora).Rechazo);
        Assert.True(carrito.Agregar(Guid.NewGuid(), Guid.NewGuid(), true, Sucursal.Cusco, 3, Ahora).EsExito);
        var digital = Assert.Single(carrito.Lineas);
        Assert.Equal((null, 1), (digital.CodigoDeSucursal, digital.Cantidad.Valor));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public void Una_linea_anterior_sin_sucursal_no_recibe_sumas_ni_se_cambia_pero_se_quita()
    {
        var anterior = LineaDeCarrito.Crear(Guid.NewGuid(), Libro, false, null, Cantidad.Crear(2), Ahora);
        var carrito = CarritoDeDominio.Restaurar(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), EstadoDeCarrito.Abierto, 1, Ahora, Ahora, null, [anterior]);

        Anadir(carrito, Sucursal.Principal, 1);

        Assert.Equal([((string?)null, 2), (Sucursal.Principal, 1)], Lineas(carrito));
        Assert.True(carrito.Lineas[0].EsImpresoSinSucursal);
        Assert.Equal(RechazoDeCarrito.LineaSinSucursal, carrito.Modificar(anterior.LineaId, 3, Ahora).Rechazo);
        Assert.True(carrito.Eliminar(anterior.LineaId, Ahora).EsExito);
    }

    [Fact]
    public void Retirar_una_edicion_del_catalogo_quita_todas_sus_lineas_y_deja_un_solo_aviso()
    {
        var carrito = Nuevo();
        Anadir(carrito, Sucursal.Principal, 2);
        Anadir(carrito, Sucursal.Cusco, 1, 1);
        Anadir(carrito, Sucursal.Cusco, 1, 2, Guid.NewGuid());

        Assert.Equal(ResultadoDeRetiro.Anotada, carrito.RetirarEdicion(Libro, diferir: true, Ahora));
        var retiradas = carrito.AplicarRetirosPendientes(Ahora);

        Assert.Equal(Libro, Assert.Single(retiradas).EdicionId);
        Assert.NotEqual(Libro, Assert.Single(carrito.Lineas).EdicionId);
    }

    // ---------- Casos de uso ----------

    private static Task<Resultado<ResultadoDeCarrito>> Agregar(EntornoDelCarrito e, Guid edicion, int cantidad, string? sucursal, string? cookie = null) =>
        new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(edicion, cantidad, cookie, sucursal));

    [Fact]
    [Trait("Criterio", "AC-CAR-03")]
    public async Task El_maximo_es_el_de_la_sucursal_elegida_sumando_las_lineas_del_libro_en_ella()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(3);
        e.Disponibilidad.VendibleEn(libro, (Sucursal.Principal, 3), (Sucursal.Cusco, 10));

        var cinco = await Agregar(e, libro, 5, Sucursal.Principal);
        await Agregar(e, libro, 2, Sucursal.Principal);
        var dosMas = await Agregar(e, libro, 2, Sucursal.Principal);
        var cusco = await Agregar(e, libro, 5, Sucursal.Cusco);

        Assert.Equal([("maximo", "3"), ("sucursal", Sucursal.Principal)], cinco.Error!.Errores.Select(x => (x.Campo, x.Clave)));
        Assert.Equal(ClavesDelCarrito.SinStock, dosMas.Error!.Clave);
        Assert.Equal([(Sucursal.Principal, 2), (Sucursal.Cusco, 5)], cusco.Valor.Carrito.Lineas.Select(l => (l.CodigoDeSucursal!, l.Cantidad)));
        Assert.Equal([(3, 3), (10, 10)], cusco.Valor.Carrito.Lineas.Select(l => (l.VendibleEnLaSucursal!.Value, l.MaximoEditable!.Value)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-35")]
    public async Task Pedir_mas_de_lo_que_queda_en_una_sucursal_se_rechaza_sin_tocar_ninguna_linea()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(30);
        e.Disponibilidad.VendibleEn(libro, (Sucursal.Principal, 30), (Sucursal.Cusco, 10));
        await Agregar(e, libro, 18, Sucursal.Principal);
        await Agregar(e, libro, 6, Sucursal.Cusco);

        var cabe = await Agregar(e, libro, 2, Sucursal.Cusco);
        var noCabe = await Agregar(e, libro, 5, Sucursal.Cusco);

        Assert.True(cabe.EsExito);
        Assert.Equal(ClavesDelCarrito.SinStock, noCabe.Error!.Clave);
        Assert.Equal([(Sucursal.Principal, 18), (Sucursal.Cusco, 8)], e.Carritos.Todos.Single().Lineas.Select(l => (l.CodigoDeSucursal!, l.Cantidad.Valor)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-34")]
    public async Task Agregar_exige_una_sucursal_registrada_y_sin_lista_de_sucursales_no_se_puede_comprobar()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);

        var sin = await Agregar(e, libro, 1, null);
        var desconocida = await Agregar(e, libro, 1, "no-existe");
        e.Sucursales.SinLista = true;
        var sinLista = await Agregar(e, libro, 1, Sucursal.Principal);

        Assert.Equal(("sucursal", CategoriaDeError.Validacion), (Assert.Single(sin.Error!.Errores).Campo, sin.Error.Categoria));
        Assert.Equal(("sucursal", CategoriaDeError.Validacion), (Assert.Single(desconocida.Error!.Errores).Campo, desconocida.Error.Categoria));
        Assert.Equal(CategoriaDeError.NoDisponible, sinLista.Error!.Categoria);
        Assert.Empty(e.Carritos.Todos);
        Assert.True((await Agregar(e, e.Digital(), 1, null)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-35")]
    public async Task Subir_una_linea_se_mide_contra_la_suma_de_las_lineas_de_su_sucursal()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(25);
        await Agregar(e, libro, 18, Sucursal.Principal);
        await Agregar(e, libro, 4, Sucursal.Principal);
        var segunda = e.Carritos.Todos.Single().Lineas[1].LineaId;

        var ocho = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(segunda, 8));
        var siete = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(segunda, 7));

        Assert.Equal("25", ocho.Error!.Errores.Single(x => x.Campo == "maximo").Clave);
        Assert.Equal([18, 7], siete.Valor.Carrito.Lineas.Select(l => l.Cantidad));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Una_linea_de_otro_carrito_o_inexistente_responde_no_encontrada_sin_evento_de_acceso_denegado()
    {
        var e = EntornoDelCarrito.DeCliente();
        var libro = e.Impreso(5);
        var ajeno = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Ahora);
        ajeno.Agregar(libro, false, 1, Ahora);
        e.Carritos.Sembrar(ajeno);
        await Agregar(e, libro, 1, Sucursal.Principal);

        var deOtro = await new ModificarLinea(e.Servicios()).Ejecutar(new ModificarLineaEntrada(ajeno.Lineas.Single().LineaId, 3));
        var inexistente = await new EliminarLinea(e.Servicios()).Ejecutar(new EliminarLineaEntrada(Guid.NewGuid()));

        Assert.Equal(ClavesDelCarrito.LineaNoEncontrada, deOtro.Error!.Clave);
        Assert.Equal(ClavesDelCarrito.LineaNoEncontrada, inexistente.Error!.Clave);
        Assert.Equal(1, ajeno.Lineas.Single().Cantidad.Valor);
        Assert.Empty(e.Denegaciones.Eventos);
    }
}
