using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>
/// Una escritura o lectura del esquema <c>pedidos</c> que PostgreSQL rechazó por una razón que no es la disponibilidad. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c>, el <c>MessageText</c> ni el <c>Statement</c> de la excepción original, porque
/// un <c>Detail</c> incluye la fila completa y en <c>pedidos.pedidos</c> está el <b>contacto de entrega</b> (PED-40).
/// </summary>
public sealed class FallaDeLaBaseDePedidosException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación de pedidos (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>La base no respondió a tiempo (conexión caída, interbloqueo, serialización o consulta cancelada): el caso de uso responde «servicio no disponible» y es reintentable.</summary>
public sealed class PedidosNoDisponiblesException(string motivo) : Exception($"La base de pedidos no está disponible ({motivo}).");

/// <summary>
/// Acceso común a las tablas del esquema <c>pedidos</c>: cada operación corre en la unidad de trabajo en curso bajo el rol <c>rol_pedidos</c> (<c>EjecutarComo</c>, AD18). Un <c>lock_timeout</c> (55P03) sale como <see cref="EsperaDeBloqueoDeUnPedidoAgotadaException"/> (el caso de uso responde <c>conflicto</c>); un interbloqueo, una
/// serialización, una consulta cancelada o una conexión caída salen como <see cref="PedidosNoDisponiblesException"/>; cualquier otro rechazo, como <see cref="FallaDeLaBaseDePedidosException"/> sin el detalle de la fila.
/// </summary>
public sealed class AccesoAlEsquemaDePedidos(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    public const string ViolacionDeUnicidad = "23505";

    private static readonly EsquemaDeModulo Pedidos = EsquemaDeModulo.Crear("pedidos");

    private static bool EsDisponibilidad(string? estado) => estado is "40001" or "40P01" or "57014" || estado?.StartsWith("08", StringComparison.Ordinal) == true;

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    await unidad.EjecutarComo(
                        Pedidos,
                        async interno =>
                        {
                            resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false);
                        },
                        token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (PostgresException excepcion)
        {
            // Se descarta la excepción original: su Detail puede traer la fila entera, contacto incluido.
            if (excepcion.SqlState == "55P03")
            {
                throw new EsperaDeBloqueoDeUnPedidoAgotadaException();
            }

            if (EsDisponibilidad(excepcion.SqlState))
            {
                throw new PedidosNoDisponiblesException("base-no-disponible");
            }

            // Una unicidad de las operaciones en curso es la segunda defensa de PED-21: la operación que llegó segunda se rechaza.
            if (excepcion.SqlState == ViolacionDeUnicidad && excepcion.ConstraintName is "anulacion_lineas_una_en_curso" or "entrega_lineas_una_en_curso" or "anulaciones_una_en_curso_por_pedido")
            {
                throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "pedido-operacion-en-curso"));
            }

            throw new FallaDeLaBaseDePedidosException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
        }
        catch (NpgsqlException)
        {
            throw new PedidosNoDisponiblesException("base-no-disponible");
        }

        return resultado;
    }

    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    public static DateTimeOffset? LeerInstante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<DateTimeOffset>(posicion);

    public static Guid? LeerGuid(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetGuid(posicion);

    public static string? LeerTexto(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetString(posicion);
}
