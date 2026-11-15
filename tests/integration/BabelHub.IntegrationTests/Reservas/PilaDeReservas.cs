using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Fuentes;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>Lo que se pidió auditar, con el tipo del actor del contexto en ese instante (la expiración debe auditarse con actor «sistema» aunque la transacción sea de un cliente). Seguro entre hilos.</summary>
public sealed class AuditoriaDeIntegracionEspia(IActorActual actor) : IAuditoria
{
    private readonly object _candado = new();

    public List<(string Accion, string? ObjetoId, Guid? Fuente, TipoDeActor? Actor, IReadOnlyDictionary<string, string?> Detalle)> Registros { get; } = [];

    public bool Falla { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("La auditoría no responde."));
        }

        lock (_candado)
        {
            Registros.Add((solicitud.Accion.Identificador, solicitud.ObjetoId, solicitud.SucursalOrigen, actor.Actual?.Tipo, solicitud.Detalle));
        }

        return Task.CompletedTask;
    }

    public int Contar(string accion)
    {
        lock (_candado)
        {
            return Registros.Count(r => r.Accion == accion);
        }
    }
}

public sealed class DenegacionesDeIntegracionEspia : IDenegacionDeAcceso
{
    private readonly object _candado = new();

    public List<string> Motivos { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            Motivos.Add(denegacion.Motivo);
        }

        return Task.CompletedTask;
    }
}


/// <summary>El origen de la solicitud en curso, por flujo asíncrono: cada llamada de la pila tiene el suyo (las pruebas de concurrencia simulan clientes de orígenes distintos) salvo que se fije uno.</summary>
public sealed class OrigenPorFlujo : IOrigenDeSolicitud
{
    private readonly AsyncLocal<OrigenDeSolicitud?> _actual = new();

    public OrigenDeSolicitud Actual => _actual.Value ?? OrigenDeSolicitud.Desconocido;

    public void Fijar(OrigenDeSolicitud origen) => _actual.Value = origen;

    /// <summary>Un origen conocido nuevo: la huella es única para esta llamada.</summary>
    public static OrigenDeSolicitud Nuevo() =>
        new(HuellaDeOrigen.Crear(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), "v1"), "203.0.113.9");
}

/// <summary>Los eventos de seguridad que se publicaron (los de límite excedido). Seguro entre hilos.</summary>
public sealed class EventosDeSeguridadEspia : IPublicadorDeEventosDeSeguridad
{
    private readonly object _candado = new();

    public List<EventoDeSeguridad> Eventos { get; } = [];

