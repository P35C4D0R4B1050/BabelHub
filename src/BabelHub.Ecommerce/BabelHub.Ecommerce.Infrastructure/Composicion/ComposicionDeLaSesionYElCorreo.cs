using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La sesión de la web (COMP-1; web-base, identidad-seguridad T41): el cifrador de los secretos de sesión con la clave del host, los almacenes de sesiones y de borradores sobre PostgreSQL, el registro de seguridad de la web sobre
/// Auditoría, los casos de uso <see cref="SesionesWeb"/> y <see cref="BorradoresDePantalla"/>, la purga y el paso de restauración. Necesita ya registrados la unidad de trabajo, Identidad (<see cref="ISesionesDeIdentidad"/> y
/// <see cref="IVerificadorDeCredencial"/>), Auditoría (<see cref="IDenegacionDeAcceso"/>) y las alertas técnicas.
/// </summary>
public static class ComposicionDeLaSesionWeb
{
    public static IServiceCollection AgregarSesionWeb(this IServiceCollection servicios, ConfiguracionDeSesionesWeb claves)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(claves);

        servicios.TryAddSingleton<ICifradorDeSecretosDeSesion>(new AesGcmCifradorDeSecretosDeSesion(claves));
        servicios.TryAddSingleton<IAleatoriosSeguros, AleatoriosSeguros>();
        servicios.TryAddSingleton(typeof(HostDeSesion), _ => claves.Host);
        servicios.TryAddSingleton<LimitadorDeRenovaciones>();
        servicios.TryAddScoped<IAlmacenDeSesionesWeb, AlmacenDeSesionesWebPostgres>();
        servicios.TryAddScoped<IAlmacenDeBorradores, AlmacenDeBorradoresPostgres>();
        servicios.TryAddScoped<RegistroDeBloqueosPorTextosPendientes>();
        servicios.TryAddScoped<IRegistroDeSeguridadDeLaWeb, RegistroDeSeguridadDeLaWebEnAuditoria>();
        servicios.TryAddScoped<ISesionesWeb, SesionesWeb>();
        servicios.TryAddScoped<IBorradoresDePantalla, BorradoresDePantalla>();
        servicios.TryAddScoped<IVaciadoDeLaWeb, VaciadoDeLaWebPostgres>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPasoDeRestauracion, PasoDeRestauracionDeSesionesWeb>());
        servicios.TryAddScoped<PurgarSesionesWeb>();
        servicios.TryAddSingleton(sp => new TrabajoDePurgaDeSesionesWeb(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(), new AlertasTecnicasPorCorrida(sp.GetRequiredService<IServiceScopeFactory>()), sp.GetRequiredService<IReloj>()));

        // Las claves de Data Protection (antiforgery) viven cifradas en la base del host, no en el disco del contenedor (T12).
        servicios.AddDataProtection();
        servicios.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>().Configure<UnidadDeTrabajoPostgres, ICifradorDeSecretosDeSesion>(
            (o, unidad, cifrador) => o.XmlRepository = new RepositorioDeClavesDeProteccionPostgres(unidad, unidad, cifrador, claves.Host));
        return servicios;
    }
}

/// <summary>
/// El correo de la cuenta de envío (COMP-1; IDE-65, IDE-66, IDE-73, IDE-78): la cuota diaria con su política, la cola cifrada de correos pendientes sobre PostgreSQL, el transporte SMTP (sin él, solo fuera de producción, no sale
/// nada y cada envío es un fallo recuperable), el renderizador con las plantillas de seguridad y las de los módulos que las aportan, y el enviador de producción.
/// </summary>
public static class ComposicionDelCorreo
{
    public static IServiceCollection AgregarCorreoDelEcommerce(
        this IServiceCollection servicios,
        byte[] claveDeLaCola,
        OpcionesDelCorreoSmtp? smtp)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(claveDeLaCola);

        servicios.TryAddSingleton(new PoliticaDeCuotaDeCorreo(ConfiguracionDeCuotaDeCorreo.Predeterminada.Validar()));
        servicios.TryAddSingleton(new CifradorDeLaColaDeCorreo(claveDeLaCola));
        servicios.TryAddSingleton<IAlmacenDeCuotaDeCorreo>(sp => new PostgresAlmacenDeCuotaDeCorreo(sp.GetRequiredService<UnidadDeTrabajoPostgres>()));
        servicios.TryAddSingleton(sp => new PostgresColaDeCorreosPendientes(sp.GetRequiredService<UnidadDeTrabajoPostgres>(), sp.GetRequiredService<CifradorDeLaColaDeCorreo>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton<IColaDeCorreosPendientes>(sp => sp.GetRequiredService<PostgresColaDeCorreosPendientes>());
        servicios.TryAddSingleton<ITransporteDeCorreo>(smtp is null ? new TransporteDeCorreoNoConfigurado() : new SmtpTransporteDeCorreo(smtp));
        servicios.TryAddSingleton<IRenderizadorDeCorreo>(RenderizadorDeCorreoDeProduccion.Con([new Application.Pagos.PlantillasDeCorreoDePagosParaPlataforma(), new PlantillasDeCorreoDeReclamos()]));
        servicios.TryAddScoped(sp => new CuotaDeCorreo(sp.GetRequiredService<PoliticaDeCuotaDeCorreo>(), sp.GetRequiredService<IAlmacenDeCuotaDeCorreo>(), sp.GetRequiredService<IPublicadorDeAlertasTecnicas>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped<IEnviadorDeCorreo, EnviadorDeCorreoDeProduccion>();
        // Lo que corre en segundo plano (IDE-66, IDE-78, IDE-80): el reintento de lo pendiente al renovarse la cuota y el vaciado semanal de Enviados (solo con IMAP configurado).
        servicios.TryAddScoped<ReintentoDeCorreosPendientes>();
        if (smtp is not null)
        {
            servicios.TryAddSingleton(new LimpiezaDeEnviadosImap(smtp));
        }

        // El correo de las alertas a la administración (AUD-29, IDE-82): sin este registro el trabajo de correo de Auditoría no puede resolverse y ninguna alerta sale. Las direcciones las aporta Identidad (IDireccionesDeCuentas).
        servicios.TryAddScoped<IEnvioDeCorreoDeAlertas, EnvioDeCorreoDeAlertasDeProduccion>();
        return servicios;
    }
}

/// <summary><see cref="IPublicadorDeAlertasTecnicas"/> para un trabajo que vive más que una solicitud: cada alerta se publica en su propio alcance y nunca interrumpe.</summary>
public sealed class AlertasTecnicasPorCorrida(IServiceScopeFactory fabrica) : IPublicadorDeAlertasTecnicas
{
    public async Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        await using var alcance = fabrica.CreateAsyncScope();
        await alcance.ServiceProvider.GetRequiredService<IPublicadorDeAlertasTecnicas>().Publicar(alerta, cancelacion).ConfigureAwait(false);
    }
}
