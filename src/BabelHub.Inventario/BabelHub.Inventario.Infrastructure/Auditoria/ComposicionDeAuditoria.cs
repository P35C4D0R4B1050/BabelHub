using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Auditoria;

/// <summary>
/// Lo que el anfitrión da al módulo <c>Auditoria</c> y que no es suyo (T28): la conexión de depuración (otro rol de base de datos: la aplicación no es miembro del rol que ejecuta la función, T23) y las claves del cursor (de
/// <c>BABELHUB__Auditoria__ClaveDeCursor</c>, T25; distintas de las del E-commerce).
/// </summary>
public sealed record OpcionesDelModuloDeAuditoria(NpgsqlDataSource Depuracion, IClavesDeCursor ClavesDeCursor, LimitadorDeEscrituras? Limitador = null);

/// <summary>
/// El registro de dependencias del módulo <c>Auditoria</c> del Inventario (T28). El anfitrión pone lo que es de la plataforma y de otros módulos: <see cref="NpgsqlDataSource"/> (el rol de la aplicación), <see cref="IUnidadDeTrabajo"/> y
/// <see cref="IConexionDeLaUnidadDeTrabajo"/> (las registra <c>AgregarModuloFuentes</c>), <see cref="IActorActual"/> y <see cref="IEstablecedorDeActor"/>, <see cref="IOrigenDeSolicitud"/>, <see cref="IReloj"/> e
/// <see cref="IRegistroDeTrabajosDeFondo"/>; y, si existe, <see cref="IBajasRecibidas"/> (el almacén de las bajas del titular que la depuración anonimiza). Lo demás se registra aquí.
/// <para>
/// Todo lo que depende del actor o del origen de la solicitud va <b>Scoped</b>; los trabajos de fondo crean su propio alcance en cada corrida y fijan el actor «sistema» de forma explícita. El Inventario no tiene correo de alertas
/// (lo envía el E-commerce, que lee las alertas con la credencial de lectura), ni almacén aislado de Identidad.
/// </para>
/// </summary>
public static class ComposicionDeAuditoria
{
    public static IServiceCollection AgregarModuloAuditoria(this IServiceCollection servicios, OpcionesDelModuloDeAuditoria opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.TryAddSingleton(CatalogoDeAccionesDeAuditoria.Crear());
        servicios.TryAddSingleton(CatalogoDeAlertasDeAuditoria.Crear());
        servicios.TryAddSingleton(opciones.ClavesDeCursor);
        servicios.TryAddSingleton(sp => new FirmadorDeCursor(sp.GetRequiredService<IClavesDeCursor>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(opciones.Limitador ?? new LimitadorDeEscrituras());

        // Escrituras en transacción propia (fuera de la de la operación) y conexiones aparte.
        servicios.TryAddSingleton<IAlmacenDeEventosDeSeguridad>(sp => new PostgresAlmacenDeEventosDeSeguridad(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<LimitadorDeEscrituras>()));
        servicios.TryAddSingleton<IEscritorDeDenegaciones>(sp => new PostgresEscritorDeDenegaciones(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<LimitadorDeEscrituras>()));
        servicios.TryAddSingleton<IAcumuladorDeLimites>(sp => new PostgresAcumuladorDeLimites(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<LimitadorDeEscrituras>()));
        servicios.TryAddSingleton<IAnonimizadorDeRegistros>(new PostgresAnonimizadorDeRegistros(opciones.Depuracion));
        servicios.TryAddSingleton<IBloqueoDeLaDepuracion>(sp => new PostgresBloqueoDeLaDepuracion(sp.GetRequiredService<NpgsqlDataSource>()));

        // Almacenes dentro de la unidad de trabajo.
        servicios.TryAddScoped<IAlmacenDeRegistros, PostgresAlmacenDeRegistros>();
        servicios.TryAddScoped<IConsultaLocalDeRegistros, PostgresConsultaLocalDeRegistros>();
        servicios.TryAddScoped<IHistorialLocal, PostgresHistorialLocal>();
        servicios.TryAddScoped<IAlmacenDeAlertas, PostgresAlmacenDeAlertas>();
        servicios.TryAddScoped<IConteoLocalDeEventos, PostgresConteoLocalDeEventos>();
        servicios.TryAddScoped<IAcumuladoParaCerrar, PostgresAcumuladoParaCerrar>();
        servicios.TryAddScoped<IAlmacenDePorAnonimizar, PostgresAlmacenDePorAnonimizar>();
        servicios.TryAddScoped<IEstadoDeLaDepuracion, PostgresEstadoDeLaDepuracion>();

        // El contexto de denegación (Scoped: un evento por solicitud) y el resolutor de rutas; las plantillas de ruta las aporta la Api (IPlantillasDeRuta).
        servicios.TryAddScoped<IContextoDeDenegacion, ContextoDeDenegacion>();
        servicios.TryAddSingleton<ResolutorDeRutas>();

        // El constructor de registros y los puertos de los demás módulos (D15).
        servicios.TryAddScoped(sp => new ConstructorDeRegistros(
            ServicioDeOrigen.Inventario, sp.GetRequiredService<CatalogoDeAcciones>(), sp.GetRequiredService<IReloj>(), sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IOrigenDeSolicitud>()));
        servicios.TryAddScoped<IAuditoria, AuditoriaDeAplicacion>();
        servicios.TryAddScoped<IRegistroDeSeguridad, RegistroDeSeguridadDeAplicacion>();
        servicios.TryAddScoped<IPublicadorDeEventosDeSeguridad, PublicadorDeEventosDeSeguridad>();
        servicios.TryAddScoped<IContadorDeEventosDeSeguridad, ContadorDeEventosDeSeguridad>();
        servicios.TryAddScoped<IAuditoriaDeBajasRecibidas, AuditoriaDeBajasRecibidas>();
        servicios.TryAddScoped<IDenegacionDeAcceso>(sp => new DenegacionDeAccesoDeAplicacion(
            sp.GetRequiredService<ConstructorDeRegistros>(), sp.GetRequiredService<IContextoDeDenegacion>(), sp.GetRequiredService<ResolutorDeRutas>(), sp.GetRequiredService<IEscritorDeDenegaciones>(),
            sp.GetRequiredService<IPublicadorDeAlertasTecnicas>(), sp.GetRequiredService<IReloj>(), sp.GetService<IRutaDeLaSolicitud>()));

        // Las alertas: una sola implementación para los tres puertos.
        servicios.TryAddScoped(sp => new PublicadorDeAlertas(
            ServicioDeOrigen.Inventario, sp.GetRequiredService<CatalogoDeMotivosDeAlerta>(), sp.GetRequiredService<IAlmacenDeAlertas>(), sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped<IAlertas>(sp => sp.GetRequiredService<PublicadorDeAlertas>());
        servicios.TryAddScoped<IPublicadorDeAlertasTecnicas>(sp => sp.GetRequiredService<PublicadorDeAlertas>());
        servicios.TryAddScoped<IPublicadorDeAlertasDeSeguridad>(sp => sp.GetRequiredService<PublicadorDeAlertas>());

        // Las consultas locales: auditoría y alertas (solo administrador central o la integración de lectura), historial de la sucursal del token.
        servicios.TryAddScoped<IConsultasDeAuditoria, ConsultasDeAuditoriaLocales>();
        servicios.TryAddScoped<IConsultaDeAlertas, ConsultaDeAlertasLocales>();
        servicios.TryAddScoped<IHistorialDeSucursal, HistorialDeSucursalLocal>();

        // Trabajos de fondo (T27): cada corrida en su propio alcance y con el actor «sistema» fijado de forma explícita.
        servicios.TryAddScoped<CerrarVentanasDeLimite>(sp => new CerrarVentanasDeLimite(
            sp.GetRequiredService<IAcumuladoParaCerrar>(), sp.GetRequiredService<IAlmacenDeRegistros>(), sp.GetRequiredService<CatalogoDeAcciones>(), ServicioDeOrigen.Inventario,
            sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IAlertas>(), sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped<DepurarAuditoria>(sp => new DepurarAuditoria(
            sp.GetService<IBajasRecibidas>() is { } bajas ? [new BajasRecibidasComoFuente(bajas)] : [],
            sp.GetRequiredService<IAlmacenDePorAnonimizar>(), sp.GetRequiredService<IAnonimizadorDeRegistros>(), [.. sp.GetServices<IDepuradorAdicional>()], sp.GetRequiredService<IAuditoria>(),
            sp.GetRequiredService<IEstadoDeLaDepuracion>(), sp.GetRequiredService<IBloqueoDeLaDepuracion>(), sp.GetRequiredService<IAlertas>(), sp.GetRequiredService<IActorActual>(),
            sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IReloj>()));
        // El último paso de la restauración (AUD-31): vuelve a llenar por_anonimizar y depura antes de abrir al público.
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPasoDeRestauracion, PasoDeRestauracionDeDepuracion>());
        return servicios;
    }

    /// <summary>
    /// Registra el servicio hospedado que corre los dos trabajos del módulo (cierre de ventanas y depuración diaria). <b>Solo en el modo normal</b>: en restauración o ensayo no corre ningún trabajo (PLA-19); el anfitrión pasa
    /// <paramref name="soloEnModoNormal"/> con el resultado de <c>ModoDeArranque</c>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeAuditoria(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (!soloEnModoNormal)
        {
            return servicios;
        }

        servicios.AddHostedService(sp =>
        {
            var fabrica = sp.GetRequiredService<IServiceScopeFactory>();
            var registro = sp.GetRequiredService<IRegistroDeTrabajosDeFondo>();
            var actor = sp.GetRequiredService<IEstablecedorDeActor>();
            var alertas = new AlertasPorCorrida(fabrica);
            var reloj = sp.GetRequiredService<IReloj>();
            return new ServicioDeTrabajosDeAuditoria(
            [
                TrabajosDeAuditoria.Cierre(fabrica, registro, actor, alertas, reloj),
                TrabajosDeAuditoria.Depuracion(fabrica, registro, actor, alertas, reloj),
            ]);
        });
        return servicios;
    }
}

/// <summary>
/// <see cref="IAlertas"/> para los trabajos de fondo, que viven más que una solicitud: cada alerta se publica en su propio alcance (los almacenes y la unidad de trabajo son por alcance). Nunca interrumpe, como el publicador real.
/// </summary>
internal sealed class AlertasPorCorrida(IServiceScopeFactory fabrica) : IAlertas
{
    public async Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarTecnica(motivo, cancelacion).ConfigureAwait(false);
    }

    public async Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarDeSeguridad(motivo, cancelacion).ConfigureAwait(false);
    }
}
