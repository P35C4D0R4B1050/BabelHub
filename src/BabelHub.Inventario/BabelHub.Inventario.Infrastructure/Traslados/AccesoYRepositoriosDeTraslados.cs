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
/// llegara a ellos solo vería y escribiría lo de su sucursal y la política le rechazaría el resto. <b>Falla cerrado</b>, como <c>AccesoAFuentes</c>, sin un actor con contexto <b>y</b> con un alcance de datos que la unidad de
/// trabajo pudo fijar para él (un encargado sin sucursal o un rol desconocido tienen actor pero no alcance, y la política les devolvería cero filas sin error) y deja una alerta técnica. Una espera de bloqueo agotada
/// (<c>55P03</c>) es <see cref="EsperaDeBloqueoAgotadaException"/>, no un error del servidor, y el rechazo de la política (<c>42501</c> con la tabla) es <see cref="AlcanceDeDatosDenegadoException"/>. Los errores de integridad
/// y de datos (<c>23xxx</c> y <c>22xxx</c>) se reenvían <b>sin</b> la excepción original: su <c>Detail</c> trae la fila, con el motivo y la guía, y nunca debe registrarse ni propagarse. Un interbloqueo o un fallo de serialización
/// pasan tal cual para que el decorador de reintento los reconozca.
/// </summary>
internal sealed class AccesoATraslados(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion, IActorActual actores, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    /// <summary>El motivo de la alerta técnica cuando se intenta tocar los traslados sin actor o sin alcance de datos (consta en el catálogo de motivos de alertas técnicas).</summary>
    public const string MotivoDeAlertaSinActor = "alcance-de-datos-sin-actor";

    private const string EsperaAgotada = "55P03";

    private const string Privilegios = "42501";

    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeTraslados.Esquema);

    /// <summary>Corre el bloque con el rol del módulo y el alcance de datos del actor, sobre la conexión y la transacción en curso.</summary>
    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        await ExigirActorConAlcance(cancelacion).ConfigureAwait(false);

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
        catch (PostgresException ex) when (ex.SqlState is { } estado && (estado.StartsWith("23", StringComparison.Ordinal) || estado.StartsWith("22", StringComparison.Ordinal)))
        {
            // Sin la excepción original ni su Detail: trae la fila (motivo, guía). Solo el código de estado, que no lleva datos.
            throw new InvalidOperationException($"La base de datos de traslados rechazó la operación (SQLSTATE {estado}).");
        }

        return resultado;
    }

    private async Task ExigirActorConAlcance(CancellationToken cancelacion)
    {
        if (actores.Actual is { Tipo: not TipoDeActor.Anonimo } && await HayAlcanceFijado(cancelacion).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlertaSinActor, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // La alerta no debe tapar el rechazo: si ni siquiera se puede alertar, la operación igual falla cerrada.
        }

        throw new ContextoDeActorAusenteException("Se intentó acceder a los traslados sin un actor con alcance de datos: cada caso de uso autoriza antes, una ruta de servicio establece el actor de integración y un trabajo de fondo, el del sistema.");
    }

    private async Task<bool> HayAlcanceFijado(CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand("SELECT coalesce(current_setting('babelhub.alcance', true), '') <> ''", (NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion);
        return (bool)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
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

    public Task<PaginaDeTrasladosGuardados> Listar(FiltroDeTraslados filtro, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                const string Condicion =
                    """
                    WHERE (@estado::text IS NULL OR estado = @estado)
                      AND (@fuente::uuid IS NULL OR origen_id = @fuente OR destino_id = @fuente)
                      AND (@vinculado::boolean IS NULL OR (pedido_id IS NOT NULL) = @vinculado)
                      AND (@antes::timestamptz IS NULL OR (estado = 'solicitado' AND solicitado_en <= @antes))
                    """;

                void Filtros(NpgsqlCommand comando)
                {
                    comando.Parameters.Add(new NpgsqlParameter("estado", NpgsqlDbType.Text) { Value = filtro.Estado is { } e ? e.Identificador() : DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("fuente", NpgsqlDbType.Uuid) { Value = filtro.FuenteId is { } f ? f : DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("vinculado", NpgsqlDbType.Boolean) { Value = filtro.Vinculado is { } v ? v : DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("antes", NpgsqlDbType.TimestampTz) { Value = filtro.SolicitadoAntesDe is { } a ? a.ToUniversalTime() : DBNull.Value });
                }

                await using var total = new NpgsqlCommand($"SELECT count(*) FROM traslados.traslados {Condicion}", conexion, transaccion);
                Filtros(total);
                var cuenta = (long)(await total.ExecuteScalarAsync(token).ConfigureAwait(false))!;

                await using var comandoPagina = new NpgsqlCommand($"SELECT {Columnas} FROM traslados.traslados {Condicion} ORDER BY solicitado_en DESC, id DESC LIMIT @limite OFFSET @desde", conexion, transaccion);
                Filtros(comandoPagina);
                comandoPagina.Parameters.AddWithValue("limite", tamano);
                comandoPagina.Parameters.AddWithValue("desde", (long)(pagina - 1) * tamano);
                var elementos = new List<Traslado>();
                await using var lector = await comandoPagina.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    elementos.Add(Leer(lector));
                }

                return new PaginaDeTrasladosGuardados(elementos, (int)Math.Min(cuenta, int.MaxValue));
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Traslado>> DelPedido(Guid pedidoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Traslado>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM traslados.traslados WHERE pedido_id = @pedido ORDER BY solicitado_en DESC, id DESC", conexion, transaccion);
                comando.Parameters.AddWithValue("pedido", pedidoId);
                var resultado = new List<Traslado>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    resultado.Add(Leer(lector));
                }

                return resultado;
            },
            cancelacion);


    public Task<IReadOnlyList<Guid>> VinculadosSinAprobarVencidos(DateTimeOffset solicitadosAntesDe, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                // SKIP LOCKED y ORDER BY id: dos instancias del trabajo no toman el mismo traslado y el orden es único.
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT id FROM traslados.traslados
                    WHERE pedido_id IS NOT NULL AND estado = 'solicitado' AND solicitado_en < @antes AND alerta_de_demora_en IS NULL
                    ORDER BY id LIMIT @maximo FOR UPDATE SKIP LOCKED
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("antes", NpgsqlDbType.TimestampTz) { Value = solicitadosAntesDe.ToUniversalTime() });
                comando.Parameters.AddWithValue("maximo", maximo);
                var ids = new List<Guid>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    ids.Add(lector.GetGuid(0));
                }

                return ids;
            },
            cancelacion);

    public Task<bool> MarcarAlertaDeDemora(Guid trasladoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "UPDATE traslados.traslados SET alerta_de_demora_en = @ahora WHERE id = @id AND estado = 'solicitado' AND alerta_de_demora_en IS NULL",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", trasladoId);
                comando.Parameters.Add(Fecha("ahora", ahora));
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);

    public Task<int> AnonimizarCerrados(DateTimeOffset cerradosAntesDe, int lote, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE traslados.traslados
                    SET solicitado_por = NULL, motivo = NULL, motivo_de_cierre = NULL, guia = NULL
                    WHERE id IN (
                        SELECT id FROM traslados.traslados
                        WHERE cerrado_en < @antes AND (solicitado_por IS NOT NULL OR motivo IS NOT NULL OR motivo_de_cierre IS NOT NULL OR guia IS NOT NULL)
                        ORDER BY cerrado_en, id LIMIT @lote)
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.Add(Fecha("antes", cerradosAntesDe));
                comando.Parameters.AddWithValue("lote", lote);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

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
