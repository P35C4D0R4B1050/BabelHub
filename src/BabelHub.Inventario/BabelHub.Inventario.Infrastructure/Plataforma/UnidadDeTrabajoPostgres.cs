using System.Data;
using System.Data.Common;
using BabelHub.Inventario.Application.Plataforma;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Opciones de la unidad de trabajo sobre PostgreSQL.</summary>
public sealed record OpcionesDeUnidadDeTrabajo
{
    /// <summary>Prefijo del rol de cada esquema: el rol del esquema <c>identidad</c> es <c>rol_identidad</c> (lo crean las migraciones, AD18).</summary>
    public string PrefijoDelRolDeEsquema { get; init; } = "rol_";

    /// <summary>
    /// Espera máxima por cualquier bloqueo de la transacción (<c>SET LOCAL lock_timeout</c>): filas de stock, líneas de reserva y esperas de unicidad. Sin tope, una transacción colgada sosteniendo la fila de una edición
    /// popular dejaría a todas las demás esperando con su conexión hasta agotar el pool. Pasada la espera, PostgreSQL responde <c>55P03</c> y la Infraestructura del módulo lo traduce a
    /// <c>EsperaDeBloqueoAgotadaException</c>. Los bloqueos asesores fijan su propia espera mientras se toman y la restauran a este valor.
    /// </summary>
    public TimeSpan EsperaMaximaDeBloqueos { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Unidad de trabajo sobre PostgreSQL: una transacción por caso de uso (SEG-04) y, con <see cref="EjecutarComo"/>, la escritura de
/// un módulo bajo el rol de su esquema (AD18). La aplicación se conecta con un rol de acceso <c>NOINHERIT</c> miembro de los roles
/// de esquema: por sí solo no puede leer ningún esquema, y <c>SET LOCAL ROLE</c> le da el permiso de un módulo solo mientras
/// dure el bloque, así una operación entre módulos es una sola transacción sin ampliar el permiso de ninguno.
/// La conexión pertenece al flujo asíncrono que abrió la transacción: no debe usarse desde tareas paralelas.
/// </summary>
public sealed class UnidadDeTrabajoPostgres(NpgsqlDataSource origen, OpcionesDeUnidadDeTrabajo? opciones = null, IActorActual? actor = null)
    : IUnidadDeTrabajo, IConexionDeLaUnidadDeTrabajo, ILecturaCoherente, IElevacionDeAlcance
{
    private const string TransaccionAbortada = "25P02";

    private readonly OpcionesDeUnidadDeTrabajo _opciones = opciones ?? new OpcionesDeUnidadDeTrabajo();
    private readonly AsyncLocal<Activa?> _activa = new();

    public bool HayTransaccion => _activa.Value is not null;

    public DbConnection Conexion => (_activa.Value ?? throw SinTransaccion()).Conexion;

    public DbTransaction Transaccion => (_activa.Value ?? throw SinTransaccion()).Transaccion;

    public bool HayAlcanceDeDatosFijado => !string.IsNullOrEmpty((_activa.Value ?? throw SinTransaccion()).Alcance);

    public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) =>
        EjecutarEn(IsolationLevel.ReadCommitted, soloLectura: false, trabajo, cancelacion);

    /// <summary>Una transacción de solo lectura con <c>REPEATABLE READ</c>: todo lo que se lee dentro ve la misma instantánea (D06). Si ya hay una transacción, se une a ella.</summary>
    public Task<T> EjecutarLectura<T>(Func<CancellationToken, Task<T>> lectura, CancellationToken cancelacion = default) =>
        EjecutarEn(IsolationLevel.RepeatableRead, soloLectura: true, lectura, cancelacion);

    private async Task<T> EjecutarEn<T>(IsolationLevel aislamiento, bool soloLectura, Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(trabajo);

        // Un caso de uso que llama a otro dentro de su transacción se une a ella: todo se confirma o todo se revierte.
        if (_activa.Value is not null)
        {
            return await trabajo(cancelacion).ConfigureAwait(false);
        }

        await using var conexion = await origen.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        await using var transaccion = await conexion.BeginTransactionAsync(aislamiento, cancelacion).ConfigureAwait(false);
        var activa = new Activa(conexion, transaccion);
        _activa.Value = activa;
        try
        {
            // Sin puerto del actor, o con el puerto pero sin actor establecido, no se fija valor y las políticas de filas deniegan (falla cerrada, SEG-15).
            await PrepararTransaccion(activa, soloLectura, AlcanceDeDatosDelActor.Para(actor?.Actual), cancelacion).ConfigureAwait(false);

            var resultado = await trabajo(cancelacion).ConfigureAwait(false);
            await transaccion.CommitAsync(cancelacion).ConfigureAwait(false);
            return resultado;
        }
        catch
        {
            await RevertirSinFallar(transaccion).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _activa.Value = null;
        }
    }

    public async Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(esquema);
        ArgumentNullException.ThrowIfNull(bloque);

        var activa = _activa.Value ?? throw new InvalidOperationException("EjecutarComo exige una transacción activa.");

        // El rol vigente lo lleva la propia unidad de trabajo (es la única que lo cambia): se lee una vez al abrir la transacción, no antes de cada bloque.
        var previo = activa.Rol;
        var destino = _opciones.PrefijoDelRolDeEsquema + esquema.Nombre;
        if (previo == destino)
        {
            // Un bloque anidado del mismo esquema ya corre con ese rol: no hay nada que cambiar ni que restaurar.
            await bloque(cancelacion).ConfigureAwait(false);
            return;
        }

        await EjecutarSql(activa, $"SET LOCAL ROLE {Citar(destino)}", cancelacion).ConfigureAwait(false);
        activa.Rol = destino;
        try
        {
            await bloque(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            await RestaurarRol(activa, previo!).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Implementación explícita: solo se llama por la interfaz interna <see cref="IElevacionDeAlcance"/>, de modo que la prueba de arquitectura
    /// ve quién la usa. Con <c>red</c> las políticas de filas ven todas las filas mientras dure el bloque, y al salir vuelve el valor del actor.
    /// </summary>
    async Task<T> IElevacionDeAlcance.ConAlcanceDeRed<T>(Func<CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        var activa = _activa.Value ?? throw new InvalidOperationException("ConAlcanceDeRed exige una transacción activa.");
        var previo = activa.Alcance;

        await FijarAlcance(activa, AlcanceDeDatosDelActor.Red, cancelacion).ConfigureAwait(false);
        try
        {
            return await bloque(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // Si el actor no tenía valor, queda vacío (no coincide con ninguna sucursal ni con «red»): las políticas deniegan.
                await FijarAlcance(activa, string.IsNullOrEmpty(previo) ? string.Empty : previo, CancellationToken.None).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == TransaccionAbortada)
            {
                // La transacción ya falló: el valor vuelve solo al revertirla, y no se enmascara el error original del bloque.
            }
        }
    }

    /// <summary>Traduce el rechazo de la política de filas (ver <see cref="IElevacionDeAlcance.TraducirRechazoDeLaPolitica{T}"/>).</summary>
    Task<T> IElevacionDeAlcance.TraducirRechazoDeLaPolitica<T>(Func<Task<T>> operacion) => RechazoDeLaPolitica.Traducir(operacion);

    /// <summary>
    /// Fija el alcance de datos solo para esta transacción (<c>set_config(…, true)</c>, parametrizado): no se escapa a otra conexión del pool.
    /// Un valor nulo no lo fija, y las políticas de filas deniegan.
    /// </summary>
    private static async Task FijarAlcance(Activa activa, string? valor, CancellationToken cancelacion)
    {
        if (valor is null)
        {
            return;
        }

        await using var comando = new NpgsqlCommand("SELECT set_config(@nombre, @valor, true)", activa.Conexion, activa.Transaccion);
        comando.Parameters.AddWithValue("nombre", AlcanceDeDatosDelActor.Parametro);
        comando.Parameters.AddWithValue("valor", valor);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        activa.Alcance = valor;
    }

    /// <summary>
    /// Todo lo que una transacción necesita antes de su primer caso de uso, <b>en una sola ida a la base</b>: solo lectura si corresponde, la espera máxima de bloqueos (<c>lock_timeout</c>),
    /// el alcance de datos del actor (<c>set_config(…, true)</c>: vale solo para esta transacción) y el rol con el que se abrió la sesión, que la unidad lleva en memoria desde entonces.
    /// </summary>
    private async Task PrepararTransaccion(Activa activa, bool soloLectura, string? alcance, CancellationToken cancelacion)
    {
        var espera = ((long)Math.Ceiling(_opciones.EsperaMaximaDeBloqueos.TotalMilliseconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sql = (soloLectura ? "SET TRANSACTION READ ONLY; " : string.Empty)
            + "SELECT current_user, set_config('lock_timeout', @espera, true)"
            + (alcance is null ? string.Empty : ", set_config(@nombre, @valor, true)");

        await using var comando = new NpgsqlCommand(sql, activa.Conexion, activa.Transaccion);
        comando.Parameters.AddWithValue("espera", espera);
        if (alcance is not null)
        {
            comando.Parameters.AddWithValue("nombre", AlcanceDeDatosDelActor.Parametro);
            comando.Parameters.AddWithValue("valor", alcance);
        }

        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        do
        {
            if (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                activa.Rol = lector.GetString(0);
                activa.Alcance = alcance;
                return;
            }
        }
        while (await lector.NextResultAsync(cancelacion).ConfigureAwait(false));

        throw new InvalidOperationException("La base no devolvió el rol de la sesión al preparar la transacción.");
    }

    private static InvalidOperationException SinTransaccion() => new("No hay una unidad de trabajo activa.");

    /// <summary>PostgreSQL entre comillas dobles, para que ningún nombre de rol se interprete como SQL.</summary>
    private static string Citar(string identificador) => "\"" + identificador.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static async Task RestaurarRol(Activa activa, string previo)
    {
        // El rol lógico vuelve al del bloque que contiene al que termina, aunque la base ya no pueda confirmarlo (la transacción abortada se revierte entera).
        activa.Rol = previo;
        try
        {
            // SET LOCAL ROLE <previo> y no RESET ROLE: un EjecutarComo anidado debe volver al rol del bloque que lo contiene.
            await EjecutarSql(activa, $"SET LOCAL ROLE {Citar(previo)}", CancellationToken.None).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == TransaccionAbortada)
        {
            // La transacción ya falló: el rol vuelve solo al revertirla, y no se enmascara el error original del bloque.
        }
    }

    private static async Task RevertirSinFallar(NpgsqlTransaction transaccion)
    {
        try
        {
            await transaccion.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            // Si la conexión ya se perdió, PostgreSQL revierte la transacción al cerrarse la sesión.
        }
    }

    private static async Task EjecutarSql(Activa activa, string sql, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, activa.Conexion, activa.Transaccion);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    /// <summary>La transacción en curso y lo que la unidad de trabajo fijó en ella: es la única que cambia el rol y el alcance de datos, así que sabe cuáles rigen sin preguntarle a la base.</summary>
    private sealed class Activa(NpgsqlConnection conexion, NpgsqlTransaction transaccion)
    {
        public NpgsqlConnection Conexion { get; } = conexion;

        public NpgsqlTransaction Transaccion { get; } = transaccion;

        /// <summary>El rol vigente (<c>current_user</c>): el de la sesión al abrir, y el de un esquema dentro de <see cref="EjecutarComo"/>.</summary>
        public string? Rol { get; set; }

        /// <summary>El valor de <c>babelhub.alcance</c> fijado en esta transacción, o <see langword="null"/> si no se fijó (las políticas de filas deniegan).</summary>
        public string? Alcance { get; set; }
    }
}
