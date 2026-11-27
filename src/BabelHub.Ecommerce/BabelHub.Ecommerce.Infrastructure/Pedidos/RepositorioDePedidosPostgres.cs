using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>
/// El repositorio de los pedidos sobre el esquema <c>pedidos</c> (T28). Cada lectura <c>conBloqueo</c> es <c>SELECT … FOR UPDATE</c> con un <c>lock_timeout</c> de 5 segundos (un tiempo agotado sale como «conflicto», reintentable), en orden de identificador cuando son varios (el orden global de bloqueo, D19). <b>Todos los valores van por parámetros</b> y nada se interpola en el
/// texto SQL. <b>El contacto de entrega</b> se lee solo cuando el caso de uso lo necesita (un listado lo omite) y se escribe una sola vez, al crear el pedido; después solo se <b>anonimiza</b>. Ni los registros ni las excepciones llevan una fila (<see cref="AccesoAlEsquemaDePedidos"/>).
/// </summary>
internal sealed class RepositorioDePedidosPostgres(AccesoAlEsquemaDePedidos acceso) : IRepositorioDePedidos
{
    private static readonly Guid SinIniciador = Guid.Empty;

    private const string ColumnasDelPedido =
        "id, numero, cuenta_id, tipo_de_entrega, codigo_de_sucursal_de_recojo, region_de_entrega, costo_de_envio, total, estado, creado_en, fecha_estado_final, {0}, contacto_anonimizado_en, terminos_version, terminos_aceptado_en, "
        + "privacidad_version, privacidad_aceptado_en, reembolso_digital_version, reembolso_digital_aceptado_en";

    private const string ColumnasDeLaLinea =
        "pedido_id, id, edicion_id, es_digital, titulo, isbn, cantidad, precio_unitario, importe, estado, codigo_de_fuente, requiere_traslado, traslado_id, fecha_estimada_de_llegada, listo_en, entregada_en, no_recogida_en, anulada_en, envio_id";

    private const string ConContacto = "contacto_nombre, contacto_telefono, contacto_direccion";

    private const string SinContacto = "NULL::text, NULL::text, NULL::text";

    // ---------- Pedidos ----------

