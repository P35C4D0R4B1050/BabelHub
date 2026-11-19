using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Pagos;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El ingreso de Identidad: responde lo que la prueba le pone y anota lo que recibe (sin los valores secretos).</summary>
internal sealed class IngresoFalso : IIngreso
{
    public List<string> Llamadas { get; } = [];

    public Resultado<ResultadoDelIngreso> Resultado { get; set; } = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "credenciales-invalidas"));

    public Resultado<SecretoDelSegundoFactor> Secreto { get; set; } = Resultado<SecretoDelSegundoFactor>.Exito(new SecretoDelSegundoFactor(new ValorSecreto("JBSWY3DPEHPK3PXP"), "BabelHub", "encargada@babelhub.example"));

    public PasoPendienteDelIngreso? Paso { get; set; } = PasoPendienteDelIngreso.PresentarSegundoFactor;

    public Exception? Lanza { get; set; }

    public SolicitudDeIngreso? UltimaSolicitud { get; private set; }

    public string? UltimoCodigo { get; private set; }

    private Task<Resultado<ResultadoDelIngreso>> Responder(string llamada)
    {
        Llamadas.Add(llamada);
        return Lanza is null ? Task.FromResult(Resultado) : Task.FromException<Resultado<ResultadoDelIngreso>>(Lanza);
    }

    public Task<Resultado<ResultadoDelIngreso>> Ingresar(SolicitudDeIngreso solicitud, CancellationToken cancelacion = default)
    {
        UltimaSolicitud = solicitud;
        return Responder("ingresar");
    }

    public Task<Resultado<ResultadoDelIngreso>> PresentarSegundoFactor(ValorSecreto desafio, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        UltimoCodigo = codigo.Revelar();
        return Responder("presentar");
    }

    public Task<Resultado<SecretoDelSegundoFactor>> IniciarConfiguracionDelSegundoFactor(ValorSecreto desafio, CancellationToken cancelacion = default)
    {
        Llamadas.Add("iniciar-configuracion");
        return Task.FromResult(Secreto);
    }

    public Task<Resultado<ResultadoDelIngreso>> ConfirmarElSegundoFactor(ValorSecreto desafio, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        UltimoCodigo = codigo.Revelar();
        return Responder("confirmar");
    }

    public Task<Resultado<ResultadoDelIngreso>> CambiarLaContrasenaTemporal(ValorSecreto desafio, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default) => Responder("cambiar-contrasena");

    public Task<PasoPendienteDelIngreso?> PasoDelDesafio(ValorSecreto desafio, CancellationToken cancelacion = default) => Task.FromResult(Paso);

    public Task CancelarElDesafio(ValorSecreto desafio, CancellationToken cancelacion = default)
    {
        Llamadas.Add("cancelar");
        return Task.CompletedTask;
    }
}

internal sealed class DesafioSobreElIngresoFalso(IngresoFalso ingreso) : IDesafioDelIngreso
{
    public Task<PasoDelIngresoWeb> PasoPendiente(string valorDelDesafio, CancellationToken cancelacion = default) => Task.FromResult(ingreso.Paso switch
    {
        PasoPendienteDelIngreso.PresentarSegundoFactor => PasoDelIngresoWeb.PresentarSegundoFactor,
        PasoPendienteDelIngreso.ConfigurarSegundoFactor => PasoDelIngresoWeb.ConfigurarSegundoFactor,
        PasoPendienteDelIngreso.CambiarContrasenaTemporal => PasoDelIngresoWeb.ContrasenaTemporal,
        _ => PasoDelIngresoWeb.Ninguno,
    });

    public Task Cancelar(string valorDelDesafio, CancellationToken cancelacion = default) => ingreso.CancelarElDesafio(new ValorSecreto(valorDelDesafio), cancelacion);
}

internal sealed class RegistroFalso : IRegistroDeCuentas
{
    public List<string> Llamadas { get; } = [];

