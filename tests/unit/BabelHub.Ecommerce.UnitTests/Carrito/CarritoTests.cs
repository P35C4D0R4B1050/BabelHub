using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T01: el agregado <see cref="Domain.Carrito.Carrito"/> (propietario cuenta o visitante, una línea por edición, impresa de 1 a 20 unidades y digital siempre una, tope de 30 líneas, total, estado y caducidad de 30 días desde la
/// última acción de la persona solo del visitante).
/// </summary>
public sealed class CarritoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid Libro = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Digital = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Domain.Carrito.Carrito DeCuenta() => Domain.Carrito.Carrito.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Ahora);

    private static Domain.Carrito.Carrito DeVisitante() => Domain.Carrito.Carrito.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(new byte[32]), Ahora);

    private static Guid Edicion(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    [Fact]
    public void Un_carrito_nuevo_esta_abierto_y_vacio_y_el_de_cuenta_no_caduca()
    {
        var carrito = DeCuenta();

        Assert.Equal(EstadoDeCarrito.Abierto, carrito.Estado);
        Assert.Empty(carrito.Lineas);
        Assert.Null(carrito.VenceEn);
        Assert.False(carrito.EstaCaducado(Ahora.AddYears(5)));
    }

    [Fact]
    public void Agregar_un_impreso_crea_su_linea_y_volver_a_agregarlo_suma_las_cantidades()
    {
        var carrito = DeCuenta();

        var primera = carrito.Agregar(Libro, esDigital: false, cantidad: 2, Ahora);
        var segunda = carrito.Agregar(Libro, esDigital: false, cantidad: 3, Ahora.AddMinutes(1));

        Assert.True(primera.EsExito);
        Assert.True(segunda.EsExito);
        var linea = Assert.Single(carrito.Lineas);
        Assert.Equal((Libro, false, 5), (linea.EdicionId, linea.EsDigital, linea.Cantidad.Valor));
        Assert.Equal(5, segunda.CantidadFinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-02")]
    public void Un_carrito_acepta_un_impreso_y_un_digital_y_deja_el_digital_en_una_unidad()
    {
        var carrito = DeCuenta();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        var resultado = carrito.Agregar(Digital, esDigital: true, cantidad: 2, Ahora);
        var cambio = carrito.Modificar(Digital, cantidad: 2, Ahora);

        Assert.True(resultado.EsExito);
        Assert.True(cambio.EsExito);
        Assert.Equal(2, carrito.Lineas.Count);
        Assert.Equal(1, carrito.Lineas.Single(l => l.EdicionId == Digital).Cantidad.Valor);
        Assert.Equal(1, resultado.CantidadFinal);
        Assert.Equal(1, cambio.CantidadFinal);
    }

    [Fact]
    public void Volver_a_agregar_una_digital_la_deja_en_una_unidad()
    {
        var carrito = DeCuenta();
        carrito.Agregar(Digital, esDigital: true, cantidad: 1, Ahora);

        var otraVez = carrito.Agregar(Digital, esDigital: true, cantidad: 1, Ahora);

        Assert.True(otraVez.EsExito);
        Assert.Equal(1, Assert.Single(carrito.Lineas).Cantidad.Valor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Una_cantidad_fuera_de_1_a_20_se_rechaza_sin_cambiar_el_carrito(int cantidad)
    {
        var carrito = DeCuenta();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);
        var version = carrito.Version;

        var agregar = carrito.Agregar(Edicion(2), esDigital: false, cantidad, Ahora);
        var modificar = carrito.Modificar(Libro, cantidad, Ahora);

        Assert.Equal(RechazoDeCarrito.CantidadInvalida, agregar.Rechazo);
        Assert.Equal(cantidad > Cantidad.Maxima ? RechazoDeCarrito.TopeDeUnidades : RechazoDeCarrito.CantidadInvalida, modificar.Rechazo);
        Assert.Equal(version, carrito.Version);
        Assert.Equal(1, Assert.Single(carrito.Lineas).Cantidad.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-32")]
    public void La_linea_treinta_y_una_se_rechaza_con_su_motivo_y_las_treinta_primeras_se_aceptan()
    {
        var carrito = DeVisitante();
        for (var i = 1; i <= 30; i++)
        {
            Assert.True(carrito.Agregar(Edicion(i), esDigital: false, cantidad: 1, Ahora).EsExito);
        }

        var treintaYUna = carrito.Agregar(Edicion(31), esDigital: false, cantidad: 1, Ahora);
        var repetida = carrito.Agregar(Edicion(30), esDigital: false, cantidad: 1, Ahora);

        Assert.Equal(RechazoDeCarrito.TopeDeLineas, treintaYUna.Rechazo);
        Assert.Equal(30, carrito.Lineas.Count);
        Assert.True(repetida.EsExito);
        Assert.Equal(2, carrito.Lineas.Single(l => l.EdicionId == Edicion(30)).Cantidad.Valor);
    }

    [Fact]
    public void Modificar_cambia_la_cantidad_y_eliminar_quita_la_linea()
    {
        var carrito = DeCuenta();
        carrito.Agregar(Libro, esDigital: false, cantidad: 2, Ahora);
        carrito.Agregar(Edicion(2), esDigital: false, cantidad: 1, Ahora);

        var modificada = carrito.Modificar(Libro, cantidad: 7, Ahora);
        var eliminada = carrito.Eliminar(Edicion(2), Ahora);

        Assert.Equal(7, modificada.CantidadFinal);
        Assert.True(eliminada.EsExito);
        Assert.Equal(Libro, Assert.Single(carrito.Lineas).EdicionId);
        Assert.Equal(RechazoDeCarrito.LineaNoEstaEnElCarrito, carrito.Modificar(Edicion(9), 1, Ahora).Rechazo);
        Assert.Equal(RechazoDeCarrito.LineaNoEstaEnElCarrito, carrito.Eliminar(Edicion(9), Ahora).Rechazo);
    }

    [Fact]
    public void Cada_cambio_sube_la_version_y_el_instante_de_modificacion()
    {
        var carrito = DeCuenta();
        var inicial = carrito.Version;

        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora.AddMinutes(1));
        carrito.Modificar(Libro, cantidad: 2, Ahora.AddMinutes(2));
        carrito.Eliminar(Libro, Ahora.AddMinutes(3));

        Assert.Equal(inicial + 3, carrito.Version);
        Assert.Equal(Ahora.AddMinutes(3), carrito.ModificadoEn);
    }

    [Fact]
    public void El_total_es_la_suma_de_los_importes_de_cada_linea_redondeados_a_dos_decimales()
    {
        var carrito = DeCuenta();
        carrito.Agregar(Libro, esDigital: false, cantidad: 3, Ahora);
        carrito.Agregar(Digital, esDigital: true, cantidad: 1, Ahora);

        var total = carrito.Total(new Dictionary<Guid, decimal> { [Libro] = 37.30m, [Digital] = 19.99m });

        Assert.Equal(131.89m, total);
    }

    [Fact]
    public void El_importe_de_una_linea_se_redondea_alejandose_de_cero()
    {
        var carrito = DeCuenta();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        Assert.Equal(0.03m, carrito.Total(new Dictionary<Guid, decimal> { [Libro] = 0.025m }));
    }

    [Fact]
    public void El_total_de_un_carrito_vacio_es_cero_y_sin_el_precio_de_una_linea_no_se_calcula()
    {
        var carrito = DeCuenta();
        Assert.Equal(0m, carrito.Total(new Dictionary<Guid, decimal>()));

        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        Assert.Throws<ArgumentException>(() => carrito.Total(new Dictionary<Guid, decimal>()));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-25")]
    public void El_carrito_de_un_visitante_sigue_vigente_a_los_29_dias_y_23_horas_de_su_ultima_modificacion_y_a_los_30_dias_ya_caduco()
    {
        var carrito = DeVisitante();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        Assert.Equal(Ahora.AddDays(30), carrito.VenceEn);
        Assert.False(carrito.EstaCaducado(Ahora.AddDays(29).AddHours(23)));
        Assert.True(carrito.EstaCaducado(Ahora.AddDays(30)));
        Assert.True(carrito.EstaCaducado(Ahora.AddDays(31)));
    }

    [Fact]
    public void Cada_accion_de_la_persona_mueve_la_caducidad_del_visitante()
    {
        var carrito = DeVisitante();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        carrito.Modificar(Libro, cantidad: 2, Ahora.AddDays(20));

        Assert.Equal(Ahora.AddDays(50), carrito.VenceEn);
        Assert.False(carrito.EstaCaducado(Ahora.AddDays(31)));
    }

    [Fact]
    public void Una_accion_rechazada_no_mueve_la_caducidad()
    {
        var carrito = DeVisitante();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        carrito.Agregar(Libro, esDigital: false, cantidad: 500, Ahora.AddDays(20));

        Assert.Equal(Ahora.AddDays(30), carrito.VenceEn);
    }

    [Fact]
    public void Un_carrito_fusionado_queda_sin_lineas_y_no_admite_ninguna()
    {
        var carrito = DeVisitante();
        carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora);

        carrito.MarcarFusionado(Ahora.AddMinutes(1));

        Assert.Equal(EstadoDeCarrito.Fusionado, carrito.Estado);
        Assert.Empty(carrito.Lineas);
        Assert.Equal(RechazoDeCarrito.CarritoFusionado, carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora).Rechazo);
        Assert.Equal(RechazoDeCarrito.CarritoFusionado, carrito.Modificar(Libro, 1, Ahora).Rechazo);
        Assert.Equal(RechazoDeCarrito.CarritoFusionado, carrito.Eliminar(Libro, Ahora).Rechazo);
    }

    [Fact]
    public void El_propietario_es_una_cuenta_o_un_visitante_con_una_huella_de_32_bytes()
    {
        var cuenta = Guid.NewGuid();
        var deCuenta = PropietarioDelCarrito.DeCuenta(cuenta);
        var deVisitante = PropietarioDelCarrito.DeVisitante(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

        Assert.Equal(cuenta, deCuenta.CuentaId);
        Assert.False(deCuenta.EsDeVisitante);
        Assert.True(deVisitante.EsDeVisitante);
        Assert.Null(deVisitante.CuentaId);
        Assert.Equal(32, deVisitante.HuellaDelVisitante.Length);
        Assert.Throws<ArgumentException>(() => PropietarioDelCarrito.DeCuenta(Guid.Empty));
        Assert.Throws<ArgumentException>(() => PropietarioDelCarrito.DeVisitante(new byte[31]));
    }
}
