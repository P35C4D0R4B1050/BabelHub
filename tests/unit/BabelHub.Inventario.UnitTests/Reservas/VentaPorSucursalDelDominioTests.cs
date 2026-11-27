using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T35: la venta por sucursal en el Dominio (SEL-41, SEL-43): el recojo sin sucursal de cabecera, la sucursal de entrega de cada línea y la asignación pura y acumulada por fuente y edición.</summary>
public sealed class VentaPorSucursalDelDominioTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cusco = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Ayacucho = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid Libro = Guid.Parse("e0000000-0000-0000-0000-000000000003");
    private static readonly Guid OtroLibro = Guid.Parse("e0000000-0000-0000-0000-000000000004");

    private static LineaDeReserva Linea(Guid fuente, int cantidad = 1) => LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), Libro, fuente, cantidad, Inicio);

    private static LineaPorSucursal Pedida(Guid fuente, int cantidad, Guid? edicion = null) => new(Guid.NewGuid(), edicion ?? Libro, fuente, cantidad);

    private static Dictionary<(Guid FuenteId, Guid EdicionId), int> Vendible(params (Guid Fuente, Guid Edicion, int Cantidad)[] filas) =>
        filas.ToDictionary(f => (f.Fuente, f.Edicion), f => f.Cantidad);

    // --- el recojo sin sucursal de cabecera ---

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public void Un_recojo_por_sucursal_no_tiene_sucursal_de_cabecera_y_cada_linea_se_entrega_en_su_fuente()
    {
        var enCusco = Linea(Cusco, 18);
        var enAyacucho = Linea(Ayacucho, 6);

        var reserva = ReservaDePedido.IniciarPorSucursal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [enCusco, enAyacucho], reemplazos: 2, extendida: true);

        Assert.Equal((TipoDeEntrega.Recojo, (Guid?)null, (string?)null), (reserva.TipoDeEntrega, reserva.SucursalDeRecojoId, reserva.RegionDeEntrega));
        Assert.True(reserva.EsRecojoPorSucursal);
        Assert.Equal((Cusco, Ayacucho), (reserva.SucursalDeEntregaDe(enCusco), reserva.SucursalDeEntregaDe(enAyacucho)));
        Assert.Equal((2, true, ExpiracionDeReserva.AlIniciar(Inicio)), (reserva.Reemplazos, reserva.Extendida, reserva.FechaDeExpiracion));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public void Un_recojo_con_sucursal_de_cabecera_entrega_todas_sus_lineas_alli_aunque_salgan_de_otra_fuente()
    {
        var desdeCusco = Linea(Cusco);

        var reserva = ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Recojo, Ayacucho, null, Inicio, [desdeCusco]);

        Assert.False(reserva.EsRecojoPorSucursal);
        Assert.Equal(Ayacucho, reserva.SucursalDeEntregaDe(desdeCusco));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public void Un_domicilio_no_es_un_recojo_por_sucursal_aunque_tampoco_tenga_sucursal_de_cabecera()
    {
        var reserva = ReservaDePedido.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoDeEntrega.Domicilio, null, "lima", Inicio, [Linea(Cusco)]);

        Assert.False(reserva.EsRecojoPorSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public void El_recojo_por_sucursal_tambien_exige_al_menos_una_linea_y_no_repetir_la_linea_del_pedido()
    {
        var linea = Linea(Cusco);

        Assert.Throws<ArgumentException>(() => ReservaDePedido.IniciarPorSucursal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, []));
        Assert.Throws<ArgumentException>(() => ReservaDePedido.IniciarPorSucursal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [linea, linea]));
    }

    // --- la asignación ---

    [Fact]
    [Trait("Criterio", "AC-SEL-42")]
    public void Cada_linea_se_cubre_solo_con_su_sucursal()
    {
        var lineas = new[] { Pedida(Cusco, 18), Pedida(Ayacucho, 6) };

        var asignadas = AsignacionPorSucursal.Asignar(lineas, Vendible((Cusco, Libro, 20), (Ayacucho, Libro, 6)));

        Assert.Equal(lineas, asignadas.Select(a => a.Linea));
        Assert.All(asignadas, a => Assert.True(a.Cubierta));
        Assert.Equal([20, 6], asignadas.Select(a => a.VendibleEnLaSucursal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public void Las_lineas_de_una_edicion_y_sucursal_se_acumulan_y_la_que_no_cabe_se_indica_con_el_vendible_de_la_sucursal()
    {
        var primera = Pedida(Ayacucho, 3);
        var segunda = Pedida(Ayacucho, 3);

        var asignadas = AsignacionPorSucursal.Asignar([primera, segunda], Vendible((Ayacucho, Libro, 5), (Cusco, Libro, 30)));

        Assert.Equal([true, false], asignadas.Select(a => a.Cubierta));
        Assert.Equal(5, asignadas[1].VendibleEnLaSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public void Tres_y_dos_caben_en_cinco()
    {
        var asignadas = AsignacionPorSucursal.Asignar([Pedida(Ayacucho, 3), Pedida(Ayacucho, 2)], Vendible((Ayacucho, Libro, 5)));

        Assert.All(asignadas, a => Assert.True(a.Cubierta));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public void Una_linea_que_no_cabe_no_consume_y_la_siguiente_puede_caber()
    {
        var asignadas = AsignacionPorSucursal.Asignar([Pedida(Ayacucho, 6), Pedida(Ayacucho, 3)], Vendible((Ayacucho, Libro, 5)));

        Assert.Equal([false, true], asignadas.Select(a => a.Cubierta));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-43")]
    public void Cada_pareja_de_fuente_y_edicion_se_acumula_por_separado()
    {
        var lineas = new[] { Pedida(Ayacucho, 5), Pedida(Ayacucho, 5, OtroLibro), Pedida(Cusco, 5) };

        var asignadas = AsignacionPorSucursal.Asignar(lineas, Vendible((Ayacucho, Libro, 5), (Ayacucho, OtroLibro, 5), (Cusco, Libro, 5)));

        Assert.All(asignadas, a => Assert.True(a.Cubierta));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-43")]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Una_sucursal_sin_stock_o_sin_fila_no_cubre_nada_y_reporta_cero(int? enLaFila)
    {
        var vendible = enLaFila is { } cantidad ? Vendible((Ayacucho, Libro, cantidad)) : Vendible((Cusco, Libro, 30));

        var asignada = Assert.Single(AsignacionPorSucursal.Asignar([Pedida(Ayacucho, 1)], vendible));

        Assert.Equal((false, 0), (asignada.Cubierta, asignada.VendibleEnLaSucursal));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-43")]
    [InlineData(0)]
    [InlineData(-1)]
    public void La_cantidad_es_mayor_que_cero(int cantidad) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AsignacionPorSucursal.Asignar([Pedida(Ayacucho, cantidad)], Vendible((Ayacucho, Libro, 5))));
}
