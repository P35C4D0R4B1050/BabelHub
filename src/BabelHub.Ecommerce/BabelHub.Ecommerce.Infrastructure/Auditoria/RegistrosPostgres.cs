using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using Npgsql;
using NpgsqlTypes;
using TipoDeActor = BabelHub.Ecommerce.Application.Plataforma.TipoDeActor;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// Acceso común del módulo <c>Auditoria</c> a PostgreSQL: cada operación corre como el rol del esquema (<c>rol_auditoria</c>, con <c>SET LOCAL ROLE</c>) dentro de la unidad de trabajo del caso de uso
/// (<see cref="EnLaUnidad{T}"/>, que se une a la transacción en curso o abre una) o en una conexión y una transacción cortas propias (<see cref="Propia{T}"/>), que es lo que necesita un evento
/// de seguridad que debe sobrevivir a la reversión de la operación o un acumulado que no debe esperar a nadie.
/// </summary>
internal static class EscrituraDeAuditoria
{
    private static readonly EsquemaDeModulo Auditoria = EsquemaDeModulo.Crear("auditoria");

    public static readonly TimeSpan EsperaDeLaBase = TimeSpan.FromSeconds(1);

    public static async Task<T> EnLaUnidad<T>(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion, Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(unidad);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Auditoria,
                    async interno => resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false),
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }

    /// <summary>Una conexión y una transacción propias, con el rol del esquema y las esperas máximas de la base (1 s de bloqueo, 5 s de sentencia); confirma al terminar.</summary>
    public static async Task<T> Propia<T>(NpgsqlDataSource origen, Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(origen);
        await using var conexion = await origen.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        await using var transaccion = await conexion.BeginTransactionAsync(cancelacion).ConfigureAwait(false);
        await using (var preparacion = new NpgsqlCommand("SET LOCAL ROLE \"rol_auditoria\"; SET LOCAL lock_timeout = '1s'; SET LOCAL statement_timeout = '5s'", conexion, transaccion))
        {
            await preparacion.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }

        var resultado = await bloque(conexion, transaccion, cancelacion).ConfigureAwait(false);
        await transaccion.CommitAsync(cancelacion).ConfigureAwait(false);
        return resultado;
    }

    public static NpgsqlCommand Comando(string sql, NpgsqlConnection conexion, NpgsqlTransaction transaccion) => new(sql, conexion, transaccion);

    public static string Texto(ServicioAuditado servicio) => servicio == ServicioAuditado.Ecommerce ? "ecommerce" : "inventario";

    public static string Texto(ServicioDeOrigen servicio) => servicio == ServicioDeOrigen.Ecommerce ? "ecommerce" : "inventario";

    public static string Texto(TipoDeRegistroAuditado tipo) => tipo switch
    {
        TipoDeRegistroAuditado.Auditoria => "auditoria",
        TipoDeRegistroAuditado.Seguridad => "seguridad",
        _ => "ambos",
    };

    public static string Texto(TipoDeActor tipo) => tipo switch
    {
        TipoDeActor.Usuario => "usuario",
        TipoDeActor.Sistema => "sistema",
        TipoDeActor.Integracion => "integracion",
        _ => "anonimo",
    };

    public static string Texto(ResultadoAuditado resultado) => resultado switch
    {
        ResultadoAuditado.Exitoso => "exitoso",
        ResultadoAuditado.Rechazado => "rechazado",
        _ => "fallido",
    };

    public static ServicioAuditado ServicioDe(string texto) => texto == "ecommerce" ? ServicioAuditado.Ecommerce : ServicioAuditado.Inventario;

    public static TipoDeRegistroAuditado TipoDeRegistroDe(string texto) => texto switch
    {
        "auditoria" => TipoDeRegistroAuditado.Auditoria,
        "seguridad" => TipoDeRegistroAuditado.Seguridad,
        _ => TipoDeRegistroAuditado.Ambos,
    };

    public static TipoDeActor ActorDe(string texto) => texto switch
    {
        "usuario" => TipoDeActor.Usuario,
        "sistema" => TipoDeActor.Sistema,
        "integracion" => TipoDeActor.Integracion,
        _ => TipoDeActor.Anonimo,
    };

    public static ResultadoAuditado ResultadoDe(string texto) => texto switch
    {
        "exitoso" => ResultadoAuditado.Exitoso,
        "rechazado" => ResultadoAuditado.Rechazado,
        _ => ResultadoAuditado.Fallido,
    };

    /// <summary>La lista de columnas de <c>registros</c> que lee <see cref="Leer"/>, en su orden.</summary>
    public const string ColumnasDeRegistro =
        "r.id, r.servicio, r.tipo, r.actor_tipo, r.actor_id, r.rol, r.instante_utc, r.accion, r.resultado, r.objeto_tipo, r.objeto_id, r.sucursal_origen, r.sucursal_destino, r.motivo, r.origen, r.ruta, r.detalle::text, r.claves_personales, r.truncado, r.correlacion, r.anonimizado_en";

    public static RegistroAuditado Leer(NpgsqlDataReader lector)
    {
        string? Cadena(int i) => lector.IsDBNull(i) ? null : lector.GetString(i);
        return new RegistroAuditado(
            lector.GetGuid(0),
            ServicioDe(lector.GetString(1)),
            TipoDeRegistroDe(lector.GetString(2)),
            ActorDe(lector.GetString(3)),
            lector.IsDBNull(4) ? null : lector.GetGuid(4),
            Cadena(5),
            lector.GetFieldValue<DateTimeOffset>(6).ToUniversalTime(),
            lector.GetString(7),
            ResultadoDe(lector.GetString(8)))
        {
            ObjetoTipo = Cadena(9),
            ObjetoId = Cadena(10),
            SucursalOrigen = Cadena(11),
            SucursalDestino = Cadena(12),
            Motivo = Cadena(13),
            Origen = Cadena(14),
            Ruta = Cadena(15),
            Detalle = Cadena(16),
            ClavesPersonales = lector.IsDBNull(17) ? [] : lector.GetFieldValue<string[]>(17),
            Truncado = lector.GetBoolean(18),
            Correlacion = Cadena(19),
            AnonimizadoEn = lector.IsDBNull(20) ? null : lector.GetFieldValue<DateTimeOffset>(20).ToUniversalTime(),
        };
    }
}

