using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>carrito</c>: un propietario de las tablas y las funciones, el rol <c>rol_carrito</c> sin login con el permiso mínimo
/// (<see cref="DefinicionDelCarrito.Permisos"/>) y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_carrito</c>). Una sola instancia del contenedor para las clases de prueba del carrito.
/// </summary>
public sealed class PostgresDelCarritoFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-carrito-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDelCarrito AccesoAlEsquema { get; private set; } = null!;

    public RepositorioDeCarritosPostgres Carritos { get; private set; } = null!;

    public RepositorioDeCheckoutsPostgres Checkouts { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_carrito NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_carrito TO acceso;
            """
            + DefinicionDelCarrito.Sql
            + """
            ALTER SCHEMA carrito OWNER TO propietario;
            ALTER TABLE carrito.carritos OWNER TO propietario;
            ALTER TABLE carrito.lineas_de_carrito OWNER TO propietario;
            ALTER TABLE carrito.avisos_de_carrito OWNER TO propietario;
            ALTER TABLE carrito.checkouts OWNER TO propietario;
            ALTER TABLE carrito.lineas_del_checkout OWNER TO propietario;
            ALTER TABLE carrito.fusiones_aplicadas OWNER TO propietario;
            ALTER FUNCTION carrito.rechazar_linea_en_carrito_fusionado() OWNER TO propietario;
            """
            + DefinicionDelCarrito.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = ClaveDeAcceso };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDelCarrito(Unidad, Unidad);
        Carritos = new RepositorioDeCarritosPostgres(AccesoAlEsquema);
        Checkouts = new RepositorioDeCheckoutsPostgres(AccesoAlEsquema);
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
    public Task Limpiar() => Ejecutar("TRUNCATE carrito.fusiones_aplicadas, carrito.lineas_del_checkout, carrito.checkouts, carrito.avisos_de_carrito, carrito.lineas_de_carrito, carrito.carritos CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDelCarrito : ICollectionFixture<PostgresDelCarritoFixture>
{
    public const string Nombre = "Postgres del carrito";
}
