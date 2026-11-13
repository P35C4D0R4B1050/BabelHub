using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18: dos esquemas de módulo (<c>identidad</c> y <c>pedidos</c>), un rol por esquema
/// sin login, un propietario de las tablas, un rol de depuración y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro
/// solo de los roles de esquema). Las migraciones de cada servicio crean esto mismo en producción.
/// </summary>
public sealed class PostgresDeRolesFixture : IAsyncLifetime
{
    private const string ClaveDeAcceso = "acceso-pruebas-0123456789";

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("postgres-pruebas")
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        const string preparacion = """
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE depuracion NOLOGIN;
            CREATE ROLE rol_identidad NOLOGIN;
            CREATE ROLE rol_pedidos NOLOGIN;
            CREATE ROLE rol_plataforma NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD 'acceso-pruebas-0123456789';
            GRANT rol_identidad, rol_pedidos, rol_plataforma TO acceso;

            CREATE SCHEMA identidad AUTHORIZATION propietario;
            CREATE SCHEMA pedidos AUTHORIZATION propietario;
            CREATE TABLE identidad.usuarios (id int PRIMARY KEY, correo text NOT NULL);
            CREATE TABLE pedidos.pedidos (id int PRIMARY KEY, cliente int NOT NULL);
            ALTER TABLE identidad.usuarios OWNER TO propietario;
            ALTER TABLE pedidos.pedidos OWNER TO propietario;

            GRANT USAGE ON SCHEMA identidad TO rol_identidad;
            GRANT SELECT, INSERT, UPDATE ON identidad.usuarios TO rol_identidad;
            GRANT USAGE ON SCHEMA pedidos TO rol_pedidos;
            GRANT SELECT, INSERT, UPDATE ON pedidos.pedidos TO rol_pedidos;
            INSERT INTO identidad.usuarios VALUES (1, 'lector@correo.pe');
            """;
        await using var comando = Administracion.CreateCommand(preparacion);
        await comando.ExecuteNonQueryAsync();

        // La tabla de eventos de límite del Inventario (plataforma.eventos_limite), con el permiso mínimo del rol del esquema.
        await using var eventos = Administracion.CreateCommand(
            BabelHub.Inventario.Infrastructure.Plataforma.DefinicionDeEventosDeLimite.Sql + """
            ALTER SCHEMA plataforma OWNER TO propietario;
            ALTER TABLE plataforma.eventos_limite OWNER TO propietario;
            GRANT USAGE ON SCHEMA plataforma TO rol_plataforma;
            GRANT SELECT, INSERT, DELETE ON plataforma.eventos_limite TO rol_plataforma;
            """);
        await eventos.ExecuteNonQueryAsync();

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Username = "acceso",
            Password = ClaveDeAcceso,
        };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
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

    public async Task<T?> Consultar<T>(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }
}
