using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Una escritura o lectura del esquema <c>pagos</c> que PostgreSQL rechazó. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c> ni el <c>Statement</c> de la excepción original, porque el
/// <c>Detail</c> de un <c>CHECK</c> o de un índice único incluye la fila completa («Failing row contains …») y con ella el nombre, el teléfono y la dirección de entrega (PAG-30, AGENTS §5). No guarda la excepción original como interna.
/// </summary>
public sealed class FallaDeLaBaseDePagosException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación de pagos (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>
/// Acceso común a las tablas del esquema <c>pagos</c>: cada operación corre en la unidad de trabajo en curso (o en una transacción corta propia, si no hay) bajo el rol <c>rol_pagos</c> (<c>EjecutarComo</c>, AD18). Usa solo los
/// contratos de <c>Plataforma</c> (regla 13). La conexión pertenece al flujo asíncrono que abrió la transacción: no se usa desde tareas paralelas. Todo error de PostgreSQL sale como <see cref="FallaDeLaBaseDePagosException"/>, sin los datos
/// de la fila; un <c>lock_timeout</c> vencido (<c>55P03</c>) sale como <see cref="TimeoutException"/>, que la interfaz traduce a «servicio no disponible» sin dejar nada a medias.
/// </summary>
public sealed class AccesoAlEsquemaDePagos(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    public const string ViolacionDeUnicidad = "23505";

    public const string BloqueoNoDisponible = "55P03";

    /// <summary>Lo que espera una lectura <c>FOR UPDATE</c> a que otra transacción suelte la fila: más que una consulta normal, menos que el tiempo de espera de la solicitud.</summary>
    public const string EsperaDelBloqueo = "5s";

    private static readonly EsquemaDeModulo Pagos = EsquemaDeModulo.Crear("pagos");

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Pagos,
                    async interno =>
                    {
                        try
                        {
                            resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false);
                        }
                        catch (PostgresException excepcion) when (excepcion.SqlState == BloqueoNoDisponible)
                        {
                            throw new TimeoutException("Otra operación mantiene bloqueada la fila del pago.");
                        }
                        catch (PostgresException excepcion)
                        {
                            // Se descarta la excepción original: su Detail puede traer la fila entera.
                            throw new FallaDeLaBaseDePagosException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
                        }
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }

    /// <summary>Fija la espera del bloqueo de la transacción en curso: se ejecuta antes de una lectura <c>FOR UPDATE</c>.</summary>
    public static async Task FijarEsperaDelBloqueo(NpgsqlConnection conexion, NpgsqlTransaction transaccion, CancellationToken token)
    {
        await using var comando = new NpgsqlCommand($"SET LOCAL lock_timeout = '{EsperaDelBloqueo}'", conexion, transaccion);
        await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>Un código de estado de unicidad (23505) con una de estas restricciones: lo que las traducciones de «ya existe» buscan.</summary>
    public static bool EsUnicidad(FallaDeLaBaseDePagosException falla, params string[] restricciones) =>
        falla.CodigoDeEstado == ViolacionDeUnicidad && falla.Restriccion is { } restriccion && restricciones.Contains(restriccion, StringComparer.Ordinal);

    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    /// <summary>Npgsql solo escribe instantes con desplazamiento 0: se normaliza a UTC.</summary>
    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static T? LeerNulable<T>(NpgsqlDataReader filas, int posicion)
        where T : struct => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<T>(posicion);

    public static string? LeerTexto(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetString(posicion);

    public static DateTimeOffset? LeerInstante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : new DateTimeOffset(filas.GetDateTime(posicion), TimeSpan.Zero);
}

/// <summary>
/// Los textos estables con que el esquema guarda las listas cerradas del dominio: el nombre del miembro en <c>snake_case</c> (<c>ConCobro</c> → <c>con_cobro</c>). Un texto desconocido no se adivina: falla, porque la base ya lo
/// rechaza con su <c>CHECK</c> y leer uno distinto significa que el esquema y el dominio se desfasaron.
/// </summary>
internal static class TextosDePagos
{
    public static string A<T>(T valor)
        where T : struct, Enum
    {
        var nombre = valor.ToString();
        var texto = new StringBuilder(nombre.Length + 4);
        for (var i = 0; i < nombre.Length; i++)
        {
            if (i > 0 && char.IsUpper(nombre[i]))
            {
                texto.Append('_');
            }

            texto.Append(char.ToLowerInvariant(nombre[i]));
        }

        return texto.ToString();
    }

    public static string? A<T>(T? valor)
        where T : struct, Enum => valor is { } v ? A(v) : null;

    public static T De<T>(string texto)
        where T : struct, Enum
    {
        foreach (var miembro in Enum.GetValues<T>())
        {
            if (string.Equals(A(miembro), texto, StringComparison.Ordinal))
            {
                return miembro;
            }
        }

        throw new InvalidOperationException($"El almacén de pagos trae un valor desconocido para {typeof(T).Name}.");
    }

    public static T? DeONulo<T>(string? texto)
        where T : struct, Enum => texto is null ? null : De<T>(texto);
}
