using System.Globalization;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// <see cref="IAlmacenDeSesionesWeb"/> sobre <c>web.sesiones_web</c> (T10): cada operación corre bajo <c>rol_web</c> en la unidad de trabajo en curso. <see cref="CargarParaRenovar"/>
/// toma la fila con <c>SELECT … FOR UPDATE</c> y un <c>lock_timeout</c> de 5 segundos (plan §5), y <see cref="Guardar"/> nunca reabre una fila cerrada (WEB-28). La fila no
/// guarda el valor de la cookie sino su SHA-256, ni el rol ni la sucursal.
/// </summary>
public sealed class AlmacenDeSesionesWebPostgres(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexionDeLaUnidad, TimeSpan? plazoDeBloqueo = null) : IAlmacenDeSesionesWeb
{
    /// <summary>El plazo que una solicitud espera la fila retenida por otra antes de responder servicio no disponible (plan §5: 5 segundos).</summary>
    public static readonly TimeSpan PlazoDeBloqueoPredeterminado = TimeSpan.FromSeconds(5);

    private const string FilaBloqueada = "55P03";

    private const string Columnas = """
        id_hash, usuario_id, host, creada_en, ultima_actividad, expira_absoluta, credencial_cifrada, credencial_vence_en, renovacion_cifrada,
        renovacion_incierta, retorno_tras_aceptar, revocacion_pendiente, reintentos_de_revocacion, version_de_clave, cerrada_en, motivo_de_cierre
        """;

    private readonly string _plazo = ((long)(plazoDeBloqueo ?? PlazoDeBloqueoPredeterminado).TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";

    public Task Insertar(SesionGuardada sesion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        return AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"""
                    INSERT INTO web.sesiones_web ({Columnas})
                    VALUES (@id, @usuario, @host, @creada, @actividad, @expira, @credencial, @credencial_vence, @renovacion, @incierta, @retorno, @pendiente, @reintentos, @version, @cerrada, @motivo)
                    """,
                    conexion,
                    transaccion);
                Parametros(comando, sesion);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<SesionGuardada?> Cargar(HashDeSesion id, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM web.sesiones_web WHERE id_hash = @id", conexion, transaccion);
                comando.Parameters.AddWithValue("id", Convert.FromHexString(id.Valor));
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await filas.ReadAsync(token).ConfigureAwait(false) ? Leer(filas) : null;
            },
            cancelacion);

