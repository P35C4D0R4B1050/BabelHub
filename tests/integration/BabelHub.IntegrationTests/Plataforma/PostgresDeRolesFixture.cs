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

    /// <summary>La contraseña del rol del latido, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDelLatido = "latido-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("postgres-pruebas")
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    /// <summary>La fuente de datos del rol <c>plataforma_latido</c>: el único que escribe el latido.</summary>
    public NpgsqlDataSource Latido { get; private set; } = null!;

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
            """ + BabelHub.Inventario.Infrastructure.Plataforma.DefinicionDeEventosDeLimite.Permisos);
        await eventos.ExecuteNonQueryAsync();

        await using var entradas = Administracion.CreateCommand(
            BabelHub.Ecommerce.Infrastructure.Plataforma.DefinicionDeEntradasProcesadas.Sql + """
            ALTER TABLE plataforma.entradas_procesadas OWNER TO propietario;
            GRANT SELECT, INSERT, UPDATE, DELETE ON plataforma.entradas_procesadas TO rol_plataforma;
            """);
        await entradas.ExecuteNonQueryAsync();

        // El registro de trabajos de fondo y el estado de los umbrales (plataforma.trabajos_de_fondo y plataforma.estado_de_umbrales).
        await using var trabajos = Administracion.CreateCommand(
            BabelHub.Ecommerce.Infrastructure.Plataforma.DefinicionDeTrabajosDeFondo.Sql + """
            ALTER TABLE plataforma.trabajos_de_fondo OWNER TO propietario;
            ALTER TABLE plataforma.estado_de_umbrales OWNER TO propietario;
            """ + BabelHub.Ecommerce.Infrastructure.Plataforma.DefinicionDeTrabajosDeFondo.Permisos);
        await trabajos.ExecuteNonQueryAsync();

        // El latido de la base (plataforma.latido) y su rol mínimo plataforma_latido, con su contraseña puesta aparte, como hace el despliegue.
        await using var latido = Administracion.CreateCommand(
            BabelHub.Ecommerce.Infrastructure.Plataforma.DefinicionDeLatido.Sql
            + "ALTER TABLE plataforma.latido OWNER TO propietario;\n"
            + BabelHub.Ecommerce.Infrastructure.Plataforma.DefinicionDeLatido.Rol
            + $"ALTER ROLE plataforma_latido PASSWORD '{ClaveDelLatido}';");
        await latido.ExecuteNonQueryAsync();

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Username = "acceso",
            Password = ClaveDeAcceso,
        };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);

        var cadenaDelLatido = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Username = "plataforma_latido",
            Password = ClaveDelLatido,
        };
        Latido = NpgsqlDataSource.Create(cadenaDelLatido.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (Acceso is not null)
        {
            await Acceso.DisposeAsync();
        }

        if (Latido is not null)
        {
            await Latido.DisposeAsync();
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
