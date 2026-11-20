using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// <see cref="IAcumuladorDeLimites"/> sobre <c>auditoria.acumulado_de_limites</c> (T25; AUD-28, AUD-41, D11): <b>cada rechazo se persiste sin diferirlo</b>, en una conexión y una transacción propias y
/// cortas, con espera máxima de 1 segundo, sin pérdida tras un reinicio. Una fila existente suma con <c>UPDATE</c> (sin bloqueo de ventana); una fila nueva toma el bloqueo asesor de la
/// ventana, respeta el tope de 1000 filas y agrupa el resto en la fila «otros» (la regla de <see cref="AcumuladoDeLimite"/>). Una ventana ya cerrada no acumula: el rechazo se informa como llegado
/// tarde. Si la escritura falla, quien llama publica una alerta técnica y el rechazo sigue protegiendo.
/// </summary>
public sealed class PostgresAcumuladorDeLimites(NpgsqlDataSource origen, LimitadorDeEscrituras limitador) : IAcumuladorDeLimites
{
    public async Task<ResultadoDeRegistroDeRechazo> Registrar(string huella, string limite, DateTimeOffset instante, string? direccionCompleta, CancellationToken cancelacion = default)
    {
        // El dominio valida los formatos y la dirección (una IP canónica o nada) antes de tocar la base.
        var validacion = new AcumuladoDeLimite(VentanaDeLimite.Que(instante));
        validacion.Registrar(huella, limite, instante, direccionCompleta);
        var direccion = validacion.Filas[0].DireccionCompleta;
        var inicio = VentanaDeLimite.Que(instante).Inicio;

        return await limitador.Ejecutar(
            () => EscrituraDeAuditoria.Propia(
                origen,
                async (c, t, ct) =>
                {
                    if (await Cerrada(c, t, inicio, ct).ConfigureAwait(false))
                    {
                        return ResultadoDeRegistroDeRechazo.VentanaCerrada;
                    }

                    if (await Sumar(c, t, huella, limite, inicio, direccion, ct).ConfigureAwait(false))
                    {
                        return ResultadoDeRegistroDeRechazo.Acumulado;
                    }

                    // Fila nueva: el bloqueo de la ventana serializa el tope de filas.
                    await using (var bloqueo = EscrituraDeAuditoria.Comando("SELECT pg_advisory_xact_lock(hashtextextended('auditoria.acumulado|' || @inicio, 0))", c, t))
                    {
                        bloqueo.Parameters.AddWithValue("inicio", inicio.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        await bloqueo.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    // Con el bloqueo tomado se vuelve a mirar: un cierre que confirmó mientras se esperaba no debe dejar una fila abierta con dirección en una ventana cerrada.
                    if (await Cerrada(c, t, inicio, ct).ConfigureAwait(false))
                    {
                        return ResultadoDeRegistroDeRechazo.VentanaCerrada;
                    }

                    if (await Sumar(c, t, huella, limite, inicio, direccion, ct).ConfigureAwait(false))
                    {
                        return ResultadoDeRegistroDeRechazo.Acumulado;
                    }

                    long filas;
                    await using (var cuenta = EscrituraDeAuditoria.Comando("SELECT count(*) FROM auditoria.acumulado_de_limites WHERE ventana_inicio = @inicio AND huella <> 'otros'", c, t))
                    {
                        cuenta.Parameters.Add(new NpgsqlParameter("inicio", NpgsqlDbType.TimestampTz) { Value = inicio });
                        filas = (long)(await cuenta.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                    }

                    if (filas < AcumuladoDeLimite.TopeDeFilas - 1)
                    {
                        await Insertar(c, t, huella, limite, inicio, direccion, ct).ConfigureAwait(false);
                        return ResultadoDeRegistroDeRechazo.Acumulado;
                    }

                    // La ventana está llena: el origen nuevo se suma a la fila «otros», sin dirección.
                    await Insertar(c, t, AcumuladoDeLimite.HuellaDeOtros, AcumuladoDeLimite.LimiteDeOtros, inicio, null, ct).ConfigureAwait(false);
                    return ResultadoDeRegistroDeRechazo.AgrupadoEnOtros;
                },
                cancelacion)).ConfigureAwait(false);
    }

    private static async Task<bool> Cerrada(NpgsqlConnection c, NpgsqlTransaction t, DateTimeOffset inicio, CancellationToken ct)
    {
        await using var comando = EscrituraDeAuditoria.Comando("SELECT EXISTS (SELECT 1 FROM auditoria.acumulado_de_limites WHERE ventana_inicio = @inicio AND cerrada_en IS NOT NULL)", c, t);
        comando.Parameters.Add(new NpgsqlParameter("inicio", NpgsqlDbType.TimestampTz) { Value = inicio });
        return (bool)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static async Task<bool> Sumar(NpgsqlConnection c, NpgsqlTransaction t, string huella, string limite, DateTimeOffset inicio, string? direccion, CancellationToken ct)
    {
        await using var comando = EscrituraDeAuditoria.Comando(
            """
            UPDATE auditoria.acumulado_de_limites
            SET rechazos = LEAST(rechazos + 1, 2147483646), direccion_completa = COALESCE(direccion_completa, @direccion)
            WHERE huella = @huella AND limite = @limite AND ventana_inicio = @inicio AND cerrada_en IS NULL
            """,
            c,
            t);
        Parametros(comando, huella, limite, inicio, direccion);
        return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static async Task Insertar(NpgsqlConnection c, NpgsqlTransaction t, string huella, string limite, DateTimeOffset inicio, string? direccion, CancellationToken ct)
    {
        await using var comando = EscrituraDeAuditoria.Comando(
            """
            INSERT INTO auditoria.acumulado_de_limites (huella, limite, ventana_inicio, rechazos, direccion_completa)
            VALUES (@huella, @limite, @inicio, 1, @direccion)
            ON CONFLICT (huella, limite, ventana_inicio) DO UPDATE SET rechazos = LEAST(auditoria.acumulado_de_limites.rechazos + 1, 2147483646) WHERE auditoria.acumulado_de_limites.cerrada_en IS NULL
            """,
            c,
            t);
        Parametros(comando, huella, limite, inicio, direccion);
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Parametros(NpgsqlCommand comando, string huella, string limite, DateTimeOffset inicio, string? direccion)
    {
        comando.Parameters.AddWithValue("huella", huella);
        comando.Parameters.AddWithValue("limite", limite);
        comando.Parameters.Add(new NpgsqlParameter("inicio", NpgsqlDbType.TimestampTz) { Value = inicio });
        comando.Parameters.Add(new NpgsqlParameter("direccion", NpgsqlDbType.Text) { Value = (object?)direccion ?? DBNull.Value });
    }
}

/// <summary>
/// <see cref="IConteoLocalDeEventos"/> (T10, T25): cuántos registros de una acción hay en un rango (con el índice parcial por acción e instante) y cuántos rechazos de un límite hay en las ventanas
/// de 5 minutos alineadas que <b>se solapan</b> con el rango. La cuenta por solapamiento puede pasarse hasta una ventana de la ventana deslizante exacta: se prefiere pasarse a quedarse corto,
/// porque el umbral protege (SEG-09). Solo devuelve un número.
/// </summary>
public sealed class PostgresConteoLocalDeEventos(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IConteoLocalDeEventos
{
    public Task<int> ContarRegistros(AccionAuditable accion, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando("SELECT count(*) FROM auditoria.registros WHERE accion = @accion AND instante_utc >= @desde AND instante_utc < @hasta", c, t);
                comando.Parameters.AddWithValue("accion", accion.Identificador);
                comando.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.TimestampTz) { Value = desde.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlDbType.TimestampTz) { Value = hasta.ToUniversalTime() });
                return (int)Math.Min((long)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!, int.MaxValue);
            },
            cancelacion);

    public Task<int> ContarRechazosDeLimite(string? limite, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    """
                    SELECT COALESCE(sum(rechazos), 0) FROM auditoria.acumulado_de_limites
                    WHERE ventana_inicio < @hasta AND ventana_inicio + interval '5 minutes' > @desde AND (@limite::text IS NULL OR limite = @limite)
                    """,
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.TimestampTz) { Value = desde.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlDbType.TimestampTz) { Value = hasta.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("limite", NpgsqlDbType.Text) { Value = (object?)limite ?? DBNull.Value });
                return (int)Math.Min(Convert.ToInt64(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture), int.MaxValue);
            },
            cancelacion);
}

/// <summary><see cref="IAcumuladoParaCerrar"/> sobre <c>auditoria.acumulado_de_limites</c> (T25, T27): el cierre de una ventana y su purga.</summary>
public sealed class PostgresAcumuladoParaCerrar(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IAcumuladoParaCerrar
{
    public Task<IReadOnlyList<DateTimeOffset>> VentanasCerrables(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<IReadOnlyList<DateTimeOffset>>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    """
                    SELECT DISTINCT ventana_inicio FROM auditoria.acumulado_de_limites
                    WHERE cerrada_en IS NULL AND ventana_inicio + interval '5 minutes 10 seconds' <= @ahora
                    ORDER BY ventana_inicio LIMIT @maximo
                    """,
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                comando.Parameters.AddWithValue("maximo", maximo);
                var inicios = new List<DateTimeOffset>();
                await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    inicios.Add(lector.GetFieldValue<DateTimeOffset>(0).ToUniversalTime());
                }

                return inicios;
            },
            cancelacion);

    public Task<AcumuladoDeLimite?> Cargar(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<AcumuladoDeLimite?>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using (var bloqueo = EscrituraDeAuditoria.Comando("SELECT pg_advisory_xact_lock(hashtextextended('auditoria.acumulado|' || @inicio, 0))", c, t))
                {
                    bloqueo.Parameters.AddWithValue("inicio", inicioDeLaVentana.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    await bloqueo.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var comando = EscrituraDeAuditoria.Comando(
                    "SELECT huella, limite, rechazos, direccion_completa, cerrada_en IS NOT NULL FROM auditoria.acumulado_de_limites WHERE ventana_inicio = @inicio FOR UPDATE",
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("inicio", NpgsqlDbType.TimestampTz) { Value = inicioDeLaVentana });
                var filas = new List<FilaDeAcumulado>();
                var cerrada = false;
                await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    filas.Add(new FilaDeAcumulado(lector.GetString(0), lector.GetString(1), lector.GetInt32(2), lector.IsDBNull(3) ? null : lector.GetString(3)));
                    cerrada |= lector.GetBoolean(4);
                }

                return filas.Count == 0 ? null : AcumuladoDeLimite.Restaurar(VentanaDeLimite.Que(inicioDeLaVentana), filas, cerrada);
            },
            cancelacion);

    public Task GuardarCierre(DateTimeOffset inicioDeLaVentana, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Ejecutar(
            "UPDATE auditoria.acumulado_de_limites SET cerrada_en = @ahora, direccion_completa = NULL WHERE ventana_inicio = @inicio AND cerrada_en IS NULL",
            inicioDeLaVentana,
            ahora,
            cancelacion);

    public async Task<int> RegistrarIntentoFallido(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default) =>
        await EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    "UPDATE auditoria.acumulado_de_limites SET intentos_de_cierre = intentos_de_cierre + 1 WHERE ventana_inicio = @inicio AND cerrada_en IS NULL RETURNING intentos_de_cierre",
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("inicio", NpgsqlDbType.TimestampTz) { Value = inicioDeLaVentana });
                var maximo = 0;
                await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    maximo = Math.Max(maximo, lector.GetInt32(0));
                }

