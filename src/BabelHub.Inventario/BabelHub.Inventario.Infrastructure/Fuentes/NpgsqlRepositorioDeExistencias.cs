using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// <see cref="IRepositorioDeExistencias"/> sobre PostgreSQL (D01, D04, D09): la tabla <c>stock</c> es de este módulo y su <b>única escritura</b> es un <c>UPDATE</c> condicional que espeja la
/// regla del Dominio (<c>0 ≤ reservado ≤ físico</c>) y devuelve los valores anterior y posterior de la misma fila; el <c>CHECK</c> de la tabla es la última defensa. SQL explícito y siempre
/// con parámetros. Las filas se garantizan y se bloquean <b>en orden (edición, fuente)</b>, el mismo de <c>OrdenDeBloqueo</c> en la Aplicación, de modo que dos transacciones que piden las mismas
/// ediciones en orden contrario no se interbloquean. <c>Aplicar</c> corre con el alcance del actor; si no cambia ninguna fila se distingue la falta de stock de la denegación por la política de filas
/// (primero con el alcance del actor y luego, con elevación, solo si la fila existe: un booleano).
/// </summary>
internal sealed class NpgsqlRepositorioDeExistencias(AccesoAFuentes acceso) : IRepositorioDeExistencias
{
    private const string Impreso = "impreso";

    private const string SentenciaDeMovimiento = """
        UPDATE fuentes.stock AS s
        SET fisico = s.fisico + @df, reservado = s.reservado + @dr, version = s.version + 1
        FROM (SELECT fuente_id, edicion_id, fisico, reservado FROM fuentes.stock WHERE fuente_id = @f AND edicion_id = @e FOR UPDATE) AS a
        WHERE s.fuente_id = a.fuente_id AND s.edicion_id = a.edicion_id
          AND s.fisico::bigint + @df >= s.reservado::bigint + @dr
          AND s.reservado::bigint + @dr >= 0
          AND s.fisico::bigint + @df >= 0
          AND s.fisico::bigint + @df <= 2147483647
        RETURNING a.fisico, a.reservado, s.fisico, s.reservado
        """;

