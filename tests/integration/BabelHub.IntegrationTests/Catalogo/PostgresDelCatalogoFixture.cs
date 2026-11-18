using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>catalogo</c>: un propietario de las tablas y de las funciones, el rol <c>rol_catalogo</c> sin login con el permiso mínimo
/// (<see cref="DefinicionDelCatalogo.Permisos"/>) y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_catalogo</c>). Una sola instancia del contenedor para las clases de prueba del catálogo.
/// </summary>
public sealed class PostgresDelCatalogoFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-catalogo-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDelCatalogo AccesoAlEsquema { get; private set; } = null!;

    public RepositorioDeObrasPostgres Obras { get; private set; } = null!;

    public RepositorioDeReferenciasPostgres Referencias { get; private set; } = null!;

    public RepositorioDeIngresosPostgres Ingresos { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_catalogo NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_catalogo TO acceso;
            """
            + DefinicionDelCatalogo.Sql
            + """
            ALTER SCHEMA catalogo OWNER TO propietario;
            ALTER TABLE catalogo.obras OWNER TO propietario;
            ALTER TABLE catalogo.ediciones OWNER TO propietario;
            ALTER TABLE catalogo.licencias OWNER TO propietario;
            ALTER TABLE catalogo.archivos_digitales OWNER TO propietario;
            ALTER TABLE catalogo.archivos_pendientes OWNER TO propietario;
            ALTER TABLE catalogo.ingresos_solicitados OWNER TO propietario;
            ALTER FUNCTION catalogo.verificar_edicion() OWNER TO propietario;
            ALTER FUNCTION catalogo.normalizar(text) OWNER TO propietario;
            """
            + DefinicionDelCatalogo.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var clave = ClaveDeAcceso;
        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDelCatalogo(Unidad, Unidad);
        Obras = new RepositorioDeObrasPostgres(AccesoAlEsquema);
        Referencias = new RepositorioDeReferenciasPostgres(AccesoAlEsquema);
        Ingresos = new RepositorioDeIngresosPostgres(AccesoAlEsquema);
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
    public Task Limpiar() => Ejecutar("TRUNCATE catalogo.ingresos_solicitados, catalogo.archivos_pendientes, catalogo.archivos_digitales, catalogo.licencias, catalogo.ediciones, catalogo.obras CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDelCatalogo : ICollectionFixture<PostgresDelCatalogoFixture>
{
    public const string Nombre = "Postgres del catálogo";
}
