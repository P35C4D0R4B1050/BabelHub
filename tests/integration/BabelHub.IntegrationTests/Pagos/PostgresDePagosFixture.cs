using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// PostgreSQL real con la forma de permisos de AD18 para el esquema <c>pagos</c>: un propietario de las tablas, el rol <c>rol_pagos</c> sin login con el permiso mínimo (<see cref="DefinicionDePagos.Permisos"/>) y el rol de acceso de la
/// aplicación (<c>NOINHERIT</c>, miembro solo de <c>rol_pagos</c>). Una sola instancia del contenedor para las clases de prueba de los repositorios de Pagos.
/// </summary>
public sealed class PostgresDePagosFixture : IAsyncLifetime
{
    /// <summary>La contraseña del rol de acceso, distinta en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ClaveDeAcceso = "acceso-pagos-" + Guid.NewGuid().ToString("N");

    private static readonly string[] Tablas =
    [
        "intenciones", "intencion_lineas", "intencion_reserva_lineas", "intentos", "compensaciones", "reembolsos", "reembolso_lineas", "avisos_de_pago", "avisos_a_carrito",
    ];

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource Acceso { get; private set; } = null!;

    public NpgsqlDataSource Administracion { get; private set; } = null!;

    public UnidadDeTrabajoPostgres Unidad { get; private set; } = null!;

    public AccesoAlEsquemaDePagos AccesoAlEsquema { get; private set; } = null!;

    internal RepositorioDePagosPostgres Pagos { get; private set; } = null!;

    internal RepositorioDeReembolsosPostgres Reembolsos { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        var propiedad = string.Concat(Tablas.Select(t => $"ALTER TABLE pagos.{t} OWNER TO propietario;\n"));
        await using var preparacion = Administracion.CreateCommand(
            $"""
            CREATE ROLE propietario NOLOGIN;
            CREATE ROLE rol_pagos NOLOGIN;
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_pagos TO acceso;
            """
            + DefinicionDePagos.Sql
            + "ALTER SCHEMA pagos OWNER TO propietario;\n"
            + propiedad
            + DefinicionDePagos.Permisos);
        await preparacion.ExecuteNonQueryAsync();

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = "acceso", Password = ClaveDeAcceso };
        Acceso = NpgsqlDataSource.Create(cadena.ConnectionString);
        Unidad = new UnidadDeTrabajoPostgres(Acceso);
        AccesoAlEsquema = new AccesoAlEsquemaDePagos(Unidad, Unidad);
        Pagos = new RepositorioDePagosPostgres(AccesoAlEsquema);
        Reembolsos = new RepositorioDeReembolsosPostgres(AccesoAlEsquema);
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
    public Task Limpiar() => Ejecutar($"TRUNCATE {string.Join(", ", Tablas.Select(t => "pagos." + t))} CASCADE");
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionPostgresDePagos : ICollectionFixture<PostgresDePagosFixture>
{
    public const string Nombre = "Postgres de pagos";
}
