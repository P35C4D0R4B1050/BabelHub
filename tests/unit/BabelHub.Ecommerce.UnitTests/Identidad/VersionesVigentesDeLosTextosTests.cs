using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>Lo que Identidad informa a la web de los textos legales (IDE-57; AC-IDE-39): las mismas versiones con las que compara la aceptación, con su fecha de vigencia, y nunca una versión inventada.</summary>
public sealed class VersionesVigentesDeLosTextosTests
{
    private sealed class VersionesQueFallan : IVersionesDeTextosVigentes
    {
        public Task<VersionesDeTextos> Obtener(CancellationToken cancelacion = default) => throw new InvalidOperationException("No hay versiones de textos vigentes: falta sembrarlas.");

        public Task<(VersionesDeTextos Versiones, DateOnly VigenteDesde)> ObtenerConVigencia(CancellationToken cancelacion = default) => throw new InvalidOperationException("No hay versiones de textos vigentes: falta sembrarlas.");
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public async Task Informa_las_tres_versiones_vigentes_y_la_fecha_desde_la_que_rigen()
    {
        var versiones = new VersionesDePrueba(new VersionesDeTextos("t-7", "p-3", "r-2")) { VigenteDesde = new DateOnly(2026, 11, 20) };

        var vigentes = await new VersionesVigentesDeLosTextosDeIdentidad(versiones).Consultar();

        Assert.Equal(new VersionesVigentesDeLosTextos("t-7", "p-3", "r-2", new DateOnly(2026, 11, 20)), vigentes);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public async Task Sigue_a_la_fila_vigente_sin_cache_y_si_no_se_puede_leer_lanza_en_vez_de_inventar_una_version()
    {
        var versiones = new VersionesDePrueba(new VersionesDeTextos("t-7", "p-3", "r-2"));
        var consulta = new VersionesVigentesDeLosTextosDeIdentidad(versiones);
        Assert.Equal("t-7", (await consulta.Consultar()).Terminos);

        versiones.Vigentes = new VersionesDeTextos("t-8", "p-3", "r-2");

        Assert.Equal("t-8", (await consulta.Consultar()).Terminos);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VersionesVigentesDeLosTextosDeIdentidad(new VersionesQueFallan()).Consultar());
    }
}
