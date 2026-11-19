using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>
/// El repositorio de las hojas y de la secuencia por año (T13, D10) sobre el esquema <c>reclamaciones</c>, con Npgsql directo y bajo <c>EjecutarComo(reclamaciones)</c> <b>dentro de la transacción en curso</b> (no abre otra: el registro, la auditoría y la cancelación de la cuenta son todo
/// o nada). Cada lectura «para actualizar» es <c>SELECT … FOR UPDATE</c> con <c>lock_timeout</c> de 5 s (vencido: <see cref="HojasNoDisponiblesException"/>). <b>Los datos personales viajan como parámetros y nunca en un mensaje de error</b> (<see cref="FallaDeLaBaseDeReclamacionesException"/>), y el
/// repositorio no escribe ninguna columna original: <see cref="Guardar"/> actualiza solo las de estado, respuesta, aviso y marca, que son las que el rol puede cambiar.
/// </summary>
internal sealed class RepositorioDeHojasPostgres(AccesoAlEsquemaDeReclamaciones acceso) : IRepositorioDeHojas
{
    private const string Columnas =
        "id, numero, tipo, nombre, documento, domicilio, correo, bien_contratado, monto_reclamado, detalle, pedido_referencia, contenido_hash, fecha_registro, clave_hash, estado, respuesta, respondida_en, respondida_por, "
        + "aviso_estado, aviso_enviado_en, aviso_en_curso_hasta, descartada_en, de_cuenta_cancelada, anonimizada_en";

