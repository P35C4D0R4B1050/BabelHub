using System.Text.Json;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Notificaciones;

/// <summary>
/// Una operación sobre el esquema <c>notificaciones</c> que PostgreSQL rechazó. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c> ni el <c>Statement</c> de la excepción original, porque
/// el <c>Detail</c> de un error de clave única incluye la fila (AGENTS §5). No guarda la excepción original como interna.
/// </summary>
public sealed class FallaDeLaBaseDeNotificacionesException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación de notificaciones (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>
/// Acceso común a las tablas del esquema <c>notificaciones</c>: cada operación corre <b>en la unidad de trabajo en curso</b> (o en una transacción corta propia, si no hay) bajo el rol <c>rol_notificaciones</c> (<c>EjecutarComo</c>,
/// AD18), de modo que un aviso pedido por otro módulo se guarda en la misma transacción que su operación. Todo error de PostgreSQL sale como <see cref="FallaDeLaBaseDeNotificacionesException"/>, sin la fila.
/// </summary>
internal sealed class AccesoAlEsquemaDeNotificaciones(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    private static readonly EsquemaDeModulo Notificaciones = EsquemaDeModulo.Crear("notificaciones");

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Notificaciones,
                    async interno =>
                    {
                        try
                        {
                            resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false);
                        }
                        catch (PostgresException excepcion)
                        {
                            // Se descarta la excepción original: su Detail puede traer la fila entera.
                            throw new FallaDeLaBaseDeNotificacionesException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
                        }
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }
}

/// <summary>
/// <see cref="IRepositorioDeAvisos"/> sobre <c>notificaciones.avisos</c> (T12): el <c>INSERT</c> va en un <b>punto de guardado</b> con <c>ON CONFLICT DO NOTHING</c> (un error del <c>INSERT</c> no aborta la transacción del módulo que
/// pidió el aviso), el trabajo toma los avisos con <c>FOR UPDATE SKIP LOCKED</c> y los resultados se escriben solo si el aviso <b>sigue sin terminar</b> (una cancelación nunca se revierte). Nunca guarda ni lee un número.
/// </summary>
internal sealed class RepositorioDeAvisosPostgres(AccesoAlEsquemaDeNotificaciones acceso) : IRepositorioDeAvisos
{
    private const string PuntoDeGuardado = "aviso_nuevo";

    private const string Columnas = "id, evento_id, destinatario_ref, tipo_destinatario, plantilla, version_plantilla, campos::text, estado, intentos, proxima_fecha, arrendado_hasta, cupos::text, repeticiones_de_duplicado, motivo_de_cierre, creado_en, fecha_final";

