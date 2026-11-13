using System.Net;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class ClientesHttpDeServicioTests
{
    private const string SecretoDeUsuario = "secreto-del-alcance-operacion-de-usuario-0001";
    private const string SecretoDeDisponibilidad = "secreto-del-alcance-lectura-de-disponibilidad-02";
    private const string SecretoDeOrigen = "secreto-del-alcance-origen-reenviado-0003";
    private const string HuellaDelOrigen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";

    private sealed class ServidorSimulado(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private int _llamadas;

        public int Llamadas => _llamadas;

        public List<HttpRequestMessage> Solicitudes { get; } = [];

        public List<Dictionary<string, string>> CabecerasPorIntento { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var intento = Interlocked.Increment(ref _llamadas);
            lock (CabecerasPorIntento)
            {
                CabecerasPorIntento.Add(request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
            }

            return await responder(request, intento, cancellationToken);
        }
    }

    private sealed class OrigenDePrueba(OrigenDeSolicitud origen) : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => origen;
    }

    private static OpcionesDeCredencialesDeSalida Credenciales() => new(
    [
        new(AlcanceDeServicio.OperacionDeUsuario, new ValorSecreto(SecretoDeUsuario)),
        new(AlcanceDeServicio.LecturaDeDisponibilidad, new ValorSecreto(SecretoDeDisponibilidad)),
        new(AlcanceDeServicio.OrigenReenviado, new ValorSecreto(SecretoDeOrigen)),
    ]);

    private static HttpClient Cliente(
        ServidorSimulado servidor,
        OpcionesDelClienteDeServicio? opciones = null,
        OrigenDeSolicitud? origen = null)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton(Credenciales());
        servicios.AddSingleton<IOrigenDeSolicitud>(new OrigenDePrueba(origen ?? OrigenDeSolicitud.Desconocido));
        servicios.AgregarClienteDeServicio("inventario", new Uri("https://inventario.interno/"), opciones ?? Rapido())
            .ConfigurePrimaryHttpMessageHandler(() => servidor);
        return servicios.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("inventario");
    }

    private static OpcionesDelClienteDeServicio Rapido() => new()
    {
        Espera = TimeSpan.FromMilliseconds(600),
        RetrasoDelReintento = TimeSpan.FromMilliseconds(10),
        DuracionDelCorte = TimeSpan.FromSeconds(5),
    };

    private static HttpRequestMessage Solicitud(HttpMethod metodo, AlcanceDeServicio alcance = AlcanceDeServicio.LecturaDeDisponibilidad, string ruta = "disponibilidad") =>
        new HttpRequestMessage(metodo, ruta).ConAlcance(alcance);

    private static HttpResponseMessage Respuesta(HttpStatusCode estado) => new(estado);

    [Fact]
    public void LosPlazosPorDefecto_SonDiezSegundosYTreintaParaElModeloDeLenguaje()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), OpcionesDelClienteDeServicio.ParaLaClase(ClaseDeDependencia.Normal).Espera);
        Assert.Equal(TimeSpan.FromSeconds(30), OpcionesDelClienteDeServicio.ParaLaClase(ClaseDeDependencia.ProveedorDeLlm).Espera);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Enviar_ConUnaDependenciaQueNoResponde_CortaLaEsperaAlVencerElPlazo()
    {
        var servidor = new ServidorSimulado(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Respuesta(HttpStatusCode.OK);
        });
        var cliente = Cliente(servidor);
        var inicio = DateTime.UtcNow;

        var error = await Record.ExceptionAsync(() => cliente.SendAsync(Solicitud(HttpMethod.Post)));

        Assert.IsAssignableFrom<TimeoutRejectedException>(error);
        Assert.True(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Enviar_ConElPlazoVencido_LasDemasOperacionesSiguenFuncionando()
    {
        var servidor = new ServidorSimulado(async (solicitud, _, token) =>
        {
            if (solicitud.RequestUri!.AbsolutePath.EndsWith("lenta", StringComparison.Ordinal))
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }

            return Respuesta(HttpStatusCode.OK);
        });
        var cliente = Cliente(servidor);

        var lenta = Record.ExceptionAsync(() => cliente.SendAsync(Solicitud(HttpMethod.Post, ruta: "lenta")));
        var rapida = await cliente.SendAsync(Solicitud(HttpMethod.Get, ruta: "rapida"));

        Assert.Equal(HttpStatusCode.OK, rapida.StatusCode);
        Assert.NotNull(await lenta);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-13")]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task Enviar_ConUnaOperacionIdempotenteQueFallaDosVeces_LaReintentaYTerminaBien(string metodo)
    {
        var servidor = new ServidorSimulado((_, intento, _) => Task.FromResult(Respuesta(intento < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        var respuesta = await cliente.SendAsync(Solicitud(new HttpMethod(metodo)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(3, servidor.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-13")]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task Enviar_ConUnaOperacionNoIdempotente_NoLaReintentaNunca(string metodo)
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.ServiceUnavailable)));
        var cliente = Cliente(servidor);

        var respuesta = await cliente.SendAsync(Solicitud(new HttpMethod(metodo)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal(1, servidor.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Enviar_ConUnPostMarcadoConClaveDeIdempotencia_SiSePuedeReintentar()
    {
        var servidor = new ServidorSimulado((_, intento, _) => Task.FromResult(Respuesta(intento < 2 ? HttpStatusCode.BadGateway : HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        var respuesta = await cliente.SendAsync(Solicitud(HttpMethod.Post).ComoIdempotente());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(2, servidor.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Enviar_ConUnErrorDelCliente_NoSeReintenta()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.NotFound)));
        var cliente = Cliente(servidor);

        var respuesta = await cliente.SendAsync(Solicitud(HttpMethod.Get));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal(1, servidor.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-13")]
    public async Task Enviar_ConMuchosFallosSeguidos_AbreElCortacircuitosYFallaAlInstanteSinLlamar()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.InternalServerError)));
        var cliente = Cliente(servidor, Rapido() with { ReintentosMaximos = 0, LlamadasMinimasParaCortar = 5 });

        for (var i = 0; i < 10; i++)
        {
            try
            {
                await cliente.SendAsync(Solicitud(HttpMethod.Post));
            }
            catch (BrokenCircuitException)
            {
                break;
            }
        }

        var llamadasAntes = servidor.Llamadas;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => cliente.SendAsync(Solicitud(HttpMethod.Post)));

        Assert.Equal(llamadasAntes, servidor.Llamadas);
        Assert.InRange(llamadasAntes, 5, 9);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_AdjuntaLaCredencialDelAlcanceDeLaOperacion()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        await cliente.SendAsync(Solicitud(HttpMethod.Get));

        var cabeceras = Assert.Single(servidor.CabecerasPorIntento);
        Assert.Equal(SecretoDeDisponibilidad, cabeceras["X-Credencial-De-Servicio"]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        Assert.False(cabeceras.ContainsKey("X-Origen-Reenviado"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConUnaOperacionDeUsuario_LlevaSuTokenYNuncaUnaCabeceraDeActor()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var cliente = Cliente(servidor);
        var solicitud = Solicitud(HttpMethod.Post, AlcanceDeServicio.OperacionDeUsuario, "reservas").ConTokenDeUsuario("jwt-del-usuario");
        solicitud.Headers.TryAddWithoutValidation("X-Actor", "administrador-central");
        solicitud.Headers.TryAddWithoutValidation("X-Rol", "administrador");

        await cliente.SendAsync(solicitud);

        var cabeceras = Assert.Single(servidor.CabecerasPorIntento);
        Assert.Equal(SecretoDeUsuario, cabeceras["X-Credencial-De-Servicio"]);
        Assert.Equal("Bearer jwt-del-usuario", cabeceras["Authorization"]);
        Assert.False(cabeceras.ContainsKey("X-Actor"));
        Assert.False(cabeceras.ContainsKey("X-Rol"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConUnaOperacionDeUsuarioSinToken_NoSale()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.SendAsync(Solicitud(HttpMethod.Post, AlcanceDeServicio.OperacionDeUsuario, "reservas")));
        Assert.Equal(0, servidor.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_SinDeclararElAlcance_NoSale()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.GetAsync("disponibilidad"));
        Assert.Equal(0, servidor.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConUnAlcanceSinCredencialConfigurada_NoSale()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.SendAsync(Solicitud(HttpMethod.Get, AlcanceDeServicio.BajaDeTitular)));
        Assert.Equal(0, servidor.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConLaDireccionDelClienteFinal_LaReenviaConLaCredencialDeOrigen()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var origen = new OrigenDeSolicitud(HuellaDeOrigen.Crear(HuellaDelOrigen, "v1"), "203.0.113.7");
        var cliente = Cliente(servidor, origen: origen);

        await cliente.SendAsync(Solicitud(HttpMethod.Get).ConOrigenDelCliente());

        var cabeceras = Assert.Single(servidor.CabecerasPorIntento);
        Assert.Equal("203.0.113.7", cabeceras["X-Origen-Reenviado"]);
        Assert.Equal(SecretoDeOrigen, cabeceras["X-Credencial-De-Origen"]);
        Assert.Equal(SecretoDeDisponibilidad, cabeceras["X-Credencial-De-Servicio"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConUnOrigenDesconocido_NoReenviaNingunaDireccion()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var cliente = Cliente(servidor, origen: OrigenDeSolicitud.Desconocido);

        await cliente.SendAsync(Solicitud(HttpMethod.Get).ConOrigenDelCliente());

        var cabeceras = Assert.Single(servidor.CabecerasPorIntento);
        Assert.False(cabeceras.ContainsKey("X-Origen-Reenviado"));
        Assert.False(cabeceras.ContainsKey("X-Credencial-De-Origen"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConCabecerasDeReenvioDelCliente_LasDescartaYUsaSoloLasPropias()
    {
        var servidor = new ServidorSimulado((_, _, _) => Task.FromResult(Respuesta(HttpStatusCode.OK)));
        var origen = new OrigenDeSolicitud(HuellaDeOrigen.Crear(HuellaDelOrigen, "v1"), "203.0.113.7");
        var cliente = Cliente(servidor, origen: origen);
        var solicitud = Solicitud(HttpMethod.Get).ConOrigenDelCliente();
        solicitud.Headers.TryAddWithoutValidation("X-Origen-Reenviado", "198.51.100.99");
        solicitud.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.99");
        solicitud.Headers.TryAddWithoutValidation("Forwarded", "for=198.51.100.99");
        solicitud.Headers.TryAddWithoutValidation("X-Credencial-De-Servicio", "credencial-falsa");

        await cliente.SendAsync(solicitud);

        var cabeceras = Assert.Single(servidor.CabecerasPorIntento);
        Assert.Equal("203.0.113.7", cabeceras["X-Origen-Reenviado"]);
        Assert.Equal(SecretoDeDisponibilidad, cabeceras["X-Credencial-De-Servicio"]);
        Assert.False(cabeceras.ContainsKey("X-Forwarded-For"));
        Assert.False(cabeceras.ContainsKey("Forwarded"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task Enviar_ConUnReintento_CadaIntentoLlevaSusCredenciales()
    {
        var servidor = new ServidorSimulado((_, intento, _) => Task.FromResult(Respuesta(intento < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var cliente = Cliente(servidor);

        await cliente.SendAsync(Solicitud(HttpMethod.Get));

        Assert.Equal(2, servidor.CabecerasPorIntento.Count);
        Assert.All(servidor.CabecerasPorIntento, c => Assert.Equal(SecretoDeDisponibilidad, c["X-Credencial-De-Servicio"]));
    }

    [Fact]
    public async Task ConsultarPorIDependenciasExternas_ConElPlazoVencido_InformaQueNoEstaDisponible()
    {
        var servidor = new ServidorSimulado(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Respuesta(HttpStatusCode.OK);
        });
        var cliente = Cliente(servidor);
        var dependencias = new DependenciasExternas(new OpcionesDeDependencias(TimeSpan.FromSeconds(5)));

        var respuesta = await dependencias.Consultar(
            "inventario", ClaseDeDependencia.Normal, async token => (await cliente.SendAsync(Solicitud(HttpMethod.Post), token)).StatusCode, new ValidadorAceptaTodo());

        Assert.False(respuesta.HayRespuesta);
        Assert.Equal(CategoriaDeError.NoDisponible, respuesta.ComoNecesaria().Error!.Categoria);
    }

    [Fact]
    public void Credenciales_ConSecretosCortosRepetidosOAlcanceRepetido_Lanzan()
    {
        var largo = new ValorSecreto(new string('a', 40));
        var otroLargo = new ValorSecreto(new string('b', 40));

        Assert.Throws<ArgumentException>(() => new OpcionesDeCredencialesDeSalida([new(AlcanceDeServicio.Historial, new ValorSecreto("corto"))]));
        Assert.Throws<ArgumentException>(() => new OpcionesDeCredencialesDeSalida([new(AlcanceDeServicio.Historial, largo), new(AlcanceDeServicio.MarcarAlerta, largo)]));
        Assert.Throws<ArgumentException>(() => new OpcionesDeCredencialesDeSalida([new(AlcanceDeServicio.Historial, largo), new(AlcanceDeServicio.Historial, otroLargo)]));
    }

    private sealed class ValidadorAceptaTodo : IValidadorDeRespuestaExterna<HttpStatusCode>
    {
        public ResultadoDeValidacion Validar(HttpStatusCode respuesta) => ResultadoDeValidacion.Valido;
    }
}
