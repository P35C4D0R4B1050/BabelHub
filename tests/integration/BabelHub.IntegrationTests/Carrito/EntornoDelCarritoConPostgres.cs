using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Carrito;

// Los dobles de las integraciones de Carrito con PostgreSQL real (T30 a T32): configurables, sin simular nada de lo que Carrito decide.

public sealed class RelojDelCarrito : IReloj
{
    private long _ticks = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero).UtcTicks;

    public DateTimeOffset Ahora => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    public void Adelantar(TimeSpan tiempo) => Interlocked.Add(ref _ticks, tiempo.Ticks);
}

public sealed class CatalogoDelCarrito : IConsultaDeCatalogo
{
    private readonly Dictionary<Guid, EdicionConsultada> _ediciones = [];

    public bool Caido { get; set; }

    public Guid Poner(FormatoDeEdicion formato, decimal precio, string titulo = "Cien años de soledad", EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa)
    {
        var id = Guid.NewGuid();
        _ediciones[id] = new EdicionConsultada(id, Guid.NewGuid(), titulo, formato, "9780307474728", precio, estado, null);
        return id;
    }

    public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        if (Caido)
        {
            return Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "catalogo-no-disponible")));
        }

        IReadOnlyList<EdicionConsultada> halladas;
        lock (_ediciones)
        {
            halladas = [.. edicionIds.Where(_ediciones.ContainsKey).Select(id => _ediciones[id])];
        }

        return Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Exito(halladas));
    }

    public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

public sealed class DisponibilidadDelCarrito : IDisponibilidadDeEdiciones
{
    private readonly Dictionary<Guid, int> _vendible = [];

    public void Vendible(Guid edicionId, int vendible) => _vendible[edicionId] = vendible;

    public Task<Resultado<IReadOnlyList<DisponibilidadDeEdicion>>> Consultar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        IReadOnlyList<DisponibilidadDeEdicion> respuesta =
        [
            .. edicionIds.Select(id => _vendible.TryGetValue(id, out var v)
                ? new DisponibilidadDeEdicion(id, v > 0 ? EstadoDeDisponibilidad.Disponible : EstadoDeDisponibilidad.Agotada, v, [], FrescuraDeLaDisponibilidad.Reciente, DateTimeOffset.UnixEpoch, null)
                : new DisponibilidadDeEdicion(id, EstadoDeDisponibilidad.DisponibleAlInstante, null, [], FrescuraDeLaDisponibilidad.Reciente, null, null)),
        ];
        return Task.FromResult(Resultado<IReadOnlyList<DisponibilidadDeEdicion>>.Exito(respuesta));
    }
}

public sealed class PosesionDelCarrito : IPosesionDeEdiciones
{
    private readonly HashSet<Guid> _poseidas = [];

    public void Poseer(Guid edicionId) => _poseidas.Add(edicionId);

    public Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDePosesion.Posee([.. edicionIds.Where(_poseidas.Contains)]));
}

public sealed class CuentasDelCarrito : IEstadoDeCuenta
{
    private readonly HashSet<Guid> _canceladas = [];

    public bool CorreoSinVerificar { get; set; }

    public bool NoResponde { get; set; }

    public void Cancelar(Guid cuenta) => _canceladas.Add(cuenta);

    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (NoResponde)
        {
            throw new InvalidOperationException("Identidad no responde.");
        }

        var cancelada = _canceladas.Contains(usuarioId);
        return Task.FromResult<EstadoDeCuenta?>(new EstadoDeCuenta(usuarioId, !cancelada && !CorreoSinVerificar, EstadoDeAceptacionDeLosTextos.AlDia, !CorreoSinVerificar, cancelada, false));
    }

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.Aceptada);
}

public sealed class PagosDelCarrito : ICreadorDeIntencionesDePago
{
    /// <summary>La última instantánea que recibió Pagos (lleva el contacto, que es dato personal y se imprime redactado).</summary>
    public InstantaneaDeCompra? UltimaInstantanea { get; private set; }

    public ResultadoDeCrearIntencion AlCrear { get; set; } = ResultadoDeCrearIntencion.Creada;

    public ResultadoDeCancelarIntencion AlCancelar { get; set; } = ResultadoDeCancelarIntencion.Cancelada;

    public int Creaciones => _creaciones;

    public int Cancelaciones => _cancelaciones;

    public int Reaperturas => _reaperturas;

    private int _creaciones;
    private int _cancelaciones;
    private int _reaperturas;

