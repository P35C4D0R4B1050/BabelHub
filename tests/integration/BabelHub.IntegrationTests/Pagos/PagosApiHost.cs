using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.IntegrationTests.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Carrito;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>Un <c>IPagos</c> que anota cada llamada y responde lo que la prueba le pida: sirve para probar la Presentación sin la Aplicación.</summary>
public sealed class PagosFalso : IPagos
{
    public static readonly Guid Pedido = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    public List<string> Llamadas { get; } = [];

    public List<object> Entradas { get; } = [];

    public Resultado<ClaveDeIntentoEmitida> Clave { get; set; } = Resultado<ClaveDeIntentoEmitida>.Exito(new ClaveDeIntentoEmitida("clave.firmada", DateTimeOffset.UtcNow.AddMinutes(30), 140m, DateTimeOffset.UtcNow.AddMinutes(15), null));

    public Resultado<ResultadoDelCobroDelCliente> Cobro { get; set; } = Resultado<ResultadoDelCobroDelCliente>.Exito(new ResultadoDelCobroDelCliente(EstadoDelCobro.Aprobado, null, Guid.NewGuid()));

    public Resultado<EstadoDelPagoVista> EstadoDelPago { get; set; } = Resultado<EstadoDelPagoVista>.Exito(new EstadoDelPagoVista(EstadoDelPagoDeLaCompra.ListoParaPagar, DateTimeOffset.UtcNow.AddMinutes(15), null, null, true));

    public Task<Resultado<ClaveDeIntentoEmitida>> EmitirClaveDeIntento(EmitirClaveDeIntentoEntrada entrada, CancellationToken cancelacion = default) => Anotar("clave", entrada, Clave);

    public Task<Resultado<ResultadoDelCobroDelCliente>> Cobrar(CobrarEntrada entrada, CancellationToken cancelacion = default) => Anotar("cobrar", entrada, Cobro);

    public Task<Resultado<EstadoDelPagoVista>> ObtenerEstado(Guid pedidoId, CancellationToken cancelacion = default) => Anotar("estado", pedidoId, EstadoDelPago);

    private Task<Resultado<T>> Anotar<T>(string llamada, object entrada, Resultado<T> respuesta)
    {
        Llamadas.Add(llamada);
        Entradas.Add(entrada);
        return Task.FromResult(respuesta);
    }
}

