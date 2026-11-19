using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// Las renovaciones opacas (T33; IDE-14, IDE-44, IDE-46, IDE-60, IDE-61): se guardan por la huella SHA-256 de su valor; usarla es un <c>UPDATE … WHERE usada_en IS NULL</c> (rotación única: dos solicitudes con la misma
/// renovación solo rotan una, la otra se trata como reutilización) y revocar es una actualización por familia, por usuario o de todos.
/// </summary>
public sealed class PostgresRepositorioDeRenovaciones(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeRenovaciones
{
    public Task Guardar(Renovacion renovacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(renovacion);
        return Escribir(
            """
            INSERT INTO identidad.renovaciones (huella, usuario_id, familia, emitida_en, expira_absoluta, usada_en, revocada_en) VALUES (@h, @u, @f, @emitida, @expira, @usada, @revocada)
            ON CONFLICT (huella) DO UPDATE SET usada_en = EXCLUDED.usada_en, revocada_en = EXCLUDED.revocada_en
            """,
            cancelacion,
            ("h", renovacion.Huella),
            ("u", renovacion.UsuarioId),
            ("f", renovacion.Familia),
            ("emitida", renovacion.Emitida),
            ("expira", renovacion.ExpiraAbsoluta),
            ("usada", renovacion.UsadaEn),
            ("revocada", renovacion.RevocadaEn));
    }

    public Task<Renovacion?> PorHuella(string huella, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "SELECT huella, usuario_id, familia, emitida_en, expira_absoluta, usada_en, revocada_en FROM identidad.renovaciones WHERE huella = @h",
                    ("h", huella));
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await filas.ReadAsync(ct).ConfigureAwait(false)
                    ? Renovacion.Restaurar(
                        filas.GetString(0),
                        filas.GetGuid(1),
                        filas.GetGuid(2),
                        AccesoAlEsquemaDeIdentidad.Instante(filas, 3),
                        AccesoAlEsquemaDeIdentidad.Instante(filas, 4),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 5),
                        AccesoAlEsquemaDeIdentidad.Nulable<DateTimeOffset>(filas, 6))
                    : null;
            },
            cancelacion);

    public async Task<bool> MarcarUsada(string huella, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        await Filas("UPDATE identidad.renovaciones SET usada_en = @ahora WHERE huella = @h AND usada_en IS NULL", cancelacion, ("h", huella), ("ahora", ahora)).ConfigureAwait(false) == 1;

    public async Task RevocarFamilia(Guid usuarioId, Guid familia, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        await Filas("UPDATE identidad.renovaciones SET revocada_en = @ahora WHERE usuario_id = @u AND familia = @f AND revocada_en IS NULL", cancelacion, ("u", usuarioId), ("f", familia), ("ahora", ahora)).ConfigureAwait(false);

    public async Task RevocarTodas(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        await Filas("UPDATE identidad.renovaciones SET revocada_en = @ahora WHERE usuario_id = @u AND revocada_en IS NULL", cancelacion, ("u", usuarioId), ("ahora", ahora)).ConfigureAwait(false);

    public async Task RevocarLasDeTodos(DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        await Filas("UPDATE identidad.renovaciones SET revocada_en = @ahora WHERE revocada_en IS NULL", cancelacion, ("ahora", ahora)).ConfigureAwait(false);

    public async Task RevocarLasDemas(Guid usuarioId, Guid familiaQueSeConserva, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        await Filas("UPDATE identidad.renovaciones SET revocada_en = @ahora WHERE usuario_id = @u AND familia <> @f AND revocada_en IS NULL", cancelacion, ("u", usuarioId), ("f", familiaQueSeConserva), ("ahora", ahora)).ConfigureAwait(false);

    private Task Escribir(string sql, CancellationToken cancelacion, params (string, object?)[] parametros) => Filas(sql, cancelacion, parametros);

    private Task<int> Filas(string sql, CancellationToken cancelacion, params (string, object?)[] parametros) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, sql, parametros);
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}

/// <summary>Los desafíos de ingreso (T33; IDE-21, IDE-23): se guardan por la huella de su valor opaco y se consumen con <c>UPDATE … WHERE NOT usado</c>: una sola solicitud lo consume.</summary>
public sealed class PostgresRepositorioDeDesafios(AccesoAlEsquemaDeIdentidad acceso) : IRepositorioDeDesafios
{
    public Task Guardar(DesafioDeIngreso desafio, string huella, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(desafio);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "INSERT INTO identidad.desafios_de_ingreso (id, huella, usuario_id, modo, creado_en, vence_en, usado) VALUES (@id, @h, @u, @modo, @creado, @vence, @usado)",
                    ("id", desafio.Id),
                    ("h", huella),
                    ("u", desafio.UsuarioId),
                    ("modo", Modo(desafio.Modo)),
                    ("creado", desafio.Creado),
                    ("vence", desafio.Vence),
                    ("usado", desafio.Usado));
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<DesafioDeIngreso?> PorHuella(string huella, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT id, usuario_id, modo, creado_en, usado FROM identidad.desafios_de_ingreso WHERE huella = @h", ("h", huella));
                await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await filas.ReadAsync(ct).ConfigureAwait(false)
                    ? DesafioDeIngreso.Restaurar(filas.GetGuid(0), filas.GetGuid(1), Modo(filas.GetString(2)), AccesoAlEsquemaDeIdentidad.Instante(filas, 3), filas.GetBoolean(4))
                    : null;
            },
            cancelacion);

    public Task<bool> MarcarUsado(Guid desafioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "UPDATE identidad.desafios_de_ingreso SET usado = true WHERE id = @id AND NOT usado", ("id", desafioId));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            },
            cancelacion);

    private static string Modo(ModoDelDesafio modo) => modo switch
    {
        ModoDelDesafio.PresentarSegundoFactor => "presentar-segundo-factor",
        ModoDelDesafio.ConfigurarSegundoFactor => "configurar-segundo-factor",
        ModoDelDesafio.CambiarContrasenaTemporal => "cambiar-contrasena-temporal",
        _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Modo desconocido."),
    };

    private static ModoDelDesafio Modo(string modo) => modo switch
    {
        "presentar-segundo-factor" => ModoDelDesafio.PresentarSegundoFactor,
        "configurar-segundo-factor" => ModoDelDesafio.ConfigurarSegundoFactor,
        "cambiar-contrasena-temporal" => ModoDelDesafio.CambiarContrasenaTemporal,
        _ => throw new InvalidOperationException("Modo de desafío guardado desconocido."),
    };
}

