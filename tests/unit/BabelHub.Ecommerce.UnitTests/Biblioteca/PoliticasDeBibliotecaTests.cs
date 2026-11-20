using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>Las políticas de descarga, la clave, los motivos de lista cerrada, la ayuda y la retención de dos años (T02).</summary>
public sealed class PoliticasDeBibliotecaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public void La_politica_fija_cinco_descargas_trescientos_segundos_y_diez_enlaces_por_hora()
    {
        Assert.Equal(5, PoliticaDeDescargas.MaximoDeDescargas);
        Assert.Equal(300, PoliticaDeDescargas.VigenciaDelEnlace.TotalSeconds);
        Assert.Equal(10, PoliticaDeDescargas.EnlacesPorClienteYHora);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public void La_clave_es_el_resumen_sha256_y_nunca_se_muestra()
    {
        var clave = ClaveDeDescarga.Crear("0123456789abcdef0123456789abcdef");

        Assert.NotNull(clave);
        Assert.Equal(32, clave.Resumen.Length);
        Assert.Equal(clave, ClaveDeDescarga.Crear("0123456789abcdef0123456789abcdef"));
        Assert.NotEqual(clave, ClaveDeDescarga.Crear("0123456789abcdef0123456789abcdee"));
        Assert.DoesNotContain("0123456789abcdef", clave.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(clave.Hexadecimal, clave.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("con espacios dentro de la clave")]
    [InlineData("clave-con-ñ-no-ascii-123")]
    public void Una_clave_mal_formada_se_rechaza(string? texto)
    {
        Assert.Null(ClaveDeDescarga.Crear(texto));
        Assert.Null(ClaveDeDescarga.Crear(new string('a', 129)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public void Los_motivos_de_lista_cerrada_ida_y_vuelta_y_rechazan_texto_libre()
    {
        foreach (var motivo in Enum.GetValues<MotivoDeAyuda>())
        {
            Assert.Equal(motivo, CodigosDeMotivos.AyudaDesdeCodigo(motivo.Codigo()));
        }

        foreach (var motivo in Enum.GetValues<MotivoDeRestitucion>())
        {
            Assert.Equal(motivo, CodigosDeMotivos.RestitucionDesdeCodigo(motivo.Codigo()));
        }

        foreach (var causa in Enum.GetValues<CausaDeRevocacion>())
        {
            Assert.Equal(causa, CodigosDeBiblioteca.CausaDesdeCodigo(causa.Codigo()));
        }

        Assert.Null(CodigosDeMotivos.AyudaDesdeCodigo("mi telefono es 999"));
        Assert.Null(CodigosDeMotivos.RestitucionDesdeCodigo(null));
        Assert.Null(CodigosDeBiblioteca.CausaDesdeCodigo("otra"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public void La_ayuda_nace_abierta_y_se_cierra_una_sola_vez()
    {
        var acceso = Guid.NewGuid();
        var ayuda = SolicitudDeAyuda.Abrir(acceso, MotivoDeAyuda.DescargasAgotadas, Ahora);

        Assert.Equal(EstadoDeAyuda.Abierta, ayuda.Estado);
        Assert.Equal(acceso, ayuda.AccesoId);
        Assert.Equal(Ahora, ayuda.CreadaEn);
        Assert.True(ayuda.Cerrar(Ahora.AddHours(1)));
        Assert.False(ayuda.Cerrar(Ahora.AddHours(2)));
        Assert.Equal(Ahora.AddHours(1), ayuda.CerradaEn);
        Assert.Throws<ArgumentException>(() => SolicitudDeAyuda.Abrir(Guid.Empty, MotivoDeAyuda.Otro, Ahora));
    }

    [Theory]
    [InlineData("2024-11-19", "2026-11-20", true)]  // 2 años y un día
    [InlineData("2024-11-21", "2026-11-20", false)] // 2 años menos un día
    [InlineData("2024-11-20", "2026-11-20", true)]  // exactamente 2 años (D09: hoy >= día + 2 años)
    [InlineData("2024-02-29", "2026-02-28", true)]  // bisiesto: 29-feb + 2 años = 28-feb
    [Trait("Criterio", "AC-BIB-23")]
    [Trait("Criterio", "AC-BIB-24")]
    public void La_retencion_es_de_dos_anios_por_dia_de_lima(string dia, string hoy, bool vencido)
    {
        Assert.Equal(vencido, PoliticaDeRetencion.EstaVencido(DateOnly.Parse(dia), DateOnly.Parse(hoy)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public void Depurar_un_registro_deja_solo_la_edicion_y_el_dia_y_es_idempotente()
    {
        var edicion = Guid.NewGuid();
        var registro = new RegistroDeDescarga(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), edicion, Ahora, new DateOnly(2024, 11, 19));

        var depurado = registro.Depurar();

        Assert.True(depurado.EstaDepurado);
        Assert.Null(depurado.AccesoId);
        Assert.Null(depurado.UsuarioId);
        Assert.Null(depurado.EntregadaEn);
        Assert.Equal(edicion, depurado.EdicionId);
        Assert.Equal(registro.Dia, depurado.Dia);
        Assert.Equal(depurado, depurado.Depurar());
        Assert.False(registro.EstaDepurado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public void La_clave_registrada_esta_vigente_hasta_su_vencimiento()
    {
        var clave = ClaveDeDescarga.Crear("0123456789abcdef0123456789abcdef")!;
        var registrada = new ClaveRegistrada(Guid.NewGuid(), clave, Ahora, Ahora.AddSeconds(300), Guid.NewGuid());

        Assert.True(registrada.EstaVigente(Ahora.AddSeconds(299)));
        Assert.False(registrada.EstaVigente(Ahora.AddSeconds(300)));
    }
}
