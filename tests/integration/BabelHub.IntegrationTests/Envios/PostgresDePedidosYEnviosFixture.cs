using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>
/// Un solo PostgreSQL real con los esquemas <c>pedidos</c> y <c>envios</c> y la forma de permisos de AD18 (un propietario, un rol por esquema sin login y un rol de acceso <c>NOINHERIT</c> miembro de los dos): sirve a las pruebas que ejercen el <b>Pedidos real</b> junto con el
/// <b>Envíos real</b> en una misma transacción (T20, T22).
/// </summary>
public sealed class PostgresDePedidosYEnviosFixture : IAsyncLifetime
{
    private static readonly string ClaveDeAcceso = "acceso-pedidos-envios-" + Guid.NewGuid().ToString("N");

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
            CREATE ROLE rol_envios NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_pedidos TO acceso;
            GRANT rol_envios TO acceso;
            """
            + DefinicionDePedidos.Sql
            + DefinicionDeEnvios.Sql
            + """
            ALTER SCHEMA pedidos OWNER TO propietario;
            ALTER SCHEMA envios OWNER TO propietario;
            DO $d$ DECLARE t text; BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'pedidos' LOOP EXECUTE format('ALTER TABLE pedidos.%I OWNER TO propietario', t); END LOOP;
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'envios' LOOP EXECUTE format('ALTER TABLE envios.%I OWNER TO propietario', t); END LOOP;
            END $d$;
            ALTER FUNCTION envios.proteger_la_guia_y_la_clave() OWNER TO propietario;
            """
            + DefinicionDePedidos.Permisos
            + DefinicionDeEnvios.Permisos);
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

    public async Task<T?> Consultar<T>(string sql)
    {
        await using var comando = Administracion.CreateCommand(sql);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }

    public async Task Limpiar()
    {
        await using var comando = Administracion.CreateCommand("TRUNCATE envios.envio_lineas, envios.envios CASCADE; DO $d$ DECLARE t text; BEGIN FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'pedidos' LOOP EXECUTE format('TRUNCATE pedidos.%I CASCADE', t); END LOOP; END $d$;");
        await comando.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDePedidosYEnvios : ICollectionFixture<PostgresDePedidosYEnviosFixture>
{
    public const string Nombre = "Postgres de pedidos y envios";
}
