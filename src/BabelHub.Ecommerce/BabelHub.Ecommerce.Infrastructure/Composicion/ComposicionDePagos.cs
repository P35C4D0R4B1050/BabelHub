using System.Text;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La composición del módulo Pagos (T27; <c>ecommerce-pagos</c>): primero la guarda de arranque (<see cref="GuardaDeArranqueDePagos"/>: la lista blanca de pasarela y llave por entorno, los secretos y el adaptador nulo en los modos sin llamadas reales), luego los
/// repositorios de PostgreSQL bajo <c>EjecutarComo(pagos)</c>, la pasarela elegida (el simulador solo en <c>Development</c> y <c>Test</c>, el nulo si el modo no permite llamadas reales; <b>el adaptador de Culqi aún no existe y su elección falla el arranque</b>, T37 y T38),
/// las reservas del Inventario sobre <see cref="IClienteDelInventario"/>, los casos de uso por alcance de solicitud, las implementaciones públicas (<see cref="IPagos"/>, <see cref="IReembolsos"/>, <see cref="ILiberacionDeContactoDePago"/>, <see cref="IAvisosDeLaPasarela"/>,
/// <see cref="ICreadorDeIntencionesDePago"/> y las partes de Pagos de <see cref="IPendientesDeCuenta"/> y <see cref="IAplicadorDeCancelacion"/>) y los trabajos de fondo (los registra <see cref="ComposicionDeLosTrabajosDePagos"/>). Lo que otros módulos aún no entregan (Identidad
/// para la cuenta del pagador y Pedidos para la confirmación de la compra) se registra al final con sus dobles provisionales, que en producción <b>fallan el arranque</b>. Necesita ya registrados: <see cref="IUnidadDeTrabajo"/>, <see cref="IConexionDeLaUnidadDeTrabajo"/>,
/// <see cref="IActorActual"/>, <see cref="IDenegacionDeAcceso"/>, <see cref="IReloj"/>, <see cref="IClienteDelInventario"/>, <see cref="IControlDeLimites"/>, <see cref="IProcesadorDeEntradasDeIntegracion"/>, <see cref="IEnviadorDeCorreo"/>, el auditor y el publicador de
/// alertas técnicas de Auditoría, y de Carrito <see cref="ICheckoutDelCliente"/>, <see cref="IProgresoDelCheckout"/> e <see cref="IPosesionDeEdiciones"/>.
/// </summary>
public static class ComposicionDePagos
{
    public static IServiceCollection AgregarPagos(this IServiceCollection servicios, OpcionesDeArranqueDePagos opciones, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);
        GuardaDeArranqueDePagos.Exigir(opciones);
        if (opciones.Pasarela == GuardaDeArranqueDePagos.Culqi && !opciones.PasarelaEsNula)
        {
            throw new ConfiguracionDePagosInvalidaException($"Configuración de Pagos inválida: {GuardaDeArranqueDePagos.VariablePasarela} «culqi» aún no tiene adaptador (T37 y T38, a la espera de las llaves de pruebas).");
        }

        // ---------- Infraestructura ----------
        if (opciones.PasarelaEsNula)
        {
            servicios.TryAddSingleton<IPasarelaDePago, NuloPasarelaDePago>();
        }
        else
        {
            servicios.TryAddSingleton<IPasarelaDePago>(sp => new SimuladorPasarelaDePago(sp.GetRequiredService<IReloj>()));
        }

        var actual = Encoding.UTF8.GetBytes(opciones.ClaveDeIntentos!);
        var anterior = opciones.ClaveDeIntentosAnterior is { } a ? Encoding.UTF8.GetBytes(a) : null;
        servicios.TryAddSingleton(new OpcionesDeClavesDeIntento(actual, anterior));
        servicios.TryAddSingleton<FirmadorDeClavesDeIntento>();
        servicios.TryAddSingleton(OpcionesDelCobro.PorDefecto);
        servicios.TryAddScoped<AccesoAlEsquemaDePagos>();
        servicios.TryAddScoped<IRepositorioDePagos, RepositorioDePagosPostgres>();
        servicios.TryAddScoped<IRepositorioDeReembolsos, RepositorioDeReembolsosPostgres>();
        servicios.TryAddScoped<IReservasDelPago, RestReservasDelPago>();
        // La cuenta del pagador sale de Identidad cuando esta ya está compuesta (sin ella el doble provisional falla cerrado y ninguna compra se cobra; en producción su guarda lo exige).
        if (servicios.Any(d => d.ServiceType == typeof(IEstadoDeCuenta)) && servicios.Any(d => d.ServiceType == typeof(IMarcaDeCancelacionDeCuenta)) && servicios.Any(d => d.ServiceType == typeof(ICorreoDelTitular)))
        {
            servicios.TryAddScoped<ICuentaDelPagador, CuentaDelPagadorSobreIdentidad>();
        }

