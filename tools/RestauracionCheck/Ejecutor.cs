using System.Data;
using System.Globalization;
using Npgsql;

namespace BabelHub.Tools.RestauracionCheck;

/// <summary>
/// La comprobación de una restauración en el ensayo (PLA-36, PLA-40; AC-PLA-11, AC-PLA-40). Solo lectura: se conecta a las dos bases restauradas
/// con el rol <c>restauracion-check</c> (que existe solo en las bases del ensayo, nunca en producción), en transacciones de solo lectura y con un
/// solo cuadro de datos por base (<c>REPEATABLE READ</c>). Imprime **solo contadores** (nunca identificadores, datos personales ni cadenas de
/// conexión) y sale con 0 si todo cumple, 1 si alguna comprobación falla y 2 si el uso es inválido.
/// </summary>
public static class Ejecutor
{
    /// <summary>El único rol con el que se comprueba: de solo lectura y creado solo en las bases restauradas del ensayo.</summary>
    public const string RolDeComprobacion = "restauracion-check";

    /// <summary>Una reserva confirmada hace menos de esto, al instante de referencia, todavía puede no tener su pedido (PLA-40).</summary>
    public static readonly TimeSpan MargenDeConfirmacion = TimeSpan.FromMinutes(10);

    public const string Ayuda = """
        Uso:
          RestauracionCheck --bases <cadena-ecommerce> <cadena-inventario> --instante <AAAA-MM-DDThh:mm:ssZ>
                            --salud-ecommerce <url> --salud-inventario <url>
                            [--tabla-pedidos <t>] [--tabla-lineas <t>] [--tabla-ediciones <t>] [--tabla-reservas <t>]
        Las cadenas de conexión también pueden ir en las variables RESTAURACION_CHECK_ECOMMERCE y RESTAURACION_CHECK_INVENTARIO (preferible: así
        no viajan en los argumentos del proceso). Se conecta como el rol «restauracion-check», de solo lectura, que existe solo en las bases
        restauradas del ensayo. --instante es el instante del respaldo restaurado (UTC). Las direcciones de salud son las completas
        (por ejemplo http://127.0.0.1:8080/health/ready). Códigos: 0 todo cumple, 1 alguna comprobación falla, 2 uso inválido.
        """;

    public static async Task<int> EjecutarAsync(string[] args, TextWriter salida, TextWriter errores, CancellationToken cancelacion, HttpMessageHandler? manejador = null, Func<string, string?>? entorno = null)
    {
        var (opciones, error) = Opciones.Interpretar(args, entorno ?? Environment.GetEnvironmentVariable);
        if (opciones is null)
        {
            await errores.WriteLineAsync($"Error: {error}").ConfigureAwait(false);
            await errores.WriteLineAsync(Ayuda).ConfigureAwait(false);
            return 2;
        }

        var informe = new Informe(salida);
        using var cliente = manejador is null ? new HttpClient() : new HttpClient(manejador, disposeHandler: false);
        cliente.Timeout = TimeSpan.FromSeconds(10);

        await ComprobarSaludAsync(informe, cliente, "ecommerce", opciones.SaludEcommerce, cancelacion).ConfigureAwait(false);
        await ComprobarSaludAsync(informe, cliente, "inventario", opciones.SaludInventario, cancelacion).ConfigureAwait(false);

        var ecommerce = await AbrirAsync(informe, "ecommerce", opciones.BaseEcommerce, cancelacion).ConfigureAwait(false);
        var inventario = await AbrirAsync(informe, "inventario", opciones.BaseInventario, cancelacion).ConfigureAwait(false);
        try
        {
            var rolEcommerce = ecommerce is not null && await ComprobarRolAsync(informe, "ecommerce", ecommerce, cancelacion).ConfigureAwait(false);
            var rolInventario = inventario is not null && await ComprobarRolAsync(informe, "inventario", inventario, cancelacion).ConfigureAwait(false);

            // Con un rol con permisos de más no se consulta nada más: la comprobación es de solo lectura o no es.
            if (ecommerce is not null && inventario is not null && rolEcommerce && rolInventario)
            {
                await ComprobarMismoInstanteAsync(informe, ecommerce, inventario, cancelacion).ConfigureAwait(false);
                await ComprobarPedidosYReservasAsync(informe, ecommerce, inventario, opciones, cancelacion).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ecommerce is not null)
            {
                await ecommerce.DisposeAsync().ConfigureAwait(false);
            }

            if (inventario is not null)
            {
                await inventario.DisposeAsync().ConfigureAwait(false);
            }
        }

        return await informe.TerminarAsync().ConfigureAwait(false);
    }

