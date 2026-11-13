using System.Data.Common;
using BabelHub.Inventario.Application.Plataforma;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Opciones de la unidad de trabajo sobre PostgreSQL.</summary>
public sealed record OpcionesDeUnidadDeTrabajo
{
    /// <summary>Prefijo del rol de cada esquema: el rol del esquema <c>identidad</c> es <c>rol_identidad</c> (lo crean las migraciones, AD18).</summary>
    public string PrefijoDelRolDeEsquema { get; init; } = "rol_";
}

/// <summary>
/// Unidad de trabajo sobre PostgreSQL: una transacción por caso de uso (SEG-04) y, con <see cref="EjecutarComo"/>, la escritura de
/// un módulo bajo el rol de su esquema (AD18). La aplicación se conecta con un rol de acceso <c>NOINHERIT</c> miembro de los roles
/// de esquema: por sí solo no puede leer ningún esquema, y <c>SET LOCAL ROLE</c> le da el permiso de un módulo solo mientras
/// dure el bloque, así una operación entre módulos es una sola transacción sin ampliar el permiso de ninguno.
/// La conexión pertenece al flujo asíncrono que abrió la transacción: no debe usarse desde tareas paralelas.
/// </summary>
public sealed class UnidadDeTrabajoPostgres(NpgsqlDataSource origen, OpcionesDeUnidadDeTrabajo? opciones = null)
    : IUnidadDeTrabajo, IConexionDeLaUnidadDeTrabajo
{
    private const string TransaccionAbortada = "25P02";

    private readonly OpcionesDeUnidadDeTrabajo _opciones = opciones ?? new OpcionesDeUnidadDeTrabajo();
    private readonly AsyncLocal<Activa?> _activa = new();

    public bool HayTransaccion => _activa.Value is not null;

    public DbConnection Conexion => (_activa.Value ?? throw SinTransaccion()).Conexion;

    public DbTransaction Transaccion => (_activa.Value ?? throw SinTransaccion()).Transaccion;

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(trabajo);

        // Un caso de uso que llama a otro dentro de su transacción se une a ella: todo se confirma o todo se revierte.
        if (_activa.Value is not null)
        {
            return await trabajo(cancelacion).ConfigureAwait(false);
        }

        await using var conexion = await origen.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        await using var transaccion = await conexion.BeginTransactionAsync(cancelacion).ConfigureAwait(false);
        _activa.Value = new Activa(conexion, transaccion);
        try
        {
            var resultado = await trabajo(cancelacion).ConfigureAwait(false);
            await transaccion.CommitAsync(cancelacion).ConfigureAwait(false);
            return resultado;
        }
        catch
        {
            await RevertirSinFallar(transaccion).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _activa.Value = null;
        }
    }

    public async Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(esquema);
        ArgumentNullException.ThrowIfNull(bloque);

        var activa = _activa.Value ?? throw new InvalidOperationException("EjecutarComo exige una transacción activa.");
        var previo = (string)(await Escalar(activa, "SELECT current_user", cancelacion).ConfigureAwait(false))!;

        await EjecutarSql(activa, $"SET LOCAL ROLE {Citar(_opciones.PrefijoDelRolDeEsquema + esquema.Nombre)}", cancelacion).ConfigureAwait(false);
        try
        {
            await bloque(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            await RestaurarRol(activa, previo).ConfigureAwait(false);
        }
    }

    private static InvalidOperationException SinTransaccion() => new("No hay una unidad de trabajo activa.");

    /// <summary>PostgreSQL entre comillas dobles, para que ningún nombre de rol se interprete como SQL.</summary>
    private static string Citar(string identificador) => "\"" + identificador.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static async Task RestaurarRol(Activa activa, string previo)
    {
        try
        {
            // SET LOCAL ROLE <previo> y no RESET ROLE: un EjecutarComo anidado debe volver al rol del bloque que lo contiene.
            await EjecutarSql(activa, $"SET LOCAL ROLE {Citar(previo)}", CancellationToken.None).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == TransaccionAbortada)
        {
            // La transacción ya falló: el rol vuelve solo al revertirla, y no se enmascara el error original del bloque.
        }
    }

    private static async Task RevertirSinFallar(NpgsqlTransaction transaccion)
    {
        try
        {
            await transaccion.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            // Si la conexión ya se perdió, PostgreSQL revierte la transacción al cerrarse la sesión.
        }
    }

    private static async Task EjecutarSql(Activa activa, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, activa.Conexion, activa.Transaccion);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    private static async Task<object?> Escalar(Activa activa, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, activa.Conexion, activa.Transaccion);
        return await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false);
    }

    private sealed record Activa(NpgsqlConnection Conexion, NpgsqlTransaction Transaccion);
}
