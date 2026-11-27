using System.Text.Json;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// El repositorio de las intenciones, los intentos, las compensaciones y los avisos sobre el esquema <c>pagos</c> (T24), con Npgsql directo y bajo <c>EjecutarComo(pagos)</c>. Cada lectura «para actualizar» es <c>SELECT … FOR UPDATE</c> con
/// <c>lock_timeout</c> de 5 s (vencido: <see cref="TimeoutException"/>) y entrega una entidad nueva leída bajo el bloqueo; las listas que bloquean lo hacen por <c>id</c> ascendente. Las altas que pueden repetirse (el intento, la compensación y los avisos)
/// usan <c>INSERT … ON CONFLICT DO NOTHING</c>: un conflicto no aborta la transacción y el resultado dice cuál fue. <b>El contacto, la huella y los títulos viajan como parámetros y nunca en un mensaje de error</b>
/// (<see cref="FallaDeLaBaseDePagosException"/>). Las líneas se reemplazan enteras al guardar la intención.
/// </summary>
internal sealed class RepositorioDePagosPostgres(AccesoAlEsquemaDePagos acceso) : IRepositorioDePagos
{
    private const string Columnas =
        "id, cuenta_id, estado, motivo_de_cierre, tiene_impresos, tipo_de_entrega, codigo_de_sucursal_de_recojo, region_de_entrega, total, envio, huella_de_instantanea, instantanea_obsoleta, iniciada_en, aprobado_en, "
        + "confirmacion_iniciada_en, reservas_confirmadas_en, reclamada_hasta, vence_en, limite_pendiente_en, reserva_expira_en, reserva_extendida, terminos_version, terminos_aceptado_en, privacidad_version, "
        + "privacidad_aceptado_en, reembolso_digital_version, reembolso_digital_aceptado_en, contacto_nombre, contacto_telefono, contacto_direccion, contacto_liberado_en, desvinculada_en, creada_en, cerrada_en, envios_por_sucursal";

    private static readonly JsonSerializerOptions JsonDeLosEnvios = new(JsonSerializerDefaults.Web);

    private const string ColumnasDelIntento =
        "id, intencion_id, medio, estado, referencia_pasarela, consulta_inmediata, motivo_de_rechazo, importe, iniciado_en, enviado_en, resuelto_en, consultas, ultima_consulta_en";

    private const string ColumnasDeLaCompensacion =
        "intencion_id, intento_id, variante, motivo, iniciada_en, reservas_liberadas_en, motivo_de_liberacion, reembolso_registrado_en, reembolso_id, checkout_cerrado_en, completada_en";

    private const string IntentoActivo = "estado IN ('iniciado', 'pendiente_de_confirmacion', 'aprobado')";

    private static readonly string[] NombresDeColumnas = Columnas.Split(", ");

    private static readonly string InsertarIntencion =
        $"INSERT INTO pagos.intenciones ({Columnas}) VALUES ({string.Join(", ", NombresDeColumnas.Select(c => "@" + c))})";

    // El identificador y la fecha de creación nunca cambian.
    private static readonly string ActualizarIntencion =
        $"UPDATE pagos.intenciones SET {string.Join(", ", NombresDeColumnas.Where(c => c is not ("id" or "creada_en")).Select(c => $"{c} = @{c}"))} WHERE id = @id";

    // ---------- Intenciones ----------

