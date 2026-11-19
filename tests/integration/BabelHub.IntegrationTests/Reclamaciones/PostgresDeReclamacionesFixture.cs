using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// PostgreSQL real con la forma de permisos del esquema <c>reclamaciones</c>: el propietario <c>reclamaciones_dueno</c> (dueño de las tablas, los disparadores y la función; el rol de acceso no es miembro), el rol <c>rol_reclamaciones</c> sin login con el permiso mínimo y el rol de
/// acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_reclamaciones</c>). Una sola instancia del contenedor para las clases de prueba de reclamaciones.
/// </summary>
public sealed class PostgresDeReclamacionesFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-reclamaciones-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    internal RepositorioDeHojasPostgres Hojas { get; private set; } = null!;

    internal AnonimizadorDeHojasPostgres Anonimizador { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE reclamaciones_dueno NOLOGIN;
            CREATE ROLE rol_reclamaciones NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_reclamaciones TO acceso;
            """
            + DefinicionDeReclamaciones.Sql
            + DefinicionDeReclamaciones.Propiedad
            + DefinicionDeReclamaciones.FuncionDeAnonimizacion
            + DefinicionDeReclamaciones.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var clave = ClaveDeAcceso;
        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = clave };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        var esquema = new AccesoAlEsquemaDeReclamaciones(Unidad, Unidad);
        Hojas = new RepositorioDeHojasPostgres(esquema);
        Anonimizador = new AnonimizadorDeHojasPostgres(esquema);
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

    /// <summary>Vacía las tablas entre pruebas: el disparador prohíbe <c>TRUNCATE</c> y <c>DELETE</c> también al dueño, así que se desactiva solo aquí, como administrador de la prueba.</summary>
    public Task Limpiar() => Ejecutar(
        """
        ALTER TABLE reclamaciones.hojas_reclamacion DISABLE TRIGGER USER;
        ALTER TABLE reclamaciones.secuencias_de_hoja DISABLE TRIGGER USER;
        DELETE FROM reclamaciones.hojas_reclamacion;
        DELETE FROM reclamaciones.secuencias_de_hoja;
        DELETE FROM reclamaciones.corridas_de_anonimizacion;
        ALTER TABLE reclamaciones.hojas_reclamacion ENABLE TRIGGER USER;
        ALTER TABLE reclamaciones.secuencias_de_hoja ENABLE TRIGGER USER;
        """);
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDeReclamaciones : ICollectionFixture<PostgresDeReclamacionesFixture>
{
    public const string Nombre = "Postgres de reclamaciones";
}