        // ---------- Aplicación ----------
        servicios.TryAddScoped<AutorizadorDePagos>();
        servicios.TryAddScoped<BloqueoDeIntenciones>();
        servicios.TryAddScoped<LimitadorDeCobros>();
        servicios.TryAddScoped<EmitirClaveDeIntento>();
        servicios.TryAddScoped<ICasoDeUso<EmitirClaveDeIntentoEntrada, ClaveDeIntentoEmitida>>(sp => sp.GetRequiredService<EmitirClaveDeIntento>());
        servicios.TryAddScoped<RegistrarIntento>();
        servicios.TryAddScoped<ExtenderReservaDelPago>();
        servicios.TryAddScoped<MarcarCompraAprobada>();
        servicios.TryAddScoped<ConfirmarReservasDeLaCompra>();
        servicios.TryAddScoped<ICompletadorDeCompra, CompletarCompra>();
        servicios.TryAddScoped<RegistrarReembolso>();
        servicios.TryAddScoped<ProcesarReembolso>();
        servicios.TryAddScoped<ICompensadorDeCompra, CompensarCompra>();
        servicios.TryAddScoped<AvisarACarrito>();
        servicios.TryAddScoped<IAvisadorACarrito>(sp => sp.GetRequiredService<AvisarACarrito>());
        servicios.TryAddScoped<IConfirmadorDeCompraAprobada, ConfirmarCompraAprobada>();
        servicios.TryAddScoped<IniciarCobro>();
        servicios.TryAddScoped<ICasoDeUso<CobrarEntrada, ResultadoDelCobroDelCliente>>(sp => sp.GetRequiredService<IniciarCobro>());
        servicios.TryAddScoped<ObtenerEstadoDelPago>();
        servicios.TryAddScoped<ICasoDeUso<Guid, EstadoDelPagoVista>>(sp => sp.GetRequiredService<ObtenerEstadoDelPago>());
        servicios.TryAddScoped<IPagos, PagosDelCliente>();
        servicios.TryAddScoped<ReintentarReembolso>();
        servicios.TryAddScoped<ListarReembolsosPendientes>();
        servicios.TryAddScoped<IReembolsos, ReembolsosDePagos>();
        servicios.TryAddScoped<IAvisosDeLaPasarela, RegistrarAvisoDeLaPasarela>();
        servicios.TryAddScoped<ILiberacionDeContactoDePago, LiberarContactoDeLaIntencion>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPendientesDeCuenta, ContarPendientesDeCuenta>());
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IAplicadorDeCancelacion, DesvincularPagosDeUnaCuenta>());

        // Pagos es la implementación real de lo que Carrito declara: reemplaza al doble provisional aunque ya lo haya registrado.
        servicios.Replace(ServiceDescriptor.Scoped<ICreadorDeIntencionesDePago, IntencionesDePagoDeLaAplicacion>());

        // ---------- Casos de uso de los trabajos de fondo (los corre ComposicionDeLosTrabajosDePagos) ----------
        servicios.TryAddScoped<ConsultarPagosPendientes>();
        servicios.TryAddScoped<ReanudarComprasAprobadas>();
        servicios.TryAddScoped<CerrarIntencionesVencidas>();
        servicios.TryAddScoped<ReintentarReembolsosPendientes>();
        servicios.TryAddScoped<EnviarAvisosDePago>();
        servicios.TryAddScoped<PurgarContactoDeIntenciones>();
        servicios.TryAddScoped<DepurarIntencionesAntiguas>();
        servicios.TryAddScoped<PurgarAvisosDePagos>();

        // ---------- Lo que otros módulos aún no entregan, con su guarda de producción ----------
        return servicios.AgregarDoblesProvisionalesDePagos(esProduccion);
    }
}

public static class ComposicionDeLosTrabajosDePagos
{
    /// <summary>
    /// Registra los nueve trabajos de fondo de Pagos y el servicio que los corre (T27). <paramref name="alcanceDeSistema"/> lo construye el anfitrión —la Api— con sus servicios y fija el actor «sistema» alrededor de cada corrida: la Infraestructura de Pagos no fija el actor.
    /// Se registra siempre (el singleton es perezoso), pero el servicio que los corre solo en modo normal (en <c>Restauracion</c> y <c>Ensayo</c> no corre ningún trabajo), y después de <c>AgregarPagos</c>; quien la llame añade también <c>AddHostedService&lt;ServicioDeTrabajosDePagos&gt;()</c>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeFondoDePagos(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistemaDePagos> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeFondoDePagos.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            new AlertasTecnicasPorCorrida(sp.GetRequiredService<IServiceScopeFactory>()),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDePagos(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDePagos>>()));
        return servicios;
    }
}
