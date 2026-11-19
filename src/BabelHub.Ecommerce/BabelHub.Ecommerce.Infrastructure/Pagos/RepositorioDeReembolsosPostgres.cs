using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// El repositorio de los reembolsos de toda la plataforma (D08) sobre el esquema <c>pagos</c> (T24): la clave natural es única por origen (<c>reembolsos_clave_natural</c>, una violación sale como <see cref="FallaDeLaBaseDePagosException"/>
/// sin datos de la fila) y <see cref="Obtener"/> bloquea la fila con <c>FOR UPDATE</c> y <c>lock_timeout</c>. Los reintentos automáticos se preseleccionan en SQL con la misma espera creciente del dominio y el dominio decide al final
/// (<see cref="Reembolso.PuedeReintentarseAutomaticamente"/>).
/// </summary>
internal sealed class RepositorioDeReembolsosPostgres(AccesoAlEsquemaDePagos acceso) : IRepositorioDeReembolsos
{
    private const string Columnas = "id, origen, clave_natural, intencion_id, intento_id, monto, envio, estado, referencia_pasarela, intentos, ultimo_intento_en, motivo_pendiente, creado_en, resuelto_en";

    public Task<Reembolso?> Obtener(Guid reembolsoId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<Reembolso?>(
            async (conexion, transaccion, token) => (await Leer(conexion, transaccion, "id = @valor", [new NpgsqlParameter("valor", reembolsoId)], string.Empty, paraActualizar, token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<Reembolso?> BuscarPorOrigen(OrigenDeReembolso origen, string claveNatural, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<Reembolso?>(
            async (conexion, transaccion, token) => (await Leer(
                conexion,
                transaccion,
                "origen = @origen AND clave_natural = @clave",
                [new NpgsqlParameter("origen", TextosDePagos.A(origen)), new NpgsqlParameter("clave", claveNatural)],
                string.Empty,
                false,
                token).ConfigureAwait(false)).FirstOrDefault(),
            cancelacion);

    public Task<IReadOnlyList<Reembolso>> ListarDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) => await Leer(conexion, transaccion, "intencion_id = @valor", [new NpgsqlParameter("valor", intencionId)], " ORDER BY creado_en, id", false, token).ConfigureAwait(false),
            cancelacion);

    public Task Agregar(Reembolso reembolso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reembolso);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    $"INSERT INTO pagos.reembolsos ({Columnas}) VALUES (@id, @origen, @clave, @intencion, @intento, @monto, @envio, @estado, @referencia, @intentos, @ultimo, @motivo, @creado, @resuelto)",
                    conexion,
                    transaccion))
                {
                    Parametros(comando, reembolso, soloLoQueCambia: false);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                foreach (var linea in reembolso.Lineas)
                {
                    await using var comando = new NpgsqlCommand("INSERT INTO pagos.reembolso_lineas (reembolso_id, linea_id, monto) VALUES (@reembolso, @linea, @monto)", conexion, transaccion);
                    Parametro(comando, "reembolso", NpgsqlDbType.Uuid, reembolso.Id);
                    Parametro(comando, "linea", NpgsqlDbType.Uuid, linea.LineaId);
                    Parametro(comando, "monto", NpgsqlDbType.Numeric, linea.Monto.Valor);
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
    }

    public Task Guardar(Reembolso reembolso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reembolso);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "UPDATE pagos.reembolsos SET estado = @estado, referencia_pasarela = @referencia, intentos = @intentos, ultimo_intento_en = @ultimo, motivo_pendiente = @motivo, resuelto_en = @resuelto WHERE id = @id",
                    conexion,
                    transaccion);
                Parametros(comando, reembolso, soloLoQueCambia: true);
                if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                {
                    throw new InvalidOperationException("El reembolso no existe.");
                }

                return 0;
            },
            cancelacion);
    }

    public Task<int> ContarPendientesDeLaCuenta(Guid cuentaId, OrigenDeReembolso origen, bool sinLosDeCancelacionDeCuenta = false, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Una compensación por PAG-32 no difiere la cancelación: se excluye por su intento (la compensación es una por cobro).
                var exclusion = sinLosDeCancelacionDeCuenta
                    ? " AND NOT EXISTS (SELECT 1 FROM pagos.compensaciones c WHERE c.intento_id = r.intento_id AND c.motivo = 'cancelacion_de_cuenta_en_curso')"
                    : string.Empty;
                await using var comando = new NpgsqlCommand(
                    "SELECT count(*) FROM pagos.reembolsos r JOIN pagos.intenciones i ON i.id = r.intencion_id "
                    + "WHERE i.cuenta_id = @cuenta AND r.origen = @origen AND r.estado IN ('pendiente', 'solicitado')" + exclusion,
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("cuenta", cuentaId);
                comando.Parameters.AddWithValue("origen", TextosDePagos.A(origen));
                return Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            },
            cancelacion);

    public Task<(IReadOnlyList<Reembolso> Reembolsos, int Total)> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                int total;
                await using (var contar = new NpgsqlCommand("SELECT count(*) FROM pagos.reembolsos WHERE estado IN ('pendiente', 'solicitado')", conexion, transaccion))
                {
                    total = Convert.ToInt32(await contar.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                }

                var lista = await Leer(
                    conexion,
                    transaccion,
                    "estado IN ('pendiente', 'solicitado')",
                    [new NpgsqlParameter("tamano", tamano), new NpgsqlParameter("desde", (long)(pagina - 1) * tamano)],
                    " ORDER BY creado_en, id LIMIT @tamano OFFSET @desde",
                    false,
                    token).ConfigureAwait(false);
                return ((IReadOnlyList<Reembolso>)lista, total);
            },
            cancelacion);

    public Task<IReadOnlyList<Guid>> ListarHuerfanos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        ListarIdentificadores(
            "SELECT id FROM pagos.reembolsos WHERE estado = 'solicitado' AND ultimo_intento_en <= @corte ORDER BY ultimo_intento_en, id LIMIT @limite",
            [Instante("corte", ahora - Reembolso.PlazoDeHuerfano), new NpgsqlParameter("limite", limite)],
            cancelacion);

    public Task<IReadOnlyList<Guid>> ListarReintentables(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<Guid>>(
            async (conexion, transaccion, token) =>
            {
                // Preselección en SQL con la misma espera creciente que el dominio (base · 2^(intentos-1), tope de una hora); el dominio decide al final.
                var candidatos = await Leer(
                    conexion,
                    transaccion,
                    "estado = 'pendiente' AND intentos < @maximo AND (motivo_pendiente IS NULL OR motivo_pendiente = 'sin_respuesta') "
                    + "AND (ultimo_intento_en IS NULL OR ultimo_intento_en <= @ahora - (LEAST(@tope, @baseEspera * power(2, LEAST(GREATEST(intentos - 1, 0), 10))) * interval '1 second'))",
                    [
                        new NpgsqlParameter("maximo", Reembolso.MaximoDeReintentosAutomaticos),
                        new NpgsqlParameter("tope", Reembolso.EsperaMaximaEntreReintentos.TotalSeconds),
                        new NpgsqlParameter("baseEspera", Reembolso.EsperaTrasSinRespuesta.TotalSeconds),
                        Instante("ahora", ahora),
                        new NpgsqlParameter("limite", limite),
                    ],
                    " ORDER BY creado_en, id LIMIT @limite",
                    false,
                    token).ConfigureAwait(false);
                return candidatos.Where(r => r.PuedeReintentarseAutomaticamente(ahora)).Select(r => r.Id).ToArray();
            },
            cancelacion);

    // ---------- Piezas ----------

    private static NpgsqlParameter Instante(string nombre, DateTimeOffset valor) => new(nombre, NpgsqlDbType.TimestampTz) { Value = valor.ToUniversalTime() };

    private static void Parametro(NpgsqlCommand comando, string nombre, NpgsqlDbType tipo, object? valor) =>
        comando.Parameters.Add(new NpgsqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value });

    private static void Parametros(NpgsqlCommand comando, Reembolso r, bool soloLoQueCambia)
    {
        Parametro(comando, "id", NpgsqlDbType.Uuid, r.Id);
        Parametro(comando, "estado", NpgsqlDbType.Text, TextosDePagos.A(r.Estado));
        Parametro(comando, "referencia", NpgsqlDbType.Text, r.ReferenciaDePasarela);
        Parametro(comando, "intentos", NpgsqlDbType.Integer, r.Intentos);
        Parametro(comando, "ultimo", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(r.UltimoIntentoEn));
        Parametro(comando, "motivo", NpgsqlDbType.Text, TextosDePagos.A(r.MotivoPendiente));
        Parametro(comando, "resuelto", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(r.ResueltoEn));
        if (soloLoQueCambia)
        {
            return;
        }

        Parametro(comando, "origen", NpgsqlDbType.Text, TextosDePagos.A(r.Origen));
        Parametro(comando, "clave", NpgsqlDbType.Varchar, r.ClaveNatural);
        Parametro(comando, "intencion", NpgsqlDbType.Uuid, r.IntencionId);
        Parametro(comando, "intento", NpgsqlDbType.Uuid, r.IntentoId);
        Parametro(comando, "monto", NpgsqlDbType.Numeric, r.Monto.Valor);
        Parametro(comando, "envio", NpgsqlDbType.Numeric, r.Envio.Valor);
        Parametro(comando, "creado", NpgsqlDbType.TimestampTz, AccesoAlEsquemaDePagos.UtcONulo(r.CreadoEn));
    }

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

    private static async Task<IReadOnlyList<Reembolso>> Leer(
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

        var cabeceras = new List<(Guid Id, Func<IReadOnlyList<LineaDeReembolso>, Reembolso> Construir)>();
        await using (var comando = new NpgsqlCommand($"SELECT {Columnas} FROM pagos.reembolsos WHERE {filtro}{sufijo}{(paraActualizar ? " FOR UPDATE" : string.Empty)}", conexion, transaccion))
        {
            comando.Parameters.AddRange(parametros);
            await using var f = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await f.ReadAsync(token).ConfigureAwait(false))
            {
                var id = f.GetGuid(0);
                var origen = TextosDePagos.De<OrigenDeReembolso>(f.GetString(1));
                var clave = f.GetString(2);
                var intencion = f.GetGuid(3);
                var intento = f.GetGuid(4);
                var monto = Importe.De(f.GetDecimal(5));
                var envio = Importe.De(f.GetDecimal(6));
                var estado = TextosDePagos.De<EstadoDeReembolso>(f.GetString(7));
                var referencia = AccesoAlEsquemaDePagos.LeerTexto(f, 8);
                var intentos = f.GetInt32(9);
                var ultimo = AccesoAlEsquemaDePagos.LeerInstante(f, 10);
                var motivo = TextosDePagos.DeONulo<MotivoPendienteDeReembolso>(AccesoAlEsquemaDePagos.LeerTexto(f, 11));
                var creado = AccesoAlEsquemaDePagos.LeerInstante(f, 12)!.Value;
                var resuelto = AccesoAlEsquemaDePagos.LeerInstante(f, 13);
                cabeceras.Add((id, lineas => Reembolso.Rehidratar(id, origen, clave, intencion, intento, monto, lineas, envio, creado, estado, referencia, intentos, ultimo, motivo, resuelto)));
            }
        }

        var reembolsos = new List<Reembolso>(cabeceras.Count);
        foreach (var (id, construir) in cabeceras)
        {
            var lineas = new List<LineaDeReembolso>();
            await using var comando = new NpgsqlCommand("SELECT linea_id, monto FROM pagos.reembolso_lineas WHERE reembolso_id = @reembolso ORDER BY linea_id", conexion, transaccion);
            comando.Parameters.AddWithValue("reembolso", id);
            await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await filas.ReadAsync(token).ConfigureAwait(false))
            {
                lineas.Add(new LineaDeReembolso(filas.GetGuid(0), Importe.De(filas.GetDecimal(1))));
            }

            reembolsos.Add(construir(lineas));
        }

        return reembolsos;
    }
}