    public Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default)
    {
        UltimaInstantanea = instantanea;
        Interlocked.Increment(ref _creaciones);
        return Task.FromResult(AlCrear);
    }

    public Task<ResultadoDeReabrirIntencion> Reabrir(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _reaperturas);
        return Task.FromResult(ResultadoDeReabrirIntencion.Reabierta);
    }

    public Task<ResultadoDeCancelarIntencion> Cancelar(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _cancelaciones);
        return Task.FromResult(AlCancelar);
    }
}

public sealed class TextosDelCarrito : ITextosLegales
{
    private readonly Dictionary<TipoDeTextoLegal, string> _versiones = new() { [TipoDeTextoLegal.Terminos] = "t-1", [TipoDeTextoLegal.Privacidad] = "p-1", [TipoDeTextoLegal.Reembolso] = "r-1" };

    public void Vigente(TipoDeTextoLegal tipo, string version) => _versiones[tipo] = version;

    public Task<Resultado<TextoLegal>> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<TextoLegal>.Exito(new TextoLegal(tipo, "Texto", _versiones[tipo], new DateOnly(2026, 1, 1), false, "Resumen", [])));
}

public sealed class MarcaDelCarrito : IMarcaDeCancelacion
{
    public EstadoDeLaMarcaDeCancelacion Estado { get; set; } = EstadoDeLaMarcaDeCancelacion.Libre;

    public Task<EstadoDeLaMarcaDeCancelacion> LeerConBloqueo(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(Estado);
}

public sealed class AuditoriaDelCarritoDePrueba : IAuditoriaDelCarrito
{
    private readonly List<string> _eventos = [];

    public bool Caida { get; set; }

    public IReadOnlyList<string> Eventos
    {
        get
        {
            lock (_eventos)
            {
                return [.. _eventos];
            }
        }
    }

    private Task Anotar(string evento)
    {
        if (Caida)
        {
            throw new InvalidOperationException("La auditoría no está disponible.");
        }

        lock (_eventos)
        {
            _eventos.Add(evento);
        }

        return Task.CompletedTask;
    }

    public Task FusionAplicada(Guid cuentaId, int lineasDelVisitante, IReadOnlyList<MotivoDeAjusteDeAviso> ajustes, CancellationToken cancelacion = default) => Anotar("fusion");

    public Task EdicionesRetiradas(int ediciones, int carritosAfectados, int lineasQuitadas, int lineasAnotadas, CancellationToken cancelacion = default) => Anotar("retiro");

    public Task CheckoutTerminado(string motivo, CancellationToken cancelacion = default) => Anotar("terminado:" + motivo);

    public Task CarritoEliminadoPorIdentidad(string motivo, CancellationToken cancelacion = default) => Anotar("eliminado:" + motivo);

    public Task PurgaRealizada(string trabajo, int cantidad, CancellationToken cancelacion = default) => Anotar("purga:" + trabajo);

    public Task PasoAlPagoIniciado(bool conImpresos, bool aDomicilio, CancellationToken cancelacion = default) => Anotar("paso-al-pago");

    public Task PasoAlPagoAbortado(string motivo, CancellationToken cancelacion = default) => Anotar("aborto:" + motivo);
}

public sealed class AlertasDelCarritoDePrueba : IAlertasDelCarrito
{
    public List<TipoDeAlertaDelCarrito> Publicadas { get; } = [];

    public Task Publicar(TipoDeAlertaDelCarrito tipo, int cantidad, CancellationToken cancelacion = default)
    {
        lock (Publicadas)
        {
            Publicadas.Add(tipo);
        }

        return Task.CompletedTask;
    }
}

public sealed class DenegacionesDelCarrito : IDenegacionDeAcceso
{
    public int Registradas => _registradas;

    private int _registradas;

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _registradas);
        return Task.CompletedTask;
    }
}

public sealed class HuellasDelCarrito : ICalculadorDeHuella
{
    private static string Texto(string valor) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public HuellaDeOrigen DeOrigen(string direccion) => HuellaDeOrigen.Crear(Texto(direccion), "v1");

    public HuellaDeOrigen? DeOrigenPorRed48(string direccion) => null;

    public IReadOnlyList<HuellaDeOrigen> DeOrigenEnRotacion(string direccion) => [DeOrigen(direccion)];

    public HuellaDeSujeto DeSujeto(string valor, string proposito) => HuellaDeSujeto.Crear(Texto(proposito + "|" + valor), "v1");

    public IReadOnlyList<HuellaDeSujeto> DeSujetoEnRotacion(string valor, string proposito) => [DeSujeto(valor, proposito)];
}

