using BabelHub.Inventario.Application.Ajustes.Interno;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Ajustes;

/// <summary>
/// Cómo entra la Infraestructura de <c>Ajustes</c> a su esquema: cada operación corre <b>dentro de la transacción de la unidad de trabajo</b> y con <c>SET LOCAL ROLE rol_ajustes</c> (<c>EjecutarComo</c>), con el alcance de datos
/// del actor (la política de filas de <c>ajustes</c> e <c>ingresos</c> es la segunda capa del alcance por sucursal) o, solo para la lectura de lo devuelto por pedido, con la elevación a la red (<c>ConAlcanceDeRed</c>, D09),
/// siempre después de que la Aplicación autorizó. Sin actor con contexto <b>falla cerrado</b>, como <c>AccesoAFuentes</c>: la política devolvería cero filas sin error. Una espera de bloqueo agotada (<c>55P03</c>) es
/// <see cref="EsperaDeBloqueoAgotadaException"/>, no un error del servidor. <c>PostgresException.Detail</c> (que trae la fila, con la nota) nunca se registra ni se propaga.
/// </summary>
internal sealed class AccesoAAjustes(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion, IElevacionDeAlcance elevacion, IActorActual actores)
{
    private const string EsperaAgotada = "55P03";

    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeAjustes.Esquema);

    public Task<T> ConAlcanceDelActor<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion) => Ejecutar(bloque, elevar: false, cancelacion);

    public Task<T> ConAlcanceDeRed<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion) => Ejecutar(bloque, elevar: true, cancelacion);

    private async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, bool elevar, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        if (actores.Actual is not { Tipo: not TipoDeActor.Anonimo })
        {
            throw new ContextoDeActorAusenteException("Se intentó acceder a los ajustes sin un actor con contexto: cada caso de uso autoriza antes y un trabajo de fondo corre como sistema.");
        }

        var conexionActiva = (NpgsqlConnection)conexion.Conexion;
        var transaccion = (NpgsqlTransaction)conexion.Transaccion;
        T resultado = default!;

        try
        {
            await elevacion.TraducirRechazoDeLaPolitica(async () =>
            {
                await unidad.EjecutarComo(
                    Esquema,
                    async token =>
                    {
                        resultado = elevar
                            ? await elevacion.ConAlcanceDeRed(t => bloque(conexionActiva, transaccion, t), token).ConfigureAwait(false)
                            : await bloque(conexionActiva, transaccion, token).ConfigureAwait(false);
                    },
                    cancelacion).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == EsperaAgotada)
        {
            throw new EsperaDeBloqueoAgotadaException(ex);
        }

        return resultado;
    }
}

/// <summary><see cref="IRepositorioDeAjustes"/> sobre <c>ajustes.ajustes</c>: SQL explícito, siempre con parámetros; solo inserción y lectura.</summary>
internal sealed class NpgsqlRepositorioDeAjustes(AccesoAAjustes acceso) : IRepositorioDeAjustes
{
    private const string ViolacionDeUnicidad = "23505";

    private const string Columnas = "id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, conteo_real, nota, usuario_id, fecha, devolucion_id, pedido_id";

    public Task Insertar(Ajuste ajuste, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ajuste);
        return acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                try
                {
                    await using var comando = new NpgsqlCommand(
                        $"INSERT INTO ajustes.ajustes ({Columnas}) VALUES (@id, @fuente, @edicion, @motivo, @anterior, @posterior, @conteo, @nota, @usuario, @fecha, @devolucion, @pedido)",
                        conexion,
                        transaccion);
                    comando.Parameters.AddWithValue("id", ajuste.Id);
                    comando.Parameters.AddWithValue("fuente", ajuste.FuenteId);
                    comando.Parameters.AddWithValue("edicion", ajuste.EdicionId);
                    comando.Parameters.AddWithValue("motivo", ajuste.Motivo.Identificador());
                    comando.Parameters.AddWithValue("anterior", ajuste.CantidadAnterior);
                    comando.Parameters.AddWithValue("posterior", ajuste.CantidadPosterior);
                    comando.Parameters.Add(new NpgsqlParameter("conteo", NpgsqlDbType.Integer) { Value = (object?)ajuste.ConteoReal ?? DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("nota", NpgsqlDbType.Text) { Value = (object?)ajuste.Nota ?? DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("usuario", NpgsqlDbType.Uuid) { Value = (object?)ajuste.UsuarioId ?? DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("fecha", NpgsqlDbType.TimestampTz) { Value = ajuste.Fecha.ToUniversalTime() });
                    comando.Parameters.Add(new NpgsqlParameter("devolucion", NpgsqlDbType.Uuid) { Value = (object?)ajuste.DevolucionId ?? DBNull.Value });
                    comando.Parameters.Add(new NpgsqlParameter("pedido", NpgsqlDbType.Uuid) { Value = (object?)ajuste.PedidoId ?? DBNull.Value });
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return 0;
                }
                catch (PostgresException ex) when (ex.SqlState == ViolacionDeUnicidad)
                {
                    // Nunca se propaga el Detail de la excepción de PostgreSQL: trae la fila, con la nota.
                    throw new AjusteDuplicadoException();
                }
            },
            cancelacion);
    }

