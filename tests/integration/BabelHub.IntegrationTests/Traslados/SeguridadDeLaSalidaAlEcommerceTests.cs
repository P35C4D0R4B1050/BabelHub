using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// Revisión de seguridad de INV-4c: la salida al E-commerce solo viaja por HTTPS (<c>http</c> se rechaza al componer, salvo el bucle local de las pruebas) y nunca sigue una redirección: una respuesta 3xx no reenvía la
/// credencial <c>solicitar-aviso</c> a otra ruta y se clasifica como error permanente.
/// </summary>
public sealed class SeguridadDeLaSalidaAlEcommerceTests
{
    [Theory]
    [InlineData("http://ecommerce.interno")]
    [InlineData("ftp://ecommerce.interno")]
    public void UnaDireccionQueNoEsHttpsSeRechazaAlComponer(string direccion)
    {
        var servicios = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => servicios.AgregarSalidaAlEcommerce(new Uri(direccion)));
    }

    [Fact]
    public void UnaDireccionRelativaSeRechazaAlComponer()
    {
        var servicios = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => servicios.AgregarSalidaAlEcommerce(new Uri("/api", UriKind.Relative)));
    }

    [Fact]
    public async Task UnaRedireccionNoSeSigue_NoReenviaLaCredencial_YEsUnErrorPermanente()
    {
        var llamadas = new List<(string Ruta, bool ConCredencial)>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.Run(c =>
        {
            lock (llamadas)
            {
                llamadas.Add((c.Request.Path.Value ?? string.Empty, c.Request.Headers.ContainsKey("X-Credencial-De-Servicio")));
            }

            if (c.Request.Path.Value!.EndsWith("/avisos", StringComparison.Ordinal))
            {
                c.Response.StatusCode = StatusCodes.Status302Found;
                c.Response.Headers.Location = "/otra-ruta-que-no-debe-recibir-la-credencial";
            }

            return Task.CompletedTask;
        });
        await app.StartAsync();

        var servicios = new ServiceCollection();
        servicios.AddSingleton(new OpcionesDeCredencialesDeSalida([new(AlcanceDeServicio.SolicitarAviso, new ValorSecreto("credencial-de-solicitar-aviso-abcdefghijklmnop"))]));
        servicios.AddSingleton<IOrigenDeSolicitud>(new OrigenPorFlujo());
        servicios.AgregarSalidaAlEcommerce(new Uri(app.Urls.First()), new OpcionesDelClienteDeServicio { ReintentosMaximos = 0 }, permitirHttp: true);
        await using var proveedor = servicios.BuildServiceProvider();
        var envio = proveedor.GetRequiredService<IEnvioAlEcommerce>();

        var resultado = await envio.EnviarAviso(new AvisoDeTrasladoParaEnviar("traslado:6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1:aprobado", "ayacucho", 42, "aprobado", "destino", 3, new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero)));

        Assert.Equal(ResultadoDeEnvio.Permanente, resultado);
        var (ruta, conCredencial) = Assert.Single(llamadas);
        Assert.EndsWith("/avisos", ruta, StringComparison.Ordinal);
        Assert.True(conCredencial);
        Assert.DoesNotContain(llamadas, l => l.Ruta.Contains("otra-ruta", StringComparison.Ordinal));
    }

    [Fact]
    public void LosTrabajosDeFondoDeReservasAjustesYTrasladosSeRegistranLosTres_NingunoSeDeduplicaPorTipo()
    {
        var servicios = new ServiceCollection();

        servicios.AgregarTrabajosDeReservas(soloEnModoNormal: true);
        servicios.AgregarTrabajosDeAjustes(soloEnModoNormal: true);
        servicios.AgregarTrabajosDeTraslados(soloEnModoNormal: true);

        Assert.Equal(3, servicios.Count(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)));
    }

    [Fact]
    public void FueraDelModoNormalNoSeRegistraNingunTrabajoDeFondo()
    {
        var servicios = new ServiceCollection();

        servicios.AgregarTrabajosDeReservas(soloEnModoNormal: false);
        servicios.AgregarTrabajosDeAjustes(soloEnModoNormal: false);
        servicios.AgregarTrabajosDeTraslados(soloEnModoNormal: false);

        Assert.DoesNotContain(servicios, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }
}
