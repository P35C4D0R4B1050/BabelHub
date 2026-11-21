using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// Lo que el host real del E-commerce compone entre Auditoría, Identidad y el correo (COMP-1): el grafo se valida al construir, pero una fábrica que pide un puerto sin registrar solo falla al resolverlo en una corrida, y los dos
/// módulos se probaban cada uno con sus dobles. Cada prueba <b>resuelve</b> en el host compuesto lo que el trabajo de fondo o la operación pedirá. Sin base de datos: no se abre ninguna conexión.
/// </summary>
public sealed class ComposicionDeLaAuditoriaYElCorreoEnElHostTests
{
    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task ElTrabajoDeCorreoDeAlertas_SeResuelveEnElHost_ConElAdaptadorDeProduccionYLasDireccionesDeIdentidad()
    {
        await using var app = AnfitrionDelEcommerceTests.Compuesto();
        await using var alcance = app.Services.CreateAsyncScope();
        var servicios = alcance.ServiceProvider;

        var envio = servicios.GetRequiredService<IEnvioDeCorreoDeAlertas>();
        var agrupar = servicios.GetRequiredService<AgruparYEnviarAlertas>();

        Assert.IsType<EnvioDeCorreoDeAlertasDeProduccion>(envio);
        Assert.NotNull(agrupar);
        Assert.IsType<DireccionesDeCuentasDeIdentidad>(servicios.GetRequiredService<IDireccionesDeCuentas>());
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public async Task LosRegistrosYEventosDeIdentidad_VanPorElEnrutadorAlAlmacenAislado_ConIdentidadCompuesta()
    {
        await using var app = AnfitrionDelEcommerceTests.Compuesto();
        await using var alcance = app.Services.CreateAsyncScope();
        var servicios = alcance.ServiceProvider;

        Assert.IsType<EnrutadorDeRegistros>(servicios.GetRequiredService<IAlmacenDeRegistros>());
        Assert.IsType<EnrutadorDeEventosDeSeguridad>(servicios.GetRequiredService<IAlmacenDeEventosDeSeguridad>());
        Assert.NotNull(servicios.GetRequiredService<IAlmacenDeRegistrosAislados>());
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task LaDepuracionDeAuditoria_SeResuelveConLoQueAportaIdentidad_YSuPasoDeRestauracionEstaRegistrado()
    {
        await using var app = AnfitrionDelEcommerceTests.Compuesto();
        await using var alcance = app.Services.CreateAsyncScope();
        var servicios = alcance.ServiceProvider;

        var depurar = servicios.GetRequiredService<DepurarAuditoria>();
        var pasos = servicios.GetServices<IPasoDeRestauracion>().Select(p => p.Nombre).ToArray();

        Assert.NotNull(depurar);
        Assert.NotNull(servicios.GetRequiredService<ICuentasCanceladasPendientes>());
        Assert.NotNull(servicios.GetRequiredService<IDepuradorDeRegistrosAislados>());
        Assert.Contains(PasoDeRestauracionDeDepuracion.NombreDelPaso, pasos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task ElReintentoDeLosCorreosPendientes_CorreEnSegundoPlanoEnElModoNormal_YEnRestauracionNoCorreNada()
    {
        await using var normal = AnfitrionDelEcommerceTests.Compuesto(ModoDeArranque.Normal);
        await using var restauracion = AnfitrionDelEcommerceTests.Compuesto(ModoDeArranque.Restauracion);
        await using var alcance = normal.Services.CreateAsyncScope();

        var enNormal = normal.Services.GetServices<IHostedService>().OfType<ServicioDeTrabajosDeIdentidad>().SelectMany(s => s.Nombres).ToArray();
        var enRestauracion = restauracion.Services.GetServices<IHostedService>().OfType<ServicioDeTrabajosDeIdentidad>().ToArray();

        Assert.Contains(TrabajosDelCorreo.ReintentoDeLosPendientes, enNormal);
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<ReintentoDeCorreosPendientes>());
        Assert.Same(normal.Services.GetRequiredService<PostgresColaDeCorreosPendientes>(), normal.Services.GetRequiredService<IColaDeCorreosPendientes>());
        Assert.Empty(enRestauracion);
    }
}