    public Task<Ajuste?> PorId(Guid ajusteId, CancellationToken cancelacion = default) =>
        Una("id = @p", "p", ajusteId, cancelacion);

    public Task<Ajuste?> PorDevolucionYEdicion(Guid devolucionId, Guid edicionId, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDeRed(
            async (conexion, transaccion, token) =>
            {
                // La comprobación de que ya existe es de toda la red: el alcance de la sucursal que reingresa no debe ocultar el de otra (se compara la fuente después y es conflicto).
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM ajustes.ajustes WHERE motivo = 'devolucion' AND devolucion_id = @d AND edicion_id = @e", conexion, transaccion);
                comando.Parameters.AddWithValue("d", devolucionId);
                comando.Parameters.AddWithValue("e", edicionId);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? Leer(lector) : null;
            },
            cancelacion);

    public Task<PaginaDeAjustesGuardados> Historial(Guid fuenteId, int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var total = new NpgsqlCommand("SELECT count(*) FROM ajustes.ajustes WHERE fuente_id = @f", conexion, transaccion);
                total.Parameters.AddWithValue("f", fuenteId);
                var cuenta = (long)(await total.ExecuteScalarAsync(token).ConfigureAwait(false))!;

                await using var comando = new NpgsqlCommand(
                    $"SELECT {Columnas} FROM ajustes.ajustes WHERE fuente_id = @f ORDER BY fecha DESC, id DESC LIMIT @limite OFFSET @desde",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("f", fuenteId);
                comando.Parameters.AddWithValue("limite", tamano);
                comando.Parameters.AddWithValue("desde", (pagina - 1) * tamano);
                var elementos = new List<Ajuste>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    elementos.Add(Leer(lector));
                }

                return new PaginaDeAjustesGuardados(elementos, (int)Math.Min(cuenta, int.MaxValue));
            },
            cancelacion);

    public Task<IReadOnlyList<UnidadesReingresadas>> ReingresadasPorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(pedidoIds);
        return acceso.ConAlcanceDeRed<IReadOnlyList<UnidadesReingresadas>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    SELECT pedido_id, edicion_id, sum(cantidad_posterior - cantidad_anterior)::int
                    FROM ajustes.ajustes
                    WHERE motivo = 'devolucion' AND pedido_id = ANY (@pedidos)
                    GROUP BY pedido_id, edicion_id
                    ORDER BY pedido_id, edicion_id
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("pedidos", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pedidoIds.ToArray() });
                var resultado = new List<UnidadesReingresadas>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    resultado.Add(new UnidadesReingresadas(lector.GetGuid(0), lector.GetGuid(1), lector.GetInt32(2)));
                }

                return resultado;
            },
            cancelacion);
    }

    private Task<Ajuste?> Una(string condicion, string parametro, Guid valor, CancellationToken cancelacion) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM ajustes.ajustes WHERE {condicion}", conexion, transaccion);
                comando.Parameters.AddWithValue(parametro, valor);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? Leer(lector) : null;
            },
            cancelacion);

    private static Ajuste Leer(NpgsqlDataReader l) =>
        Ajuste.Rehidratar(
            l.GetGuid(0),
            l.GetGuid(1),
            l.GetGuid(2),
            MotivosDeAjuste.DeIdentificador(l.GetString(3)) ?? throw new InvalidOperationException("El motivo guardado no es de la lista."),
            l.GetInt32(4),
            l.GetInt32(5),
            l.IsDBNull(6) ? null : l.GetInt32(6),
            l.IsDBNull(7) ? null : l.GetString(7),
            l.IsDBNull(8) ? null : l.GetGuid(8),
            l.GetFieldValue<DateTimeOffset>(9),
            l.IsDBNull(10) ? null : l.GetGuid(10),
            l.IsDBNull(11) ? null : l.GetGuid(11));
}

/// <summary><see cref="IRepositorioDeIngresos"/> sobre <c>ajustes.ingresos</c>.</summary>
internal sealed class NpgsqlRepositorioDeIngresos(AccesoAAjustes acceso) : IRepositorioDeIngresos
{
    private const string Columnas = "id, fuente_id, edicion_id, cantidad, referencia, nota, usuario_id, fecha, ajuste_id";

