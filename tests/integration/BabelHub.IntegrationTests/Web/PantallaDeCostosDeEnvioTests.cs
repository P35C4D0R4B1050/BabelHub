using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Carrito;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T48: la pantalla «Costo de envío por sucursal» sobre el anfitrión web con las vistas reales (CAR-46, CAR-48): la lista con un formulario por sucursal, el guardado con su token antifalsificación y su confirmación, la entrada estricta y los
/// rechazos del módulo pintados en pantalla. Quién puede y qué importe vale lo decide la Aplicación (sus pruebas son unitarias y de PostgreSQL).
/// </summary>
public sealed class PantallaDeCostosDeEnvioTests : IAsyncLifetime
{
    private const string Ruta = "/administracion/costos-de-envio";

    private readonly EnviosFalsos _envios = new();
    private AnfitrionWeb _anfitrion = null!;
    private (string Cookie, string Token)? _token;

    private sealed class ActorFijo : IActorActual
    {
        public ActorActual? Actual { get; } = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central", null);
    }

    private sealed class Lista : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("AYA", "Ayacucho")], true));
    }

    private sealed class EnviosFalsos : IAdministracionDeEnvios
    {
        public List<CambiarCostoDeEnvioEntrada> Cambios { get; } = [];

        public Resultado<CostosDeEnvio> Costos { get; set; } = Resultado<CostosDeEnvio>.Exito(new CostosDeEnvio(
            [new("AYA", "Ayacucho", 18.5m, true), new("CUS", "Cusco", 30m, false)],
            30m));

        public ErrorDeAplicacion? Rechazo { get; set; }

        public Task<Resultado<CostosDeEnvio>> Listar(CancellationToken cancelacion = default) => Task.FromResult(Costos);

        public Task<Resultado<CostoDeEnvioDeSucursal>> Cambiar(CambiarCostoDeEnvioEntrada entrada, CancellationToken cancelacion = default)
        {
            Cambios.Add(entrada);
            return Task.FromResult(Rechazo is { } error
                ? Resultado<CostoDeEnvioDeSucursal>.Fallo(error)
                : Resultado<CostoDeEnvioDeSucursal>.Exito(new CostoDeEnvioDeSucursal(entrada.CodigoDeSucursal, "Ayacucho", entrada.Importe, true)));
        }
    }

    public async Task InitializeAsync() => _anfitrion = await AnfitrionWeb.Iniciar(
        servicios =>
        {
            servicios.AddSingleton<IAdministracionDeEnvios>(_envios);
            servicios.AddSingleton<IActorActual>(new ActorFijo());
            servicios.AddSingleton<IListaDeSucursales>(new Lista());
            servicios.AddSingleton<IRegistradorDeDenegacionesHttp>(new DenegacionesEspia());
            servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Gestion, []));
            servicios.AgregarProteccionContraFalsificacion();
        },
        despuesDelEnrutado: app => app.MapGet("/prueba/token", (HttpContext contexto, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(contexto).RequestToken)));

    public async Task DisposeAsync() => await _anfitrion.DisposeAsync();

    private async Task<(HttpStatusCode Estado, string Html)> Ver(string ruta = Ruta)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        using var respuesta = await _anfitrion.Cliente.SendAsync(solicitud);
        return (respuesta.StatusCode, WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
    }

    private async Task<HttpResponseMessage> Guardar(IEnumerable<KeyValuePair<string, string>> campos, bool conToken = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, Ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        if (conToken)
        {
            if (_token is null)
            {
                using var respuestaDelToken = await _anfitrion.Cliente.GetAsync("/prueba/token");
                var cookie = respuestaDelToken.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
                _token = (cookie, await respuestaDelToken.Content.ReadAsStringAsync());
            }

            solicitud.Headers.TryAddWithoutValidation("Cookie", _token.Value.Cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, _token.Value.Token);
        }

        solicitud.Content = new FormUrlEncodedContent(campos);
        return await _anfitrion.Cliente.SendAsync(solicitud);
    }

    private static KeyValuePair<string, string>[] Campos(string sucursal, string importe) => [new("sucursal", sucursal), new("importe", importe)];

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Cada_sucursal_muestra_su_costo_y_si_es_propio_con_un_formulario_etiquetado_y_su_token()
    {
        var (estado, html) = await Ver();

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Costo de envío por sucursal", html, StringComparison.Ordinal);
        Assert.Matches(
            $@"Ayacucho</th>[\s\S]*?{Regex.Escape(FormatoPeruano.Soles(18.5m))}[\s\S]*?>Propio<[\s\S]*?Cusco</th>[\s\S]*?{Regex.Escape(FormatoPeruano.Soles(30m))}[\s\S]*?>Predeterminado<",
            html);
        Assert.Contains("Costo de envío de Ayacucho en soles", html, StringComparison.Ordinal);
        Assert.Contains("name=\"importe\" value=\"18.50\" min=\"0\" max=\"500\" step=\"0.01\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"sucursal\" value=\"CUS\"", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(html, "action=\"/administracion/costos-de-envio\"").Count);
        Assert.True(Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count >= 2);
        Assert.DoesNotContain("Guardamos el costo", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Guardar_llama_al_modulo_con_el_importe_y_vuelve_a_la_lista_con_la_confirmacion()
    {
        using var respuesta = await Guardar(Campos("AYA", "18.50"));

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal(Ruta + "?guardada=AYA", respuesta.Headers.Location?.OriginalString);
        Assert.Equal([new CambiarCostoDeEnvioEntrada("AYA", 18.50m)], _envios.Cambios);

        var (_, html) = await Ver(Ruta + "?guardada=AYA");
        Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("Guardamos el costo de Ayacucho", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Guardamos el costo", (await Ver(Ruta + "?guardada=no-existe")).Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("18,50")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData("1234567890123")]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Un_importe_que_no_es_un_numero_se_rechaza_sin_llamar_al_modulo(string importe)
    {
        using var respuesta = await Guardar(Campos("AYA", importe));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_envios.Cambios);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Sin_token_o_con_un_campo_de_mas_no_se_cambia_nada()
    {
        using var sinToken = await Guardar(Campos("AYA", "10"), conToken: false);
        using var deMas = await Guardar([.. Campos("AYA", "10"), new("cuentaId", Guid.NewGuid().ToString("D"))]);

        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deMas.StatusCode);
        Assert.Empty(_envios.Cambios);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Un_rol_sin_permiso_recibe_403_en_pantalla_al_ver_y_al_guardar()
    {
        var prohibido = new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido");
        _envios.Costos = Resultado<CostosDeEnvio>.Fallo(prohibido);
        _envios.Rechazo = prohibido;

        var (estado, html) = await Ver();
        using var guardado = await Guardar(Campos("AYA", "10"));

        Assert.Equal(HttpStatusCode.Forbidden, estado);
        Assert.DoesNotContain("name=\"importe\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, guardado.StatusCode);
    }

    [Fact]
    public async Task Una_consulta_desconocida_es_validacion()
    {
        var (estado, _) = await Ver(Ruta + "?cuentaId=1");

        Assert.Equal(HttpStatusCode.BadRequest, estado);
    }
}
