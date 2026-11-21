using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La composición del módulo Libro de Reclamaciones (T14; <c>ecommerce-libro-reclamaciones</c>): los repositorios de PostgreSQL bajo <c>EjecutarComo(reclamaciones)</c>, el anonimizador sobre la función de base de datos, <b>el correo verificado de la cuenta sobre el contrato público de Identidad</b>,
/// los casos de uso por alcance de solicitud, las implementaciones públicas (<see cref="IReclamaciones"/>, <see cref="IAdministracionDeReclamaciones"/> e <see cref="IHojasDelConsumidor"/>) y las partes de Reclamaciones de <see cref="IPendientesDeCuenta"/> y <see cref="IAplicadorDeCancelacion"/>.
/// <b>No registra dobles provisionales</b>: todo lo que necesita ya existe (el correo, la verificación de persona, los límites, la auditoría y la unidad de trabajo los entregan Plataforma, Auditoría e Identidad), y si falta algo el contenedor falla al resolverlo. Los trabajos de fondo los registra
/// <see cref="ComposicionDeLosTrabajosDeReclamaciones"/>. Necesita ya registrados: <see cref="IUnidadDeTrabajo"/>, <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>, <see cref="IDenegacionDeAcceso"/>, <see cref="IReloj"/>, <see cref="IControlDeLimites"/>,
/// <see cref="IExigenciaDeVerificacionDePersona"/>, <see cref="ICalculadorDeHuella"/>, <see cref="IEnviadorDeCorreo"/>, el auditor y el publicador de alertas técnicas de Auditoría, e <see cref="ICorreoVerificadoDeSistema"/> de Identidad (el correo verificado, contrato de sistema).
/// </summary>
public static class ComposicionDeReclamaciones
{
    public static IServiceCollection AgregarReclamaciones(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // ---------- Infraestructura (por solicitud: usa la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDeReclamaciones>();
        servicios.TryAddScoped<IRepositorioDeHojas, RepositorioDeHojasPostgres>();
        servicios.TryAddScoped<IAnonimizadorDeHojas, AnonimizadorDeHojasPostgres>();
        servicios.TryAddScoped<ICorreoVerificadoDeLaCuenta, CorreoVerificadoDeLaCuenta>();

        // ---------- Aplicación ----------
        servicios.TryAddScoped<AutorizadorDeReclamaciones>();
        servicios.TryAddScoped<LimiteDeConsultaDeHojas>();
        servicios.TryAddScoped<EnvioDelAvisoDeRespuesta>();
        servicios.TryAddScoped<RegistrarHoja>();
        servicios.TryAddScoped<ICasoDeUso<RegistrarHojaEntrada, ConstanciaDeHoja>>(sp => sp.GetRequiredService<RegistrarHoja>());
        servicios.TryAddScoped<ListarHojas>();
        servicios.TryAddScoped<ICasoDeUso<ListarHojasEntrada, PaginaDeHojas>>(sp => sp.GetRequiredService<ListarHojas>());
        servicios.TryAddScoped<ObtenerHojaParaAdministracion>();
        servicios.TryAddScoped<ICasoDeUso<ObtenerHojaEntrada, DetalleDeHoja>>(sp => sp.GetRequiredService<ObtenerHojaParaAdministracion>());
        servicios.TryAddScoped<ResponderHoja>();
        servicios.TryAddScoped<ICasoDeUso<ResponderHojaEntrada, ResultadoDeLaHoja>>(sp => sp.GetRequiredService<ResponderHoja>());
        servicios.TryAddScoped<DescartarHoja>();
        servicios.TryAddScoped<ICasoDeUso<DescartarHojaEntrada, ResultadoDeLaHoja>>(sp => sp.GetRequiredService<DescartarHoja>());
        servicios.TryAddScoped<ReintentarAvisoDeRespuesta>();
        servicios.TryAddScoped<ICasoDeUso<ReintentarAvisoEntrada, ResultadoDeLaHoja>>(sp => sp.GetRequiredService<ReintentarAvisoDeRespuesta>());
        servicios.TryAddScoped<ListarMisHojas>();
        servicios.TryAddScoped<ICasoDeUso<ListarMisHojasEntrada, PaginaDeMisHojas>>(sp => sp.GetRequiredService<ListarMisHojas>());
        servicios.TryAddScoped<ObtenerMiHoja>();
        servicios.TryAddScoped<ICasoDeUso<ObtenerMiHojaEntrada, MiHoja>>(sp => sp.GetRequiredService<ObtenerMiHoja>());

        // ---------- Contratos públicos y receptores ----------
        servicios.TryAddScoped<IReclamaciones, ReclamacionesDeLaAplicacion>();
        servicios.TryAddScoped<IAdministracionDeReclamaciones, AdministracionDeReclamacionesDeLaAplicacion>();
        servicios.TryAddScoped<IHojasDelConsumidor, HojasDelConsumidorDeLaAplicacion>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IPendientesDeCuenta, ContarHojasPendientesDeUnCorreo>());
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IAplicadorDeCancelacion, MarcarHojasDeUnaCuentaCancelada>());

        // ---------- Casos de uso de los trabajos de fondo (los corre ComposicionDeLosTrabajosDeReclamaciones) ----------
        servicios.TryAddScoped<AnonimizarHojasVencidas>();
        servicios.TryAddScoped<RevisarHojasSinAnonimizar>();
        return servicios;
    }
}

public static class ComposicionDeLosTrabajosDeReclamaciones
{
    /// <summary>
    /// Registra los dos trabajos de fondo de Reclamaciones y el servicio que los corre (T14). <paramref name="alcanceDeSistema"/> lo construye el anfitrión —la Api— con sus servicios y fija el actor «sistema» alrededor de cada corrida: la Infraestructura no fija el actor.
    /// Se llama después de <c>AgregarReclamaciones</c> y el servicio hospedado solo se añade en modo <c>Normal</c>; el anfitrión ya registró <see cref="IRegistroDeTrabajosDeFondo"/> e <see cref="IReloj"/>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeFondoDeReclamaciones(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistemaDeReclamaciones> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeFondoDeReclamaciones.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDeReclamaciones(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeReclamaciones>>()));
        return servicios;
    }
}
