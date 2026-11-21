using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// La versión vigente de los textos legales sale de Identidad (IDE-57) y no del proveedor provisional (WEB-29, AC-WEB-24): la web muestra y envía la misma versión que Identidad compara al registrar la aceptación. Sin base de datos:
/// el adaptador y la composición con dobles de Identidad.
/// </summary>
public sealed class VersionesDeTextosDeIdentidadTests
{
    private sealed class IdentidadFalsa(Func<VersionesVigentesDeLosTextos> respuesta) : IVersionesVigentesDeLosTextos
    {
        public Task<VersionesVigentesDeLosTextos> Consultar(CancellationToken cancelacion = default) => Task.FromResult(respuesta());
    }

    private static readonly VersionesVigentesDeLosTextos Vigentes = new("t-2026-11", "p-2026-10", "r-2026-09", new DateOnly(2026, 11, 20));

    [Theory]
    [InlineData(TipoDeTextoLegal.Terminos, "t-2026-11")]
    [InlineData(TipoDeTextoLegal.Privacidad, "p-2026-10")]
    [InlineData(TipoDeTextoLegal.Reembolso, "r-2026-09")]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Cada_texto_informa_la_version_de_Identidad_y_la_fecha_del_conjunto(TipoDeTextoLegal tipo, string esperada)
    {
        var version = await new VersionesDeTextosDeIdentidad(new IdentidadFalsa(() => Vigentes)).ObtenerVigente(tipo);

        Assert.Equal(new VersionVigenteDeTexto(esperada, new DateOnly(2026, 11, 20)), version);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Si_Identidad_no_puede_leer_las_versiones_el_texto_no_esta_disponible_y_no_se_inventa_una_version()
    {
        var consulta = new ConsultarTextoLegal(
            new VersionesDeTextosDeIdentidad(new IdentidadFalsa(() => throw new InvalidOperationException("No hay versiones de textos vigentes: falta sembrarlas."))),
            new ContenidoDeTextosEmbebido());

        var resultado = await consulta.Obtener(TipoDeTextoLegal.Terminos);

        Assert.False(resultado.EsExito);
        Assert.Equal("version-de-textos-no-disponible", resultado.Error!.Clave);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Con_Identidad_compuesta_la_version_provisional_nunca_se_usa_sin_importar_el_orden_de_registro_ni_el_modo(bool permitirProvisionales, bool identidadAntesQueLaWeb)
    {
        var servicios = new ServiceCollection();
        void RegistrarIdentidad() => servicios.AddScoped<IVersionesVigentesDeLosTextos>(_ => new IdentidadFalsa(() => Vigentes));
        if (identidadAntesQueLaWeb)
        {
            RegistrarIdentidad();
        }

        servicios.AgregarModuloWeb(permitirProvisionales);
        if (!identidadAntesQueLaWeb)
        {
            RegistrarIdentidad();
        }

        await using var proveedor = servicios.BuildServiceProvider();
        await using var alcance = proveedor.CreateAsyncScope();

        var versiones = alcance.ServiceProvider.GetRequiredService<IVersionesDeTextosLegales>();
        var version = await versiones.ObtenerVigente(TipoDeTextoLegal.Terminos);

        Assert.IsType<VersionesDeTextosDeIdentidad>(versiones);
        Assert.Equal("t-2026-11", version!.Version);
        Assert.NotEqual(VersionesDeTextosProvisionales.Version.Version, version.Version);
    }

    [Theory]
    [InlineData(true, typeof(VersionesDeTextosProvisionales))]
    [InlineData(false, typeof(VersionesDeTextosNoDisponibles))]
    public void Sin_Identidad_compuesta_se_cae_a_la_provisional_solo_donde_se_permite_y_si_no_a_no_disponible(bool permitirProvisionales, Type esperado)
    {
        var servicios = new ServiceCollection();
        servicios.AgregarModuloWeb(permitirProvisionales);
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();

        Assert.IsType(esperado, alcance.ServiceProvider.GetRequiredService<IVersionesDeTextosLegales>());
    }
}