                return maximo;
            },
            cancelacion).ConfigureAwait(false);

    public Task BorrarDirecciones(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default) =>
        Ejecutar("UPDATE auditoria.acumulado_de_limites SET direccion_completa = NULL WHERE ventana_inicio = @inicio AND cerrada_en IS NULL", inicioDeLaVentana, null, cancelacion);

    public Task<int> Purgar(DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    "DELETE FROM auditoria.acumulado_de_limites WHERE cerrada_en IS NOT NULL AND ventana_inicio + interval '5 minutes' + interval '24 hours' <= @ahora",
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);

    private Task Ejecutar(string sql, DateTimeOffset inicio, DateTimeOffset? ahora, CancellationToken cancelacion) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(sql, c, t);
                comando.Parameters.Add(new NpgsqlParameter("inicio", NpgsqlDbType.TimestampTz) { Value = inicio });
                if (ahora is { } valor)
                {
                    comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = valor.ToUniversalTime() });
                }

                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}

/// <summary><see cref="IAlmacenDePorAnonimizar"/>: solo inserción de identificadores opacos con validación de tipo y formato en la base (T24); repetir uno no hace nada.</summary>
public sealed class PostgresAlmacenDePorAnonimizar(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IAlmacenDePorAnonimizar
{
    public Task<int> Agregar(TipoDePorAnonimizar tipo, IReadOnlyCollection<Guid> identificadores, DateTimeOffset registradoEn, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(identificadores);
        return EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    """
                    INSERT INTO auditoria.por_anonimizar (identificador_opaco, tipo, registrado_en)
                    SELECT unnest(@ids), @tipo, @registrado
                    ON CONFLICT (identificador_opaco) DO NOTHING
                    """,
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = identificadores.ToArray() });
                comando.Parameters.AddWithValue("tipo", tipo == TipoDePorAnonimizar.CuentaCancelada ? "cuenta_cancelada" : "baja_de_titular");
                comando.Parameters.Add(new NpgsqlParameter("registrado", NpgsqlDbType.TimestampTz) { Value = registradoEn.ToUniversalTime() });
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
    }
}

