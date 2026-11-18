using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Traslados;

/// <summary>
/// Cómo entra la Infraestructura de <c>Traslados</c> a su esquema: cada operación corre <b>dentro de la transacción de la unidad de trabajo</b> y con <c>SET LOCAL ROLE rol_traslados</c> (<c>EjecutarComo</c>), con el <b>alcance de
/// datos del actor</b> que fijó la unidad de trabajo: la política de filas de <c>traslados</c> (origen o destino igual al alcance) es la segunda capa del alcance por sucursal. <b>No usa la elevación a la red</b> (solo <c>Fuentes</c> y
/// <c>Ajustes</c> pueden, D09): los puertos de <c>Reservas</c> y los trabajos de fondo corren con el alcance <c>red</c> que la lista blanca da al actor sistema, integración o credencial de servicio, de modo que un encargado que
/// llegara a ellos solo vería y escribiría lo de su sucursal y la política le rechazaría el resto. Sin actor con contexto <b>falla cerrado</b>, como <c>AccesoAFuentes</c>: la política devolvería cero filas sin error. Una espera
/// de bloqueo agotada (<c>55P03</c>) es <see cref="EsperaDeBloqueoAgotadaException"/>, no un error del servidor, y el rechazo de la política (<c>42501</c> con la tabla) es <see cref="AlcanceDeDatosDenegadoException"/>.
/// <c>PostgresException.Detail</c> (que trae la fila, con el motivo y la guía) nunca se registra ni se propaga.
/// </summary>
internal sealed class AccesoATraslados(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion, IActorActual actores)
{
    private const string EsperaAgotada = "55P03";

    private const string Privilegios = "42501";

    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeTraslados.Esquema);

    /// <summary>Corre el bloque con el rol del módulo y el alcance de datos del actor, sobre la conexión y la transacción en curso.</summary>
    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        if (actores.Actual is not { Tipo: not TipoDeActor.Anonimo })
        {
            throw new ContextoDeActorAusenteException("Se intentó acceder a los traslados sin un actor con contexto: cada caso de uso autoriza antes y un trabajo de fondo corre como sistema.");
        }

        var conexionActiva = (NpgsqlConnection)conexion.Conexion;
        var transaccion = (NpgsqlTransaction)conexion.Transaccion;
        T resultado = default!;

        try
        {
            await unidad.EjecutarComo(
                Esquema,
                async token => resultado = await bloque(conexionActiva, transaccion, token).ConfigureAwait(false),
                cancelacion).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == EsperaAgotada)
        {
            throw new EsperaDeBloqueoAgotadaException(ex);
        }
        catch (PostgresException ex) when (ex.SqlState == Privilegios && (!string.IsNullOrEmpty(ex.TableName) || ex.MessageText.Contains("row-level security", StringComparison.OrdinalIgnoreCase)))
        {
            // La política de filas rechazó la escritura (un permiso de tabla que falta no trae el nombre de la tabla). Sin el Detail de la excepción: trae la fila.
            throw new AlcanceDeDatosDenegadoException();
        }

        return resultado;
    }
}

/// <summary>
/// <see cref="IRepositorioDeTraslados"/> sobre <c>traslados.traslados</c>: SQL explícito, siempre con parámetros. Las transiciones son <b>condicionales por la versión</b> (<c>WHERE id = @id AND version = @anterior</c>), el bloqueo es
/// <c>FOR UPDATE</c> (por identificador y ordenado cuando son varios) y el traslado vinculado se inserta con <c>ON CONFLICT DO NOTHING</c>, de modo que repetir una confirmación no aborta su transacción (plan §5).
/// </summary>
internal sealed class NpgsqlRepositorioDeTraslados(AccesoATraslados acceso) : IRepositorioDeTraslados
{
    private const string Columnas =
        "id, numero, edicion_id, origen_id, destino_id, cantidad, estado, motivo, motivo_de_cierre, pedido_id, pedido_anulado, fecha_estimada, estimada_por_confirmar, guia, solicitado_por, solicitado_en, aprobado_en, despachado_en, recibido_en, cerrado_en, alerta_de_demora_en, version";

    private const string ColumnasDeInsercion =
        "id, edicion_id, origen_id, destino_id, cantidad, estado, motivo, motivo_de_cierre, pedido_id, pedido_anulado, fecha_estimada, estimada_por_confirmar, guia, solicitado_por, solicitado_en, aprobado_en, despachado_en, recibido_en, cerrado_en, version";

    private const string ValoresDeInsercion =
        "@id, @edicion, @origen, @destino, @cantidad, @estado, @motivo, @motivoCierre, @pedido, @anulado, @fechaEstimada, @porConfirmar, @guia, @solicitadoPor, @solicitadoEn, @aprobadoEn, @despachadoEn, @recibidoEn, @cerradoEn, @version";

