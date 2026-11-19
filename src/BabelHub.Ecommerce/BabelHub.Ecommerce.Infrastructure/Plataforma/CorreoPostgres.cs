using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// Las tablas del correo de <c>Plataforma</c> (identidad-seguridad T34; IDE-66, IDE-73, IDE-78, IDE-80, IDE-82): <c>cuota_correo</c> (lo enviado por día de Lima y cupo), <c>topes_de_alertas_de_correo</c> (el tope de 24 al día
/// por motivo y servicio) y <c>correos_pendientes</c> (solo alertas, reclamos y pagos, con el contenido —la dirección y los datos— <b>cifrado</b>). Permiso mínimo del rol del esquema; la migración las incorpora tal cual.
/// </summary>
public static class DefinicionDelCorreo
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS plataforma;

        CREATE TABLE IF NOT EXISTS plataforma.cuota_correo (
            dia date NOT NULL,
            cupo text NOT NULL CHECK (cupo IN ('administracion', 'criticas-de-cuenta', 'codigos', 'resto')),
            enviados integer NOT NULL DEFAULT 0 CHECK (enviados >= 0),
            PRIMARY KEY (dia, cupo)
        );

        CREATE TABLE IF NOT EXISTS plataforma.topes_de_alertas_de_correo (
            dia date NOT NULL,
            motivo text NOT NULL CHECK (motivo ~ '^[a-z][a-z0-9-]{0,99}$'),
            servicio text NOT NULL CHECK (servicio ~ '^[a-z][a-z0-9-]{0,29}$'),
            enviados integer NOT NULL DEFAULT 0 CHECK (enviados >= 0),
            PRIMARY KEY (dia, motivo, servicio)
        );

        -- Lo pendiente: el contenido (dirección, datos, motivo y servicio) va cifrado con AES-GCM; en claro solo la categoría, la plantilla y los plazos.
        CREATE TABLE IF NOT EXISTS plataforma.correos_pendientes (
            id uuid PRIMARY KEY,
            categoria text NOT NULL CHECK (categoria IN ('alerta-a-la-administracion', 'alerta-critica-de-cuenta', 'respuesta-de-reclamo', 'pago')),
            prioridad smallint NOT NULL CHECK (prioridad BETWEEN 0 AND 2),
            plantilla text NOT NULL CHECK (plantilla ~ '^[a-z][a-z0-9-]{0,63}$'),
            contenido bytea NOT NULL CHECK (octet_length(contenido) BETWEEN 30 AND 16384),
            intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0),
            proximo_intento timestamptz NOT NULL,
            creada timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS correos_pendientes_por_turno ON plataforma.correos_pendientes (proximo_intento, prioridad, creada);
        """;

    /// <summary>La cuota y los topes solo se leen y se actualizan por día; la cola se lee, se escribe y se purga lo enviado. Nunca <c>TRUNCATE</c>.</summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA plataforma TO rol_plataforma;
        GRANT SELECT, INSERT, UPDATE ON plataforma.cuota_correo TO rol_plataforma;
        GRANT SELECT, INSERT, UPDATE ON plataforma.topes_de_alertas_de_correo TO rol_plataforma;
        GRANT SELECT, INSERT, DELETE ON plataforma.correos_pendientes TO rol_plataforma;
        GRANT UPDATE (intentos, proximo_intento) ON plataforma.correos_pendientes TO rol_plataforma;
        """;
}

