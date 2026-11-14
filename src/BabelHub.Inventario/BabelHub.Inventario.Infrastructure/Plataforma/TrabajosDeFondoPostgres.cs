using System.Text.RegularExpressions;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// Definición de las tablas de <c>Plataforma</c> que registran el trabajo de fondo (<c>plataforma.trabajos_de_fondo</c>: la última
/// corrida de cada trabajo y su resultado enumerado) y el estado de los umbrales de alerta (<c>plataforma.estado_de_umbrales</c>), con
/// el permiso mínimo del rol del esquema; la migración las incorpora tal cual.
/// </summary>
public static class DefinicionDeTrabajosDeFondo
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS plataforma;
        CREATE TABLE IF NOT EXISTS plataforma.trabajos_de_fondo (
            nombre text PRIMARY KEY CHECK (nombre ~ '^[a-z][a-z0-9-]{0,99}$'),
            ultima_corrida timestamptz NOT NULL,
            resultado text NOT NULL CHECK (resultado IN ('exito', 'fallo'))
        );
        CREATE TABLE IF NOT EXISTS plataforma.estado_de_umbrales (
            motivo text PRIMARY KEY CHECK (motivo ~ '^[a-z][a-z0-9-]{0,99}$'),
            superado boolean NOT NULL,
            ultima_evaluacion timestamptz NOT NULL
        );
        """;

    /// <summary>El rol del esquema solo lee y escribe estas filas: sin <c>DELETE</c> ni <c>TRUNCATE</c> (el historial de corridas no se borra desde la aplicación).</summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA plataforma TO rol_plataforma;
        GRANT SELECT, INSERT, UPDATE ON plataforma.trabajos_de_fondo TO rol_plataforma;
        GRANT SELECT, INSERT, UPDATE ON plataforma.estado_de_umbrales TO rol_plataforma;
        """;
}

/// <summary>Acceso común: cada operación corre en su propia transacción como el rol del esquema <c>plataforma</c>.</summary>
internal static partial class EscrituraDePlataforma
{
    private static readonly EsquemaDeModulo Plataforma = EsquemaDeModulo.Crear("plataforma");

    /// <summary>Nombres estables en minúscula y con guiones: nunca texto libre, que podría arrastrar datos personales o internos.</summary>
    [GeneratedRegex("^[a-z][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex NombreEstable();

    public static string Validar(string valor, string parametro)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return NombreEstable().IsMatch(valor)
            ? valor
            : throw new ArgumentException("El nombre debe ser estable: minúsculas, dígitos y guiones, de hasta 100 caracteres.", parametro);
    }

    public static async Task<T> Ejecutar<T>(UnidadDeTrabajoPostgres unidad, Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Plataforma,
                    async interno => resultado = await bloque((NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion, interno).ConfigureAwait(false),
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }
}

/// <summary>
/// <see cref="IRegistroDeTrabajosDeFondo"/> sobre <c>plataforma.trabajos_de_fondo</c>: una fila por trabajo con su última corrida y
/// su resultado enumerado. Una corrida más vieja que la guardada no la pisa (dos instancias que registran fuera de orden).
/// </summary>
public sealed class RegistroDeTrabajosDeFondoPostgres(UnidadDeTrabajoPostgres unidad) : IRegistroDeTrabajosDeFondo
{
    public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
    {
        var nombre = EscrituraDePlataforma.Validar(trabajo, nameof(trabajo));
        return EscrituraDePlataforma.Ejecutar(
            unidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    INSERT INTO plataforma.trabajos_de_fondo (nombre, ultima_corrida, resultado)
                    VALUES (@nombre, @instante, @resultado)
                    ON CONFLICT (nombre) DO UPDATE
                        SET ultima_corrida = EXCLUDED.ultima_corrida, resultado = EXCLUDED.resultado
                        WHERE EXCLUDED.ultima_corrida >= plataforma.trabajos_de_fondo.ultima_corrida
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("nombre", nombre);
                comando.Parameters.AddWithValue("instante", instante.ToUniversalTime());
                comando.Parameters.AddWithValue("resultado", resultado == ResultadoDeTrabajo.Exito ? "exito" : "fallo");
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    /// <summary>La última corrida de un trabajo, o <c>null</c> si nunca corrió (para el estado de salud y las comprobaciones de atraso).</summary>
    public Task<(DateTimeOffset Instante, ResultadoDeTrabajo Resultado)?> UltimaCorrida(string trabajo, CancellationToken cancelacion = default)
    {
        var nombre = EscrituraDePlataforma.Validar(trabajo, nameof(trabajo));
        return EscrituraDePlataforma.Ejecutar<(DateTimeOffset, ResultadoDeTrabajo)?>(
            unidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT ultima_corrida, resultado FROM plataforma.trabajos_de_fondo WHERE nombre = @nombre", conexion, transaccion);
                comando.Parameters.AddWithValue("nombre", nombre);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false)
                    ? (new DateTimeOffset(filas.GetDateTime(0), TimeSpan.Zero), filas.GetString(1) == "exito" ? ResultadoDeTrabajo.Exito : ResultadoDeTrabajo.Fallo)
                    : null;
            },
            cancelacion);
    }
}

/// <summary>
/// <see cref="IEstadoDeUmbrales"/> sobre <c>plataforma.estado_de_umbrales</c>: tras un reinicio, un umbral que sigue superado no vuelve a
/// alertar (reemplaza al estado en memoria, que alertaba una vez más).
/// </summary>
public sealed class EstadoDeUmbralesPostgres(UnidadDeTrabajoPostgres unidad) : IEstadoDeUmbrales
{
    public Task<EstadoDeUmbral?> Obtener(string motivo, CancellationToken cancelacion)
    {
        var nombre = EscrituraDePlataforma.Validar(motivo, nameof(motivo));
        return EscrituraDePlataforma.Ejecutar<EstadoDeUmbral?>(
            unidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT superado, ultima_evaluacion FROM plataforma.estado_de_umbrales WHERE motivo = @motivo", conexion, transaccion);
                comando.Parameters.AddWithValue("motivo", nombre);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false)
                    ? new EstadoDeUmbral(filas.GetBoolean(0), new DateTimeOffset(filas.GetDateTime(1), TimeSpan.Zero))
                    : null;
            },
            cancelacion);
    }

    public Task Guardar(string motivo, EstadoDeUmbral estado, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(estado);
        var nombre = EscrituraDePlataforma.Validar(motivo, nameof(motivo));
        return EscrituraDePlataforma.Ejecutar(
            unidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    INSERT INTO plataforma.estado_de_umbrales (motivo, superado, ultima_evaluacion)
                    VALUES (@motivo, @superado, @instante)
                    ON CONFLICT (motivo) DO UPDATE
                        SET superado = EXCLUDED.superado, ultima_evaluacion = EXCLUDED.ultima_evaluacion
                        WHERE EXCLUDED.ultima_evaluacion >= plataforma.estado_de_umbrales.ultima_evaluacion
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("motivo", nombre);
                comando.Parameters.AddWithValue("superado", estado.Superado);
                comando.Parameters.AddWithValue("instante", estado.UltimaEvaluacion.ToUniversalTime());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }
}
