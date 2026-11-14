using System.Net;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Api;

public sealed class OrigenDelBordeTests : IAsyncLifetime
{
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class EventosEspia : IPublicadorDeEventosDeSeguridad
    {
        public List<EventoDeSeguridad> Eventos { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            lock (Eventos)
            {
                Eventos.Add(evento);
            }

            return Task.CompletedTask;
        }
    }

    private readonly EventosEspia _eventos = new();
    private readonly CalculadorDeHuellaHmac _huellas = new(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella)));
    private WebApplication? _app;
    private HttpClient _cliente = null!;

    public Task InitializeAsync() => Iniciar(new OpcionesDeOrigenDelBorde(["172.16.0.0/12", "10.9.0.5"]));

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task Iniciar(OpcionesDeOrigenDelBorde opciones)
    {
        if (_app is not null)
        {
            _cliente.Dispose();
            await _app.DisposeAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp());
        builder.Services.AgregarOrigenDelBorde(opciones);
        builder.Services.AddSingleton<ICalculadorDeHuella>(_huellas);
        builder.Services.AddSingleton<IReloj>(new RelojDePrueba());
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(_eventos);
        _app = builder.Build();

        // La dirección de la conexión (el proxy) la fija la prueba, como lo haría Kestrel.
        _app.Use(async (contexto, siguiente) =>
        {
            var conexion = contexto.Request.Headers["X-Prueba-Conexion"].ToString();
            contexto.Connection.RemoteIpAddress = conexion.Length > 0 ? IPAddress.Parse(conexion) : IPAddress.Parse("172.18.0.5");
            await siguiente(contexto);
        });
        _app.UsarManejoDeErrores();
        _app.UsarOrigenDelBorde();
        _app.MapGet("/origen", (HttpContext contexto, IOrigenDeSolicitud origenes) => Results.Json(new
        {
            direccion = origenes.Actual.Direccion,
            huella = origenes.Actual.Huella.ToString(),
            desconocido = origenes.Actual.Huella.EsDesconocido,
            reenvioVisible = contexto.Request.Headers.ContainsKey("X-Forwarded-For") || contexto.Request.Headers.ContainsKey("Forwarded") || contexto.Request.Headers.ContainsKey("X-Origen-Reenviado"),
            cabeceraVisible = contexto.Request.Headers.ContainsKey("X-Origen-Cliente"),
        }));

        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    private sealed record Respuesta(string? Direccion, string Huella, bool Desconocido, bool ReenvioVisible, bool CabeceraVisible);

    private async Task<Respuesta> Pedir(Action<HttpRequestMessage>? configurar = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/origen");
        configurar?.Invoke(solicitud);
        var respuesta = await _cliente.SendAsync(solicitud);
        var json = System.Text.Json.JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;
        return new Respuesta(
            json.GetProperty("direccion").GetString(),
            json.GetProperty("huella").GetString()!,
            json.GetProperty("desconocido").GetBoolean(),
            json.GetProperty("reenvioVisible").GetBoolean(),
            json.GetProperty("cabeceraVisible").GetBoolean());
    }

    private string? UnicoMotivo()
    {
        lock (_eventos.Eventos)
        {
            var evento = Assert.Single(_eventos.Eventos);
            Assert.Equal(TipoDeEventoDeSeguridad.OrigenIndeterminable, evento.Tipo);
            Assert.Null(evento.DireccionCompleta);
            return evento.Motivo;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task ConLaConexionDelProxyYLaDireccionDelClienteFinal_ElOrigenEsEsaDireccion()
    {
        var respuesta = await Pedir(s => s.Headers.Add("X-Origen-Cliente", "203.0.113.7"));

        Assert.Equal("203.0.113.7", respuesta.Direccion);
        Assert.False(respuesta.Desconocido);
        Assert.Equal(_huellas.DeOrigen("203.0.113.7").ToString(), respuesta.Huella);
        lock (_eventos.Eventos)
        {
            Assert.Empty(_eventos.Eventos);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task UnaCabeceraDeReenvioDelCliente_SeIgnoraYSeUsaLaQueEntregoElBorde()
    {
        var respuesta = await Pedir(s =>
        {
            s.Headers.Add("X-Origen-Cliente", "203.0.113.7");
            s.Headers.Add("X-Forwarded-For", "198.51.100.99");
            s.Headers.Add("Forwarded", "for=198.51.100.99");
            s.Headers.Add("X-Origen-Reenviado", "198.51.100.99");
        });

        Assert.Equal("203.0.113.7", respuesta.Direccion);
        Assert.False(respuesta.ReenvioVisible);
        Assert.False(respuesta.CabeceraVisible);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task ConUnaConexionQueNoEsDeUnProxyConocido_LaCabeceraSeIgnoraYElOrigenEsDesconocido()
    {
        var respuesta = await Pedir(s =>
        {
            s.Headers.Add("X-Prueba-Conexion", "198.51.100.200");
            s.Headers.Add("X-Origen-Cliente", "203.0.113.7");
        });

        Assert.True(respuesta.Desconocido);
        Assert.Null(respuesta.Direccion);
        Assert.Equal(HuellaDeOrigen.Desconocido.ToString(), respuesta.Huella);
        Assert.Equal(OrigenDelBordeMiddleware.MotivoSinProxyConocido, UnicoMotivo());
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-20")]
    [InlineData("")]
    [InlineData("no-es-una-ip")]
    [InlineData("203.0.113.256")]
    [InlineData("203.0.113.7, 198.51.100.1")]
    [InlineData("203.0.113.7 198.51.100.1")]
    public async Task ConUnaCabeceraAusenteOInvalida_ElOrigenEsDesconocidoYQuedaUnEvento(string valor)
    {
        var respuesta = await Pedir(s =>
        {
            if (valor.Length > 0)
            {
                s.Headers.TryAddWithoutValidation("X-Origen-Cliente", valor);
            }
        });

        Assert.True(respuesta.Desconocido);
        Assert.Equal(valor.Length == 0 ? OrigenDelBordeMiddleware.MotivoSinCabecera : OrigenDelBordeMiddleware.MotivoDireccionInvalida, UnicoMotivo());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task ConLaCabeceraDuplicada_ElOrigenEsDesconocido()
    {
        var respuesta = await Pedir(s =>
        {
            s.Headers.TryAddWithoutValidation("X-Origen-Cliente", "203.0.113.7");
            s.Headers.TryAddWithoutValidation("X-Origen-Cliente", "198.51.100.1");
        });

        Assert.True(respuesta.Desconocido);
        Assert.Equal(OrigenDelBordeMiddleware.MotivoCabeceraDuplicada, UnicoMotivo());
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-20")]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.20")]
    [InlineData("172.20.1.1")]
    [InlineData("169.254.10.10")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("::ffff:10.0.0.5")]
    public async Task ConLaDireccionDeUnComponenteInterno_NoSeUsaComoOrigenDeUnCliente(string direccion)
    {
        var respuesta = await Pedir(s => s.Headers.Add("X-Origen-Cliente", direccion));

        Assert.True(respuesta.Desconocido);
        Assert.Equal(OrigenDelBordeMiddleware.MotivoDireccionInterna, UnicoMotivo());
    }

    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8:85a3:1::1")]
    [InlineData("::ffff:203.0.113.7")]
    [InlineData("8.8.8.8")]
    public async Task ConUnaDireccionPublicaDeCualquierFamilia_LaAcepta(string direccion)
    {
        var respuesta = await Pedir(s => s.Headers.Add("X-Origen-Cliente", direccion));

        Assert.False(respuesta.Desconocido);
        Assert.Equal(_huellas.DeOrigen(direccion), _huellas.DeOrigenEnRotacion(direccion)[0]);
        Assert.Equal(_huellas.DeOrigen(direccion).ToString(), respuesta.Huella);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public async Task ConDosDireccionesDeLaMismaRedIPv6_ElOrigenEsElMismo()
    {
        var uno = await Pedir(s => s.Headers.Add("X-Origen-Cliente", "2001:db8:85a3:1::1"));
        var otro = await Pedir(s => s.Headers.Add("X-Origen-Cliente", "2001:db8:85a3:1:ffff::9"));

        Assert.Equal(uno.Huella, otro.Huella);
        Assert.Equal("2001:db8:85a3:1::1", uno.Direccion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task ConUnaLlamadaEntreServicios_NoSeRegistraUnEventoDeOrigenIndeterminable()
    {
        var respuesta = await Pedir(s =>
        {
            s.Headers.Add("X-Prueba-Conexion", "10.9.0.5");
            s.Headers.Add("X-Credencial-De-Servicio", "credencial-de-servicio");
        });

        Assert.True(respuesta.Desconocido);
        lock (_eventos.Eventos)
        {
            Assert.Empty(_eventos.Eventos);
        }
    }

    [Fact]
    public async Task ConElProxyIndicadoPorDireccionExacta_TambienSeAceptaSuCabecera()
    {
        var respuesta = await Pedir(s =>
        {
            s.Headers.Add("X-Prueba-Conexion", "10.9.0.5");
            s.Headers.Add("X-Origen-Cliente", "203.0.113.7");
        });

        Assert.Equal("203.0.113.7", respuesta.Direccion);
    }

    [Fact]
    public async Task ParaDesarrolloLocal_SePuedenAceptarDireccionesInternasComoOrigen()
    {
        await Iniciar(new OpcionesDeOrigenDelBorde(["172.16.0.0/12"], aceptarDireccionesInternas: true));

        var respuesta = await Pedir(s => s.Headers.Add("X-Origen-Cliente", "10.0.0.5"));

        Assert.Equal("10.0.0.5", respuesta.Direccion);
    }

    [Fact]
    public void Opciones_SinProxiesOConUnoInvalido_Lanzan()
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDeOrigenDelBorde([]));
        Assert.Throws<ArgumentException>(() => new OpcionesDeOrigenDelBorde(["no-es-una-red"]));
        Assert.True(new OpcionesDeOrigenDelBorde(["172.16.0.0/12"]).EsProxyConocido(IPAddress.Parse("172.31.255.1")));
        Assert.False(new OpcionesDeOrigenDelBorde(["172.16.0.0/12"]).EsProxyConocido(IPAddress.Parse("172.32.0.1")));
        Assert.False(new OpcionesDeOrigenDelBorde(["172.16.0.0/12"]).EsProxyConocido(null));
        Assert.True(new OpcionesDeOrigenDelBorde(["::ffff:10.1.2.3"]).EsProxyConocido(IPAddress.Parse("10.1.2.3")));
    }
}
