using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>Los dispositivos reconocidos (T33; IDE-55, D11): solo la huella de la marca y la región; guardar inserta o actualiza por (usuario, huella) y deja como máximo diez por usuario, eliminando los más antiguos.</summary>
public sealed class PostgresRepositorioDeDispositivos(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeDispositivos
{
    public Task<IReadOnlyList<Dispositivo>> Listar(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Dispositivo>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT huella_marca, region, emitido_en, congelado_hasta FROM identidad.dispositivos WHERE usuario_id = @u ORDER BY emitido_en DESC, huella_marca",
                    ("u", usuarioId));
                var dispositivos = new List<Dispositivo>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    dispositivos.Add(Dispositivo.Restaurar(usuarioId, filas.GetString(0), filas.GetString(1), AccesoAlEsquemaDeIdentidad.Instante(filas, 2), AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 3)));
                }

                return dispositivos;
            },
            cancelacion);

    public Task Guardar(Dispositivo dispositivo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(dispositivo);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using (var upsert = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    """
                    INSERT INTO identidad.dispositivos (usuario_id, huella_marca, region, emitido_en, congelado_hasta) VALUES (@u, @h, @r, @e, @congelado)
                    ON CONFLICT (usuario_id, huella_marca) DO UPDATE SET region = EXCLUDED.region, congelado_hasta = EXCLUDED.congelado_hasta
                    """,
                    ("u", dispositivo.UsuarioId),
                    ("h", dispositivo.HuellaDeLaMarca),
                    ("r", dispositivo.Region),
                    ("e", dispositivo.Emitido),
                    ("congelado", dispositivo.CongeladoHasta)))
                {
                    await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var podar = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    """
                    DELETE FROM identidad.dispositivos WHERE usuario_id = @u AND huella_marca IN (
                        SELECT huella_marca FROM identidad.dispositivos WHERE usuario_id = @u ORDER BY emitido_en DESC, huella_marca OFFSET @maximo)
                    """,
                    ("u", dispositivo.UsuarioId),
                    ("maximo", IRepositorioDeDispositivos.Maximo));
                await podar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }
}

/// <summary>El cambio de correo en curso (T33; IDE-53, IDE-54): como máximo uno por usuario; guardar reemplaza el anterior (<c>ON CONFLICT (usuario_id)</c>).</summary>
public sealed class PostgresRepositorioDeCambiosDeCorreo(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeCambiosDeCorreo
{
    public Task<CambioDeCorreoPendiente?> Obtener(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT id, correo_nuevo, creado_en, estado FROM identidad.cambios_de_correo_pendientes WHERE usuario_id = @u", ("u", usuarioId));
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await filas.ReadAsync(ct).ConfigureAwait(false)
                    ? CambioDeCorreoPendiente.Restaurar(filas.GetGuid(0), usuarioId, filas.GetString(1), AccesoAlEsquemaDeIdentidad.Instante(filas, 2), Estado(filas.GetString(3)))
                    : null;
            },
            cancelacion);

    public Task Guardar(CambioDeCorreoPendiente cambio, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    """
                    INSERT INTO identidad.cambios_de_correo_pendientes (id, usuario_id, correo_nuevo, creado_en, vence_en, estado) VALUES (@id, @u, @correo, @creado, @vence, @estado)
                    ON CONFLICT (usuario_id) DO UPDATE SET id = EXCLUDED.id, correo_nuevo = EXCLUDED.correo_nuevo, creado_en = EXCLUDED.creado_en, vence_en = EXCLUDED.vence_en, estado = EXCLUDED.estado
                    """,
                    ("id", cambio.Id),
                    ("u", cambio.UsuarioId),
                    ("correo", cambio.CorreoNuevo),
                    ("creado", cambio.Creado),
                    ("vence", cambio.Vence),
                    ("estado", Estado(cambio.Estado)));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    private static string Estado(EstadoDelCambioDeCorreo estado) => estado switch
    {
        EstadoDelCambioDeCorreo.EsperandoVerificacion => "esperando-verificacion",
        EstadoDelCambioDeCorreo.PendienteDeConfirmacion => "pendiente-de-confirmacion",
        EstadoDelCambioDeCorreo.Aplicado => "aplicado",
        EstadoDelCambioDeCorreo.Rechazado => "rechazado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado desconocido."),
    };

    private static EstadoDelCambioDeCorreo Estado(string estado) => estado switch
    {
        "esperando-verificacion" => EstadoDelCambioDeCorreo.EsperandoVerificacion,
        "pendiente-de-confirmacion" => EstadoDelCambioDeCorreo.PendienteDeConfirmacion,
        "aplicado" => EstadoDelCambioDeCorreo.Aplicado,
        "rechazado" => EstadoDelCambioDeCorreo.Rechazado,
        _ => throw new InvalidOperationException("Estado de cambio de correo guardado desconocido."),
    };
}

