using System.Collections.Concurrent;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>Un reloj que se mueve a mano (los plazos, las reanudaciones y la retención de Pedidos dependen de él).</summary>
internal sealed class RelojDePedidos(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = ahora;

    public DateOnly HoyEnLima => PoliticaDeRetencion.FechaEnLima(Ahora);
}

internal sealed class DenegacionesDePedidosEspia : IDenegacionDeAcceso
{
    public ConcurrentBag<DenegacionDeAcceso> Registradas { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        Registradas.Add(denegacion);
        return Task.CompletedTask;
    }
}

internal sealed class AlertasDePedidosEspia : IPublicadorDeAlertasTecnicas
{
    public ConcurrentBag<string> Motivos { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Motivos.Add(alerta.Motivo);
        return Task.CompletedTask;
    }
}

/// <summary>
/// La Aplicación real de Pedidos (casos de uso, orquestadores y fachadas, tal como los compone <c>AgregarPedidos</c>) sobre el repositorio y la unidad de trabajo reales de PostgreSQL, con dobles seguros para hilos en los bordes
/// (Inventario, Pagos, Biblioteca, Envíos, avisos y auditoría). Cada llamada abre su propio alcance y fija su propio actor, así que varias tareas pueden operar a la vez sin pisarse.
/// </summary>
internal sealed class EscenarioDePedidos : IAsyncDisposable
{
    public static readonly DateTimeOffset InicioDelDia = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public static readonly Guid Titular = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid Encargado = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid Logistica = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid Administrador = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ServiceProvider _proveedor;

    private EscenarioDePedidos(ServiceProvider proveedor) => _proveedor = proveedor;

    public RelojDePedidos Reloj { get; private init; } = null!;

    public ReservasFalsas Reservas { get; private init; } = null!;

    public ReembolsosFalsos Reembolsos { get; private init; } = null!;

    public AccesosDeBibliotecaFalsos Accesos { get; private init; } = null!;

    public DespachoDeEnviosFalso Despacho { get; private init; } = null!;

    public NotificadorDePedidosEspia Avisos { get; private init; } = null!;

    public AuditoriaDePedidosEspia Auditoria { get; private init; } = null!;

    public DenegacionesDePedidosEspia Denegaciones { get; private init; } = null!;

    public AlertasDePedidosEspia Alertas { get; private init; } = null!;

    public AuditoriaDeContactoFalsa AuditoriaDelContacto { get; private init; } = null!;

    public LiberacionDeContactoDePagosFalsa LiberacionDeContacto { get; private init; } = null!;

    /// <summary>Compone la Aplicación real sobre la unidad de trabajo dada. Con una unidad que nunca se usa sirve para comprobar solo la composición (sin base de datos).</summary>
    public static EscenarioDePedidos Crear(UnidadDeTrabajoPostgres unidad)
    {
        ArgumentNullException.ThrowIfNull(unidad);
        var reloj = new RelojDePedidos(InicioDelDia);
        var reservas = new ReservasFalsas();
        var reembolsos = new ReembolsosFalsos();
        var accesos = new AccesosDeBibliotecaFalsos();
        var despacho = new DespachoDeEnviosFalso();
        var avisos = new NotificadorDePedidosEspia();
        var auditoria = new AuditoriaDePedidosEspia();
        var denegaciones = new DenegacionesDePedidosEspia();
        var alertas = new AlertasDePedidosEspia();
        var auditoriaDelContacto = new AuditoriaDeContactoFalsa();

        var servicios = new ServiceCollection();
        servicios.AddSingleton<IUnidadDeTrabajo>(unidad);
        servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(unidad);
        servicios.AddSingleton<IReloj>(reloj);
        var actor = new ActorDelFlujo();
        servicios.AddSingleton<IActorActual>(actor);
        servicios.AddSingleton<IEstablecedorDeActor>(actor);
        servicios.AddSingleton<IReservasDelPedido>(reservas);
        servicios.AddSingleton<IReembolsos>(reembolsos);
        servicios.AddSingleton<IAccesosDeBiblioteca>(accesos);
        servicios.AddSingleton<IDespachoDeEnvios>(despacho);
        servicios.AddSingleton<INotificador>(avisos);
        servicios.AddSingleton<IAuditoria>(auditoria);
        servicios.AddSingleton<IDenegacionDeAcceso>(denegaciones);
        servicios.AddSingleton<IPublicadorDeAlertasTecnicas>(alertas);
        servicios.AddSingleton<IAuditoriaDeContacto>(auditoriaDelContacto);
        servicios.AddSingleton<ILimitadorDePedidos>(new LimitesPermisivosDePedidos());
        servicios.AddSingleton<IResolucionDeSucursal>(new ResolucionDeSucursalFalsa());
        servicios.AddSingleton<IEstadoDeCuenta>(new EstadoDeCuentaFalso());
        servicios.AddSingleton<IMarcaDeCancelacionDeCuenta>(new MarcaDeCancelacionFalsa());
        var liberacion = new LiberacionDeContactoDePagosFalsa();
        servicios.AddSingleton<ILiberacionDeContactoDePago>(liberacion);
        servicios.AgregarPedidos(esProduccion: false);
        var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

        return new EscenarioDePedidos(proveedor)
        {
            Reloj = reloj,
            Reservas = reservas,
            Reembolsos = reembolsos,
            Accesos = accesos,
            Despacho = despacho,
            Avisos = avisos,
            Auditoria = auditoria,
            Denegaciones = denegaciones,
            Alertas = alertas,
            AuditoriaDelContacto = auditoriaDelContacto,
            LiberacionDeContacto = liberacion,
        };
    }

    /// <summary>Una unidad de trabajo que nunca se conecta: solo para comprobar que la composición resuelve todo lo que Pedidos expone.</summary>
    public static UnidadDeTrabajoPostgres UnidadSinConexion() => new(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=sin-uso;Username=sin-uso"));

    public static ActorActual DelTitular(Guid? cuenta = null) => ActorActual.DeUsuario(cuenta ?? Titular, RolesDeUsuario.Cliente, null);

    public static ActorActual DelEncargado(Guid sucursal) => ActorActual.DeUsuario(Encargado, RolesDeUsuario.EncargadoDeSucursal, sucursal);

    public static ActorActual DeLogistica() => ActorActual.DeUsuario(Logistica, RolesDeUsuario.PersonalDeLogistica, null);

    public static ActorActual DeLaAdministracion() => ActorActual.DeUsuario(Administrador, RolesDeUsuario.AdministradorCentral, null);

    /// <summary>Abre un alcance propio, fija el actor y corre el bloque; el actor se limpia al terminar.</summary>
    public async Task<T> Como<T>(ActorActual quien, Func<IServiceProvider, Task<T>> bloque)
    {
        await using var alcance = _proveedor.CreateAsyncScope();
        var establecedor = _proveedor.GetRequiredService<IEstablecedorDeActor>();
        establecedor.Establecer(quien);
        try
        {
            return await bloque(alcance.ServiceProvider);
        }
        finally
        {
            establecedor.Limpiar();
        }
    }

    /// <summary>Resuelve un servicio en un alcance nuevo (sin actor): para comprobar la composición.</summary>
    public T Resolver<T>()
        where T : notnull
    {
        using var alcance = _proveedor.CreateScope();
        return alcance.ServiceProvider.GetRequiredService<T>();
    }

    public ValueTask DisposeAsync() => _proveedor.DisposeAsync();
}
