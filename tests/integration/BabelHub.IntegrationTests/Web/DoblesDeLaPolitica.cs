using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El estado de aceptación de los textos que fija la prueba (y que puede fallar o tardar).</summary>
internal sealed class EstadoDeCuentaDePrueba : IEstadoDeCuentaDeLaWeb
{
    public EstadoDeAceptacionDeTextos Estado { get; set; } = EstadoDeAceptacionDeTextos.Pendientes;

    public Func<CancellationToken, Task<EstadoDeAceptacionDeTextos>>? Comportamiento { get; set; }

    private int _consultas;

    public int Consultas => Volatile.Read(ref _consultas);

    public Task<EstadoDeAceptacionDeTextos> Aceptacion(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _consultas);
        return Comportamiento is { } c ? c(cancelacion) : Task.FromResult(Estado);
    }
}

/// <summary>Identidad que registra la aceptación solo si la versión enviada es la vigente (R12).</summary>
internal sealed class AceptacionDePrueba(EstadoDeCuentaDePrueba estado) : IAceptacionDeTextosEnIdentidad
{
    public string Vigente { get; set; } = "terminos:v1|privacidad:v1|reembolso:v1";

    public bool Caida { get; set; }

    public List<(Guid Usuario, string Version)> Aceptadas { get; } = [];

    public Task<ResultadoDeAceptacion> Aceptar(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromResult(ResultadoDeAceptacion.NoDisponible);
        }

        if (versionMostrada != Vigente)
        {
            return Task.FromResult(ResultadoDeAceptacion.VersionCambiada);
        }

        Aceptadas.Add((usuarioId, versionMostrada));
        estado.Estado = EstadoDeAceptacionDeTextos.Aceptados;
        return Task.FromResult(ResultadoDeAceptacion.Aceptada);
    }
}

/// <summary>Un almacén que solo guarda el retorno (lo único que la política le pide); lo demás no se usa en estas pruebas.</summary>
internal sealed class AlmacenSoloDeRetornos : IAlmacenDeSesionesWeb
{
    public Dictionary<string, string> Retornos { get; } = [];

    public Task GuardarRetorno(HashDeSesion id, string ruta, CancellationToken cancelacion = default)
    {
        Retornos[id.Valor] = ruta;
        return Task.CompletedTask;
    }

    public Task<string?> TomarRetorno(HashDeSesion id, CancellationToken cancelacion = default) =>
        Task.FromResult(Retornos.Remove(id.Valor, out var ruta) ? ruta : null);

    public Task Insertar(SesionGuardada sesion, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<SesionGuardada?> Cargar(HashDeSesion id, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<SesionGuardada?> CargarParaRenovar(HashDeSesion id, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Guardar(SesionGuardada sesion, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task ActualizarActividad(HashDeSesion id, DateTimeOffset ultimaActividad, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<SesionGuardada>> CargarAbiertasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<SesionGuardada>> CargarParaPurgar(HostDeSesion host, FaseDeLaPurga fase, DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Borrar(HashDeSesion id, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>Rutas de prueba de la política de textos pendientes: cada una declara lo que declararía una pantalla real.</summary>
public sealed class ControladorDePoliticaDePrueba : Controller
{
    private IActionResult Estado() => Json(new { textosPendientes = HttpContext.Items[ContextoDePagina.ClaveTextosPendientes] is true });

    [HttpGet("/prueba/politica/cuenta")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    public IActionResult Cuenta() => Estado();

    [HttpGet("/prueba/politica/pedidos")]
    [PermitidaConTextosPendientes(CapacidadWeb.Pedidos)]
    public IActionResult Pedidos() => Estado();

    [HttpGet("/prueba/politica/libro")]
    [PermitidaConTextosPendientes(CapacidadWeb.LibroDeReclamaciones)]
    public IActionResult Libro() => Estado();

    [HttpPost("/prueba/politica/hoja")]
    [PermitidaConTextosPendientes(CapacidadWeb.EnviarHoja)]
    public IActionResult Hoja() => Estado();

    [HttpGet("/prueba/politica/catalogo")]
    public IActionResult Catalogo() => Estado();

    [HttpGet("/prueba/politica/catalogo/{id:int}")]
    public IActionResult CatalogoDe(int id) => Estado();

    [HttpGet("/prueba/politica/sin-retorno")]
    [SinRetorno]
    public IActionResult SinRetornoDePrueba() => Estado();

    [HttpPost("/prueba/politica/pago")]
    public IActionResult Pago() => Estado();

    [HttpPost("/prueba/politica/datos")]
    public IActionResult Datos() => Estado();
}

[Area("Sucursal")]
public sealed class ControladorDePoliticaDeSucursalDePrueba : Controller
{
    [HttpGet("/prueba/politica/sucursal/{sucursalId}")]
    [PermitidaConTextosPendientes(CapacidadWeb.Cuenta)]
    [ParametroDeSucursal("sucursalId")]
    public IActionResult Cuenta(string sucursalId) => Json(new { textosPendientes = HttpContext.Items[ContextoDePagina.ClaveTextosPendientes] is true });

    [HttpGet("/prueba/politica/sucursal-sin-declarar/{sucursalId}")]
    public IActionResult SinDeclarar(string sucursalId) => Content("x");
}

internal static class AnfitrionConPolitica
{
    /// <summary>Un anfitrión con la sesión web, los filtros y la política de textos pendientes montada entre la sesión y los endpoints.</summary>
    public static async Task<(AnfitrionWeb Anfitrion, SesionesWebDePrueba Sesiones, EstadoDeCuentaDePrueba Estado, AceptacionDePrueba Aceptacion, DenegacionesDePrueba Denegaciones, AlmacenSoloDeRetornos Almacen, RegistroDeSeguridadEspiaSeguro Seguridad)> Iniciar(
        HostWeb host = HostWeb.Tienda,
        SesionesWebDePrueba? sesiones = null,
        Action<IServiceCollection>? servicios = null)
    {
        sesiones ??= new SesionesWebDePrueba().ConSesionValida();
        var estado = new EstadoDeCuentaDePrueba();
        var aceptacion = new AceptacionDePrueba(estado);
        var denegaciones = new DenegacionesDePrueba();
        var almacen = new AlmacenSoloDeRetornos();
        var seguridad = new RegistroDeSeguridadEspiaSeguro();
        var flujo = new ActorDelFlujo();
        sesiones.LeerActor = () => flujo.Actual;
        var anfitrion = await AnfitrionConSesion.IniciarCon(
            sesiones,
            flujo,
            host,
            servicios: s =>
            {
                s.AgregarPoliticaDeTextosPendientes();
                s.AddSingleton<IEstadoDeCuentaDeLaWeb>(estado);
                s.AddSingleton<IAceptacionDeTextosEnIdentidad>(aceptacion);
                s.AddSingleton<BabelHub.Ecommerce.Application.Auditoria.IDenegacionDeAcceso>(denegaciones);
                s.AddSingleton<IAlmacenDeSesionesWeb>(almacen);
                s.AddSingleton<IRegistroDeSeguridadDeLaWeb>(seguridad);
                s.AddSingleton<BabelHub.Ecommerce.Application.Plataforma.IPublicadorDeAlertasTecnicas>(new AlertasEspiaSeguras());
                s.AddSingleton<IReloj>(new RelojDePrueba(EntornoDeSesiones.Inicio));
                servicios?.Invoke(s);
            },
            despuesDelHandler: app => app.UsarPoliticaDeTextosPendientes());
        return (anfitrion, sesiones, estado, aceptacion, denegaciones, almacen, seguridad);
    }
}