/// <summary>Las solicitudes sobre datos personales (T33; IDE-12, IDE-26, IDE-63): solo identificador, tipo, estado, fechas y la respuesta o el motivo; con <c>FOR UPDATE</c> dos respuestas simultáneas no se pisan.</summary>
public sealed class PostgresRepositorioDeSolicitudesDeDatos(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeSolicitudesDeDatos
{
    private const string Columnas = "id, usuario_id, tipo, estado, recibida_en, fecha_limite, atendida_en, respuesta, motivo";

    public Task Guardar(SolicitudDeDatos solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    $"""
                    INSERT INTO identidad.solicitudes_de_datos ({Columnas}) VALUES (@id, @u, @tipo, @estado, @recibida, @limite, @atendida, @respuesta, @motivo)
                    ON CONFLICT (id) DO UPDATE SET estado = EXCLUDED.estado, atendida_en = EXCLUDED.atendida_en, respuesta = EXCLUDED.respuesta, motivo = EXCLUDED.motivo
                    """,
                    ("id", solicitud.Id),
                    ("u", solicitud.UsuarioId),
                    ("tipo", Tipo(solicitud.Tipo)),
                    ("estado", Estado(solicitud.Estado)),
                    ("recibida", solicitud.RecibidaEn),
                    ("limite", solicitud.FechaLimite),
                    ("atendida", solicitud.AtendidaEn),
                    ("respuesta", solicitud.Respuesta),
                    ("motivo", solicitud.Motivo));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public async Task<SolicitudDeDatos?> Obtener(Guid solicitudId, bool paraActualizar = false, CancellationToken cancelacion = default) =>
        (await Leer($"SELECT {Columnas} FROM identidad.solicitudes_de_datos WHERE id = @id{(paraActualizar ? " FOR UPDATE" : string.Empty)}", cancelacion, ("id", solicitudId)).ConfigureAwait(false)).FirstOrDefault();

    public Task<IReadOnlyList<SolicitudDeDatos>> DelUsuario(Guid usuarioId, CancellationToken cancelacion = default) =>
        Leer($"SELECT {Columnas} FROM identidad.solicitudes_de_datos WHERE usuario_id = @u ORDER BY recibida_en DESC, id", cancelacion, ("u", usuarioId));

    public Task<IReadOnlyList<SolicitudDeDatos>> Todas(CancellationToken cancelacion = default) =>
        Leer($"SELECT {Columnas} FROM identidad.solicitudes_de_datos ORDER BY recibida_en DESC, id", cancelacion);

    private Task<IReadOnlyList<SolicitudDeDatos>> Leer(string sql, CancellationToken cancelacion, params (string, object?)[] parametros) =>
        acceso.Ejecutar<IReadOnlyList<SolicitudDeDatos>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, sql, parametros);
                var solicitudes = new List<SolicitudDeDatos>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    solicitudes.Add(SolicitudDeDatos.Restaurar(
                        filas.GetGuid(0),
                        filas.GetGuid(1),
                        Tipo(filas.GetString(2)),
                        Estado(filas.GetString(3)),
                        AccesoAlEsquemaDeIdentidad.Instante(filas, 4),
                        DateOnly.FromDateTime(filas.GetDateTime(5)),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 6),
                        AccesoAlEsquemaDeIdentidad.Texto(filas, 7),
                        AccesoAlEsquemaDeIdentidad.Texto(filas, 8)));
                }

                return solicitudes;
            },
            cancelacion);

    private static string Tipo(TipoDeSolicitudDeDatos tipo) => tipo switch
    {
        TipoDeSolicitudDeDatos.Acceso => "acceso",
        TipoDeSolicitudDeDatos.Rectificacion => "rectificacion",
        TipoDeSolicitudDeDatos.Cancelacion => "cancelacion",
        TipoDeSolicitudDeDatos.Oposicion => "oposicion",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo desconocido."),
    };

    private static TipoDeSolicitudDeDatos Tipo(string tipo) => tipo switch
    {
        "acceso" => TipoDeSolicitudDeDatos.Acceso,
        "rectificacion" => TipoDeSolicitudDeDatos.Rectificacion,
        "cancelacion" => TipoDeSolicitudDeDatos.Cancelacion,
        "oposicion" => TipoDeSolicitudDeDatos.Oposicion,
        _ => throw new InvalidOperationException("Tipo de solicitud guardado desconocido."),
    };

    private static string Estado(EstadoDeSolicitudDeDatos estado) => estado switch
    {
        EstadoDeSolicitudDeDatos.Recibida => "recibida",
        EstadoDeSolicitudDeDatos.EnAtencion => "en-atencion",
        EstadoDeSolicitudDeDatos.Atendida => "atendida",
        EstadoDeSolicitudDeDatos.Rechazada => "rechazada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado desconocido."),
    };

    private static EstadoDeSolicitudDeDatos Estado(string estado) => estado switch
    {
        "recibida" => EstadoDeSolicitudDeDatos.Recibida,
        "en-atencion" => EstadoDeSolicitudDeDatos.EnAtencion,
        "atendida" => EstadoDeSolicitudDeDatos.Atendida,
        "rechazada" => EstadoDeSolicitudDeDatos.Rechazada,
        _ => throw new InvalidOperationException("Estado de solicitud guardado desconocido."),
    };
}

