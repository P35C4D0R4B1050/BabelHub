using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// La raíz del host de gestión (O-5, observación del 2026-11-27; WEB-35, AC-WEB-29): antes respondía <c>ECO-NO-ENCONTRADO</c> porque <c>/</c> solo existía en la tienda. Sin sesión lleva al ingreso y con sesión, a lo que usa el rol.
/// </summary>
public sealed class RaizDeLaGestionTests
{
    private static Task<AnfitrionWeb> Iniciar(SesionesWebDePrueba sesiones) =>
        AnfitrionConSesion.Iniciar(
            sesiones,
            HostWeb.Gestion,
            servicios: s => s.AddSingleton<IDesafioDelIngreso>(new DesafioDePrueba()),
            antesDeLasRutas: app => app.MapearElInicioDeLaGestion());

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task SinSesion_LaRaizDeLaGestionLlevaAlIngresoSinGuardarseEnCache()
    {
        await using var anfitrion = await Iniciar(new SesionesWebDePrueba());

        using var respuesta = await anfitrion.Cliente.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.False(respuesta.Headers.Contains("Clear-Site-Data"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task ConUnPasoDelIngresoPendiente_LaRaizLlevaAlIngresoQueLoMuestra()
    {
        await using var anfitrion = await Iniciar(new SesionesWebDePrueba());
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/");
        solicitud.Headers.Add("Cookie", $"{CookieDelDesafio.Nombre}={new string('d', 43)}");

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task ConLaSesionVencida_LaRaizLlevaAlIngresoYLimpiaElNavegador()
    {
        await using var anfitrion = await Iniciar(new SesionesWebDePrueba().ConCarga(EstadoDeCarga.VencidaPorInactividad));
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/");
        solicitud.Headers.Add("Cookie", AnfitrionConSesion.Cookie(HostWeb.Gestion));

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("\"cache\", \"storage\"", respuesta.Headers.GetValues("Clear-Site-Data").Single());
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-29")]
    [InlineData(RolWeb.Encargado, "/logistica/envios")]
    [InlineData(RolWeb.Logistica, "/logistica/envios")]
    [InlineData(RolWeb.AdministradorCentral, "/administracion/obras")]
    public async Task ConSesion_LaRaizLlevaAlDestinoDelRol(RolWeb rol, string destino)
    {
        await using var anfitrion = await Iniciar(new SesionesWebDePrueba().ConSesionValida(rol, Guid.NewGuid()));
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/");
        solicitud.Headers.Add("Cookie", AnfitrionConSesion.Cookie(HostWeb.Gestion));

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(destino, respuesta.Headers.Location!.OriginalString);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-29")]
    [InlineData(RolWeb.Cliente)]
    [InlineData(RolWeb.Visitante)]
    public void UnRolQueNoEsDelPersonal_NuncaVuelveALaRaiz(RolWeb rol) => Assert.Equal("/sin-permiso", InicioDeLaGestion.Destino(rol));
}
