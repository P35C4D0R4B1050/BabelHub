using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>El importe en soles, los medios de pago, los motivos de rechazo y los plazos del pago (T01; PAG-03, PAG-08, PAG-09, PAG-15, PAG-18).</summary>
public sealed class ImporteYPlazosTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void El_total_de_una_compra_con_envio_se_cobra_en_soles_con_dos_decimales()
    {
        var total = Importe.De(100.00m) + Importe.De(23.90m);

        Assert.Equal(123.90m, total.Valor);
        Assert.Equal(12_390, total.EnCentimos);
        Assert.Equal("PEN", Importe.Moneda);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-01")]
    [InlineData(-0.01)]
    [InlineData(10.001)]
    [InlineData(0.005)]
    public void Un_importe_negativo_o_con_mas_de_dos_decimales_se_rechaza(double valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Importe.De((decimal)valor));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Un_importe_con_ceros_a_la_derecha_se_acepta_y_se_compara_por_valor()
    {
        Assert.Equal(Importe.De(10m), Importe.De(10.00m));
        Assert.Equal(Importe.De(10.5m), Importe.De(10.50m));
        Assert.True(Importe.De(10m) < Importe.De(10.01m));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Un_importe_se_crea_desde_centimos_y_la_resta_no_puede_quedar_negativa()
    {
        Assert.Equal(Importe.De(0.05m), Importe.DesdeCentimos(5));
        Assert.Equal(Importe.De(2.50m), Importe.De(10m) - Importe.De(7.50m));
        Assert.Throws<InvalidOperationException>(() => Importe.De(1m) - Importe.De(2m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Importe.DesdeCentimos(-1));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void El_importe_se_muestra_sin_perder_los_decimales()
    {
        Assert.Equal("123.90", Importe.De(123.9m).ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void Solo_se_puede_pagar_con_yape_o_con_tarjeta()
    {
        Assert.Equal(new[] { MedioDePago.Yape, MedioDePago.Tarjeta }, Enum.GetValues<MedioDePago>());
        Assert.True(MedioDePagoExtensiones.TryDesdeTexto("yape", out var yape));
        Assert.Equal(MedioDePago.Yape, yape);
        Assert.True(MedioDePagoExtensiones.TryDesdeTexto("tarjeta", out var tarjeta));
        Assert.Equal(MedioDePago.Tarjeta, tarjeta);
        Assert.False(MedioDePagoExtensiones.TryDesdeTexto("plin", out _));
        Assert.False(MedioDePagoExtensiones.TryDesdeTexto(null, out _));
        Assert.Equal("yape", MedioDePago.Yape.ATexto());
        Assert.Equal("tarjeta", MedioDePago.Tarjeta.ATexto());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-09")]
    public void Los_motivos_de_rechazo_son_una_lista_cerrada_y_un_texto_desconocido_cae_en_otro()
    {
        Assert.Equal(MotivoDeRechazo.TarjetaRechazada, MotivoDeRechazoExtensiones.DesdeTexto("tarjeta_rechazada"));
        Assert.Equal(MotivoDeRechazo.Otro, MotivoDeRechazoExtensiones.DesdeTexto("el banco dijo que no, con el numero 4111111111111111"));
        Assert.Equal(MotivoDeRechazo.Otro, MotivoDeRechazoExtensiones.DesdeTexto(null));
        Assert.All(Enum.GetValues<MotivoDeRechazo>(), m => Assert.Equal(m, MotivoDeRechazoExtensiones.DesdeTexto(m.ATexto())));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void La_intencion_vence_a_los_15_minutos_con_impresos_y_a_los_30_en_solo_digital()
    {
        Assert.Equal(Inicio.AddMinutes(15), PlazosDelPago.VenceEn(Inicio, tieneImpresos: true, reservaExpiraEn: Inicio.AddMinutes(15)));
        Assert.Equal(Inicio.AddMinutes(30), PlazosDelPago.VenceEn(Inicio, tieneImpresos: false, reservaExpiraEn: null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Con_impresos_la_intencion_nunca_vence_despues_de_la_reserva()
    {
        Assert.Equal(Inicio.AddMinutes(9), PlazosDelPago.VenceEn(Inicio, tieneImpresos: true, reservaExpiraEn: Inicio.AddMinutes(9)));
        Assert.Equal(Inicio.AddMinutes(15), PlazosDelPago.VenceEn(Inicio, tieneImpresos: true, reservaExpiraEn: Inicio.AddMinutes(20)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Una_compra_con_impresos_exige_el_instante_de_la_reserva()
    {
        Assert.Throws<ArgumentException>(() => PlazosDelPago.VenceEn(Inicio, tieneImpresos: true, reservaExpiraEn: null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public void El_limite_de_un_pago_pendiente_es_de_30_minutos_desde_el_inicio_y_la_respuesta_se_espera_10_segundos()
    {
        Assert.Equal(Inicio.AddMinutes(30), PlazosDelPago.LimiteDePendienteEn(Inicio));
        Assert.Equal(TimeSpan.FromSeconds(10), PlazosDelPago.TiempoDeRespuesta);
        Assert.Equal(TimeSpan.FromSeconds(15), PlazosDelPago.EsperaDeReconciliacion);
        Assert.Equal(TimeSpan.FromMinutes(1), PlazosDelPago.IntervaloDeConsulta);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_pasados_30_minutos_es_tardio_y_a_los_30_exactos_todavia_no()
    {
        Assert.False(PlazosDelPago.EsTardio(Inicio, Inicio.AddMinutes(30), reservaExpiraEn: null));
        Assert.True(PlazosDelPago.EsTardio(Inicio, Inicio.AddMinutes(30).AddTicks(1), reservaExpiraEn: null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_con_la_reserva_expirada_es_tardio_aunque_no_pasen_30_minutos()
    {
        var reserva = Inicio.AddMinutes(15);

        Assert.False(PlazosDelPago.EsTardio(Inicio, reserva.AddTicks(-1), reserva));
        Assert.True(PlazosDelPago.EsTardio(Inicio, reserva, reserva));
        Assert.True(PlazosDelPago.EsTardio(Inicio, reserva.AddMinutes(1), reserva));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_de_una_compra_solo_digital_con_menos_de_30_minutos_no_es_tardio()
    {
        Assert.False(PlazosDelPago.EsTardio(Inicio, Inicio.AddMinutes(20), reservaExpiraEn: null));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void La_reserva_esta_expirada_desde_el_instante_en_que_vence()
    {
        var reserva = Inicio.AddMinutes(15);

        Assert.False(PlazosDelPago.ReservaExpirada(reserva, reserva.AddTicks(-1)));
        Assert.True(PlazosDelPago.ReservaExpirada(reserva, reserva));
        Assert.False(PlazosDelPago.ReservaExpirada(null, Inicio.AddDays(1)));
    }
}
