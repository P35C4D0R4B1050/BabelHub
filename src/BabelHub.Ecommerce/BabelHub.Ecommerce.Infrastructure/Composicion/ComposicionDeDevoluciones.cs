using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>La configuración de Devoluciones es inválida para el entorno (un doble provisional en producción): el arranque falla nombrando cuál.</summary>
public sealed class ConfiguracionDeDevolucionesInvalidaException(string mensaje) : InvalidOperationException(mensaje);

/// <summary>
/// La composición del módulo Devoluciones (T22; <c>ecommerce-devoluciones</c>): el repositorio de PostgreSQL bajo <c>EjecutarComo(devoluciones)</c>, los casos de uso por alcance de solicitud, las implementaciones públicas <see cref="IDevolucionesDelCliente"/>
/// y <see cref="IGestionDeDevoluciones"/>, la parte de Devoluciones de los pendientes y de la cancelación de la cuenta (<see cref="IPendientesDeCuenta"/> e <see cref="IAplicadorDeCancelacion"/>, que se registran como parte de las enumeraciones que Identidad
/// recorre) y, al final, lo que otros módulos aún no entregan. <b>Orden en producción:</b> el cliente del Inventario (<see cref="IClienteDelInventario"/>) y Biblioteca se registran <b>antes</b> de <c>AgregarDevoluciones</c>: si el cliente está, el reingreso y las
/// sucursales son <c>RestInventarioDeDevoluciones</c>; si no, el doble provisional (falla cerrado) y la guarda de producción lo rechaza. Necesita ya registrados: <see cref="IUnidadDeTrabajo"/>, <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>,
/// <see cref="IDenegacionDeAcceso"/>, <see cref="IReloj"/>, <see cref="IControlDeLimites"/>, <see cref="BabelHub.Ecommerce.Application.Auditoria.IAuditoria"/>, <see cref="IPublicadorDeAlertasTecnicas"/>, <see cref="BabelHub.Ecommerce.Application.Notificaciones.INotificador"/>,
/// <see cref="IEstadoDeCuenta"/>, <see cref="IMarcaDeCancelacionDeCuenta"/>, <see cref="BabelHub.Ecommerce.Application.Pagos.IReembolsos"/>, <see cref="IAccesosDeBiblioteca"/> y el contrato de lectura de Pedidos para Devoluciones (real o provisional).
/// </summary>
public static class ComposicionDeDevoluciones
{
    public static IServiceCollection AgregarDevoluciones(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // ---------- Infraestructura (por solicitud: usa la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDeDevoluciones>();
        servicios.TryAddScoped<IRepositorioDeDevoluciones, RepositorioDeDevolucionesPostgres>();
        servicios.TryAddSingleton<CacheDeSucursalesActivas>();
        servicios.TryAddSingleton<ControlDeLaDepuracionDeDevoluciones>();

        // ---------- Aplicación ----------
        servicios.TryAddScoped<AutorizadorDeDevoluciones>();
        servicios.TryAddScoped<LimitesDeDevoluciones>();
        servicios.TryAddScoped<AvisosDeDevoluciones>();
        servicios.TryAddScoped<ProcesarReembolsoDeLinea>();
        servicios.TryAddScoped<LocalizadorDeDevolucionesDeUnaCuenta>();
        servicios.TryAddScoped<SolicitarDevolucionDeImpreso>();
        servicios.TryAddScoped<SolicitarReembolsoDeDigital>();
        servicios.TryAddScoped<ReportarArchivoDefectuoso>();
        servicios.TryAddScoped<DecidirReporte>();
        servicios.TryAddScoped<AprobarYRechazarDevolucion>();
        servicios.TryAddScoped<ConfirmarRecepcion>();
        servicios.TryAddScoped<ConciliarReembolsosDeDevoluciones>();
        servicios.TryAddScoped<RechazarDevolucionesVencidas>();
        servicios.TryAddScoped<RevisarDevolucionesEstancadas>();
        servicios.TryAddScoped<RedactarDevolucionesVencidas>();
        servicios.TryAddScoped<RevisarDepuracionDeDevoluciones>();
        servicios.TryAddScoped<ConsultasDeDevoluciones>();

        // ---------- Contratos públicos ----------
        servicios.TryAddScoped<IDevolucionesDelCliente, DevolucionesDelClienteDeLaAplicacion>();
        servicios.TryAddScoped<IGestionDeDevoluciones, GestionDeDevolucionesDeLaAplicacion>();

        // La parte de Devoluciones de la cancelación de la cuenta: se suma a la enumeración que Identidad recorre (no se reemplaza a nadie).
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPendientesDeCuenta, ContarPendientesDeCuenta>());
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IAplicadorDeCancelacion, RedactarDevolucionesDeUnaCuenta>());

        // ---------- Lo que aún no existe, con su guarda de producción ----------
        if (servicios.Any(d => d.ServiceType == typeof(IClienteDelInventario)))
        {
            servicios.TryAddScoped<IInventarioDeDevoluciones, RestInventarioDeDevoluciones>();
        }

        servicios.TryAddSingleton<IInventarioDeDevoluciones, InventarioDeDevolucionesProvisional>();
        servicios.TryAddSingleton<IResolucionDeSucursal, ResolucionDeSucursalDeDevolucionesProvisional>();
        servicios.TryAddSingleton<IAccesosParaDevoluciones, AccesosParaDevolucionesProvisional>();
        if (esProduccion)
        {
            var provisionales = new List<string>();
            foreach (var (puerto, doble) in new[]
            {
                (typeof(IInventarioDeDevoluciones), typeof(InventarioDeDevolucionesProvisional)),
                (typeof(IResolucionDeSucursal), typeof(ResolucionDeSucursalDeDevolucionesProvisional)),
                (typeof(IAccesosParaDevoluciones), typeof(AccesosParaDevolucionesProvisional)),
            })
            {
                if (servicios.Any(d => d.ServiceType == puerto && (d.ImplementationType == doble || d.ImplementationInstance?.GetType() == doble)))
                {
                    provisionales.Add(puerto.Name);
                }
            }

            if (provisionales.Count > 0)
            {
                throw new ConfiguracionDeDevolucionesInvalidaException(
                    $"Devoluciones: en producción no se permiten dobles provisionales (fallan cerrado). Falta la implementación real de: {string.Join(", ", provisionales)}.");
            }
        }

        return servicios;
    }
}