    public Task<PaginaDeDominio> Listar(CriterioDeListado criterio, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(criterio);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var filtros = new List<string>();
                var parametros = new List<NpgsqlParameter>();
                if (criterio.CuentaId is { } cuenta)
                {
                    filtros.Add("cuenta_id = @cuenta");
                    parametros.Add(new NpgsqlParameter("cuenta", cuenta));
                }

                if (criterio.CodigoDeSucursalDelEncargado is { } sucursal)
                {
                    filtros.Add("tipo_de_entrega = 'recojo' AND codigo_de_sucursal_de_recojo = @sucursal");
                    parametros.Add(new NpgsqlParameter("sucursal", sucursal));
                }

                if (criterio.SoloADomicilio)
                {
                    filtros.Add("tipo_de_entrega = 'domicilio'");
                }

                if (criterio.Estado is { } estado)
                {
                    filtros.Add("estado = @estado");
                    parametros.Add(new NpgsqlParameter("estado", CodigosDePedidos.De(estado)));
                }

                var donde = filtros.Count == 0 ? "TRUE" : string.Join(" AND ", filtros);
                int total;
                await using (var contar = new NpgsqlCommand($"SELECT count(*) FROM pedidos.pedidos WHERE {donde}", conexion, transaccion))
                {
                    Agregar(contar, parametros);
                    total = Convert.ToInt32(await contar.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                }

                var pagina = new List<NpgsqlParameter>(parametros.Select(Copiar))
                {
                    new("desplazamiento", Math.Max(0, (criterio.Pagina - 1) * criterio.Tamano)),
                    new("tamano", Math.Max(1, criterio.Tamano)),
                };
                var pedidos = await LeerPedidos(conexion, transaccion, donde, pagina, " ORDER BY creado_en DESC, id LIMIT @tamano OFFSET @desplazamiento", conBloqueo: false, conContacto: false, token).ConfigureAwait(false);
                return new PaginaDeDominio(pedidos, total);
            },
            cancelacion);
    }

    public Task<Pedido?> Obtener(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                (await LeerPedidos(conexion, transaccion, "id = @valor", [new NpgsqlParameter("valor", pedidoId)], string.Empty, conBloqueo, conContacto: true, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<bool> Existe(Guid pedidoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pedidos.pedidos WHERE id = @id)", conexion, transaccion);
                comando.Parameters.AddWithValue("id", pedidoId);
                return (bool)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion);

    public Task<bool> Agregar(Pedido pedido, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Sin objetivo de conflicto: una colisión de id o de número tampoco aborta la transacción del llamador (dice «ya existía»).
                await using (var insertar = new NpgsqlCommand(
                    """
                    INSERT INTO pedidos.pedidos (
                        id, numero, cuenta_id, tipo_de_entrega, codigo_de_sucursal_de_recojo, region_de_entrega, costo_de_envio, total, estado, creado_en, fecha_estado_final, contacto_nombre, contacto_telefono, contacto_direccion,
                        contacto_anonimizado_en, terminos_version, terminos_aceptado_en, privacidad_version, privacidad_aceptado_en, reembolso_digital_version, reembolso_digital_aceptado_en, version)
                    VALUES (
                        @id, @numero, @cuenta, @tipo, @sucursal, @region, @costo, @total, @estado, @creado, @final, @nombre, @telefono, @direccion, @anonimizado, @terminos, @terminosEn, @privacidad, @privacidadEn, @reembolso, @reembolsoEn, 1)
                    ON CONFLICT DO NOTHING
                    """,
                    conexion,
                    transaccion))
                {
                    insertar.Parameters.AddWithValue("id", pedido.Id);
                    insertar.Parameters.AddWithValue("numero", pedido.Numero.Valor);
                    insertar.Parameters.AddWithValue("cuenta", pedido.CuentaId);
                    insertar.Parameters.AddWithValue("tipo", CodigosDePedidos.De(pedido.Tipo));
                    insertar.Parameters.AddWithValue("sucursal", AccesoAlEsquemaDePedidos.ONulo(pedido.CodigoDeSucursalDeRecojo));
                    insertar.Parameters.AddWithValue("region", AccesoAlEsquemaDePedidos.ONulo(pedido.RegionDeEntrega));
                    insertar.Parameters.AddWithValue("costo", pedido.CostoDeEnvio);
                    insertar.Parameters.AddWithValue("total", pedido.Total);
                    insertar.Parameters.AddWithValue("estado", CodigosDePedidos.De(pedido.Estado));
                    insertar.Parameters.AddWithValue("creado", pedido.CreadoEn.ToUniversalTime());
                    insertar.Parameters.AddWithValue("final", AccesoAlEsquemaDePedidos.UtcONulo(pedido.FechaEstadoFinal));
                    insertar.Parameters.AddWithValue("nombre", AccesoAlEsquemaDePedidos.ONulo(pedido.Contacto?.Nombre));
                    insertar.Parameters.AddWithValue("telefono", AccesoAlEsquemaDePedidos.ONulo(pedido.Contacto?.Telefono));
                    insertar.Parameters.AddWithValue("direccion", AccesoAlEsquemaDePedidos.ONulo(pedido.Contacto?.Direccion));
                    insertar.Parameters.AddWithValue("anonimizado", AccesoAlEsquemaDePedidos.UtcONulo(pedido.ContactoAnonimizadoEn));
                    insertar.Parameters.AddWithValue("terminos", pedido.Terminos.Version);
                    insertar.Parameters.AddWithValue("terminosEn", pedido.Terminos.AceptadoEn.ToUniversalTime());
                    insertar.Parameters.AddWithValue("privacidad", pedido.Privacidad.Version);
                    insertar.Parameters.AddWithValue("privacidadEn", pedido.Privacidad.AceptadoEn.ToUniversalTime());
                    insertar.Parameters.AddWithValue("reembolso", AccesoAlEsquemaDePedidos.ONulo(pedido.ReembolsoDigital?.Version));
                    insertar.Parameters.AddWithValue("reembolsoEn", AccesoAlEsquemaDePedidos.UtcONulo(pedido.ReembolsoDigital?.AceptadoEn));
                    if (await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        return false;
                    }
                }

                var posicion = 0;
                foreach (var linea in pedido.Lineas)
                {
                    await using var comando = new NpgsqlCommand(
                        """
                        INSERT INTO pedidos.lineas_de_pedido (
                            id, pedido_id, edicion_id, posicion, es_digital, titulo, isbn, cantidad, precio_unitario, importe, estado, codigo_de_fuente, requiere_traslado, traslado_id, fecha_estimada_de_llegada, listo_en,
                            entregada_en, no_recogida_en, anulada_en, envio_id, version)
                        VALUES (@id, @pedido, @edicion, @posicion, @digital, @titulo, @isbn, @cantidad, @precio, @importe, @estado, @fuente, @traslado, @trasladoId, @fecha, @listo, @entregada, @noRecogida, @anulada, @envio, 1)
                        """,
                        conexion,
                        transaccion);
                    ParametrosDeLaLinea(comando, linea);
                    comando.Parameters.AddWithValue("pedido", pedido.Id);
                    comando.Parameters.AddWithValue("edicion", linea.EdicionId);
                    comando.Parameters.AddWithValue("posicion", posicion++);
                    comando.Parameters.AddWithValue("digital", linea.EsDigital);
                    comando.Parameters.AddWithValue("titulo", linea.Titulo);
                    comando.Parameters.AddWithValue("isbn", AccesoAlEsquemaDePedidos.ONulo(linea.Isbn));
                    comando.Parameters.AddWithValue("cantidad", linea.Cantidad);
                    comando.Parameters.AddWithValue("precio", linea.PrecioUnitario);
                    comando.Parameters.AddWithValue("importe", linea.Importe);
                    comando.Parameters.AddWithValue("fuente", AccesoAlEsquemaDePedidos.ONulo(linea.CodigoDeFuente));
                    comando.Parameters.AddWithValue("traslado", linea.RequiereTraslado);
                    comando.Parameters.AddWithValue("trasladoId", AccesoAlEsquemaDePedidos.ONulo(linea.TrasladoId));
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion);
    }

    public Task Actualizar(Pedido pedido, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // El contacto solo se toca para anonimizarlo: una lectura sin contacto (un listado) nunca lo borra al guardar.
                var anonimizar = pedido.ContactoAnonimizadoEn is not null;
                await using (var comando = new NpgsqlCommand(
                    $"""
                    UPDATE pedidos.pedidos SET estado = @estado, fecha_estado_final = @final, version = version + 1
                        {(anonimizar ? ", contacto_nombre = NULL, contacto_telefono = NULL, contacto_direccion = NULL, contacto_anonimizado_en = COALESCE(contacto_anonimizado_en, @anonimizado)" : string.Empty)}
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion))
                {
                    comando.Parameters.AddWithValue("id", pedido.Id);
                    comando.Parameters.AddWithValue("estado", CodigosDePedidos.De(pedido.Estado));
                    comando.Parameters.AddWithValue("final", AccesoAlEsquemaDePedidos.UtcONulo(pedido.FechaEstadoFinal));
                    if (anonimizar)
                    {
                        comando.Parameters.AddWithValue("anonimizado", AccesoAlEsquemaDePedidos.UtcONulo(pedido.ContactoAnonimizadoEn));
                    }

                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("El pedido no existe.");
                    }
                }

                foreach (var linea in pedido.Lineas)
                {
                    await using var comando = new NpgsqlCommand(
                        """
                        UPDATE pedidos.lineas_de_pedido SET estado = @estado, fecha_estimada_de_llegada = @fecha, listo_en = @listo, entregada_en = @entregada, no_recogida_en = @noRecogida, anulada_en = @anulada, envio_id = @envio, version = version + 1
                        WHERE id = @id
                        """,
                        conexion,
                        transaccion);
                    ParametrosDeLaLinea(comando, linea);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Pedido>> ListarDeLaCuentaConBloqueo(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => LeerPedidos(conexion, transaccion, "cuenta_id = @valor", [new NpgsqlParameter("valor", cuentaId)], " ORDER BY id", conBloqueo: true, conContacto: true, token),
            cancelacion);

    public Task<IReadOnlyList<Pedido>> ListarVencidosParaDepurar(DateOnly hoyEnLima, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            (conexion, transaccion, token) => LeerPedidos(
                conexion,
                transaccion,
                """
                fecha_estado_final IS NOT NULL
                AND ((fecha_estado_final AT TIME ZONE 'America/Lima')::date + INTERVAL '2 years')::date <= @hoy
                AND (contacto_nombre IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pedidos.anulaciones a WHERE a.pedido_id = pedidos.pedidos.id AND a.iniciada_por <> @vacio)
                    OR EXISTS (SELECT 1 FROM pedidos.entregas e WHERE e.pedido_id = pedidos.pedidos.id AND e.iniciada_por <> @vacio))
                """,
                [new NpgsqlParameter("hoy", NpgsqlDbType.Date) { Value = hoyEnLima }, new NpgsqlParameter("vacio", SinIniciador), new NpgsqlParameter("maximo", Math.Max(1, maximo))],
                " ORDER BY id LIMIT @maximo",
                conBloqueo: true,
                conContacto: true,
                token,
                omitirBloqueados: true),
            cancelacion);

    public Task<IReadOnlyList<Guid>> PedidosConRecojoVencido(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT DISTINCT l.pedido_id FROM pedidos.lineas_de_pedido l JOIN pedidos.pedidos p ON p.id = l.pedido_id
                    WHERE p.tipo_de_entrega = 'recojo' AND l.estado = 'listo_para_recojo' AND l.listo_en < @limite
                    ORDER BY l.pedido_id LIMIT @maximo
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("limite", ahora.ToUniversalTime() - TimeSpan.FromHours(PoliticaDeNoRecogido.PlazoEnHoras));
                comando.Parameters.AddWithValue("maximo", Math.Max(1, maximo));
                return (IReadOnlyList<Guid>)await LeerGuids(comando, token).ConfigureAwait(false);
            },
            cancelacion);

    // ---------- Contacto, traslados y retención ----------

    public Task RegistrarLiberacionDeContacto(Guid pedidoId, DateTimeOffset desde, CancellationToken cancelacion = default) =>
        Escribir("INSERT INTO pedidos.liberaciones_de_contacto (pedido_id, desde, intentos) VALUES (@pedido, @desde, 0) ON CONFLICT DO NOTHING", [new("pedido", pedidoId), new("desde", desde.ToUniversalTime())], cancelacion);

    public Task<IReadOnlyList<LiberacionDeContactoPendiente>> LiberacionesDeContactoPendientes(int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT pedido_id, desde, intentos FROM pedidos.liberaciones_de_contacto ORDER BY desde LIMIT @maximo", conexion, transaccion);
                comando.Parameters.AddWithValue("maximo", Math.Max(1, maximo));
                var filas = new List<LiberacionDeContactoPendiente>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    filas.Add(new LiberacionDeContactoPendiente(lector.GetGuid(0), lector.GetFieldValue<DateTimeOffset>(1), lector.GetInt32(2)));
                }

                return (IReadOnlyList<LiberacionDeContactoPendiente>)filas;
            },
            cancelacion);

    public Task QuitarLiberacionDeContacto(Guid pedidoId, CancellationToken cancelacion = default) =>
        Escribir("DELETE FROM pedidos.liberaciones_de_contacto WHERE pedido_id = @pedido", [new("pedido", pedidoId)], cancelacion);

    public Task AnotarIntentoDeLiberacionDeContacto(Guid pedidoId, DateTimeOffset cuando, CancellationToken cancelacion = default) =>
        Escribir("UPDATE pedidos.liberaciones_de_contacto SET intentos = intentos + 1, ultimo_intento_en = @cuando WHERE pedido_id = @pedido", [new("pedido", pedidoId), new("cuando", cuando.ToUniversalTime())], cancelacion);

    public Task<EstadoDeTrasladoRegistrado?> ObtenerEstadoDeTraslado(string eventoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT evento_id, traslado_id, pedido_id, estado, cuerpo_huella, recibido_en FROM pedidos.estados_de_traslado_recibidos WHERE evento_id = @evento", conexion, transaccion);
                comando.Parameters.AddWithValue("evento", eventoId);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false)
                    ? new EstadoDeTrasladoRegistrado(lector.GetString(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetString(3), lector.GetString(4), lector.GetFieldValue<DateTimeOffset>(5))
                    : null;
            },
            cancelacion);

    public Task GuardarEstadoDeTraslado(EstadoDeTrasladoRegistrado estado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(estado);
        return Escribir(
            "INSERT INTO pedidos.estados_de_traslado_recibidos (evento_id, traslado_id, pedido_id, estado, cuerpo_huella, recibido_en) VALUES (@evento, @traslado, @pedido, @estado, @huella, @recibido) ON CONFLICT DO NOTHING",
            [new("evento", estado.EventoId), new("traslado", estado.TrasladoId), new("pedido", estado.PedidoId), new("estado", estado.Estado), new("huella", estado.HuellaDelCuerpo), new("recibido", estado.RecibidoEn.ToUniversalTime())],
            cancelacion);
    }

    public Task<int> PurgarEstadosDeTraslado(DateTimeOffset recibidosAntesDe, CancellationToken cancelacion = default) =>
        Contar("DELETE FROM pedidos.estados_de_traslado_recibidos WHERE recibido_en < @limite", [new("limite", recibidosAntesDe.ToUniversalTime())], cancelacion);

    public Task<int> AnonimizarIniciadoresDeOperaciones(Guid pedidoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var cambiadas = 0;
                foreach (var tabla in new[] { "anulaciones", "entregas" })
                {
                    await using var comando = new NpgsqlCommand(
                        $"UPDATE pedidos.{tabla} SET iniciada_por = @vacio, rol_del_iniciador = NULL, sucursal_del_iniciador = NULL WHERE pedido_id = @pedido AND iniciada_por <> @vacio",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("vacio", SinIniciador);
                    comando.Parameters.AddWithValue("pedido", pedidoId);
                    cambiadas += await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return cambiadas;
            },
            cancelacion);

    public Task<DateTimeOffset?> UltimaDepuracionExitosaEn(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT ultima_exitosa_en FROM pedidos.depuraciones_de_pedidos WHERE id = 1", conexion, transaccion);
                return await comando.ExecuteScalarAsync(token).ConfigureAwait(false) is DateTime cuando ? (DateTimeOffset?)new DateTimeOffset(cuando.ToUniversalTime()) : null;
            },
            cancelacion);

    public Task RegistrarDepuracionExitosa(DateTimeOffset cuando, CancellationToken cancelacion = default) =>
        Escribir(
            "INSERT INTO pedidos.depuraciones_de_pedidos (id, ultima_exitosa_en) VALUES (1, @cuando) ON CONFLICT (id) DO UPDATE SET ultima_exitosa_en = EXCLUDED.ultima_exitosa_en",
            [new("cuando", cuando.ToUniversalTime())],
            cancelacion);

    // ---------- Conteos ----------

    public Task<int> ContarPedidosConImpresaSinCerrar(Guid cuentaId, CancellationToken cancelacion = default) =>
        Contar(
            "SELECT count(DISTINCT p.id) FROM pedidos.pedidos p JOIN pedidos.lineas_de_pedido l ON l.pedido_id = p.id WHERE p.cuenta_id = @cuenta AND NOT l.es_digital AND l.estado NOT IN ('entregado', 'anulado')",
            [new("cuenta", cuentaId)],
            cancelacion);

    public Task<int> ContarNoRecogidasSinAnularDesde(DateTimeOffset marcadasAntesDe, CancellationToken cancelacion = default) =>
        Contar("SELECT count(*) FROM pedidos.lineas_de_pedido WHERE estado = 'no_recogido' AND no_recogida_en < @limite", [new("limite", marcadasAntesDe.ToUniversalTime())], cancelacion);

    public Task<int> ContarImpresasSinEstadoFinalDesde(DateTimeOffset creadosAntesDe, CancellationToken cancelacion = default) =>
        Contar(
            "SELECT count(*) FROM pedidos.lineas_de_pedido l JOIN pedidos.pedidos p ON p.id = l.pedido_id WHERE p.creado_en < @limite AND NOT l.es_digital AND l.estado NOT IN ('entregado', 'anulado')",
            [new("limite", creadosAntesDe.ToUniversalTime())],
            cancelacion);

    // ---------- Liberaciones de reserva ----------

    public Task RegistrarLiberacionesDeReserva(Guid pedidoId, IReadOnlyCollection<Guid> lineaIds, string motivo, DateTimeOffset desde, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                foreach (var lineaId in lineaIds)
                {
                    await using var comando = new NpgsqlCommand(
                        "INSERT INTO pedidos.liberaciones_de_reserva (linea_id, pedido_id, motivo, pendiente_desde, intentos) VALUES (@linea, @pedido, @motivo, @desde, 0) ON CONFLICT DO NOTHING",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("linea", lineaId);
                    comando.Parameters.AddWithValue("pedido", pedidoId);
                    comando.Parameters.AddWithValue("motivo", motivo);
                    comando.Parameters.AddWithValue("desde", desde.ToUniversalTime());
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);

    public Task<IReadOnlyList<LiberacionDeReservaPendiente>> LiberacionesDeReservaPendientes(int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT pedido_id, linea_id, motivo, pendiente_desde, intentos FROM pedidos.liberaciones_de_reserva ORDER BY pendiente_desde, linea_id LIMIT @maximo", conexion, transaccion);
                comando.Parameters.AddWithValue("maximo", Math.Max(1, maximo));
                var filas = new List<LiberacionDeReservaPendiente>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    filas.Add(new LiberacionDeReservaPendiente(lector.GetGuid(0), lector.GetGuid(1), lector.GetString(2), lector.GetFieldValue<DateTimeOffset>(3), lector.GetInt32(4)));
                }

                return (IReadOnlyList<LiberacionDeReservaPendiente>)filas;
            },
            cancelacion);

    public Task QuitarLiberacionesDeReserva(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default) =>
        Escribir("DELETE FROM pedidos.liberaciones_de_reserva WHERE linea_id = ANY(@lineas)", [Lista("lineas", lineaIds)], cancelacion);

    public Task AnotarIntentoDeLiberacion(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default) =>
        Escribir("UPDATE pedidos.liberaciones_de_reserva SET intentos = intentos + 1 WHERE linea_id = ANY(@lineas)", [Lista("lineas", lineaIds)], cancelacion);

    // ---------- Anulaciones y entregas ----------

    public Task AgregarAnulacion(Anulacion anulacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(anulacion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    INSERT INTO pedidos.anulaciones (
                        id, pedido_id, origen, motivo, incluye_envio, hay_digitales, monto, estado, reembolso_id, envios_cancelados_en, reservas_liberadas_en, reembolso_registrado_en, accesos_revocados_en, iniciada_por, rol_del_iniciador,
                        sucursal_del_iniciador, proximo_intento_en, intentos, creada_en, completada_en)
                    VALUES (@id, @pedido, @origen, @motivo, @envio, @digitales, @monto, @estado, @reembolso, @envios, @reservas, @reembolsoEn, @accesos, @iniciador, @rol, @sucursal, @proximo, @intentos, @creada, @completada)
                    """,
                    conexion,
                    transaccion))
                {
                    comando.Parameters.AddWithValue("id", anulacion.Id);
                    comando.Parameters.AddWithValue("pedido", anulacion.PedidoId);
                    comando.Parameters.AddWithValue("origen", CodigosDePedidos.De(anulacion.Origen));
                    comando.Parameters.AddWithValue("motivo", CodigosDePedidos.Motivo(anulacion.Motivo));
                    comando.Parameters.AddWithValue("creada", anulacion.CreadaEn.ToUniversalTime());
                    ParametrosDeLaAnulacion(comando, anulacion);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await ReemplazarLineas(conexion, transaccion, "anulacion_lineas", "anulacion_id", anulacion.Id, anulacion.LineaIds, anulacion.BloqueaLasLineas, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task ActualizarAnulacion(Anulacion anulacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(anulacion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    UPDATE pedidos.anulaciones SET
                        incluye_envio = @envio, hay_digitales = @digitales, monto = @monto, estado = @estado, reembolso_id = @reembolso, envios_cancelados_en = @envios, reservas_liberadas_en = @reservas,
                        reembolso_registrado_en = @reembolsoEn, accesos_revocados_en = @accesos, iniciada_por = @iniciador, rol_del_iniciador = @rol, sucursal_del_iniciador = @sucursal,
                        proximo_intento_en = @proximo, intentos = @intentos, completada_en = @completada
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion))
                {
                    comando.Parameters.AddWithValue("id", anulacion.Id);
                    ParametrosDeLaAnulacion(comando, anulacion);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await ReemplazarLineas(conexion, transaccion, "anulacion_lineas", "anulacion_id", anulacion.Id, anulacion.LineaIds, anulacion.BloqueaLasLineas, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<PaginaDeOperacionesFallidasGuardada> OperacionesFallidas(int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                const string Union = """
                    SELECT id, pedido_id, 'anulacion' AS tipo, motivo, creada_en,
                           array_remove(ARRAY[
                               CASE WHEN envios_cancelados_en IS NOT NULL THEN 'envios-cancelados' END,
                               CASE WHEN reservas_liberadas_en IS NOT NULL THEN 'reservas-liberadas' END,
                               CASE WHEN reembolso_registrado_en IS NOT NULL THEN 'reembolso-registrado' END,
                               CASE WHEN accesos_revocados_en IS NOT NULL THEN 'accesos-revocados' END], NULL) AS pasos
                    FROM pedidos.anulaciones WHERE estado = 'fallida'
                    UNION ALL
                    SELECT e.id, e.pedido_id, 'entrega', NULL::text, e.creada_en,
                           array_remove(ARRAY[CASE WHEN EXISTS (SELECT 1 FROM pedidos.entrega_lineas l WHERE l.entrega_id = e.id AND l.cierre_confirmado_en IS NOT NULL) THEN 'cierre-confirmado' END], NULL)
                    FROM pedidos.entregas e WHERE e.estado = 'fallida'
                    """;
                int total;
                await using (var contar = new NpgsqlCommand($"SELECT count(*) FROM ({Union}) f", conexion, transaccion))
                {
                    total = Convert.ToInt32(await contar.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                }

                var filas = new List<OperacionFallidaGuardada>();
                await using var comando = new NpgsqlCommand($"SELECT id, pedido_id, tipo, motivo, creada_en, pasos FROM ({Union}) f ORDER BY creada_en DESC, id LIMIT @tamano OFFSET @desplazamiento", conexion, transaccion);
                comando.Parameters.AddWithValue("tamano", Math.Max(1, tamano));
                comando.Parameters.AddWithValue("desplazamiento", Math.Max(0, (pagina - 1) * tamano));
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    filas.Add(new OperacionFallidaGuardada(lector.GetGuid(0), lector.GetGuid(1), lector.GetString(2), AccesoAlEsquemaDePedidos.LeerTexto(lector, 3), lector.GetFieldValue<string[]>(5), lector.GetFieldValue<DateTimeOffset>(4)));
                }

                return new PaginaDeOperacionesFallidasGuardada(filas, total);
            },
            cancelacion);

    public Task<IReadOnlyList<Anulacion>> AnulacionesEnCurso(Guid pedidoId, CancellationToken cancelacion = default) =>
        LeerAnulaciones("pedido_id = @valor AND estado = 'en_curso'", [new NpgsqlParameter("valor", pedidoId)], " ORDER BY creada_en", cancelacion);

    public Task<IReadOnlyList<Anulacion>> AnulacionesFallidas(Guid pedidoId, CancellationToken cancelacion = default) =>
        LeerAnulaciones("pedido_id = @valor AND estado = 'fallida'", [new NpgsqlParameter("valor", pedidoId)], " ORDER BY creada_en", cancelacion);

    public Task AgregarEntrega(Entrega entrega, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    INSERT INTO pedidos.entregas (id, pedido_id, estado, iniciada_por, rol_del_iniciador, sucursal_del_iniciador, proximo_intento_en, intentos, creada_en)
                    VALUES (@id, @pedido, @estado, @iniciador, @rol, @sucursal, @proximo, @intentos, @creada)
                    """,
                    conexion,
                    transaccion))
                {
                    comando.Parameters.AddWithValue("id", entrega.Id);
                    comando.Parameters.AddWithValue("pedido", entrega.PedidoId);
                    comando.Parameters.AddWithValue("creada", entrega.CreadaEn.ToUniversalTime());
                    ParametrosDeLaEntrega(comando, entrega);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await EscribirLineasDeEntrega(conexion, transaccion, entrega, insertar: true, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task ActualizarEntrega(Entrega entrega, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    "UPDATE pedidos.entregas SET estado = @estado, iniciada_por = @iniciador, rol_del_iniciador = @rol, sucursal_del_iniciador = @sucursal, proximo_intento_en = @proximo, intentos = @intentos WHERE id = @id",
                    conexion,
                    transaccion))
                {
                    comando.Parameters.AddWithValue("id", entrega.Id);
                    ParametrosDeLaEntrega(comando, entrega);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await EscribirLineasDeEntrega(conexion, transaccion, entrega, insertar: false, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Entrega>> EntregasEnCurso(Guid pedidoId, CancellationToken cancelacion = default) =>
        LeerEntregas("pedido_id = @valor AND estado = 'en_curso'", [new NpgsqlParameter("valor", pedidoId)], " ORDER BY creada_en", cancelacion);

    public async Task<OperacionesPorReanudar> OperacionesEnCursoParaReanudar(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default)
    {
        // Se reclama cada fila con una concesión (se empuja su próximo intento) para que dos instancias no tomen la misma; la lectura va sin bloqueo, ya reclamada.
        var (anulaciones, entregas) = await acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var tomadas = new List<Guid>[2] { [], [] };
                var indice = 0;
                foreach (var tabla in new[] { "anulaciones", "entregas" })
                {
                    await using var comando = new NpgsqlCommand(
                        $"""
                        WITH elegidas AS (
                            SELECT id FROM pedidos.{tabla} WHERE estado = 'en_curso' AND proximo_intento_en <= @ahora ORDER BY creada_en, id LIMIT @maximo FOR UPDATE SKIP LOCKED)
                        UPDATE pedidos.{tabla} t SET proximo_intento_en = @concesion FROM elegidas e WHERE t.id = e.id RETURNING t.id
                        """,
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                    comando.Parameters.AddWithValue("maximo", Math.Max(1, maximo));
                    comando.Parameters.AddWithValue("concesion", ahora.ToUniversalTime() + TimeSpan.FromMinutes(2));
                    tomadas[indice++].AddRange(await LeerGuids(comando, token).ConfigureAwait(false));
                }

                return (tomadas[0], tomadas[1]);
            },
            cancelacion).ConfigureAwait(false);
        var filasDeAnulaciones = anulaciones.Count == 0 ? [] : await LeerAnulaciones("id = ANY(@valor)", [Lista("valor", anulaciones)], " ORDER BY creada_en", cancelacion).ConfigureAwait(false);
        var filasDeEntregas = entregas.Count == 0 ? [] : await LeerEntregas("id = ANY(@valor)", [Lista("valor", entregas)], " ORDER BY creada_en", cancelacion).ConfigureAwait(false);
        return new OperacionesPorReanudar(filasDeAnulaciones, filasDeEntregas);
    }

    // ---------- Lectura ----------

    private static async Task<IReadOnlyList<Pedido>> LeerPedidos(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        string filtro,
        IReadOnlyList<NpgsqlParameter> parametros,
        string orden,
        bool conBloqueo,
        bool conContacto,
        CancellationToken cancelacion,
        bool omitirBloqueados = false)
    {
        if (conBloqueo)
        {
            await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = '5s'", cancelacion).ConfigureAwait(false);
        }

        var filas = new List<(DatosBasicosDelPedido Datos, string? Nombre, string? Telefono, string? Direccion)>();
        await using (var comando = new NpgsqlCommand(
            $"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, ColumnasDelPedido, conContacto ? ConContacto : SinContacto)} FROM pedidos.pedidos WHERE {filtro}{orden}{(conBloqueo ? (omitirBloqueados ? " FOR UPDATE SKIP LOCKED" : " FOR UPDATE") : string.Empty)}",
            conexion,
            transaccion))
        {
            Agregar(comando, parametros);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                filas.Add((DatosBasicosDelPedido.Leer(lector), AccesoAlEsquemaDePedidos.LeerTexto(lector, 11), AccesoAlEsquemaDePedidos.LeerTexto(lector, 12), AccesoAlEsquemaDePedidos.LeerTexto(lector, 13)));
            }
        }

        if (conBloqueo)
        {
            await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = DEFAULT", cancelacion).ConfigureAwait(false);
        }

        if (filas.Count == 0)
        {
            return [];
        }

        var lineas = new Dictionary<Guid, List<DatosPersistidosDeLinea>>();
        await using (var comando = new NpgsqlCommand($"SELECT {ColumnasDeLaLinea} FROM pedidos.lineas_de_pedido WHERE pedido_id = ANY(@ids) ORDER BY pedido_id, posicion", conexion, transaccion))
        {
            comando.Parameters.Add(Lista("ids", filas.Select(f => f.Datos.Id).ToArray()));
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                var pedidoId = lector.GetGuid(0);
                if (!lineas.TryGetValue(pedidoId, out var lista))
                {
                    lineas[pedidoId] = lista = [];
                }

                lista.Add(LeerLinea(lector));
            }
        }

        return [.. filas.Select(f => f.Datos.Convertir(f.Nombre, f.Telefono, f.Direccion, lineas.GetValueOrDefault(f.Datos.Id) ?? []))];
    }

    private static DatosPersistidosDeLinea LeerLinea(NpgsqlDataReader l)
    {
        var esDigital = l.GetBoolean(3);
        var estado = l.GetString(9);
        return new DatosPersistidosDeLinea(
            l.GetGuid(1),
            l.GetGuid(2),
            esDigital,
            l.GetString(4),
            AccesoAlEsquemaDePedidos.LeerTexto(l, 5),
            l.GetInt32(6),
            l.GetDecimal(7),
            l.GetDecimal(8),
            AccesoAlEsquemaDePedidos.LeerTexto(l, 10),
            l.GetBoolean(11),
            AccesoAlEsquemaDePedidos.LeerGuid(l, 12),
            l.IsDBNull(13) ? null : l.GetFieldValue<DateOnly>(13),
            esDigital ? null : CodigosDePedidos.A<EstadoDeLineaImpresa>(estado),
            esDigital ? CodigosDePedidos.A<EstadoDeLineaDigital>(estado) : null,
            AccesoAlEsquemaDePedidos.LeerInstante(l, 14),
            AccesoAlEsquemaDePedidos.LeerInstante(l, 15),
            AccesoAlEsquemaDePedidos.LeerInstante(l, 16),
            AccesoAlEsquemaDePedidos.LeerInstante(l, 17),
            AccesoAlEsquemaDePedidos.LeerGuid(l, 18));
    }

    private sealed record DatosBasicosDelPedido(
        Guid Id,
        string Numero,
        Guid CuentaId,
        TipoDeEntregaDelPedido Tipo,
        string? Sucursal,
        string? Region,
        decimal Costo,
        decimal Total,
        EstadoDePedido Estado,
        DateTimeOffset Creado,
        DateTimeOffset? Final,
        DateTimeOffset? Anonimizado,
        AceptacionDeTexto Terminos,
        AceptacionDeTexto Privacidad,
        AceptacionDeTexto? Reembolso)
    {
        public static DatosBasicosDelPedido Leer(NpgsqlDataReader f) =>
            new(
                f.GetGuid(0),
                f.GetString(1),
                f.GetGuid(2),
                CodigosDePedidos.A<TipoDeEntregaDelPedido>(f.GetString(3)),
                AccesoAlEsquemaDePedidos.LeerTexto(f, 4),
                AccesoAlEsquemaDePedidos.LeerTexto(f, 5),
                f.GetDecimal(6),
                f.GetDecimal(7),
                CodigosDePedidos.A<EstadoDePedido>(f.GetString(8)),
                f.GetFieldValue<DateTimeOffset>(9),
                AccesoAlEsquemaDePedidos.LeerInstante(f, 10),
                AccesoAlEsquemaDePedidos.LeerInstante(f, 14),
                new AceptacionDeTexto(f.GetString(15), f.GetFieldValue<DateTimeOffset>(16)),
                new AceptacionDeTexto(f.GetString(17), f.GetFieldValue<DateTimeOffset>(18)),
                f.IsDBNull(19) ? null : new AceptacionDeTexto(f.GetString(19), f.GetFieldValue<DateTimeOffset>(20)));

        public Pedido Convertir(string? nombre, string? telefono, string? direccion, IReadOnlyList<DatosPersistidosDeLinea> lineas) =>
            Pedido.Rehidratar(new DatosPersistidosDePedido(Id, Numero, CuentaId, Tipo, Sucursal, Region, Costo, Total, Estado, Creado, Final, nombre, telefono, direccion, Anonimizado, Terminos, Privacidad, Reembolso, lineas));
    }

    private Task<IReadOnlyList<Anulacion>> LeerAnulaciones(string filtro, IReadOnlyList<NpgsqlParameter> parametros, string orden, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var filas = new List<Anulacion>();
                var cabeceras = new List<(Guid Id, Func<IReadOnlyList<Guid>, Anulacion> Crear)>();
                await using (var comando = new NpgsqlCommand(
                    $"""
                    SELECT id, pedido_id, origen, motivo, CASE WHEN incluye_envio THEN (SELECT p.costo_de_envio FROM pedidos.pedidos p WHERE p.id = anulaciones.pedido_id) ELSE 0 END, hay_digitales, monto, estado, reembolso_id, envios_cancelados_en, reservas_liberadas_en, reembolso_registrado_en, accesos_revocados_en, iniciada_por,
                           rol_del_iniciador, sucursal_del_iniciador, proximo_intento_en, intentos, creada_en, completada_en
                    FROM pedidos.anulaciones WHERE {filtro}{orden}
                    """,
                    conexion,
                    transaccion))
                {
                    Agregar(comando, parametros);
                    await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await lector.ReadAsync(token).ConfigureAwait(false))
                    {
                        var pasos = new Dictionary<PasoDeAnulacion, DateTimeOffset>();
                        void Paso(PasoDeAnulacion paso, int posicion)
                        {
                            if (AccesoAlEsquemaDePedidos.LeerInstante(lector, posicion) is { } cuando)
                            {
                                pasos[paso] = cuando;
                            }
                        }

                        Paso(PasoDeAnulacion.EnviosCancelados, 9);
                        Paso(PasoDeAnulacion.ReservasLiberadas, 10);
                        Paso(PasoDeAnulacion.ReembolsoRegistrado, 11);
                        Paso(PasoDeAnulacion.AccesosRevocados, 12);
                        var (id, pedido, origen, motivo, envio, digitales, monto, estado, reembolso) =
                            (lector.GetGuid(0), lector.GetGuid(1), CodigosDePedidos.A<OrigenDeLaAnulacion>(lector.GetString(2)), CodigosDePedidos.MotivoDe(lector.GetString(3)), lector.GetDecimal(4), lector.GetBoolean(5), lector.GetDecimal(6),
                             CodigosDePedidos.A<EstadoDeOperacion>(lector.GetString(7)), AccesoAlEsquemaDePedidos.LeerGuid(lector, 8));
                        var iniciador = new IniciadorDeOperacion(lector.GetGuid(13), AccesoAlEsquemaDePedidos.LeerTexto(lector, 14), AccesoAlEsquemaDePedidos.LeerGuid(lector, 15));
                        var (proximo, intentos, creada, completada) = (AccesoAlEsquemaDePedidos.LeerInstante(lector, 16), lector.GetInt32(17), lector.GetFieldValue<DateTimeOffset>(18), AccesoAlEsquemaDePedidos.LeerInstante(lector, 19));
                        cabeceras.Add((id, lineas => Anulacion.Rehidratar(id, pedido, origen, motivo, iniciador, envio, monto, digitales, lineas, estado, reembolso, creada, completada, intentos, proximo, pasos)));
                    }
                }

                var lineasPorAnulacion = await LeerLineasDeOperacion(conexion, transaccion, "anulacion_lineas", "anulacion_id", cabeceras.Select(c => c.Id).ToArray(), token).ConfigureAwait(false);
                foreach (var (id, crear) in cabeceras)
                {
                    filas.Add(crear(lineasPorAnulacion.GetValueOrDefault(id)?.Select(l => l.LineaId).ToList() ?? []));
                }

                return (IReadOnlyList<Anulacion>)filas;
            },
            cancelacion);

    private Task<IReadOnlyList<Entrega>> LeerEntregas(string filtro, IReadOnlyList<NpgsqlParameter> parametros, string orden, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var cabeceras = new List<(Guid Id, Func<IReadOnlyList<LineaDeEntrega>, Entrega> Crear)>();
                await using (var comando = new NpgsqlCommand(
                    $"SELECT id, pedido_id, estado, iniciada_por, rol_del_iniciador, sucursal_del_iniciador, proximo_intento_en, intentos, creada_en FROM pedidos.entregas WHERE {filtro}{orden}",
                    conexion,
                    transaccion))
                {
                    Agregar(comando, parametros);
                    await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await lector.ReadAsync(token).ConfigureAwait(false))
                    {
                        var (id, pedido, estado) = (lector.GetGuid(0), lector.GetGuid(1), CodigosDePedidos.A<EstadoDeOperacion>(lector.GetString(2)));
                        var iniciador = new IniciadorDeOperacion(lector.GetGuid(3), AccesoAlEsquemaDePedidos.LeerTexto(lector, 4), AccesoAlEsquemaDePedidos.LeerGuid(lector, 5));
                        var (proximo, intentos, creada) = (AccesoAlEsquemaDePedidos.LeerInstante(lector, 6), lector.GetInt32(7), lector.GetFieldValue<DateTimeOffset>(8));
                        cabeceras.Add((id, lineas => Entrega.Rehidratar(id, pedido, iniciador, estado, creada, intentos, proximo, lineas)));
                    }
                }

                var lineasPorEntrega = await LeerLineasDeEntrega(conexion, transaccion, cabeceras.Select(c => c.Id).ToArray(), token).ConfigureAwait(false);
                return (IReadOnlyList<Entrega>)[.. cabeceras.Select(c => c.Crear(lineasPorEntrega.GetValueOrDefault(c.Id) ?? []))];
            },
            cancelacion);

    private static async Task<Dictionary<Guid, List<(Guid LineaId, int Posicion)>>> LeerLineasDeOperacion(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string tabla, string columna, Guid[] ids, CancellationToken cancelacion)
    {
        var lineas = new Dictionary<Guid, List<(Guid, int)>>();
        if (ids.Length == 0)
        {
            return lineas;
        }

        await using var comando = new NpgsqlCommand($"SELECT {columna}, linea_id, posicion FROM pedidos.{tabla} WHERE {columna} = ANY(@ids) ORDER BY {columna}, posicion", conexion, transaccion);
        comando.Parameters.Add(Lista("ids", ids));
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            var id = lector.GetGuid(0);
            if (!lineas.TryGetValue(id, out var lista))
            {
                lineas[id] = lista = [];
            }

            lista.Add((lector.GetGuid(1), lector.GetInt32(2)));
        }

        return lineas;
    }

    private static async Task<Dictionary<Guid, List<LineaDeEntrega>>> LeerLineasDeEntrega(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid[] ids, CancellationToken cancelacion)
    {
        var lineas = new Dictionary<Guid, List<LineaDeEntrega>>();
        if (ids.Length == 0)
        {
            return lineas;
        }

        await using var comando = new NpgsqlCommand("SELECT entrega_id, linea_id, clave_de_cierre, cierre_confirmado_en, resultado_del_cierre FROM pedidos.entrega_lineas WHERE entrega_id = ANY(@ids) ORDER BY entrega_id, posicion", conexion, transaccion);
        comando.Parameters.Add(Lista("ids", ids));
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            var id = lector.GetGuid(0);
            if (!lineas.TryGetValue(id, out var lista))
            {
                lineas[id] = lista = [];
            }

            lista.Add(new LineaDeEntrega(
                lector.GetGuid(1),
                lector.GetString(2),
                AccesoAlEsquemaDePedidos.LeerInstante(lector, 3),
                lector.IsDBNull(4) ? null : CodigosDePedidos.A<ResultadoDelCierre>(lector.GetString(4))));
        }

        return lineas;
    }

    // ---------- Escritura ----------

    private static void ParametrosDeLaLinea(NpgsqlCommand comando, LineaDePedido linea)
    {
        comando.Parameters.AddWithValue("id", linea.Id);
        comando.Parameters.AddWithValue("estado", linea.EsDigital ? CodigosDePedidos.De(linea.EstadoDigital!.Value) : CodigosDePedidos.De(linea.EstadoImpreso!.Value));
        comando.Parameters.Add(new NpgsqlParameter("fecha", NpgsqlDbType.Date) { Value = linea.FechaEstimadaDeLlegada is { } f ? f : DBNull.Value });
        comando.Parameters.AddWithValue("listo", AccesoAlEsquemaDePedidos.UtcONulo(linea.ListoEn));
        comando.Parameters.AddWithValue("entregada", AccesoAlEsquemaDePedidos.UtcONulo(linea.EntregadaEn));
        comando.Parameters.AddWithValue("noRecogida", AccesoAlEsquemaDePedidos.UtcONulo(linea.NoRecogidaEn));
        comando.Parameters.AddWithValue("anulada", AccesoAlEsquemaDePedidos.UtcONulo(linea.AnuladaEn));
        comando.Parameters.AddWithValue("envio", AccesoAlEsquemaDePedidos.ONulo(linea.EnvioId));
    }

    private static void ParametrosDeLaAnulacion(NpgsqlCommand comando, Anulacion a)
    {
        comando.Parameters.AddWithValue("envio", a.IncluyeEnvio);
        comando.Parameters.AddWithValue("digitales", a.HayDigitales);
        comando.Parameters.AddWithValue("monto", a.Monto);
        comando.Parameters.AddWithValue("estado", CodigosDePedidos.De(a.Estado));
        comando.Parameters.AddWithValue("reembolso", AccesoAlEsquemaDePedidos.ONulo(a.ReembolsoId));
        comando.Parameters.AddWithValue("envios", AccesoAlEsquemaDePedidos.UtcONulo(a.PasoConfirmadoEn(PasoDeAnulacion.EnviosCancelados)));
        comando.Parameters.AddWithValue("reservas", AccesoAlEsquemaDePedidos.UtcONulo(a.PasoConfirmadoEn(PasoDeAnulacion.ReservasLiberadas)));
        comando.Parameters.AddWithValue("reembolsoEn", AccesoAlEsquemaDePedidos.UtcONulo(a.PasoConfirmadoEn(PasoDeAnulacion.ReembolsoRegistrado)));
        comando.Parameters.AddWithValue("accesos", AccesoAlEsquemaDePedidos.UtcONulo(a.PasoConfirmadoEn(PasoDeAnulacion.AccesosRevocados)));
        comando.Parameters.AddWithValue("iniciador", a.Iniciador.UsuarioId);
        comando.Parameters.AddWithValue("rol", AccesoAlEsquemaDePedidos.ONulo(a.Iniciador.Rol));
        comando.Parameters.AddWithValue("sucursal", AccesoAlEsquemaDePedidos.ONulo(a.Iniciador.SucursalId));
        comando.Parameters.AddWithValue("proximo", AccesoAlEsquemaDePedidos.UtcONulo(a.ProximoIntentoEn));
        comando.Parameters.AddWithValue("intentos", a.Intentos);
        comando.Parameters.AddWithValue("completada", AccesoAlEsquemaDePedidos.UtcONulo(a.CompletadaEn));
    }

    private static void ParametrosDeLaEntrega(NpgsqlCommand comando, Entrega e)
    {
        comando.Parameters.AddWithValue("estado", CodigosDePedidos.De(e.Estado));
        comando.Parameters.AddWithValue("iniciador", e.Iniciador.UsuarioId);
        comando.Parameters.AddWithValue("rol", AccesoAlEsquemaDePedidos.ONulo(e.Iniciador.Rol));
        comando.Parameters.AddWithValue("sucursal", AccesoAlEsquemaDePedidos.ONulo(e.Iniciador.SucursalId));
        comando.Parameters.AddWithValue("proximo", AccesoAlEsquemaDePedidos.UtcONulo(e.ProximoIntentoEn));
        comando.Parameters.AddWithValue("intentos", e.Intentos);
    }

    /// <summary>Reescribe las líneas de una anulación (se recalculan si un 409 revela que una salió): se borran y se insertan con su posición y si la operación sigue en curso.</summary>
    private static async Task ReemplazarLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string tabla, string columna, Guid id, IReadOnlyList<Guid> lineaIds, bool enCurso, CancellationToken cancelacion)
    {
        await using (var borrar = new NpgsqlCommand($"DELETE FROM pedidos.{tabla} WHERE {columna} = @id", conexion, transaccion))
        {
            borrar.Parameters.AddWithValue("id", id);
            await borrar.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }

        var posicion = 0;
        foreach (var lineaId in lineaIds)
        {
            await using var comando = new NpgsqlCommand($"INSERT INTO pedidos.{tabla} ({columna}, linea_id, posicion, en_curso) VALUES (@id, @linea, @posicion, @enCurso)", conexion, transaccion);
            comando.Parameters.AddWithValue("id", id);
            comando.Parameters.AddWithValue("linea", lineaId);
            comando.Parameters.AddWithValue("posicion", posicion++);
            comando.Parameters.AddWithValue("enCurso", enCurso);
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }
    }

    private static async Task EscribirLineasDeEntrega(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Entrega entrega, bool insertar, CancellationToken cancelacion)
    {
        var posicion = 0;
        foreach (var linea in entrega.Lineas)
        {
            await using var comando = new NpgsqlCommand(
                insertar
                    ? "INSERT INTO pedidos.entrega_lineas (entrega_id, linea_id, posicion, clave_de_cierre, cierre_confirmado_en, resultado_del_cierre, en_curso) VALUES (@entrega, @linea, @posicion, @clave, @cierre, @resultado, @enCurso)"
                    : "UPDATE pedidos.entrega_lineas SET cierre_confirmado_en = @cierre, resultado_del_cierre = @resultado, en_curso = @enCurso WHERE entrega_id = @entrega AND linea_id = @linea",
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("entrega", entrega.Id);
            comando.Parameters.AddWithValue("linea", linea.LineaId);
            if (insertar)
            {
                comando.Parameters.AddWithValue("posicion", posicion++);
                comando.Parameters.AddWithValue("clave", linea.ClaveDeCierre);
            }

            comando.Parameters.AddWithValue("cierre", AccesoAlEsquemaDePedidos.UtcONulo(linea.CierreConfirmadoEn));
            comando.Parameters.AddWithValue("resultado", linea.ResultadoDelCierre is { } r ? CodigosDePedidos.De(r) : DBNull.Value);
            comando.Parameters.AddWithValue("enCurso", entrega.BloqueaLasLineas);
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }
    }

    private Task Escribir(string sql, IReadOnlyList<NpgsqlParameter> parametros, CancellationToken cancelacion) => Contar(sql, parametros, cancelacion);

    private Task<int> Contar(string sql, IReadOnlyList<NpgsqlParameter> parametros, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                Agregar(comando, parametros);
                if (sql.StartsWith("SELECT", StringComparison.Ordinal))
                {
                    return Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                }

                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    private static async Task<List<Guid>> LeerGuids(NpgsqlCommand comando, CancellationToken cancelacion)
    {
        var ids = new List<Guid>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            ids.Add(lector.GetGuid(0));
        }

        return ids;
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    private static NpgsqlParameter Lista(string nombre, IEnumerable<Guid> valores) => new(nombre, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = valores.ToArray() };

    private static void Agregar(NpgsqlCommand comando, IEnumerable<NpgsqlParameter> parametros)
    {
        foreach (var parametro in parametros)
        {
            comando.Parameters.Add(Copiar(parametro));
        }
    }

    /// <summary>Un parámetro no puede pertenecer a dos comandos: se copia para cada uso.</summary>
    private static NpgsqlParameter Copiar(NpgsqlParameter parametro) => new(parametro.ParameterName, parametro.NpgsqlDbType) { Value = parametro.Value };
}
