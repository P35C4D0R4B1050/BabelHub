using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Migracion;

/// <summary>Un paso del plan de migración: un nombre estable (lo único que se imprime) y su SQL idempotente.</summary>
public sealed record PasoDeMigracion(string Nombre, string Sql);

/// <summary>
/// Una cuenta de conexión del servicio (la de la aplicación o una de depuración) con los roles de esquema de los que es miembro. <b>La clave nunca se imprime</b>. La cuenta de la aplicación es <c>NOINHERIT</c> (solo escribe con
/// <c>SET LOCAL ROLE</c> dentro de una transacción, AD18); la de depuración hereda (ejecuta su función sin cambiar de rol).
/// </summary>
public sealed record CuentaDeBase(string Usuario, string Clave, IReadOnlyList<string> Roles, bool Hereda = false)
{
    public override string ToString() => $"[cuenta {Usuario}]";
}

/// <summary>El plan de un servicio: su nombre, los pasos en orden, los esquemas cuya propiedad pasa al rol propietario y las cuentas que se crean o se actualizan.</summary>
public sealed record PlanDeMigracion(string Servicio, IReadOnlyList<PasoDeMigracion> Pasos, IReadOnlyList<string> EsquemasDelPropietario, IReadOnlyList<CuentaDeBase> Cuentas);

/// <summary>La migración no pudo completarse: el mensaje nombra el paso, el rol o la variable, nunca un valor secreto ni un dato.</summary>
public sealed class MigracionFallidaException : Exception
{
    public MigracionFallidaException(string message)
        : base(message)
    {
    }

    public MigracionFallidaException()
        : this("La migración falló.")
    {
    }

    public MigracionFallidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// El migrador de esquemas y roles de PostgreSQL (COMP-2; PLA-04, AD18, SEG-11): con la <b>cuenta propietaria</b> (administración de la base, solo durante la migración; nunca la de la aplicación) aplica en orden el SQL idempotente de los módulos del
/// servicio, deja la propiedad de lo creado en un rol propietario sin login y sin privilegios, y crea o actualiza las cuentas de conexión del servicio con sus roles. <b>Es repetible sin daño</b> (todo el SQL es idempotente y las cuentas solo
/// actualizan su contraseña), <b>serializa</b> las corridas con un candado y <b>falla cerrado</b>: la cuenta debe ser administradora, un rol que falte o un paso que falle detienen la corrida nombrándolo. No imprime claves, cadenas de conexión ni datos.
/// </summary>
public sealed class MigradorDeLaBase(NpgsqlDataSource propietaria, TextWriter salida)
{
    /// <summary>El candado de la corrida: un solo migrador a la vez por base.</summary>
    private const long CandadoDeMigracion = 0x62616265_6c687562;

