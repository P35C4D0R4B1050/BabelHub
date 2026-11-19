using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Domain.Envios;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Envios;

/// <summary>
/// El repositorio de los envíos sobre el esquema <c>envios</c> (T12). Cada lectura <c>paraActualizar</c> es <c>SELECT … FOR UPDATE</c> con un <c>lock_timeout</c> de 5 segundos (un tiempo agotado es «no disponible»); <b>nada
/// guarda ni lee nombre, dirección ni teléfono</b> (ENV-15). <c>Agregar</c> usa un <c>SAVEPOINT</c> para que el único <c>(linea_id)</c> no aborte la transacción del llamador (Pedidos), y el único
/// <c>(operador, numero_de_guia)</c> se traduce a <see cref="GuiaRepetidaException"/>. Todos los valores van por parámetros.
/// </summary>
internal sealed class RepositorioDeEnviosPostgres(AccesoAlEsquemaDeEnvios acceso) : IRepositorioDeEnvios
{
    private const string Columnas =
        "id, pedido_id, codigo_de_fuente, estado, numero_de_guia, operador, clave_de_solicitud, guia_en_curso_hasta, despacho_iniciado_en, despacho_en_curso_hasta, despacho_incierto_en, creado_en, guia_generada_en, "
        + "en_reparto_en, entregado_en, cancelado_en, progreso_en_reparto_informado_en, progreso_entregado_informado_en, version";

    public Task<ResultadoDeAgregadoDeEnvio> Agregar(Envio envio, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(envio);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await transaccion.SaveAsync("agregar_envio", token).ConfigureAwait(false);
                try
                {
                    await using (var insertar = new NpgsqlCommand(
                        $"INSERT INTO envios.envios ({Columnas}) VALUES (@id, @pedido, @fuente, @estado, @numero, @operador, @clave, @guiaEnCurso, @despachoIniciado, @despachoEnCurso, @despachoIncierto, @creado, "
                        + "@guiaGenerada, @enReparto, @entregado, @cancelado, @progresoReparto, @progresoEntrega, @version)",
                        conexion,
                        transaccion))
                    {
                        Parametros(insertar, envio);
                        await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    var posicion = 0;
                    foreach (var linea in envio.Lineas)
                    {
                        await using var comando = new NpgsqlCommand("INSERT INTO envios.envio_lineas (linea_id, envio_id, pedido_id, posicion) VALUES (@linea, @envio, @pedido, @posicion)", conexion, transaccion);
                        comando.Parameters.AddWithValue("linea", linea.LineaId);
                        comando.Parameters.AddWithValue("envio", envio.Id);
                        comando.Parameters.AddWithValue("pedido", envio.PedidoId);
                        comando.Parameters.AddWithValue("posicion", posicion++);
                        await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await transaccion.ReleaseAsync("agregar_envio", token).ConfigureAwait(false);
                    return ResultadoDeAgregadoDeEnvio.Agregado;
                }
                catch (PostgresException excepcion) when (excepcion.SqlState == AccesoAlEsquemaDeEnvios.ViolacionDeUnicidad && excepcion.ConstraintName == "envio_lineas_pkey")
                {
                    // Otra transacción ganó el único (linea_id): se deshace solo este intento y la del llamador sigue viva.
                    await transaccion.RollbackAsync("agregar_envio", token).ConfigureAwait(false);
                    return ResultadoDeAgregadoDeEnvio.LineaRepetida;
                }
                catch (PostgresException excepcion) when (excepcion.SqlState == AccesoAlEsquemaDeEnvios.ViolacionDeUnicidad && excepcion.ConstraintName == "envios_guia_unica")
                {
                    await transaccion.RollbackAsync("agregar_envio", token).ConfigureAwait(false);
                    throw new GuiaRepetidaException("guia-repetida");
                }
            },
            cancelacion);
    }

