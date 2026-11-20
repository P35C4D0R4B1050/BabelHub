using System.Globalization;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Domain.Devoluciones;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Devoluciones;

/// <summary>
/// El repositorio de las devoluciones y de los cupos de línea sobre el esquema <c>devoluciones</c> (T19, T21). Cada lectura <c>paraActualizar</c> es <c>SELECT … FOR UPDATE</c> con un <c>lock_timeout</c> de 5 segundos (un tiempo agotado es «no disponible»);
/// la revisión de plazos usa <c>FOR UPDATE SKIP LOCKED</c>. El cupo se toma con un <c>UPDATE … WHERE cantidad_comprometida + @n &lt;= cantidad_comprada</c> <b>en orden de <c>linea_id</c></b>: dos solicitudes simultáneas sobre la misma unidad no pasan las dos y dos solicitudes con las mismas
/// líneas no se interbloquean. El motivo y el texto se convierten <b>explícitamente</b> (<see cref="TextoRedactado.Rehidratar"/> y su <c>Valor</c>), nunca por serialización; <b>nada se registra</b> con el valor de una fila. Todos los valores van por parámetros.
/// </summary>
internal sealed class RepositorioDeDevolucionesPostgres(AccesoAlEsquemaDeDevoluciones acceso) : IRepositorioDeDevoluciones
{
    private const string ColumnasDeLaDevolucion =
        "id, pedido_id, tipo, origen, codigo_de_sucursal_receptora, motivo, texto, creada_en, redactada_en, reingreso_iniciado_en, reingreso_incierto_en, reingreso_confirmado_en";

    private const string NoHayLineasNoFinales = "NOT EXISTS (SELECT 1 FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id AND l.estado NOT IN ('reembolsada', 'rechazada'))";

    private const string TodasAprobadas = "NOT EXISTS (SELECT 1 FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id AND l.estado <> 'aprobada')";

