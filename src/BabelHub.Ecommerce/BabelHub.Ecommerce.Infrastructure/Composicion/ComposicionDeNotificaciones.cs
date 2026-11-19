using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La composición real del módulo Notificaciones en el E-commerce (T12 a T16; <c>transversal-notificaciones</c>): los repositorios de PostgreSQL bajo <c>EjecutarComo(notificaciones)</c>, la huella de los números, el envío según el modo
/// (n8n, simulado o nulo), el cliente REST del contacto de una sucursal, los casos de uso con su contrato público y la entrada firmada de los estados de entrega. <b>Reemplaza</b> a los dobles provisionales (<c>INotificador</c> y
/// <c>ICancelacionDeAvisos</c>) aunque ya estén registrados: la implementación real nunca se ignora en silencio. Necesita ya registrados la unidad de trabajo y su conexión, <see cref="IActorActual"/>, <see cref="IReloj"/>,
/// <see cref="IControlDeLimites"/>, <see cref="ICalculadorDeHuella"/>, <see cref="IClienteDelInventario"/>, <see cref="IProcesadorDeEntradasDeIntegracion"/>, <see cref="IPublicadorDeEventosDeSeguridad"/> y de Auditoría
/// <c>IAuditoria</c>, <c>IAlertas</c> e <c>IDenegacionDeAcceso</c>; de Identidad, <see cref="IEstadoDeCuenta"/> e <see cref="IContactoDeAvisosDelCliente"/> (se resuelven al usarse, así que Identidad puede registrarse después).
/// </summary>
public static class ComposicionDeNotificaciones
{
    public static IServiceCollection AgregarNotificaciones(this IServiceCollection servicios, OpcionesDeNotificaciones opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        // ---------- Infraestructura ----------
        servicios.TryAddScoped<AccesoAlEsquemaDeNotificaciones>();
        servicios.TryAddScoped<IRepositorioDeAvisos, RepositorioDeAvisosPostgres>();
        servicios.TryAddScoped<IRepositorioDeNumerosDesactivados, RepositorioDeNumerosDesactivadosPostgres>();
        servicios.TryAddSingleton(opciones.Huella);
        servicios.TryAddSingleton<IHuellasDeNumeros>(sp => new CalculadorDeHuellaDeNumero(sp.GetRequiredService<OpcionesDeHuellaDeNumero>(), sp.GetRequiredService<ICalculadorDeHuella>()));
        servicios.TryAddScoped<IContactoDeSucursal, ClienteDeContactoDelInventario>();
        AgregarElEnvio(servicios, opciones);

        // ---------- Aplicación: el contrato público, que reemplaza a los dobles provisionales ----------
        servicios.TryAddScoped<TopeDeAvisosPorNumero>();
        servicios.Replace(ServiceDescriptor.Scoped<INotificador, SolicitarAviso>());
        servicios.Replace(ServiceDescriptor.Scoped<ICancelacionDeAvisos, CancelarAvisosPendientes>());
        servicios.TryAddScoped<IRecepcionDeAvisos, RecibirSolicitudDelInventario>();
        servicios.TryAddScoped<IRegistroDeEstadoDeEntrega, RegistrarEstadoDeEntrega>();
        servicios.TryAddScoped<IRecepcionDeEstadosDeEntrega, RecibirEstadoDeEntregaDeN8n>();
        servicios.TryAddScoped<IConsultaDeAvisos, ConsultarAvisos>();
        servicios.TryAddScoped<IDesactivacionDeNumeros, DesactivarAvisosDeUnNumero>();

        // ---------- Casos de uso de los trabajos de fondo (los corre AgregarTrabajosDeNotificaciones) ----------
        servicios.TryAddScoped<EnviarAvisosPendientes>();
        servicios.TryAddScoped<PurgarAvisosFinalizados>();

        // ---------- La entrada firmada de n8n: el proveedor entra a la lista blanca de las entradas de integración ----------
        if (opciones.Envio == ModoDelEnvioDeAvisos.N8n)
        {
            HabilitarElProveedor(servicios, RecibirEstadoDeEntregaDeN8n.Proveedor);
        }

        return servicios;
    }

    /// <summary>Los trabajos de fondo de Notificaciones (envío y purga) y el servicio que los corre; <paramref name="alcanceDeSistema"/> lo da el anfitrión, que fija el actor «sistema». Solo se llama en modo normal (PLA-19).</summary>
    public static IServiceCollection AgregarTrabajosDeNotificaciones(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistemaDeNotificaciones> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeNotificaciones.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            new AlertasTecnicasPorCorrida(sp.GetRequiredService<IServiceScopeFactory>()),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDeNotificaciones(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeNotificaciones>>()));
        return servicios;
    }

    private static void AgregarElEnvio(IServiceCollection servicios, OpcionesDeNotificaciones opciones)
    {
        switch (opciones.Envio)
        {
            case ModoDelEnvioDeAvisos.N8n:
                servicios.TryAddSingleton(opciones.N8n!);
                servicios.AddHttpClient(N8nEnvioDeWhatsApp.NombreDelCliente, cliente => cliente.Timeout = TimeSpan.FromSeconds(10))
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) });
                servicios.TryAddScoped<IEnvioDeWhatsApp>(sp => new N8nEnvioDeWhatsApp(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(N8nEnvioDeWhatsApp.NombreDelCliente),
                    sp.GetRequiredService<OpcionesDeEnvioPorN8n>(),
                    sp.GetRequiredService<IReloj>()));
                break;
            case ModoDelEnvioDeAvisos.Simulado:
                servicios.TryAddScoped<IEnvioDeWhatsApp, SimuladoEnvioDeWhatsApp>();
                break;
            default:
                servicios.TryAddScoped<IEnvioDeWhatsApp, NuloEnvioDeWhatsApp>();
                break;
        }
    }

    /// <summary>Suma el proveedor a la lista blanca de entradas de integración sin quitar los que otro módulo ya habilitó (por ejemplo, la pasarela de pago).</summary>
    private static void HabilitarElProveedor(IServiceCollection servicios, string proveedor)
    {
        var existente = servicios.LastOrDefault(d => d.ServiceType == typeof(OpcionesDeEntradasDeIntegracion))?.ImplementationInstance as OpcionesDeEntradasDeIntegracion;
        var proveedores = (existente?.ProveedoresPermitidos ?? new HashSet<string>()).Append(proveedor).Distinct(StringComparer.Ordinal).ToList();
        servicios.Replace(ServiceDescriptor.Singleton(new OpcionesDeEntradasDeIntegracion(proveedores, existente?.Tolerancia, existente?.TopeDelCuerpoEnBytes ?? 262_144)));
    }
}
