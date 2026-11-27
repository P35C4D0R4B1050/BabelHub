using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>T44 de ecommerce-pedidos (AC-PED-36, AC-PED-39): la intención admite un recojo sin sucursal común y varias líneas de una edición impresa, sin cambiar las intenciones anteriores.</summary>
public sealed class RecojoPorSucursalDeIntencionTests
{
    private static InstantaneaDeIntencion Recojo(string? sucursal, string? region = null, ContactoDeEntrega? contacto = null, Importe? envio = null, params LineaDeIntencion[] lineas)
    {
        var compradas = lineas.Length > 0 ? lineas : [Impresa()];
        var costo = envio ?? Importe.Cero;
        return InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Recojo, sucursal, region, compradas, costo, compradas.Aggregate(costo, (suma, l) => suma + l.Importe), "huella-r", Texto(), Texto(), null, contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-36")]
    public void Un_recojo_por_sucursal_no_lleva_sucursal_comun_y_admite_dos_lineas_de_una_edicion_impresa()
    {
        var otraLinea = Impresa(cantidad: 6) with { LineaId = Guid.NewGuid() };

        var intencion = Abierta(Recojo(sucursal: null, lineas: [Impresa(), otraLinea]));

        Assert.Null(intencion.CodigoDeSucursalDeRecojo);
        Assert.Equal(TipoDeEntrega.Recojo, intencion.TipoDeEntrega);
        Assert.Equal(new[] { 2, 6 }, intencion.Lineas.Select(l => l.Cantidad));
        Assert.Single(intencion.Lineas.Select(l => l.EdicionId).Distinct());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [Trait("Criterio", "AC-PED-36")]
    public void Una_sucursal_de_recojo_en_blanco_equivale_a_ninguna(string enBlanco)
    {
        Assert.Null(Recojo(enBlanco).CodigoDeSucursalDeRecojo);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public void Un_recojo_anterior_con_sucursal_comun_se_conserva()
    {
        var intencion = Abierta(ConRecojo());

        Assert.Equal("AYA-01", intencion.CodigoDeSucursalDeRecojo);
        Assert.Equal("huella-r", intencion.HuellaDeInstantanea);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public void El_recojo_sigue_sin_region_contacto_ni_envio()
    {
        Assert.Throws<ArgumentException>(() => Recojo(sucursal: null, region: "AYA"));
        Assert.Throws<ArgumentException>(() => Recojo(sucursal: null, contacto: Contacto()));
        Assert.Throws<ArgumentException>(() => Recojo(sucursal: null, envio: Importe.De(30m)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public void Fuera_del_recojo_la_sucursal_comun_sigue_prohibida()
    {
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Domicilio, "AYA-01", "AYA", new[] { Impresa() }, Importe.Cero, Importe.De(100m), "h", Texto(), Texto(), null, Contacto()));
        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.SinEntrega, "AYA-01", null, new[] { Digital() }, Importe.Cero, Importe.De(25m), "h", Texto(), Texto(), Texto(), null));
    }
}