/// <summary>
/// <see cref="IAnonimizadorDeRegistros"/>: ejecuta <c>auditoria.anonimizar_registros()</c> con <b>otra fuente de datos</b>, la del rol de depuración (la aplicación no es miembro de ese rol, T23), en su
/// propia conexión y transacción, <b>un lote por llamada</b> (cada lote confirma) hasta que devuelva menos del tamaño del lote. La función no recibe parámetros: la aplicación no puede pedir anonimizar a nadie en particular.
/// </summary>
public sealed class PostgresAnonimizadorDeRegistros(NpgsqlDataSource depuracion) : IAnonimizadorDeRegistros
{
    /// <summary>El tamaño del lote de la función: mientras devuelva esto hay más; cada llamada confirma su lote (una corrida grande no depende de una sola transacción larga).</summary>
    public const int TamanoDelLote = 5000;

    /// <summary>Un tope de lotes por corrida: la siguiente continúa (un respaldo restaurado con muchos millones de filas no ocupa la base para siempre).</summary>
    public const int MaximoDeLotesPorCorrida = 2000;

    public static readonly TimeSpan EsperaPorLote = TimeSpan.FromMinutes(2);

    public async Task<int> Anonimizar(CancellationToken cancelacion = default)
    {
        var total = 0;
        for (var lotes = 0; lotes < MaximoDeLotesPorCorrida; lotes++)
        {
            await using var conexion = await depuracion.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
            await using var comando = new NpgsqlCommand("SELECT auditoria.anonimizar_registros()", conexion) { CommandTimeout = (int)EsperaPorLote.TotalSeconds };
            var lote = (int)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
            total += lote;
            if (lote < TamanoDelLote)
            {
                break;
            }
        }

        return total;
    }
}

