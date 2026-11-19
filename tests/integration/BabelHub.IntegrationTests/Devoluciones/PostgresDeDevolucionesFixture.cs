using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>devoluciones</c>: un propietario de las tablas y las funciones, el rol <c>rol_devoluciones</c> sin login con el permiso mínimo
/// (<see cref="DefinicionDeDevoluciones.Permisos"/>) y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_devoluciones</c>). Una sola instancia del contenedor para las clases de prueba.
/// </summary>
public sealed class PostgresDeDevolucionesFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-devoluciones-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDeDevoluciones AccesoAlEsquema { get; private set; } = null!;

    internal RepositorioDeDevolucionesPostgres Devoluciones { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_devoluciones NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_devoluciones TO acceso;
            """
            + DefinicionDeDevoluciones.Sql
            + """
            ALTER SCHEMA devoluciones OWNER TO propietario;
            ALTER TABLE devoluciones.devoluciones OWNER TO propietario;
            ALTER TABLE devoluciones.lineas_de_devolucion OWNER TO propietario;
            ALTER TABLE devoluciones.cambios_de_linea OWNER TO propietario;
            ALTER TABLE devoluciones.cupos_de_linea OWNER TO propietario;
            ALTER FUNCTION devoluciones.proteger_la_devolucion() OWNER TO propietario;
            ALTER FUNCTION devoluciones.proteger_la_linea() OWNER TO propietario;
            ALTER FUNCTION devoluciones.proteger_el_cambio() OWNER TO propietario;
            """
            + DefinicionDeDevoluciones.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var clave = ClaveDeAcceso;
        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDeDevoluciones(Unidad, Unidad);
        Devoluciones = new RepositorioDeDevolucionesPostgres(AccesoAlEsquema);
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
    public Task Limpiar() => Ejecutar("TRUNCATE devoluciones.cambios_de_linea, devoluciones.lineas_de_devolucion, devoluciones.devoluciones, devoluciones.cupos_de_linea CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDeDevoluciones : ICollectionFixture<PostgresDeDevolucionesFixture>
{
    public const string Nombre = "Postgres de devoluciones";
}