    public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
    {
        lock (_candado)
        {
            Eventos.Add(evento);
        }

        return Task.CompletedTask;
    }
}
/// <summary>
/// La pila real del Inventario para <c>Fuentes</c> y <c>Reservas</c> (el contenedor de dependencias de producción: unidad de trabajo con el rol real de la aplicación, repositorios de PostgreSQL, decoradores de
/// transacción y de reintento por interbloqueo) con la auditoría, las denegaciones, las alertas y el reloj como dobles. Cada llamada de <see cref="Ejecutar{TEntrada, TSalida}"/> corre en su propio flujo asíncrono con
/// su actor, de modo que muchas llamadas simultáneas se comportan como solicitudes distintas.
/// </summary>
public sealed class PilaDeReservas : IDisposable
{
    public PilaDeReservas(PostgresDeReservasFixture bd, Action<IServiceCollection>? ajustes = null, IReadOnlyDictionary<string, string?>? configuracion = null, bool reservasPrimero = false)
    {
        var actor = new ActorDelFlujo();
        Establecedor = actor;
        Auditoria = new AuditoriaDeIntegracionEspia(actor);
        Denegaciones = new DenegacionesDeIntegracionEspia();
        Alertas = new AlertasTecnicasEspia();
        Eventos = new EventosDeSeguridadEspia();
        Origen = new OrigenPorFlujo();
        Reloj = new RelojDeIntegracion();

        var servicios = new ServiceCollection();
        servicios.AddSingleton(bd.Acceso);
        servicios.AddSingleton<IActorActual>(actor);
        servicios.AddSingleton<IEstablecedorDeActor>(actor);
        servicios.AddSingleton<IReloj>(Reloj);
        servicios.AddSingleton<IPublicadorDeAlertasTecnicas>(Alertas);
        servicios.AddSingleton<IDenegacionDeAcceso>(Denegaciones);
        servicios.AddSingleton<IAuditoria>(Auditoria);
        servicios.AddSingleton<IPublicadorDeEventosDeSeguridad>(Eventos);
        servicios.AddSingleton<IOrigenDeSolicitud>(Origen);
        servicios.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas));
        servicios.AddSingleton<IControlDeLimites>(sp => new EvaluarLimite(
            sp.GetRequiredService<CatalogoDePoliticas>(),
            new MotorDeLimitesPostgres(sp.GetRequiredService<UnidadDeTrabajoPostgres>(), new UnidadDeTrabajoPostgres(bd.Acceso, actor: actor)),
            null,
            Origen,
            Eventos,
            Reloj));
        ajustes?.Invoke(servicios);

        var ajustesDeConfiguracion = new ConfigurationBuilder().AddInMemoryCollection(configuracion ?? new Dictionary<string, string?>()).Build();
        if (reservasPrimero)
        {
            servicios.AgregarModuloReservas(ajustesDeConfiguracion);
            servicios.AgregarModuloFuentes();
        }
        else
        {
            servicios.AgregarModuloFuentes();
            servicios.AgregarModuloReservas(ajustesDeConfiguracion);
        }

        Proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public ServiceProvider Proveedor { get; }

    public IEstablecedorDeActor Establecedor { get; }

    public AuditoriaDeIntegracionEspia Auditoria { get; }

    public DenegacionesDeIntegracionEspia Denegaciones { get; }

    public AlertasTecnicasEspia Alertas { get; }

    public EventosDeSeguridadEspia Eventos { get; }

    public OrigenPorFlujo Origen { get; }

    public RelojDeIntegracion Reloj { get; }

    public static ActorActual Cliente(Guid? id = null) => ActorActual.DeUsuario(id ?? Guid.NewGuid(), RolesDeUsuario.Cliente);

    /// <summary>Corre un caso de uso en su propio flujo asíncrono con el actor dado (<see langword="null"/> = nadie estableció el contexto).</summary>
    public Task<Resultado<TSalida>> Ejecutar<TEntrada, TSalida>(ActorActual? actor, TEntrada entrada, CancellationToken cancelacion = default, OrigenDeSolicitud? origen = null) =>
        Task.Run(
            async () =>
            {
                Origen.Fijar(origen ?? OrigenPorFlujo.Nuevo());
                if (actor is not null)
                {
                    Establecedor.Establecer(actor);
                }

                await using var ambito = Proveedor.CreateAsyncScope();
                return await ambito.ServiceProvider.GetRequiredService<ICasoDeUso<TEntrada, TSalida>>().Ejecutar(entrada, cancelacion);
            },
            cancelacion);

    /// <summary>Un cliente reserva. Devuelve el resultado completo (el éxito o el fallo esperado).</summary>
    public Task<Resultado<ReservaDelPedido>> Reservar(ActorActual? actor, ReservarEntrada entrada, CancellationToken cancelacion = default, OrigenDeSolicitud? origen = null) =>
        Ejecutar<ReservarEntrada, ReservaDelPedido>(actor, entrada, cancelacion, origen);

    /// <summary>Corre un bloque en su propio flujo, con el actor dado y un ámbito del contenedor.</summary>
    public Task<T> EnUnAmbito<T>(ActorActual? actor, Func<IServiceProvider, Task<T>> bloque, OrigenDeSolicitud? origen = null) =>
        Task.Run(
            async () =>
            {
                Origen.Fijar(origen ?? OrigenPorFlujo.Nuevo());
                if (actor is not null)
                {
                    Establecedor.Establecer(actor);
                }

                await using var ambito = Proveedor.CreateAsyncScope();
                return await bloque(ambito.ServiceProvider);
            });

    public static ReservarEntrada ARecojo(string sucursal, IReadOnlyList<LineaSolicitada> lineas, Guid? pedidoId = null) =>
        new(pedidoId ?? Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, sucursal, null, lineas, Guid.NewGuid().ToString("N"));

    public static ReservarEntrada ADomicilio(string region, IReadOnlyList<LineaSolicitada> lineas, Guid? pedidoId = null) =>
        new(pedidoId ?? Guid.NewGuid(), TipoDeEntregaDeReserva.Domicilio, null, region, lineas, Guid.NewGuid().ToString("N"));

    public static LineaSolicitada Linea(Guid edicionId, int cantidad = 1) => new(Guid.NewGuid(), edicionId, cantidad);

    public void Dispose() => Proveedor.Dispose();
}