/// <summary><see cref="IEstadoDeLaDepuracion"/> sobre la fila única de <c>auditoria.estado_de_depuracion</c>: persistente, sobrevive a un reinicio.</summary>
public sealed class PostgresEstadoDeLaDepuracion(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IEstadoDeLaDepuracion
{
    public Task<EstadoDeLaDepuracion> Obtener(CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    "SELECT ultima_exitosa, primer_fallo_sin_exito, intentos_fallidos, proximo_intento, anonimizados_sin_registrar FROM auditoria.estado_de_depuracion WHERE id = 1",
                    c,
                    t);
                await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    return EstadoDeLaDepuracion.Inicial;
                }

                DateTimeOffset? Instante(int i) => lector.IsDBNull(i) ? null : lector.GetFieldValue<DateTimeOffset>(i).ToUniversalTime();
                return new EstadoDeLaDepuracion(Instante(0), Instante(1), lector.GetInt32(2), Instante(3), lector.GetInt32(4));
            },
            cancelacion);

    public Task Guardar(EstadoDeLaDepuracion estado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(estado);
        return EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    """
                    INSERT INTO auditoria.estado_de_depuracion (id, ultima_exitosa, primer_fallo_sin_exito, intentos_fallidos, proximo_intento, anonimizados_sin_registrar)
                    VALUES (1, @exitosa, @primer, @intentos, @proximo, @pendientes)
                    ON CONFLICT (id) DO UPDATE SET ultima_exitosa = EXCLUDED.ultima_exitosa, primer_fallo_sin_exito = EXCLUDED.primer_fallo_sin_exito,
                        intentos_fallidos = EXCLUDED.intentos_fallidos, proximo_intento = EXCLUDED.proximo_intento, anonimizados_sin_registrar = EXCLUDED.anonimizados_sin_registrar
                    """,
                    c,
                    t);
                void P(string nombre, DateTimeOffset? valor) => comando.Parameters.Add(new NpgsqlParameter(nombre, NpgsqlDbType.TimestampTz) { Value = valor is { } v ? v.ToUniversalTime() : DBNull.Value });
                P("exitosa", estado.UltimaExitosa);
                P("primer", estado.PrimerFalloSinExito);
                P("proximo", estado.ProximoIntento);
                comando.Parameters.AddWithValue("intentos", estado.IntentosFallidos);
                comando.Parameters.AddWithValue("pendientes", estado.AnonimizadosSinRegistrar);
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
    }
}

