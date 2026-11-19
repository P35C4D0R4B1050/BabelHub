using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using Npgsql;
using NpgsqlTypes;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// Acceso común a las tablas del esquema <c>identidad</c> (T33): cada operación corre en la unidad de trabajo en curso (o en una transacción corta propia, si no hay) bajo el rol <c>rol_identidad</c> (<c>EjecutarComo</c>).
/// La conexión pertenece al flujo asíncrono que abrió la transacción: no se usa desde tareas paralelas. Las actualizaciones que deciden una carrera son <c>UPDATE … WHERE</c> de una sentencia (D17).
/// </summary>
public sealed class AccesoAlEsquemaDeIdentidad(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    private static readonly EsquemaDeModulo Identidad = EsquemaDeModulo.Crear("identidad");

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Identidad,
                    async interno => resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false),
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }

    public static NpgsqlCommand Comando(NpgsqlConnection c, NpgsqlTransaction t, string sql, params (string Nombre, object? Valor)[] parametros)
    {
        var comando = new NpgsqlCommand(sql, c, t);
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.Add(new NpgsqlParameter(nombre, valor switch
            {
                DateTimeOffset f => f.ToUniversalTime(),
                null => DBNull.Value,
                _ => valor,
            }));
        }

        return comando;
    }

    public static T? Nulable<T>(NpgsqlDataReader filas, int posicion)
        where T : struct => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<T>(posicion);

    public static string? Texto(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetString(posicion);

    public static DateTimeOffset Instante(NpgsqlDataReader filas, int posicion) => filas.GetFieldValue<DateTimeOffset>(posicion);

    public static string Rol(RolDeUsuario rol) => rol switch
    {
        RolDeUsuario.Cliente => "cliente",
        RolDeUsuario.EncargadoDeSucursal => "encargado-de-sucursal",
        RolDeUsuario.Logistica => "personal-de-logistica",
        RolDeUsuario.AdministradorCentral => "administrador-central",
        _ => throw new ArgumentOutOfRangeException(nameof(rol), rol, "Rol desconocido."),
    };

    public static RolDeUsuario Rol(string rol) => rol switch
    {
        "cliente" => RolDeUsuario.Cliente,
        "encargado-de-sucursal" => RolDeUsuario.EncargadoDeSucursal,
        "personal-de-logistica" => RolDeUsuario.Logistica,
        "administrador-central" => RolDeUsuario.AdministradorCentral,
        _ => throw new InvalidOperationException("Rol guardado desconocido."),
    };

    public static string Proposito(DominioProposito proposito) => proposito switch
    {
        DominioProposito.VerificacionDeCorreo => "verificacion-de-correo",
        DominioProposito.RecuperacionDeAcceso => "recuperacion-de-acceso",
        _ => throw new ArgumentOutOfRangeException(nameof(proposito), proposito, "Propósito desconocido."),
    };
}

