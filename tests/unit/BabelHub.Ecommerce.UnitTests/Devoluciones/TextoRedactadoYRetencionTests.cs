using System.Text.Json;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>El texto libre redactado y la retención de dos años (T03; DEV-24, DEV-27, DEV-29, DEV-31; AC-DEV-26, AC-DEV-29, AC-DEV-31).</summary>
public sealed class TextoRedactadoYRetencionTests
{
    private const string Sembrado = "Mi dirección es Jr. Centinela 4321 y mi DNI 12345678 <script>alert(1)</script>";

    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid Cliente = Guid.Parse("c1000000-0000-0000-0000-000000000001");

    private static readonly Guid Personal = Guid.Parse("d1000000-0000-0000-0000-000000000001");

    // ---------- TextoRedactado ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public void El_texto_no_se_vuelca_en_ToString_ni_en_la_serializacion_ni_en_la_interpolacion()
    {
        var texto = TextoRedactado.Crear(Sembrado, TextoRedactado.LongitudMaximaDelTexto)!;

        Assert.Equal("[redactado]", texto.ToString());
        Assert.Equal("\"[redactado]\"", JsonSerializer.Serialize(texto));
        Assert.DoesNotContain("Centinela", $"{texto}", StringComparison.Ordinal);
        Assert.DoesNotContain("Centinela", JsonSerializer.Serialize(new { texto }), StringComparison.Ordinal);
        Assert.Contains("Centinela", texto.Valor, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<TextoRedactado>("\"hola\""));
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-31")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con\nsalto")]
    [InlineData("con\ttabulación")]
    [InlineData("con\u0007campana")]
    public void Un_texto_vacio_o_con_caracteres_de_control_no_se_crea(string? valor)
    {
        Assert.Null(TextoRedactado.Crear(valor, TextoRedactado.LongitudMaximaDelTexto));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public void El_largo_esta_acotado_300_el_motivo_y_2000_el_texto()
    {
        Assert.NotNull(TextoRedactado.Crear(new string('a', 300), TextoRedactado.LongitudMaximaDelMotivo));
        Assert.Null(TextoRedactado.Crear(new string('a', 301), TextoRedactado.LongitudMaximaDelMotivo));
        Assert.NotNull(TextoRedactado.Crear(new string('a', 2000), TextoRedactado.LongitudMaximaDelTexto));
        Assert.Null(TextoRedactado.Crear(new string('a', 2001), TextoRedactado.LongitudMaximaDelTexto));
        Assert.Equal("hola", TextoRedactado.Crear("  hola  ", 10)!.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public void Redactar_reemplaza_el_valor_por_uno_fijo_y_repetirlo_no_cambia_nada()
    {
        var texto = TextoRedactado.Crear(Sembrado, TextoRedactado.LongitudMaximaDelTexto)!;

        var redactado = texto.Redactar();

        Assert.True(redactado.EstaRedactado);
        Assert.Equal("[redactado]", redactado.Valor);
        Assert.Same(redactado, redactado.Redactar());
        Assert.False(texto.EstaRedactado);
        Assert.True(TextoRedactado.Rehidratar("lo que sea", redactado: true).EstaRedactado);
        Assert.Equal("[redactado]", TextoRedactado.Rehidratar("lo que sea", redactado: true).Valor);
        Assert.Equal("guardado", TextoRedactado.Rehidratar("guardado", redactado: false).Valor);
    }

    // ---------- Redacción de la devolución ----------

    private static Devolucion Reporte(out Guid acceso)
    {
        acceso = Guid.NewGuid();
        return Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Digital, OrigenDeDevolucion.Reporte, null, TextoRedactado.Crear("El archivo falla", 300)!, TextoRedactado.Crear(Sembrado, 2000)!, [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 30m, acceso)], Ahora, ResponsableDelCambio.Cliente(Cliente));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public void Redactar_por_cancelacion_quita_motivo_texto_responsable_del_cliente_y_acceso_y_conserva_el_resto()
    {
        var devolucion = Reporte(out _);
        devolucion.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
        var linea = devolucion.Lineas[0];

        var cambio = devolucion.Redactar(Ahora.AddDays(5), quitarElAcceso: true);

        Assert.True(cambio);
        Assert.True(devolucion.Motivo.EstaRedactado);
        Assert.True(devolucion.Texto!.EstaRedactado);
        Assert.Equal(Ahora.AddDays(5), devolucion.RedactadaEn);
        Assert.Null(linea.AccesoId);
        Assert.Null(linea.Cambios[0].Responsable.Id);
        Assert.Equal(TipoDeResponsable.Cliente, linea.Cambios[0].Responsable.Tipo);
        Assert.Equal(ResponsableDelCambio.Personal(Personal), linea.Cambios[1].Responsable);
        Assert.Equal(1, linea.Cantidad);
        Assert.Equal(30m, linea.Monto);
        Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, linea.Estado);
        Assert.Equal(Ahora.AddHours(1), linea.EstadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public void Redactar_es_idempotente_y_reaplicable()
    {
        var devolucion = Reporte(out _);
        Assert.True(devolucion.Redactar(Ahora, quitarElAcceso: true));
        var redactadaEn = devolucion.RedactadaEn;

        Assert.False(devolucion.Redactar(Ahora.AddDays(9), quitarElAcceso: true));
        Assert.Equal(redactadaEn, devolucion.RedactadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public void La_redaccion_por_retencion_conserva_la_referencia_al_acceso_y_quita_solo_el_responsable_del_cliente()
    {
        var devolucion = Reporte(out var acceso);

        devolucion.Redactar(Ahora, quitarElAcceso: false);

        Assert.Equal(acceso, devolucion.Lineas[0].AccesoId);
        Assert.Null(devolucion.Lineas[0].Cambios[0].Responsable.Id);
        Assert.True(devolucion.Motivo.EstaRedactado);
    }

    // ---------- Retención ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-29")]
    [InlineData("2026-11-20T15:00:00Z", "2028-11-19", false)]
    [InlineData("2026-11-20T15:00:00Z", "2028-11-20", true)]
    [InlineData("2026-11-20T15:00:00Z", "2028-11-21", true)]
    [InlineData("2026-11-21T03:00:00Z", "2028-11-20", true)]
    [InlineData("2026-11-21T05:00:00Z", "2028-11-20", false)]
    [InlineData("2024-02-29T12:00:00Z", "2026-02-27", false)]
    [InlineData("2024-02-29T12:00:00Z", "2026-02-28", true)]
    public void A_los_dos_anios_por_dia_de_Lima_se_cumple_el_plazo(string ultimoCambio, string hoy, bool vencida)
    {
        Assert.Equal(vencida, PoliticaDeRetencion.EstaVencida(DateTimeOffset.Parse(ultimoCambio, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(hoy, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public void La_devolucion_vence_a_los_dos_anios_del_ultimo_cambio_solo_con_todas_las_lineas_finales()
    {
        var devolucion = Devolucion.Solicitar(
            Guid.NewGuid(),
            TipoDeDevolucion.Impreso,
            OrigenDeDevolucion.Solicitud,
            "lima-central",
            TextoRedactado.Crear("Defecto", 300)!,
            null,
            [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 50m), new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 50m)],
            Ahora,
            ResponsableDelCambio.Cliente(Cliente));
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime).AddYears(2).AddDays(1);

        Assert.False(devolucion.EstaVencidaParaDepurar(hoy));
        devolucion.Rechazar(Ahora.AddDays(1), ResponsableDelCambio.Personal(Personal));
        Assert.True(devolucion.TodasFinales);
        Assert.False(devolucion.EstaVencidaParaDepurar(DateOnly.FromDateTime(Ahora.UtcDateTime).AddYears(2)));
        Assert.True(devolucion.EstaVencidaParaDepurar(hoy.AddDays(1)));
        devolucion.Redactar(Ahora.AddYears(2).AddDays(3), quitarElAcceso: false);
        Assert.False(devolucion.EstaVencidaParaDepurar(hoy.AddYears(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public void Una_linea_no_final_impide_la_depuracion_aunque_hayan_pasado_los_dos_anios()
    {
        var devolucion = Devolucion.Solicitar(Guid.NewGuid(), TipoDeDevolucion.Impreso, OrigenDeDevolucion.Solicitud, "lima-central", TextoRedactado.Crear("Defecto", 300)!, null, [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 50m)], Ahora, ResponsableDelCambio.Cliente(Cliente));
        devolucion.Aprobar(Ahora, ResponsableDelCambio.Personal(Personal));

        Assert.False(devolucion.EstaVencidaParaDepurar(DateOnly.FromDateTime(Ahora.UtcDateTime).AddYears(10)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public void La_cota_de_candidatas_nunca_deja_fuera_a_una_devolucion_vencida_incluido_el_29_de_febrero()
    {
        // Un último cambio cada seis horas durante tres años y un día de hoy cada día durante cuatro: toda devolución vencida está antes de la cota.
        var inicio = new DateTimeOffset(2023, 12, 25, 0, 0, 0, TimeSpan.Zero);
        for (var cambio = inicio; cambio < inicio.AddDays(3 * 366); cambio = cambio.AddHours(6))
        {
            for (var hoy = new DateOnly(2025, 12, 20); hoy < new DateOnly(2026, 3, 10); hoy = hoy.AddDays(1))
            {
                if (PoliticaDeRetencion.EstaVencida(cambio, hoy))
                {
                    Assert.True(cambio < PoliticaDeRetencion.CotaDeCandidatas(hoy), $"{cambio:O} {hoy}");
                }
            }
        }
    }
}