    public async Task Insertar(Traslado traslado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(traslado);
        var numero = await acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"INSERT INTO traslados.traslados ({ColumnasDeInsercion}) VALUES ({ValoresDeInsercion}) RETURNING numero", conexion, transaccion);
                Parametros(comando, traslado);
                return (long)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion).ConfigureAwait(false);
        traslado.AsignarNumero(numero);
    }

    public async Task<bool> InsertarVinculado(Traslado traslado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(traslado);
        var numero = await acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Sin el índice nombrado: cualquier violación de unicidad (el único parcial por pedido) se descarta sin abortar la transacción de la confirmación.
                await using var comando = new NpgsqlCommand($"INSERT INTO traslados.traslados ({ColumnasDeInsercion}) VALUES ({ValoresDeInsercion}) ON CONFLICT DO NOTHING RETURNING numero", conexion, transaccion);
                Parametros(comando, traslado);
                return (long?)await comando.ExecuteScalarAsync(token).ConfigureAwait(false);
            },
            cancelacion).ConfigureAwait(false);

        if (numero is null)
        {
            return false;
        }

        traslado.AsignarNumero(numero.Value);
        return true;
    }

    public Task<Traslado?> PorId(Guid trasladoId, CancellationToken cancelacion = default) =>
        Uno("WHERE id = @id", trasladoId, cancelacion);

    public Task<Traslado?> PorIdBloqueado(Guid trasladoId, CancellationToken cancelacion = default) =>
        Uno("WHERE id = @id FOR UPDATE", trasladoId, cancelacion);

    public Task<Traslado?> VinculadoActivo(Guid pedidoId, Guid edicionId, Guid origenId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"SELECT {Columnas} FROM traslados.traslados WHERE pedido_id = @pedido AND edicion_id = @edicion AND origen_id = @origen AND estado <> 'cancelado'",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                comando.Parameters.AddWithValue("edicion", edicionId);
                comando.Parameters.AddWithValue("origen", origenId);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? Leer(lector) : null;
            },
            cancelacion);

    public Task<IReadOnlyList<Traslado>> PorIdsBloqueados(IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(trasladoIds);
        return acceso.Ejecutar<IReadOnlyList<Traslado>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM traslados.traslados WHERE id = ANY (@ids) ORDER BY id FOR UPDATE", conexion, transaccion);
                comando.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = trasladoIds.ToArray() });
                var resultado = new List<Traslado>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    resultado.Add(Leer(lector));
                }

                return resultado;
            },
            cancelacion);
    }

    public Task<bool> Guardar(Traslado traslado, long versionAnterior, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(traslado);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Una sola sentencia condicional por la versión anterior: la que llega segunda actualiza cero filas. Solo las columnas que cambian (el rol del módulo no puede cambiar el origen, el destino, la cantidad ni el pedido).
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE traslados.traslados
                    SET estado = @estado, motivo_de_cierre = @motivoCierre, pedido_anulado = @anulado, guia = @guia,
                        aprobado_en = @aprobadoEn, despachado_en = @despachadoEn, recibido_en = @recibidoEn, cerrado_en = @cerradoEn, version = @version
                    WHERE id = @id AND version = @anterior
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", traslado.Id);
                comando.Parameters.AddWithValue("anterior", versionAnterior);
                comando.Parameters.AddWithValue("estado", traslado.Estado.Identificador());
                comando.Parameters.Add(new NpgsqlParameter("motivoCierre", NpgsqlDbType.Text) { Value = (object?)traslado.MotivoDeCierre ?? DBNull.Value });
                comando.Parameters.AddWithValue("anulado", traslado.PedidoAnulado);
                comando.Parameters.Add(new NpgsqlParameter("guia", NpgsqlDbType.Text) { Value = (object?)traslado.Guia ?? DBNull.Value });
                comando.Parameters.Add(Fecha("aprobadoEn", traslado.AprobadoEn));
                comando.Parameters.Add(Fecha("despachadoEn", traslado.DespachadoEn));
                comando.Parameters.Add(Fecha("recibidoEn", traslado.RecibidoEn));
                comando.Parameters.Add(Fecha("cerradoEn", traslado.CerradoEn));
                comando.Parameters.AddWithValue("version", traslado.Version);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    private Task<Traslado?> Uno(string condicion, Guid id, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM traslados.traslados {condicion}", conexion, transaccion);
                comando.Parameters.AddWithValue("id", id);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? Leer(lector) : null;
            },
            cancelacion);

    private static void Parametros(NpgsqlCommand comando, Traslado t)
    {
        comando.Parameters.AddWithValue("id", t.Id);
        comando.Parameters.AddWithValue("edicion", t.EdicionId);
        comando.Parameters.AddWithValue("origen", t.OrigenId);
        comando.Parameters.AddWithValue("destino", t.DestinoId);
        comando.Parameters.AddWithValue("cantidad", t.Cantidad);
        comando.Parameters.AddWithValue("estado", t.Estado.Identificador());
        comando.Parameters.Add(new NpgsqlParameter("motivo", NpgsqlDbType.Text) { Value = (object?)t.Motivo ?? DBNull.Value });
        comando.Parameters.Add(new NpgsqlParameter("motivoCierre", NpgsqlDbType.Text) { Value = (object?)t.MotivoDeCierre ?? DBNull.Value });
        comando.Parameters.Add(new NpgsqlParameter("pedido", NpgsqlDbType.Uuid) { Value = (object?)t.PedidoId ?? DBNull.Value });
        comando.Parameters.AddWithValue("anulado", t.PedidoAnulado);
        comando.Parameters.Add(new NpgsqlParameter("fechaEstimada", NpgsqlDbType.Date) { Value = t.FechaEstimada is { } f ? f : DBNull.Value });
        comando.Parameters.AddWithValue("porConfirmar", t.EstimadaPorConfirmar);
        comando.Parameters.Add(new NpgsqlParameter("guia", NpgsqlDbType.Text) { Value = (object?)t.Guia ?? DBNull.Value });
        comando.Parameters.Add(new NpgsqlParameter("solicitadoPor", NpgsqlDbType.Uuid) { Value = (object?)t.SolicitadoPor ?? DBNull.Value });
        comando.Parameters.Add(Fecha("solicitadoEn", t.SolicitadoEn));
        comando.Parameters.Add(Fecha("aprobadoEn", t.AprobadoEn));
        comando.Parameters.Add(Fecha("despachadoEn", t.DespachadoEn));
        comando.Parameters.Add(Fecha("recibidoEn", t.RecibidoEn));
        comando.Parameters.Add(Fecha("cerradoEn", t.CerradoEn));
        comando.Parameters.AddWithValue("version", t.Version);
    }

    private static NpgsqlParameter Fecha(string nombre, DateTimeOffset? valor) =>
        new(nombre, NpgsqlDbType.TimestampTz) { Value = valor is { } v ? v.ToUniversalTime() : DBNull.Value };

    private static Traslado Leer(NpgsqlDataReader l) =>
        Traslado.Rehidratar(
            l.GetGuid(0),
            l.GetInt64(1),
            l.GetGuid(2),
            l.GetGuid(3),
            l.GetGuid(4),
            l.GetInt32(5),
            EstadosDeTraslado.DeIdentificador(l.GetString(6)) ?? throw new InvalidOperationException("El estado guardado no es de la lista."),
            l.IsDBNull(7) ? null : l.GetString(7),
            l.IsDBNull(8) ? null : l.GetString(8),
            l.IsDBNull(9) ? null : l.GetGuid(9),
            l.GetBoolean(10),
            l.IsDBNull(11) ? null : l.GetFieldValue<DateOnly>(11),
            l.GetBoolean(12),
            l.IsDBNull(13) ? null : l.GetString(13),
            l.IsDBNull(14) ? null : l.GetGuid(14),
            l.GetFieldValue<DateTimeOffset>(15),
            l.IsDBNull(16) ? null : l.GetFieldValue<DateTimeOffset>(16),
            l.IsDBNull(17) ? null : l.GetFieldValue<DateTimeOffset>(17),
            l.IsDBNull(18) ? null : l.GetFieldValue<DateTimeOffset>(18),
            l.IsDBNull(19) ? null : l.GetFieldValue<DateTimeOffset>(19),
            l.IsDBNull(20) ? null : l.GetFieldValue<DateTimeOffset>(20),
            l.GetInt64(21));
}