/// <summary>
/// El repositorio de cuentas (T33; D17): la cuenta por correo o por identificador (con <c>FOR UPDATE</c> si se va a actualizar), el alta con <c>ON CONFLICT DO NOTHING</c> sobre el correo normalizado (dos registros
/// simultáneos dejan una sola cuenta), el guardado de todo lo mutable y los recuentos con bloqueo que impiden dejar el sistema sin administrador.
/// </summary>
public sealed class PostgresRepositorioDeCuentas(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeCuentas
{
    private const string Columnas = """
        id, nombre, correo, correo_verificado, rol, sucursal, activo, hash_contrasena, contrasena_temporal_hasta, debe_cambiar, sesiones_validas_desde, creada_en, cancelada_en,
        mayor_de_18_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en,
        numero_whatsapp, consentimiento_whatsapp, consentimiento_whatsapp_en
        """;

    public Task<CuentaDeIdentidad?> PorCorreo(string correoNormalizado, CancellationToken cancelacion = default) =>
        Leer($"SELECT {Columnas} FROM identidad.usuarios WHERE correo = @correo", [("correo", Usuario.NormalizarCorreo(correoNormalizado))], cancelacion);

    public Task<CuentaDeIdentidad?> PorId(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default) =>
        Leer($"SELECT {Columnas} FROM identidad.usuarios WHERE id = @id{(paraActualizar ? " FOR UPDATE" : string.Empty)}", [("id", usuarioId)], cancelacion);

    public Task<bool> Insertar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    $"""
                    INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, sucursal, activo, hash_contrasena, contrasena_temporal_hasta, debe_cambiar, sesiones_validas_desde, creada_en, cancelada_en,
                        mayor_de_18_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en,
                        numero_whatsapp, consentimiento_whatsapp, consentimiento_whatsapp_en)
                    VALUES (@id, @nombre, @correo, @verificado, @rol, @sucursal, @activo, @hash, @temporal, @debe, @corte, @creada, @cancelada, @mayor, @tv, @tf, @pv, @pf, @rv, @rf, @numero, @consiente, @consiente_en)
                    ON CONFLICT (correo) DO NOTHING
                    """,
                    Parametros(cuenta));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            },
            cancelacion);
    }

    public Task Guardar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    """
                    UPDATE identidad.usuarios SET nombre = @nombre, correo = @correo, correo_verificado = @verificado, rol = @rol, sucursal = @sucursal, activo = @activo, hash_contrasena = @hash,
                        contrasena_temporal_hasta = @temporal, debe_cambiar = @debe, sesiones_validas_desde = @corte, cancelada_en = @cancelada, mayor_de_18_en = @mayor,
                        acepto_terminos_version = @tv, acepto_terminos_en = @tf, acepto_privacidad_version = @pv, acepto_privacidad_en = @pf, acepto_reembolso_version = @rv, acepto_reembolso_en = @rf,
                        numero_whatsapp = @numero, consentimiento_whatsapp = @consiente, consentimiento_whatsapp_en = @consiente_en
                    WHERE id = @id
                    """,
                    Parametros(cuenta));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task Eliminar(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                // Una cuenta sin verificar que se reemplaza (IDE-84) no hereda ningún dato: se van también sus filas dependientes.
                foreach (var tabla in new[] { "codigos_de_un_solo_uso", "renovaciones", "desafios_de_ingreso", "dispositivos", "cambios_de_correo_pendientes", "codigos_de_respaldo", "segundo_factor", "avisos_de_cuenta" })
                {
                    await using var hijas = AccesoAlEsquemaDeIdentidad.Comando(c, t, $"DELETE FROM identidad.{tabla} WHERE usuario_id = @id", ("id", usuarioId));
                    await hijas.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "DELETE FROM identidad.usuarios WHERE id = @id", ("id", usuarioId));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task<int> ContarAdministradoresActivos(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                // FOR UPDATE: dos desactivaciones o degradaciones simultáneas se serializan y ninguna deja el sistema sin administrador (IDE-49, D17).
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT count(*)::int FROM (SELECT 1 FROM identidad.usuarios WHERE rol = 'administrador-central' AND activo AND cancelada_en IS NULL FOR UPDATE) a");
                return (int)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> IdsActivosConRol(IReadOnlyCollection<RolDeUsuario> roles, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT id FROM identidad.usuarios WHERE rol = ANY(@roles) AND activo AND cancelada_en IS NULL ORDER BY id",
                    ("roles", roles.Select(AccesoAlEsquemaDeIdentidad.Rol).ToArray()));
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

    public Task<EncargadoDeSucursal?> EncargadoActivoDe(Guid sucursal, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<EncargadoDeSucursal?>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT id FROM identidad.usuarios WHERE rol = 'encargado-de-sucursal' AND sucursal = @sucursal AND activo AND cancelada_en IS NULL",
                    ("sucursal", sucursal));
                return await comando.ExecuteScalarAsync(ct).ConfigureAwait(false) is Guid id ? new EncargadoDeSucursal(id, sucursal) : null;
            },
            cancelacion);

    private Task<CuentaDeIdentidad?> Leer(string sql, (string, object?)[] parametros, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, sql, parametros);
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await filas.ReadAsync(ct).ConfigureAwait(false) ? Mapear(filas) : null;
            },
            cancelacion);

    private static (string, object?)[] Parametros(CuentaDeIdentidad cuenta)
    {
        var u = cuenta.Usuario;
        string? Version(TextoLegal texto) => cuenta.Aceptacion.VersionAceptada(texto);
        DateTimeOffset? Fecha(TextoLegal texto) => cuenta.Aceptacion.Aceptadas.TryGetValue(texto, out var a) ? a.Fecha : null;
        return
        [
            ("id", u.Id),
            ("nombre", cuenta.Nombre),
            ("correo", u.Correo),
            ("verificado", u.CorreoVerificado),
            ("rol", AccesoAlEsquemaDeIdentidad.Rol(u.Rol)),
            ("sucursal", u.Sucursal),
            ("activo", u.Activo),
            ("hash", cuenta.ResumenDeAcceso),
            ("temporal", cuenta.ContrasenaTemporalHasta),
            ("debe", cuenta.DebeCambiarLaTemporal),
            ("corte", u.SesionesValidasDesde),
            ("creada", cuenta.CreadaEn),
            ("cancelada", cuenta.CanceladaEn),
            ("mayor", cuenta.Aceptacion.MayorDeEdadDeclaradaEn),
            ("tv", Version(TextoLegal.Terminos)),
            ("tf", Fecha(TextoLegal.Terminos)),
            ("pv", Version(TextoLegal.Privacidad)),
            ("pf", Fecha(TextoLegal.Privacidad)),
            ("rv", Version(TextoLegal.Reembolso)),
            ("rf", Fecha(TextoLegal.Reembolso)),
            ("numero", cuenta.Whatsapp.Numero?.Valor),
            ("consiente", cuenta.Whatsapp.Consentido),
            ("consiente_en", cuenta.Whatsapp.Fecha),
        ];
    }

    private static CuentaDeIdentidad Mapear(NpgsqlDataReader f)
    {
        var usuario = Usuario.Restaurar(f.GetGuid(0), f.GetString(2), AccesoAlEsquemaDeIdentidad.Rol(f.GetString(4)), AccesoAlEsquemaDeIdentidad.Nulable<Guid>(f, 5), f.GetBoolean(3), f.GetBoolean(6));
        if (AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(f, 10) is { } corte)
        {
            usuario.AvanzarElCorteDeSesiones(corte);
        }

        var aceptadas = new List<(TextoLegal, string, DateTimeOffset)>();
        void Texto(TextoLegal texto, int version, int fecha)
        {
            if (AccesoAlEsquemaDeIdentidad.Texto(f, version) is { } v && AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(f, fecha) is { } fe)
            {
                aceptadas.Add((texto, v, fe));
            }
        }

        Texto(TextoLegal.Terminos, 14, 15);
        Texto(TextoLegal.Privacidad, 16, 17);
        Texto(TextoLegal.Reembolso, 18, 19);

        NumeroDeWhatsApp? numero = AccesoAlEsquemaDeIdentidad.Texto(f, 20) is { } texto && NumeroDeWhatsApp.TryCrear(texto, out var n) ? n : null;
        return new CuentaDeIdentidad
        {
            Usuario = usuario,
            Nombre = f.GetString(1),
            ResumenDeAcceso = AccesoAlEsquemaDeIdentidad.Texto(f, 7),
            ContrasenaTemporalHasta = AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(f, 8),
            DebeCambiarLaTemporal = f.GetBoolean(9),
            Aceptacion = AceptacionDeTextos.Restaurar(AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(f, 13), aceptadas),
            CreadaEn = AccesoAlEsquemaDeIdentidad.Instante(f, 11),
            CanceladaEn = AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(f, 12),
            Whatsapp = ConsentimientoDeWhatsApp.Restaurar(numero, f.GetBoolean(21), AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(f, 22)),
        };
    }
}