    public Task GarantizarFilas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        return acceso.ConAlcanceDeRed(
            async (conexion, transaccion, token) =>
            {
                // En orden (edición, fuente) y solo para ediciones impresas (regla 12); lo que ya existe no se toca.
                await using var comando = new NpgsqlCommand(
                    """
                    INSERT INTO fuentes.stock (fuente_id, edicion_id, formato, fisico, reservado, version)
                    SELECT f.id, e.edicion_id, 'impreso', 0, 0, 0
                    FROM fuentes.ediciones e CROSS JOIN fuentes.fuentes f
                    WHERE e.edicion_id = ANY(@ids) AND e.formato = 'impreso'
                    ORDER BY e.edicion_id, f.id
                    ON CONFLICT (fuente_id, edicion_id) DO NOTHING
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ids", edicionIds.ToArray());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<FilaDeStock>> BloquearFilas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        return acceso.ConAlcanceDeRed(
            (conexion, transaccion, token) => LeerFilas(
                conexion,
                transaccion,
                "SELECT fuente_id, edicion_id, fisico, reservado FROM fuentes.stock WHERE edicion_id = ANY(@ids) ORDER BY edicion_id, fuente_id FOR UPDATE",
                [("ids", edicionIds.ToArray())],
                token),
            cancelacion);
    }

    public Task<ResultadoDeAplicacion> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(movimiento);

        return acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                var resultado = await Mover(conexion, transaccion, movimiento, token).ConfigureAwait(false);
                if (resultado is not null)
                {
                    return resultado;
                }

                // No cambió ninguna fila. ¿Falta de stock, o la política de filas oculta la fila a este actor?
                if (await Existe(conexion, transaccion, movimiento, token).ConfigureAwait(false))
                {
                    return ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.NoAplicable);
                }

                var existeParaLaRed = await acceso.ElevarDentro(t => Existe(conexion, transaccion, movimiento, t), token).ConfigureAwait(false);
                if (existeParaLaRed)
                {
                    return ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.DeniegadoPorAlcance);
                }

                // No hay fila: si la fuente existe y la edición es impresa es solo que nadie la garantizó (stock cero); si no, no hay stock que cambiar.
                return await ParejaConStockPosible(conexion, transaccion, movimiento, token).ConfigureAwait(false)
                    ? ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.NoAplicable)
                    : ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.EdicionInexistenteONoImpresa);
            },
            cancelacion);
    }

    public Task<ResultadoDeAplicacion> AplicarLiberacionDeVencidas(MovimientoDeStock movimiento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(movimiento);

        return acceso.ConAlcanceDeRed(
            async (conexion, transaccion, token) =>
                await Mover(conexion, transaccion, movimiento, token).ConfigureAwait(false) ?? ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.NoAplicable),
            cancelacion);
    }

    public Task<IReadOnlyList<FilaDeStock>> Leer(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        return acceso.ConAlcanceDeRed(
            (conexion, transaccion, token) => LeerFilas(
                conexion,
                transaccion,
                "SELECT fuente_id, edicion_id, fisico, reservado FROM fuentes.stock WHERE edicion_id = ANY(@ids) ORDER BY edicion_id, fuente_id",
                [("ids", edicionIds.ToArray())],
                token),
            cancelacion);
    }

    public Task<IReadOnlyList<FilaDeStock>> LeerDeLaFuente(Guid fuenteId, IReadOnlyCollection<Guid>? edicionIds, CancellationToken cancelacion = default)
    {
        // Una lista vacía es «ninguna edición»: nunca se traduce a «sin filtro» (una búsqueda sin coincidencias no devuelve todo el inventario).
        if (edicionIds is { Count: 0 })
        {
            return Task.FromResult<IReadOnlyList<FilaDeStock>>([]);
        }

        // Con el alcance del actor, no elevado: la política de filas es la segunda capa de FUE-08.
        return acceso.ConAlcanceDelActor(
            (conexion, transaccion, token) => edicionIds is null
                ? LeerFilas(
                    conexion,
                    transaccion,
                    "SELECT fuente_id, edicion_id, fisico, reservado FROM fuentes.stock WHERE fuente_id = @f ORDER BY edicion_id",
                    [("f", fuenteId)],
                    token)
                : LeerFilas(
                    conexion,
                    transaccion,
                    "SELECT fuente_id, edicion_id, fisico, reservado FROM fuentes.stock WHERE fuente_id = @f AND edicion_id = ANY(@ids) ORDER BY edicion_id",
                    [("f", fuenteId), ("ids", edicionIds.ToArray())],
                    token),
            cancelacion);
    }

    public Task<bool> HayExistencias(Guid edicionId, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDeRed(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM fuentes.stock WHERE edicion_id = @e)", conexion, transaccion);
                comando.Parameters.AddWithValue("e", edicionId);
                return (bool)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion);

    private static async Task<ResultadoDeAplicacion?> Mover(NpgsqlConnection conexion, NpgsqlTransaction transaccion, MovimientoDeStock movimiento, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(SentenciaDeMovimiento, conexion, transaccion);
        comando.Parameters.AddWithValue("f", movimiento.FuenteId);
        comando.Parameters.AddWithValue("e", movimiento.EdicionId);
        comando.Parameters.Add(new NpgsqlParameter("df", NpgsqlDbType.Bigint) { Value = (long)movimiento.DeltaFisico });
        comando.Parameters.Add(new NpgsqlParameter("dr", NpgsqlDbType.Bigint) { Value = (long)movimiento.DeltaReservado });

        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        if (!await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            return null;
        }

        return ResultadoDeAplicacion.Exito(
            new CantidadesDeStock(lector.GetInt32(0), lector.GetInt32(1)),
            new CantidadesDeStock(lector.GetInt32(2), lector.GetInt32(3)));
    }

    private static async Task<bool> Existe(NpgsqlConnection conexion, NpgsqlTransaction transaccion, MovimientoDeStock movimiento, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM fuentes.stock WHERE fuente_id = @f AND edicion_id = @e)", conexion, transaccion);
        comando.Parameters.AddWithValue("f", movimiento.FuenteId);
        comando.Parameters.AddWithValue("e", movimiento.EdicionId);
        return (bool)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
    }

    private static async Task<bool> ParejaConStockPosible(NpgsqlConnection conexion, NpgsqlTransaction transaccion, MovimientoDeStock movimiento, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM fuentes.fuentes WHERE id = @f) AND EXISTS (SELECT 1 FROM fuentes.ediciones WHERE edicion_id = @e AND formato = @formato)",
            conexion,
            transaccion);
        comando.Parameters.AddWithValue("f", movimiento.FuenteId);
        comando.Parameters.AddWithValue("e", movimiento.EdicionId);
        comando.Parameters.AddWithValue("formato", Impreso);
        return (bool)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
    }

    private static async Task<IReadOnlyList<FilaDeStock>> LeerFilas(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        string sql,
        (string Nombre, object Valor)[] parametros,
        CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }

        var filas = new List<FilaDeStock>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            filas.Add(new FilaDeStock(lector.GetGuid(0), lector.GetGuid(1), lector.GetInt32(2), lector.GetInt32(3)));
        }

        return filas;
    }
}
