using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// <see cref="IRepositorioDeReservas"/> sobre PostgreSQL (esquema <c>reservas</c>, D01, D07). SQL explícito y siempre con parámetros. Cada escritura de estado es <b>una sola sentencia condicional</b> que devuelve
/// con <c>RETURNING</c> lo que de verdad cambió: el stock se ajusta solo por las líneas realmente actualizadas. Las comparaciones de vencimiento reciben <c>@ahora</c> del reloj de la aplicación y no usan la hora de la
/// base (D12). Las líneas que se van a cambiar se bloquean <b>por id y en orden</b> (<c>ORDER BY id FOR UPDATE</c>), siempre después del bloqueo asesor del pedido que toma la Aplicación; nunca se bloquea la cabecera antes
/// que el pedido. Una violación de unicidad al insertar (una cabecera o una línea activa concurrentes que el bloqueo del pedido ya debería impedir) se devuelve como <see cref="ReservaDuplicadaException"/>, nunca como un 500.
/// </summary>
internal sealed class NpgsqlRepositorioDeReservas(AccesoAReservas acceso) : IRepositorioDeReservas
{
    private const string ViolacionDeUnicidad = "23505";

    private const string ColumnasDeCabecera = "id, pedido_id, cliente_id, tipo_entrega, sucursal_recojo_id, region_entrega, iniciada_en, fecha_expiracion, extendida, reemplazos";

    private const string ColumnasDeLinea = "id, linea_id, edicion_id, fuente_id, cantidad, estado, cerrada_por, traslado_id, creada_por_traslado, recreada_en_destino, confirmada_en, anulada_en, estado_cambiado_en";

    /// <summary>La condición de «línea pendiente de una cabecera vencida»: la fecha de la cabecera ya llegó a <c>@ahora</c> (en el instante exacto, también).</summary>
    private const string PendienteVencida = "l.estado = 'pendiente' AND r.fecha_expiracion IS NOT NULL AND r.fecha_expiracion <= @ahora";

    public async Task<ReservaDePedido?> UltimaDelPedido(Guid pedidoId, CancellationToken cancelacion = default) =>
        (await Una("pedido_id = @p ORDER BY reemplazos DESC, iniciada_en DESC, id DESC", "p", pedidoId, cancelacion).ConfigureAwait(false))?.Reserva;