/// <summary>El <c>INSERT</c> de un registro, que comparten el almacén de la unidad de trabajo, el de eventos de seguridad y el de denegaciones.</summary>
internal static class InsercionDeRegistros
{
    private const string Sql = """
        INSERT INTO auditoria.registros (id, servicio, tipo, actor_tipo, actor_id, rol, instante_utc, accion, objeto_tipo, objeto_id, sucursal_origen, sucursal_destino, motivo, origen, ruta, detalle, claves_personales, resultado, truncado, correlacion, anonimizado_en)
        VALUES (@id, @servicio, @tipo, @actor_tipo, @actor_id, @rol, @instante, @accion, @objeto_tipo, @objeto_id, @sucursal_origen, @sucursal_destino, @motivo, @origen, @ruta, @detalle, @claves, @resultado, @truncado, @correlacion, @anonimizado_en)
        """;

    public static async Task<int> Insertar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, RegistroAuditado r, bool ignorarDuplicado, CancellationToken cancelacion)
    {
        await using var comando = EscrituraDeAuditoria.Comando(Sql + (ignorarDuplicado ? " ON CONFLICT DO NOTHING" : string.Empty), conexion, transaccion);
        void P(string nombre, object? valor, NpgsqlDbType? tipo = null)
        {
            var parametro = tipo is { } t ? new NpgsqlParameter(nombre, t) : new NpgsqlParameter { ParameterName = nombre };
            parametro.Value = valor ?? DBNull.Value;
            comando.Parameters.Add(parametro);
        }

        P("id", r.Id, NpgsqlDbType.Uuid);
        P("servicio", EscrituraDeAuditoria.Texto(r.Servicio), NpgsqlDbType.Text);
        P("tipo", EscrituraDeAuditoria.Texto(r.Tipo), NpgsqlDbType.Text);
        P("actor_tipo", EscrituraDeAuditoria.Texto(r.ActorTipo), NpgsqlDbType.Text);
        P("actor_id", r.ActorId, NpgsqlDbType.Uuid);
        P("rol", r.ActorRol, NpgsqlDbType.Text);
        P("instante", r.InstanteUtc, NpgsqlDbType.TimestampTz);
        P("accion", r.Accion, NpgsqlDbType.Text);
        P("objeto_tipo", r.ObjetoTipo, NpgsqlDbType.Text);
        P("objeto_id", r.ObjetoId, NpgsqlDbType.Text);
        P("sucursal_origen", r.SucursalOrigen, NpgsqlDbType.Text);
        P("sucursal_destino", r.SucursalDestino, NpgsqlDbType.Text);
        P("motivo", r.Motivo, NpgsqlDbType.Text);
        P("origen", r.Origen, NpgsqlDbType.Text);
        P("ruta", r.Ruta, NpgsqlDbType.Text);
        P("detalle", string.IsNullOrEmpty(r.Detalle) ? "{}" : r.Detalle, NpgsqlDbType.Jsonb);
        P("claves", r.ClavesPersonales.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text);
        P("resultado", EscrituraDeAuditoria.Texto(r.Resultado), NpgsqlDbType.Text);
        P("truncado", r.Truncado, NpgsqlDbType.Boolean);
        P("correlacion", r.Correlacion, NpgsqlDbType.Text);
        P("anonimizado_en", r.AnonimizadoEn, NpgsqlDbType.TimestampTz);
        return await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// <see cref="IAlmacenDeRegistros"/> sobre <c>auditoria.registros</c> (T25; AUD-03, AUD-04): solo inserción, <b>dentro de la unidad de trabajo del caso de uso</b>, de modo que si la operación se
/// revierte o la auditoría falla, ninguna de las dos queda (AC-AUD-03).
/// </summary>
public sealed class PostgresAlmacenDeRegistros(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IAlmacenDeRegistros
{
    public async Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var contrato = registro.AModeloDeContrato();
        await EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (conexion, transaccion, ct) => await InsercionDeRegistros.Insertar(conexion, transaccion, contrato, ignorarDuplicado: false, ct).ConfigureAwait(false),
            cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// El tope de concurrencia con cortacircuitos de las escrituras en transacción propia (plan §5): un ataque con muchos orígenes no debe agotar el <i>pool</i> de conexiones. Al agotarse el tope o con el
/// cortacircuitos abierto lanza <see cref="EscrituraSaturadaException"/> sin tocar la base; quien llama conserva la protección y publica una alerta técnica.
/// </summary>
public sealed class LimitadorDeEscrituras(int concurrenciaMaxima = 16, int fallosParaAbrir = 8, TimeProvider? reloj = null)
{
    private static readonly TimeSpan Pausa = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _cupos = new(concurrenciaMaxima, concurrenciaMaxima);
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private int _fallosSeguidos;
    private long _abiertoHastaTicks;

    public async Task<T> Ejecutar<T>(Func<Task<T>> escritura)
    {
        ArgumentNullException.ThrowIfNull(escritura);
        if (_reloj.GetUtcNow().UtcTicks < Interlocked.Read(ref _abiertoHastaTicks))
        {
            throw new EscrituraSaturadaException();
        }

        if (!await _cupos.WaitAsync(0).ConfigureAwait(false))
        {
            throw new EscrituraSaturadaException();
        }

        try
        {
            var resultado = await escritura().ConfigureAwait(false);
            Interlocked.Exchange(ref _fallosSeguidos, 0);
            return resultado;
        }
        catch (Exception)
        {
            if (Interlocked.Increment(ref _fallosSeguidos) >= fallosParaAbrir)
            {
                Interlocked.Exchange(ref _abiertoHastaTicks, (_reloj.GetUtcNow() + Pausa).UtcTicks);
                Interlocked.Exchange(ref _fallosSeguidos, 0);
            }

            throw;
        }
        finally
        {
            _cupos.Release();
        }
    }
}

/// <summary>El tope de concurrencia de escrituras o el cortacircuitos están al límite: la escritura no se intentó.</summary>
public sealed class EscrituraSaturadaException : Exception
{
    public EscrituraSaturadaException()
        : base("Las escrituras de auditoría en transacción propia están al límite de concurrencia.")
    {
    }
}

/// <summary>
/// <see cref="IAlmacenDeEventosDeSeguridad"/> (AUD-12, AC-AUD-03): un evento que <b>no concede nada</b> se escribe en una conexión y una transacción cortas propias, de modo que la transacción
/// del llamante (el bloqueo temporal, el rechazo) ni se aborta ni espera si el <c>INSERT</c> falla, y el evento no depende de que esa operación se confirme.
/// </summary>
public sealed class PostgresAlmacenDeEventosDeSeguridad(NpgsqlDataSource origen, LimitadorDeEscrituras limitador) : IAlmacenDeEventosDeSeguridad
{
    public async Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var contrato = registro.AModeloDeContrato();
        await limitador.Ejecutar(
            () => EscrituraDeAuditoria.Propia(origen, async (c, t, ct) => await InsercionDeRegistros.Insertar(c, t, contrato, ignorarDuplicado: false, ct).ConfigureAwait(false), cancelacion)).ConfigureAwait(false);
    }
}

/// <summary>
/// <see cref="IEscritorDeDenegaciones"/> (AUD-05, D19): el acceso denegado, <b>fuera de la transacción del caso de uso</b> (que ya se deshizo), en su propia conexión y transacción corta, con
/// <c>ON CONFLICT DO NOTHING</c> sobre el índice único parcial <c>(servicio, correlacion, motivo)</c>: un único evento por intento aunque el rechazo pase por varios puntos.
/// </summary>
public sealed class PostgresEscritorDeDenegaciones(NpgsqlDataSource origen, LimitadorDeEscrituras limitador) : IEscritorDeDenegaciones
{
    public async Task Escribir(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var contrato = registro.AModeloDeContrato();
        await limitador.Ejecutar(
            () => EscrituraDeAuditoria.Propia(origen, async (c, t, ct) => await InsercionDeRegistros.Insertar(c, t, contrato, ignorarDuplicado: true, ct).ConfigureAwait(false), cancelacion)).ConfigureAwait(false);
    }
}

/// <summary>
/// La consulta de los registros propios (T25; AUD-07, AUD-09, AUD-17, AUD-30): filtros con parámetros (nunca SQL armado con texto), el texto buscado con <c>%</c>, <c>_</c> y <c>\</c> <b>escapados</b>
/// (<c>ILIKE … ESCAPE</c>, con el índice de <c>pg_trgm</c>), <c>statement_timeout</c>, el orden único de D05 (instante descendente, servicio, sucursal de origen sin sucursal al final, identificador)
/// y un cursor de instantánea: la secuencia máxima al abrir más la posición del último entregado, de modo que un registro posterior no aparece ni desplaza a los demás (AUD-30).
/// </summary>
public sealed class PostgresConsultaLocalDeRegistros(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IConsultaLocalDeRegistros
{
    public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) =>
        ConsultaDePaginas.Consultar(unidad, conexion, consulta.Filtro ?? new FiltroDeAuditoria(), consulta.Cursor, restriccion: null, cancelacion);
}

/// <summary>La paginación común de las consultas de <c>registros</c> (la lista de auditoría y el historial de una sucursal del Inventario).</summary>
internal static class ConsultaDePaginas
{
    private const string Version = "v1";

    public static async Task<PaginaDeRegistros> Consultar(
        IUnidadDeTrabajo unidad,
        IConexionDeLaUnidadDeTrabajo conexion,
        FiltroDeAuditoria filtro,
        string? cursor,
        (string Sql, Action<NpgsqlParameterCollection> Parametros)? restriccion,
        CancellationToken cancelacion)
    {
        var posicion = Posicion.Leer(cursor);
        return await EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (conexion, transaccion, ct) =>
            {
                await using (var espera = EscrituraDeAuditoria.Comando("SET LOCAL statement_timeout = '8s'", conexion, transaccion))
                {
                    await espera.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                var instantanea = posicion?.Instantanea;
                if (instantanea is null)
                {
                    await using var maxima = EscrituraDeAuditoria.Comando("SELECT COALESCE(max(secuencia), 0) FROM auditoria.registros", conexion, transaccion);
                    instantanea = (long)(await maxima.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                }

                var sql = new System.Text.StringBuilder("SELECT " + EscrituraDeAuditoria.ColumnasDeRegistro + " FROM auditoria.registros r WHERE r.secuencia <= @instantanea");
                await using var comando = EscrituraDeAuditoria.Comando(string.Empty, conexion, transaccion);
                comando.Parameters.AddWithValue("instantanea", instantanea.Value);

                if (filtro.Accion is not null)
                {
                    sql.Append(" AND r.accion = @accion");
                    comando.Parameters.AddWithValue("accion", filtro.Accion);
                }

                if (filtro.UsuarioId is { } usuario)
                {
                    sql.Append(" AND r.actor_id = @usuario");
                    comando.Parameters.AddWithValue("usuario", usuario);
                }

                if (filtro.Origen is not null)
                {
                    sql.Append(" AND r.origen = @origen");
                    comando.Parameters.AddWithValue("origen", filtro.Origen);
                }

                if (filtro.Desde is { } desde)
                {
                    sql.Append(" AND r.instante_utc >= @desde");
                    comando.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.TimestampTz) { Value = desde.ToUniversalTime() });
                }

                if (filtro.Hasta is { } hasta)
                {
                    sql.Append(" AND r.instante_utc <= @hasta");
                    comando.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlDbType.TimestampTz) { Value = hasta.ToUniversalTime() });
                }

                if (filtro.Texto is { } texto)
                {
                    sql.Append(" AND r.detalle::text ILIKE '%' || @texto || '%' ESCAPE '\\'");
                    comando.Parameters.AddWithValue("texto", EscaparComodines(texto.Trim()));
                }

                if (restriccion is { } r)
                {
                    sql.Append(" AND ").Append(r.Sql);
                    r.Parametros(comando.Parameters);
                }

                if (posicion?.Ultimo is { } ultimo)
                {
                    sql.Append(
                        """
                         AND (r.instante_utc < @k_instante
                           OR (r.instante_utc = @k_instante AND (r.servicio > @k_servicio
                             OR (r.servicio = @k_servicio AND ((r.sucursal_origen IS NULL) > @k_sin_sucursal
                               OR ((r.sucursal_origen IS NULL) = @k_sin_sucursal AND ((@k_sin_sucursal AND r.id > @k_id)
                                 OR (NOT @k_sin_sucursal AND (r.sucursal_origen > @k_sucursal OR (r.sucursal_origen = @k_sucursal AND r.id > @k_id))))))))))
                        """);
                    comando.Parameters.Add(new NpgsqlParameter("k_instante", NpgsqlDbType.TimestampTz) { Value = new DateTimeOffset(ultimo.Ticks, TimeSpan.Zero) });
                    comando.Parameters.AddWithValue("k_servicio", ultimo.Servicio);
                    comando.Parameters.AddWithValue("k_sin_sucursal", ultimo.Sucursal is null);
                    comando.Parameters.Add(new NpgsqlParameter("k_sucursal", NpgsqlDbType.Text) { Value = (object?)ultimo.Sucursal ?? DBNull.Value });
                    comando.Parameters.AddWithValue("k_id", ultimo.Id);
                }

                sql.Append(" ORDER BY r.instante_utc DESC, r.servicio, (r.sucursal_origen IS NULL), r.sucursal_origen, r.id LIMIT @limite");
                comando.Parameters.AddWithValue("limite", PaginaDeRegistros.TamanoDePagina + 1);
                comando.CommandText = sql.ToString();

                var filas = new List<RegistroAuditado>(PaginaDeRegistros.TamanoDePagina + 1);
                await using (var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await lector.ReadAsync(ct).ConfigureAwait(false))
                    {
                        filas.Add(EscrituraDeAuditoria.Leer(lector));
                    }
                }

                var hayMas = filas.Count > PaginaDeRegistros.TamanoDePagina;
                var pagina = filas.Take(PaginaDeRegistros.TamanoDePagina).ToList();
                var siguiente = hayMas ? Posicion.Escribir(instantanea.Value, pagina[^1]) : null;
                return new PaginaDeRegistros(pagina, siguiente) { CursorDeLaPagina = cursor ?? $"{Version}|{instantanea.Value.ToString(CultureInfo.InvariantCulture)}" };
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>Los comodines de <c>LIKE</c> y la barra de escape se tratan como texto literal.</summary>
    public static string EscaparComodines(string texto) =>
        texto.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record Ultimo(long Ticks, string Servicio, string? Sucursal, Guid Id);

    private sealed record Posicion(long Instantanea, Ultimo? Ultimo)
    {
        /// <summary>El cursor llega firmado por la Aplicación: aun así se lee con forma estricta, y una forma inesperada es un defecto, no una entrada.</summary>
        public static Posicion? Leer(string? cursor)
        {
            if (cursor is null)
            {
                return null;
            }

            var partes = cursor.Split('|');
            if (partes.Length is not (2 or 7) || partes[0] != Version || !long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var instantanea))
            {
                throw new InvalidOperationException("El cursor de la página no tiene la forma del almacén.");
            }

            if (partes.Length == 2)
            {
                return new Posicion(instantanea, null);
            }

            if (!long.TryParse(partes[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParseExact(partes[6], "D", out var id) || (partes[4] != "0" && partes[4] != "1"))
            {
                throw new InvalidOperationException("El cursor de la página no tiene la forma del almacén.");
            }

            return new Posicion(instantanea, new Ultimo(ticks, partes[3], partes[4] == "1" ? null : partes[5], id));
        }

        public static string Escribir(long instantanea, RegistroAuditado ultimo) =>
            string.Join(
                '|',
                Version,
                instantanea.ToString(CultureInfo.InvariantCulture),
                ultimo.InstanteUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
                EscrituraDeAuditoria.Texto(ultimo.Servicio),
                ultimo.SucursalOrigen is null ? "1" : "0",
                ultimo.SucursalOrigen ?? string.Empty,
                ultimo.Id.ToString("D"));
    }
}
