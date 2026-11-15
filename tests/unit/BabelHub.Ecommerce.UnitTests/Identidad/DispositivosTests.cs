using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>La marca de dispositivo, la región habitual y el cambio de correo pendiente (T09; AC-IDE-47, AC-IDE-48; IDE-53, IDE-54, IDE-55).</summary>
public sealed class DispositivosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Usuario = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private static string Huella(char c) => new(c, 64);

    private static Dispositivo Conocido(char c = 'a', string region = "PE") => Dispositivo.Emitir(Usuario, Huella(c), region, Ahora.AddDays(-10));

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public void Un_dispositivo_con_marca_de_un_ingreso_anterior_en_una_region_habitual_es_de_confianza()
    {
        var confianza = ConfianzaDeDispositivos.Evaluar(Huella('a'), "pe", [Conocido()], Ahora);

        Assert.True(confianza.DispositivoReconocido);
        Assert.True(confianza.RegionHabitual);
        Assert.True(confianza.PermiteAplicarElCambio);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-48")]
    [InlineData("b", "PE", false, true)]
    [InlineData("a", "US", true, false)]
    [InlineData("b", "US", false, false)]
    [InlineData(null, "PE", false, true)]
    [InlineData("a", null, true, false)]
    [InlineData("a", "ZZ", true, false)]
    [InlineData("a", "Peru", true, false)]
    public void Sin_marca_conocida_o_sin_region_habitual_el_cambio_no_se_puede_aplicar(string? marca, string? region, bool reconocido, bool habitual)
    {
        var presentada = marca is null ? null : Huella(marca[0]);

        var confianza = ConfianzaDeDispositivos.Evaluar(presentada, region, [Conocido()], Ahora);

        Assert.Equal(reconocido, confianza.DispositivoReconocido);
        Assert.Equal(habitual, confianza.RegionHabitual);
        Assert.False(confianza.PermiteAplicarElCambio);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public void Un_dispositivo_congelado_ya_no_cuenta_como_reconocido_para_decidir()
    {
        var congelado = Conocido();
        congelado.Congelar(Ahora.AddHours(24));

        Assert.False(ConfianzaDeDispositivos.Evaluar(Huella('a'), "PE", [congelado], Ahora).DispositivoReconocido);
        Assert.True(ConfianzaDeDispositivos.Evaluar(Huella('a'), "PE", [congelado], Ahora.AddHours(24)).DispositivoReconocido);
        congelado.Descongelar();
        Assert.True(congelado.PermiteCambiosSensibles(Ahora));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-47")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void La_huella_de_la_marca_es_un_sha256_y_la_region_nunca_es_un_texto_libre(string huella)
    {
        Assert.Throws<ArgumentException>(() => Dispositivo.Emitir(Usuario, huella, "PE", Ahora));
        Assert.Equal("ZZ", Dispositivo.Emitir(Usuario, Huella('c'), "Lima, Peru, 12.04,-77.03", Ahora).Region);
        Assert.Equal("PE", Dispositivo.NormalizarRegion(" pe "));
        Assert.Equal("ZZ", Dispositivo.NormalizarRegion(null));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public void Desde_un_dispositivo_reconocido_el_cambio_espera_la_verificacion_y_se_aplica_sin_congelar_nada()
    {
        var cambio = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, " Nuevo@Correo.PE ", new ConfianzaDeLaSolicitud(true, true), Ahora);

        Assert.Equal("nuevo@correo.pe", cambio.CorreoNuevo);
        Assert.Equal(EstadoDelCambioDeCorreo.EsperandoVerificacion, cambio.Estado);
        Assert.False(cambio.CongelaAlDispositivo);

        Assert.Equal(ResultadoDelCambioDeCorreo.Hecho, cambio.CorreoNuevoVerificado(Ahora.AddMinutes(5)));
        Assert.Equal(EstadoDelCambioDeCorreo.Aplicado, cambio.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public void Desde_un_dispositivo_no_reconocido_el_cambio_no_se_aplica_y_congela_el_dispositivo_aunque_se_verifique_el_correo_nuevo()
    {
        var cambio = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, "nuevo@correo.pe", new ConfianzaDeLaSolicitud(false, false), Ahora);

        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, cambio.Estado);
        Assert.True(cambio.CongelaAlDispositivo);
        Assert.Equal(Ahora.AddHours(24), cambio.Vence);
        Assert.Equal(ResultadoDelCambioDeCorreo.NoCorresponde, cambio.CorreoNuevoVerificado(Ahora.AddMinutes(1)));
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, cambio.Estado);

        var solicitante = Conocido('e');
        solicitante.Congelar(cambio.Vence);
        Assert.False(solicitante.PermiteCambiosSensibles(Ahora.AddHours(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public void El_titular_confirma_desde_un_dispositivo_reconocido_dentro_de_las_24_horas_y_queda_verificar_el_correo_nuevo()
    {
        var cambio = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, "nuevo@correo.pe", new ConfianzaDeLaSolicitud(false, true), Ahora);

        Assert.Equal(ResultadoDelCambioDeCorreo.DesdeDispositivoNoReconocido, cambio.ConfirmarElTitular(Ahora.AddHours(1), desdeDispositivoReconocido: false));
        Assert.Equal(EstadoDelCambioDeCorreo.PendienteDeConfirmacion, cambio.Estado);

        Assert.Equal(ResultadoDelCambioDeCorreo.Hecho, cambio.ConfirmarElTitular(Ahora.AddHours(23), desdeDispositivoReconocido: true));
        Assert.Equal(EstadoDelCambioDeCorreo.EsperandoVerificacion, cambio.Estado);
        Assert.Equal(ResultadoDelCambioDeCorreo.Hecho, cambio.CorreoNuevoVerificado(Ahora.AddHours(23).AddMinutes(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-48")]
    public void El_titular_lo_rechaza_y_vencidas_las_24_horas_el_cambio_se_descarta()
    {
        var rechazado = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, "a@b.pe", new ConfianzaDeLaSolicitud(false, false), Ahora);
        var vencido = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, "a@b.pe", new ConfianzaDeLaSolicitud(false, false), Ahora);
        var tardio = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, "a@b.pe", new ConfianzaDeLaSolicitud(false, false), Ahora);

        Assert.Equal(ResultadoDelCambioDeCorreo.Hecho, rechazado.RechazarElTitular(Ahora.AddHours(2), true));
        Assert.Equal(EstadoDelCambioDeCorreo.Rechazado, rechazado.Estado);
        Assert.Equal(ResultadoDelCambioDeCorreo.NoCorresponde, rechazado.ConfirmarElTitular(Ahora.AddHours(3), true));

        Assert.False(vencido.DescartarSiVencio(Ahora.AddHours(23)));
        Assert.True(vencido.DescartarSiVencio(Ahora.AddHours(24)));
        Assert.Equal(EstadoDelCambioDeCorreo.Descartado, vencido.Estado);

        Assert.Equal(ResultadoDelCambioDeCorreo.FueraDePlazo, tardio.ConfirmarElTitular(Ahora.AddHours(25), true));
        Assert.Equal(EstadoDelCambioDeCorreo.Descartado, tardio.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-47")]
    public void Un_cambio_que_espera_la_verificacion_y_pasa_de_las_24_horas_se_descarta()
    {
        var cambio = CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Usuario, "a@b.pe", new ConfianzaDeLaSolicitud(true, true), Ahora);

        Assert.Equal(ResultadoDelCambioDeCorreo.FueraDePlazo, cambio.CorreoNuevoVerificado(Ahora.AddHours(24)));
        Assert.Equal(EstadoDelCambioDeCorreo.Descartado, cambio.Estado);
        Assert.Throws<ArgumentException>(() => CambioDeCorreoPendiente.Pedir(Guid.Empty, Usuario, "a@b.pe", new ConfianzaDeLaSolicitud(true, true), Ahora));
        Assert.Throws<ArgumentException>(() => CambioDeCorreoPendiente.Pedir(Guid.NewGuid(), Guid.Empty, "a@b.pe", new ConfianzaDeLaSolicitud(true, true), Ahora));
    }
}
