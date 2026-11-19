using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>envios</c>: un propietario de las tablas y la función, el rol <c>rol_envios</c> sin login con el permiso mínimo
/// (<see cref="DefinicionDeEnvios.Permisos"/>) y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_envios</c>). Una sola instancia del contenedor para las clases de prueba.
/// </summary>
public sealed class PostgresDeEnviosFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-envios-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDeEnvios AccesoAlEsquema { get; private set; } = null!;

    internal RepositorioDeEnviosPostgres Envios { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_envios NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_envios TO acceso;
            """
            + DefinicionDeEnvios.Sql
            + """
            ALTER SCHEMA envios OWNER TO propietario;
            ALTER TABLE envios.envios OWNER TO propietario;
            ALTER TABLE envios.envio_lineas OWNER TO propietario;
            ALTER FUNCTION envios.proteger_la_guia_y_la_clave() OWNER TO propietario;
            """
            + DefinicionDeEnvios.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var clave = ClaveDeAcceso;
        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDeEnvios(Unidad, Unidad);
        Envios = new RepositorioDeEnviosPostgres(AccesoAlEsquema);
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
        return valor is null or DBNull ? default : (T)valor;
    }

    /// <summary>Vacía las tablas entre pruebas.</summary>
    public Task Limpiar() => Ejecutar("TRUNCATE envios.envio_lineas, envios.envios CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDeEnvios : ICollectionFixture<PostgresDeEnviosFixture>
{
    public const string Nombre = "Postgres de envios";
}