    public SolicitudDeAltaDeCuenta? UltimaAlta { get; private set; }

    public Resultado<RespuestaUniforme> Alta { get; set; } = Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());

    public Resultado<ResultadoDelIngreso> Verificacion { get; set; } = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "codigo-invalido"));

    public Task<Resultado<RespuestaUniforme>> Registrar(SolicitudDeAltaDeCuenta solicitud, CancellationToken cancelacion = default)
    {
        Llamadas.Add("registrar");
        UltimaAlta = solicitud;
        return Task.FromResult(Alta);
    }

    public Task<Resultado<ResultadoDelIngreso>> VerificarCorreo(string correo, ValorSecreto codigo, CancellationToken cancelacion = default)
    {
        Llamadas.Add("verificar");
        return Task.FromResult(Verificacion);
    }

    public Task<Resultado<RespuestaUniforme>> ReenviarCodigo(string correo, CancellationToken cancelacion = default)
    {
        Llamadas.Add("reenviar");
        return Task.FromResult(Alta);
    }
}

internal sealed class RecuperacionFalsa : IRecuperacionDeAcceso
{
    public List<string> Llamadas { get; } = [];

    public Resultado<RespuestaUniforme> Respuesta { get; set; } = Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme());

    public Task<Resultado<RespuestaUniforme>> Solicitar(string correo, CancellationToken cancelacion = default)
    {
        Llamadas.Add("solicitar");
        return Task.FromResult(Respuesta);
    }

    public Task<Resultado<RespuestaUniforme>> Confirmar(string correo, ValorSecreto codigo, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default)
    {
        Llamadas.Add("confirmar");
        return Task.FromResult(Respuesta);
    }
}

internal sealed class VerificacionFalsa : IExigenciaDeVerificacionDePersona
{
    public ResultadoDeExigencia Resultado { get; set; } = ResultadoDeExigencia.NoExigida;

    public string? UltimoToken { get; private set; }

    public Task<ResultadoDeExigencia> Exigir(AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default)
    {
        UltimoToken = tokenDeVerificacion;
        return Task.FromResult(Resultado);
    }
}

internal sealed class TextosFalsos : IAceptacionDeTextos
{
    public Task<Resultado<VersionParaAceptar>> VersionVigente(CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<VersionParaAceptar>.Exito(new VersionParaAceptar(
            "2026-11",
            [
                new TextoLegal(TipoDeTextoLegal.Terminos, "Términos y condiciones", "T-2026-11", new DateOnly(2026, 11, 1), false, "Resumen", []),
                new TextoLegal(TipoDeTextoLegal.Privacidad, "Política de privacidad", "P-2026-11", new DateOnly(2026, 11, 1), false, "Resumen", []),
            ])));

    public Task<ResultadoDeAceptacion> Aceptar(ActorVerificado actor, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeAceptacion.Aceptada);
}

/// <summary>La Presentación del ingreso, el registro y la recuperación en proceso (T28), con las vistas reales y los contratos de Identidad, la sesión del servidor y Carrito doblados.</summary>
internal sealed class AccesoApiHost : IAsyncDisposable
{
    private AnfitrionWeb _anfitrion = null!;
    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public IngresoFalso Ingreso { get; } = new();

    public RegistroFalso Registro { get; } = new();

    public RecuperacionFalsa Recuperacion { get; } = new();

    public VerificacionFalsa Verificacion { get; } = new();

    public List<SolicitudDeInicioDeSesion> Sesiones { get; } = [];

    public bool IniciarRechaza { get; set; }

    public List<string> LlamadasDeCarrito { get; } = [];

    public List<string> Revocaciones { get; } = [];

    public HostWeb Host { get; private set; }

    public static async Task<AccesoApiHost> Crear(HostWeb host = HostWeb.Tienda)
    {
        var anfitrion = new AccesoApiHost { Host = host };
        await anfitrion.Iniciar();
        return anfitrion;
    }