    public Task Insertar(IngresoDeMercaderia ingreso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ingreso);
        return acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    $"INSERT INTO ajustes.ingresos ({Columnas}) VALUES (@id, @fuente, @edicion, @cantidad, @referencia, @nota, @usuario, @fecha, @ajuste)",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", ingreso.Id);
                comando.Parameters.AddWithValue("fuente", ingreso.FuenteId);
                comando.Parameters.AddWithValue("edicion", ingreso.EdicionId);
                comando.Parameters.AddWithValue("cantidad", ingreso.Cantidad);
                comando.Parameters.Add(new NpgsqlParameter("referencia", NpgsqlDbType.Text) { Value = (object?)ingreso.Referencia ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("nota", NpgsqlDbType.Text) { Value = (object?)ingreso.Nota ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("usuario", NpgsqlDbType.Uuid) { Value = (object?)ingreso.UsuarioId ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("fecha", NpgsqlDbType.TimestampTz) { Value = ingreso.Fecha.ToUniversalTime() });
                comando.Parameters.AddWithValue("ajuste", ingreso.AjusteId);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<IngresoDeMercaderia?> PorId(Guid ingresoId, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand($"SELECT {Columnas} FROM ajustes.ingresos WHERE id = @id", conexion, transaccion);
                comando.Parameters.AddWithValue("id", ingresoId);
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? Leer(lector) : null;
            },
            cancelacion);

    public Task<IReadOnlyList<IngresoDeMercaderia>> Ultimos(Guid? edicionId, int limite, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDeRed<IReadOnlyList<IngresoDeMercaderia>>(
            async (conexion, transaccion, token) =>
            {
                // La lista de ingresos es solo de la administración central (la Aplicación lo comprobó): lee toda la red.
                await using var comando = new NpgsqlCommand(
                    $"SELECT {Columnas} FROM ajustes.ingresos WHERE (@edicion::uuid IS NULL OR edicion_id = @edicion) ORDER BY fecha DESC, id DESC LIMIT @limite",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("edicion", NpgsqlDbType.Uuid) { Value = (object?)edicionId ?? DBNull.Value });
                comando.Parameters.AddWithValue("limite", limite);
                var resultado = new List<IngresoDeMercaderia>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    resultado.Add(Leer(lector));
                }

                return resultado;
            },
            cancelacion);

    private static IngresoDeMercaderia Leer(NpgsqlDataReader l) =>
        IngresoDeMercaderia.Rehidratar(
            l.GetGuid(0),
            l.GetGuid(1),
            l.GetGuid(2),
            l.GetInt32(3),
            l.IsDBNull(4) ? null : l.GetString(4),
            l.IsDBNull(5) ? null : l.GetString(5),
            l.IsDBNull(6) ? null : l.GetGuid(6),
            l.GetFieldValue<DateTimeOffset>(7),
            l.GetGuid(8));
}

/// <summary>
/// <see cref="IRepositorioDeOperacionesIdempotentes"/> sobre <c>ajustes.operaciones_idempotentes</c>: <c>INSERT … ON CONFLICT DO NOTHING</c>; la repetición concurrente <b>espera</b> a que la primera confirme (el índice único
/// bloquea) y entonces lee la fila existente (AJU-22). La tabla no lleva seguridad por filas: la clave se acota por usuario y es la Aplicación quien comprobó el alcance antes.
/// </summary>
internal sealed class NpgsqlRepositorioDeOperacionesIdempotentes(AccesoAAjustes acceso) : IRepositorioDeOperacionesIdempotentes
{
    public Task<OperacionIdempotenteGuardada?> Registrar(Guid usuarioId, string operacion, string clave, string huellaDeLaCarga, string resultado, CancellationToken cancelacion = default) =>
        acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using (var insertar = new NpgsqlCommand(
                    "INSERT INTO ajustes.operaciones_idempotentes (usuario_id, operacion, clave, huella_de_carga, resultado) VALUES (@u, @o, @c, @h, @r::jsonb) ON CONFLICT (usuario_id, operacion, clave) DO NOTHING",
                    conexion,
                    transaccion))
                {
                    insertar.Parameters.AddWithValue("u", usuarioId);
                    insertar.Parameters.AddWithValue("o", operacion);
                    insertar.Parameters.AddWithValue("c", clave);
                    insertar.Parameters.AddWithValue("h", huellaDeLaCarga);
                    insertar.Parameters.AddWithValue("r", resultado);
                    if (await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                    {
                        return null;
                    }
                }

                await using var leer = new NpgsqlCommand("SELECT huella_de_carga, resultado::text FROM ajustes.operaciones_idempotentes WHERE usuario_id = @u AND operacion = @o AND clave = @c", conexion, transaccion);
                leer.Parameters.AddWithValue("u", usuarioId);
                leer.Parameters.AddWithValue("o", operacion);
                leer.Parameters.AddWithValue("c", clave);
                await using var lector = await leer.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await lector.ReadAsync(token).ConfigureAwait(false) ? new OperacionIdempotenteGuardada(lector.GetString(0), lector.GetString(1)) : null;
            },
            cancelacion);
}