/// <summary>
/// Carrito de punta a punta sobre <b>PostgreSQL real</b> (T30 a T32): los casos de uso reales por los contratos públicos, los repositorios reales bajo <c>rol_carrito</c>, la composición real (<c>AgregarCarrito</c>) y dobles configurables para lo que es de otros módulos (Catálogo,
/// Pagos, Identidad, Biblioteca) y el Inventario simulado fiel a los contratos v1. La auditoría es un espía que puede caer (la real depende de las acciones de Carrito del carril de auditoría). Cada llamada corre en su propio alcance de servicios y con su actor, como una solicitud.
/// </summary>
public sealed class EntornoDelCarritoConPostgres : IAsyncDisposable
{
    private readonly ServiceProvider _proveedor;
    private readonly ActorDelFlujo _actor = new();

    private EntornoDelCarritoConPostgres(PostgresDelCarritoFixture bd)
    {
        Bd = bd;
        Reloj = new RelojDelCarrito();
        Inventario = new InventarioSimuladoDelCarrito(Reloj);
        InventarioDeLasPruebas = new InventarioDeLasPruebas(Inventario);
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IReloj>(Reloj);
        servicios.AddSingleton<IActorActual>(_actor);
        servicios.AddSingleton<IEstablecedorDeActor>(_actor);
        servicios.AddSingleton(bd.Acceso);
        servicios.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<Npgsql.NpgsqlDataSource>()));
        servicios.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<IConsultaDeCatalogo>(Catalogo);
        servicios.AddSingleton<IDisponibilidadDeEdiciones>(Disponibilidad);
        servicios.AddSingleton<IPosesionDeEdiciones>(Posesion);
        servicios.AddSingleton<IEstadoDeCuenta>(Cuentas);
        servicios.AddSingleton<ICreadorDeIntencionesDePago>(Pagos);
        servicios.AddSingleton<IInventarioDelCarrito>(InventarioDeLasPruebas);
        servicios.AddSingleton<ITextosLegales>(Textos);
        servicios.AddSingleton<IMarcaDeCancelacion>(Marca);
        servicios.AddSingleton<IAuditoriaDelCarrito>(Auditoria);
        servicios.AddSingleton<IAlertasDelCarrito>(Alertas);
        servicios.AddSingleton<IDenegacionDeAcceso>(Denegaciones);
        servicios.AddSingleton<ICalculadorDeHuella>(new HuellasDelCarrito());
        servicios.AddSingleton(Doble<IClienteDelInventario>());
        servicios.AddSingleton(Doble<IConsentimientoDeAvisos>());
        servicios.AddSingleton(Doble<ILimitadorDelCarrito>());
        servicios.AgregarCarrito(AppSettingsOpcionesDeEnvio.Desde("12.00"), esProduccion: false);
        _proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public PostgresDelCarritoFixture Bd { get; }

    public RelojDelCarrito Reloj { get; }

    public CatalogoDelCarrito Catalogo { get; } = new();

    public DisponibilidadDelCarrito Disponibilidad { get; } = new();

    public PosesionDelCarrito Posesion { get; } = new();

    public CuentasDelCarrito Cuentas { get; } = new();

    public PagosDelCarrito Pagos { get; } = new();

    public InventarioSimuladoDelCarrito Inventario { get; }

    public InventarioDeLasPruebas InventarioDeLasPruebas { get; }

    public TextosDelCarrito Textos { get; } = new();

    public MarcaDelCarrito Marca { get; } = new();

    public AuditoriaDelCarritoDePrueba Auditoria { get; } = new();

    public AlertasDelCarritoDePrueba Alertas { get; } = new();

    public DenegacionesDelCarrito Denegaciones { get; } = new();

    public static async Task<EntornoDelCarritoConPostgres> Crear(PostgresDelCarritoFixture bd)
    {
        await bd.Limpiar();
        return new EntornoDelCarritoConPostgres(bd);
    }

    /// <summary>Un doble que lanza en toda llamada: para lo que estas pruebas nunca tocan (límites y consentimiento de avisos se prueban aparte).</summary>
    private static T Doble<T>()
        where T : class => System.Reflection.DispatchProxy.Create<T, LimitadorYConsentimientoPermisivos>();

    /// <summary>Los límites de uso permiten todo y el consentimiento de avisos no se ofrece: lo que estas pruebas no ejercitan.</summary>
    public class LimitadorYConsentimientoPermisivos : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? metodo, object?[]? argumentos)
        {
            var tipo = metodo!.ReturnType;
            if (tipo == typeof(Task<ResultadoDeLimiteDelCarrito>))
            {
                return Task.FromResult(ResultadoDeLimiteDelCarrito.Permitido);
            }

            if (tipo == typeof(Task<OfertaDeConsentimiento>))
            {
                return Task.FromResult(OfertaDeConsentimiento.NoSeOfrece);
            }

            if (tipo == typeof(Task<bool>))
            {
                return Task.FromResult(false);
            }

            throw new NotSupportedException();
        }
    }

    // ---------- Quién pide ----------

    public static Guid NuevaCuenta() => Guid.NewGuid();

    /// <summary>Una solicitud de un cliente con sesión (o de un visitante si <paramref name="cuenta"/> es <see langword="null"/>), en su propio alcance de servicios.</summary>
    public async Task<T> Como<T>(Guid? cuenta, Func<IServiceProvider, Task<T>> accion)
    {
        using var alcance = _proveedor.CreateScope();
        if (cuenta is { } id)
        {
            _actor.Establecer(ActorActual.DeUsuario(id, "cliente", null));
        }
        else
        {
            _actor.Establecer(ActorActual.Anonimo());
        }

        try
        {
            return await accion(alcance.ServiceProvider);
        }
        finally
        {
            _actor.Limpiar();
        }
    }

    /// <summary>Un trabajo de fondo (actor «sistema»).</summary>
    public async Task<T> ComoSistema<T>(Func<IServiceProvider, Task<T>> accion)
    {
        using var alcance = _proveedor.CreateScope();
        _actor.Establecer(ActorActual.DelSistema());
        try
        {
            return await accion(alcance.ServiceProvider);
        }
        finally
        {
            _actor.Limpiar();
        }
    }

    public Task<Resultado<ResultadoDeCarrito>> Agregar(Guid? cuenta, Guid edicion, int cantidad, string? cookie = null) =>
        Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(edicion, cantidad, cuenta is null ? cookie : null)));

    public static readonly TextoVistoDeCompra[] LosDosTextos = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    public static readonly TextoVistoDeCompra[] LosTresTextos = [.. LosDosTextos, new(TextoLegalDeCompra.ReembolsoDigital, "r-1")];

    /// <summary>Lleva la cuenta hasta tener el checkout con la entrega elegida y los textos aceptados, listo para pasar al pago.</summary>
    public async Task<VistaDelCheckout> PrepararCheckout(Guid cuenta, bool domicilio = false, bool conDigitales = false)
    {
        var textos = conDigitales ? LosTresTextos : LosDosTextos;
        await Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()));
        var entrega = domicilio
            ? new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null, new ContactoDeCompra("Ana Pérez Quispe", "987654321", "Jr. Lima 123", "ayacucho"))
            : new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null);
        var vista = await Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(entrega));
        await Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(textos)));
        return vista.Valor;
    }

    public Task<Resultado<ResultadoDePasarAlPago>> Pagar(Guid cuenta, decimal total, bool conDigitales = false, Guid? pedidoId = null) =>
        Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().PasarAlPago(new PasarAlPagoEntrada(total, conDigitales ? LosTresTextos : LosDosTextos, pedidoId)));

    public async ValueTask DisposeAsync() => await _proveedor.DisposeAsync();
}