    // ---------- Salud de los dos servicios (PLA-36) ----------

    private static async Task ComprobarSaludAsync(Informe informe, HttpClient cliente, string servicio, Uri direccion, CancellationToken cancelacion)
    {
        try
        {
            using var respuesta = await cliente.GetAsync(direccion, cancelacion).ConfigureAwait(false);
            if (respuesta.IsSuccessStatusCode)
            {
                await informe.OkAsync($"salud-{servicio}", $"responde {(int)respuesta.StatusCode}").ConfigureAwait(false);
            }
            else
            {
                await informe.FallaAsync($"salud-{servicio}", $"responde {(int)respuesta.StatusCode} y debe responder 200").ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            await informe.FallaAsync($"salud-{servicio}", $"no responde ({ex.GetType().Name})").ConfigureAwait(false);
        }
    }

    // ---------- Conexión de solo lectura ----------

    private static async Task<NpgsqlConnection?> AbrirAsync(Informe informe, string nombre, string cadena, CancellationToken cancelacion)
    {
        NpgsqlConnection? conexion = null;
        try
        {
            var constructor = new NpgsqlConnectionStringBuilder(cadena)
            {
                // Sin esquema por defecto (solo pg_catalog): una tabla sin esquema no puede ser secuestrada por un esquema de la base restaurada.
                Options = "-c default_transaction_read_only=on -c search_path=pg_catalog",
                Timeout = 10,
                CommandTimeout = 300,
                Pooling = false,
                ApplicationName = "restauracion-check",
            };
            conexion = new NpgsqlConnection(constructor.ConnectionString);
            await conexion.OpenAsync(cancelacion).ConfigureAwait(false);
            await informe.OkAsync($"conexion-{nombre}", "base abierta en solo lectura").ConfigureAwait(false);
            return conexion;
        }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or InvalidOperationException or TimeoutException or IOException)
        {
            if (conexion is not null)
            {
                await conexion.DisposeAsync().ConfigureAwait(false);
            }

            // Nunca se repite el texto de la excepción: puede llevar el servidor o el usuario.
            await informe.FallaAsync($"conexion-{nombre}", $"no se pudo abrir la base ({ex.GetType().Name})").ConfigureAwait(false);
            return null;
        }
    }

