using System.Net;
using System.Text;
using BabelHub.Ecommerce.Api.Pagos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T29: la entrada de avisos de la pasarela (PAG-19, AC-PAG-16): deshabilitada sin un esquema de firma verificable, tope del cuerpo, marca y firma obligatorias con el mismo 401 uniforme, un duplicado es 200, un fallo al aplicar es 503 y el actor es la integración;
/// las acciones de reembolsos pendientes de la administración (PAG-10, AC-PAG-08); y el anfitrión de los trabajos con el actor «sistema» y el servicio solo en modo normal.
/// </summary>
public sealed class ApiDePagosAvisosReembolsosYTrabajosTests
{
    private sealed class AvisosFalso(IActorActual actor) : IAvisosDeLaPasarela
    {
        public List<(string Cuerpo, string Marca, string Firma)> Recibidos { get; } = [];

        public List<ActorActual?> Actores { get; } = [];

        public Resultado<bool> Respuesta { get; set; } = Resultado<bool>.Exito(true);

        public Task<Resultado<bool>> Recibir(AvisoDeLaPasarelaEntrada entrada, CancellationToken cancelacion = default)
        {
            Recibidos.Add((Encoding.UTF8.GetString(entrada.Cuerpo.Span), entrada.MarcaDeTiempo, entrada.Firma));
            Actores.Add(actor.Actual);
            return Task.FromResult(Respuesta);
        }
    }

    private static async Task<(WebApplication App, HttpClient Cliente, AvisosFalso Avisos, ActorDelFlujo Actor)> IniciarAvisos(bool habilitada)
    {
        var actor = new ActorDelFlujo();
        var avisos = new AvisosFalso(actor);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<IAvisosDeLaPasarela>(avisos);
        builder.Services.AddSingleton<IRegistradorDeDenegacionesHttp>(new Carrito.DenegacionesEspia());
        var app = builder.Build();
        app.UsarManejoDeErrores();
        app.UseRouting();
        app.MapearAvisosDeLaPasarela(habilitada);
        await app.StartAsync();
        var cliente = app.GetTestClient();
        cliente.BaseAddress = new Uri("https://localhost/");
        return (app, cliente, avisos, actor);
    }