    private const string EsperaDelBloqueo = "5s";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<bool> InsertarSiNoExiste(Aviso aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                // Un error SQL aborta la transacción de PostgreSQL aunque se capture: se revierte solo hasta el punto de guardado y la operación del módulo sigue intacta (NOT-04).
                await tx.SaveAsync(PuntoDeGuardado, ct).ConfigureAwait(false);
                try
                {
                    await using var comando = new NpgsqlCommand(
                        $"INSERT INTO notificaciones.avisos ({SinCasts(Columnas)}) VALUES (@id, @evento, @ref, @tipo, @plantilla, @version, @campos, @estado, @intentos, @proxima, @arrendado, @cupos, @repeticiones, @motivo, @creado, @final) ON CONFLICT (evento_id, destinatario_ref, plantilla) DO NOTHING",
                        cx,
                        tx);
                    Parametros(comando, aviso);
                    var filas = await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await tx.ReleaseAsync(PuntoDeGuardado, ct).ConfigureAwait(false);
                    return filas == 1;
                }
                catch
                {
                    await RevertirAlPunto(tx).ConfigureAwait(false);
                    throw;
                }
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Aviso>> NoFinalesDe(TipoDeDestinatario tipo, string destinatarioRef, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Aviso>>(
            async (cx, tx, ct) =>
            {
                await FijarEspera(cx, tx, ct).ConfigureAwait(false);
                await using var comando = new NpgsqlCommand(
                    $"SELECT {Columnas} FROM notificaciones.avisos WHERE tipo_destinatario = @tipo AND destinatario_ref = @ref AND estado IN ('pendiente', 'enviando') ORDER BY creado_en FOR UPDATE",
                    cx,
                    tx);
                comando.Parameters.AddWithValue("tipo", Texto(tipo));
                comando.Parameters.AddWithValue("ref", destinatarioRef);
                return await Leer(comando, ct).ConfigureAwait(false);
            },
            cancelacion);

    public Task Guardar(Aviso aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return acceso.Ejecutar(async (cx, tx, ct) => await Actualizar(cx, tx, aviso, soloSiNoEsFinal: false, ct).ConfigureAwait(false), cancelacion);
    }

    public Task<bool> GuardarSiNoEsFinal(Aviso aviso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        return acceso.Ejecutar((cx, tx, ct) => Actualizar(cx, tx, aviso, soloSiNoEsFinal: true, ct), cancelacion);
    }

    public Task<Aviso?> PorId(Guid id, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                if (paraActualizar)
                {
                    await FijarEspera(cx, tx, ct).ConfigureAwait(false);
                }

                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM notificaciones.avisos WHERE id = @id{(paraActualizar ? " FOR UPDATE" : string.Empty)}", cx, tx);
                comando.Parameters.AddWithValue("id", id);
                return (await Leer(comando, ct).ConfigureAwait(false)).FirstOrDefault();
            },
            cancelacion);

    public Task<IReadOnlyList<Aviso>> TomarParaEnviar(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Aviso>>(
            async (cx, tx, ct) =>
            {
                // Pendientes cuya fecha llegó, en envío con el arrendamiento vencido y los que vencieron por edad; dos trabajos nunca toman el mismo (SKIP LOCKED).
                await using var comando = new NpgsqlCommand(
                    $"""
                    SELECT {Columnas} FROM notificaciones.avisos
                    WHERE estado IN ('pendiente', 'enviando')
                      AND ((estado = 'pendiente' AND proxima_fecha <= @ahora)
                        OR (estado = 'enviando' AND arrendado_hasta <= @ahora)
                        OR creado_en <= @limite_de_edad)
                    ORDER BY creado_en
                    LIMIT @maximo
                    FOR UPDATE SKIP LOCKED
                    """,
                    cx,
                    tx);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                comando.Parameters.AddWithValue("limite_de_edad", (ahora - PoliticaDeReintento.EdadMaxima).ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                return await Leer(comando, ct).ConfigureAwait(false);
            },
            cancelacion);

    public Task<PaginaDeAvisosAlmacenados> Listar(EstadoDelAviso? estado, int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                var filtro = estado is null ? string.Empty : "WHERE estado = @estado";
                await using var conteo = new NpgsqlCommand($"SELECT count(*) FROM notificaciones.avisos {filtro}", cx, tx);
                await using var lista = new NpgsqlCommand($"SELECT {Columnas} FROM notificaciones.avisos {filtro} ORDER BY creado_en DESC, id LIMIT @tamano OFFSET @desde", cx, tx);
                if (estado is { } valor)
                {
                    conteo.Parameters.AddWithValue("estado", Texto(valor));
                    lista.Parameters.AddWithValue("estado", Texto(valor));
                }

                lista.Parameters.AddWithValue("tamano", tamano);
                lista.Parameters.AddWithValue("desde", (long)(pagina - 1) * tamano);
                var total = Convert.ToInt32(await conteo.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                return new PaginaDeAvisosAlmacenados(await Leer(lista, ct).ConfigureAwait(false), total);
            },
            cancelacion);

    public Task<int> PurgarFinalesAnterioresA(DateTimeOffset limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM notificaciones.avisos WHERE estado NOT IN ('pendiente', 'enviando') AND fecha_final < @limite", cx, tx);
                comando.Parameters.AddWithValue("limite", limite.ToUniversalTime());
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);

    private static async Task<bool> Actualizar(NpgsqlConnection cx, NpgsqlTransaction tx, Aviso aviso, bool soloSiNoEsFinal, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            $"""
            UPDATE notificaciones.avisos SET
                campos = @campos, estado = @estado, intentos = @intentos, proxima_fecha = @proxima, arrendado_hasta = @arrendado, cupos = @cupos,
                repeticiones_de_duplicado = @repeticiones, motivo_de_cierre = @motivo, fecha_final = @final
            WHERE id = @id{(soloSiNoEsFinal ? " AND estado IN ('pendiente', 'enviando')" : string.Empty)}
            """,
            cx,
            tx);
        Parametros(comando, aviso);
        return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private static void Parametros(NpgsqlCommand comando, Aviso aviso)
    {
        var p = comando.Parameters;
        p.AddWithValue("id", aviso.Id);
        p.AddWithValue("evento", aviso.EventoId);
        p.AddWithValue("ref", aviso.DestinatarioRef);
        p.AddWithValue("tipo", Texto(aviso.Tipo));
        p.AddWithValue("plantilla", aviso.Plantilla);
        p.AddWithValue("version", aviso.VersionDeLaPlantilla);
        p.Add(new NpgsqlParameter("campos", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(aviso.Campos, Json) });
        p.AddWithValue("estado", Texto(aviso.Estado));
        p.AddWithValue("intentos", aviso.Intentos);
        p.AddWithValue("proxima", aviso.ProximaFecha.ToUniversalTime());
        p.Add(new NpgsqlParameter("arrendado", NpgsqlDbType.TimestampTz) { Value = aviso.ArrendadoHasta is { } hasta ? hasta.ToUniversalTime() : DBNull.Value });
        p.Add(new NpgsqlParameter("cupos", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(aviso.Cupos, Json) });
        p.AddWithValue("repeticiones", aviso.RepeticionesDeDuplicado);
        p.Add(new NpgsqlParameter("motivo", NpgsqlDbType.Varchar) { Value = (object?)aviso.MotivoDeCierre ?? DBNull.Value });
        p.AddWithValue("creado", aviso.Creado.ToUniversalTime());
        p.Add(new NpgsqlParameter("final", NpgsqlDbType.TimestampTz) { Value = aviso.FechaFinal is { } fin ? fin.ToUniversalTime() : DBNull.Value });
    }

    private static async Task<IReadOnlyList<Aviso>> Leer(NpgsqlCommand comando, CancellationToken ct)
    {
        var avisos = new List<Aviso>();
        await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await filas.ReadAsync(ct).ConfigureAwait(false))
        {
            avisos.Add(Aviso.Restaurar(
                filas.GetGuid(0),
                filas.GetString(1),
                filas.GetString(2),
                ATipo(filas.GetString(3)),
                filas.GetString(4),
                filas.GetInt32(5),
                JsonSerializer.Deserialize<Dictionary<string, string>>(filas.GetString(6), Json) ?? [],
                AEstado(filas.GetString(7)),
                filas.GetInt32(8),
                Instante(filas, 9)!.Value,
                Instante(filas, 10),
                JsonSerializer.Deserialize<List<CupoDelTope>>(filas.GetString(11), Json) ?? [],
                filas.GetInt32(12),
                filas.IsDBNull(13) ? null : filas.GetString(13),
                Instante(filas, 14)!.Value,
                Instante(filas, 15)));
        }

        return avisos;
    }

    private static DateTimeOffset? Instante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : new DateTimeOffset(filas.GetDateTime(posicion), TimeSpan.Zero);

    private static async Task FijarEspera(NpgsqlConnection cx, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand($"SET LOCAL lock_timeout = '{EsperaDelBloqueo}'", cx, tx);
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task RevertirAlPunto(NpgsqlTransaction tx)
    {
        try
        {
            await tx.RollbackAsync(PuntoDeGuardado, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is NpgsqlException or InvalidOperationException)
        {
            // La conexión ya se perdió: PostgreSQL revierte la transacción al cerrarse; no se enmascara el error original.
        }
    }

    /// <summary>La lista de columnas para un <c>INSERT</c>: la misma que la lectura, sin los <c>::text</c> de los campos JSON.</summary>
    private static string SinCasts(string columnas) => columnas.Replace("::text", string.Empty, StringComparison.Ordinal);

    private static string Texto(TipoDeDestinatario tipo) => tipo == TipoDeDestinatario.Cliente ? "cliente" : "sucursal";

    private static TipoDeDestinatario ATipo(string texto) => texto == "cliente" ? TipoDeDestinatario.Cliente : TipoDeDestinatario.Sucursal;

    internal static string Texto(EstadoDelAviso estado) => estado switch
    {
        EstadoDelAviso.Pendiente => "pendiente",
        EstadoDelAviso.Enviando => "enviando",
        EstadoDelAviso.Enviado => "enviado",
        EstadoDelAviso.Fallido => "fallido",
        EstadoDelAviso.Cancelado => "cancelado",
        EstadoDelAviso.OmitidoSinConsentimiento => "omitido_sin_consentimiento",
        EstadoDelAviso.OmitidoPorTope => "omitido_por_tope",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    private static EstadoDelAviso AEstado(string texto) => texto switch
    {
        "pendiente" => EstadoDelAviso.Pendiente,
        "enviando" => EstadoDelAviso.Enviando,
        "enviado" => EstadoDelAviso.Enviado,
        "fallido" => EstadoDelAviso.Fallido,
        "cancelado" => EstadoDelAviso.Cancelado,
        "omitido_sin_consentimiento" => EstadoDelAviso.OmitidoSinConsentimiento,
        "omitido_por_tope" => EstadoDelAviso.OmitidoPorTope,
        _ => throw new InvalidOperationException("La base trae un estado de aviso desconocido."),
    };
}

/// <summary>
/// <see cref="IRepositorioDeNumerosDesactivados"/> sobre <c>notificaciones.numeros_desactivados</c> (T12; NOT-13): solo la huella con la versión de su clave. La consulta mira <b>todas las versiones vigentes</b> a la vez, así
/// una rotación de la clave no reactiva un número.
/// </summary>
internal sealed class RepositorioDeNumerosDesactivadosPostgres(AccesoAlEsquemaDeNotificaciones acceso) : IRepositorioDeNumerosDesactivados
{
    public Task<bool> Agregar(NumeroDesactivado numero, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(numero);
        return acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO notificaciones.numeros_desactivados (version_clave, huella_numero, desactivado_en, motivo) VALUES (@version, @huella, @en, @motivo) ON CONFLICT (version_clave, huella_numero) DO NOTHING",
                    cx,
                    tx);
                comando.Parameters.AddWithValue("version", numero.Huella.VersionDeLaClave);
                comando.Parameters.AddWithValue("huella", numero.Huella.Valor);
                comando.Parameters.AddWithValue("en", numero.DesactivadoEn.ToUniversalTime());
                comando.Parameters.AddWithValue("motivo", numero.Motivo);
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task<bool> ExisteAlguna(IReadOnlyList<HuellaDeNumero> huellas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellas);
        if (huellas.Count == 0)
        {
            return Task.FromResult(false);
        }

        return acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM notificaciones.numeros_desactivados n JOIN unnest(@versiones, @huellas) AS v(version, huella) ON n.version_clave = v.version AND n.huella_numero = v.huella)",
                    cx,
                    tx);
                Parejas(comando, huellas);
                return (bool)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            },
            cancelacion);
    }

    public Task<int> Eliminar(IReadOnlyList<HuellaDeNumero> huellas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellas);
        if (huellas.Count == 0)
        {
            return Task.FromResult(0);
        }

        return acceso.Ejecutar(
            async (cx, tx, ct) =>
            {
                await using var comando = new NpgsqlCommand(
                    "DELETE FROM notificaciones.numeros_desactivados n USING unnest(@versiones, @huellas) AS v(version, huella) WHERE n.version_clave = v.version AND n.huella_numero = v.huella",
                    cx,
                    tx);
                Parejas(comando, huellas);
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
    }

    private static void Parejas(NpgsqlCommand comando, IReadOnlyList<HuellaDeNumero> huellas)
    {
        comando.Parameters.Add(new NpgsqlParameter("versiones", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = huellas.Select(h => h.VersionDeLaClave).ToArray() });
        comando.Parameters.Add(new NpgsqlParameter("huellas", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = huellas.Select(h => h.Valor).ToArray() });
    }
}