    public Task<int> SiguienteSecuencia(int anio, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await AccesoAlEsquemaDeReclamaciones.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
                await using (var crear = new NpgsqlCommand("INSERT INTO reclamaciones.secuencias_de_hoja (anio, ultimo) VALUES (@anio, 0) ON CONFLICT DO NOTHING", conexion, transaccion))
                {
                    crear.Parameters.AddWithValue("anio", anio);
                    await crear.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await using var avanzar = new NpgsqlCommand("UPDATE reclamaciones.secuencias_de_hoja SET ultimo = ultimo + 1 WHERE anio = @anio RETURNING ultimo", conexion, transaccion);
                avanzar.Parameters.AddWithValue("anio", anio);
                return Convert.ToInt32(await avanzar.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            },
            cancelacion);

    public Task<ResultadoDeAgregado> Agregar(HojaDeReclamacion hoja, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(hoja);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // ON CONFLICT (clave_hash) DO NOTHING: la clave repetida no aborta la transacción y es la autoridad final de la idempotencia (REC-18); un número repetido sí falla.
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO reclamaciones.hojas_reclamacion (id, numero, anio, tipo, nombre, documento, domicilio, correo, correo_normalizado, bien_contratado, monto_reclamado, detalle, pedido_referencia, contenido_hash, fecha_registro, clave_hash, estado) "
                    + "VALUES (@id, @numero, @anio, @tipo, @nombre, @documento, @domicilio, @correo, @correoNormalizado, @bien, @monto, @detalle, @pedido, @contenido, @fecha, @clave, 'pendiente') ON CONFLICT (clave_hash) DO NOTHING",
                    conexion,
                    transaccion);
                Parametro(comando, "id", NpgsqlDbType.Uuid, hoja.Id);
                Parametro(comando, "numero", NpgsqlDbType.Varchar, hoja.Numero.ToString());
                Parametro(comando, "anio", NpgsqlDbType.Integer, hoja.Numero.Anio);
                Parametro(comando, "tipo", NpgsqlDbType.Text, hoja.Tipo.ATexto());
                Parametro(comando, "nombre", NpgsqlDbType.Text, hoja.Datos.Nombre);
                Parametro(comando, "documento", NpgsqlDbType.Text, hoja.Datos.Documento);
                Parametro(comando, "domicilio", NpgsqlDbType.Text, hoja.Datos.Domicilio);
                Parametro(comando, "correo", NpgsqlDbType.Text, hoja.Datos.Correo);
                Parametro(comando, "correoNormalizado", NpgsqlDbType.Text, hoja.Datos.CorreoNormalizado);
                Parametro(comando, "bien", NpgsqlDbType.Text, hoja.BienContratado);
                Parametro(comando, "monto", NpgsqlDbType.Numeric, hoja.MontoReclamado);
                Parametro(comando, "detalle", NpgsqlDbType.Text, hoja.Detalle);
                Parametro(comando, "pedido", NpgsqlDbType.Text, hoja.PedidoReferencia);
                Parametro(comando, "contenido", NpgsqlDbType.Text, hoja.ContenidoHash);
                Parametro(comando, "fecha", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDeReclamaciones.UtcONulo(hoja.FechaRegistro));
                Parametro(comando, "clave", NpgsqlDbType.Text, hoja.ClaveHash);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1 ? ResultadoDeAgregado.Agregada : ResultadoDeAgregado.ClaveRepetida;
            },
            cancelacion);
    }

    public Task<HojaDeReclamacion?> BuscarPorClave(IReadOnlyList<string> clavesHash, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clavesHash);
        if (clavesHash.Count == 0)
        {
            return Task.FromResult<HojaDeReclamacion?>(null);
        }

        return acceso.Ejecutar<HojaDeReclamacion?>(
            async (conexion, transaccion, token) =>
                (await Leer(conexion, transaccion, "clave_hash = ANY(@claves)", [new NpgsqlParameter("claves", clavesHash.ToArray())], " LIMIT 1", token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);
    }

    public Task<HojaDeReclamacion?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<HojaDeReclamacion?>(
            async (conexion, transaccion, token) =>
            {
                if (paraActualizar)
                {
                    await AccesoAlEsquemaDeReclamaciones.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
                }

                return (await Leer(conexion, transaccion, "id = @valor", [new NpgsqlParameter("valor", id)], paraActualizar ? " FOR UPDATE" : string.Empty, token).ConfigureAwait(false)).FirstOrDefault();
            },
            cancelacion);

    public Task Guardar(HojaDeReclamacion hoja, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(hoja);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    UPDATE reclamaciones.hojas_reclamacion SET
                        estado = @estado, respuesta = @respuesta, respondida_en = @respondidaEn, respondida_por = @respondidaPor, aviso_estado = @aviso, aviso_enviado_en = @avisoEnviadoEn,
                        aviso_en_curso_hasta = @avisoEnCursoHasta, descartada_en = @descartadaEn, de_cuenta_cancelada = @cancelada
                    WHERE id = @id
                    """,
                    conexion,
                    transaccion);
                Parametro(comando, "id", NpgsqlDbType.Uuid, hoja.Id);
                Parametro(comando, "estado", NpgsqlDbType.Text, EstadoATexto(hoja.Estado));
                Parametro(comando, "respuesta", NpgsqlDbType.Text, hoja.Respuesta);
                Parametro(comando, "respondidaEn", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDeReclamaciones.UtcONulo(hoja.RespondidaEn));
                Parametro(comando, "respondidaPor", NpgsqlDbType.Uuid, hoja.RespondidaPor);
                Parametro(comando, "aviso", NpgsqlDbType.Text, hoja.AvisoDeRespuesta is { } aviso ? AvisoATexto(aviso) : null);
                Parametro(comando, "avisoEnviadoEn", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDeReclamaciones.UtcONulo(hoja.AvisoEnviadoEn));
                Parametro(comando, "avisoEnCursoHasta", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDeReclamaciones.UtcONulo(hoja.AvisoEnCursoHasta));
                Parametro(comando, "descartadaEn", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDeReclamaciones.UtcONulo(hoja.DescartadaEn));
                Parametro(comando, "cancelada", NpgsqlDbType.Boolean, hoja.DeCuentaCancelada);
                if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                {
                    throw new InvalidOperationException("La hoja no existe.");
                }

                return 0;
            },
            cancelacion);
    }

    public Task<ListadoDeHojas> Listar(EstadoDeHoja? estado, int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var filtro = estado is { } e ? "estado = @estado" : "estado IN ('pendiente', 'respondido')";
                NpgsqlParameter[] parametros = estado is { } s ? [new NpgsqlParameter("estado", EstadoATexto(s))] : [];
                return await Paginar(conexion, transaccion, filtro, parametros, pagina, tamano, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<HojaDeReclamacion?> ObtenerPorNumero(NumeroDeHoja numero, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<HojaDeReclamacion?>(
            async (conexion, transaccion, token) => (await Leer(conexion, transaccion, "numero = @numero", [new NpgsqlParameter("numero", numero.ToString())], string.Empty, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<ListadoDeHojas> ListarDelCorreo(string correoNormalizado, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correoNormalizado);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                await Paginar(conexion, transaccion, "correo_normalizado = @correo AND NOT de_cuenta_cancelada", [new NpgsqlParameter("correo", correoNormalizado)], pagina, tamano, token).ConfigureAwait(false),
            cancelacion);
    }

    public Task<int> ContarPendientesDelCorreo(string correoNormalizado, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correoNormalizado);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE correo_normalizado = @correo AND estado = 'pendiente' AND NOT de_cuenta_cancelada", conexion, transaccion);
                comando.Parameters.AddWithValue("correo", correoNormalizado);
                return Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            },
            cancelacion);
    }

    public Task<int> MarcarDeCuentaCancelada(string correoNormalizado, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correoNormalizado);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("UPDATE reclamaciones.hojas_reclamacion SET de_cuenta_cancelada = true WHERE correo_normalizado = @correo AND NOT de_cuenta_cancelada", conexion, transaccion);
                comando.Parameters.AddWithValue("correo", correoNormalizado);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);
    }

    // ---------- Piezas ----------

    internal static string EstadoATexto(EstadoDeHoja estado) => estado switch
    {
        EstadoDeHoja.Pendiente => "pendiente",
        EstadoDeHoja.Respondido => "respondido",
        EstadoDeHoja.Descartado => "descartado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    internal static string AvisoATexto(AvisoDeRespuesta aviso) => aviso switch
    {
        AvisoDeRespuesta.Pendiente => "pendiente",
        AvisoDeRespuesta.Enviado => "enviado",
        _ => throw new ArgumentOutOfRangeException(nameof(aviso)),
    };

    private static void Parametro(NpgsqlCommand comando, string nombre, NpgsqlDbType tipo, object? valor) =>
        comando.Parameters.Add(new NpgsqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value });

    private static async Task<ListadoDeHojas> Paginar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, int pagina, int tamano, CancellationToken token)
    {
        int total;
        await using (var contar = new NpgsqlCommand($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE {filtro}", conexion, transaccion))
        {
            contar.Parameters.AddRange(parametros.Select(p => new NpgsqlParameter(p.ParameterName, p.Value)).ToArray());
            total = Convert.ToInt32(await contar.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        var hojas = await Leer(
            conexion,
            transaccion,
            filtro,
            [.. parametros, new NpgsqlParameter("tamano", tamano), new NpgsqlParameter("desde", (long)(pagina - 1) * tamano)],
            " ORDER BY fecha_registro DESC, id LIMIT @tamano OFFSET @desde",
            token).ConfigureAwait(false);
        return new ListadoDeHojas(hojas, total);
    }

    private static async Task<IReadOnlyList<HojaDeReclamacion>> Leer(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, string sufijo, CancellationToken token)
    {
        var hojas = new List<HojaDeReclamacion>();
        await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM reclamaciones.hojas_reclamacion WHERE {filtro}{sufijo}", conexion, transaccion);
        comando.Parameters.AddRange(parametros);
        await using var f = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await f.ReadAsync(token).ConfigureAwait(false))
        {
            if (!TipoDeHojaExtensiones.TryDesdeTexto(f.GetString(2), out var tipo) || !NumeroDeHoja.TryParse(f.GetString(1), out var numero))
            {
                throw new InvalidOperationException("El almacén de reclamaciones trae un valor desconocido.");
            }

            hojas.Add(HojaDeReclamacion.Rehidratar(
                f.GetGuid(0),
                numero,
                tipo,
                new DatosDelConsumidor(f.GetString(3), f.GetString(4), f.GetString(5), f.GetString(6)),
                f.GetString(7),
                f.GetDecimal(8),
                f.GetString(9),
                AccesoAlEsquemaDeReclamaciones.LeerTexto(f, 10),
                AccesoAlEsquemaDeReclamaciones.LeerTexto(f, 11),
                AccesoAlEsquemaDeReclamaciones.LeerInstante(f, 12)!.Value,
                f.GetString(13),
                f.GetString(14) switch
                {
                    "pendiente" => EstadoDeHoja.Pendiente,
                    "respondido" => EstadoDeHoja.Respondido,
                    "descartado" => EstadoDeHoja.Descartado,
                    _ => throw new InvalidOperationException("El almacén de reclamaciones trae un estado desconocido."),
                },
                AccesoAlEsquemaDeReclamaciones.LeerTexto(f, 15),
                AccesoAlEsquemaDeReclamaciones.LeerInstante(f, 16),
                f.IsDBNull(17) ? null : f.GetGuid(17),
                AccesoAlEsquemaDeReclamaciones.LeerTexto(f, 18) switch
                {
                    null => null,
                    "pendiente" => AvisoDeRespuesta.Pendiente,
                    "enviado" => AvisoDeRespuesta.Enviado,
                    _ => throw new InvalidOperationException("El almacén de reclamaciones trae un aviso desconocido."),
                },
                AccesoAlEsquemaDeReclamaciones.LeerInstante(f, 19),
                AccesoAlEsquemaDeReclamaciones.LeerInstante(f, 20),
                AccesoAlEsquemaDeReclamaciones.LeerInstante(f, 21),
                f.GetBoolean(22),
                AccesoAlEsquemaDeReclamaciones.LeerInstante(f, 23)));
        }

        return hojas;
    }
}

/// <summary>
/// La anonimización de las hojas de dos años (T13, REC-17): invoca la <b>función de base de datos</b> <c>reclamaciones.anonimizar_hojas()</c> sin parámetros, bajo el rol del esquema y <b>dentro de la transacción actual</b> (la misma que la auditoría); repite por lotes mientras devuelva un lote
/// lleno y suma lo anonimizado. La política (dos años por día de Lima) la aplica la función, no este adaptador.
/// </summary>
internal sealed class AnonimizadorDeHojasPostgres(AccesoAlEsquemaDeReclamaciones acceso) : IAnonimizadorDeHojas
{
    public const int TamanoDelLote = 5000;

    public Task<int> Anonimizar(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var total = 0;
                int lote;
                do
                {
                    await using var comando = new NpgsqlCommand("SELECT reclamaciones.anonimizar_hojas()", conexion, transaccion);
                    lote = Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                    total += lote;
                }
                while (lote >= TamanoDelLote);

                return total;
            },
            cancelacion);

    public Task<DateTimeOffset?> UltimaCorridaEn(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT ultima_en FROM reclamaciones.corridas_de_anonimizacion WHERE id = 1", conexion, transaccion);
                return await comando.ExecuteScalarAsync(token).ConfigureAwait(false) is DateTime instante ? (DateTimeOffset?)new DateTimeOffset(instante, TimeSpan.Zero) : null;
            },
            cancelacion);
}