    private static HttpRequestMessage Aviso(string cuerpo, string? marca = "1763550000", string? firma = "firma-de-prueba")
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post, AvisosDeLaPasarelaEnLaApi.Ruta) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        if (marca is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(AvisosDeLaPasarelaEnLaApi.CabeceraDeLaMarca, marca);
        }

        if (firma is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(AvisosDeLaPasarelaEnLaApi.CabeceraDeLaFirma, firma);
        }

        return solicitud;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aviso_con_marca_y_firma_llega_a_Pagos_como_la_integracion_y_responde_200_y_el_actor_se_limpia()
    {
        var (app, cliente, avisos, actor) = await IniciarAvisos(habilitada: true);
        await using var _ = app;
        using var respuesta = await cliente.SendAsync(Aviso("{\"id\":\"evt_1\"}"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(("{\"id\":\"evt_1\"}", "1763550000", "firma-de-prueba"), Assert.Single(avisos.Recibidos));
        Assert.Equal(TipoDeActor.Integracion, avisos.Actores.Single()!.Tipo);
        Assert.Null(actor.Actual);
    }

    [Fact]
    public async Task Sin_un_esquema_de_firma_verificable_la_entrada_no_existe()
    {
        var (app, cliente, avisos, _) = await IniciarAvisos(habilitada: false);
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Aviso("{}"));

        Assert.True(respuesta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Empty(avisos.Recibidos);
    }

    [Theory]
    [InlineData(null, "firma")]
    [InlineData("1763550000", null)]
    [InlineData("", "firma")]
    public async Task Sin_marca_o_sin_firma_es_el_mismo_401_uniforme_sin_llamar_a_Pagos(string? marca, string? firma)
    {
        var (app, cliente, avisos, _) = await IniciarAvisos(habilitada: true);
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Aviso("{}", marca, firma));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Empty(avisos.Recibidos);
    }

    [Fact]
    public async Task Un_cuerpo_mas_grande_que_el_tope_se_rechaza_con_la_cabecera_y_tambien_por_trozos()
    {
        var (app, cliente, avisos, _) = await IniciarAvisos(habilitada: true);
        await using var _ = app;
        var grande = new string('x', AvisosDeLaPasarelaEnLaApi.TopeDelCuerpo + 1);

        using var conLongitud = await cliente.SendAsync(Aviso(grande));
        using var porTrozos = new HttpRequestMessage(HttpMethod.Post, AvisosDeLaPasarelaEnLaApi.Ruta) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(grande))) };
        porTrozos.Headers.TransferEncodingChunked = true;
        porTrozos.Headers.TryAddWithoutValidation(AvisosDeLaPasarelaEnLaApi.CabeceraDeLaMarca, "1763550000");
        porTrozos.Headers.TryAddWithoutValidation(AvisosDeLaPasarelaEnLaApi.CabeceraDeLaFirma, "firma");
        using var respuestaPorTrozos = await cliente.SendAsync(porTrozos);

        Assert.Equal(HttpStatusCode.BadRequest, conLongitud.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, respuestaPorTrozos.StatusCode);
        Assert.Empty(avisos.Recibidos);
    }

    [Theory]
    [InlineData(CategoriaDeError.NoAutenticado, "no-autenticado", HttpStatusCode.Unauthorized, "ECO-NO-AUTENTICADO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", HttpStatusCode.ServiceUnavailable, "ECO-SERVICIO-NO-DISPONIBLE")]
    public async Task Los_fallos_de_Pagos_se_traducen_sin_repetir_el_cuerpo_ni_la_firma(CategoriaDeError categoria, string clave, HttpStatusCode estado, string codigo)
    {
        var (app, cliente, avisos, actor) = await IniciarAvisos(habilitada: true);
        await using var _ = app;
        avisos.Respuesta = Resultado<bool>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await cliente.SendAsync(Aviso("{\"secreto\":\"cuerpo-reservado\"}", firma: "firma-reservada"));

        Assert.Equal(estado, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains(codigo, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("cuerpo-reservado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("firma-reservada", texto, StringComparison.Ordinal);
        Assert.Null(actor.Actual);
    }

    // ---------- Reembolsos de la administración ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task La_lista_de_reembolsos_pendientes_muestra_origen_monto_y_motivo_sin_datos_de_pago()
    {
        await using var host = await PagosApiHost.Crear();
        var id = Guid.NewGuid();
        host.Reembolsos.Pagina = Resultado<PaginaDeReembolsos>.Exito(new PaginaDeReembolsos([new ReembolsoPendienteVista(id, OrigenDelReembolso.Compensacion, 140m, EstadoDelReembolso.Pendiente, MotivoPendienteDelReembolso.NoSoportado, DateTimeOffset.UtcNow, 2)], 1, 20, 1));

        using var respuesta = await host.Cliente.SendAsync(host.Pedir("/administracion/reembolsos", html: true));

        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Compensación de un cobro", html, StringComparison.Ordinal);
        Assert.Contains("S/", html, StringComparison.Ordinal);
        Assert.Contains("no reembolsa con ese medio", html, StringComparison.Ordinal);
        Assert.Contains(id.ToString("D"), html, StringComparison.Ordinal);
        Assert.Equal(["listar:1:20"], host.Reembolsos.Llamadas);
    }

    [Fact]
    public async Task Reintentar_un_reembolso_llama_a_Pagos_y_vuelve_a_la_lista_y_un_rol_sin_permiso_recibe_403()
    {
        await using var host = await PagosApiHost.Crear();
        var id = Guid.NewGuid();

        using var respuesta = await host.Enviar("/administracion/reembolsos/reintentar", [new("reembolsoId", id.ToString("D"))]);
        host.Reembolsos.Reintento = Resultado<EstadoDelReembolso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido"));
        using var prohibido = await host.Enviar("/administracion/reembolsos/reintentar", [new("reembolsoId", id.ToString("D"))]);
        using var invalido = await host.Enviar("/administracion/reembolsos/reintentar", [new("reembolsoId", "xyz")]);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/administracion/reembolsos", respuesta.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Forbidden, prohibido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal([$"reintentar:{id:D}", $"reintentar:{id:D}"], host.Reembolsos.Llamadas);
    }

    // ---------- Trabajos de fondo ----------

    [Fact]
    public async Task El_alcance_de_los_trabajos_fija_el_actor_sistema_en_la_corrida_y_lo_limpia_aunque_falle()
    {
        var actor = new ActorDelFlujo();
        var alcance = TrabajosDePagosEnLaApi.AlcanceDeSistema(actor);
        var visto = new List<ActorActual?>();

        await alcance(_ => { visto.Add(actor.Actual); return Task.CompletedTask; }, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => alcance(_ => { visto.Add(actor.Actual); throw new InvalidOperationException("falló"); }, CancellationToken.None));

        Assert.All(visto, a => Assert.Equal(TipoDeActor.Sistema, a!.Tipo));
        Assert.Equal(2, visto.Count);
        Assert.Null(actor.Actual);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void El_servicio_hospedado_solo_se_registra_en_modo_normal(bool soloEnModoNormal, int hospedados)
    {
        var servicios = new ServiceCollection();
        servicios.AgregarTrabajosDePagosEnLaApi(soloEnModoNormal);

        Assert.Equal(hospedados, servicios.Count(s => s.ServiceType == typeof(IHostedService)));
    }
}

/// <summary>T29 (revisión): la paginación del listado de reembolsos y la consulta de los GET del pago son estrictas.</summary>
public sealed class ConsultasEstrictasDelPagoTests
{
    [Theory]
    [InlineData("/administracion/reembolsos?pagina=abc")]
    [InlineData("/administracion/reembolsos?tamano=-1")]
    [InlineData("/administracion/reembolsos?pagina=1&otro=2")]
    [InlineData("/administracion/reembolsos?cuentaId=1")]
    public async Task Una_paginacion_o_una_consulta_invalida_es_validacion_sin_llamar_a_Pagos(string ruta)
    {
        await using var host = await PagosApiHost.Crear();

        using var respuesta = await host.Cliente.SendAsync(host.Pedir(ruta));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(host.Reembolsos.Llamadas);
    }

    [Fact]
    public async Task La_paginacion_valida_llega_a_Pagos_y_la_ausente_usa_los_valores_por_omision()
    {
        await using var host = await PagosApiHost.Crear();

        using var conValores = await host.Cliente.SendAsync(host.Pedir("/administracion/reembolsos?pagina=2&tamano=5", html: true));
        using var sinValores = await host.Cliente.SendAsync(host.Pedir("/administracion/reembolsos", html: true));

        Assert.Equal(["listar:2:5", "listar:1:20"], host.Reembolsos.Llamadas);
    }

    [Theory]
    [InlineData("/checkout/pago?cuentaId=1")]
    [InlineData("/checkout/pago/estado?x=1")]
    public async Task Los_GET_del_pago_solo_admiten_el_pedidoId_en_la_consulta(string ruta)
    {
        await using var host = await PagosApiHost.Crear();

        using var respuesta = await host.Cliente.SendAsync(host.Pedir(ruta));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(host.Pagos.Llamadas);
    }
}