    public Task<Envio?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                (await Leer(conexion, transaccion, "id = @valor", [new NpgsqlParameter("valor", id)], string.Empty, paraActualizar, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<IReadOnlyList<Envio>> ObtenerDelPedido(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => Leer(conexion, transaccion, "pedido_id = @valor", [new NpgsqlParameter("valor", pedidoId)], " ORDER BY id", paraActualizar, token),
            cancelacion);

    public Task<IReadOnlyList<Envio>> BuscarPorLineas(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => Leer(
                conexion,
                transaccion,
                "id IN (SELECT envio_id FROM envios.envio_lineas WHERE linea_id = ANY(@valor))",
                [new NpgsqlParameter("valor", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = lineaIds.ToArray() }],
                " ORDER BY id",
                false,
                token),
            cancelacion);

    public Task Guardar(Envio envio, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(envio);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE envios.envios SET
                        estado = @estado, numero_de_guia = @numero, operador = @operador, clave_de_solicitud = @clave, guia_en_curso_hasta = @guiaEnCurso, despacho_iniciado_en = @despachoIniciado,
                        despacho_en_curso_hasta = @despachoEnCurso, despacho_incierto_en = @despachoIncierto, guia_generada_en = @guiaGenerada, en_reparto_en = @enReparto, entregado_en = @entregado,
                        cancelado_en = @cancelado, progreso_en_reparto_informado_en = @progresoReparto, progreso_entregado_informado_en = @progresoEntrega, version = version + 1
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion);
                Parametros(comando, envio);
                try
                {
                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("El envío no existe.");
                    }
                }
                catch (PostgresException excepcion) when (excepcion.SqlState == AccesoAlEsquemaDeEnvios.ViolacionDeUnicidad && excepcion.ConstraintName == "envios_guia_unica")
                {
                    // El operador repitió el número de otro envío: se trata como ENV-04 con una alerta, nunca como un error 500.
                    throw new GuiaRepetidaException("guia-repetida");
                }

                return 0;
            },
            cancelacion);
    }

    public Task<ListadoDeEnvios> Listar(EstadoDeEnvio? estado, int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var filtro = estado is null ? "TRUE" : "estado = @estado";
                var parametros = () => estado is null ? Array.Empty<NpgsqlParameter>() : [new NpgsqlParameter("estado", estado.Value.Codigo())];
                int total;
                await using (var contar = new NpgsqlCommand($"SELECT count(*) FROM envios.envios WHERE {filtro}", conexion, transaccion))
                {
                    contar.Parameters.AddRange(parametros());
                    total = Convert.ToInt32(await contar.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                }

                var envios = await Leer(
                    conexion,
                    transaccion,
                    filtro,
                    [.. parametros(), new NpgsqlParameter("limite", tamano), new NpgsqlParameter("desplazamiento", (pagina - 1) * tamano)],
                    " ORDER BY creado_en DESC, id LIMIT @limite OFFSET @desplazamiento",
                    false,
                    token).ConfigureAwait(false);
                return new ListadoDeEnvios(envios, total);
            },
            cancelacion);

    public Task<IReadOnlyList<Envio>> ObtenerConProgresoPendiente(int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => Leer(
                conexion,
                transaccion,
                "(en_reparto_en IS NOT NULL AND progreso_en_reparto_informado_en IS NULL) OR (entregado_en IS NOT NULL AND progreso_entregado_informado_en IS NULL)",
                [new NpgsqlParameter("maximo", maximo)],
                " ORDER BY en_reparto_en, id LIMIT @maximo",
                false,
                token),
            cancelacion);

    public Task<IReadOnlyList<Envio>> ObtenerConDespachoIniciadoAntesDe(DateTimeOffset limite, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => Leer(
                conexion,
                transaccion,
                "estado = 'guia_generada' AND despacho_iniciado_en < @limite",
                [new NpgsqlParameter("limite", limite.ToUniversalTime()), new NpgsqlParameter("maximo", maximo)],
                " ORDER BY despacho_iniciado_en, id LIMIT @maximo",
                false,
                token),
            cancelacion);

    public Task<IReadOnlyList<Envio>> ObtenerCanceladosConGuiaEntre(DateTimeOffset desde, DateTimeOffset hasta, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => Leer(
                conexion,
                transaccion,
                "estado = 'cancelado' AND numero_de_guia IS NOT NULL AND cancelado_en > @desde AND cancelado_en <= @hasta",
                [new NpgsqlParameter("desde", desde.ToUniversalTime()), new NpgsqlParameter("hasta", hasta.ToUniversalTime()), new NpgsqlParameter("maximo", maximo)],
                " ORDER BY cancelado_en, id LIMIT @maximo",
                false,
                token),
            cancelacion);

    private static async Task<IReadOnlyList<Envio>> Leer(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        string filtro,
        NpgsqlParameter[] parametros,
        string orden,
        bool paraActualizar,
        CancellationToken cancelacion)
    {
        if (paraActualizar)
        {
            await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = '5s'", cancelacion).ConfigureAwait(false);
        }

        var filas = new List<FilaDeEnvio>();
        await using (var comando = new NpgsqlCommand($"SELECT {Columnas} FROM envios.envios WHERE {filtro}{orden}{(paraActualizar ? " FOR UPDATE" : string.Empty)}", conexion, transaccion))
        {
            comando.Parameters.AddRange(parametros);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                filas.Add(FilaDeEnvio.Leer(lector));
            }
        }

        if (paraActualizar)
        {
            await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = DEFAULT", cancelacion).ConfigureAwait(false);
        }

        if (filas.Count == 0)
        {
            return [];
        }

        var lineas = new Dictionary<Guid, List<Guid>>();
        await using (var comando = new NpgsqlCommand("SELECT envio_id, linea_id FROM envios.envio_lineas WHERE envio_id = ANY(@ids) ORDER BY envio_id, posicion", conexion, transaccion))
        {
            comando.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filas.Select(f => f.Id).ToArray() });
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                var envioId = lector.GetGuid(0);
                if (!lineas.TryGetValue(envioId, out var lista))
                {
                    lineas[envioId] = lista = [];
                }

                lista.Add(lector.GetGuid(1));
            }
        }

        return filas.Select(f => f.Convertir(lineas.GetValueOrDefault(f.Id) ?? [])).ToList();
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    private static void Parametros(NpgsqlCommand comando, Envio e)
    {
        comando.Parameters.AddWithValue("id", e.Id);
        comando.Parameters.AddWithValue("pedido", e.PedidoId);
        comando.Parameters.AddWithValue("fuente", e.CodigoDeFuente);
        comando.Parameters.AddWithValue("estado", e.Estado.Codigo());
        comando.Parameters.AddWithValue("numero", AccesoAlEsquemaDeEnvios.ONulo(e.NumeroDeGuia?.ToString()));
        comando.Parameters.AddWithValue("operador", AccesoAlEsquemaDeEnvios.ONulo(e.Operador));
        comando.Parameters.AddWithValue("clave", AccesoAlEsquemaDeEnvios.ONulo(e.ClaveDeSolicitud));
        comando.Parameters.AddWithValue("guiaEnCurso", AccesoAlEsquemaDeEnvios.UtcONulo(e.GuiaEnCursoHasta));
        comando.Parameters.AddWithValue("despachoIniciado", AccesoAlEsquemaDeEnvios.UtcONulo(e.DespachoIniciadoEn));
        comando.Parameters.AddWithValue("despachoEnCurso", AccesoAlEsquemaDeEnvios.UtcONulo(e.DespachoEnCursoHasta));
        comando.Parameters.AddWithValue("despachoIncierto", AccesoAlEsquemaDeEnvios.UtcONulo(e.DespachoIncertoEn));
        comando.Parameters.AddWithValue("creado", e.CreadoEn.ToUniversalTime());
        comando.Parameters.AddWithValue("guiaGenerada", AccesoAlEsquemaDeEnvios.UtcONulo(e.GuiaGeneradaEn));
        comando.Parameters.AddWithValue("enReparto", AccesoAlEsquemaDeEnvios.UtcONulo(e.EnRepartoEn));
        comando.Parameters.AddWithValue("entregado", AccesoAlEsquemaDeEnvios.UtcONulo(e.EntregadoEn));
        comando.Parameters.AddWithValue("cancelado", AccesoAlEsquemaDeEnvios.UtcONulo(e.CanceladoEn));
        comando.Parameters.AddWithValue("progresoReparto", AccesoAlEsquemaDeEnvios.UtcONulo(e.ProgresoEnRepartoInformadoEn));
        comando.Parameters.AddWithValue("progresoEntrega", AccesoAlEsquemaDeEnvios.UtcONulo(e.ProgresoEntregadoInformadoEn));
        comando.Parameters.AddWithValue("version", e.Version);
    }

    private sealed record FilaDeEnvio(
        Guid Id,
        Guid PedidoId,
        string Fuente,
        EstadoDeEnvio Estado,
        string? Numero,
        string? Operador,
        Guid? Clave,
        DateTimeOffset? GuiaEnCurso,
        DateTimeOffset? DespachoIniciado,
        DateTimeOffset? DespachoEnCurso,
        DateTimeOffset? DespachoIncierto,
        DateTimeOffset Creado,
        DateTimeOffset? GuiaGenerada,
        DateTimeOffset? EnReparto,
        DateTimeOffset? Entregado,
        DateTimeOffset? Cancelado,
        DateTimeOffset? ProgresoReparto,
        DateTimeOffset? ProgresoEntrega,
        int Version)
    {
        public static FilaDeEnvio Leer(NpgsqlDataReader f) =>
            new(
                f.GetGuid(0),
                f.GetGuid(1),
                f.GetString(2),
                CodigosDeEstadoDeEnvio.TryDesdeCodigo(f.GetString(3), out var estado) ? estado : throw new InvalidOperationException("Un estado de envío desconocido."),
                f.IsDBNull(4) ? null : f.GetString(4),
                f.IsDBNull(5) ? null : f.GetString(5),
                f.IsDBNull(6) ? null : f.GetGuid(6),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 7),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 8),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 9),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 10),
                f.GetFieldValue<DateTimeOffset>(11),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 12),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 13),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 14),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 15),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 16),
                AccesoAlEsquemaDeEnvios.LeerInstante(f, 17),
                f.GetInt32(18));

        public Envio Convertir(List<Guid> lineas) =>
            Envio.Rehidratar(
                Id,
                PedidoId,
                Fuente,
                lineas,
                Estado,
                Numero is null ? null : NumeroDeGuia.Crear(Numero),
                Operador,
                Clave,
                GuiaEnCurso,
                DespachoIniciado,
                DespachoEnCurso,
                DespachoIncierto,
                Creado,
                GuiaGenerada,
                EnReparto,
                Entregado,
                Cancelado,
                ProgresoReparto,
                ProgresoEntrega,
                Version);
    }
}
