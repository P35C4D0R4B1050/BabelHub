using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>biblioteca</c>: un propietario de las tablas, el rol <c>rol_biblioteca</c> sin login con el permiso mínimo (<see cref="DefinicionDeBiblioteca.Permisos"/>) y el rol de acceso de la aplicación
/// (<c>NOINHERIT</c>, miembro solo de <c>rol_biblioteca</c>). Una sola instancia del contenedor para las clases de prueba.
/// </summary>
public sealed class PostgresDeBibliotecaFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-biblioteca-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDeBiblioteca AccesoAlEsquema { get; private set; } = null!;

    internal RepositorioDeAccesosPostgres Accesos { get; private set; } = null!;

    internal RepositorioDeDescargasPostgres Descargas { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_biblioteca NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_biblioteca TO acceso;
            """
            + DefinicionDeBiblioteca.Sql
            + """
            ALTER SCHEMA biblioteca OWNER TO propietario;
            ALTER TABLE biblioteca.accesos OWNER TO propietario;
            ALTER TABLE biblioteca.descargas OWNER TO propietario;
            ALTER TABLE biblioteca.claves_de_descarga OWNER TO propietario;
            ALTER TABLE biblioteca.solicitudes_de_ayuda OWNER TO propietario;
            """
            + DefinicionDeBiblioteca.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = ClaveDeAcceso, MaxPoolSize = 100 };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDeBiblioteca(Unidad, Unidad);
        Accesos = new RepositorioDeAccesosPostgres(AccesoAlEsquema);
        Descargas = new RepositorioDeDescargasPostgres(AccesoAlEsquema);
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
    public Task Limpiar() => Ejecutar("TRUNCATE biblioteca.solicitudes_de_ayuda, biblioteca.claves_de_descarga, biblioteca.descargas, biblioteca.accesos CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDeBiblioteca : ICollectionFixture<PostgresDeBibliotecaFixture>
{
    public const string Nombre = "Postgres de biblioteca";
}