/// <summary>Las versiones vigentes de los textos legales (<c>identidad.versiones_de_textos</c>, una sola fila): la siembra la hace la migración de datos del despliegue.</summary>
public sealed class PostgresVersionesDeTextosVigentes(AccesoAlEsquemaDeIdentidad acceso) : IVersionesDeTextosVigentes
{
    public Task<VersionesDeTextos> Obtener(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT terminos, privacidad, reembolso FROM identidad.versiones_de_textos WHERE id = 1");
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await filas.ReadAsync(ct).ConfigureAwait(false)
                    ? new VersionesDeTextos(filas.GetString(0), filas.GetString(1), filas.GetString(2))
                    : throw new InvalidOperationException("No hay versiones de textos vigentes: falta sembrarlas.");
            },
            cancelacion);
}

/// <summary>
/// Los códigos de un solo uso (T33; IDE-19, IDE-56, D17): emitir invalida el anterior del mismo propósito y guarda el nuevo en la misma transacción (el índice único parcial respalda «uno vigente»); verificar es <b>una
/// sola sentencia</b> que cuenta el intento y consume el código antes de decidir, de modo que solicitudes paralelas no superan los 5 intentos ni consumen dos veces el mismo código.
/// </summary>
public sealed class PostgresCodigosDeUnSoloUso(AccesoAlEsquemaDeIdentidad acceso) : ICodigosDeUnSoloUso
{
    public Task Emitir(CodigoDeUnSoloUso codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using (var invalidar = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.codigos_de_un_solo_uso SET invalidado = true WHERE usuario_id = @u AND proposito = @p AND NOT usado AND NOT invalidado",
                    ("u", codigo.UsuarioId),
                    ("p", AccesoAlEsquemaDeIdentidad.Proposito(codigo.Proposito))))
                {
                    await invalidar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var insertar = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.codigos_de_un_solo_uso (id, usuario_id, proposito, huella, creado_en, vence_en, intentos, usado, invalidado) VALUES (@id, @u, @p, @h, @creado, @vence, @i, @usado, @inv)",
                    ("id", Guid.NewGuid()),
                    ("u", codigo.UsuarioId),
                    ("p", AccesoAlEsquemaDeIdentidad.Proposito(codigo.Proposito)),
                    ("h", codigo.Huella),
                    ("creado", codigo.Creado),
                    ("vence", codigo.Vence),
                    ("i", codigo.Intentos),
                    ("usado", codigo.Usado),
                    ("inv", codigo.Invalidado));
                await insertar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<ResultadoDelCodigo> Verificar(Guid usuarioId, DominioProposito proposito, string huellaPresentada, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellaPresentada);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                var texto = AccesoAlEsquemaDeIdentidad.Proposito(proposito);
                await using (var consumir = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    """
                    UPDATE identidad.codigos_de_un_solo_uso
                    SET intentos = intentos + CASE WHEN huella = @h THEN 0 ELSE 1 END, usado = (huella = @h)
                    WHERE usuario_id = @u AND proposito = @p AND NOT usado AND NOT invalidado AND vence_en > @ahora AND intentos < @max
                    RETURNING usado
                    """,
                    ("u", usuarioId),
                    ("p", texto),
                    ("h", huellaPresentada),
                    ("ahora", ahora),
                    ("max", CodigoDeUnSoloUso.MaximoDeIntentos)))
                {
                    var correcto = await consumir.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (correcto is bool acertado)
                    {
                        return acertado ? ResultadoDelCodigo.Correcto : ResultadoDelCodigo.Incorrecto;
                    }
                }

                // Ninguna fila cumplió: el motivo exacto solo sirve al registro de seguridad (la respuesta a la persona es genérica).
                await using var motivo = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT usado, invalidado, vence_en, intentos FROM identidad.codigos_de_un_solo_uso WHERE usuario_id = @u AND proposito = @p ORDER BY creado_en DESC LIMIT 1",
                    ("u", usuarioId),
                    ("p", texto));
                await using var filas = await motivo.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await filas.ReadAsync(ct).ConfigureAwait(false) || filas.GetBoolean(0) || filas.GetBoolean(1))
                {
                    return ResultadoDelCodigo.NoVigente;
                }

                return AccesoAlEsquemaDeIdentidad.Instante(filas, 2) <= ahora ? ResultadoDelCodigo.Vencido : ResultadoDelCodigo.IntentosAgotados;
            },
            cancelacion);
    }

    public Task Invalidar(Guid usuarioId, DominioProposito proposito, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.codigos_de_un_solo_uso SET invalidado = true WHERE usuario_id = @u AND proposito = @p AND NOT usado AND NOT invalidado",
                    ("u", usuarioId),
                    ("p", AccesoAlEsquemaDeIdentidad.Proposito(proposito)));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}

