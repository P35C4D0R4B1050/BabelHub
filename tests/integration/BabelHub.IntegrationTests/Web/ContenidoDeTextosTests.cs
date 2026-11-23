using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Web;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El cuerpo de los textos legales como recursos de la web (WEB-29, T26): título, resumen, secciones y la marca de texto provisional.</summary>
public sealed class ContenidoDeTextosTests
{
    [Theory]
    [InlineData(TipoDeTextoLegal.Terminos, "Términos y condiciones")]
    [InlineData(TipoDeTextoLegal.Privacidad, "Política de privacidad")]
    [InlineData(TipoDeTextoLegal.Reembolso, "Cambios y devoluciones")]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Los_tres_textos_existen_con_titulo_resumen_secciones_y_marca_de_provisional(TipoDeTextoLegal tipo, string titulo)
    {
        var cuerpo = await new ContenidoDeTextosEmbebido().Obtener(tipo);

        Assert.NotNull(cuerpo);
        Assert.Equal(titulo, cuerpo.Titulo);
        Assert.False(string.IsNullOrWhiteSpace(cuerpo.Resumen));
        Assert.NotEmpty(cuerpo.Secciones);
        Assert.All(cuerpo.Secciones, s => Assert.NotEmpty(s.Parrafos));
        Assert.True(cuerpo.Provisional, "Hasta que la persona responsable redacte los textos definitivos, todos son provisionales y así se marcan.");
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task La_privacidad_enumera_todas_las_cookies_que_usa_el_sitio()
    {
        var cuerpo = await new ContenidoDeTextosEmbebido().Obtener(TipoDeTextoLegal.Privacidad);

        var texto = string.Join('\n', cuerpo!.Secciones.SelectMany(s => s.Parrafos));
        foreach (var cookie in new[] { "__Host-bh-tienda", "__Host-bh-gestion", "__Host-bh-desafio", "__Host-bh-dispositivo", "bh-tema", "bh-sucursal" })
        {
            Assert.Contains(cookie, texto, StringComparison.Ordinal);
        }

        Assert.Contains("protección de formularios", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public void El_analizador_separa_titulo_resumen_secciones_y_parrafos_e_ignora_lo_que_no_tiene_titulo()
    {
        var cuerpo = ContenidoDeTextosEmbebido.Analizar("TITULO: Uno\nRESUMEN: Dos\n## A\nPrimero\n\nSegundo\n## B\nTercero\n");

        Assert.NotNull(cuerpo);
        Assert.False(cuerpo.Provisional);
        Assert.Equal(["A", "B"], cuerpo.Secciones.Select(s => s.Titulo));
        Assert.Equal(["Primero", "Segundo"], cuerpo.Secciones[0].Parrafos);
        Assert.Null(ContenidoDeTextosEmbebido.Analizar("## A\nsin título"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task La_version_provisional_declara_su_nombre_y_su_vigencia()
    {
        var version = await new VersionesDeTextosProvisionales().ObtenerVigente(TipoDeTextoLegal.Terminos);

        Assert.Equal("0.1-provisional", version!.Version);
        Assert.Equal(new DateOnly(2026, 11, 14), version.VigenteDesde);
    }
}

/// <summary>Fuera de desarrollo los textos legales no usan la versión provisional (WEB-29).</summary>
public sealed class VersionDeTextosEnProduccionTests
{
    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Sin_versiones_provisionales_el_texto_legal_esta_no_disponible()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(s =>
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<BabelHub.Ecommerce.Application.Web.Interno.IVersionesDeTextosLegales, VersionesDeTextosNoDisponibles>(s));

        using var respuesta = await anfitrion.Cliente.GetAsync("/legal/terminos");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
    }
}
