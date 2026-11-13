using System.Net;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class VerificadorTurnstileTests
{
    private const string Token = "0.AbCdEf-1234_token";
    private const string SecretoDeCloudflare = "secreto-de-turnstile-que-no-debe-aparecer-jamas";

    private sealed class ManejadorFalso(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, Uri? Destino, string Cuerpo)> Llamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Llamadas)
            {
                Llamadas.Add((request.Method, request.RequestUri, cuerpo));
            }

            return await responder(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode estado = HttpStatusCode.OK) =>
        new(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Exito(string hostname = "tienda.babelhub.pe", string accion = "registro") =>
        $$"""{"success":true,"challenge_ts":"2026-11-13T12:00:00Z","hostname":"{{hostname}}","action":"{{accion}}","error-codes":[]}""";

    private static (VerificadorTurnstile Verificador, ManejadorFalso Manejador) Armar(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        int simultaneas = 20,
        TimeSpan? espera = null)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("https://challenges.cloudflare.com/turnstile/v0/siteverify") };
        var opciones = new OpcionesDeTurnstile(new ValorSecreto(SecretoDeCloudflare), ["tienda.babelhub.pe"]) { MaximoDeVerificacionesSimultaneas = simultaneas };
        var dependencias = new DependenciasExternas(new OpcionesDeDependencias(espera ?? TimeSpan.FromSeconds(10)));
        return (new VerificadorTurnstile(http, opciones, dependencias), manejador);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Verificar_ConUnTokenValidoDeLaPaginaYElHostnameEsperados_Verifica()
    {
        var (verificador, manejador) = Armar((_, _) => Task.FromResult(Json(Exito())));

        var resultado = await verificador.Verificar(Token, "registro");

        Assert.Equal(ResultadoDeVerificacionDePersona.Verificada, resultado);
        var llamada = Assert.Single(manejador.Llamadas);
        Assert.Equal(HttpMethod.Post, llamada.Metodo);
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", llamada.Destino!.ToString());
        Assert.Contains("response=" + Uri.EscapeDataString(Token), llamada.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("secret=", llamada.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("remoteip", llamada.Cuerpo, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData("tienda.babelhub.pe", "ingreso")]
    [InlineData("sitio-ajeno.pe", "registro")]
    [InlineData("TIENDA.BABELHUB.PE.otro.pe", "registro")]
    public async Task Verificar_ConUnaAccionOUnHostnameQueNoSonLosEsperados_Falla(string hostname, string accion)
    {
        var (verificador, _) = Armar((_, _) => Task.FromResult(Json(Exito(hostname, accion))));

        Assert.Equal(ResultadoDeVerificacionDePersona.Fallida, await verificador.Verificar(Token, "registro"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Verificar_ConElHostnameEnMayusculas_LoAcepta()
    {
        var (verificador, _) = Armar((_, _) => Task.FromResult(Json(Exito("TIENDA.BabelHub.pe"))));

        Assert.Equal(ResultadoDeVerificacionDePersona.Verificada, await verificador.Verificar(Token, "registro"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""")]
    [InlineData("""{"success":false,"error-codes":["timeout-or-duplicate"]}""")]
    public async Task Verificar_ConUnTokenQueCloudflareRechaza_FallaIncluidoElTokenYaUsado(string json)
    {
        var (verificador, _) = Armar((_, _) => Task.FromResult(Json(json)));

        Assert.Equal(ResultadoDeVerificacionDePersona.Fallida, await verificador.Verificar(Token, "registro"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Verificar_ConUnTokenYaUsado_FallaSinVolverAConsultarACloudflare()
    {
        var (verificador, manejador) = Armar((_, _) => Task.FromResult(Json(Exito())));

        var primera = await verificador.Verificar(Token, "registro");
        var repetida = await verificador.Verificar(Token, "registro");

        Assert.Equal(ResultadoDeVerificacionDePersona.Verificada, primera);
        Assert.Equal(ResultadoDeVerificacionDePersona.Fallida, repetida);
        Assert.Single(manejador.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Verificar_ConUnTokenVacio_FallaSinConsultar(string token)
    {
        var (verificador, manejador) = Armar((_, _) => Task.FromResult(Json(Exito())));

        Assert.Equal(ResultadoDeVerificacionDePersona.Fallida, await verificador.Verificar(token, "registro"));
        Assert.Equal(ResultadoDeVerificacionDePersona.Fallida, await verificador.Verificar(new string('a', 2049), "registro"));
        Assert.Empty(manejador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Verificar_ConCloudflareQueNoResponde_FallaCerradoYPermiteReintentarElMismoToken()
    {
        var lento = true;
        var (verificador, manejador) = Armar(
            async (_, token) =>
            {
                if (lento)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }

                return Json(Exito());
            },
            espera: TimeSpan.FromMilliseconds(150));

        var sinRespuesta = await verificador.Verificar(Token, "registro");
        lento = false;
        var reintento = await verificador.Verificar(Token, "registro");

        Assert.Equal(ResultadoDeVerificacionDePersona.NoDisponible, sinRespuesta);
        Assert.Equal(ResultadoDeVerificacionDePersona.Verificada, reintento);
        Assert.Equal(2, manejador.Llamadas.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData(HttpStatusCode.InternalServerError, """{"success":false}""")]
    [InlineData(HttpStatusCode.OK, "esto no es json")]
    [InlineData(HttpStatusCode.OK, "null")]
    [InlineData(HttpStatusCode.OK, """{"success":true}""")]
    [InlineData(HttpStatusCode.OK, """{"success":true,"hostname":"tienda.babelhub.pe","action":"registro-con-una-accion-demasiado-larga-para-ser-valida"}""")]
    public async Task Verificar_ConUnaRespuestaConError_OFormatoInesperado_LaTrataComoSinRespuesta(HttpStatusCode estado, string json)
    {
        var (verificador, _) = Armar((_, _) => Task.FromResult(Json(json, estado)));

        Assert.Equal(ResultadoDeVerificacionDePersona.NoDisponible, await verificador.Verificar(Token, "registro"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Verificar_ConUnaFallaDeRed_FallaCerrado()
    {
        var (verificador, _) = Armar((_, _) => throw new HttpRequestException("sin red"));

        Assert.Equal(ResultadoDeVerificacionDePersona.NoDisponible, await verificador.Verificar(Token, "registro"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task Verificar_ConMasVerificacionesSimultaneasQueElBulkhead_RechazaElExcesoAlInstante()
    {
        var liberar = new TaskCompletionSource();
        var (verificador, manejador) = Armar(
            async (_, _) =>
            {
                await liberar.Task;
                return Json(Exito());
            },
            simultaneas: 2);

        var uno = verificador.Verificar("token-uno", "registro");
        var dos = verificador.Verificar("token-dos", "registro");
        while (manejador.Llamadas.Count < 2)
        {
            await Task.Delay(10);
        }

        var tercero = await verificador.Verificar("token-tres", "registro").WaitAsync(TimeSpan.FromSeconds(2));
        liberar.SetResult();
        await Task.WhenAll(uno, dos);
        var despues = await verificador.Verificar("token-tres", "registro");

        Assert.Equal(ResultadoDeVerificacionDePersona.NoDisponible, tercero);
        Assert.Equal(ResultadoDeVerificacionDePersona.Verificada, despues);
        Assert.Equal(ResultadoDeVerificacionDePersona.Verificada, await uno);
    }

    [Fact]
    public void Opciones_NoMuestranElSecretoEnSuTextoYExigenUnHostname()
    {
        var opciones = new OpcionesDeTurnstile(new ValorSecreto(SecretoDeCloudflare), ["tienda.babelhub.pe"]);

        Assert.DoesNotContain(SecretoDeCloudflare, opciones.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new OpcionesDeTurnstile(new ValorSecreto(SecretoDeCloudflare), [" "]));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public async Task ExigirVerificacionDePersona_ConElVerificadorReal_ExigeAlOrigenAutomatizadoYNoALaPersona()
    {
        var (verificador, _) = Armar((_, _) => Task.FromResult(Json(Exito())));
        var motor = new MotorDeLimitesLocal();
        var origen = HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1");
        var exigencia = new ExigirVerificacionDePersona(
            new OpcionesDeVerificacionDePersona(umbralDeRechazos: 3), motor, verificador, new OrigenFijo(new OrigenDeSolicitud(origen, "203.0.113.7")), new EventosNulos(), new RelojDeSistema());

        // La persona normal no ve ninguna verificación.
        Assert.Equal(ResultadoDeExigencia.NoExigida, await exigencia.Exigir(AccionProtegida.Registro, null));

        for (var i = 0; i < 4; i++)
        {
            await motor.RegistrarRechazoDeOrigen(origen, TimeSpan.FromMinutes(5), CancellationToken.None);
        }

        // El origen automatizado debe presentar el desafío, y sin él no se crea la cuenta.
        Assert.Equal(ResultadoDeExigencia.Requerida, await exigencia.Exigir(AccionProtegida.Registro, null));
        Assert.Equal(ResultadoDeExigencia.Verificada, await exigencia.Exigir(AccionProtegida.Registro, Token));
        Assert.Equal(ResultadoDeExigencia.Fallida, await exigencia.Exigir(AccionProtegida.Registro, Token));
    }

    private sealed class OrigenFijo(OrigenDeSolicitud origen) : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => origen;
    }

    private sealed class EventosNulos : IPublicadorDeEventosDeSeguridad
    {
        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class RelojDeSistema : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }
}