/// <summary>Las solicitudes de cancelación de cuenta (T33; IDE-27, IDE-68, IDE-81): como máximo una abierta por usuario (índice único parcial); la revisión periódica toma las abiertas con <c>FOR UPDATE SKIP LOCKED</c>.</summary>
public sealed class PostgresRepositorioDeCancelaciones(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeCancelaciones
{
    private const string Columnas = "id, usuario_id, estado, solicitada_en, reconfirmar_hasta, aviso_visto_en, cerrada_en, ejecucion_en_curso";

    public Task Guardar(SolicitudDeCancelacion solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    $"""
                    INSERT INTO identidad.solicitudes_de_cancelacion ({Columnas}) VALUES (@id, @u, @estado, @solicitada, @reconfirmar, @visto, @cerrada, @curso)
                    ON CONFLICT (id) DO UPDATE SET estado = EXCLUDED.estado, reconfirmar_hasta = EXCLUDED.reconfirmar_hasta, aviso_visto_en = EXCLUDED.aviso_visto_en, cerrada_en = EXCLUDED.cerrada_en,
                        ejecucion_en_curso = EXCLUDED.ejecucion_en_curso
                    """,
                    ("id", solicitud.Id),
                    ("u", solicitud.UsuarioId),
                    ("estado", Estado(solicitud.Estado)),
                    ("solicitada", solicitud.SolicitadaEn),
                    ("reconfirmar", solicitud.ReconfirmarHasta),
                    ("visto", solicitud.AvisoVistoEn),
                    ("cerrada", solicitud.CerradaEn),
                    ("curso", solicitud.EjecucionEnCurso));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public async Task<SolicitudDeCancelacion?> AbiertaDe(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default) =>
        (await Leer(
            $"SELECT {Columnas} FROM identidad.solicitudes_de_cancelacion WHERE usuario_id = @u AND estado IN ('diferida', 'esperando-reconfirmacion'){(paraActualizar ? " FOR UPDATE" : string.Empty)}",
            cancelacion,
            ("u", usuarioId)).ConfigureAwait(false)).FirstOrDefault();

    public async Task<SolicitudDeCancelacion?> UltimaDe(Guid usuarioId, CancellationToken cancelacion = default) =>
        (await Leer($"SELECT {Columnas} FROM identidad.solicitudes_de_cancelacion WHERE usuario_id = @u ORDER BY solicitada_en DESC, id LIMIT 1", cancelacion, ("u", usuarioId)).ConfigureAwait(false)).FirstOrDefault();

    public Task<IReadOnlyList<SolicitudDeCancelacion>> AbiertasParaRevisar(int limite, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        return Leer(
            $"SELECT {Columnas} FROM identidad.solicitudes_de_cancelacion WHERE estado IN ('diferida', 'esperando-reconfirmacion') ORDER BY solicitada_en, id LIMIT @limite FOR UPDATE SKIP LOCKED",
            cancelacion,
            ("limite", limite));
    }

    private Task<IReadOnlyList<SolicitudDeCancelacion>> Leer(string sql, CancellationToken cancelacion, params (string, object?)[] parametros) =>
        acceso.Ejecutar<IReadOnlyList<SolicitudDeCancelacion>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, sql, parametros);
                var solicitudes = new List<SolicitudDeCancelacion>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    solicitudes.Add(SolicitudDeCancelacion.Restaurar(
                        filas.GetGuid(0),
                        filas.GetGuid(1),
                        Estado(filas.GetString(2)),
                        AccesoAlEsquemaDeIdentidad.Instante(filas, 3),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 4),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 5),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 6),
                        filas.GetBoolean(7)));
                }

                return solicitudes;
            },
            cancelacion);

    private static string Estado(EstadoDeCancelacion estado) => estado switch
    {
        EstadoDeCancelacion.Diferida => "diferida",
        EstadoDeCancelacion.EsperandoReconfirmacion => "esperando-reconfirmacion",
        EstadoDeCancelacion.Ejecutada => "ejecutada",
        EstadoDeCancelacion.Descartada => "descartada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado desconocido."),
    };

    private static EstadoDeCancelacion Estado(string estado) => estado switch
    {
        "diferida" => EstadoDeCancelacion.Diferida,
        "esperando-reconfirmacion" => EstadoDeCancelacion.EsperandoReconfirmacion,
        "ejecutada" => EstadoDeCancelacion.Ejecutada,
        "descartada" => EstadoDeCancelacion.Descartada,
        _ => throw new InvalidOperationException("Estado de cancelación guardado desconocido."),
    };
}