/// <summary>
/// <see cref="IBloqueoDeLaDepuracion"/> con <c>pg_try_advisory_lock</c> de sesión sobre una conexión propia que se mantiene abierta mientras dura la corrida: dos réplicas no depuran a la vez y, si el
/// proceso muere, la base libera el bloqueo al cerrarse la sesión.
/// </summary>
public sealed class PostgresBloqueoDeLaDepuracion(NpgsqlDataSource origen, string nombre = "auditoria.depuracion") : IBloqueoDeLaDepuracion
{
    public async Task<IAsyncDisposable?> Intentar(CancellationToken cancelacion = default)
    {
        var conexion = await origen.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        try
        {
            await using var comando = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@nombre, 0))", conexion);
            comando.Parameters.AddWithValue("nombre", nombre);
            if ((bool)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!)
            {
                return new Tomado(conexion, nombre);
            }
        }
        catch
        {
            // Cancelada entre que la base tomó el bloqueo y llegó la respuesta: no se sabe si quedó tomado, y el grupo no reinicia la sesión; se cierra la conexión física.
            NpgsqlConnection.ClearPool(conexion);
            await conexion.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await conexion.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private sealed class Tomado(NpgsqlConnection conexion, string nombre) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var comando = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@nombre, 0))", conexion);
                comando.Parameters.AddWithValue("nombre", nombre);
                await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (NpgsqlException)
            {
                // Si la conexión se perdió, la base ya liberó el bloqueo al cerrarse la sesión. Si la conexión sigue viva pero no se pudo desbloquear (por ejemplo, un `statement_timeout`), el bloqueo de sesión
                // sobreviviría en el grupo, que no reinicia la sesión: se cierra la conexión física en lugar de devolverla.
                NpgsqlConnection.ClearPool(conexion);
            }
            finally
            {
                await conexion.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