public static class ComposicionDeLosTrabajosDeDevoluciones
{
    /// <summary>
    /// Registra los trabajos de fondo de Devoluciones y el servicio que los corre. <paramref name="alcanceDeSistema"/> lo construye el anfitrión —la Api— con sus servicios y fija el actor «sistema» alrededor de cada corrida. Se llama <b>solo en modo normal</b> y después de
    /// <see cref="ComposicionDeDevoluciones.AgregarDevoluciones"/>; quien la llame añade también <c>AddHostedService&lt;ServicioDeTrabajosDeDevoluciones&gt;()</c>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeFondoDeDevoluciones(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistemaDeDevoluciones> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeDevoluciones.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            new AlertasTecnicasPorCorrida(sp.GetRequiredService<IServiceScopeFactory>()),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDeDevoluciones(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeDevoluciones>>()));
        return servicios;
    }
}

/// <summary>El Inventario sin su adaptador (hace falta el cliente de Plataforma): el reingreso es incierto y las sucursales no están disponibles, de modo que nada se reingresa ni se solicita contra un Inventario simulado.</summary>
internal sealed class InventarioDeDevolucionesProvisional : IInventarioDeDevoluciones
{
    public Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion reingreso, CancellationToken cancelacion = default) => Task.FromResult<ResultadoDelReingreso>(new ResultadoDelReingreso.Incierto());

    public Task<ResultadoDeSucursalesActivas> ListarSucursalesActivas(CancellationToken cancelacion = default) => Task.FromResult<ResultadoDeSucursalesActivas>(new ResultadoDeSucursalesActivas.NoDisponible());
}

/// <summary>
/// La resolución del código de la sucursal de un actor sin la equivalencia entre el identificador y el código (D15 de <c>docs/03/08</c>, pendiente de Identidad): no resuelve ninguna, así que se niega el alcance del encargado y se registra (falla cerrado; la administración central
/// no la necesita).
/// </summary>
internal sealed class ResolucionDeSucursalDeDevolucionesProvisional : IResolucionDeSucursal
{
    public Task<string?> CodigoDe(Guid sucursalId, CancellationToken cancelacion = default) => Task.FromResult<string?>(null);
}

/// <summary>Los accesos digitales sin Biblioteca (ECO-8): ninguna consulta, revocación, suspensión ni reanudación responde, así que un reembolso o un reporte digital no se crea (falla cerrado).</summary>
internal sealed class AccesosParaDevolucionesProvisional : IAccesosParaDevoluciones
{
    private static ErrorDeAplicacion NoDisponible() => new(CategoriaDeError.NoDisponible, "servicio-no-disponible");

    public Task<Resultado<AccesoParaDevoluciones?>> Consultar(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<AccesoParaDevoluciones?>.Fallo(NoDisponible()));

    public Task<Resultado<ResultadoDeRevocarSiNoHayEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeRevocarSiNoHayEnlace>.Fallo(NoDisponible()));

    public Task<Resultado<ResultadoDeSuspenderAcceso>> Suspender(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeSuspenderAcceso>.Fallo(NoDisponible()));

    public Task<Resultado<ResultadoDeReanudarAcceso>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeReanudarAcceso>.Fallo(NoDisponible()));
}
