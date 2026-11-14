using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Los textos legales con su versión y vigencia (WEB-29, T26): la versión la informa Identidad (un puerto); el cuerpo son recursos versionados de la web.</summary>
public sealed class TextosLegalesTests
{
    private sealed class VersionesFalsas(Func<TipoDeTextoLegal, VersionVigenteDeTexto?> respuesta) : IVersionesDeTextosLegales
    {
        public Task<VersionVigenteDeTexto?> ObtenerVigente(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) => Task.FromResult(respuesta(tipo));
    }

    private sealed class ContenidoFalso(bool provisional = true) : IContenidoDeTextosLegales
    {
        public Task<CuerpoDeTextoLegal?> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) =>
            Task.FromResult<CuerpoDeTextoLegal?>(new CuerpoDeTextoLegal(
                $"Título de {tipo}",
                "Resumen de lo esencial.",
                [new SeccionDeTextoLegal("Uso del servicio", ["Primer párrafo.", "Segundo párrafo."])],
                provisional));
    }

    [Theory]
    [InlineData(TipoDeTextoLegal.Terminos)]
    [InlineData(TipoDeTextoLegal.Privacidad)]
    [InlineData(TipoDeTextoLegal.Reembolso)]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Cada_texto_muestra_su_version_y_su_fecha_de_vigencia_que_informa_Identidad(TipoDeTextoLegal tipo)
    {
        var consulta = new ConsultarTextoLegal(
            new VersionesFalsas(_ => new VersionVigenteDeTexto("2026-11-14", new DateOnly(2026, 11, 14))),
            new ContenidoFalso());

        var resultado = await consulta.Obtener(tipo);

        Assert.True(resultado.EsExito);
        Assert.Equal("2026-11-14", resultado.Valor.Version);
        Assert.Equal(new DateOnly(2026, 11, 14), resultado.Valor.VigenteDesde);
        Assert.Equal(tipo, resultado.Valor.Tipo);
        Assert.Equal("Resumen de lo esencial.", resultado.Valor.Resumen);
        Assert.Equal(2, resultado.Valor.Secciones[0].Parrafos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Un_texto_provisional_se_marca_como_tal()
    {
        var consulta = new ConsultarTextoLegal(
            new VersionesFalsas(_ => new VersionVigenteDeTexto("v1", new DateOnly(2026, 1, 1))),
            new ContenidoFalso(provisional: true));

        var resultado = await consulta.Obtener(TipoDeTextoLegal.Terminos);

        Assert.True(resultado.Valor.Provisional);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Si_Identidad_no_informa_la_version_el_texto_no_esta_disponible_y_no_se_inventa_una()
    {
        var consulta = new ConsultarTextoLegal(new VersionesFalsas(_ => null), new ContenidoFalso());

        var resultado = await consulta.Obtener(TipoDeTextoLegal.Terminos);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Si_Identidad_falla_el_texto_no_esta_disponible()
    {
        var consulta = new ConsultarTextoLegal(
            new VersionesFalsas(_ => throw new HttpRequestException("caído")),
            new ContenidoFalso());

        var resultado = await consulta.Obtener(TipoDeTextoLegal.Privacidad);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Un_tipo_inexistente_no_se_encuentra()
    {
        var consulta = new ConsultarTextoLegal(
            new VersionesFalsas(_ => new VersionVigenteDeTexto("v1", new DateOnly(2026, 1, 1))),
            new ContenidoFalso());

        var resultado = await consulta.Obtener((TipoDeTextoLegal)99);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Theory]
    [InlineData("terminos", TipoDeTextoLegal.Terminos)]
    [InlineData("privacidad", TipoDeTextoLegal.Privacidad)]
    [InlineData("reembolso", TipoDeTextoLegal.Reembolso)]
    [Trait("Criterio", "AC-WEB-24")]
    public void El_tipo_se_resuelve_por_el_nombre_de_la_ruta_y_solo_acepta_los_tres(string clave, TipoDeTextoLegal esperado)
    {
        Assert.True(TiposDeTextoLegal.TryParse(clave, out var tipo));
        Assert.Equal(esperado, tipo);
        Assert.Equal(clave, TiposDeTextoLegal.Clave(esperado));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TERMINOS")]
    [InlineData("../privacidad")]
    [InlineData("otro")]
    [Trait("Criterio", "AC-WEB-24")]
    public void Un_nombre_desconocido_no_es_un_texto_legal(string clave) =>
        Assert.False(TiposDeTextoLegal.TryParse(clave, out _));
}

/// <summary>El enlace firmado nunca aparece en el texto de un registro (SEG-10).</summary>
public sealed class EnlaceDeDescargaTests
{
    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public void El_texto_del_enlace_no_incluye_la_url_firmada()
    {
        var enlace = new Application.Biblioteca.EnlaceDeDescarga(new Uri("https://almacen.example/a.epub?X-Signature=SECRETO"), DateTimeOffset.UnixEpoch);

        var texto = $"{enlace}";

        Assert.DoesNotContain("SECRETO", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("almacen.example", texto, StringComparison.Ordinal);
    }
}