/// <summary>La fila idéntica del camino síncrono de una solicitud de código (<c>identidad.solicitudes_de_codigo</c>, IDE-65): se inserta exista o no la cuenta, con la huella del correo; se purga a los 10 minutos.</summary>
public sealed class PostgresRegistroDeSolicitudesDeCodigo(AccesoAlEsquemaDeIdentidad acceso) : IRegistroDeSolicitudesDeCodigo
{
    public static readonly TimeSpan Retencion = TimeSpan.FromMinutes(10);

    public Task Registrar(string huellaDelCorreo, DominioProposito proposito, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.solicitudes_de_codigo (id, huella_correo, proposito, instante) VALUES (@id, @h, @p, @ahora)",
                    ("id", Guid.NewGuid()),
                    ("h", huellaDelCorreo),
                    ("p", AccesoAlEsquemaDeIdentidad.Proposito(proposito)),
                    ("ahora", ahora));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    /// <summary>Purga lo de hace más de 10 minutos (el trabajo de fondo la llama); devuelve cuántas filas se eliminaron.</summary>
    public Task<int> Purgar(DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "DELETE FROM identidad.solicitudes_de_codigo WHERE instante < @limite", ("limite", ahora - Retencion));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}

/// <summary>Los avisos de seguridad que se muestran en la cuenta (<c>identidad.avisos_de_cuenta</c>, IDE-53, IDE-76, IDE-79): sin datos sensibles; marcar visto es un <c>UPDATE</c> condicional por usuario.</summary>
public sealed class PostgresAvisosDeCuenta(AccesoAlEsquemaDeIdentidad acceso) : IAvisosDeCuentaAlmacen
{
    public Task<IReadOnlyList<AvisoDeCuenta>> Listar(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<AvisoDeCuenta>>(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT id, tipo, fecha, visto_en IS NOT NULL FROM identidad.avisos_de_cuenta WHERE usuario_id = @u ORDER BY fecha DESC, id", ("u", usuarioId));
                var avisos = new List<AvisoDeCuenta>();
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await filas.ReadAsync(ct).ConfigureAwait(false))
                {
                    avisos.Add(new AvisoDeCuenta(filas.GetGuid(0), filas.GetString(1), AccesoAlEsquemaDeIdentidad.Instante(filas, 2), filas.GetBoolean(3)));
                }

                return avisos;
            },
            cancelacion);

    public Task<bool> MarcarVisto(Guid usuarioId, Guid avisoId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.avisos_de_cuenta SET visto_en = COALESCE(visto_en, @ahora) WHERE id = @id AND usuario_id = @u",
                    ("id", avisoId),
                    ("u", usuarioId),
                    ("ahora", ahora));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            },
            cancelacion);

    public Task Agregar(Guid usuarioId, string tipo, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.avisos_de_cuenta (id, usuario_id, tipo, fecha) VALUES (@id, @u, @tipo, @ahora)",
                    ("id", Guid.NewGuid()),
                    ("u", usuarioId),
                    ("tipo", tipo),
                    ("ahora", ahora));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}
