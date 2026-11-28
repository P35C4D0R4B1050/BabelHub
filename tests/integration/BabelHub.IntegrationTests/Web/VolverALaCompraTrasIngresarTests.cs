using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Http;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Volver a la compra tras ingresar (O-11, observación del 2026-11-27; CAR-16, AC-CAR-13): quien pulsa pagar como visitante ingresa y vuelve a su carrito, también si en medio hay segundo factor o un dato mal escrito. Lo que viaja es una marca de lista cerrada, nunca un
/// destino: nada que llegue de fuera redirige a otra parte.
/// </summary>
public sealed class VolverALaCompraTrasIngresarTests : IAsyncLifetime
{
    private const string Marca = "name=\"continuar\" value=\"compra\"";
    private static readonly string Desafio = new('d', 43);
    private static readonly DateTimeOffset Vence = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly KeyValuePair<string, string>[] Datos = [new("correo", "ana@correo.example"), new("contrasena", "una frase larga y segura")];
    private static readonly KeyValuePair<string, string> Compra = new("continuar", "compra");

    private AccesoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await AccesoApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static Resultado<ResultadoDelIngreso> Completo(RolDeCuenta rol = RolDeCuenta.Cliente) => Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso
    {
        Credencial = new CredencialDeSesion(new ValorSecreto("jwt-secreto-de-prueba"), Vence, new ValorSecreto("renovacion-secreta-de-prueba"), Vence.AddDays(7), Guid.NewGuid(), rol, rol == RolDeCuenta.EncargadoDeSucursal ? Guid.NewGuid() : null, CorreoVerificado: true),
    });

    private static string CookieDelDesafio => $"{BabelHub.Ecommerce.Api.Web.CookieDelDesafio.Nombre}={Desafio}";

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task ElIngresoDeLaCompra_LlevaLaMarcaEnSuFormulario_YElIngresoNormalNo()
    {
        using var deLaCompra = await _host.Cliente.SendAsync(_host.Pedir("/ingreso?continuar=compra"));
        using var normal = await _host.Cliente.SendAsync(_host.Pedir("/ingreso"));

        var conMarca = await deLaCompra.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, deLaCompra.StatusCode);
        Assert.Contains(Marca, conMarca, StringComparison.Ordinal);
        Assert.Contains("vuelves a tu carrito", conMarca, StringComparison.Ordinal);
        Assert.Contains("cf-turnstile", conMarca, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"continuar\"", await normal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task UnClienteQueEstabaComprando_VuelveASuCarrito()
    {
        _host.Ingreso.Resultado = Completo();

        using var respuesta = await _host.Enviar("/ingreso", [.. Datos, Compra]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/carrito", respuesta.Headers.Location?.OriginalString);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-13")]
    [InlineData("https://sitio-ajeno.example/robo")]
    [InlineData("//sitio-ajeno.example")]
    [InlineData("/\\sitio-ajeno.example")]
    [InlineData("/administracion/usuarios")]
    [InlineData("")]
    public async Task CualquierOtroValor_SeIgnora_YNuncaDecideElDestino(string valor)
    {
        _host.Ingreso.Resultado = Completo();

        using var pantalla = await _host.Cliente.SendAsync(_host.Pedir("/ingreso?continuar=" + Uri.EscapeDataString(valor)));
        using var envio = await _host.Enviar("/ingreso", [.. Datos, new("continuar", valor)]);

        Assert.Equal(HttpStatusCode.OK, pantalla.StatusCode);
        Assert.DoesNotContain("name=\"continuar\"", await pantalla.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("/", envio.Headers.Location?.OriginalString);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-13")]
    [InlineData("/ingreso?volver=https%3A%2F%2Fsitio-ajeno.example")]
    [InlineData("/ingreso?continuar=compra&returnUrl=%2F%2Fsitio-ajeno.example")]
    public async Task CualquierOtroParametroDeConsulta_SigueRechazado(string ruta)
    {
        using var respuesta = await _host.Cliente.SendAsync(_host.Pedir(ruta));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Null(respuesta.Headers.Location);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task UnDatoMalEscrito_ConservaLaMarcaParaElSiguienteIntento()
    {
        using var respuesta = await _host.Enviar("/ingreso", [.. Datos, Compra]);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains(Marca, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task ConSegundoFactor_LaMarcaPasaAlSegundoPaso_YAlTerminarVuelveAlCarrito()
    {
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Exito(new ResultadoDelIngreso { Desafio = new DesafioDelIngreso(new ValorSecreto(Desafio), PasoPendienteDelIngreso.PresentarSegundoFactor, Vence) });
        using var credenciales = await _host.Enviar("/ingreso", [.. Datos, Compra]);
        using var paso = await _host.Cliente.SendAsync(_host.Pedir(credenciales.Headers.Location!.OriginalString, CookieDelDesafio));
        var htmlDelPaso = await paso.Content.ReadAsStringAsync();

        _host.Ingreso.Resultado = Completo();
        using var codigo = await _host.Enviar("/ingreso/segundo-factor", [new("codigo", "123456"), Compra], CookieDelDesafio);

        Assert.Equal("/ingreso?continuar=compra", credenciales.Headers.Location.OriginalString);
        Assert.Contains("action=\"/ingreso/segundo-factor\"", htmlDelPaso, StringComparison.Ordinal);
        Assert.Contains(Marca, htmlDelPaso, StringComparison.Ordinal);
        Assert.Equal("/carrito", codigo.Headers.Location?.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task UnCodigoMalEscrito_ConservaLaMarca()
    {
        _host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "codigo-invalido"));

        using var respuesta = await _host.Enviar("/ingreso/segundo-factor", [new("codigo", "000000"), Compra], CookieDelDesafio);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains(Marca, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-13")]
    public async Task ElPersonalEnLaGestion_NoCambiaDeDestinoPorLaMarca()
    {
        await using var gestion = await AccesoApiHost.Crear(HostWeb.Gestion);
        gestion.Ingreso.Resultado = Completo(RolDeCuenta.AdministradorCentral);

        using var respuesta = await gestion.Enviar("/ingreso", [.. Datos, Compra]);

        Assert.Equal("/administracion/obras", respuesta.Headers.Location?.OriginalString);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-13")]
    [InlineData("/checkout", "/ingreso?continuar=compra")]
    [InlineData("/checkout/pago", "/ingreso?continuar=compra")]
    [InlineData("/cuenta", "/ingreso")]
    [InlineData("/checkoutx", "/ingreso")]
    public void SinSesionEnElCheckout_SeLlegaAlIngresoDeLaCompra(string ruta, string destino)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Path = ruta;

        contexto.IrAlIngreso();

        Assert.Equal(destino, contexto.Response.Headers.Location.ToString());
    }
}