    private static async Task<NpgsqlTransaction> IniciarAsync(NpgsqlConnection conexion, CancellationToken cancelacion)
    {
        var transaccion = await conexion.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancelacion).ConfigureAwait(false);
        await using var orden = new NpgsqlCommand("SET TRANSACTION READ ONLY", conexion, transaccion);
        await orden.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        return transaccion;
    }

    private static async Task<T> EscalarAsync<T>(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, CancellationToken cancelacion)
    {
        await using var orden = new NpgsqlCommand(sql, conexion, transaccion);
        var valor = await orden.ExecuteScalarAsync(cancelacion).ConfigureAwait(false);
        return (T)Convert.ChangeType(valor!, typeof(T), CultureInfo.InvariantCulture);
    }

    // ---------- El rol: de solo lectura y sin permisos de más ----------

    private static async Task<bool> ComprobarRolAsync(Informe informe, string nombre, NpgsqlConnection conexion, CancellationToken cancelacion)
    {
        try
        {
            await using var transaccion = await IniciarAsync(conexion, cancelacion).ConfigureAwait(false);
            var actual = await EscalarAsync<string>(conexion, transaccion, "SELECT current_user::text", cancelacion).ConfigureAwait(false);
            var problemas = new List<string>();
            if (!string.Equals(actual, RolDeComprobacion, StringComparison.Ordinal))
            {
                problemas.Add($"el rol conectado no es «{RolDeComprobacion}»");
            }

            if (await EscalarAsync<bool>(conexion, transaccion, "SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls FROM pg_roles WHERE rolname = current_user", cancelacion).ConfigureAwait(false))
            {
                problemas.Add("el rol tiene atributos de administración");
            }

            var escribibles = await EscalarAsync<long>(
                conexion,
                transaccion,
                "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND (has_table_privilege(c.oid, 'INSERT') OR has_table_privilege(c.oid, 'UPDATE') OR has_table_privilege(c.oid, 'DELETE') OR has_table_privilege(c.oid, 'TRUNCATE'))",
                cancelacion).ConfigureAwait(false);
            if (escribibles > 0)
            {
                problemas.Add($"el rol puede escribir en {escribibles} tabla(s)");
            }

            var creables = await EscalarAsync<long>(
                conexion,
                transaccion,
                "SELECT (SELECT count(*) FROM pg_namespace n WHERE n.nspname NOT LIKE 'pg\\_%' AND n.nspname <> 'information_schema' AND has_schema_privilege(n.oid, 'CREATE')) + (CASE WHEN has_database_privilege(current_database(), 'CREATE') THEN 1 ELSE 0 END)",
                cancelacion).ConfigureAwait(false);
            if (creables > 0)
            {
                problemas.Add("el rol puede crear objetos");
            }

            // Los roles predefinidos que dan acceso al servidor (ejecutar programas, leer o escribir archivos del servidor).
            if (await EscalarAsync<bool>(conexion, transaccion, "SELECT COALESCE(bool_or(pg_has_role(current_user, r, 'MEMBER')), false) FROM unnest(ARRAY['pg_execute_server_program', 'pg_write_server_files', 'pg_read_server_files']) AS r", cancelacion).ConfigureAwait(false))
            {
                problemas.Add("el rol es miembro de un rol con acceso al servidor (programas o archivos)");
            }

            if (problemas.Count == 0)
            {
                await informe.OkAsync($"rol-{nombre}", "solo lectura, sin permisos de más").ConfigureAwait(false);
                return true;
            }
            else
            {
                await informe.FallaAsync($"rol-{nombre}", string.Join("; ", problemas)).ConfigureAwait(false);
                return false;
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidCastException)
        {
            await informe.FallaAsync($"rol-{nombre}", $"no se pudo comprobar el rol ({ex.GetType().Name})").ConfigureAwait(false);
            return false;
        }
    }

    // ---------- Las dos bases al mismo instante (PLA-36) ----------

    private static async Task ComprobarMismoInstanteAsync(Informe informe, NpgsqlConnection ecommerce, NpgsqlConnection inventario, CancellationToken cancelacion)
    {
        try
        {
            var a = await HuellaDelClusterAsync(ecommerce, cancelacion).ConfigureAwait(false);
            var b = await HuellaDelClusterAsync(inventario, cancelacion).ConfigureAwait(false);
            var problemas = new List<string>();
            if (a.Sistema != b.Sistema)
            {
                problemas.Add("no son del mismo clúster restaurado (identificador de sistema distinto): una copia por base no está al mismo instante");
            }

            if (a.Linea != b.Linea)
            {
                problemas.Add("distinta línea de tiempo");
            }

            if (a.Arranque != b.Arranque)
            {
                problemas.Add("no se abrieron en el mismo arranque del servidor");
            }

            if (a.EnRecuperacion || b.EnRecuperacion)
            {
                problemas.Add("alguna base sigue en recuperación: la restauración no terminó");
            }

            if (problemas.Count == 0)
            {
                await informe.OkAsync("mismo-instante", "las dos bases son del mismo clúster, la misma línea de tiempo y el mismo arranque, sin recuperación pendiente").ConfigureAwait(false);
            }
            else
            {
                await informe.FallaAsync("mismo-instante", string.Join("; ", problemas)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidCastException)
        {
            await informe.FallaAsync("mismo-instante", $"no se pudo comprobar ({ex.GetType().Name})").ConfigureAwait(false);
        }
    }

    private static async Task<(string Sistema, string Linea, string Arranque, bool EnRecuperacion)> HuellaDelClusterAsync(NpgsqlConnection conexion, CancellationToken cancelacion)
    {
        await using var transaccion = await IniciarAsync(conexion, cancelacion).ConfigureAwait(false);
        await using var orden = new NpgsqlCommand(
            "SELECT (SELECT system_identifier::text FROM pg_control_system()), (SELECT timeline_id::text FROM pg_control_checkpoint()), pg_postmaster_start_time()::text, pg_is_in_recovery()",
            conexion,
            transaccion);
        await using var lector = await orden.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        await lector.ReadAsync(cancelacion).ConfigureAwait(false);
        return (lector.GetString(0), lector.GetString(1), lector.GetString(2), lector.GetBoolean(3));
    }

    // ---------- Pedidos y reservas (PLA-40) ----------

    private static async Task ComprobarPedidosYReservasAsync(Informe informe, NpgsqlConnection ecommerce, NpgsqlConnection inventario, Opciones opciones, CancellationToken cancelacion)
    {
        var pedidos = NombresDeTablas.Citar(opciones.Tablas.Pedidos);
        var lineas = NombresDeTablas.Citar(opciones.Tablas.LineasDePedido);
        var ediciones = NombresDeTablas.Citar(opciones.Tablas.Ediciones);
        var reservas = NombresDeTablas.Citar(opciones.Tablas.Reservas);
        var limite = opciones.Instante - MargenDeConfirmacion;
        try
        {
            // Inventario: las reservas de cualquier estado por (pedido, edición) y las confirmadas hace más de 10 minutos.
            var reservadas = new HashSet<(string Pedido, string Edicion)>();
            var confirmadasAntiguas = new List<string>();
            await using (var transaccion = await IniciarAsync(inventario, cancelacion).ConfigureAwait(false))
            {
                await using (var todas = new NpgsqlCommand($"SELECT pedido_id::text, edicion_id::text FROM {reservas} WHERE pedido_id IS NOT NULL", inventario, transaccion))
                await using (var lector = await todas.ExecuteReaderAsync(cancelacion).ConfigureAwait(false))
                {
                    while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
                    {
                        reservadas.Add((lector.GetString(0), lector.GetString(1)));
                    }
                }

                await using var antiguas = new NpgsqlCommand($"SELECT pedido_id::text FROM {reservas} WHERE estado = 'confirmada' AND fecha_confirmacion <= @limite AND pedido_id IS NOT NULL", inventario, transaccion);
                antiguas.Parameters.AddWithValue("limite", NpgsqlTypes.NpgsqlDbType.TimestampTz, limite);
                await using var lectorAntiguas = await antiguas.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
                while (await lectorAntiguas.ReadAsync(cancelacion).ConfigureAwait(false))
                {
                    confirmadasAntiguas.Add(lectorAntiguas.GetString(0));
                }
            }

            // E-commerce: las líneas impresas de pedidos no anulados y todos los pedidos que existen.
            long lineasImpresas = 0;
            long lineasSinReserva = 0;
            var pedidosExistentes = new HashSet<string>();
            await using (var transaccion = await IniciarAsync(ecommerce, cancelacion).ConfigureAwait(false))
            {
                await using (var impresas = new NpgsqlCommand($"SELECT l.pedido_id::text, l.edicion_id::text FROM {lineas} l JOIN {pedidos} p ON p.id = l.pedido_id JOIN {ediciones} e ON e.id = l.edicion_id WHERE p.estado <> 'anulado' AND e.formato = 'impreso'", ecommerce, transaccion))
                await using (var lector = await impresas.ExecuteReaderAsync(cancelacion).ConfigureAwait(false))
                {
                    while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
                    {
                        lineasImpresas++;
                        if (!reservadas.Contains((lector.GetString(0), lector.GetString(1))))
                        {
                            lineasSinReserva++;
                        }
                    }
                }

                await using var existentes = new NpgsqlCommand($"SELECT id::text FROM {pedidos}", ecommerce, transaccion);
                await using var lectorExistentes = await existentes.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
                while (await lectorExistentes.ReadAsync(cancelacion).ConfigureAwait(false))
                {
                    pedidosExistentes.Add(lectorExistentes.GetString(0));
                }
            }

            var reservasSinPedido = confirmadasAntiguas.LongCount(id => !pedidosExistentes.Contains(id));

            await informe.ContadorAsync("lineas_impresas_de_pedidos_no_anulados", lineasImpresas).ConfigureAwait(false);
            await informe.ContadorAsync("lineas_impresas_sin_reserva", lineasSinReserva).ConfigureAwait(false);
            await informe.ContadorAsync("reservas_confirmadas_hace_mas_de_10_min", confirmadasAntiguas.Count).ConfigureAwait(false);
            await informe.ContadorAsync("reservas_confirmadas_sin_pedido", reservasSinPedido).ConfigureAwait(false);

            if (lineasSinReserva == 0)
            {
                await informe.OkAsync("pedidos-con-reservas", "todo pedido no anulado con líneas impresas referencia reservas que existen").ConfigureAwait(false);
            }
            else
            {
                await informe.FallaAsync("pedidos-con-reservas", $"{lineasSinReserva} línea(s) impresa(s) de pedidos no anulados no tienen reserva en el Inventario").ConfigureAwait(false);
            }

            if (reservasSinPedido == 0)
            {
                await informe.OkAsync("reservas-con-pedido", "toda reserva confirmada hace más de 10 minutos referencia un pedido que existe").ConfigureAwait(false);
            }
            else
            {
                await informe.FallaAsync("reservas-con-pedido", $"{reservasSinPedido} reserva(s) confirmada(s) hace más de 10 minutos no tienen pedido").ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidCastException)
        {
            // Una tabla o columna que no existe es la causa más probable: el código de error de PostgreSQL ayuda y no lleva datos.
            var detalle = ex is PostgresException postgres ? $"{ex.GetType().Name} {postgres.SqlState}" : ex.GetType().Name;
            await informe.FallaAsync("pedidos-y-reservas", $"no se pudo comprobar ({detalle}); revisa los nombres de las tablas (--tabla-*)").ConfigureAwait(false);
        }
    }

    /// <summary>El informe: una línea por comprobación y los contadores; nunca identificadores ni datos personales.</summary>
    private sealed class Informe(TextWriter salida)
    {
        private int _fallos;

        public Task OkAsync(string nombre, string detalle) => salida.WriteLineAsync($"ok {nombre}: {detalle}");

        public Task ContadorAsync(string nombre, long valor) => salida.WriteLineAsync($"contador {nombre}={valor.ToString(CultureInfo.InvariantCulture)}");

        public Task FallaAsync(string nombre, string detalle)
        {
            _fallos++;
            return salida.WriteLineAsync($"FALLA {nombre}: {detalle}");
        }

        public async Task<int> TerminarAsync()
        {
            await salida.WriteLineAsync(_fallos == 0 ? "Restauración comprobada: todo cumple." : $"Restauración NO comprobada: {_fallos} comprobación(es) fallaron.").ConfigureAwait(false);
            return _fallos == 0 ? 0 : 1;
        }
    }
}