/// <summary>El checkout vivo que ve Pagos: solo <c>ObtenerParaPagar</c> responde.</summary>
public sealed class CheckoutVivoFalso : ICheckoutDelCliente
{
    public Resultado<CheckoutVivoParaPagar> Vivo { get; set; } = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(PagosFalso.Pedido, EstadoVisibleDelCheckout.EnPago));

    public int Consultas { get; private set; }

    public Task<Resultado<CheckoutVivoParaPagar>> ObtenerParaPagar(CancellationToken cancelacion = default)
    {
        Consultas++;
        return Task.FromResult(Vivo);
    }

    public Task<Resultado<VistaDelCheckout>> Iniciar(IniciarCheckoutEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCheckout>> Obtener(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCheckout>> ElegirEntrega(ElegirEntregaEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCheckout>> RegistrarAceptaciones(RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ResultadoDePasarAlPago>> PasarAlPago(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCarrito>> VolverAlCarrito(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCarrito>> Reiniciar(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>La Presentación del pago en proceso (T28): MVC con solo el <c>PagoController</c>, el manejo de errores, el filtro antifalsificación y los contratos de Pagos y del checkout doblados.</summary>
internal sealed class PagosApiHost : IAsyncDisposable
{
    public static readonly Guid UsuarioPorOmision = Guid.Parse("c0000000-0000-0000-0000-000000000001");


    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public PagosFalso Pagos { get; } = new();

    public CheckoutVivoFalso Checkout { get; } = new();

    public ReembolsosFalso Reembolsos { get; } = new();

    public DenegacionesEspia Denegaciones { get; } = new();

    public static async Task<PagosApiHost> Crear()
    {
        var host = new PagosApiHost();
        await host.Iniciar();
        return host;
    }

    private AnfitrionWeb _anfitrion = null!;

    private async Task Iniciar()
    {
        var actor = new ActorDelFlujo();
        _anfitrion = await AnfitrionWeb.Iniciar(
            servicios =>
            {
                servicios.AddSingleton<IActorActual>(actor);
                servicios.AddSingleton<IEstablecedorDeActor>(actor);
                servicios.AddSingleton<IPagos>(Pagos);
                servicios.AddSingleton<IReembolsos>(Reembolsos);
                servicios.AddSingleton<ICheckoutDelCliente>(Checkout);
                servicios.AddSingleton<IRegistradorDeDenegacionesHttp>(Denegaciones);
                servicios.AddSingleton<IListaDeSucursales>(new ListaVacia());
                servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
                servicios.AgregarProteccionContraFalsificacion();
            },
            antesDeLasRutas: app => app.Use(async (contexto, siguiente) =>
            {
                if (Guid.TryParse(contexto.Request.Headers["X-Prueba-Usuario"], out var usuario))
                {
                    actor.Establecer(ActorActual.DeUsuario(usuario, RolesDeUsuario.Cliente, null));
                }
                else
                {
                    actor.Limpiar();
                }

                await siguiente();
            }),
            despuesDelEnrutado: app => app.MapGet("/prueba/token", (HttpContext contexto, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(contexto).RequestToken)));
        Cliente = _anfitrion.Cliente;
    }

    private sealed class ListaVacia : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([], true));
    }

    public async ValueTask DisposeAsync()
    {
        if (_anfitrion is not null)
        {
            await _anfitrion.DisposeAsync();
        }
    }

    public HttpRequestMessage Pedir(string ruta, bool conSesion = true, bool htmx = false, bool html = false)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (conSesion)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", UsuarioPorOmision.ToString("D"));
        }

        if (htmx)
        {
            solicitud.Headers.TryAddWithoutValidation("HX-Request", "true");
        }

        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        return solicitud;
    }

    public async Task<HttpResponseMessage> Enviar(string ruta, IEnumerable<KeyValuePair<string, string>>? campos = null, bool conSesion = true, bool conToken = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        if (conSesion)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", UsuarioPorOmision.ToString("D"));
        }

        if (conToken)
        {
            if (_token is null)
            {
                using var respuestaDelToken = await Cliente.GetAsync("/prueba/token");
                var cookie = respuestaDelToken.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
                _token = (cookie, await respuestaDelToken.Content.ReadAsStringAsync());
            }

            solicitud.Headers.TryAddWithoutValidation("Cookie", _token.Value.Cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, _token.Value.Token);
        }

        solicitud.Content = new FormUrlEncodedContent(campos ?? []);
        return await Cliente.SendAsync(solicitud);
    }

    public static async Task<JsonElement> Leer(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private sealed class PartesDeTipos(params Type[] tipos) : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "controladores-del-pago";

        public IEnumerable<TypeInfo> Types => tipos.Select(t => t.GetTypeInfo());
    }
}

/// <summary>Un <c>IReembolsos</c> que anota la llamada y responde lo que la prueba le pida.</summary>
public sealed class ReembolsosFalso : IReembolsos
{
    public List<string> Llamadas { get; } = [];

    public Resultado<PaginaDeReembolsos> Pagina { get; set; } = Resultado<PaginaDeReembolsos>.Exito(new PaginaDeReembolsos([], 1, 20, 0));

    public Resultado<EstadoDelReembolso> Reintento { get; set; } = Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Reembolsado);

    public Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"listar:{pagina}:{tamano}");
        return Task.FromResult(Pagina);
    }

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"reintentar:{reembolsoId:D}");
        return Task.FromResult(Reintento);
    }

    public Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default) => throw new NotSupportedException();
}