/// <summary>
/// Las bajas de titulares pendientes (T33; IDE-64, IDE-75): una fila por identificador opaco, sin clave foránea; agregar es idempotente y entregar o cerrar son actualizaciones que se pueden repetir. Los trabajos de fondo
/// toman cada baja en su propia transacción.
/// </summary>
public sealed class PostgresRepositorioDeBajas(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeBajas
{
    public Task Agregar(BajaDeTitular baja, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(baja);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.bajas_titulares (identificador_opaco, creada_en, entregada_en, cerrada_en) VALUES (@id, @creada, @entregada, @cerrada) ON CONFLICT (identificador_opaco) DO NOTHING",
                    ("id", baja.IdentificadorOpaco),
                    ("creada", baja.Creada),
                    ("entregada", baja.EntregadaEn),
                    ("cerrada", baja.CerradaEn));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyList<BajaDeTitular>> Pendientes(int limite, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        return acceso.Ejecutar<IReadOnlyList<BajaDeTitular>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT identificador_opaco, creada_en, entregada_en, cerrada_en FROM identidad.bajas_titulares WHERE entregada_en IS NULL OR cerrada_en IS NULL ORDER BY creada_en, identificador_opaco LIMIT @limite FOR UPDATE SKIP LOCKED",
                    ("limite", limite));
                var bajas = new List<BajaDeTitular>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    bajas.Add(new BajaDeTitular(filas.GetGuid(0), AccesoAlEsquemaDeIdentidad.Instante(filas, 1), AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 2), AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 3)));
                }

                return bajas;
            },
            cancelacion);
    }

    public Task Guardar(BajaDeTitular baja, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(baja);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.bajas_titulares SET entregada_en = COALESCE(entregada_en, @entregada), cerrada_en = COALESCE(cerrada_en, @cerrada) WHERE identificador_opaco = @id",
                    ("id", baja.IdentificadorOpaco),
                    ("entregada", baja.EntregadaEn),
                    ("cerrada", baja.CerradaEn));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }
}

/// <summary>Las cuentas canceladas cuyos registros aislados aún no se anonimizaron (<c>identidad.por_anonimizar</c>, AUD-19, AUD-31): agregar es idempotente; la depuración (otro rol) las consume.</summary>
public sealed class PostgresPorAnonimizar(AccesoAlEsquemaDeIdentidad acceso) : IPorAnonimizar
{
    public Task Agregar(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.por_anonimizar (identificador_opaco, tipo, registrado_en) VALUES (@id, 'cuenta_cancelada', now()) ON CONFLICT (identificador_opaco) DO NOTHING",
                    ("id", usuarioId));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        return acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT identificador_opaco FROM identidad.por_anonimizar WHERE tipo = 'cuenta_cancelada' AND procesado_en IS NULL ORDER BY registrado_en, identificador_opaco LIMIT @maximo",
                    ("maximo", maximo));
                var ids = new List<Guid>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    ids.Add(filas.GetGuid(0));
                }

                return ids;
            },
            cancelacion);
    }
}