    public async Task<int> Ejecutar(PlanDeMigracion plan, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var propietario = "propietario_" + NombreSeguro(plan.Servicio);

        NpgsqlConnection conexion;
        try
        {
            conexion = await propietaria.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        {
            throw new MigracionFallidaException("No se pudo abrir la cuenta propietaria (revisa la variable de la cadena de conexión).", e);
        }

        await using (conexion)
        {
            await ExigirAdministracion(conexion, cancelacion).ConfigureAwait(false);

            // La contraseña de las cuentas viaja en una sentencia SQL: el servidor no debe registrar sentencias, errores con su texto ni parámetros en esta sesión (solo un superusuario puede cambiarlo).
            await Ejecutar(conexion, "sin-registro-de-sentencias", "SET log_statement = 'none'; SET log_min_error_statement = 'panic'; SET log_parameter_max_length = 0; SET log_parameter_max_length_on_error = 0;", cancelacion).ConfigureAwait(false);
            if (!await Escalar<bool>(conexion, $"SELECT pg_try_advisory_lock({CandadoDeMigracion})", cancelacion).ConfigureAwait(false))
            {
                throw new MigracionFallidaException("Hay otra migración en curso sobre esta base.");
            }

            try
            {
                await Ejecutar(conexion, "propietario", CrearElPropietario(propietario), cancelacion).ConfigureAwait(false);
                foreach (var paso in plan.Pasos)
                {
                    await Ejecutar(conexion, paso.Nombre, paso.Sql, cancelacion).ConfigureAwait(false);
                }

                if (plan.EsquemasDelPropietario.Count > 0)
                {
                    await Ejecutar(conexion, "propiedad", PasarLaPropiedad(propietario, plan.EsquemasDelPropietario), cancelacion).ConfigureAwait(false);
                }

                // Las cuentas de conexión son del clúster, no de la base: dos servicios sobre una misma instancia se pisarían la contraseña si se llamaran igual. Cada cuenta lleva el nombre de su servicio, y solo sus cuentas pueden conectarse a su base (ni siquiera «public»: así una cuenta de otro servicio no llega a ella ni usando un rol de esquema del mismo nombre).
                var prefijoDeCuentas = NombreSeguro(plan.Servicio) + "_";
                var ajenas = plan.Cuentas.Where(c => !c.Usuario.StartsWith(prefijoDeCuentas, StringComparison.Ordinal)).Select(c => c.Usuario).ToList();
                if (ajenas.Count > 0)
                {
                    throw new MigracionFallidaException($"Las cuentas de {plan.Servicio} deben llamarse «{prefijoDeCuentas}…» para no pisar las de otro servicio en la misma instancia: {string.Join(", ", ajenas)}.");
                }

                await Ejecutar(conexion, "conexion-solo-de-sus-cuentas", "DO $r$ BEGIN EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database()); END $r$;", cancelacion).ConfigureAwait(false);
                foreach (var cuenta in plan.Cuentas)
                {
                    await AplicarCuenta(conexion, cuenta, cancelacion).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await Escalar<bool>(conexion, $"SELECT pg_advisory_unlock({CandadoDeMigracion})", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // La conexión se perdió: el candado de sesión se libera solo al cerrarla.
                }
            }
        }

        await salida.WriteLineAsync($"migracion: {plan.Servicio} al día ({plan.Pasos.Count} pasos, {plan.Cuentas.Count} cuentas)").ConfigureAwait(false);
        return 0;
    }

    private static async Task ExigirAdministracion(NpgsqlConnection conexion, CancellationToken cancelacion)
    {
        if (!await Escalar<bool>(conexion, "SELECT rolsuper FROM pg_roles WHERE rolname = current_user", cancelacion).ConfigureAwait(false))
        {
            throw new MigracionFallidaException("La cuenta propietaria debe tener privilegios de administración de la base (crea roles, extensiones y la propiedad de los objetos); la de la aplicación no sirve.");
        }
    }

    private static async Task<T> Escalar<T>(NpgsqlConnection conexion, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        var valor = await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false);
        return (T)valor!;
    }

    /// <summary>Un paso en su propia transacción: o se aplica entero o no cambia nada. El error nombra el paso y el código de PostgreSQL, nunca el SQL ni los datos.</summary>
    private async Task Ejecutar(NpgsqlConnection conexion, string nombre, string sql, CancellationToken cancelacion)
    {
        await using var transaccion = await conexion.BeginTransactionAsync(cancelacion).ConfigureAwait(false);
        try
        {
            await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
            await transaccion.CommitAsync(cancelacion).ConfigureAwait(false);
        }
        catch (PostgresException e)
        {
            await RevertirSinFallar(transaccion).ConfigureAwait(false);
            throw new MigracionFallidaException($"El paso «{nombre}» falló (SQLSTATE {e.SqlState}).", e);
        }
        catch
        {
            await RevertirSinFallar(transaccion).ConfigureAwait(false);
            throw;
        }

        await salida.WriteLineAsync($"paso: {nombre}").ConfigureAwait(false);
    }

    private static async Task RevertirSinFallar(NpgsqlTransaction transaccion)
    {
        try
        {
            await transaccion.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // La conexión ya se perdió: nada que revertir.
        }
    }

    private static string CrearElPropietario(string propietario) => $$"""
        DO $p$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{propietario}}') THEN
                CREATE ROLE {{propietario}} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
            END IF;
            ALTER ROLE {{propietario}} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
        END
        $p$;
        """;

    /// <summary>
    /// Deja en el rol propietario lo que creó la cuenta de administración en los esquemas indicados (tablas, vistas y funciones; las secuencias siguen a su tabla). Lo que un módulo ya asignó a su propio propietario
    /// (<c>auditoria_migraciones</c>, <c>*_dueno</c>) no se toca. Idempotente.
    /// </summary>
    private static string PasarLaPropiedad(string propietario, IReadOnlyList<string> esquemas)
    {
        var lista = string.Join(", ", esquemas.Select(e => $"'{NombreSeguro(e)}'"));
        return $$"""
            DO $o$
            DECLARE
                r record;
            BEGIN
                FOR r IN SELECT nspname FROM pg_namespace WHERE nspname IN ({{lista}}) AND pg_get_userbyid(nspowner) = current_user LOOP
                    EXECUTE format('ALTER SCHEMA %I OWNER TO {{propietario}}', r.nspname);
                END LOOP;
                FOR r IN SELECT n.nspname, c.relname, c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname IN ({{lista}}) AND c.relkind IN ('r', 'p', 'v', 'm') AND pg_get_userbyid(c.relowner) = current_user LOOP
                    EXECUTE format('ALTER %s %I.%I OWNER TO {{propietario}}', CASE r.relkind WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'TABLE' END, r.nspname, r.relname);
                END LOOP;
                FOR r IN SELECT p.oid::regprocedure AS firma FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                         WHERE n.nspname IN ({{lista}}) AND p.prokind = 'f' AND pg_get_userbyid(p.proowner) = current_user LOOP
                    EXECUTE format('ALTER FUNCTION %s OWNER TO {{propietario}}', r.firma);
                END LOOP;
            END
            $o$;
            """;
    }

    /// <summary>
    /// Crea la cuenta si no existe y le pone la contraseña del entorno (repetirlo es rotarla), le da CONNECT y la hace miembro de sus roles. Si falta alguno de los roles se detiene nombrándolo. El SQL con la contraseña se arma
    /// <b>en el servidor</b> con <c>format(%L)</c> (comillado seguro) y no se imprime.
    /// </summary>
    private async Task AplicarCuenta(NpgsqlConnection conexion, CuentaDeBase cuenta, CancellationToken cancelacion)
    {
        if (string.IsNullOrEmpty(cuenta.Clave) || !UsuarioValido(cuenta.Usuario))
        {
            throw new MigracionFallidaException($"La cuenta «{cuenta.Usuario}» no tiene un nombre válido o le falta la contraseña (revisa la variable de su cadena de conexión).");
        }

        await using (var existentes = new NpgsqlCommand("SELECT rolname FROM pg_roles WHERE rolname = ANY(@roles)", conexion))
        {
            existentes.Parameters.AddWithValue("roles", cuenta.Roles.Distinct(StringComparer.Ordinal).ToArray());
            var presentes = new HashSet<string>(StringComparer.Ordinal);
            await using (var lector = await existentes.ExecuteReaderAsync(cancelacion).ConfigureAwait(false))
            {
                while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
                {
                    presentes.Add(lector.GetString(0));
                }
            }

            var faltan = cuenta.Roles.Where(r => !presentes.Contains(r)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (faltan.Count > 0)
            {
                throw new MigracionFallidaException($"Falta el rol {string.Join(", ", faltan)} para la cuenta «{cuenta.Usuario}»: ningún paso lo creó.");
            }
        }

        var herencia = cuenta.Hereda ? "INHERIT" : "NOINHERIT";
        string sql;
        await using (var armar = new NpgsqlCommand(
            $$"""
            SELECT format('DO $c$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = %L) THEN CREATE ROLE %I; END IF; END $c$; ALTER ROLE %I LOGIN {{herencia}} NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 20 PASSWORD %L; GRANT CONNECT ON DATABASE %I TO %I;',
                          @u, @u, @u, @p, current_database(), @u)
            """,
            conexion))
        {
            armar.Parameters.AddWithValue("u", cuenta.Usuario);
            armar.Parameters.AddWithValue("p", cuenta.Clave);
            sql = (string)(await armar.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
        }

        var concesiones = string.Concat(cuenta.Roles.Distinct(StringComparer.Ordinal).Select(r => $"GRANT {NombreSeguro(r)} TO {NombreSeguro(cuenta.Usuario)};\n"));
        await Ejecutar(conexion, $"cuenta {cuenta.Usuario}", sql + "\n" + concesiones, cancelacion).ConfigureAwait(false);
    }

    private static bool UsuarioValido(string usuario) => usuario.Length is >= 3 and <= 63 && usuario.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') && char.IsAsciiLetterLower(usuario[0]);

    /// <summary>Un identificador que se concatena al SQL: solo minúsculas, dígitos y guion bajo (los nombres salen del código, nunca de un dato).</summary>
    private static string NombreSeguro(string nombre) =>
        nombre.Length is >= 1 and <= 63 && nombre.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
            ? nombre
            : throw new ArgumentException("Un nombre de rol o esquema son minúsculas, dígitos y guion bajo.", nameof(nombre));
}