    public Task<ReservaPorClave?> PorClave(Guid clienteId, string clave, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var cabecera = new NpgsqlCommand($"SELECT {ColumnasDeCabecera}, huella_de_carga FROM reservas.reservas WHERE cliente_id = @c AND clave_idempotencia = @k", conexion, transaccion);
                cabecera.Parameters.AddWithValue("c", clienteId);
                cabecera.Parameters.AddWithValue("k", clave);
                return await LeerUna(cabecera, conexion, transaccion, token).ConfigureAwait(false);
            },
            cancelacion);
    }

    public Task<bool> ClienteDadoDeBaja(Guid clienteId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM reservas.bajas_recibidas WHERE identificador_opaco = @c)", conexion, transaccion);
                comando.Parameters.AddWithValue("c", clienteId);
                return (bool)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion);

    public Task<DateTimeOffset?> ExpiracionDeLaPendienteDelCliente(Guid clienteId, Guid exceptoPedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT max(r.fecha_expiracion)
                    FROM reservas.reservas r
                    WHERE r.cliente_id = @c AND r.pedido_id <> @p AND r.fecha_expiracion IS NOT NULL AND r.fecha_expiracion > @ahora
                      AND EXISTS (SELECT 1 FROM reservas.reserva_lineas l WHERE l.reserva_id = r.id AND l.estado = 'pendiente')
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("c", clienteId);
                comando.Parameters.AddWithValue("p", exceptoPedidoId);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                var valor = await comando.ExecuteScalarAsync(token).ConfigureAwait(false);
                return valor is null or DBNull ? (DateTimeOffset?)null : new DateTimeOffset((DateTime)valor, TimeSpan.Zero);
            },
            cancelacion);

    private Task<ReservaPorClave?> Una(string condicion, string parametro, Guid valor, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var cabecera = new NpgsqlCommand($"SELECT {ColumnasDeCabecera}, huella_de_carga FROM reservas.reservas WHERE {condicion} LIMIT 1", conexion, transaccion);
                cabecera.Parameters.AddWithValue(parametro, valor);
                return await LeerUna(cabecera, conexion, transaccion, token).ConfigureAwait(false);
            },
            cancelacion);

    private static async Task<ReservaPorClave?> LeerUna(NpgsqlCommand cabecera, NpgsqlConnection conexion, NpgsqlTransaction transaccion, CancellationToken token)
    {
        Guid id, pedidoId, clienteId;
        string tipo, huella;
        Guid? sucursal;
        string? region;
        DateTimeOffset iniciadaEn;
        DateTimeOffset? expiracion;
        bool extendida;
        int reemplazos;
        await using (var lector = await cabecera.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (!await lector.ReadAsync(token).ConfigureAwait(false))
            {
                return null;
            }

            id = lector.GetGuid(0);
            pedidoId = lector.GetGuid(1);
            clienteId = lector.GetGuid(2);
            tipo = lector.GetString(3);
            sucursal = lector.IsDBNull(4) ? null : lector.GetGuid(4);
            region = lector.IsDBNull(5) ? null : lector.GetString(5);
            iniciadaEn = lector.GetFieldValue<DateTimeOffset>(6);
            expiracion = lector.IsDBNull(7) ? null : lector.GetFieldValue<DateTimeOffset>(7);
            extendida = lector.GetBoolean(8);
            reemplazos = lector.GetInt32(9);
            huella = lector.GetString(10);
        }

        var lineas = await LeerLineas(conexion, transaccion, id, token).ConfigureAwait(false);
        return new ReservaPorClave(ReservaDePedido.Rehidratar(id, pedidoId, clienteId, ATipo(tipo), sucursal, region, iniciadaEn, expiracion, extendida, reemplazos, lineas), huella);
    }

    public Task Insertar(ReservaDePedido reserva, DatosDeLaSolicitud solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(solicitud);

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                try
                {
                    await using (var cabecera = new NpgsqlCommand(
                        """
                        INSERT INTO reservas.reservas
                            (id, pedido_id, cliente_id, huella_origen, clave_idempotencia, huella_de_carga, tipo_entrega, sucursal_recojo_id, region_entrega, iniciada_en, fecha_expiracion, extendida, reemplazos)
                        VALUES (@id, @pedido, @cliente, @origen, @clave, @carga, @tipo, @sucursal, @region, @iniciada, @expira, @extendida, @reemplazos)
                        """,
                        conexion,
                        transaccion))
                    {
                        cabecera.Parameters.AddWithValue("id", reserva.Id);
                        cabecera.Parameters.AddWithValue("pedido", reserva.PedidoId);
                        cabecera.Parameters.AddWithValue("cliente", reserva.ClienteId);
                        cabecera.Parameters.Add(new NpgsqlParameter("origen", NpgsqlDbType.Text) { Value = (object?)solicitud.HuellaDeOrigen ?? DBNull.Value });
                        cabecera.Parameters.AddWithValue("clave", solicitud.ClaveDeIdempotencia);
                        cabecera.Parameters.AddWithValue("carga", solicitud.HuellaDeLaCarga);
                        cabecera.Parameters.AddWithValue("tipo", reserva.EsDeRecojo ? "recojo" : "domicilio");
                        cabecera.Parameters.Add(new NpgsqlParameter("sucursal", NpgsqlDbType.Uuid) { Value = (object?)reserva.SucursalDeRecojoId ?? DBNull.Value });
                        cabecera.Parameters.Add(new NpgsqlParameter("region", NpgsqlDbType.Text) { Value = (object?)reserva.RegionDeEntrega ?? DBNull.Value });
                        cabecera.Parameters.Add(new NpgsqlParameter("iniciada", NpgsqlDbType.TimestampTz) { Value = reserva.IniciadaEn.ToUniversalTime() });
                        cabecera.Parameters.Add(new NpgsqlParameter("expira", NpgsqlDbType.TimestampTz) { Value = reserva.FechaDeExpiracion is { } f ? f.ToUniversalTime() : DBNull.Value });
                        cabecera.Parameters.AddWithValue("extendida", reserva.Extendida);
                        cabecera.Parameters.AddWithValue("reemplazos", reserva.Reemplazos);
                        await cabecera.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    var lineas = reserva.Lineas;
                    await using var comando = new NpgsqlCommand(
                        """
                        INSERT INTO reservas.reserva_lineas
                            (id, linea_id, reserva_id, pedido_id, edicion_id, fuente_id, cantidad, estado, cerrada_por, traslado_id, creada_por_traslado, recreada_en_destino, confirmada_en, anulada_en, estado_cambiado_en)
                        SELECT t.id, t.linea_id, @reserva, @pedido, t.edicion_id, t.fuente_id, t.cantidad, t.estado, t.cerrada_por, t.traslado_id, t.creada_por_traslado, t.recreada_en_destino, t.confirmada_en, t.anulada_en, t.estado_cambiado_en
                        FROM unnest(@ids, @lineas, @ediciones, @fuentes, @cantidades, @estados, @causas, @traslados, @creadas, @recreadas, @confirmadas, @anuladas, @cambiadas)
                            AS t(id, linea_id, edicion_id, fuente_id, cantidad, estado, cerrada_por, traslado_id, creada_por_traslado, recreada_en_destino, confirmada_en, anulada_en, estado_cambiado_en)
                        """,
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("reserva", reserva.Id);
                    comando.Parameters.AddWithValue("pedido", reserva.PedidoId);
                    comando.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineas.Select(l => l.Id).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("lineas", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineas.Select(l => l.LineaId).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("ediciones", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineas.Select(l => l.EdicionId).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("fuentes", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineas.Select(l => l.FuenteId).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("cantidades", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = lineas.Select(l => l.Cantidad).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("estados", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = lineas.Select(l => AEstado(l.Estado)).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("causas", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = lineas.Select(l => l.CerradaPor is { } c ? ACausa(c) : null).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("traslados", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineas.Select(l => l.TrasladoId).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("creadas", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = lineas.Select(l => l.CreadaPorTraslado).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("recreadas", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = lineas.Select(l => l.RecreadaEnDestino).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("confirmadas", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = lineas.Select(l => l.ConfirmadaEn?.ToUniversalTime()).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("anuladas", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = lineas.Select(l => l.AnuladaEn?.ToUniversalTime()).ToArray() });
                    comando.Parameters.Add(new NpgsqlParameter("cambiadas", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = lineas.Select(l => l.EstadoCambiadoEn.ToUniversalTime()).ToArray() });
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return 0;
                }
                catch (PostgresException ex) when (ex.SqlState == ViolacionDeUnicidad)
                {
                    // La transacción queda abortada: el caso de uso termina con un fallo y la unidad de trabajo la revierte.
                    throw new ReservaDuplicadaException(ex);
                }
            },
            cancelacion);
    }

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        return acceso.Ejecutar<IReadOnlyList<UnidadesVencidas>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"""
                    SELECT l.fuente_id, l.edicion_id, sum(l.cantidad)::integer
                    FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                    WHERE {PendienteVencida} AND l.edicion_id = ANY(@ids)
                    GROUP BY l.edicion_id, l.fuente_id
                    ORDER BY l.edicion_id, l.fuente_id
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ids", edicionIds.ToArray());
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });

                var unidades = new List<UnidadesVencidas>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    unidades.Add(new UnidadesVencidas(lector.GetGuid(0), lector.GetGuid(1), lector.GetInt32(2)));
                }

                return unidades;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Guid>> PedidosConVencidas(IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"""
                    SELECT DISTINCT l.pedido_id
                    FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                    WHERE {PendienteVencida}{(edicionIds is null ? string.Empty : " AND l.edicion_id = ANY(@ids)")}
                    ORDER BY l.pedido_id
                    """,
                    conexion,
                    transaccion);
                if (edicionIds is not null)
                {
                    comando.Parameters.AddWithValue("ids", edicionIds.ToArray());
                }

                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });

                var pedidos = new List<Guid>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    pedidos.Add(lector.GetGuid(0));
                }

                return pedidos;
            },
            cancelacion);

    public Task<IReadOnlyList<LineaVencida>> VencidasDelPedido(Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<LineaVencida>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"""
                    SELECT l.pedido_id, l.id, l.linea_id, l.edicion_id, l.fuente_id, l.cantidad
                    FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                    WHERE l.pedido_id = @pedido AND {PendienteVencida}
                    ORDER BY l.id
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                return await LeerVencidas(comando, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<IReadOnlyList<LineaVencida>> ExpirarLineasVencidas(Guid pedidoId, IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<LineaVencida>>(
            async (conexion, transaccion, token) =>
            {
                // Una sola sentencia: bloquea las líneas por id y en orden, repite la comprobación de vencimiento con @ahora y solo cambia (y devuelve) las que siguen pendientes y vencidas.
                await using var comando = new NpgsqlCommand(
                    $"""
                    WITH candidatas AS (
                        SELECT l.id
                        FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                        WHERE l.pedido_id = @pedido AND {PendienteVencida}{(edicionIds is null ? string.Empty : " AND l.edicion_id = ANY(@ids)")}
                        ORDER BY l.id
                        FOR UPDATE OF l
                    )
                    UPDATE reservas.reserva_lineas AS u
                    SET estado = 'expirada', estado_cambiado_en = @ahora
                    FROM candidatas c, reservas.reservas r
                    WHERE u.id = c.id AND u.estado = 'pendiente' AND r.id = u.reserva_id AND r.fecha_expiracion IS NOT NULL AND r.fecha_expiracion <= @ahora
                    RETURNING u.pedido_id, u.id, u.linea_id, u.edicion_id, u.fuente_id, u.cantidad
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                if (edicionIds is not null)
                {
                    comando.Parameters.AddWithValue("ids", edicionIds.ToArray());
                }

                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                return await LeerVencidas(comando, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<IReadOnlyList<LineaLiberada>> LiberarLineas(Guid pedidoId, bool incluirConfirmadas, IReadOnlyCollection<Guid>? lineaIds, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<LineaLiberada>>(
            async (conexion, transaccion, token) =>
            {
                // Una sola sentencia: bloquea por id y en orden las líneas activas del pedido y solo cambia (y devuelve) las que seguían en ese estado.
                var estados = incluirConfirmadas ? "('pendiente', 'confirmada')" : "('pendiente')";
                await using var comando = new NpgsqlCommand(
                    $"""
                    WITH candidatas AS (
                        SELECT l.id, l.estado AS estado_previo
                        FROM reservas.reserva_lineas l
                        WHERE l.pedido_id = @pedido AND l.estado IN {estados}{(lineaIds is null ? string.Empty : " AND l.linea_id = ANY(@lineas)")}
                        ORDER BY l.id
                        FOR UPDATE OF l
                    )
                    UPDATE reservas.reserva_lineas AS u
                    SET estado = 'liberada', estado_cambiado_en = @ahora
                    FROM candidatas c
                    WHERE u.id = c.id AND u.estado = c.estado_previo
                    RETURNING u.pedido_id, u.id, u.linea_id, u.edicion_id, u.fuente_id, u.cantidad, u.traslado_id, c.estado_previo = 'confirmada'
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                if (lineaIds is not null)
                {
                    comando.Parameters.AddWithValue("lineas", lineaIds.ToArray());
                }

                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });

                var lineas = new List<LineaLiberada>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    lineas.Add(new LineaLiberada(lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetGuid(3), lector.GetGuid(4), lector.GetInt32(5), lector.IsDBNull(6) ? null : lector.GetGuid(6), lector.GetBoolean(7)));
                }

                return [.. lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.FuenteId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.LineaDeReservaId, OrdenDeBloqueo.DeIdentificadores)];
            },
            cancelacion);

    public Task<IReadOnlyList<LineaConfirmada>> ConfirmarLineas(Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<LineaConfirmada>>(
            async (conexion, transaccion, token) =>
            {
                // Una sola sentencia: bloquea por id y en orden las pendientes del pedido cuya cabecera todavía no venció y solo cambia (y devuelve) las que siguen así.
                await using var comando = new NpgsqlCommand(
                    """
                    WITH candidatas AS (
                        SELECT l.id
                        FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                        WHERE l.pedido_id = @pedido AND l.estado = 'pendiente' AND r.fecha_expiracion IS NOT NULL AND r.fecha_expiracion > @ahora
                        ORDER BY l.id
                        FOR UPDATE OF l
                    )
                    UPDATE reservas.reserva_lineas AS u
                    SET estado = 'confirmada', confirmada_en = @ahora, estado_cambiado_en = @ahora
                    FROM candidatas c, reservas.reservas r
                    WHERE u.id = c.id AND u.estado = 'pendiente' AND r.id = u.reserva_id AND r.fecha_expiracion > @ahora
                    RETURNING u.pedido_id, u.id, u.linea_id, u.edicion_id, u.fuente_id, u.cantidad, u.reserva_id
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });

                var lineas = new List<LineaConfirmada>();
                var cabeceras = new HashSet<Guid>();
                await using (var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await lector.ReadAsync(token).ConfigureAwait(false))
                    {
                        lineas.Add(new LineaConfirmada(lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetGuid(3), lector.GetGuid(4), lector.GetInt32(5)));
                        cabeceras.Add(lector.GetGuid(6));
                    }
                }

                if (cabeceras.Count > 0)
                {
                    // Confirmada, la reserva ya no expira.
                    await using var sinExpiracion = new NpgsqlCommand("UPDATE reservas.reservas SET fecha_expiracion = NULL WHERE id = ANY(@ids)", conexion, transaccion);
                    sinExpiracion.Parameters.AddWithValue("ids", cabeceras.ToArray());
                    await sinExpiracion.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return [.. lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.FuenteId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.LineaDeReservaId, OrdenDeBloqueo.DeIdentificadores)];
            },
            cancelacion);

    public Task VincularTraslados(IReadOnlyCollection<(Guid LineaDeReservaId, Guid TrasladoId)> vinculos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(vinculos);
        if (vinculos.Count == 0)
        {
            return Task.CompletedTask;
        }

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE reservas.reserva_lineas AS l
                    SET traslado_id = v.traslado_id
                    FROM unnest(@lineas, @traslados) AS v(linea_id, traslado_id)
                    WHERE l.id = v.linea_id AND l.traslado_id IS DISTINCT FROM v.traslado_id
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("lineas", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = vinculos.Select(v => v.LineaDeReservaId).ToArray() });
                comando.Parameters.Add(new NpgsqlParameter("traslados", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = vinculos.Select(v => v.TrasladoId).ToArray() });
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public async Task<bool> ExtenderExpiracion(Guid reservaId, DateTimeOffset nuevaExpiracion, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        await acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "UPDATE reservas.reservas SET fecha_expiracion = @nueva, extendida = true WHERE id = @id AND NOT extendida AND fecha_expiracion IS NOT NULL AND fecha_expiracion > @ahora",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", reservaId);
                comando.Parameters.Add(new NpgsqlParameter("nueva", NpgsqlDbType.TimestampTz) { Value = nuevaExpiracion.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion).ConfigureAwait(false);

    public Task<IReadOnlyList<LineaLiberada>> LiberarLineasPorId(IReadOnlyCollection<Guid> lineaDeReservaIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lineaDeReservaIds);
        if (lineaDeReservaIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<LineaLiberada>>([]);
        }

        return acceso.Ejecutar<IReadOnlyList<LineaLiberada>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    WITH candidatas AS (
                        SELECT l.id, l.estado AS estado_previo
                        FROM reservas.reserva_lineas l
                        WHERE l.id = ANY(@ids) AND l.estado IN ('pendiente', 'confirmada')
                        ORDER BY l.id
                        FOR UPDATE OF l
                    )
                    UPDATE reservas.reserva_lineas AS u
                    SET estado = 'liberada', estado_cambiado_en = @ahora
                    FROM candidatas c
                    WHERE u.id = c.id AND u.estado = c.estado_previo
                    RETURNING u.pedido_id, u.id, u.linea_id, u.edicion_id, u.fuente_id, u.cantidad, u.traslado_id, c.estado_previo = 'confirmada'
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ids", lineaDeReservaIds.ToArray());
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                return await LeerLiberadas(comando, token).ConfigureAwait(false);
            },
            cancelacion);
    }

    public Task<IReadOnlyList<LineaLiberada>> AnularVentas(IReadOnlyCollection<Guid> lineaDeReservaIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lineaDeReservaIds);
        if (lineaDeReservaIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<LineaLiberada>>([]);
        }

        return acceso.Ejecutar<IReadOnlyList<LineaLiberada>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    WITH candidatas AS (
                        SELECT l.id
                        FROM reservas.reserva_lineas l
                        WHERE l.id = ANY(@ids) AND l.estado = 'cerrada' AND l.cerrada_por = 'traslado' AND l.anulada_en IS NULL
                        ORDER BY l.id
                        FOR UPDATE OF l
                    )
                    UPDATE reservas.reserva_lineas AS u
                    SET anulada_en = @ahora
                    FROM candidatas c
                    WHERE u.id = c.id AND u.anulada_en IS NULL
                    RETURNING u.pedido_id, u.id, u.linea_id, u.edicion_id, u.fuente_id, u.cantidad, u.traslado_id, false
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ids", lineaDeReservaIds.ToArray());
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                return await LeerLiberadas(comando, token).ConfigureAwait(false);
            },
            cancelacion);
    }

    private static async Task<IReadOnlyList<LineaLiberada>> LeerLiberadas(NpgsqlCommand comando, CancellationToken cancelacion)
    {
        var lineas = new List<LineaLiberada>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            lineas.Add(new LineaLiberada(lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetGuid(3), lector.GetGuid(4), lector.GetInt32(5), lector.IsDBNull(6) ? null : lector.GetGuid(6), lector.GetBoolean(7)));
        }

        return [.. lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.FuenteId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.LineaDeReservaId, OrdenDeBloqueo.DeIdentificadores)];
    }

    public Task<OperacionGuardada?> ObtenerOperacion(Guid pedidoId, string tipo, string clave, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT huella_de_carga, coalesce(resultado::text, '') FROM reservas.operaciones_idempotentes WHERE pedido_id = @p AND tipo = @t AND clave = @c",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("p", pedidoId);
                comando.Parameters.AddWithValue("t", tipo);
                comando.Parameters.AddWithValue("c", clave);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? new OperacionGuardada(lector.GetString(0), lector.GetString(1)) : null;
            },
            cancelacion);

    public Task GuardarOperacion(Guid pedidoId, string tipo, string clave, string huellaDeLaCarga, string resultado, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                try
                {
                    await using var comando = new NpgsqlCommand(
                        "INSERT INTO reservas.operaciones_idempotentes (pedido_id, tipo, clave, huella_de_carga, resultado, creada_en) VALUES (@p, @t, @c, @h, @r::jsonb, now())",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("p", pedidoId);
                    comando.Parameters.AddWithValue("t", tipo);
                    comando.Parameters.AddWithValue("c", clave);
                    comando.Parameters.AddWithValue("h", huellaDeLaCarga);
                    comando.Parameters.AddWithValue("r", resultado);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return 0;
                }
                catch (PostgresException ex) when (ex.SqlState == ViolacionDeUnicidad)
                {
                    // El bloqueo del pedido ya serializa las operaciones: si igual llegó otra con esta clave, es un conflicto y no un error del servidor.
                    throw new ReservaDuplicadaException(ex);
                }
            },
            cancelacion);

    public Task<IReadOnlyList<LineaCerrada>> CerrarLineas(IReadOnlyCollection<Guid> lineaDeReservaIds, CausaDeCierre causa, Guid? envioId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lineaDeReservaIds);
        if (lineaDeReservaIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<LineaCerrada>>([]);
        }

        return acceso.Ejecutar<IReadOnlyList<LineaCerrada>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    WITH candidatas AS (
                        SELECT l.id
                        FROM reservas.reserva_lineas l
                        WHERE l.id = ANY(@ids) AND l.estado = 'confirmada'
                        ORDER BY l.id
                        FOR UPDATE OF l
                    )
                    UPDATE reservas.reserva_lineas AS u
                    SET estado = 'cerrada', cerrada_por = @causa, envio_id = @envio, estado_cambiado_en = @ahora
                    FROM candidatas c
                    WHERE u.id = c.id AND u.estado = 'confirmada'
                    RETURNING u.pedido_id, u.id, u.linea_id, u.edicion_id, u.fuente_id, u.cantidad
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ids", lineaDeReservaIds.ToArray());
                comando.Parameters.AddWithValue("causa", ACausa(causa));
                comando.Parameters.Add(new NpgsqlParameter("envio", NpgsqlDbType.Uuid) { Value = (object?)envioId ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });

                var lineas = new List<LineaCerrada>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    lineas.Add(new LineaCerrada(lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetGuid(3), lector.GetGuid(4), lector.GetInt32(5)));
                }

                return [.. lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.FuenteId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.LineaDeReservaId, OrdenDeBloqueo.DeIdentificadores)];
            },
            cancelacion);
    }

    public Task<IReadOnlyDictionary<Guid, Guid?>> EnviosDeCierre(IReadOnlyCollection<Guid> lineaDeReservaIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lineaDeReservaIds);
        if (lineaDeReservaIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(new Dictionary<Guid, Guid?>());
        }

        return acceso.Ejecutar<IReadOnlyDictionary<Guid, Guid?>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT id, envio_id FROM reservas.reserva_lineas WHERE id = ANY(@ids)", conexion, transaccion);
                comando.Parameters.AddWithValue("ids", lineaDeReservaIds.ToArray());
                var envios = new Dictionary<Guid, Guid?>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    envios[lector.GetGuid(0)] = lector.IsDBNull(1) ? null : lector.GetGuid(1);
                }

                return envios;
            },
            cancelacion);
    }

    public Task BloquearLineas(IReadOnlyCollection<Guid> lineaDeReservaIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lineaDeReservaIds);
        if (lineaDeReservaIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT id FROM reservas.reserva_lineas WHERE id = ANY(@ids) ORDER BY id FOR UPDATE", conexion, transaccion);
                comando.Parameters.AddWithValue("ids", lineaDeReservaIds.ToArray());
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                }

                return 0;
            },
            cancelacion);
    }

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximo);

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Aparta unidades la línea confirmada y la pendiente que todavía no venció. Solo identificadores: nada de datos del cliente.
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT pedido_id, count(*) OVER ()
                    FROM (
                        SELECT DISTINCT l.pedido_id
                        FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                        WHERE l.fuente_id = @fuente AND l.edicion_id = @edicion
                          AND (l.estado = 'confirmada' OR (l.estado = 'pendiente' AND (r.fecha_expiracion IS NULL OR r.fecha_expiracion > @ahora)))
                    ) t
                    ORDER BY pedido_id
                    LIMIT @maximo
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("fuente", fuenteId);
                comando.Parameters.AddWithValue("edicion", edicionId);
                comando.Parameters.AddWithValue("maximo", maximo);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });

                var pedidos = new List<Guid>();
                var total = 0;
                await using (var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await lector.ReadAsync(token).ConfigureAwait(false))
                    {
                        pedidos.Add(lector.GetGuid(0));
                        total = (int)lector.GetInt64(1);
                    }
                }

                // Con un máximo de cero no se lee ninguna fila: el total se cuenta aparte.
                if (pedidos.Count == 0)
                {
                    await using var conteo = new NpgsqlCommand(
                        """
                        SELECT count(DISTINCT l.pedido_id)
                        FROM reservas.reserva_lineas l JOIN reservas.reservas r ON r.id = l.reserva_id
                        WHERE l.fuente_id = @fuente AND l.edicion_id = @edicion
                          AND (l.estado = 'confirmada' OR (l.estado = 'pendiente' AND (r.fecha_expiracion IS NULL OR r.fecha_expiracion > @ahora)))
                        """,
                        conexion,
                        transaccion);
                    conteo.Parameters.AddWithValue("fuente", fuenteId);
                    conteo.Parameters.AddWithValue("edicion", edicionId);
                    conteo.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                    total = (int)(long)(await conteo.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                return new PedidosQueApartanUnidades(pedidos, total);
            },
            cancelacion);
    }

    private static async Task<IReadOnlyList<LineaVencida>> LeerVencidas(NpgsqlCommand comando, CancellationToken cancelacion)
    {
        var lineas = new List<LineaVencida>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            lineas.Add(new LineaVencida(lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetGuid(3), lector.GetGuid(4), lector.GetInt32(5)));
        }

        // Un orden estable para quien agrega: por edición, fuente e id, el mismo de los bloqueos.
        return [.. lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.FuenteId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.LineaDeReservaId, OrdenDeBloqueo.DeIdentificadores)];
    }

    private static async Task<List<LineaDeReserva>> LeerLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid reservaId, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand($"SELECT {ColumnasDeLinea} FROM reservas.reserva_lineas WHERE reserva_id = @r ORDER BY edicion_id, linea_id", conexion, transaccion);
        comando.Parameters.AddWithValue("r", reservaId);

        var lineas = new List<LineaDeReserva>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            lineas.Add(LineaDeReserva.Rehidratar(
                lector.GetGuid(0),
                lector.GetGuid(1),
                lector.GetGuid(2),
                lector.GetGuid(3),
                lector.GetInt32(4),
                AEstadoDeLinea(lector.GetString(5)),
                lector.IsDBNull(6) ? null : ACausaDeCierre(lector.GetString(6)),
                lector.IsDBNull(7) ? null : lector.GetGuid(7),
                lector.GetBoolean(8),
                lector.GetBoolean(9),
                lector.IsDBNull(10) ? null : lector.GetFieldValue<DateTimeOffset>(10),
                lector.IsDBNull(11) ? null : lector.GetFieldValue<DateTimeOffset>(11),
                lector.GetFieldValue<DateTimeOffset>(12)));
        }

        return lineas;
    }

    private static TipoDeEntrega ATipo(string tipo) => tipo switch
    {
        "recojo" => TipoDeEntrega.Recojo,
        "domicilio" => TipoDeEntrega.Domicilio,
        _ => throw new InvalidOperationException("La reserva guardada tiene un tipo de entrega desconocido."),
    };

    private static string AEstado(EstadoDeLinea estado) => estado switch
    {
        EstadoDeLinea.Pendiente => "pendiente",
        EstadoDeLinea.Confirmada => "confirmada",
        EstadoDeLinea.Cerrada => "cerrada",
        EstadoDeLinea.Liberada => "liberada",
        EstadoDeLinea.Expirada => "expirada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    private static EstadoDeLinea AEstadoDeLinea(string estado) => estado switch
    {
        "pendiente" => EstadoDeLinea.Pendiente,
        "confirmada" => EstadoDeLinea.Confirmada,
        "cerrada" => EstadoDeLinea.Cerrada,
        "liberada" => EstadoDeLinea.Liberada,
        "expirada" => EstadoDeLinea.Expirada,
        _ => throw new InvalidOperationException("La línea guardada tiene un estado desconocido."),
    };

    private static string ACausa(CausaDeCierre causa) => causa switch
    {
        CausaDeCierre.Entrega => "entrega",
        CausaDeCierre.Reparto => "reparto",
        CausaDeCierre.Traslado => "traslado",
        _ => throw new ArgumentOutOfRangeException(nameof(causa)),
    };

    private static CausaDeCierre ACausaDeCierre(string causa) => causa switch
    {
        "entrega" => CausaDeCierre.Entrega,
        "reparto" => CausaDeCierre.Reparto,
        "traslado" => CausaDeCierre.Traslado,
        _ => throw new InvalidOperationException("La línea guardada tiene una causa de cierre desconocida."),
    };
}