    public Task<IntencionDePago?> ObtenerIntencion(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IntencionDePago?>(
            async (conexion, transaccion, token) =>
            {
                var lista = await LeerIntenciones(conexion, transaccion, "id = @valor", [new NpgsqlParameter("valor", pedidoId)], string.Empty, paraActualizar, token).ConfigureAwait(false);
                return lista.FirstOrDefault();
            },
            cancelacion);

    public Task AgregarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(intencion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(InsertarIntencion, conexion, transaccion))
                {
                    ParametrosDeIntencion(comando, intencion);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await InsertarLineas(conexion, transaccion, intencion, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task GuardarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(intencion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(ActualizarIntencion, conexion, transaccion))
                {
                    ParametrosDeIntencion(comando, intencion);
                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        throw new InvalidOperationException("La intención de pago no existe.");
                    }
                }

                foreach (var tabla in new[] { "pagos.intencion_lineas", "pagos.intencion_reserva_lineas" })
                {
                    await using var borrar = new NpgsqlCommand($"DELETE FROM {tabla} WHERE intencion_id = @intencion", conexion, transaccion);
                    borrar.Parameters.AddWithValue("intencion", intencion.Id);
                    await borrar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await InsertarLineas(conexion, transaccion, intencion, token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<IntencionDePago>> ListarIntencionesDeLaCuenta(Guid cuentaId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<IntencionDePago>>(
            async (conexion, transaccion, token) => await LeerIntenciones(conexion, transaccion, "cuenta_id = @valor", [new NpgsqlParameter("valor", cuentaId)], " ORDER BY id", paraActualizar, token).ConfigureAwait(false),
            cancelacion);

    public Task<int> ContarIntencionesEnCursoDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM pagos.intenciones WHERE cuenta_id = @cuenta AND estado IN ('con_cobro', 'aprobada')", conexion, transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                return Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> ListarIntencionesAprobadas(int limite, CancellationToken cancelacion = default) =>
        ListarIdentificadores("SELECT id FROM pagos.intenciones WHERE estado = 'aprobada' ORDER BY aprobado_en, id LIMIT @limite", [new NpgsqlParameter("limite", limite)], cancelacion);

    public Task<IReadOnlyList<Guid>> ListarIntencionesAbiertasVencidas(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        ListarIdentificadores(
            "SELECT id FROM pagos.intenciones WHERE estado = 'abierta' AND vence_en <= @ahora ORDER BY vence_en, id LIMIT @limite",
            [Instante("ahora", ahora), new NpgsqlParameter("limite", limite)],
            cancelacion);

    public Task<IReadOnlyList<Guid>> ListarIntencionesConContactoVencido(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        ListarIdentificadores(
            "SELECT id FROM pagos.intenciones WHERE contacto_nombre IS NOT NULL AND creada_en <= @corte ORDER BY creada_en, id LIMIT @limite",
            [Instante("corte", ahora - PlazosDelPago.RetencionDelContacto), new NpgsqlParameter("limite", limite)],
            cancelacion);

    public Task<IReadOnlyList<IntencionDePago>> ListarIntencionesCerradasParaDepurar(DateTimeOffset corte, int limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<IntencionDePago>>(
            async (conexion, transaccion, token) => await LeerIntenciones(
                conexion,
                transaccion,
                "cuenta_id IS NOT NULL AND cerrada_en IS NOT NULL AND cerrada_en < @corte",
                [Instante("corte", corte), new NpgsqlParameter("limite", limite)],
                " ORDER BY cerrada_en, id LIMIT @limite",
                paraActualizar: true,
                token).ConfigureAwait(false),
            cancelacion);

    // ---------- Intentos ----------

    public Task<IntentoDeCobro?> ObtenerIntento(Guid intentoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IntentoDeCobro?>(
            async (conexion, transaccion, token) => (await LeerIntentos(conexion, transaccion, "id = @valor", [new NpgsqlParameter("valor", intentoId)], string.Empty, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<IntentoDeCobro?> ObtenerIntentoActivo(Guid intencionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IntentoDeCobro?>(
            async (conexion, transaccion, token) =>
                (await LeerIntentos(conexion, transaccion, $"intencion_id = @valor AND {IntentoActivo}", [new NpgsqlParameter("valor", intencionId)], string.Empty, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<IntentoDeCobro?> ObtenerUltimoIntento(Guid intencionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IntentoDeCobro?>(
            async (conexion, transaccion, token) =>
                (await LeerIntentos(conexion, transaccion, "intencion_id = @valor", [new NpgsqlParameter("valor", intencionId)], " ORDER BY iniciado_en DESC, id DESC LIMIT 1", token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<ResultadoDeAgregarIntento> AgregarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(intento);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // ON CONFLICT DO NOTHING: ni la clave repetida ni el intento activo existente abortan la transacción (el caso de uso sigue leyendo).
                int filas;
                await using (var comando = new NpgsqlCommand(
                    $"INSERT INTO pagos.intentos ({ColumnasDelIntento}) VALUES (@id, @intencion, @medio, @estado, @referencia, @consulta, @motivo, @importe, @iniciado, @enviado, @resuelto, @consultas, @ultima) ON CONFLICT DO NOTHING",
                    conexion,
                    transaccion))
                {
                    ParametrosDelIntento(comando, intento);
                    filas = await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                if (filas == 1)
                {
                    return ResultadoDeAgregarIntento.Agregado;
                }

                await using var existe = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pagos.intentos WHERE id = @id)", conexion, transaccion);
                existe.Parameters.AddWithValue("id", intento.Id);
                return (bool)(await existe.ExecuteScalarAsync(token).ConfigureAwait(false) ?? false) ? ResultadoDeAgregarIntento.ClaveRepetida : ResultadoDeAgregarIntento.IntentoActivoExistente;
            },
            cancelacion);
    }

    public Task GuardarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(intento);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE pagos.intentos SET
                        estado = @estado, referencia_pasarela = @referencia, consulta_inmediata = @consulta, motivo_de_rechazo = @motivo, enviado_en = @enviado, resuelto_en = @resuelto,
                        consultas = @consultas, ultima_consulta_en = @ultima
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion);
                ParametrosDelIntento(comando, intento, soloLoQueCambia: true);
                if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                {
                    throw new InvalidOperationException("El intento de cobro no existe.");
                }

                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Guid>> ListarIntentosParaConsultar(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        ListarIdentificadores(
            "SELECT id FROM pagos.intentos WHERE estado IN ('iniciado', 'pendiente_de_confirmacion') OR (estado IN ('vencido', 'rechazado') AND resuelto_en >= @desde) ORDER BY iniciado_en, id LIMIT @limite",
            [Instante("desde", ahora - IntentoDeCobro.VentanaDeConciliacion), new NpgsqlParameter("limite", limite)],
            cancelacion);

    // ---------- Compensaciones ----------

    public Task<IReadOnlyList<Compensacion>> ListarCompensacionesSinTerminar(int limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Compensacion>>(
            async (conexion, transaccion, token) => await LeerCompensaciones(conexion, transaccion, "completada_en IS NULL", [new NpgsqlParameter("limite", limite)], " ORDER BY iniciada_en, intento_id LIMIT @limite", token).ConfigureAwait(false),
            cancelacion);

    public Task<Compensacion?> ObtenerCompensacion(Guid intentoId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<Compensacion?>(
            async (conexion, transaccion, token) => (await LeerCompensaciones(conexion, transaccion, "intento_id = @valor", [new NpgsqlParameter("valor", intentoId)], string.Empty, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<bool> AgregarCompensacion(Compensacion compensacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(compensacion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"INSERT INTO pagos.compensaciones ({ColumnasDeLaCompensacion}) VALUES (@intencion, @intento, @variante, @motivo, @iniciada, @liberadas, @motivoLiberacion, @reembolsoEn, @reembolso, @cerrado, @completada) ON CONFLICT DO NOTHING",
                    conexion,
                    transaccion);
                ParametrosDeLaCompensacion(comando, compensacion);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task GuardarCompensacion(Compensacion compensacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(compensacion);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE pagos.compensaciones SET
                        reservas_liberadas_en = @liberadas, motivo_de_liberacion = @motivoLiberacion, reembolso_registrado_en = @reembolsoEn, reembolso_id = @reembolso,
                        checkout_cerrado_en = @cerrado, completada_en = @completada
                    WHERE intencion_id = @intencion AND intento_id = @intento
                    """,
                    conexion,
                    transaccion);
                ParametrosDeLaCompensacion(comando, compensacion);
                if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                {
                    throw new InvalidOperationException("La compensación no existe.");
                }

                return 0;
            },
            cancelacion);
    }

    // ---------- Avisos ----------

    public Task<bool> AgregarAvisoACarrito(AvisoACarrito aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO pagos.avisos_a_carrito (id, intencion_id, intento_id, transicion, estado, creado_en, enviado_en, intentos, proximo_intento_en) VALUES (@id, @intencion, @intento, @evento, @estado, @creado, @enviado, @intentos, @proximo) ON CONFLICT DO NOTHING",
                    conexion,
                    transaccion);
                ParametrosDelAviso(comando, aviso.Id, aviso.IntencionId, aviso.IntentoId, TextosDePagos.A(aviso.Transicion), TextosDePagos.A(aviso.Estado), aviso.CreadoEn, aviso.EnviadoEn, aviso.Intentos, aviso.ProximoIntentoEn);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task<bool> AgregarAvisoDePago(AvisoDePago aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO pagos.avisos_de_pago (id, intencion_id, intento_id, evento, estado, creado_en, enviado_en, intentos, proximo_intento_en) VALUES (@id, @intencion, @intento, @evento, @estado, @creado, @enviado, @intentos, @proximo) ON CONFLICT DO NOTHING",
                    conexion,
                    transaccion);
                ParametrosDelAviso(comando, aviso.Id, aviso.IntencionId, aviso.IntentoId, TextosDePagos.A(aviso.Evento), TextosDePagos.A(aviso.Estado), aviso.CreadoEn, aviso.EnviadoEn, aviso.Intentos, aviso.ProximoIntentoEn);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<AvisoACarrito>> ListarAvisosACarritoDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<AvisoACarrito>>(
            async (conexion, transaccion, token) => await LeerAvisosACarrito(conexion, transaccion, "intencion_id = @valor", [new NpgsqlParameter("valor", intencionId)], " ORDER BY creado_en, id", token).ConfigureAwait(false),
            cancelacion);

    public Task<IReadOnlyList<AvisoDePago>> ListarAvisosDePagoDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<AvisoDePago>>(
            async (conexion, transaccion, token) => await LeerAvisosDePago(conexion, transaccion, "intencion_id = @valor", [new NpgsqlParameter("valor", intencionId)], " ORDER BY creado_en, id", token).ConfigureAwait(false),
            cancelacion);

    public Task<IReadOnlyList<Guid>> ListarIntencionesConAvisosACarritoListos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        ListarIdentificadores(
            "SELECT intencion_id FROM pagos.avisos_a_carrito WHERE estado = 'pendiente' AND (proximo_intento_en IS NULL OR proximo_intento_en <= @ahora) GROUP BY intencion_id ORDER BY min(creado_en), intencion_id LIMIT @limite",
            [Instante("ahora", ahora), new NpgsqlParameter("limite", limite)],
            cancelacion);

    public Task<IReadOnlyList<AvisoDePago>> ListarAvisosDePagoListos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<AvisoDePago>>(
            async (conexion, transaccion, token) => await LeerAvisosDePago(
                conexion,
                transaccion,
                "estado = 'pendiente' AND (proximo_intento_en IS NULL OR proximo_intento_en <= @ahora)",
                [Instante("ahora", ahora), new NpgsqlParameter("limite", limite)],
                " ORDER BY creado_en, id LIMIT @limite",
                token).ConfigureAwait(false),
            cancelacion);

    public Task GuardarAvisoACarrito(AvisoACarrito aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return ActualizarAviso("pagos.avisos_a_carrito", aviso.Id, TextosDePagos.A(aviso.Estado), aviso.EnviadoEn, aviso.Intentos, aviso.ProximoIntentoEn, cancelacion);
    }

    public Task GuardarAvisoDePago(AvisoDePago aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return ActualizarAviso("pagos.avisos_de_pago", aviso.Id, TextosDePagos.A(aviso.Estado), aviso.EnviadoEn, aviso.Intentos, aviso.ProximoIntentoEn, cancelacion);
    }

    public Task<int> PurgarAvisosAnterioresA(DateTimeOffset corte, int limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var borrados = 0;
                foreach (var tabla in new[] { "pagos.avisos_de_pago", "pagos.avisos_a_carrito" })
                {
                    await using var comando = new NpgsqlCommand($"DELETE FROM {tabla} WHERE id IN (SELECT id FROM {tabla} WHERE creado_en < @corte ORDER BY creado_en, id LIMIT @limite)", conexion, transaccion);
                    comando.Parameters.Add(Instante("corte", corte));
                    comando.Parameters.AddWithValue("limite", limite);
                    borrados += await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return borrados;
            },
            cancelacion);

    // ---------- Piezas ----------

    private Task ActualizarAviso(string tabla, Guid id, string estado, DateTimeOffset? enviadoEn, int intentos, DateTimeOffset? proximoIntentoEn, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"UPDATE {tabla} SET estado = @estado, enviado_en = @enviado, intentos = @intentos, proximo_intento_en = @proximo WHERE id = @id", conexion, transaccion);
                Parametro(comando, "id", NpgsqlDbType.Uuid, id);
                Parametro(comando, "estado", NpgsqlDbType.Text, estado);
                Parametro(comando, "enviado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(enviadoEn));
                Parametro(comando, "intentos", NpgsqlDbType.Integer, intentos);
                Parametro(comando, "proximo", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(proximoIntentoEn));
                if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                {
                    throw new InvalidOperationException("El aviso no existe.");
                }

                return 0;
            },
            cancelacion);

    private Task<IReadOnlyList<Guid>> ListarIdentificadores(string sql, NpgsqlParameter[] parametros, CancellationToken cancelacion) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                var ids = new List<Guid>();
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                comando.Parameters.AddRange(parametros);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    ids.Add(filas.GetGuid(0));
                }

                return ids;
            },
            cancelacion);

    private static NpgsqlParameter Instante(string nombre, DateTimeOffset valor) => new(nombre, NpgsqlDbType.TimestampTz) { Value = valor.ToUniversalTime() };

    private static void Parametro(NpgsqlCommand comando, string nombre, NpgsqlDbType tipo, object? valor) =>
        comando.Parameters.Add(new NpgsqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value });

    private static void ParametrosDelAviso(NpgsqlCommand comando, Guid id, Guid intencionId, Guid intentoId, string evento, string estado, DateTimeOffset creadoEn, DateTimeOffset? enviadoEn, int intentos, DateTimeOffset? proximoIntentoEn)
    {
        Parametro(comando, "id", NpgsqlDbType.Uuid, id);
        Parametro(comando, "intencion", NpgsqlDbType.Uuid, intencionId);
        Parametro(comando, "intento", NpgsqlDbType.Uuid, intentoId);
        Parametro(comando, "evento", NpgsqlDbType.Text, evento);
        Parametro(comando, "estado", NpgsqlDbType.Text, estado);
        Parametro(comando, "creado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(creadoEn));
        Parametro(comando, "enviado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(enviadoEn));
        Parametro(comando, "intentos", NpgsqlDbType.Integer, intentos);
        Parametro(comando, "proximo", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(proximoIntentoEn));
    }

    private static void ParametrosDelIntento(NpgsqlCommand comando, IntentoDeCobro i, bool soloLoQueCambia = false)
    {
        Parametro(comando, "id", NpgsqlDbType.Uuid, i.Id);
        Parametro(comando, "estado", NpgsqlDbType.Text, TextosDePagos.A(i.Estado));
        Parametro(comando, "referencia", NpgsqlDbType.Text, i.ReferenciaDePasarela);
        Parametro(comando, "consulta", NpgsqlDbType.Boolean, i.ConsultaInmediata);
        Parametro(comando, "motivo", NpgsqlDbType.Text, TextosDePagos.A(i.MotivoDeRechazo));
        Parametro(comando, "enviado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.EnviadoEn));
        Parametro(comando, "resuelto", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ResueltoEn));
        Parametro(comando, "consultas", NpgsqlDbType.Integer, i.Consultas);
        Parametro(comando, "ultima", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.UltimaConsultaEn));
        if (soloLoQueCambia)
        {
            return;
        }

        Parametro(comando, "intencion", NpgsqlDbType.Uuid, i.IntencionId);
        Parametro(comando, "medio", NpgsqlDbType.Text, TextosDePagos.A(i.Medio));
        Parametro(comando, "importe", NpgsqlDbType.Numeric, i.Importe.Valor);
        Parametro(comando, "iniciado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.IniciadoEn));
    }

    private static void ParametrosDeLaCompensacion(NpgsqlCommand comando, Compensacion c)
    {
        Parametro(comando, "intencion", NpgsqlDbType.Uuid, c.IntencionId);
        Parametro(comando, "intento", NpgsqlDbType.Uuid, c.IntentoId);
        Parametro(comando, "variante", NpgsqlDbType.Text, TextosDePagos.A(c.Variante));
        Parametro(comando, "motivo", NpgsqlDbType.Text, TextosDePagos.A(c.Motivo));
        Parametro(comando, "iniciada", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(c.IniciadaEn));
        Parametro(comando, "liberadas", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(c.ReservasLiberadasEn));
        Parametro(comando, "motivoLiberacion", NpgsqlDbType.Text, TextosDePagos.A(c.MotivoDeLiberacion));
        Parametro(comando, "reembolsoEn", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(c.ReembolsoRegistradoEn));
        Parametro(comando, "reembolso", NpgsqlDbType.Uuid, c.ReembolsoId);
        Parametro(comando, "cerrado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(c.CheckoutCerradoEn));
        Parametro(comando, "completada", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(c.CompletadaEn));
    }

    /// <summary>El detalle del envío por sucursal como se guarda (<c>jsonb</c>): <c>[{"codigoDeSucursal":"…","importe":0.00}]</c>, o nulo.</summary>
    private static string? TextoDeLosEnvios(IReadOnlyList<EnvioDeSucursalDeIntencion>? envios) =>
        envios is null ? null : JsonSerializer.Serialize(envios.Select(e => new EnvioGuardado(e.CodigoDeSucursal, e.Importe.Valor)), JsonDeLosEnvios);

    private static IReadOnlyList<EnvioDeSucursalDeIntencion>? EnviosDe(string? texto) =>
        texto is null
            ? null
            : [.. (JsonSerializer.Deserialize<EnvioGuardado[]>(texto, JsonDeLosEnvios) ?? throw new InvalidOperationException("El detalle de envíos guardado de la intención no es válido."))
                .Select(e => new EnvioDeSucursalDeIntencion(e.CodigoDeSucursal, Importe.De(e.Importe)))];

    private sealed record EnvioGuardado(string CodigoDeSucursal, decimal Importe);

    private static void ParametrosDeIntencion(NpgsqlCommand comando, IntencionDePago i)
    {
        Parametro(comando, "id", NpgsqlDbType.Uuid, i.Id);
        Parametro(comando, "cuenta_id", NpgsqlDbType.Uuid, i.CuentaId);
        Parametro(comando, "estado", NpgsqlDbType.Text, TextosDePagos.A(i.Estado));
        Parametro(comando, "motivo_de_cierre", NpgsqlDbType.Text, TextosDePagos.A(i.MotivoDeCierre));
        Parametro(comando, "tiene_impresos", NpgsqlDbType.Boolean, i.TieneImpresos);
        Parametro(comando, "tipo_de_entrega", NpgsqlDbType.Text, TextosDePagos.A(i.TipoDeEntrega));
        Parametro(comando, "codigo_de_sucursal_de_recojo", NpgsqlDbType.Text, i.CodigoDeSucursalDeRecojo);
        Parametro(comando, "region_de_entrega", NpgsqlDbType.Text, i.RegionDeEntrega);
        Parametro(comando, "total", NpgsqlDbType.Numeric, i.Total.Valor);
        Parametro(comando, "envio", NpgsqlDbType.Numeric, i.Envio.Valor);
        Parametro(comando, "huella_de_instantanea", NpgsqlDbType.Text, i.HuellaDeInstantanea);
        Parametro(comando, "instantanea_obsoleta", NpgsqlDbType.Boolean, i.InstantaneaObsoleta);
        Parametro(comando, "iniciada_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.IniciadaEn));
        Parametro(comando, "aprobado_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.AprobadoEn));
        Parametro(comando, "confirmacion_iniciada_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ConfirmacionIniciadaEn));
        Parametro(comando, "reservas_confirmadas_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ReservasConfirmadasEn));
        Parametro(comando, "reclamada_hasta", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ReclamadaHasta));
        Parametro(comando, "vence_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.VenceEn));
        Parametro(comando, "limite_pendiente_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.LimitePendienteEn));
        Parametro(comando, "reserva_expira_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ReservaExpiraEn));
        Parametro(comando, "reserva_extendida", NpgsqlDbType.Boolean, i.ReservaExtendida);
        Parametro(comando, "terminos_version", NpgsqlDbType.Text, i.Terminos?.Version);
        Parametro(comando, "terminos_aceptado_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.Terminos?.AceptadoEn));
        Parametro(comando, "privacidad_version", NpgsqlDbType.Text, i.Privacidad?.Version);
        Parametro(comando, "privacidad_aceptado_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.Privacidad?.AceptadoEn));
        Parametro(comando, "reembolso_digital_version", NpgsqlDbType.Text, i.ReembolsoDigital?.Version);
        Parametro(comando, "reembolso_digital_aceptado_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ReembolsoDigital?.AceptadoEn));
        Parametro(comando, "contacto_nombre", NpgsqlDbType.Text, i.Contacto?.Nombre);
        Parametro(comando, "contacto_telefono", NpgsqlDbType.Text, i.Contacto?.Telefono);
        Parametro(comando, "contacto_direccion", NpgsqlDbType.Text, i.Contacto?.Direccion);
        Parametro(comando, "contacto_liberado_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.ContactoLiberadoEn));
        Parametro(comando, "desvinculada_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.DesvinculadaEn));
        Parametro(comando, "creada_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.CreadaEn));
        Parametro(comando, "cerrada_en", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(i.CerradaEn));
        Parametro(comando, "envios_por_sucursal", NpgsqlDbType.Jsonb, TextoDeLosEnvios(i.EnviosPorSucursal));
    }

    private static async Task InsertarLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, IntencionDePago intencion, CancellationToken token)
    {
        var posicion = 0;
        foreach (var linea in intencion.Lineas)
        {
            await using var comando = new NpgsqlCommand(
                "INSERT INTO pagos.intencion_lineas (intencion_id, linea_id, posicion, edicion_id, es_digital, titulo, isbn, cantidad, precio_unitario, importe) VALUES (@intencion, @linea, @posicion, @edicion, @digital, @titulo, @isbn, @cantidad, @precio, @importe)",
                conexion,
                transaccion);
            Parametro(comando, "intencion", NpgsqlDbType.Uuid, intencion.Id);
            Parametro(comando, "linea", NpgsqlDbType.Uuid, linea.LineaId);
            Parametro(comando, "posicion", NpgsqlDbType.Integer, posicion++);
            Parametro(comando, "edicion", NpgsqlDbType.Uuid, linea.EdicionId);
            Parametro(comando, "digital", NpgsqlDbType.Boolean, linea.EsDigital);
            Parametro(comando, "titulo", NpgsqlDbType.Text, linea.Titulo);
            Parametro(comando, "isbn", NpgsqlDbType.Text, linea.Isbn);
            Parametro(comando, "cantidad", NpgsqlDbType.Integer, linea.Cantidad);
            Parametro(comando, "precio", NpgsqlDbType.Numeric, linea.PrecioUnitario.Valor);
            Parametro(comando, "importe", NpgsqlDbType.Numeric, linea.Importe.Valor);
            await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        foreach (var reserva in intencion.LineasConfirmadas)
        {
            await using var comando = new NpgsqlCommand(
                "INSERT INTO pagos.intencion_reserva_lineas (intencion_id, linea_id, codigo_de_fuente, requiere_traslado, traslado_id, fecha_estimada_de_llegada) VALUES (@intencion, @linea, @fuente, @traslado, @trasladoId, @llegada)",
                conexion,
                transaccion);
            Parametro(comando, "intencion", NpgsqlDbType.Uuid, intencion.Id);
            Parametro(comando, "linea", NpgsqlDbType.Uuid, reserva.LineaId);
            Parametro(comando, "fuente", NpgsqlDbType.Text, reserva.CodigoDeFuente);
            Parametro(comando, "traslado", NpgsqlDbType.Boolean, reserva.RequiereTraslado);
            Parametro(comando, "trasladoId", NpgsqlDbType.Uuid, reserva.TrasladoId);
            Parametro(comando, "llegada", NpgsqlDbType.Date, reserva.FechaEstimadaDeLlegada);
            await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<IntencionDePago>> LeerIntenciones(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        string filtro,
        NpgsqlParameter[] parametros,
        string sufijo,
        bool paraActualizar,
        CancellationToken token)
    {
        if (paraActualizar)
        {
            await AccesoAlEsquemaDePagos.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
        }

        var cabeceras = new List<(Guid Id, Func<IReadOnlyList<LineaDeIntencion>, IReadOnlyList<ReservaConfirmadaDeLinea>, IntencionDePago> Construir)>();
        await using (var comando = new NpgsqlCommand($"SELECT {Columnas} FROM pagos.intenciones WHERE {filtro}{sufijo}{(paraActualizar ? " FOR UPDATE" : string.Empty)}", conexion, transaccion))
        {
            comando.Parameters.AddRange(parametros);
            await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await filas.ReadAsync(token).ConfigureAwait(false))
            {
                cabeceras.Add(LeerCabecera(filas));
            }
        }

        var intenciones = new List<IntencionDePago>(cabeceras.Count);
        foreach (var (id, construir) in cabeceras)
        {
            intenciones.Add(construir(await LeerLineas(conexion, transaccion, id, token).ConfigureAwait(false), await LeerReservas(conexion, transaccion, id, token).ConfigureAwait(false)));
        }

        return intenciones;
    }

    private static (Guid Id, Func<IReadOnlyList<LineaDeIntencion>, IReadOnlyList<ReservaConfirmadaDeLinea>, IntencionDePago> Construir) LeerCabecera(NpgsqlDataReader f)
    {
        var id = f.GetGuid(0);
        var cuenta = AccesoAlEsquemaDePagos.LeerNulable<Guid>(f, 1);
        var estado = TextosDePagos.De<EstadoDeIntencion>(f.GetString(2));
        var motivoDeCierre = TextosDePagos.DeONulo<MotivoDeCierre>(AccesoAlEsquemaDePagos.LeerTexto(f, 3));
        var tieneImpresos = f.GetBoolean(4);
        var entrega = TextosDePagos.DeONulo<TipoDeEntrega>(AccesoAlEsquemaDePagos.LeerTexto(f, 5));
        var sucursal = AccesoAlEsquemaDePagos.LeerTexto(f, 6);
        var region = AccesoAlEsquemaDePagos.LeerTexto(f, 7);
        var total = Importe.De(f.GetDecimal(8));
        var envio = Importe.De(f.GetDecimal(9));
        var huella = AccesoAlEsquemaDePagos.LeerTexto(f, 10);
        var obsoleta = f.GetBoolean(11);
        var iniciada = AccesoAlEsquemaDePagos.LeerInstante(f, 12)!.Value;
        var aprobado = AccesoAlEsquemaDePagos.LeerInstante(f, 13);
        var confirmacion = AccesoAlEsquemaDePagos.LeerInstante(f, 14);
        var confirmadas = AccesoAlEsquemaDePagos.LeerInstante(f, 15);
        var reclamada = AccesoAlEsquemaDePagos.LeerInstante(f, 16);
        var vence = AccesoAlEsquemaDePagos.LeerInstante(f, 17)!.Value;
        var limite = AccesoAlEsquemaDePagos.LeerInstante(f, 18)!.Value;
        var reservaExpira = AccesoAlEsquemaDePagos.LeerInstante(f, 19);
        var extendida = f.GetBoolean(20);
        var terminos = Texto(f, 21, 22);
        var privacidad = Texto(f, 23, 24);
        var reembolsoDigital = Texto(f, 25, 26);
        var contacto = AccesoAlEsquemaDePagos.LeerTexto(f, 27) is { } nombre ? new ContactoDeEntrega(nombre, f.GetString(28), f.GetString(29)) : null;
        var contactoLiberado = AccesoAlEsquemaDePagos.LeerInstante(f, 30);
        var desvinculada = AccesoAlEsquemaDePagos.LeerInstante(f, 31);
        var creada = AccesoAlEsquemaDePagos.LeerInstante(f, 32)!.Value;
        var cerrada = AccesoAlEsquemaDePagos.LeerInstante(f, 33);
        var envios = EnviosDe(AccesoAlEsquemaDePagos.LeerTexto(f, 34));

        return (
            id,
            (lineas, reservas) => IntencionDePago.Rehidratar(
                id, creada, cuenta, estado, motivoDeCierre, tieneImpresos, entrega, sucursal, region, total, envio, huella, obsoleta, lineas, terminos, privacidad, reembolsoDigital, contacto, contactoLiberado, desvinculada,
                iniciada, vence, limite, reservaExpira, extendida, aprobado, confirmacion, confirmadas, reservas, reclamada, cerrada, envios));
    }

    private static TextoAceptado? Texto(NpgsqlDataReader f, int version, int fecha) =>
        AccesoAlEsquemaDePagos.LeerTexto(f, version) is { } v && AccesoAlEsquemaDePagos.LeerInstante(f, fecha) is { } en ? new TextoAceptado(v, en) : null;

    private static async Task<IReadOnlyList<LineaDeIntencion>> LeerLineas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid intencionId, CancellationToken token)
    {
        var lineas = new List<LineaDeIntencion>();
        await using var comando = new NpgsqlCommand(
            "SELECT linea_id, edicion_id, es_digital, titulo, isbn, cantidad, precio_unitario, importe FROM pagos.intencion_lineas WHERE intencion_id = @intencion ORDER BY posicion",
            conexion,
            transaccion);
        comando.Parameters.AddWithValue("intencion", intencionId);
        await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await filas.ReadAsync(token).ConfigureAwait(false))
        {
            lineas.Add(new LineaDeIntencion(
                filas.GetGuid(0),
                AccesoAlEsquemaDePagos.LeerNulable<Guid>(filas, 1),
                filas.GetBoolean(2),
                AccesoAlEsquemaDePagos.LeerTexto(filas, 3),
                AccesoAlEsquemaDePagos.LeerTexto(filas, 4),
                filas.GetInt32(5),
                Importe.De(filas.GetDecimal(6)),
                Importe.De(filas.GetDecimal(7))));
        }

        return lineas;
    }

    private static async Task<IReadOnlyList<ReservaConfirmadaDeLinea>> LeerReservas(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid intencionId, CancellationToken token)
    {
        var reservas = new List<ReservaConfirmadaDeLinea>();
        await using var comando = new NpgsqlCommand(
            "SELECT linea_id, codigo_de_fuente, requiere_traslado, traslado_id, fecha_estimada_de_llegada FROM pagos.intencion_reserva_lineas WHERE intencion_id = @intencion ORDER BY linea_id",
            conexion,
            transaccion);
        comando.Parameters.AddWithValue("intencion", intencionId);
        await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await filas.ReadAsync(token).ConfigureAwait(false))
        {
            reservas.Add(new ReservaConfirmadaDeLinea(
                filas.GetGuid(0),
                filas.GetString(1),
                filas.GetBoolean(2),
                AccesoAlEsquemaDePagos.LeerNulable<Guid>(filas, 3),
                filas.IsDBNull(4) ? null : filas.GetFieldValue<DateOnly>(4)));
        }

        return reservas;
    }

    private static async Task<IReadOnlyList<IntentoDeCobro>> LeerIntentos(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, string sufijo, CancellationToken token)
    {
        var intentos = new List<IntentoDeCobro>();
        await using var comando = new NpgsqlCommand($"SELECT {ColumnasDelIntento} FROM pagos.intentos WHERE {filtro}{sufijo}", conexion, transaccion);
        comando.Parameters.AddRange(parametros);
        await using var f = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await f.ReadAsync(token).ConfigureAwait(false))
        {
            intentos.Add(IntentoDeCobro.Rehidratar(
                f.GetGuid(0),
                f.GetGuid(1),
                TextosDePagos.De<MedioDePago>(f.GetString(2)),
                Importe.De(f.GetDecimal(7)),
                AccesoAlEsquemaDePagos.LeerInstante(f, 8)!.Value,
                TextosDePagos.De<EstadoDeIntento>(f.GetString(3)),
                AccesoAlEsquemaDePagos.LeerTexto(f, 4),
                f.GetBoolean(5),
                TextosDePagos.DeONulo<MotivoDeRechazo>(AccesoAlEsquemaDePagos.LeerTexto(f, 6)),
                AccesoAlEsquemaDePagos.LeerInstante(f, 9),
                AccesoAlEsquemaDePagos.LeerInstante(f, 10),
                f.GetInt32(11),
                AccesoAlEsquemaDePagos.LeerInstante(f, 12)));
        }

        return intentos;
    }

    private static async Task<IReadOnlyList<Compensacion>> LeerCompensaciones(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, string sufijo, CancellationToken token)
    {
        var compensaciones = new List<Compensacion>();
        await using var comando = new NpgsqlCommand($"SELECT {ColumnasDeLaCompensacion} FROM pagos.compensaciones WHERE {filtro}{sufijo}", conexion, transaccion);
        comando.Parameters.AddRange(parametros);
        await using var f = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await f.ReadAsync(token).ConfigureAwait(false))
        {
            compensaciones.Add(Compensacion.Rehidratar(
                f.GetGuid(0),
                f.GetGuid(1),
                TextosDePagos.De<MotivoDeCompensacion>(f.GetString(3)),
                TextosDePagos.De<VarianteDeCompensacion>(f.GetString(2)),
                AccesoAlEsquemaDePagos.LeerInstante(f, 4)!.Value,
                AccesoAlEsquemaDePagos.LeerInstante(f, 5),
                TextosDePagos.DeONulo<MotivoDeLiberacion>(AccesoAlEsquemaDePagos.LeerTexto(f, 6)),
                AccesoAlEsquemaDePagos.LeerInstante(f, 7),
                AccesoAlEsquemaDePagos.LeerNulable<Guid>(f, 8),
                AccesoAlEsquemaDePagos.LeerInstante(f, 9),
                AccesoAlEsquemaDePagos.LeerInstante(f, 10)));
        }

        return compensaciones;
    }

    private static async Task<IReadOnlyList<AvisoACarrito>> LeerAvisosACarrito(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, string sufijo, CancellationToken token)
    {
        var avisos = new List<AvisoACarrito>();
        await using var comando = new NpgsqlCommand($"SELECT id, intencion_id, intento_id, transicion, estado, creado_en, enviado_en, intentos, proximo_intento_en FROM pagos.avisos_a_carrito WHERE {filtro}{sufijo}", conexion, transaccion);
        comando.Parameters.AddRange(parametros);
        await using var f = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await f.ReadAsync(token).ConfigureAwait(false))
        {
            avisos.Add(AvisoACarrito.Rehidratar(
                f.GetGuid(0),
                f.GetGuid(1),
                f.GetGuid(2),
                TextosDePagos.De<TransicionDeAviso>(f.GetString(3)),
                TextosDePagos.De<EstadoDeAvisoACarrito>(f.GetString(4)),
                AccesoAlEsquemaDePagos.LeerInstante(f, 5)!.Value,
                AccesoAlEsquemaDePagos.LeerInstante(f, 6),
                f.GetInt32(7),
                AccesoAlEsquemaDePagos.LeerInstante(f, 8)));
        }

        return avisos;
    }

    private static async Task<IReadOnlyList<AvisoDePago>> LeerAvisosDePago(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, string sufijo, CancellationToken token)
    {
        var avisos = new List<AvisoDePago>();
        await using var comando = new NpgsqlCommand($"SELECT id, intencion_id, intento_id, evento, estado, creado_en, enviado_en, intentos, proximo_intento_en FROM pagos.avisos_de_pago WHERE {filtro}{sufijo}", conexion, transaccion);
        comando.Parameters.AddRange(parametros);
        await using var f = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await f.ReadAsync(token).ConfigureAwait(false))
        {
            avisos.Add(AvisoDePago.Rehidratar(
                f.GetGuid(0),
                f.GetGuid(1),
                f.GetGuid(2),
                TextosDePagos.De<EventoDeAvisoDePago>(f.GetString(3)),
                TextosDePagos.De<EstadoDeAvisoDePago>(f.GetString(4)),
                AccesoAlEsquemaDePagos.LeerInstante(f, 5)!.Value,
                AccesoAlEsquemaDePagos.LeerInstante(f, 6),
                f.GetInt32(7),
                AccesoAlEsquemaDePagos.LeerInstante(f, 8)));
        }

        return avisos;
    }
}