/// <summary>
/// El Inventario de las pruebas: delega en el simulado fiel a los contratos v1 salvo que una prueba le fije otra respuesta (caído, sin respuesta, límite, sin stock, respuesta inválida).
/// </summary>
public sealed class InventarioDeLasPruebas(IInventarioDelCarrito real) : IInventarioDelCarrito
{
    public Func<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>>? RespuestaDeDisponibilidad { get; set; }

    public Func<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>>? RespuestaDePrevia { get; set; }

    public Func<ResultadoDeReservar>? RespuestaDeReserva { get; set; }

    public Func<ResultadoDeLiberar>? RespuestaDeLiberar { get; set; }

    public int Reservas { get; private set; }

    public int Liberaciones { get; private set; }

    public Task<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        RespuestaDeDisponibilidad is { } r ? Task.FromResult(r()) : real.ConsultarDisponibilidad(edicionIds, cancelacion);

    public Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default) =>
        RespuestaDePrevia is { } r ? Task.FromResult(r()) : real.ConsultarPrevia(entrega, lineas, cancelacion);

    public Task<BabelHub.Ecommerce.Application.Carrito.Interno.ListaDeSucursales?> ListarSucursales(CancellationToken cancelacion = default) => real.ListarSucursales(cancelacion);

    public Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        Reservas++;
        return RespuestaDeReserva is { } r ? Task.FromResult(r()) : real.Reservar(pedidoId, generacionDeReserva, entrega, lineas, cancelacion);
    }

    public Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default)
    {
        Liberaciones++;
        return RespuestaDeLiberar is { } r ? Task.FromResult(r()) : real.Liberar(pedidoId, motivo, cancelacion);
    }
}