    public Task<Devolucion?> Obtener(Guid devolucionId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                (await Leer(conexion, transaccion, "d.id = @valor", [new NpgsqlParameter("valor", devolucionId)], string.Empty, paraActualizar ? " FOR UPDATE OF d" : string.Empty, paraActualizar, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<Devolucion?> ObtenerParaActualizarSinEsperar(Guid devolucionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                (await Leer(conexion, transaccion, "d.id = @valor", [new NpgsqlParameter("valor", devolucionId)], string.Empty, " FOR UPDATE OF d SKIP LOCKED", false, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task Agregar(Devolucion devolucion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(devolucion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var insertar = new NpgsqlCommand(
                    $"INSERT INTO devoluciones.devoluciones ({ColumnasDeLaDevolucion}) VALUES (@id, @pedido, @tipo, @origen, @sucursal, @motivo, @texto, @creada, @redactada, @iniciado, @incierto, @confirmado)",
                    conexion,
                    transaccion))
                {
                    ParametrosDeLaDevolucion(insertar, devolucion);
                    insertar.Parameters.AddWithValue("tipo", Codigo(devolucion.Tipo));
                    insertar.Parameters.AddWithValue("origen", Codigo(devolucion.Origen));
                    insertar.Parameters.AddWithValue("pedido", devolucion.PedidoId);
                    insertar.Parameters.AddWithValue("sucursal", AccesoAlEsquemaDeDevoluciones.ONulo(devolucion.CodigoDeSucursalReceptora));
                    insertar.Parameters.AddWithValue("creada", devolucion.CreadaEn.ToUniversalTime());
                    await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                foreach (var linea in devolucion.Lineas)
                {
                    await using var comando = new NpgsqlCommand(
                        """
                        INSERT INTO devoluciones.lineas_de_devolucion
                            (id, devolucion_id, linea_id, edicion_id, cantidad, precio_unitario, monto, estado, estado_en, aprobada_en, acceso_id, reembolso_registrado_en, reembolsada_en)
                        VALUES (@id, @devolucion, @linea, @edicion, @cantidad, @precio, @monto, @estado, @estadoEn, @aprobada, @acceso, @reembolsoRegistrado, @reembolsada)
                        """,
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("devolucion", devolucion.Id);
                    comando.Parameters.AddWithValue("linea", linea.LineaDePedidoId);
                    comando.Parameters.AddWithValue("edicion", linea.EdicionId);
                    comando.Parameters.AddWithValue("cantidad", linea.Cantidad);
                    comando.Parameters.AddWithValue("precio", linea.PrecioUnitario);
                    comando.Parameters.AddWithValue("monto", linea.Monto);
                    ParametrosDeLaLinea(comando, linea);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await GuardarLosCambios(conexion, transaccion, linea, token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }

    public Task Guardar(Devolucion devolucion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(devolucion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    UPDATE devoluciones.devoluciones SET
                        motivo = @motivo, texto = @texto, redactada_en = @redactada, reingreso_iniciado_en = @iniciado, reingreso_incierto_en = @incierto, reingreso_confirmado_en = @confirmado
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion))
                {
                    ParametrosDeLaDevolucion(comando, devolucion);
                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("La devolución no existe.");
                    }
                }

                foreach (var linea in devolucion.Lineas)
                {
                    await using (var comando = new NpgsqlCommand(
                        """
                        UPDATE devoluciones.lineas_de_devolucion SET
                            estado = @estado, estado_en = @estadoEn, aprobada_en = @aprobada, acceso_id = @acceso, reembolso_registrado_en = @reembolsoRegistrado, reembolsada_en = @reembolsada
                        WHERE id = @id
                        """,
                        conexion,
                        transaccion))
                    {
                        ParametrosDeLaLinea(comando, linea);
                        if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                        {
                            throw new InvalidOperationException("La línea de la devolución no existe.");
                        }
                    }

                    await GuardarLosCambios(conexion, transaccion, linea, token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }

    public Task<Guid?> TomarCupos(IReadOnlyList<CupoPedido> cupos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cupos);
        if (cupos.Any(c => c.Unidades < 1 || c.CantidadComprada < 1 || c.LineaId == Guid.Empty || c.PedidoId == Guid.Empty))
        {
            throw new ArgumentOutOfRangeException(nameof(cupos), "Un cupo necesita la línea, el pedido, lo comprado y al menos una unidad.");
        }

        return acceso.Ejecutar<Guid?>(
            async (conexion, transaccion, token) =>
            {
                // Siempre en orden de linea_id: dos solicitudes con las mismas líneas no se interbloquean (plan §1).
                foreach (var cupo in cupos.OrderBy(c => c.LineaId))
                {
                    await using (var crear = new NpgsqlCommand(
                        "INSERT INTO devoluciones.cupos_de_linea (linea_id, pedido_id, cantidad_comprada, cantidad_comprometida) VALUES (@linea, @pedido, @comprada, 0) ON CONFLICT (linea_id) DO NOTHING",
                        conexion,
                        transaccion))
                    {
                        crear.Parameters.AddWithValue("linea", cupo.LineaId);
                        crear.Parameters.AddWithValue("pedido", cupo.PedidoId);
                        crear.Parameters.AddWithValue("comprada", cupo.CantidadComprada);
                        await crear.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    // La sentencia condicional: la fila queda bloqueada hasta el final de la transacción y solo una de dos solicitudes simultáneas compromete la última unidad.
                    await using var tomar = new NpgsqlCommand(
                        "UPDATE devoluciones.cupos_de_linea SET cantidad_comprometida = cantidad_comprometida + @unidades WHERE linea_id = @linea AND cantidad_comprometida + @unidades <= cantidad_comprada",
                        conexion,
                        transaccion);
                    tomar.Parameters.AddWithValue("linea", cupo.LineaId);
                    tomar.Parameters.AddWithValue("unidades", cupo.Unidades);
                    if (await tomar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        return cupo.LineaId;
                    }
                }

                return null;
            },
            cancelacion);
    }

    public Task DevolverCupos(IReadOnlyList<(Guid LineaId, int Unidades)> cupos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cupos);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                foreach (var (lineaId, unidades) in cupos.OrderBy(c => c.LineaId))
                {
                    // Nunca se devuelve más de lo comprometido: recortar a cero liberaría en silencio unidades de otra devolución sobre la misma línea (defecto de programación, no un hecho que se acepte).
                    await using var comando = new NpgsqlCommand(
                        "UPDATE devoluciones.cupos_de_linea SET cantidad_comprometida = cantidad_comprometida - @unidades WHERE linea_id = @linea AND cantidad_comprometida >= @unidades",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("linea", lineaId);
                    comando.Parameters.AddWithValue("unidades", unidades);
                    if (unidades < 1 || await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("Se devolvió más de lo comprometido en el cupo de una línea.");
                    }
                }

                return 0;
            },
            cancelacion);
    }

    public Task<PaginaDeDevolucionesDelDominio> ListarDePedidos(IReadOnlyCollection<Guid> pedidoIds, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(pedidoIds);
        var ids = pedidoIds.ToArray();
        return Pagina("d.pedido_id = ANY(@valor)", () => [new NpgsqlParameter("valor", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids }], pagina, tamano, cancelacion);
    }

    public Task<PaginaDeDevolucionesDelDominio> ListarDeLaSucursal(string codigoDeSucursal, int pagina, int tamano, CancellationToken cancelacion = default) =>
        Pagina("d.tipo = 'impreso' AND d.origen = 'solicitud' AND d.codigo_de_sucursal_receptora = @valor", () => [new NpgsqlParameter("valor", codigoDeSucursal)], pagina, tamano, cancelacion);

    public Task<PaginaDeDevolucionesDelDominio> ListarDeLaAdministracion(FiltroDeLaAdministracion filtro, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var condiciones = new List<string> { "TRUE" };
        if (filtro.Tipo is not null)
        {
            condiciones.Add("d.tipo = @tipo");
        }

        if (filtro.SoloReportes)
        {
            condiciones.Add("d.origen = 'reporte'");
        }

        return Pagina(string.Join(" AND ", condiciones), () => filtro.Tipo is { } t ? [new NpgsqlParameter("tipo", Codigo(t))] : [], pagina, tamano, cancelacion);
    }

    public Task<IReadOnlyList<(Guid DevolucionId, Guid LineaDePedidoId)>> ObtenerConReembolsoPendiente(int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<(Guid, Guid)>>(
            async (conexion, transaccion, token) =>
            {
                var pendientes = new List<(Guid, Guid)>();
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT d.id, l.linea_id
                    FROM devoluciones.devoluciones d
                    JOIN devoluciones.lineas_de_devolucion l ON l.devolucion_id = d.id
                    WHERE (d.tipo = 'impreso' AND l.estado = 'recibida') OR (d.tipo = 'digital' AND l.estado = 'aprobada')
                    ORDER BY d.creada_en, d.id, l.linea_id
                    LIMIT @maximo
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("maximo", maximo);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    pendientes.Add((lector.GetGuid(0), lector.GetGuid(1)));
                }

                return pendientes;
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> ObtenerConRecepcionVencida(DateTimeOffset aprobadasAntesDe, int maximo, CancellationToken cancelacion = default) =>
        Identificadores(
            $"d.tipo = 'impreso' AND d.reingreso_iniciado_en IS NULL AND EXISTS (SELECT 1 FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id) AND {TodasAprobadas} "
            + "AND (SELECT min(l.aprobada_en) FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id) < @limite",
            "(SELECT min(l.aprobada_en) FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id), d.id",
            aprobadasAntesDe,
            maximo,
            cancelacion);

    public Task<IReadOnlyList<Guid>> ObtenerConReingresoEstancado(DateTimeOffset iniciadoAntesDe, int maximo, CancellationToken cancelacion = default) =>
        Identificadores($"d.reingreso_iniciado_en < @limite AND {TodasAprobadas}", "d.reingreso_iniciado_en, d.id", iniciadoAntesDe, maximo, cancelacion);

    public Task<IReadOnlyList<Guid>> ObtenerVencidasParaDepurar(DateTimeOffset ultimoCambioAntesDe, int maximo, CancellationToken cancelacion = default) =>
        Identificadores(
            $"d.redactada_en IS NULL AND {NoHayLineasNoFinales} AND (SELECT max(l.estado_en) FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id) < @limite",
            "(SELECT max(l.estado_en) FROM devoluciones.lineas_de_devolucion l WHERE l.devolucion_id = d.id), d.id",
            ultimoCambioAntesDe,
            maximo,
            cancelacion);

    // ---------- Apoyo ----------

    private Task<IReadOnlyList<Guid>> Identificadores(string filtro, string orden, DateTimeOffset limite, int maximo, CancellationToken cancelacion) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                var ids = new List<Guid>();
                await using var comando = new NpgsqlCommand($"SELECT d.id FROM devoluciones.devoluciones d WHERE {filtro} ORDER BY {orden} LIMIT @maximo", conexion, transaccion);
                comando.Parameters.AddWithValue("limite", limite.ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    ids.Add(lector.GetGuid(0));
                }

                return ids;
            },
            cancelacion);

    private Task<PaginaDeDevolucionesDelDominio> Pagina(string filtro, Func<NpgsqlParameter[]> parametros, int pagina, int tamano, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                int total;
                await using (var contar = new NpgsqlCommand($"SELECT count(*) FROM devoluciones.devoluciones d WHERE {filtro}", conexion, transaccion))
                {
                    contar.Parameters.AddRange(parametros());
                    total = Convert.ToInt32(await contar.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }

                var devoluciones = await Leer(
                    conexion,
                    transaccion,
                    filtro,
                    [.. parametros(), new NpgsqlParameter("limite", tamano), new NpgsqlParameter("desplazamiento", (long)(pagina - 1) * tamano)],
                    " ORDER BY d.creada_en DESC, d.id LIMIT @limite OFFSET @desplazamiento",
                    string.Empty,
                    false,
                    token).ConfigureAwait(false);
                return new PaginaDeDevolucionesDelDominio(devoluciones, total);
            },
            cancelacion);

    private static async Task<IReadOnlyList<Devolucion>> Leer(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        string filtro,
        NpgsqlParameter[] parametros,
        string orden,
        string bloqueo,
        bool conTiempoDeEspera,
        CancellationToken cancelacion)
    {
        var bloquea = bloqueo.Length > 0 && !bloqueo.Contains("SKIP LOCKED", StringComparison.Ordinal);
        if (bloquea || conTiempoDeEspera)
        {
            await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = '5s'", cancelacion).ConfigureAwait(false);
        }

        var filas = new List<FilaDeLaDevolucion>();
        await using (var comando = new NpgsqlCommand($"SELECT {string.Join(", ", ColumnasDeLaDevolucion.Split(", ").Select(c => "d." + c))} FROM devoluciones.devoluciones d WHERE {filtro}{orden}{bloqueo}", conexion, transaccion))
        {
            comando.Parameters.AddRange(parametros);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                filas.Add(FilaDeLaDevolucion.Leer(lector));
            }
        }

        if (bloquea || conTiempoDeEspera)
        {
            await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = DEFAULT", cancelacion).ConfigureAwait(false);
        }

        if (filas.Count == 0)
        {
            return [];
        }

        var lineas = new Dictionary<Guid, List<FilaDeLaLinea>>();
        var lineaIds = new List<Guid>();
        await using (var comando = new NpgsqlCommand(
            """
            SELECT id, devolucion_id, linea_id, edicion_id, cantidad, precio_unitario, monto, acceso_id, reembolso_registrado_en
            FROM devoluciones.lineas_de_devolucion WHERE devolucion_id = ANY(@ids) ORDER BY devolucion_id, linea_id
            """,
            conexion,
            transaccion))
        {
            comando.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filas.Select(f => f.Id).ToArray() });
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                var fila = FilaDeLaLinea.Leer(lector);
                if (!lineas.TryGetValue(fila.DevolucionId, out var lista))
                {
                    lineas[fila.DevolucionId] = lista = [];
                }

                lista.Add(fila);
                lineaIds.Add(fila.Id);
            }
        }

        var cambios = new Dictionary<Guid, List<CambioDeEstado>>();
        if (lineaIds.Count > 0)
        {
            await using var comando = new NpgsqlCommand(
                "SELECT linea_de_devolucion_id, estado, cambiado_en, responsable_tipo, responsable_id FROM devoluciones.cambios_de_linea WHERE linea_de_devolucion_id = ANY(@ids) ORDER BY linea_de_devolucion_id, orden",
                conexion,
                transaccion);
            comando.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineaIds.ToArray() });
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                var lineaId = lector.GetGuid(0);
                if (!cambios.TryGetValue(lineaId, out var lista))
                {
                    cambios[lineaId] = lista = [];
                }

                lista.Add(new CambioDeEstado(
                    EstadosDeLineaDeDevolucion.Desde(lector.GetString(1)),
                    lector.GetFieldValue<DateTimeOffset>(2),
                    ResponsableDelCambio.Rehidratar(DesdeResponsable(lector.GetString(3)), lector.IsDBNull(4) ? null : lector.GetGuid(4))));
            }
        }

        return [.. filas.Select(f => f.Convertir([.. (lineas.GetValueOrDefault(f.Id) ?? []).Select(l => l.Convertir(cambios.GetValueOrDefault(l.Id) ?? []))]))];
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    private static void ParametrosDeLaDevolucion(NpgsqlCommand comando, Devolucion d)
    {
        comando.Parameters.AddWithValue("id", d.Id);
        // La conversión es explícita: el valor, o el fijo si ya se redactó; nunca por serialización.
        comando.Parameters.AddWithValue("motivo", d.Motivo.Valor);
        comando.Parameters.AddWithValue("texto", AccesoAlEsquemaDeDevoluciones.ONulo(d.Texto?.Valor));
        comando.Parameters.AddWithValue("redactada", AccesoAlEsquemaDeDevoluciones.UtcONulo(d.RedactadaEn));
        comando.Parameters.AddWithValue("iniciado", AccesoAlEsquemaDeDevoluciones.UtcONulo(d.ReingresoIniciadoEn));
        comando.Parameters.AddWithValue("incierto", AccesoAlEsquemaDeDevoluciones.UtcONulo(d.ReingresoIncertoEn));
        comando.Parameters.AddWithValue("confirmado", AccesoAlEsquemaDeDevoluciones.UtcONulo(d.ReingresoConfirmadoEn));
    }

    private static void ParametrosDeLaLinea(NpgsqlCommand comando, LineaDeDevolucion l)
    {
        comando.Parameters.AddWithValue("id", l.Id);
        comando.Parameters.AddWithValue("estado", l.Estado.Codigo());
        comando.Parameters.AddWithValue("estadoEn", l.EstadoEn.ToUniversalTime());
        comando.Parameters.AddWithValue("aprobada", AccesoAlEsquemaDeDevoluciones.UtcONulo(l.AprobadaEn));
        comando.Parameters.AddWithValue("acceso", AccesoAlEsquemaDeDevoluciones.ONulo(l.AccesoId));
        comando.Parameters.AddWithValue("reembolsoRegistrado", AccesoAlEsquemaDeDevoluciones.UtcONulo(l.ReembolsoRegistradoEn));
        comando.Parameters.AddWithValue("reembolsada", AccesoAlEsquemaDeDevoluciones.UtcONulo(l.ReembolsadaEn));
    }

    /// <summary>Añade los cambios que faltan y, si el responsable perdió su identificador (la cuenta se canceló o pasaron dos años), lo quita: nada más cambia en una fila de historial.</summary>
    private static async Task GuardarLosCambios(NpgsqlConnection conexion, NpgsqlTransaction transaccion, LineaDeDevolucion linea, CancellationToken cancelacion)
    {
        for (var orden = 0; orden < linea.Cambios.Count; orden++)
        {
            var cambio = linea.Cambios[orden];
            await using var comando = new NpgsqlCommand(
                """
                INSERT INTO devoluciones.cambios_de_linea (linea_de_devolucion_id, orden, estado, cambiado_en, responsable_tipo, responsable_id)
                VALUES (@linea, @orden, @estado, @cambiado, @tipo, @responsable)
                ON CONFLICT (linea_de_devolucion_id, orden) DO UPDATE SET responsable_id = EXCLUDED.responsable_id
                WHERE devoluciones.cambios_de_linea.responsable_id IS NOT NULL AND EXCLUDED.responsable_id IS NULL
                """,
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("linea", linea.Id);
            comando.Parameters.AddWithValue("orden", orden);
            comando.Parameters.AddWithValue("estado", cambio.Estado.Codigo());
            comando.Parameters.AddWithValue("cambiado", cambio.Instante.ToUniversalTime());
            comando.Parameters.AddWithValue("tipo", CodigoDelResponsable(cambio.Responsable.Tipo));
            comando.Parameters.AddWithValue("responsable", AccesoAlEsquemaDeDevoluciones.ONulo(cambio.Responsable.Id));
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }
    }

    private static string Codigo(TipoDeDevolucion tipo) => tipo == TipoDeDevolucion.Impreso ? "impreso" : "digital";

    private static string Codigo(OrigenDeDevolucion origen) => origen == OrigenDeDevolucion.Solicitud ? "solicitud" : "reporte";

    private static string CodigoDelResponsable(TipoDeResponsable tipo) => tipo switch
    {
        TipoDeResponsable.Cliente => "cliente",
        TipoDeResponsable.Personal => "personal",
        _ => "sistema",
    };

    private static TipoDeResponsable DesdeResponsable(string codigo) => codigo switch
    {
        "cliente" => TipoDeResponsable.Cliente,
        "personal" => TipoDeResponsable.Personal,
        "sistema" => TipoDeResponsable.Sistema,
        _ => throw new InvalidOperationException("Un tipo de responsable desconocido."),
    };

    private sealed record FilaDeLaDevolucion(
        Guid Id,
        Guid PedidoId,
        TipoDeDevolucion Tipo,
        OrigenDeDevolucion Origen,
        string? Sucursal,
        string Motivo,
        string? Texto,
        DateTimeOffset Creada,
        DateTimeOffset? Redactada,
        DateTimeOffset? Iniciado,
        DateTimeOffset? Incierto,
        DateTimeOffset? Confirmado)
    {
        // Un record vuelca sus miembros en ToString: este lleva el motivo y el texto (DEV-31), así que no vuelca nada.
        public override string ToString() => nameof(FilaDeLaDevolucion);

        public static FilaDeLaDevolucion Leer(NpgsqlDataReader f) =>
            new(
                f.GetGuid(0),
                f.GetGuid(1),
                f.GetString(2) == "impreso" ? TipoDeDevolucion.Impreso : TipoDeDevolucion.Digital,
                f.GetString(3) == "solicitud" ? OrigenDeDevolucion.Solicitud : OrigenDeDevolucion.Reporte,
                f.IsDBNull(4) ? null : f.GetString(4),
                f.GetString(5),
                f.IsDBNull(6) ? null : f.GetString(6),
                f.GetFieldValue<DateTimeOffset>(7),
                AccesoAlEsquemaDeDevoluciones.LeerInstante(f, 8),
                AccesoAlEsquemaDeDevoluciones.LeerInstante(f, 9),
                AccesoAlEsquemaDeDevoluciones.LeerInstante(f, 10),
                AccesoAlEsquemaDeDevoluciones.LeerInstante(f, 11));

        public Devolucion Convertir(IReadOnlyList<LineaRehidratada> lineas)
        {
            // El texto está redactado si y solo si la devolución tiene fecha de redacción (la restricción de la tabla lo garantiza).
            var redactado = Redactada is not null;
            return Devolucion.Rehidratar(
                Id,
                PedidoId,
                Tipo,
                Origen,
                Sucursal,
                TextoRedactado.Rehidratar(Motivo, redactado),
                Texto is null ? null : TextoRedactado.Rehidratar(Texto, redactado),
                Creada,
                Redactada,
                Iniciado,
                Incierto,
                Confirmado,
                lineas);
        }
    }

    private sealed record FilaDeLaLinea(Guid Id, Guid DevolucionId, Guid LineaDePedidoId, Guid EdicionId, int Cantidad, decimal Precio, decimal Monto, Guid? AccesoId, DateTimeOffset? ReembolsoRegistrado)
    {
        public static FilaDeLaLinea Leer(NpgsqlDataReader f) =>
            new(f.GetGuid(0), f.GetGuid(1), f.GetGuid(2), f.GetGuid(3), f.GetInt32(4), f.GetDecimal(5), f.GetDecimal(6), f.IsDBNull(7) ? null : f.GetGuid(7), AccesoAlEsquemaDeDevoluciones.LeerInstante(f, 8));

        public LineaRehidratada Convertir(IReadOnlyList<CambioDeEstado> cambios) => new(Id, LineaDePedidoId, EdicionId, Cantidad, Precio, Monto, AccesoId, cambios, ReembolsoRegistrado);
    }
}
