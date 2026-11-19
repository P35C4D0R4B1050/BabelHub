using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// La seguridad entre servicios y los límites de uso del Inventario (COMP-1; SEG-05, SEG-15, SEG-26, SEG-32): la matriz de alcances con las operaciones que declaran los módulos (rechazo por defecto), el autorizador de las llamadas
/// de servicio y el resolutor del origen, el catálogo de políticas y el control de límites sobre PostgreSQL (los eventos viven en la transacción de quien reserva: si se revierte, no cuenta) con el local como respaldo para los
/// límites generales. Vive en la composición porque une piezas de Plataforma que ningún otro proyecto debe ver por dentro. Necesita ya registrados <see cref="IActorActual"/>, <see cref="IOrigenDeSolicitud"/>, <see cref="IReloj"/>,
/// <see cref="ICalculadorDeHuella"/>, <see cref="IPublicadorDeEventosDeSeguridad"/> y el <see cref="NpgsqlDataSource"/> del rol de acceso.
/// </summary>
public static class ComposicionDeLaSeguridadDelInventario
{
    public static IServiceCollection AgregarSeguridadYLimitesDelInventario(this IServiceCollection servicios, IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> declaraciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(declaraciones);

        var matriz = new MatrizDeAlcances(declaraciones);
        servicios.AddSingleton<IMatrizDeAlcancesDeServicio>(matriz);
        servicios.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        servicios.AddScoped<IAutorizadorDeLlamadasDeServicio>(sp => new AutorizarLlamadaDeServicio(
            matriz, sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IOrigenDeSolicitud>(), sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IReloj>()));
        servicios.AddScoped<IResolutorDeOrigenDeServicio>(sp => new ResolverOrigenDeLlamadaDeServicio(
            sp.GetRequiredService<ICalculadorDeHuella>(), sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IReloj>()));

        var local = new MotorDeLimitesLocal();
        servicios.AddScoped(sp => new MotorDeLimitesPostgres(sp.GetRequiredService<UnidadDeTrabajoPostgres>(), new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>(), actor: sp.GetRequiredService<IActorActual>())));
        servicios.AddScoped<ISustitutorDeSujetoDeLimites>(sp => sp.GetRequiredService<MotorDeLimitesPostgres>());
        servicios.AddScoped<IControlDeLimites>(sp => new EvaluarLimite(
            sp.GetRequiredService<CatalogoDePoliticas>(), sp.GetRequiredService<MotorDeLimitesPostgres>(), local, sp.GetRequiredService<IOrigenDeSolicitud>(), sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IReloj>()));
        return servicios;
    }
}
