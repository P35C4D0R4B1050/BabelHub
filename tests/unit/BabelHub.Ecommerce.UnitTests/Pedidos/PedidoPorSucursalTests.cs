using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T43 (PED-01, PED-04, PED-43, AC-PED-36, AC-PED-39): un pedido de la venta por sucursal admite varias líneas de una edición impresa, cada una de la sucursal que la atiende, y un recojo sin sucursal común; un pedido anterior conserva la suya.</summary>
public sealed class PedidoPorSucursalTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);
    private static readonly AceptacionDeTexto Terminos = new("t-1", Ahora.AddMinutes(-3));
    private static readonly AceptacionDeTexto Privacidad = new("p-1", Ahora.AddMinutes(-3));
    private static readonly AceptacionDeTexto Reembolso = new("r-1", Ahora.AddMinutes(-3));
    private static readonly Guid Libro = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DatosDeLineaNueva Impresa(string fuente, int cantidad, Guid? edicion = null, bool traslado = false) =>
        new(Guid.NewGuid(), edicion ?? Libro, false, "Estructuras de datos", "9780000000001", cantidad, 60m, 60m * cantidad, fuente, traslado, traslado ? Guid.NewGuid() : null, null);

    private static DatosDeLineaNueva Digital(Guid? edicion = null) =>
        new(Guid.NewGuid(), edicion ?? Guid.NewGuid(), true, "Cien años de soledad", "9780000000002", 1, 20m, 20m, null, false, null, null);

    private static DatosDePedidoNuevo Datos(TipoDeEntregaDelPedido tipo, IReadOnlyList<DatosDeLineaNueva> lineas, decimal envio = 0m, string? sucursal = null) =>
        new(
            Guid.NewGuid(),
            "ABCD234567",
            Guid.NewGuid(),
            tipo,
            sucursal,
            tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null,
            envio,
            lineas.Sum(l => l.Importe) + envio,
            tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123") : null,
            Terminos,
            Privacidad,
            lineas.Any(l => l.EsDigital) ? Reembolso : null,
            lineas,
            Ahora);

    private static IEnumerable<(string? Sucursal, int Cantidad)> PorSucursal(Pedido pedido) =>
        pedido.Lineas.Select(l => (pedido.SucursalDeLaLinea(l), l.Cantidad));

    [Fact]
    [Trait("Criterio", "AC-PED-36")]
    public void Un_pedido_a_domicilio_con_el_mismo_libro_en_dos_sucursales_y_dos_lotes_tiene_tres_lineas_cada_una_con_su_sucursal()
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, [Impresa("cusco", 18), Impresa("ayacucho", 6), Impresa("ayacucho", 2)], envio: 60m));

        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        var pedido = resultado.Pedido!;
        Assert.Equal(new (string?, int)[] { ("cusco", 18), ("ayacucho", 6), ("ayacucho", 2) }, PorSucursal(pedido));
        Assert.Equal(3, pedido.Lineas.Select(l => l.Id).Distinct().Count());
        Assert.Equal(60m, pedido.CostoDeEnvio);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-36")]
    public void Un_recojo_sin_sucursal_comun_toma_la_sucursal_de_la_fuente_de_cada_linea_impresa_y_ninguna_para_la_digital()
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("ayacucho", 1), Impresa("cusco", 2), Digital()]));

        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        var pedido = resultado.Pedido!;
        Assert.Null(pedido.CodigoDeSucursalDeRecojo);
        Assert.Equal(new (string?, int)[] { ("ayacucho", 1), ("cusco", 2), (null, 1) }, PorSucursal(pedido));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Criterio", "AC-PED-36")]
    public void Una_sucursal_de_recojo_en_blanco_equivale_a_ninguna(string enBlanco)
    {
        var pedido = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("cusco", 1)], sucursal: enBlanco)).Pedido!;

        Assert.Null(pedido.CodigoDeSucursalDeRecojo);
        Assert.Equal("cusco", pedido.SucursalDeLaLinea(pedido.Lineas.Single()));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-36")]
    public void Un_recojo_sin_sucursal_comun_con_una_impresa_sin_fuente_no_se_crea()
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("cusco", 1), Impresa(" ", 1)]));

        Assert.Equal(RechazoDeCrearPedido.FuenteFaltante, resultado.Rechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public void En_un_pedido_anterior_con_sucursal_comun_cada_impresa_es_de_aquella_sucursal_aunque_venga_por_traslado()
    {
        var pedido = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("cusco", 1, traslado: true), Impresa("ayacucho", 2, edicion: Guid.NewGuid()), Digital()], sucursal: "ayacucho")).Pedido!;

        Assert.Equal(new (string?, int)[] { ("ayacucho", 1), ("ayacucho", 2), (null, 1) }, PorSucursal(pedido));
        Assert.Equal("cusco", pedido.Lineas[0].CodigoDeFuente);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public void Un_pedido_anterior_rehidratado_conserva_la_sucursal_comun_como_la_de_sus_lineas()
    {
        var linea = new DatosPersistidosDeLinea(Guid.NewGuid(), Libro, false, "Estructuras de datos", null, 1, 60m, 60m, "cusco", true, Guid.NewGuid(), null, EstadoDeLineaImpresa.EnTransito, null, null, null, null, null, null);
        var pedido = Pedido.Rehidratar(new DatosPersistidosDePedido(Guid.NewGuid(), "ABCD234567", Guid.NewGuid(), TipoDeEntregaDelPedido.Recojo, "ayacucho", null, 0m, 60m, EstadoDePedido.EnTransito, Ahora, null, null, null, null, null, Terminos, Privacidad, null, [linea]));

        Assert.Equal("ayacucho", pedido.SucursalDeLaLinea(pedido.Lineas.Single()));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-36")]
    public void Solo_las_digitales_no_repiten_edicion()
    {
        var edicion = Guid.NewGuid();

        var digitales = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Digital, [Digital(edicion), Digital(edicion)]));
        var mixto = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("cusco", 1, edicion), Impresa("cusco", 1, edicion), Digital()]));

        Assert.Equal(RechazoDeCrearPedido.LineaRepetida, digitales.Rechazo);
        Assert.True(mixto.Creado, mixto.Rechazo?.ToString());
    }

    [Fact]
    public void La_sucursal_de_una_linea_de_otro_pedido_no_se_resuelve()
    {
        var uno = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("cusco", 1)])).Pedido!;
        var otro = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, [Impresa("ayacucho", 1)])).Pedido!;

        Assert.Throws<ArgumentException>(() => uno.SucursalDeLaLinea(otro.Lineas.Single()));
    }
}