/// <summary>
/// La reserva atómica de la cuota diaria de correo (T34; IDE-66, IDE-73, IDE-82): un <c>INSERT … ON CONFLICT DO UPDATE … WHERE enviados &lt; tamaño</c> por cupo candidato, de modo que dos instancias no superan un
/// cupo y la que no cabe pasa al siguiente. El día es el de Lima. Devolver resta uno sin bajar de cero.
/// </summary>
public sealed class PostgresAlmacenDeCuotaDeCorreo(UnidadDeTrabajoPostgres unidad) : IAlmacenDeCuotaDeCorreo
{
    public Task<ReservaDeCupoDeCorreo?> Reservar(DateOnly dia, IReadOnlyList<CupoCandidato> candidatos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(candidatos);
        return EscrituraDePlataforma.Ejecutar<ReservaDeCupoDeCorreo?>(
            unidad,
            async (c, t, ct) =>
            {
                foreach (var candidato in candidatos)
                {
                    if (candidato.Tamano < 1)
                    {
                        continue;
                    }

                    await using var reservar = new NpgsqlCommand(
                        """
                        INSERT INTO plataforma.cuota_correo (dia, cupo, enviados) VALUES (@dia, @cupo, 1)
                        ON CONFLICT (dia, cupo) DO UPDATE SET enviados = plataforma.cuota_correo.enviados + 1 WHERE plataforma.cuota_correo.enviados < @tamano
                        RETURNING enviados
                        """,
                        c,
                        t);
                    reservar.Parameters.AddWithValue("dia", dia);
                    reservar.Parameters.AddWithValue("cupo", Texto(candidato.Cupo));
                    reservar.Parameters.AddWithValue("tamano", candidato.Tamano);
                    if (await reservar.ExecuteScalarAsync(ct).ConfigureAwait(false) is not int enCupo)
                    {
                        continue;
                    }

                    await using var total = new NpgsqlCommand("SELECT COALESCE(sum(enviados), 0)::int FROM plataforma.cuota_correo WHERE dia = @dia", c, t);
                    total.Parameters.AddWithValue("dia", dia);
                    return new ReservaDeCupoDeCorreo(candidato.Cupo, enCupo, (int)(await total.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
                }

                return null;
            },
            cancelacion);
    }

    public Task Devolver(DateOnly dia, CupoDeCorreo cupo, CancellationToken cancelacion = default) =>
        Escribir("UPDATE plataforma.cuota_correo SET enviados = GREATEST(enviados - 1, 0) WHERE dia = @dia AND cupo = @cupo", cancelacion, ("dia", dia), ("cupo", Texto(cupo)));

    public Task<bool> ReservarAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, int tope, CancellationToken cancelacion = default)
    {
        if (tope < 1)
        {
            return Task.FromResult(false);
        }

        return EscrituraDePlataforma.Ejecutar(
            unidad,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    INSERT INTO plataforma.topes_de_alertas_de_correo (dia, motivo, servicio, enviados) VALUES (@dia, @motivo, @servicio, 1)
                    ON CONFLICT (dia, motivo, servicio) DO UPDATE SET enviados = plataforma.topes_de_alertas_de_correo.enviados + 1 WHERE plataforma.topes_de_alertas_de_correo.enviados < @tope
                    RETURNING enviados
                    """,
                    c,
                    t);
                comando.Parameters.AddWithValue("dia", dia);
                comando.Parameters.AddWithValue("motivo", EscrituraDePlataforma.Validar(motivo, nameof(motivo)));
                comando.Parameters.AddWithValue("servicio", EscrituraDePlataforma.Validar(servicio, nameof(servicio)));
                comando.Parameters.AddWithValue("tope", tope);
                return await comando.ExecuteScalarAsync(ct).ConfigureAwait(false) is int;
            },
            cancelacion);
    }

    public Task DevolverAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, CancellationToken cancelacion = default) =>
        Escribir(
            "UPDATE plataforma.topes_de_alertas_de_correo SET enviados = GREATEST(enviados - 1, 0) WHERE dia = @dia AND motivo = @motivo AND servicio = @servicio",
            cancelacion,
            ("dia", dia),
            ("motivo", EscrituraDePlataforma.Validar(motivo, nameof(motivo))),
            ("servicio", EscrituraDePlataforma.Validar(servicio, nameof(servicio))));

    private Task Escribir(string sql, CancellationToken cancelacion, params (string, object)[] parametros) =>
        EscrituraDePlataforma.Ejecutar(
            unidad,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand(sql, c, t);
                foreach (var (nombre, valor) in parametros)
                {
                    comando.Parameters.AddWithValue(nombre, valor);
                }

                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    private static string Texto(CupoDeCorreo cupo) => cupo switch
    {
        CupoDeCorreo.Administracion => "administracion",
        CupoDeCorreo.CriticasDeCuenta => "criticas-de-cuenta",
        CupoDeCorreo.Codigos => "codigos",
        CupoDeCorreo.Resto => "resto",
        _ => throw new ArgumentOutOfRangeException(nameof(cupo), cupo, "Cupo desconocido."),
    };
}

/// <summary>
/// El cifrado del contenido de la cola de correo (T34): AES-256-GCM con nonce aleatorio, una clave derivada con HKDF-SHA256 de la clave maestra del entorno para este propósito y el identificador del correo como datos
/// asociados (un contenido copiado a otra fila no descifra). El resultado es <c>[versión 1][nonce][etiqueta][cifrado]</c>.
/// </summary>
public sealed class CifradorDeLaColaDeCorreo
{
    public const int LongitudMinimaDeLaClave = 32;

    private const byte Version = 1;

    private const int BytesDelNonce = 12;

    private const int BytesDeLaEtiqueta = 16;

    private readonly byte[] _clave;

    /// <summary>El identificador de la fila y su contexto en claro (categoría y plantilla) viajan como datos asociados: cambiar la plantilla de una fila la vuelve ilegible.</summary>
    private static byte[] DatosAsociados(Guid id, string contexto) => [.. id.ToByteArray(), .. Encoding.UTF8.GetBytes(contexto)];

    public CifradorDeLaColaDeCorreo(byte[] claveMaestra)
    {
        ArgumentNullException.ThrowIfNull(claveMaestra);
        if (claveMaestra.Length < LongitudMinimaDeLaClave)
        {
            throw new ArgumentException($"La clave de la cola de correo debe tener al menos {LongitudMinimaDeLaClave} bytes.", nameof(claveMaestra));
        }

        _clave = HKDF.DeriveKey(HashAlgorithmName.SHA256, claveMaestra, 32, salt: null, info: Encoding.ASCII.GetBytes("babelhub/plataforma/cola-de-correo"));
    }

    public byte[] Cifrar(ReadOnlySpan<byte> contenido, Guid id, string contexto = "")
    {
        var nonce = RandomNumberGenerator.GetBytes(BytesDelNonce);
        var cifrado = new byte[contenido.Length];
        var etiqueta = new byte[BytesDeLaEtiqueta];
        using (var aes = new AesGcm(_clave, BytesDeLaEtiqueta))
        {
            aes.Encrypt(nonce, contenido, cifrado, etiqueta, DatosAsociados(id, contexto));
        }

        var resultado = new byte[1 + BytesDelNonce + BytesDeLaEtiqueta + cifrado.Length];
        resultado[0] = Version;
        nonce.CopyTo(resultado, 1);
        etiqueta.CopyTo(resultado, 1 + BytesDelNonce);
        cifrado.CopyTo(resultado, 1 + BytesDelNonce + BytesDeLaEtiqueta);
        return resultado;
    }

    public byte[] Descifrar(ReadOnlySpan<byte> cifrado, Guid id, string contexto = "")
    {
        if (cifrado.Length < 1 + BytesDelNonce + BytesDeLaEtiqueta || cifrado[0] != Version)
        {
            throw new CryptographicException("El contenido cifrado no tiene un formato válido.");
        }

        var nonce = cifrado.Slice(1, BytesDelNonce).ToArray();
        var etiqueta = cifrado.Slice(1 + BytesDelNonce, BytesDeLaEtiqueta).ToArray();
        var texto = cifrado[(1 + BytesDelNonce + BytesDeLaEtiqueta)..].ToArray();
        var plano = new byte[texto.Length];
        using var aes = new AesGcm(_clave, BytesDeLaEtiqueta);
        aes.Decrypt(nonce, texto, etiqueta, plano, DatosAsociados(id, contexto));
        return plano;
    }
}

/// <summary>
/// La cola <c>plataforma.correos_pendientes</c> (T34; IDE-78, IDE-80): solo alertas a la administración, alertas críticas, respuestas a reclamos y pagos; el <b>contenido va cifrado</b> y lo enviado se purga. <see cref="Vencidos"/>
/// los entrega por prioridad (la administración primero) y, a igual prioridad, del más antiguo al más reciente. Lo que no se puede descifrar (clave cambiada, fila alterada) se omite y no detiene el resto.
/// </summary>
public sealed class PostgresColaDeCorreosPendientes(UnidadDeTrabajoPostgres unidad, CifradorDeLaColaDeCorreo cifrador, IReloj reloj) : IColaDeCorreosPendientes
{
    private sealed record Contenido(string Destinatario, Dictionary<string, string> Datos, string? Motivo, string? Servicio);

    public Task Agregar(CorreoSaliente correo, int prioridad, DateTimeOffset proximoIntento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(correo);
        var categoria = Texto(correo.Categoria);
        var id = Guid.NewGuid();
        var contenido = cifrador.Cifrar(
            JsonSerializer.SerializeToUtf8Bytes(new Contenido(correo.Destinatario, new Dictionary<string, string>(correo.Datos), correo.Motivo, correo.Servicio)),
            id,
            $"{categoria}|{correo.Plantilla}");
        return EscrituraDePlataforma.Ejecutar(
            unidad,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO plataforma.correos_pendientes (id, categoria, prioridad, plantilla, contenido, proximo_intento, creada) VALUES (@id, @categoria, @prioridad, @plantilla, @contenido, @proximo, @creada)",
                    c,
                    t);
                comando.Parameters.AddWithValue("id", id);
                comando.Parameters.AddWithValue("categoria", categoria);
                comando.Parameters.AddWithValue("prioridad", (short)prioridad);
                comando.Parameters.AddWithValue("plantilla", correo.Plantilla);
                comando.Parameters.AddWithValue("contenido", contenido);
                comando.Parameters.AddWithValue("proximo", proximoIntento.ToUniversalTime());
                comando.Parameters.AddWithValue("creada", reloj.Ahora.ToUniversalTime());
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    /// <summary>Tras tantos intentos un correo deja de reintentarse y espera su purga (una dirección inválida no se reintenta para siempre).</summary>
    public const int MaximoDeIntentos = 20;

    /// <summary>Lo pendiente se conserva a lo sumo 30 días (la dirección cifrada incluida) y después se purga (<see cref="PurgarCaducados"/>).</summary>
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(30);

    /// <summary>Lo que no se puede descifrar se aparta un día para no ocupar el turno de las demás filas (<see cref="Vencidos"/>).</summary>
    public static readonly TimeSpan ApartadoDeLoIlegible = TimeSpan.FromDays(1);

    public Task<IReadOnlyList<CorreoPendiente>> Vencidos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        return EscrituraDePlataforma.Ejecutar<IReadOnlyList<CorreoPendiente>>(
            unidad,
            async (c, t, ct) =>
            {
                var correos = new List<CorreoPendiente>();
                var ilegibles = new List<Guid>();
                await using (var comando = new NpgsqlCommand(
                    "SELECT id, categoria, prioridad, plantilla, contenido, intentos, creada FROM plataforma.correos_pendientes WHERE proximo_intento <= @ahora AND intentos < @maximo ORDER BY prioridad, creada, id LIMIT @limite",
                    c,
                    t))
                {
                    comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                    comando.Parameters.AddWithValue("maximo", MaximoDeIntentos);
                    comando.Parameters.AddWithValue("limite", limite);
                    await using var filas = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await filas.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var id = filas.GetGuid(0);
                        try
                        {
                            var categoria = filas.GetString(1);
                            var plantilla = filas.GetString(3);
                            var contenido = JsonSerializer.Deserialize<Contenido>(cifrador.Descifrar((byte[])filas.GetValue(4), id, $"{categoria}|{plantilla}"))!;
                            correos.Add(new CorreoPendiente(
                                id,
                                new CorreoSaliente(Categoria(categoria), contenido.Destinatario, plantilla, contenido.Datos, contenido.Motivo, contenido.Servicio),
                                filas.GetInt16(2),
                                filas.GetInt32(5),
                                filas.GetFieldValue<DateTimeOffset>(6)));
                        }
                        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException)
                        {
                            ilegibles.Add(id);
                        }
                    }
                }

                // Una fila que no se descifra (clave cambiada, alterada) se aparta un día y cuenta un intento: no vuelve a ocupar el turno de las demás en cada pasada y acaba en la purga.
                foreach (var id in ilegibles)
                {
                    await using var apartar = new NpgsqlCommand("UPDATE plataforma.correos_pendientes SET intentos = intentos + 1, proximo_intento = @proximo WHERE id = @id", c, t);
                    apartar.Parameters.AddWithValue("id", id);
                    apartar.Parameters.AddWithValue("proximo", (ahora + ApartadoDeLoIlegible).ToUniversalTime());
                    await apartar.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                return correos;
            },
            cancelacion);
    }

    /// <summary>Purga lo que superó la retención de 30 días o los intentos máximos (lo ilegible, lo que nunca se pudo entregar); devuelve cuántas filas quitó. Lo llama el trabajo de fondo de Plataforma (T44).</summary>
    public Task<int> PurgarCaducados(DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        EscrituraDePlataforma.Ejecutar(
            unidad,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM plataforma.correos_pendientes WHERE creada < @limite OR intentos >= @maximo", c, t);
                comando.Parameters.AddWithValue("limite", (ahora - Retencion).ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", MaximoDeIntentos);
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);

    public Task Eliminar(Guid id, CancellationToken cancelacion = default) =>
        EscrituraDePlataforma.Ejecutar(
            unidad,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM plataforma.correos_pendientes WHERE id = @id", c, t);
                comando.Parameters.AddWithValue("id", id);
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    public Task Reprogramar(Guid id, int intentos, DateTimeOffset proximoIntento, CancellationToken cancelacion = default) =>
        EscrituraDePlataforma.Ejecutar(
            unidad,
            async (c, t, ct) =>
            {
                await using var comando = new NpgsqlCommand("UPDATE plataforma.correos_pendientes SET intentos = @intentos, proximo_intento = @proximo WHERE id = @id", c, t);
                comando.Parameters.AddWithValue("id", id);
                comando.Parameters.AddWithValue("intentos", intentos);
                comando.Parameters.AddWithValue("proximo", proximoIntento.ToUniversalTime());
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancelacion);

    private static string Texto(CategoriaDeCorreo categoria) => categoria switch
    {
        CategoriaDeCorreo.AlertaALaAdministracion => "alerta-a-la-administracion",
        CategoriaDeCorreo.AlertaCriticaDeCuenta => "alerta-critica-de-cuenta",
        CategoriaDeCorreo.RespuestaDeReclamo => "respuesta-de-reclamo",
        CategoriaDeCorreo.Pago => "pago",
        _ => throw new ArgumentOutOfRangeException(nameof(categoria), categoria, "Solo se encolan alertas, reclamos y pagos."),
    };

    private static CategoriaDeCorreo Categoria(string texto) => texto switch
    {
        "alerta-a-la-administracion" => CategoriaDeCorreo.AlertaALaAdministracion,
        "alerta-critica-de-cuenta" => CategoriaDeCorreo.AlertaCriticaDeCuenta,
        "respuesta-de-reclamo" => CategoriaDeCorreo.RespuestaDeReclamo,
        "pago" => CategoriaDeCorreo.Pago,
        _ => throw new ArgumentException("Categoría de correo guardada desconocida.", nameof(texto)),
    };
}
