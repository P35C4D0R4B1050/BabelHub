using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T14: la solicitud al Inventario sale con el token de la sesión (nunca con el que ponga un llamador), sin cabecera de actor y sin que el token
/// aparezca en ningún registro, y una operación de usuario sin sesión utilizable no sale (WEB-04, regla 9, SEG-15).
/// </summary>
public sealed class TokenDeSalidaDeLaSesionTests
{
    private const string SecretoDeUsuario = "secreto-del-alcance-operacion-de-usuario-0001";
    private const string SecretoDeOrigen = "secreto-del-alcance-origen-reenviado-0003";
    private const string TokenDeLaSesion = "eyJ.token-de-la-sesion-de-prueba.firma-ficticia";

    private sealed class RegistroCapturado : ILoggerProvider
    {
        public List<string> Mensajes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Captura(this);

        public void Dispose()
        {
        }

        private sealed class Captura(RegistroCapturado dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (dueno.Mensajes)
                {
                    dueno.Mensajes.Add(formatter(state, exception) + exception);
                }
            }
        }
    }

    private sealed class Servidor : HttpMessageHandler
    {
        public List<HttpRequestMessage> Solicitudes { get; } = [];

        public List<Dictionary<string, string>> Cabeceras { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Solicitudes.Add(request);
            Cabeceras.Add(request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private static (HttpClient Cliente, Servidor Servidor, HttpContext Contexto, RegistroCapturado Registro) Armar(string nombreDelCliente = "inventario", bool clienteAntes = false)
    {
        var servidor = new Servidor();
        var registro = new RegistroCapturado();
        var servicios = new ServiceCollection();
        servicios.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(registro));
        servicios.AddSingleton(new OpcionesDeCredencialesDeSalida(
        [
            new(AlcanceDeServicio.OperacionDeUsuario, new ValorSecreto(SecretoDeUsuario)),
            new(AlcanceDeServicio.OrigenReenviado, new ValorSecreto(SecretoDeOrigen)),
        ]));
        servicios.AddSingleton<IOrigenDeSolicitud>(new OrigenDePrueba());
        void Cliente() => servicios.AgregarClienteDeServicio(nombreDelCliente, new Uri("https://destino.interno/"), new OpcionesDelClienteDeServicio { ReintentosMaximos = 0 })
            .ConfigurePrimaryHttpMessageHandler(() => servidor);
        if (clienteAntes)
        {
            Cliente();
        }

        servicios.AgregarTokenDelUsuarioDeLaSesion();
        if (!clienteAntes)
        {
            Cliente();
        }
        var proveedor = servicios.BuildServiceProvider();

        var contexto = new DefaultHttpContext();
        proveedor.GetRequiredService<IHttpContextAccessor>().HttpContext = contexto;
        return (proveedor.GetRequiredService<IHttpClientFactory>().CreateClient(nombreDelCliente), servidor, contexto, registro);
    }

    private static HttpRequestMessage OperacionDeUsuario() =>
        new HttpRequestMessage(HttpMethod.Post, "reservas").ConAlcance(AlcanceDeServicio.OperacionDeUsuario);

    private static ResultadoDeCredencial Asegurada() =>
        new(EstadoDeCredencial.Asegurada, new ActorVerificado(Guid.NewGuid(), RolWeb.Cliente, null), new CredencialEnMemoria(TokenDeLaSesion));

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task LaSolicitudAlInventario_SaleConElTokenDeLaSesion_YSinCabeceraDeActor()
    {
        var (cliente, servidor, contexto, _) = Armar();
        contexto.EstablecerCredencial(Asegurada());
        var solicitud = OperacionDeUsuario();
        solicitud.Headers.TryAddWithoutValidation("X-Actor", "administrador-central");
        solicitud.Headers.TryAddWithoutValidation("X-Usuario", Guid.NewGuid().ToString());
        solicitud.Headers.TryAddWithoutValidation("X-Rol", "administrador-central");

        await cliente.SendAsync(solicitud);

        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal("Bearer " + TokenDeLaSesion, cabeceras["Authorization"]);
        Assert.Equal(SecretoDeUsuario, cabeceras["X-Credencial-De-Servicio"]);
        Assert.False(cabeceras.ContainsKey("X-Actor"));
        Assert.False(cabeceras.ContainsKey("X-Usuario"));
        Assert.False(cabeceras.ContainsKey("X-Rol"));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData("proveedor-de-pagos", false)]
    [InlineData("proveedor-de-pagos", true)]
    [InlineData("turnstile", false)]
    public async Task UnClienteQueNoEsElDelInventario_NuncaRecibeElToken_AunqueDeclareLaOperacionDeUsuario(string cliente, bool clienteAntes)
    {
        var (http, servidor, contexto, registro) = Armar(cliente, clienteAntes);
        contexto.EstablecerCredencial(Asegurada());

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(OperacionDeUsuario()));

        Assert.Empty(servidor.Solicitudes);
        Assert.DoesNotContain(registro.Mensajes, m => m.Contains(TokenDeLaSesion, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElClienteDelInventario_RecibeElToken_ConCualquierOrdenDeRegistro()
    {
        var (http, servidor, contexto, _) = Armar(clienteAntes: true);
        contexto.EstablecerCredencial(Asegurada());

        await http.SendAsync(OperacionDeUsuario());

        Assert.Equal("Bearer " + TokenDeLaSesion, Assert.Single(servidor.Cabeceras)["Authorization"]);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElTokenQueUnLlamadorPongaEnLasOpciones_SeIgnora_PorqueManda_LaSesion()
    {
        var (cliente, servidor, contexto, _) = Armar();
        contexto.EstablecerCredencial(Asegurada());

        await cliente.SendAsync(OperacionDeUsuario().ConTokenDeUsuario("token-ajeno-que-no-debe-salir"));

        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal("Bearer " + TokenDeLaSesion, cabeceras["Authorization"]);
        Assert.DoesNotContain("token-ajeno", cabeceras["Authorization"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElTokenDeLaSesion_NoApareceEnNingunRegistroDeLaLlamada()
    {
        var (cliente, _, contexto, registro) = Armar();
        contexto.EstablecerCredencial(Asegurada());

        await cliente.SendAsync(OperacionDeUsuario());

        Assert.NotEmpty(registro.Mensajes);
        Assert.DoesNotContain(registro.Mensajes, m => m.Contains(TokenDeLaSesion, StringComparison.Ordinal));
        Assert.DoesNotContain(registro.Mensajes, m => m.Contains(SecretoDeUsuario, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData(EstadoDeCredencial.SesionCerrada)]
    [InlineData(EstadoDeCredencial.ServicioNoDisponible)]
    [InlineData(EstadoDeCredencial.SinSesion)]
    public async Task SinUnaCredencialAsegurada_LaOperacionDeUsuarioNoSale(EstadoDeCredencial estado)
    {
        var (cliente, servidor, contexto, _) = Armar();
        contexto.EstablecerCredencial(new ResultadoDeCredencial(estado, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.SendAsync(OperacionDeUsuario().ConTokenDeUsuario("token-ajeno")));

        Assert.Empty(servidor.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task SinSesionEnLaSolicitudOFueraDeUnaSolicitud_LaOperacionDeUsuarioNoSale()
    {
        var (cliente, servidor, _, _) = Armar();

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.SendAsync(OperacionDeUsuario()));
        Assert.Empty(servidor.Solicitudes);

        // Un trabajo de fondo no tiene contexto HTTP: tampoco hay token.
        var token = await new HttpTokenDelUsuarioDeLaSesion(new HttpContextAccessor { HttpContext = null }).Actual();
        Assert.Null(token);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElTokenDeLaSesion_SeLeeDeLaSolicitudEnCurso_YNoSeFiltraAOtra()
    {
        var accesor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var puerto = new HttpTokenDelUsuarioDeLaSesion(accesor);
        accesor.HttpContext.EstablecerCredencial(Asegurada());

        Assert.Equal(TokenDeLaSesion, (await puerto.Actual())!.Revelar());

        accesor.HttpContext = new DefaultHttpContext();
        Assert.Null(await puerto.Actual());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElTokenEntregado_NoSeImprimeNiSeSerializa()
    {
        var contexto = new DefaultHttpContext();
        contexto.EstablecerCredencial(Asegurada());
        var puerto = new HttpTokenDelUsuarioDeLaSesion(new HttpContextAccessor { HttpContext = contexto });

        var token = (await puerto.Actual())!;

        Assert.Equal(ValorSecreto.Redactado, token.ToString());
        Assert.DoesNotContain(TokenDeLaSesion, System.Text.Json.JsonSerializer.Serialize(token), StringComparison.Ordinal);
        var resultado = contexto.CredencialDeLaSesion()!;
        Assert.DoesNotContain(TokenDeLaSesion, resultado.Credencial!.ToString(), StringComparison.Ordinal);
        // Ni la credencial ni el resultado completo se serializan con el token (regla 9: nada del JWT sale del proceso).
        Assert.DoesNotContain(TokenDeLaSesion, System.Text.Json.JsonSerializer.Serialize(resultado.Credencial), StringComparison.Ordinal);
        Assert.DoesNotContain(TokenDeLaSesion, System.Text.Json.JsonSerializer.Serialize(resultado), StringComparison.Ordinal);
    }
}