/// <summary>
/// El segundo factor (T33; IDE-20, IDE-21, IDE-51, IDE-52, D04): el secreto va cifrado; aceptar un paso TOTP es <c>UPDATE … WHERE ultimo_paso_usado IS NULL OR ultimo_paso_usado &lt; @paso</c> y consumir un código de
/// respaldo es <c>UPDATE … WHERE usado_en IS NULL</c>: dos solicitudes simultáneas con el mismo código aceptan solo una. Activar y reemplazar los códigos de respaldo son atómicos en la transacción del llamador.
/// </summary>
public sealed class PostgresSegundoFactorAlmacen(AccesoAlEsquemaDeIdentidad acceso) : ISegundoFactorAlmacen
{
    public Task<SegundoFactorGuardado?> Obtener(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT secreto_cifrado, estado, ultimo_paso_usado FROM identidad.segundo_factor WHERE usuario_id = @u", ("u", usuarioId));
                byte[] secreto;
                EstadoDelSegundoFactor estado;
                long? paso;
                await using (var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    if (!await filas.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    secreto = (byte[])filas.GetValue(0);
                    estado = filas.GetString(1) == "activo" ? EstadoDelSegundoFactor.Activo : EstadoDelSegundoFactor.SinConfirmar;
                    paso = AccesoAlEsquemaDeIdentidad.Nulable<long>(filas, 2);
                }

                var huellas = new List<string>();
                await using var respaldo = AccesoAlEsquemaDeIdentidad.Comando(c, t, "SELECT huella FROM identidad.codigos_de_respaldo WHERE usuario_id = @u AND usado_en IS NULL ORDER BY huella", ("u", usuarioId));
                await using var codigos = await respaldo.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await codigos.ReadAsync(ct).ConfigureAwait(false))
                {
                    huellas.Add(codigos.GetString(0));
                }

                return new SegundoFactorGuardado(SegundoFactor.Restaurar(usuarioId, estado, paso, huellas), secreto);
            },
            cancelacion);

    public Task GuardarSinConfirmar(Guid usuarioId, byte[] secretoCifrado, CancellationToken cancelacion = default) =>
        Ejecutar(
            """
            INSERT INTO identidad.segundo_factor (usuario_id, secreto_cifrado, estado) VALUES (@u, @s, 'sin-confirmar')
            ON CONFLICT (usuario_id) DO UPDATE SET secreto_cifrado = EXCLUDED.secreto_cifrado, ultimo_paso_usado = NULL WHERE identidad.segundo_factor.estado = 'sin-confirmar'
            """,
            cancelacion,
            ("u", usuarioId),
            ("s", secretoCifrado));

    public Task Activar(Guid usuarioId, long pasoConfirmado, IReadOnlyList<string> huellasDeRespaldo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellasDeRespaldo);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using (var activar = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.segundo_factor SET estado = 'activo', confirmado_en = now(), ultimo_paso_usado = @paso WHERE usuario_id = @u AND estado = 'sin-confirmar'",
                    ("u", usuarioId),
                    ("paso", pasoConfirmado)))
                {
                    if (await activar.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    {
                        throw new InvalidOperationException("No hay un segundo factor sin confirmar que activar.");
                    }
                }

                await ReemplazarEn(c, t, usuarioId, huellasDeRespaldo, ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<bool> AceptarPaso(Guid usuarioId, long paso, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.segundo_factor SET ultimo_paso_usado = @paso WHERE usuario_id = @u AND estado = 'activo' AND (ultimo_paso_usado IS NULL OR ultimo_paso_usado < @paso)",
                    ("u", usuarioId),
                    ("paso", paso));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            },
            cancelacion);

    public Task<bool> ConsumirCodigoDeRespaldo(Guid usuarioId, string huella, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(
                    c,
                    t,
                    "UPDATE identidad.codigos_de_respaldo SET usado_en = @ahora WHERE usuario_id = @u AND huella = @h AND usado_en IS NULL",
                    ("u", usuarioId),
                    ("h", huella),
                    ("ahora", ahora));
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            },
            cancelacion);

    public Task Desactivar(Guid usuarioId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                foreach (var tabla in new[] { "codigos_de_respaldo", "segundo_factor" })
                {
                    await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, $"DELETE FROM identidad.{tabla} WHERE usuario_id = @u", ("u", usuarioId));
                    await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);

    public Task ReemplazarCodigosDeRespaldo(Guid usuarioId, IReadOnlyList<string> huellasNuevas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huellasNuevas);
        return acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await ReemplazarEn(c, t, usuarioId, huellasNuevas, ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    private static async Task ReemplazarEn(NpgsqlConnection c, NpgsqlTransaction t, Guid usuarioId, IReadOnlyList<string> huellas, CancellationToken ct)
    {
        await using (var borrar = AccesoAlEsquemaDeIdentidad.Comando(c, t, "DELETE FROM identidad.codigos_de_respaldo WHERE usuario_id = @u", ("u", usuarioId)))
        {
            await borrar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var huella in huellas)
        {
            await using var insertar = AccesoAlEsquemaDeIdentidad.Comando(c, t, "INSERT INTO identidad.codigos_de_respaldo (usuario_id, huella) VALUES (@u, @h)", ("u", usuarioId), ("h", huella));
            await insertar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private Task Ejecutar(string sql, CancellationToken cancelacion, params (string, object?)[] parametros) =>
        acceso.Ejecutar(
            async (c, t, ct) =>
            {
                await using var comando = AccesoAlEsquemaDeIdentidad.Comando(c, t, sql, parametros);
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}
