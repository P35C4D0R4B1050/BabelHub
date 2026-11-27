using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>T53 (PAG-10, PAG-54; AC-PAG-45, AC-PAG-46): la instantánea a domicilio lleva el envío de cada sucursal y se rechaza si no cuadra, y los reembolsos admiten una parte del envío hasta lo cobrado.</summary>
public sealed class EnvioPorSucursalDeIntencionTests
{
    private static EnvioDeSucursalDeIntencion Envio(string sucursal, decimal importe) => new(sucursal, Importe.De(importe));

    private static InstantaneaDeIntencion ADomicilio(IReadOnlyList<EnvioDeSucursalDeIntencion>? envios, decimal envio = 48.50m) =>
        InstantaneaDeIntencion.Crear(
            Cuenta, TipoDeEntrega.Domicilio, null, "AYA", [Impresa()], Importe.De(envio), Importe.De(100m + envio), "h", Texto(), Texto(), null, Contacto(), envios);

    [Fact]
    [Trait("Criterio", "AC-PAG-45")]
    public void A_domicilio_con_el_detalle_que_suma_el_envio_la_instantanea_lo_conserva_tambien_con_importe_cero()
    {
        var instantanea = ADomicilio([Envio("ayacucho", 30m), Envio("cusco", 18.50m), Envio("huanta-2", 0m)]);

        Assert.Equal(new[] { Envio("ayacucho", 30m), Envio("cusco", 18.50m), Envio("huanta-2", 0m) }, instantanea.EnviosPorSucursal);
    }

    public static TheoryData<string, EnvioDeSucursalDeIntencion[]?> DetallesQueNoCuadran => new()
    {
        { "falta", null },
        { "vacío", [] },
        { "código con mayúsculas", [new("Ayacucho", Importe.De(48.50m))] },
        { "código de un carácter", [new("a", Importe.De(48.50m))] },
        { "código de 21 caracteres", [new("abcdefghijklmnopqrstu", Importe.De(48.50m))] },
        { "código con espacio", [new("san juan", Importe.De(48.50m))] },
        { "código con salto de línea final", [new("ayacucho\n", Importe.De(48.50m))] },
        { "código nulo", [new(null!, Importe.De(48.50m))] },
        { "código repetido", [new("ayacucho", Importe.De(30m)), new("ayacucho", Importe.De(18.50m))] },
        { "no suma el envío", [new("ayacucho", Importe.De(30m)), new("cusco", Importe.De(18.49m))] },
    };

    [Theory]
    [MemberData(nameof(DetallesQueNoCuadran))]
    [Trait("Criterio", "AC-PAG-45")]
    public void A_domicilio_un_detalle_que_falta_o_no_cuadra_rechaza_la_instantanea(string caso, EnvioDeSucursalDeIntencion[]? envios)
    {
        var error = Record.Exception(() => ADomicilio(envios));

        Assert.True(error is ArgumentException, caso);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    [Trait("Criterio", "AC-PAG-45")]
    public void Un_importe_negativo_o_con_mas_de_dos_decimales_no_llega_a_ser_un_importe(double valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Importe.De((decimal)valor));
    }

    [Theory]
    [InlineData(TipoDeEntrega.Recojo)]
    [InlineData(TipoDeEntrega.SinEntrega)]
    [Trait("Criterio", "AC-PAG-45")]
    public void Fuera_del_domicilio_no_hay_detalle_de_envios(TipoDeEntrega entrega)
    {
        LineaDeIntencion linea = entrega == TipoDeEntrega.Recojo ? Impresa() : Digital();

        Assert.Throws<ArgumentException>(() => InstantaneaDeIntencion.Crear(
            Cuenta, entrega, null, null, [linea], Importe.Cero, linea.Importe, "h", Texto(), Texto(), Texto(), null, [Envio("ayacucho", 0m)]));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-46")]
    public void El_envio_se_reembolsa_por_partes_hasta_lo_cobrado_y_nunca_mas()
    {
        var intento = Guid.NewGuid();
        var lineas = new Dictionary<Guid, Importe> { [LineaImpresa] = Importe.De(100m), [LineaDigital] = Importe.De(25m) };
        Reembolso Parte(Guid linea, decimal monto, decimal envio) => Reembolso.Crear(
            Guid.NewGuid(), OrigenDeReembolso.Anulacion, "anul:" + Guid.NewGuid().ToString("N"), PedidoId, intento, [new LineaDeReembolso(linea, Importe.De(monto))], Importe.De(envio), Inicio);
        ResultadoDeTope Tope(Reembolso nuevo, params Reembolso[] existentes) => ReglasDeReembolso.ValidarTope(Importe.De(173.50m), Importe.De(48.50m), lineas, existentes, nuevo);
        var primero = Parte(LineaImpresa, 50m, 18.50m);

        var segundo = Parte(LineaImpresa, 50m, 30m);

        Assert.Equal(ResultadoDeTope.Dentro, Tope(primero));
        Assert.Equal(Importe.De(68.50m), primero.Monto);
        Assert.Equal(ResultadoDeTope.Dentro, Tope(segundo, primero));
        Assert.Equal(ResultadoDeTope.EnvioRepetido, Tope(Parte(LineaDigital, 1m, 0.01m), primero, segundo));
    }
}
