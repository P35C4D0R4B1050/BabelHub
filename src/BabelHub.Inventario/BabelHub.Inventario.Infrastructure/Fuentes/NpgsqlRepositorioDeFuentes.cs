using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// <see cref="IRepositorioDeFuentes"/> sobre PostgreSQL. Las restricciones únicas de código y de nombre son la última defensa de dos altas simultáneas: se distinguen por el <b>nombre de la
/// restricción</b> (<c>uq_fuentes_codigo</c>, <c>uq_fuentes_nombre</c>) y se devuelven como resultado. La violación del índice parcial de recepción <b>no</b> se traduce a «código repetido»: es un
/// defecto (otro camino dejó dos recepciones) y se deja subir. Tras una violación la transacción queda abortada: el caso de uso termina con un fallo y la unidad de trabajo la revierte.
/// Nunca se concatena SQL con datos del usuario.
/// </summary>
internal sealed class NpgsqlRepositorioDeFuentes(AccesoAFuentes acceso) : IRepositorioDeFuentes
{
    private const string ViolacionDeUnicidad = "23505";
    private const string RestriccionDelCodigo = "uq_fuentes_codigo";
    private const string RestriccionDelNombre = "uq_fuentes_nombre";
    private const string Columnas = "id, codigo, nombre, region, contacto_aviso, es_recepcion";

    public async Task<Fuente?> PorId(Guid fuenteId, CancellationToken cancelacion = default) =>
        (await Leer($"SELECT {Columnas} FROM fuentes.fuentes WHERE id = @id", [("id", fuenteId)], cancelacion).ConfigureAwait(false)).FirstOrDefault();

    public async Task<Fuente?> PorCodigo(string codigo, CancellationToken cancelacion = default) =>
        (await Leer($"SELECT {Columnas} FROM fuentes.fuentes WHERE codigo = @codigo", [("codigo", codigo)], cancelacion).ConfigureAwait(false)).FirstOrDefault();

    public Task<IReadOnlyList<Fuente>> Listar(CancellationToken cancelacion = default) =>
        Leer($"SELECT {Columnas} FROM fuentes.fuentes ORDER BY codigo", [], cancelacion);

    public Task<IReadOnlyList<Fuente>> DeLaRegion(string region, CancellationToken cancelacion = default) =>
        Leer($"SELECT {Columnas} FROM fuentes.fuentes WHERE region = @region ORDER BY codigo", [("region", region)], cancelacion);

    public Task<int> Contar(CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM fuentes.fuentes", conexion, transaccion);
                return (int)(long)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion);

    public async Task<Fuente?> PorCodigoParaActualizar(string codigo, CancellationToken cancelacion = default) =>
        // FOR NO KEY UPDATE y no FOR UPDATE: las altas de stock y de rutas toman FOR KEY SHARE sobre la fuente y un FOR UPDATE las bloquearía sin necesidad.
        (await Leer($"SELECT {Columnas} FROM fuentes.fuentes WHERE codigo = @codigo FOR NO KEY UPDATE", [("codigo", codigo)], cancelacion).ConfigureAwait(false)).FirstOrDefault();

    public Task<ResultadoDeEscrituraDeFuente> Agregar(Fuente fuente, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(fuente);

        return Escribir(
            "INSERT INTO fuentes.fuentes (id, codigo, nombre, region, contacto_aviso, es_recepcion) VALUES (@id, @codigo, @nombre, @region, @contacto, @recepcion)",
            fuente,
            cancelacion);
    }

    public Task<ResultadoDeEscrituraDeFuente> Actualizar(Fuente fuente, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(fuente);

        // El código y la marca de recepción no cambian por aquí.
        return Escribir("UPDATE fuentes.fuentes SET nombre = @nombre, region = @region, contacto_aviso = @contacto WHERE id = @id", fuente, cancelacion);
    }

    public Task CambiarRecepcion(Guid? anteriorId, Guid nuevaId, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                // Primero se quita la marca y después se pone: el índice único parcial no admite dos a la vez ni siquiera dentro de una sola sentencia.
                await using (var quitar = new NpgsqlCommand("UPDATE fuentes.fuentes SET es_recepcion = false WHERE es_recepcion AND id <> @nueva", conexion, transaccion))
                {
                    quitar.Parameters.AddWithValue("nueva", nuevaId);
                    await quitar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await using var poner = new NpgsqlCommand("UPDATE fuentes.fuentes SET es_recepcion = true WHERE id = @nueva", conexion, transaccion);
                poner.Parameters.AddWithValue("nueva", nuevaId);
                if (await poner.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                {
                    throw new InvalidOperationException("La fuente de recepción elegida no existe.");
                }

                return anteriorId;
            },
            cancelacion);

    private Task<ResultadoDeEscrituraDeFuente> Escribir(string sql, Fuente fuente, CancellationToken cancelacion) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                comando.Parameters.AddWithValue("id", fuente.Id);
                comando.Parameters.AddWithValue("nombre", fuente.Nombre);
                comando.Parameters.AddWithValue("region", fuente.Region);
                comando.Parameters.AddWithValue("contacto", (object?)fuente.ContactoDeNotificacion ?? DBNull.Value);
                if (sql.StartsWith("INSERT", StringComparison.Ordinal))
                {
                    comando.Parameters.AddWithValue("codigo", fuente.Codigo);
                    comando.Parameters.AddWithValue("recepcion", fuente.EsRecepcion);
                }

                try
                {
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return ResultadoDeEscrituraDeFuente.Escrita;
                }
                catch (PostgresException ex) when (ex.SqlState == ViolacionDeUnicidad && ex.ConstraintName == RestriccionDelCodigo)
                {
                    return ResultadoDeEscrituraDeFuente.CodigoRepetido;
                }
                catch (PostgresException ex) when (ex.SqlState == ViolacionDeUnicidad && ex.ConstraintName == RestriccionDelNombre)
                {
                    return ResultadoDeEscrituraDeFuente.NombreRepetido;
                }
            },
            cancelacion);

    private Task<IReadOnlyList<Fuente>> Leer(string sql, (string Nombre, object Valor)[] parametros, CancellationToken cancelacion) =>
        acceso.ConAlcanceDelActor<IReadOnlyList<Fuente>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                foreach (var (nombre, valor) in parametros)
                {
                    comando.Parameters.AddWithValue(nombre, valor);
                }

                var fuentes = new List<Fuente>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    fuentes.Add(Fuente.Registrar(
                        lector.GetGuid(0),
                        lector.GetString(1),
                        lector.GetString(2),
                        lector.GetString(3),
                        lector.IsDBNull(4) ? null : lector.GetString(4),
                        lector.GetBoolean(5)));
                }

                return fuentes;
            },
            cancelacion);
}