/// <summary>
/// <see cref="IBuzonDeSalida"/> sobre <c>traslados.mensajes_salientes</c>: <c>INSERT … ON CONFLICT DO NOTHING</c> en la transacción de quien confirma, de modo que un duplicado (el mismo evento para la misma sucursal y tipo) no
/// aborta la transacción (plan §5). La carga llega ya como JSON sin texto libre; la primera fecha de entrega es la de creación.
/// </summary>
internal sealed class NpgsqlBuzonDeSalida(AccesoATraslados acceso) : IBuzonDeSalida
{
    public Task Insertar(IReadOnlyCollection<MensajeSaliente> mensajes, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(mensajes);
        if (mensajes.Count == 0)
        {
            return Task.CompletedTask;
        }

        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                foreach (var mensaje in mensajes)
                {
                    await using var comando = new NpgsqlCommand(
                        """
                        INSERT INTO traslados.mensajes_salientes (id, tipo, clave_de_evento, sucursal_codigo, carga, estado, intentos, proxima_fecha, creado_en)
                        VALUES (@id, @tipo, @clave, @sucursal, @carga::jsonb, 'pendiente', 0, @creado, @creado)
                        ON CONFLICT DO NOTHING
                        """,
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("id", mensaje.Id);
                    comando.Parameters.AddWithValue("tipo", mensaje.Tipo);
                    comando.Parameters.AddWithValue("clave", mensaje.ClaveDeEvento);
                    comando.Parameters.AddWithValue("sucursal", mensaje.SucursalCodigo);
                    comando.Parameters.AddWithValue("carga", mensaje.Carga);
                    comando.Parameters.Add(new NpgsqlParameter("creado", NpgsqlDbType.TimestampTz) { Value = mensaje.CreadoEn.ToUniversalTime() });
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }
}
