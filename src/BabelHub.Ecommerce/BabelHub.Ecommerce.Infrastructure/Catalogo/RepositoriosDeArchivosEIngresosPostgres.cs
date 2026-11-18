using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// Las referencias de archivo (<c>archivos_pendientes</c>, T14): el consumo toma la fila con <c>FOR UPDATE</c> (el trabajo de depuración usa <c>FOR UPDATE SKIP LOCKED</c>, así no compiten en el borde de las 24 horas) y el reemplazo de
/// un archivo es un <b>upsert por la clave del almacén</b>: la fila <c>consumida</c> pasa a <c>descarte</c> y, si ya se purgó, se inserta una <c>descarte</c> con el actor del reemplazo.
/// </summary>
public sealed class RepositorioDeReferenciasPostgres(AccesoAlEsquemaDelCatalogo acceso) : IRepositorioDeReferenciasDeArchivo
{
    private const string Columnas = "id, usuario_id, finalidad, clave_en_almacen, estado, tipo_detectado, tamano, huella_sha256, creado_en, vence_en, consumido_en";

    public Task<ReferenciaDeArchivo?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM catalogo.archivos_pendientes WHERE id = @id {(paraActualizar ? "FOR UPDATE" : string.Empty)}", conexion, transaccion);
                comando.Parameters.AddWithValue("id", id);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false) ? Leer(filas) : null;
            },
            cancelacion);

    public Task Agregar(ReferenciaDeArchivo referencia, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(referencia);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"INSERT INTO catalogo.archivos_pendientes ({Columnas}) VALUES (@id, @usuario, @finalidad, @clave, @estado, @tipo, @tamano, @huella, @creado, @vence, @consumido)",
                    conexion,
                    transaccion);
                Parametros(comando, referencia);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task Guardar(ReferenciaDeArchivo referencia, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(referencia);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // El consumo es condicional: una fila que no estaba lista (otra transacción la consumió o la depuró) no se vuelve a escribir.
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE catalogo.archivos_pendientes
                    SET estado = @estado, tipo_detectado = @tipo, tamano = @tamano, huella_sha256 = @huella, vence_en = @vence, consumido_en = @consumido
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion);
                Parametros(comando, referencia);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task EncolarDescarte(string claveEnAlmacen, FinalidadDeArchivo finalidad, Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var actualiza = new NpgsqlCommand("UPDATE catalogo.archivos_pendientes SET estado = 'descarte' WHERE clave_en_almacen = @clave", conexion, transaccion))
                {
                    actualiza.Parameters.AddWithValue("clave", claveEnAlmacen);
                    if (await actualiza.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0)
                    {
                        return 0;
                    }
                }

                // La fila consumida ya se purgó: se inserta una de descarte con el actor del reemplazo y el instante del reemplazo.
                await using var inserta = new NpgsqlCommand(
                    """
                    INSERT INTO catalogo.archivos_pendientes (id, usuario_id, finalidad, clave_en_almacen, estado, creado_en, vence_en)
                    VALUES (@id, @usuario, @finalidad, @clave, 'descarte', @ahora, @ahora)
                    ON CONFLICT (clave_en_almacen) DO UPDATE SET estado = 'descarte'
                    """,
                    conexion,
                    transaccion);
                inserta.Parameters.AddWithValue("id", Guid.NewGuid());
                inserta.Parameters.AddWithValue("usuario", usuarioId);
                inserta.Parameters.AddWithValue("finalidad", finalidad == FinalidadDeArchivo.Libro ? "libro" : "portada");
                inserta.Parameters.AddWithValue("clave", claveEnAlmacen);
                inserta.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                await inserta.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<IReadOnlyList<ReferenciaDeArchivo>> Depurables(DateTimeOffset ahora, TimeSpan margenDeSubida, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"""
                    SELECT {Columnas} FROM catalogo.archivos_pendientes
                    WHERE (estado = 'subiendo' AND creado_en + @margen <= @ahora)
                       OR (estado = 'lista' AND vence_en <= @ahora)
                       OR estado = 'descarte'
                       OR (estado = 'consumida' AND consumido_en + @retencion <= @ahora)
                    ORDER BY creado_en
                    LIMIT @maximo
                    FOR UPDATE SKIP LOCKED
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("margen", margenDeSubida);
                comando.Parameters.AddWithValue("retencion", PoliticaDeArchivos.RetencionDeReferenciasConsumidas);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                var lista = new List<ReferenciaDeArchivo>();
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    lista.Add(Leer(filas));
                }

                return (IReadOnlyList<ReferenciaDeArchivo>)lista;
            },
            cancelacion);

    public Task Eliminar(Guid id, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM catalogo.archivos_pendientes WHERE id = @id", conexion, transaccion);
                comando.Parameters.AddWithValue("id", id);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    private static ReferenciaDeArchivo Leer(NpgsqlDataReader filas)
    {
        TipoDeArchivo? tipo = null;
        if (!filas.IsDBNull(5) && TiposDeArchivo.TryDesdeNombre(filas.GetString(5), out var detectado))
        {
            tipo = detectado;
        }

        return ReferenciaDeArchivo.Rehidratar(
            filas.GetGuid(0),
            filas.GetGuid(1),
            filas.GetString(2) == "libro" ? FinalidadDeArchivo.Libro : FinalidadDeArchivo.Portada,
            filas.GetString(3),
            filas.GetString(4) switch
            {
                "lista" => EstadoDeReferencia.Lista,
                "consumida" => EstadoDeReferencia.Consumida,
                "descarte" => EstadoDeReferencia.Descarte,
                _ => EstadoDeReferencia.Subiendo,
            },
            tipo,
            AccesoAlEsquemaDelCatalogo.LeerNulable<long>(filas, 6),
            filas.IsDBNull(7) ? null : HuellaDeArchivo.Desde(filas.GetFieldValue<byte[]>(7)),
            filas.GetFieldValue<DateTimeOffset>(8),
            filas.GetFieldValue<DateTimeOffset>(9),
            AccesoAlEsquemaDelCatalogo.LeerNulable<DateTimeOffset>(filas, 10));
    }

    private static void Parametros(NpgsqlCommand comando, ReferenciaDeArchivo r)
    {
        comando.Parameters.AddWithValue("id", r.Id);
        comando.Parameters.AddWithValue("usuario", r.UsuarioId);
        comando.Parameters.AddWithValue("finalidad", r.Finalidad == FinalidadDeArchivo.Libro ? "libro" : "portada");
        comando.Parameters.AddWithValue("clave", r.ClaveDelAlmacen);
        comando.Parameters.AddWithValue("estado", r.Estado.ToString().ToLowerInvariant());
        comando.Parameters.Add(new NpgsqlParameter("tipo", NpgsqlDbType.Text) { Value = r.TipoDetectado is { } t ? TiposDeArchivo.Nombre(t) : DBNull.Value });
        comando.Parameters.Add(new NpgsqlParameter("tamano", NpgsqlDbType.Bigint) { Value = r.TamanoEnBytes is { } n ? n : DBNull.Value });
        comando.Parameters.Add(new NpgsqlParameter("huella", NpgsqlDbType.Bytea) { Value = r.Huella is { } h ? h.ComoBytes() : DBNull.Value });
        comando.Parameters.AddWithValue("creado", r.CreadoEn.ToUniversalTime());
        comando.Parameters.AddWithValue("vence", r.VenceEn.ToUniversalTime());
        comando.Parameters.Add(new NpgsqlParameter("consumido", NpgsqlDbType.TimestampTz) { Value = AccesoAlEsquemaDelCatalogo.UtcONulo(r.ConsumidoEn) });
    }
}

/// <summary>
/// Los ingresos solicitados (<c>ingresos_solicitados</c>, T14): el índice único parcial impide dos <c>pendiente</c> por edición y la clave de idempotencia es única; las transiciones son <b>condicionales</b>
/// (<c>WHERE estado = 'pendiente'</c>) y devuelven <see langword="false"/> si afectaron 0 filas.
/// </summary>
public sealed class RepositorioDeIngresosPostgres(AccesoAlEsquemaDelCatalogo acceso) : IRepositorioDeIngresos
{
    private const string Columnas = "id, edicion_id, clave_de_idempotencia, usuario_id, estado, ingreso_id, cantidad, referencia, nota, solicitado_en, intentos, ultimo_intento_en, resultado_en";

    public Task<bool> Agregar(IngresoSolicitado ingreso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ingreso);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Un conflicto (otro pendiente de la edición o la misma clave) no inserta nada y no aborta la transacción.
                await using var comando = new NpgsqlCommand(
                    $"INSERT INTO catalogo.ingresos_solicitados ({Columnas}) VALUES (@id, @edicion, @clave, @usuario, @estado, @ingreso, @cantidad, @referencia, @nota, @solicitado, @intentos, @ultimo, @resultado) ON CONFLICT DO NOTHING",
                    conexion,
                    transaccion);
                Parametros(comando, ingreso);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task<IngresoSolicitado?> Obtener(Guid ingresoSolicitadoId, bool paraActualizar, CancellationToken cancelacion = default) =>
        Uno($"WHERE id = @valor {(paraActualizar ? "FOR UPDATE" : string.Empty)}", ingresoSolicitadoId, cancelacion);

    public Task<IngresoSolicitado?> PorClave(Guid claveDeIdempotencia, CancellationToken cancelacion = default) => Uno("WHERE clave_de_idempotencia = @valor", claveDeIdempotencia, cancelacion);

    public Task<IngresoSolicitado?> PendienteDeLaEdicion(Guid edicionId, CancellationToken cancelacion = default) => Uno("WHERE edicion_id = @valor AND estado = 'pendiente'", edicionId, cancelacion);

    public Task<bool> GuardarTransicion(IngresoSolicitado ingreso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ingreso);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE catalogo.ingresos_solicitados
                    SET usuario_id = @usuario, estado = @estado, ingreso_id = @ingreso, referencia = @referencia, nota = @nota, intentos = @intentos, ultimo_intento_en = @ultimo, resultado_en = @resultado
                    WHERE id = @id AND estado = 'pendiente'
                    """,
                    conexion,
                    transaccion);
                Parametros(comando, ingreso);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<IngresoSolicitado>> DeLaEdicion(Guid edicionId, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM catalogo.ingresos_solicitados WHERE edicion_id = @edicion ORDER BY solicitado_en DESC LIMIT @maximo", conexion, transaccion);
                comando.Parameters.AddWithValue("edicion", edicionId);
                comando.Parameters.AddWithValue("maximo", maximo);
                var lista = new List<IngresoSolicitado>();
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    lista.Add(Leer(filas));
                }

                return (IReadOnlyList<IngresoSolicitado>)lista;
            },
            cancelacion);

    public Task<IReadOnlySet<Guid>> IngresosYaConstatados(IReadOnlyCollection<Guid> ingresoIds, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT ingreso_id FROM catalogo.ingresos_solicitados WHERE ingreso_id = ANY(@ids)", conexion, transaccion);
                comando.Parameters.AddWithValue("ids", ingresoIds.ToArray());
                var conjunto = new HashSet<Guid>();
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    conjunto.Add(filas.GetGuid(0));
                }

                return (IReadOnlySet<Guid>)conjunto;
            },
            cancelacion);

    private Task<IngresoSolicitado?> Uno(string clausula, Guid valor, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM catalogo.ingresos_solicitados {clausula}", conexion, transaccion);
                comando.Parameters.AddWithValue("valor", valor);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false) ? Leer(filas) : null;
            },
            cancelacion);

    private static IngresoSolicitado Leer(NpgsqlDataReader filas) =>
        IngresoSolicitado.Rehidratar(
            filas.GetGuid(0),
            filas.GetGuid(1),
            filas.GetGuid(2),
            filas.GetGuid(3),
            filas.GetString(4) switch
            {
                "registrado" => EstadoDeIngreso.Registrado,
                "fallido" => EstadoDeIngreso.Fallido,
                _ => EstadoDeIngreso.Pendiente,
            },
            AccesoAlEsquemaDelCatalogo.LeerNulable<Guid>(filas, 5),
            filas.GetInt32(6),
            AccesoAlEsquemaDelCatalogo.LeerTexto(filas, 7),
            AccesoAlEsquemaDelCatalogo.LeerTexto(filas, 8),
            filas.GetFieldValue<DateTimeOffset>(9),
            filas.GetInt32(10),
            filas.GetFieldValue<DateTimeOffset>(11),
            AccesoAlEsquemaDelCatalogo.LeerNulable<DateTimeOffset>(filas, 12));

    private static void Parametros(NpgsqlCommand comando, IngresoSolicitado i)
    {
        comando.Parameters.AddWithValue("id", i.Id);
        comando.Parameters.AddWithValue("edicion", i.EdicionId);
        comando.Parameters.AddWithValue("clave", i.ClaveDeIdempotencia);
        comando.Parameters.AddWithValue("usuario", i.UsuarioId);
        comando.Parameters.AddWithValue("estado", i.Estado.ToString().ToLowerInvariant());
        comando.Parameters.Add(new NpgsqlParameter("ingreso", NpgsqlDbType.Uuid) { Value = i.IngresoId is { } g ? g : DBNull.Value });
        comando.Parameters.AddWithValue("cantidad", i.Cantidad);
        comando.Parameters.Add(new NpgsqlParameter("referencia", NpgsqlDbType.Text) { Value = (object?)i.Referencia ?? DBNull.Value });
        comando.Parameters.Add(new NpgsqlParameter("nota", NpgsqlDbType.Text) { Value = (object?)i.Nota ?? DBNull.Value });
        comando.Parameters.AddWithValue("solicitado", i.SolicitadoEn.ToUniversalTime());
        comando.Parameters.AddWithValue("intentos", i.Intentos);
        comando.Parameters.AddWithValue("ultimo", i.UltimoIntentoEn.ToUniversalTime());
        comando.Parameters.Add(new NpgsqlParameter("resultado", NpgsqlDbType.TimestampTz) { Value = AccesoAlEsquemaDelCatalogo.UtcONulo(i.ResultadoEn) });
    }
}
