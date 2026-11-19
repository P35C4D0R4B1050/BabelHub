using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// PostgreSQL real con la forma de permisos del esquema <c>notificaciones</c>: el rol <c>rol_notificaciones</c> y una cuenta de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de ese rol), de modo que cada operación
/// pase por <c>EjecutarComo(notificaciones)</c> como en producción. Incluye una tabla ajena <c>operaciones_de_prueba</c> que escribe «el módulo que pidió el aviso» en su misma transacción.
/// </summary>
public sealed class PostgresDeNotificacionesFixture : IAsyncLifetime
{
    /// <summary>La contraseña de la cuenta de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-notificaciones-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    internal AccesoAlEsquemaDeNotificaciones Esquema { get; private set; } = null!;

    internal RepositorioDeAvisosPostgres Avisos { get; private set; } = null!;

    internal RepositorioDeNumerosDesactivadosPostgres Numeros { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE rol_notificaciones NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_notificaciones TO acceso;
            CREATE TABLE public.operaciones_de_prueba (id integer PRIMARY KEY);
            GRANT INSERT, SELECT ON public.operaciones_de_prueba TO acceso;
            """
            + DefinicionDeNotificaciones.Sql
            + DefinicionDeNotificaciones.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = ClaveDeAcceso, MaxPoolSize = 40 };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        Esquema = new AccesoAlEsquemaDeNotificaciones(Unidad, Unidad);
        Avisos = new RepositorioDeAvisosPostgres(Esquema);
        Numeros = new RepositorioDeNumerosDesactivadosPostgres(Esquema);
    }

    public async Task DisposeAsync()
    {
        if (Acceso is not null)
        {
            await Acceso.DisposeAsync();
        }

        if (Administracion is not null)
        {
            await Administracion.DisposeAsync();
        }

        await _contenedor.DisposeAsync();
    }

    public async Task Ejecutar(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<T?> Consultar<T>(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)Convert.ChangeType(valor, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task Limpiar() => Ejecutar("DELETE FROM notificaciones.avisos; DELETE FROM notificaciones.numeros_desactivados; DELETE FROM public.operaciones_de_prueba;");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDeNotificaciones : ICollectionFixture<PostgresDeNotificacionesFixture>
{
    public const string Nombre = "Postgres de notificaciones";
}