    private async Task Iniciar()
    {
        var actor = new ActorDelFlujo();
        var sesiones = DobleDeMetodos.Crear<ISesionesWeb>(out var doble);
        doble.Con(
            nameof(ISesionesWeb.Iniciar),
            argumentos =>
            {
                Sesiones.Add((SolicitudDeInicioDeSesion)argumentos[0]!);
                return Task.FromResult(IniciarRechaza
                    ? Resultado<SesionIniciada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido"))
                    : Resultado<SesionIniciada>.Exito(new SesionIniciada("valor-opaco-de-la-sesion-0123456789abcdef", "marca-nueva-de-dispositivo-0123456789abcdefghijk")));
            });
        var isesiones = DobleDeMetodos.Crear<ISesiones>(out var dobleSesiones);
        dobleSesiones.Con(nameof(ISesiones.Cerrar), _ => { Revocaciones.Add("cerrar"); return Task.FromResult(Resultado<RespuestaUniforme>.Exito(new RespuestaUniforme())); });
        var carrito = DobleDeMetodos.Crear<ICarritoDelCliente>(out var dobleCarrito);
        dobleCarrito.Con(
            nameof(ICarritoDelCliente.FusionarCarritoDeVisitante),
            _ =>
            {
                LlamadasDeCarrito.Add("fusionar");
                return Task.FromResult(Resultado<ResultadoDeFusionDeCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
            });
        _anfitrion = await AnfitrionWeb.Iniciar(
            servicios =>
            {
                servicios.AddSingleton<IActorActual>(actor);
                servicios.AddSingleton<IEstablecedorDeActor>(actor);
                servicios.AddSingleton<IIngreso>(Ingreso);
                servicios.AddSingleton<IDesafioDelIngreso>(new DesafioSobreElIngresoFalso(Ingreso));
                servicios.AddSingleton<IRegistroDeCuentas>(Registro);
                servicios.AddSingleton<IRecuperacionDeAcceso>(Recuperacion);
                servicios.AddSingleton<IExigenciaDeVerificacionDePersona>(Verificacion);
                servicios.AddSingleton<IAceptacionDeTextos>(new TextosFalsos());
                servicios.AddSingleton(sesiones);
                servicios.AddSingleton(isesiones);
                servicios.AddSingleton(carrito);
                servicios.AddSingleton(new OpcionesDelHostWeb(Host, []));
                servicios.AddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
                servicios.AgregarIngresoWeb();
                servicios.AgregarProteccionContraFalsificacion();
            },
            despuesDelEnrutado: app => app.MapGet("/prueba/token", (HttpContext contexto, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(contexto).RequestToken)));
        Cliente = _anfitrion.Cliente;
    }

    public async ValueTask DisposeAsync()
    {
        if (_anfitrion is not null)
        {
            await _anfitrion.DisposeAsync();
        }
    }

    public HttpRequestMessage Pedir(string ruta, string? cookies = null)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        if (cookies is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("Cookie", cookies);
        }

        return solicitud;
    }

    public async Task<HttpResponseMessage> Enviar(string ruta, IEnumerable<KeyValuePair<string, string>> campos, string? cookies = null, bool conToken = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        var galletas = new List<string>();
        if (conToken)
        {
            if (_token is null)
            {
                using var respuestaDelToken = await Cliente.GetAsync("/prueba/token");
                var cookie = respuestaDelToken.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
                _token = (cookie, await respuestaDelToken.Content.ReadAsStringAsync());
            }

            galletas.Add(_token.Value.Cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, _token.Value.Token);
        }

        if (cookies is not null)
        {
            galletas.Add(cookies);
        }

        if (galletas.Count > 0)
        {
            solicitud.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", galletas));
        }

        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Content = new FormUrlEncodedContent(campos);
        return await Cliente.SendAsync(solicitud);
    }
}