    public Task<SesionGuardada?> CargarParaRenovar(HashDeSesion id, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // El bloqueo dura lo que la unidad de trabajo: sin una activa se liberaría al instante y no protegería nada. Aquí siempre hay una (Ejecutar la abre),
                // pero quien llama debe haberla abierto antes de leer, para que la relectura y la escritura ocurran con la fila retenida.
                await Ejecutar(conexion, transaccion, $"SET LOCAL lock_timeout = '{_plazo}'", token).ConfigureAwait(false);
                try
                {
                    await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM web.sesiones_web WHERE id_hash = @id FOR UPDATE", conexion, transaccion);
                    comando.Parameters.AddWithValue("id", Convert.FromHexString(id.Valor));
                    SesionGuardada? fila;
                    await using (var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        fila = await filas.ReadAsync(token).ConfigureAwait(false) ? Leer(filas) : null;
                    }

                    // El plazo era solo para esperar la fila: no se arrastra a lo demás que haga la unidad de trabajo (otras tablas, otros módulos).
                    await Ejecutar(conexion, transaccion, "SET LOCAL lock_timeout = DEFAULT", token).ConfigureAwait(false);
                    return fila;
                }
                catch (PostgresException ex) when (ex.SqlState == FilaBloqueada)
                {
                    // La transacción queda abortada: quien llama responde servicio no disponible y la unidad de trabajo la revierte.
                    throw new BloqueoDeSesionNoDisponibleException("La sesión está ocupada por otra solicitud.", ex);
                }
            },
            cancelacion);

    public Task Guardar(SesionGuardada sesion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        return AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // Una fila cerrada no se reabre: si en la base ya está cerrada, solo puede seguir cerrada (por ejemplo, para anotar la revocación pendiente).
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE web.sesiones_web
                    SET ultima_actividad = @actividad, credencial_cifrada = @credencial, credencial_vence_en = @credencial_vence, renovacion_cifrada = @renovacion,
                        renovacion_incierta = @incierta, retorno_tras_aceptar = @retorno, revocacion_pendiente = @pendiente, reintentos_de_revocacion = @reintentos,
                        version_de_clave = @version, cerrada_en = COALESCE(cerrada_en, @cerrada), motivo_de_cierre = COALESCE(motivo_de_cierre, @motivo)
                    WHERE id_hash = @id AND (cerrada_en IS NULL OR @cerrada IS NOT NULL)
                    """,
                    conexion,
                    transaccion);
                Parametros(comando, sesion);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task ActualizarActividad(HashDeSesion id, DateTimeOffset ultimaActividad, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // Solo la actividad, y solo hacia adelante: nunca pisa la credencial que otra solicitud acaba de rotar ni reabre una sesión cerrada.
                await using var comando = new NpgsqlCommand(
                    "UPDATE web.sesiones_web SET ultima_actividad = @actividad WHERE id_hash = @id AND cerrada_en IS NULL AND ultima_actividad < @actividad",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", Convert.FromHexString(id.Valor));
                comando.Parameters.AddWithValue("actividad", ultimaActividad.ToUniversalTime());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<IReadOnlyList<SesionGuardada>> CargarAbiertasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar<IReadOnlyList<SesionGuardada>>(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await Ejecutar(conexion, transaccion, $"SET LOCAL lock_timeout = '{_plazo}'", token).ConfigureAwait(false);
                try
                {
                    // Un orden fijo evita el interbloqueo entre dos «cerrar todas» del mismo usuario.
                    await using var comando = new NpgsqlCommand(
                        $"SELECT {Columnas} FROM web.sesiones_web WHERE usuario_id = @usuario AND cerrada_en IS NULL ORDER BY id_hash FOR UPDATE",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("usuario", usuarioId);
                    await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                    var resultado = new List<SesionGuardada>();
                    while (await filas.ReadAsync(token).ConfigureAwait(false))
                    {
                        resultado.Add(Leer(filas));
                    }

                    return resultado;
                }
                catch (PostgresException ex) when (ex.SqlState == FilaBloqueada)
                {
                    throw new BloqueoDeSesionNoDisponibleException("La sesión está ocupada por otra solicitud.", ex);
                }
            },
            cancelacion);

    public Task<IReadOnlyList<SesionGuardada>> CargarParaPurgar(HostDeSesion host, FaseDeLaPurga fase, DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar<IReadOnlyList<SesionGuardada>>(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // SKIP LOCKED: lo que una solicitud o la purga de otra réplica tiene tomado no se espera ni se atiende dos veces.
                // Solo las del host de esta réplica: la clave de cada host solo abre sus filas.
                const string Vencimientos = """
                    WHERE host = @host
                      AND ((cerrada_en IS NULL AND (expira_absoluta <= @ahora OR ultima_actividad <= @inactividad))
                        OR (cerrada_en IS NOT NULL AND NOT revocacion_pendiente AND cerrada_en <= @retencion))
                    ORDER BY id_hash
                    """;
                const string Pendientes = """
                    WHERE host = @host AND cerrada_en IS NOT NULL AND revocacion_pendiente
                    ORDER BY reintentos_de_revocacion, cerrada_en, id_hash
                    """;
                await using var comando = new NpgsqlCommand(
                    $"SELECT {Columnas} FROM web.sesiones_web {(fase == FaseDeLaPurga.Vencimientos ? Vencimientos : Pendientes)} LIMIT @maximo FOR UPDATE SKIP LOCKED",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("host", host == HostDeSesion.Tienda ? "tienda" : "gestion");
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                comando.Parameters.AddWithValue("inactividad", (ahora - PoliticaDeSesion.InactividadMaxima).ToUniversalTime());
                comando.Parameters.AddWithValue("retencion", (ahora - PoliticaDeBorradores.Vigencia).ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                var resultado = new List<SesionGuardada>();
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    resultado.Add(Leer(filas));
                }

                return resultado;
            },
            cancelacion);

    public Task GuardarRetorno(HashDeSesion id, string ruta, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // Solo una sesión abierta: una cerrada o vencida no conserva ningún retorno (D05).
                await using var comando = new NpgsqlCommand("UPDATE web.sesiones_web SET retorno_tras_aceptar = @ruta WHERE id_hash = @id AND cerrada_en IS NULL", conexion, transaccion);
                comando.Parameters.AddWithValue("id", Convert.FromHexString(id.Valor));
                comando.Parameters.AddWithValue("ruta", ruta);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<string?> TomarRetorno(HashDeSesion id, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar<string?>(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // De un solo uso y atómico: lee la ruta bajo el bloqueo de la fila y la borra en la misma sentencia; dos solicitudes simultáneas no obtienen la misma.
                await using var comando = new NpgsqlCommand(
                    "WITH previa AS (SELECT retorno_tras_aceptar AS ruta FROM web.sesiones_web WHERE id_hash = @id AND cerrada_en IS NULL FOR UPDATE) " +
                    "UPDATE web.sesiones_web s SET retorno_tras_aceptar = NULL FROM previa WHERE s.id_hash = @id RETURNING previa.ruta",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", Convert.FromHexString(id.Valor));
                var valor = await comando.ExecuteScalarAsync(token).ConfigureAwait(false);
                return valor as string;
            },
            cancelacion);

    public Task Borrar(HashDeSesion id, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                // Solo una fila cerrada y sin revocación pendiente: una abierta o con la renovación sin revocar nunca se borra por aquí.
                await using var comando = new NpgsqlCommand("DELETE FROM web.sesiones_web WHERE id_hash = @id AND cerrada_en IS NOT NULL AND NOT revocacion_pendiente", conexion, transaccion);
                comando.Parameters.AddWithValue("id", Convert.FromHexString(id.Valor));
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    private static void Parametros(NpgsqlCommand comando, SesionGuardada fila)
    {
        var sesion = fila.Sesion;
        var p = comando.Parameters;
        p.AddWithValue("id", Convert.FromHexString(sesion.Id.Valor));
        p.AddWithValue("usuario", sesion.UsuarioId);
        p.AddWithValue("host", sesion.Host == HostDeSesion.Tienda ? "tienda" : "gestion");
        p.AddWithValue("creada", sesion.Creada.ToUniversalTime());
        p.AddWithValue("actividad", sesion.UltimaActividad.ToUniversalTime());
        p.AddWithValue("expira", sesion.ExpiraAbsoluta.ToUniversalTime());
        AgregarNulable(p, "credencial", NpgsqlTypes.NpgsqlDbType.Bytea, fila.CredencialCifrada);
        AgregarNulable(p, "credencial_vence", NpgsqlTypes.NpgsqlDbType.TimestampTz, fila.CredencialVenceEn?.ToUniversalTime());
        AgregarNulable(p, "renovacion", NpgsqlTypes.NpgsqlDbType.Bytea, fila.RenovacionCifrada);
        p.AddWithValue("incierta", fila.RenovacionIncierta);
        AgregarNulable(p, "retorno", NpgsqlTypes.NpgsqlDbType.Text, fila.RetornoTrasAceptar);
        p.AddWithValue("pendiente", fila.RevocacionPendiente);
        p.AddWithValue("reintentos", fila.ReintentosDeRevocacion);
        p.AddWithValue("version", fila.VersionDeClave);
        AgregarNulable(p, "cerrada", NpgsqlTypes.NpgsqlDbType.TimestampTz, sesion.CerradaEn?.ToUniversalTime());
        AgregarNulable(p, "motivo", NpgsqlTypes.NpgsqlDbType.Text, sesion.MotivoDeCierre is { } motivo ? Texto(motivo) : null);
    }

    private static void AgregarNulable(NpgsqlParameterCollection parametros, string nombre, NpgsqlTypes.NpgsqlDbType tipo, object? valor) =>
        parametros.Add(new NpgsqlParameter(nombre, tipo) { Value = AccesoAlEsquemaWeb.ONulo(valor) });

    private static SesionGuardada Leer(NpgsqlDataReader filas)
    {
        var cerradaEn = AccesoAlEsquemaWeb.LeerNulable<DateTimeOffset>(filas, 14);
        MotivoDeCierre? motivo = filas.IsDBNull(15) ? null : DeTexto(filas.GetString(15));
        var estado = cerradaEn is null ? EstadoDeSesion.Abierta : motivo!.Value.EstadoResultante();
        var sesion = SesionWeb.Reconstruir(
            HashDeSesion.De(Convert.ToHexStringLower(filas.GetFieldValue<byte[]>(0))),
            filas.GetGuid(1),
            filas.GetString(2) == "tienda" ? HostDeSesion.Tienda : HostDeSesion.Gestion,
            filas.GetFieldValue<DateTimeOffset>(3),
            filas.GetFieldValue<DateTimeOffset>(4),
            filas.GetFieldValue<DateTimeOffset>(5),
            estado,
            motivo,
            cerradaEn);
        return new SesionGuardada
        {
            Sesion = sesion,
            CredencialCifrada = AccesoAlEsquemaWeb.LeerBytes(filas, 6),
            CredencialVenceEn = AccesoAlEsquemaWeb.LeerNulable<DateTimeOffset>(filas, 7),
            RenovacionCifrada = AccesoAlEsquemaWeb.LeerBytes(filas, 8),
            RenovacionIncierta = filas.GetBoolean(9),
            RetornoTrasAceptar = filas.IsDBNull(10) ? null : filas.GetString(10),
            RevocacionPendiente = filas.GetBoolean(11),
            ReintentosDeRevocacion = filas.GetInt32(12),
            VersionDeClave = filas.GetInt16(13),
        };
    }

    private static string Texto(MotivoDeCierre motivo) => motivo switch
    {
        MotivoDeCierre.Voluntario => "voluntario",
        MotivoDeCierre.VencidaPorInactividad => "vencida_por_inactividad",
        MotivoDeCierre.VencidaPorTopeAbsoluto => "vencida_por_tope_absoluto",
        MotivoDeCierre.RenovacionRechazada => "renovacion_rechazada",
        MotivoDeCierre.CerradaPorRevocacion => "cerrada_por_revocacion",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo), motivo, "Motivo de cierre desconocido."),
    };

    private static MotivoDeCierre DeTexto(string texto) => texto switch
    {
        "voluntario" => MotivoDeCierre.Voluntario,
        "vencida_por_inactividad" => MotivoDeCierre.VencidaPorInactividad,
        "vencida_por_tope_absoluto" => MotivoDeCierre.VencidaPorTopeAbsoluto,
        "renovacion_rechazada" => MotivoDeCierre.RenovacionRechazada,
        "cerrada_por_revocacion" => MotivoDeCierre.CerradaPorRevocacion,
        _ => throw new InvalidOperationException("La fila guarda un motivo de cierre desconocido."),
    };
}
