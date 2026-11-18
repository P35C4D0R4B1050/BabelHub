using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// PostgreSQL real con los esquemas <c>auditoria</c> y <c>catalogo</c> en la misma base y con la forma de permisos de AD18: el rol de acceso de la aplicación (<c>NOINHERIT</c>) es miembro solo de <c>rol_auditoria</c>, <c>rol_identidad</c> y
/// <c>rol_catalogo</c>, de modo que el alta de una obra y su registro de auditoría ocurren en <b>la misma transacción</b> (T22). Las credenciales se generan distintas en cada ejecución.
/// </summary>
public sealed class PostgresDelCatalogoYAuditoriaFixture : IAsyncLifetime
{
    private static readonly string ClaveDeAcceso = "acceso-" + Guid.NewGuid().ToString("N");
    private static readonly string ClaveDeDepuracion = "depura-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Depuracion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        var catalogo = BabelHub.IntegrationTests.Auditoria.PostgresDeAuditoriaFixture.Catalogo;
        await Ejecutar(DefinicionDeAuditoria.MigracionCompleta(catalogo));
        await Ejecutar("DO $r$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_identidad') THEN CREATE ROLE rol_identidad NOLOGIN; END IF; END $r$;");
        await Ejecutar(
            "CREATE SCHEMA IF NOT EXISTS identidad; ALTER SCHEMA identidad OWNER TO auditoria_migraciones; "
            + DefinicionDeAuditoria.RegistrosInmutables("identidad")
            + DefinicionDeAuditoria.PorAnonimizar("identidad")
            + DefinicionDeAuditoria.PermisosDeLaDepuracion("identidad")
            + "GRANT USAGE ON SCHEMA identidad TO rol_identidad;\nGRANT SELECT, INSERT ON identidad.registros TO rol_identidad;\nGRANT SELECT, INSERT ON identidad.por_anonimizar TO rol_identidad;\n"
            + DefinicionDeAuditoria.FuncionDeDepuracion("identidad", "identidad"));

        await Ejecutar(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_catalogo NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_auditoria, rol_identidad, rol_catalogo TO acceso;
            ALTER ROLE auditoria_depuracion LOGIN PASSWORD '{ClaveDeDepuracion}';
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

        Acceso = NpgsqlDataSource.Create(Conexion("acceso", ClaveDeAcceso));
        Depuracion = NpgsqlDataSource.Create(Conexion("auditoria_depuracion", ClaveDeDepuracion));
    }

    public async Task DisposeAsync()
    {
        foreach (var origen in new[] { Acceso, Depuracion, Administracion })
        {
            if (origen is not null)
            {
                await origen.DisposeAsync();
            }
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

    /// <summary>Vacía las tablas del catálogo entre pruebas (la auditoría es inmutable y se conserva: cada prueba filtra por sus propios identificadores).</summary>
    public Task Limpiar() => Ejecutar("TRUNCATE catalogo.ingresos_solicitados, catalogo.archivos_pendientes, catalogo.archivos_digitales, catalogo.licencias, catalogo.ediciones, catalogo.obras CASCADE");

    private string Conexion(string usuario, string clave) =>
        new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = usuario, Password = clave }.ConnectionString;
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionDelCatalogoYAuditoria : ICollectionFixture<PostgresDelCatalogoYAuditoriaFixture>
{
    public const string Nombre = "Postgres del catálogo y la auditoría";
}
