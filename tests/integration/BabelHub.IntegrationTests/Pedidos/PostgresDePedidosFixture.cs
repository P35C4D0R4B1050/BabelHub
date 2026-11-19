using BabelHub.Ecommerce.Infrastructure.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>pedidos</c>: un propietario de las tablas, el rol <c>rol_pedidos</c> sin login con el permiso mínimo (<see cref="DefinicionDePedidos.Permisos"/>) y el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_pedidos</c>).
/// Una sola instancia del contenedor para las clases de la colección.
/// </summary>
public sealed class PostgresDePedidosFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-pedidos-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    internal RepositorioDePedidosPostgres Pedidos { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_pedidos NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_pedidos TO acceso;
            """
            + DefinicionDePedidos.Sql
            + """
            ALTER SCHEMA pedidos OWNER TO propietario;
            DO $d$ DECLARE t text; BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'pedidos' LOOP EXECUTE format('ALTER TABLE pedidos.%I OWNER TO propietario', t); END LOOP;
            END $d$;
            """
            + DefinicionDePedidos.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var clave = ClaveDeAcceso;
        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        Pedidos = new RepositorioDePedidosPostgres(new AccesoAlEsquemaDePedidos(Unidad, Unidad));
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
    public Task Limpiar() => Ejecutar("TRUNCATE pedidos.depuraciones_de_pedidos, pedidos.liberaciones_de_contacto, pedidos.estados_de_traslado_recibidos, pedidos.liberaciones_de_reserva, pedidos.entrega_lineas, pedidos.entregas, pedidos.anulacion_lineas, pedidos.anulaciones, pedidos.lineas_de_pedido, pedidos.pedidos CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDePedidos : ICollectionFixture<PostgresDePedidosFixture>
{
    public const string Nombre = "Postgres de pedidos";
}