/// <summary>
/// La parte de la matriz de cancelación (IDE-47, IDE-69, IDE-89) que es del propio esquema (T33), dentro de la transacción de la cancelación: elimina el segundo factor y sus códigos de respaldo, los códigos de un solo uso,
/// los desafíos, las renovaciones, los dispositivos, los cambios de correo pendientes y los avisos de la cuenta, y deja en las solicitudes sobre datos solo identificador, tipo, estado y fechas. No toca la cuenta.
/// </summary>
public sealed class PostgresPurgaDeIdentidad(AccesoAlEsquemaDeIdentidad acceso) : IPurgaDeIdentidad
{
    private static readonly string[] Tablas =
    [
        "codigos_de_respaldo", "segundo_factor", "codigos_de_un_solo_uso", "desafios_de_ingreso", "renovaciones", "dispositivos", "cambios_de_correo_pendientes", "avisos_de_cuenta",
    ];

    public Task EliminarLosDatosDe(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                foreach (var tabla in Tablas)
                {
                    await using var borrar = AccesoAlEsquemaDeIdentidad.Comando(c, t, $"DELETE FROM identidad.{tabla} WHERE usuario_id = @u", ("u", usuarioId));
                    await borrar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var solicitudes = AccesoAlEsquemaDeIdentidad.Comando(c, t, "UPDATE identidad.solicitudes_de_datos SET respuesta = NULL, motivo = NULL WHERE usuario_id = @u", ("u", usuarioId));
                await solicitudes.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}

/// <summary>
/// El juego de claves de firma (T33; IDE-35, IDE-36, IDE-61): <b>cargar para actualizar</b> toma el bloqueo de todas las filas (<c>FOR UPDATE</c>), de modo que dos rotaciones o declaraciones simultáneas se serializan;
/// el índice único parcial de «una sola clave activa» respalda la invariante. Guardar actualiza el estado de cada clave (primero las que dejan de ser activas, luego la activa, por el índice). La clave nueva la insertó el
/// generador en la misma transacción como <c>retirada</c> (sin efecto) hasta que este guardado la activa.
/// </summary>
public sealed class PostgresAlmacenDeClavesDeFirma(AccesoAlEsquemaDeIdentidad acceso) : IAlmacenDeClavesDeFirma, IAlmacenDeClavesPrivadas
{
    public Task<JuegoDeClavesDeFirma> Cargar(bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    $"SELECT kid, creada_en, estado, dejo_de_firmar_en, comprometida_en FROM identidad.claves_de_firma WHERE estado <> 'retirada' ORDER BY creada_en, kid{(paraActualizar ? " FOR UPDATE" : string.Empty)}");
                var claves = new List<ClaveDeFirma>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    claves.Add(ClaveDeFirma.Restaurar(
                        filas.GetString(0),
                        AccesoAlEsquemaDeIdentidad.Instante(filas, 1),
                        Estado(filas.GetString(2)),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 3),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 4)));
                }

                return claves.Count == 0 ? throw new InvalidOperationException("No hay claves de firma: falta crear la primera.") : JuegoDeClavesDeFirma.Restaurar(claves);
            },
            cancelacion);

    public Task Guardar(JuegoDeClavesDeFirma juego, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(juego);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                // Las que dejan de ser activas primero: dos claves activas a la vez violarían el índice único parcial.
                foreach (var clave in juego.Claves.OrderBy(k => k.Estado == EstadoDeClaveDeFirma.Activa ? 1 : 0))
                {
                    await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                        c,
                        t,
                        "UPDATE identidad.claves_de_firma SET estado = @estado, dejo_de_firmar_en = @dejo, comprometida_en = @comprometida WHERE kid = @kid",
                        ("kid", clave.Kid),
                        ("estado", Estado(clave.Estado)),
                        ("dejo", clave.DejoDeFirmarEn),
                        ("comprometida", clave.ComprometidaEn));
                    await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }

    public Task<IReadOnlyDictionary<string, string>> ParametrosPublicos(IReadOnlyCollection<string> kids, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(kids);
        return acceso.Ejecutar<IReadOnlyDictionary<string, string>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT kid, jwk_publico FROM identidad.claves_de_firma WHERE kid = ANY(@kids)", ("kids", kids.ToArray()));
                var parametros = new Dictionary<string, string>(StringComparer.Ordinal);
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    parametros[filas.GetString(0)] = filas.GetString(1);
                }

                return parametros;
            },
            cancelacion);
    }

    /// <summary>La clave nueva de una rotación: la fila nace como <c>retirada</c> (el guardado del juego la activa en la misma transacción) con su JWK público y la privada cifrada.</summary>
    public Task Guardar(string kid, byte[] privadaCifrada, string jwkPublico, DateTimeOffset creada, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.claves_de_firma (kid, creada_en, estado, jwk_publico, privada_cifrada) VALUES (@kid, @creada, 'retirada', @jwk, @privada)",
                    ("kid", kid),
                    ("creada", creada),
                    ("jwk", jwkPublico),
                    ("privada", privadaCifrada));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    /// <summary>La privada cifrada de la clave pedida (la activa, normalmente): solo la usa el firmador.</summary>
    public Task<byte[]?> PrivadaCifrada(string kid, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT privada_cifrada FROM identidad.claves_de_firma WHERE kid = @kid", ("kid", kid));
                return await comando.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
            },
            cancelacion);

    /// <summary>El <c>kid</c> de la clave activa.</summary>
    public Task<string?> KidDeLaActiva(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT kid FROM identidad.claves_de_firma WHERE estado = 'activa'");
                return await comando.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            },
            cancelacion);

    private static string Estado(EstadoDeClaveDeFirma estado) => estado switch
    {
        EstadoDeClaveDeFirma.Activa => "activa",
        EstadoDeClaveDeFirma.Anterior => "anterior",
        EstadoDeClaveDeFirma.Comprometida => "comprometida",
        EstadoDeClaveDeFirma.Retirada => "retirada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado desconocido."),
    };

    private static EstadoDeClaveDeFirma Estado(string estado) => estado switch
    {
        "activa" => EstadoDeClaveDeFirma.Activa,
        "anterior" => EstadoDeClaveDeFirma.Anterior,
        "comprometida" => EstadoDeClaveDeFirma.Comprometida,
        "retirada" => EstadoDeClaveDeFirma.Retirada,
        _ => throw new InvalidOperationException("Estado de clave guardado desconocido."),
    };
}

