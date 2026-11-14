using BabelHub.Ecommerce.Infrastructure.Web;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>web</c>: un propietario de las tablas, el rol <c>rol_web</c> sin login con el permiso mínimo
/// (<see cref="DefinicionDeLaWeb.Permisos"/>) y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_web</c>): por sí solo no lee nada, y
/// <c>EjecutarComo(web)</c> le da el permiso del módulo mientras dure el bloque. Se comparte entre las clases de prueba de la web (una sola instancia del contenedor).
/// </summary>
public sealed class PostgresDeLaWebFixture : IAsyncLifetime
{
    private const string ClaveDeAcceso = "acceso-web-pruebas-0123456789";

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("postgres-pruebas")
        .Build();

    /// <summary>La aplicación: rol de acceso <c>NOINHERIT</c>, solo miembro de <c>rol_web</c>.</summary>
    public NpgsqlDataSource Acceso { get; private set; } = null!;

    /// <summary>El superusuario del contenedor, para preparar y para inspeccionar lo que quedó en las tablas.</summary>
    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_web NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_web TO acceso;
            """
            + DefinicionDeLaWeb.Sql
            + """
            ALTER SCHEMA web OWNER TO propietario;
            ALTER TABLE web.sesiones_web OWNER TO propietario;
            ALTER TABLE web.borradores OWNER TO propietario;
            ALTER TABLE web.claves_de_proteccion OWNER TO propietario;
            """
            + DefinicionDeLaWeb.Permisos);
        await preparacion.ExecuteNonQueryAsync();

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

    public async Task Ejecutar(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Vacía las tablas entre pruebas.</summary>
    public Task Limpiar() => Ejecutar("TRUNCATE web.sesiones_web, web.borradores, web.claves_de_proteccion");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDeLaWeb : ICollectionFixture<PostgresDeLaWebFixture>
{
    public const string Nombre = "Postgres de la web";
}
