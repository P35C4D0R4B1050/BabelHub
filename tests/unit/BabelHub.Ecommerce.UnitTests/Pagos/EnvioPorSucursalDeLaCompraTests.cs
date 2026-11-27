using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.InstantaneasDeCompraDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>T54 (PAG-26, PAG-53, PAG-54; AC-PAG-44, AC-PAG-45): la intención conserva el envío por sucursal sin recalcular la huella, el reemplazo lo sobrescribe, la compra aprobada lo entrega desde lo guardado y la desvinculación lo elimina.</summary>
public sealed class EnvioPorSucursalDeLaCompraTests
{
    private static readonly EnvioDeSucursalDeCompra[] DosSucursales = [new("ayacucho", "Sucursal Ayacucho", 30m), new("cusco", null, 18.50m)];

    private static readonly EnvioDeSucursalDeIntencion[] Conservados = [new("ayacucho", Importe.De(30m)), new("cusco", Importe.De(18.50m))];

    private static InstantaneaDeCompra ADomicilio(string huella = "huella-a") => Mixta(huella, 48.50m) with { EnviosPorSucursal = DosSucursales };

    private static InstantaneaDeCompra DeRecojo(string huella) => Mixta(huella, 0m) with
    {
        TipoDeEntrega = TipoDeEntregaDeCompra.Recojo,
        RegionDeEntrega = null,
        Contacto = null,
        EnviosPorSucursal = null,
    };

    [Fact]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task La_intencion_conserva_el_codigo_y_el_importe_de_cada_sucursal_con_la_huella_que_recibio()
    {
        var e = new EntornoDePagos();

        await e.Creador.Crear(Cuenta, ADomicilio("huella-de-carrito"), "huella-de-carrito", e.ReservaExpiraEn);

        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.Equal(Conservados, intencion.EnviosPorSucursal);
        Assert.Equal(("huella-de-carrito", Importe.De(48.50m)), (intencion.HuellaDeInstantanea, intencion.Envio));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task Reemplazar_una_intencion_reabierta_sobrescribe_el_detalle_tambien_con_ninguno(bool conDetalleNuevo)
    {
        var e = new EntornoDePagos();
        await e.Creador.Crear(Cuenta, ADomicilio(), "huella-a", e.ReservaExpiraEn);
        await e.Creador.Reabrir(Cuenta, Pedido);
        var nueva = conDetalleNuevo
            ? Mixta("huella-b", 25m) with { EnviosPorSucursal = [new EnvioDeSucursalDeCompra("huanta", "Sucursal Huanta", 25m)] }
            : DeRecojo("huella-b");

        await e.Creador.Crear(Cuenta, nueva, "huella-b", e.ReservaExpiraEn);

        var intencion = Assert.Single(e.Repositorio.Intenciones);
        Assert.Equal(conDetalleNuevo ? new EnvioDeSucursalDeIntencion[] { new("huanta", Importe.De(25m)) } : null, intencion.EnviosPorSucursal);
    }

    [Theory]
    [InlineData("falta")]
    [InlineData("vacio")]
    [InlineData("codigo-mal-escrito")]
    [InlineData("repetido")]
    [InlineData("negativo")]
    [InlineData("tres-decimales")]
    [InlineData("no-suma")]
    [InlineData("recojo-con-detalle")]
    [Trait("Criterio", "AC-PAG-45")]
    public async Task Una_compra_cuyo_detalle_no_cuadra_no_crea_la_intencion(string caso)
    {
        var e = new EntornoDePagos();
        EnvioDeSucursalDeCompra Envio(string codigo, decimal importe) => new(codigo, null, importe);
        InstantaneaDeCompra Con(params EnvioDeSucursalDeCompra[] envios) => ADomicilio() with { EnviosPorSucursal = envios };
        var instantanea = caso switch
        {
            "falta" => ADomicilio() with { EnviosPorSucursal = null },
            "vacio" => Con(),
            "codigo-mal-escrito" => Con(Envio("Ayacucho Centro", 48.50m)),
            "repetido" => Con(Envio("cusco", 30m), Envio("cusco", 18.50m)),
            "negativo" => Con(Envio("ayacucho", 58.50m), Envio("cusco", -10m)),
            "tres-decimales" => Con(Envio("ayacucho", 30.001m), Envio("cusco", 18.499m)),
            "no-suma" => Con(Envio("ayacucho", 30m), Envio("cusco", 18m)),
            _ => DeRecojo("huella-a") with { EnviosPorSucursal = [Envio("ayacucho", 0m)] },
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => e.Creador.Crear(Cuenta, instantanea, "huella-a", e.ReservaExpiraEn));

        Assert.Empty(e.Repositorio.Intenciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task Desvincular_la_cuenta_elimina_el_detalle_junto_con_la_entrega()
    {
        var e = new EntornoDePagos();
        await e.Creador.Crear(Cuenta, ADomicilio(), "huella-a", e.ReservaExpiraEn);
        var intencion = Assert.Single(e.Repositorio.Intenciones);

        intencion.Desvincular(e.Reloj.Ahora);

        Assert.Null(intencion.EnviosPorSucursal);
        Assert.Equal(Importe.De(48.50m), intencion.Envio);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task La_compra_aprobada_lleva_el_detalle_guardado_y_ninguno_en_un_recojo(bool aDomicilio)
    {
        var e = new EntornoDePagos();
        await e.Creador.Crear(Cuenta, aDomicilio ? ADomicilio() : DeRecojo("huella-a"), "huella-a", e.ReservaExpiraEn);
        var intencion = Assert.Single(e.Repositorio.Intenciones);

        var compra = CompletarCompra.CompraAprobadaDe(Confirmada(intencion, e.Reloj.Ahora));

        Assert.Equal(aDomicilio ? new EnvioDeSucursalDeLaCompra[] { new("ayacucho", 30m), new("cusco", 18.50m) } : null, compra.EnviosPorSucursal);
        Assert.Null(compra.CodigoDeSucursalDeRecojo);
    }

    /// <summary>La intención con las reservas confirmadas, lista para entregarse a Pedidos.</summary>
    private static IntencionDePago Confirmada(IntencionDePago intencion, DateTimeOffset ahora)
    {
        var intento = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, Domain.Pagos.MedioDePago.Tarjeta, intencion.Total, ahora);
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.IniciarCobro(ahora));
        intencion.AplicarAprobacion(intento, "ch_1", ahora);
        intencion.MarcarConfirmacionIniciada(ahora);
        intencion.MarcarReservasConfirmadas(ahora, [new ReservaConfirmadaDeLinea(IntencionesDePrueba.LineaImpresa, "ayacucho", false, null, null)]);
        return intencion;
    }
}
