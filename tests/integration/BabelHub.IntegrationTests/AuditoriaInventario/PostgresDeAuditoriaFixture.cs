using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Infrastructure.Auditoria;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// PostgreSQL real con la migración del módulo <c>Auditoria</c> del E-commerce (<see cref="DefinicionDeAuditoria.MigracionCompleta"/>) y la plantilla de registros inmutables reutilizada para
/// <c>identidad.registros</c>: el rol de acceso de la aplicación (<c>NOINHERIT</c>, miembro solo de los roles de esquema, que no es miembro de los propietarios ni del rol de depuración) y el
/// rol de depuración (que solo ejecuta la función). Las credenciales se generan distintas en cada ejecución: nunca un secreto fijo en el repositorio.
/// </summary>
public sealed class PostgresDeAuditoriaFixture : IAsyncLifetime
{
    private static readonly string ClaveDeAcceso = "acceso-" + Guid.NewGuid().ToString("N");
    private static readonly string ClaveDeDepuracion = "depura-" + Guid.NewGuid().ToString("N");

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("admin-" + Guid.NewGuid().ToString("N"))
        .Build();

    /// <summary>El superusuario del contenedor: aplica las migraciones y prepara los casos.</summary>
    public NpgsqlDataSource Administracion { get; private set; } = null!;

    /// <summary>La aplicación: el rol de acceso <c>NOINHERIT</c>; solo escribe con <c>SET LOCAL ROLE rol_auditoria</c> o <c>rol_identidad</c> dentro de una transacción.</summary>
    public NpgsqlDataSource Acceso { get; private set; } = null!;

    /// <summary>El rol de depuración: solo <c>EXECUTE</c> de la función.</summary>
    public NpgsqlDataSource Depuracion { get; private set; } = null!;

    public static CatalogoDeAcciones Catalogo { get; } = CatalogoDeAccionesDeAuditoria.Crear();

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Administracion = NpgsqlDataSource.Create(_contenedor.GetConnectionString());

        await Ejecutar(DefinicionDeAuditoria.MigracionCompleta(Catalogo));

        // Identidad: los mismos roles, disparadores y función sobre su propio esquema y su propia por_anonimizar (plan de Identidad T30).
        await Ejecutar("DO $r$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_identidad') THEN CREATE ROLE rol_identidad NOLOGIN; END IF; END $r$;");
        await Ejecutar(
            DefinicionDeAuditoria.RegistrosInmutables("identidad")
            + DefinicionDeAuditoria.PorAnonimizar("identidad")
            + DefinicionDeAuditoria.PermisosDeLaDepuracion("identidad")
            + "GRANT USAGE ON SCHEMA identidad TO rol_identidad;\nGRANT SELECT, INSERT ON identidad.registros TO rol_identidad;\nGRANT SELECT, INSERT ON identidad.por_anonimizar TO rol_identidad;\n"
            + DefinicionDeAuditoria.FuncionDeDepuracion("identidad", "identidad"));

        await Ejecutar($"""
            CREATE ROLE acceso LOGIN NOINHERIT PASSWORD '{ClaveDeAcceso}';
            GRANT rol_auditoria, rol_identidad TO acceso;
            ALTER ROLE auditoria_depuracion LOGIN PASSWORD '{ClaveDeDepuracion}';
            """);

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

    /// <summary>Ejecuta <paramref name="sql"/> como la aplicación (<c>acceso</c>) bajo el rol <paramref name="rol"/>, dentro de una transacción, como hace <c>EjecutarComo</c>.</summary>
    public async Task EjecutarComoLaAplicacion(string rol, string sql)
    {
        await using var conexion = await Acceso.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var cambio = new NpgsqlCommand($"SET LOCAL ROLE \"{rol}\"", conexion, transaccion))
        {
            await cambio.ExecuteNonQueryAsync();
        }

        await using (var comando = new NpgsqlCommand(sql, conexion, transaccion))
        {
            await comando.ExecuteNonQueryAsync();
        }

        await transaccion.CommitAsync();
    }

    /// <summary>Ejecuta <paramref name="sql"/> con la conexión de <paramref name="origen"/> y, si el rol de la sesión lo pide, bajo <paramref name="rol"/>; devuelve el error de PostgreSQL si lo hay.</summary>
    public static async Task<PostgresException?> IntentarComo(NpgsqlDataSource origen, string? rol, string sql)
    {
        await using var conexion = await origen.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        try
        {
            if (rol is not null)
            {
                await using var cambio = new NpgsqlCommand($"SET LOCAL ROLE \"{rol}\"", conexion, transaccion);
                await cambio.ExecuteNonQueryAsync();
            }

            await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
            await comando.ExecuteNonQueryAsync();
            await transaccion.CommitAsync();
            return null;
        }
        catch (PostgresException error)
        {
            return error;
        }
    }

    private string Conexion(string usuario, string clave) =>
        new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Username = usuario, Password = clave }.ConnectionString;
}