/// <summary>
/// La clave privada de firma vigente (T33, T32): la privada cifrada de la clave activa se descifra con la clave del entorno (propósito <c>clave-de-firma</c>, ligada a su <c>kid</c>) y se conserva en memoria hasta un
/// minuto, de modo que una rotación se nota en poco tiempo y la privada no se descifra en cada firma. Nunca se imprime ni sale del proceso.
/// </summary>
public sealed class PostgresClavePrivadaDeFirma(PostgresAlmacenDeClavesDeFirma almacen, AesGcmCifradorDeSecretos cifrador, TimeProvider? reloj = null) : IClavePrivadaDeFirma
{
    public static readonly TimeSpan VidaDeLaCache = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private readonly SemaphoreSlim _candado = new(1, 1);
    private (ClavePrivadaActiva Clave, DateTimeOffset Hasta)? _cache;

    public async Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default)
    {
        if (_cache is { } vigente && _reloj.GetUtcNow() < vigente.Hasta)
        {
            return vigente.Clave;
        }

        await _candado.WaitAsync(cancelacion).ConfigureAwait(false);
        try
        {
            if (_cache is { } otra && _reloj.GetUtcNow() < otra.Hasta)
            {
                return otra.Clave;
            }

            var kid = await almacen.KidDeLaActiva(cancelacion).ConfigureAwait(false) ?? throw new InvalidOperationException("No hay una clave de firma activa.");
            var cifrada = await almacen.PrivadaCifrada(kid, cancelacion).ConfigureAwait(false) ?? throw new InvalidOperationException("La clave de firma activa no tiene su privada.");
            var pkcs8 = cifrador.Descifrar(AesGcmCifradorDeSecretos.PropositoDeLaClaveDeFirma, kid, cifrada);
            var clave = System.Security.Cryptography.ECDsa.Create();
            try
            {
                clave.ImportPkcs8PrivateKey(pkcs8, out _);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(pkcs8);
            }

            var activa = new ClavePrivadaActiva(kid, clave);
            _cache = (activa, _reloj.GetUtcNow() + VidaDeLaCache);
            return activa;
        }
        finally
        {
            _candado.Release();
        }
    }
}
