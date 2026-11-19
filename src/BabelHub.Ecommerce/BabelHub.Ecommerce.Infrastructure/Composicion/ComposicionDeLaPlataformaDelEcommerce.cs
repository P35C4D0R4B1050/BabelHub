using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// El Redis dedicado de contadores del E-commerce (SEG-26): la dirección <c>host:puerto</c>, el usuario ACL restringido <c>limites</c> y su contraseña (<c>BABELHUB__Seguridad__ClaveDeRedisDeLimites</c>); con TLS en el despliegue.
/// Sin él (solo fuera de producción) los límites usan el contador local por instancia.
/// </summary>
public sealed record OpcionesDelRedisDeLimites(string Direccion, ValorSecreto Clave, bool ConTls = false, string Usuario = "limites")
{
    public override string ToString() => "[configuración del Redis de límites]";
}

/// <summary>
/// La plataforma del E-commerce (COMP-1; SEG-05, SEG-15, SEG-26, SEG-32): la unidad de trabajo sobre PostgreSQL, el reloj y el actor, la huella, la matriz de alcances con las operaciones que declaran los módulos (rechazo por defecto),
/// el autorizador de las llamadas de servicio y el resolutor del origen, el catálogo de políticas y el control de límites (Redis con el motor local de respaldo para los generales; sin Redis, solo fuera de producción, el local). Vive en la
/// composición porque une piezas de Plataforma que ningún otro proyecto debe ver por dentro. Necesita ya registrados <see cref="IOrigenDeSolicitud"/> y <see cref="IPublicadorDeEventosDeSeguridad"/> (Auditoría).
/// </summary>
public static class ComposicionDeLaPlataformaDelEcommerce
{
    public static IServiceCollection AgregarPlataformaDelEcommerce(
        this IServiceCollection servicios,
        NpgsqlDataSource aplicacion,
        ConfiguracionDeSeguridad seguridad,
        IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> declaraciones,
        OpcionesDelRedisDeLimites? redis)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(aplicacion);
        ArgumentNullException.ThrowIfNull(seguridad);
        ArgumentNullException.ThrowIfNull(declaraciones);

        servicios.AddSingleton<IReloj, RelojDelSistema>();
        servicios.AddSingleton(aplicacion);
        servicios.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>()));
        servicios.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<IRegistroDeTrabajosDeFondo>(sp => new RegistroDeTrabajosDeFondoPostgres(sp.GetRequiredService<UnidadDeTrabajoPostgres>()));
        servicios.AddSingleton<ICalculadorDeHuella>(new CalculadorDeHuellaHmac(seguridad.Huella));
        servicios.AddSingleton(seguridad.CredencialesDeEntrada);
        servicios.AddSingleton(seguridad.CredencialesDeSalida);

        var matriz = new MatrizDeAlcances(declaraciones);
        servicios.AddSingleton<IMatrizDeAlcancesDeServicio>(matriz);
        servicios.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        servicios.AddScoped<IAutorizadorDeLlamadasDeServicio>(sp => new AutorizarLlamadaDeServicio(
            matriz, sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IOrigenDeSolicitud>(), sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IReloj>()));
        servicios.AddScoped<IResolutorDeOrigenDeServicio>(sp => new ResolverOrigenDeLlamadaDeServicio(
            sp.GetRequiredService<ICalculadorDeHuella>(), sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IReloj>()));

        var local = new MotorDeLimitesLocal();
        servicios.AddSingleton<IMotorDeLimites>(sp => redis is null ? local : new MotorDeLimitesRedis(ConectarRedis(redis), new OpcionesDeMotorRedis()));
        servicios.AddScoped<IControlDeLimites>(sp => new EvaluarLimite(
            sp.GetRequiredService<CatalogoDePoliticas>(), sp.GetRequiredService<IMotorDeLimites>(), local, sp.GetRequiredService<IOrigenDeSolicitud>(), sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IReloj>()));
        return servicios;
    }

    /// <summary>La conexión al Redis de contadores: sin reintentos infinitos al arrancar (un Redis caído es «no disponible» del motor, SEG-26, no un arranque bloqueado) y con el usuario restringido.</summary>
    private static IConnectionMultiplexer ConectarRedis(OpcionesDelRedisDeLimites redis)
    {
        var opciones = ConfigurationOptions.Parse(redis.Direccion);
        opciones.User = redis.Usuario;
        var clave = redis.Clave.Revelar();
        opciones.Password = clave;
        opciones.Ssl = redis.ConTls;
        opciones.AbortOnConnectFail = false;
        opciones.ConnectTimeout = 2000;
        opciones.SyncTimeout = 2000;
        return ConnectionMultiplexer.Connect(opciones);
    }
}
