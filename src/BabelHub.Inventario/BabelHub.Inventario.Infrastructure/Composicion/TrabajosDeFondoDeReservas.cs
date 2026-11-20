using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// Un caso de uso de mantenimiento respondió «no autenticado» o «prohibido»: el trabajo corrió sin el actor «sistema» (o con otro). Un trabajo que perdió su actor <b>falla a la vista</b> en lugar de no hacer nada
/// (el acceso a las reservas falla cerrado sin actor, y el alcance de datos del stock devuelve cero filas sin error: hallazgo del 2026-11-14).
/// </summary>
public sealed class ActorDeTrabajoDeFondoAusenteException : InvalidOperationException
{
    public ActorDeTrabajoDeFondoAusenteException()
        : base("El trabajo de fondo corrió sin el actor «sistema»: debe ejecutarse con EjecutorDeTrabajoDeFondo.")
    {
    }

    public ActorDeTrabajoDeFondoAusenteException(string message)
        : base(message)
    {
    }

    public ActorDeTrabajoDeFondoAusenteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Bloqueo asesor de <b>instancia única</b> de un trabajo (<c>pg_try_advisory_lock</c> de sesión sobre una conexión propia que dura la corrida): dos instancias no corren el mismo trabajo a la vez y, si el proceso muere, la base lo
/// libera al cerrarse la sesión. Quien no lo obtiene (<see langword="null"/>) omite la corrida: otra la está haciendo.
/// </summary>
public sealed class BloqueoDeInstanciaUnica(NpgsqlDataSource origen)
{
    public async Task<IAsyncDisposable?> Intentar(string nombre, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        var conexion = await origen.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        try
        {
            await using var comando = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@nombre, 0))", conexion);
            comando.Parameters.AddWithValue("nombre", "reservas.trabajo|" + nombre);
            if ((bool)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!)
            {
                return new Tomado(conexion, nombre);
            }
        }
        catch
        {
            // Cancelada entre que la base tomó el bloqueo y llegó la respuesta: no se sabe si quedó tomado, y el grupo no reinicia la sesión; se cierra la conexión física.
            NpgsqlConnection.ClearPool(conexion);
            await conexion.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await conexion.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private sealed class Tomado(NpgsqlConnection conexion, string nombre) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var comando = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@nombre, 0))", conexion);
                comando.Parameters.AddWithValue("nombre", "reservas.trabajo|" + nombre);
                await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (NpgsqlException)
            {
                // Si la conexión se perdió, la base ya liberó el bloqueo al cerrarse la sesión. Si la conexión sigue viva pero no se pudo desbloquear (por ejemplo, un `statement_timeout`), el bloqueo de sesión
                // sobreviviría en el grupo, que no reinicia la sesión: se cierra la conexión física en lugar de devolverla.
                NpgsqlConnection.ClearPool(conexion);
            }
            finally
            {
                await conexion.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Cómo terminó una corrida de un trabajo.</summary>
public enum ResultadoDeCorrida
{
    Exitosa,

    /// <summary>Otra instancia tiene el bloqueo del trabajo: esta no hace nada (y no registra nada).</summary>
    OmitidaPorOtraInstancia,

    Fallida,
}

/// <summary>
/// Un trabajo de fondo de <c>Reservas</c> (T25): cada corrida toma el <b>bloqueo de instancia única</b>, corre <b>como «sistema»</b> por <see cref="EjecutorDeTrabajoDeFondo"/> (un trabajo sin actor es un defecto, no un
/// permiso), registra el resultado enumerado en <c>plataforma.trabajos_de_fondo</c> y, si pasan 24 horas sin una corrida exitosa, publica una alerta técnica (<c>trabajo-de-fondo-detenido</c>, AUD-31) una vez por plazo.
/// Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// </summary>
public sealed class TrabajoDeFondoDeReservas
{
    /// <summary>El motivo del catálogo de alertas técnicas para un trabajo detenido.</summary>
    public const string MotivoDeTrabajoDetenido = "trabajo-de-fondo-detenido";

    /// <summary>Las horas sin una corrida exitosa que disparan la alerta (AUD-31).</summary>
    public static readonly TimeSpan PlazoSinCorrida = TimeSpan.FromHours(24);

    private readonly Func<IServiceScopeFactory, CancellationToken, Task> _corrida;
    private readonly IServiceScopeFactory _fabrica;
    private readonly EjecutorDeTrabajoDeFondo _ejecutor;
    private readonly BloqueoDeInstanciaUnica _bloqueo;
    private readonly IRegistroDeTrabajosDeFondo _registro;
    private readonly IReloj _reloj;
    private readonly TimeSpan _intervalo;
    private readonly TimeSpan _plazoSinCorrida;
    private readonly TimeProvider _tiempo;

    private DateTimeOffset _ultimaExitosa;
    private DateTimeOffset? _ultimaAlerta;

    public TrabajoDeFondoDeReservas(
        string nombre,
        TimeSpan intervalo,
        Func<IServiceScopeFactory, CancellationToken, Task> corrida,
        IServiceScopeFactory fabrica,
        EjecutorDeTrabajoDeFondo ejecutor,
        BloqueoDeInstanciaUnica bloqueo,
        IRegistroDeTrabajosDeFondo registro,
        IReloj reloj,
        TimeProvider? tiempo = null,
        TimeSpan? plazoSinCorrida = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(corrida);
        Nombre = nombre;
        _intervalo = intervalo;
        _corrida = corrida;
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _ejecutor = ejecutor ?? throw new ArgumentNullException(nameof(ejecutor));
        _bloqueo = bloqueo ?? throw new ArgumentNullException(nameof(bloqueo));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
        _plazoSinCorrida = plazoSinCorrida ?? PlazoSinCorrida;
        _tiempo = tiempo ?? TimeProvider.System;

        // Desde que arranca el servicio cuenta el plazo: un trabajo que nunca logra correr también avisa a las 24 horas.
        _ultimaExitosa = reloj.Ahora;
    }

    public string Nombre { get; }

    /// <summary>Ejecuta la corrida ahora, con el bloqueo, como «sistema» y registrada. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task<ResultadoDeCorrida> EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            await using var tomado = await _bloqueo.Intentar(Nombre, cancelacion).ConfigureAwait(false);
            if (tomado is null)
            {
                return ResultadoDeCorrida.OmitidaPorOtraInstancia;
            }

            await _ejecutor.Ejecutar(
                async token =>
                {
                    await _corrida(_fabrica, token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);

            _ultimaExitosa = _reloj.Ahora;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
            return ResultadoDeCorrida.Exitosa;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            await AlertarSiCorresponde(cancelacion).ConfigureAwait(false);
            return ResultadoDeCorrida.Fallida;
        }
    }

    /// <summary>Corre hasta que se cancele: una corrida al empezar y otra en cada intervalo.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(_intervalo, _tiempo);
        try
        {
            do
            {
                await EjecutarUnaVez(cancelacion).ConfigureAwait(false);
            }
            while (await temporizador.WaitForNextTickAsync(cancelacion).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado del servicio.
        }
    }

    private async Task Registrar(ResultadoDeTrabajo resultado, CancellationToken cancelacion)
    {
        try
        {
            // El registro escribe en el esquema de plataforma como «sistema»: una corrida propia del ejecutor.
            await _ejecutor.Ejecutar(
                async token =>
                {
                    await _registro.Registrar(Nombre, resultado, _reloj.Ahora, token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El registro de la corrida es informativo: si falla, el trabajo sigue.
        }
    }

    private async Task AlertarSiCorresponde(CancellationToken cancelacion)
    {
        var ahora = _reloj.Ahora;
        if (ahora - _ultimaExitosa < _plazoSinCorrida || (_ultimaAlerta is { } anterior && ahora - anterior < _plazoSinCorrida))
        {
            return;
        }

        try
        {
            await _ejecutor.Ejecutar(
                async token =>
                {
                    using var alcance = _fabrica.CreateScope();
                    await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarTecnica(new CodigoDeAlerta(MotivoDeTrabajoDetenido), token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);
            _ultimaAlerta = ahora;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>Los trabajos de fondo de <c>Reservas</c> con sus nombres estables, intervalos y corridas (T25; SEL-08, SEG-25, plan §5).</summary>
public static class TrabajosDeReservas
{
    public const string Expiracion = "reservas-expiracion";

    public const string PurgaDeHuellas = "reservas-purga-de-huellas";

    public const string PurgaDeRegistros = "reservas-purga-de-registros";

    public const string DepuracionDiaria = "reservas-depuracion-diaria";

    /// <summary>La expiración exacta barre cada 30 segundos (además de la que cada operación hace de lo suyo, SEL-27).</summary>
    public static readonly TimeSpan IntervaloDeLaExpiracion = TimeSpan.FromSeconds(30);

    /// <summary>La huella de origen vive 24 horas y se purga cada 15 minutos.</summary>
    public static readonly TimeSpan IntervaloDeLaPurgaDeHuellas = TimeSpan.FromMinutes(15);

    /// <summary>Las operaciones idempotentes y las bajas consumidas se purgan cada hora (su plazo es de 30 días).</summary>
    public static readonly TimeSpan IntervaloDeLaPurgaDeRegistros = TimeSpan.FromHours(1);

    public static readonly TimeSpan IntervaloDeLaDepuracion = TimeSpan.FromHours(24);

    public static TrabajoDeFondoDeReservas Expirar(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, Expiracion, IntervaloDeLaExpiracion, ExpirarVencidas, tiempo);

    public static TrabajoDeFondoDeReservas PurgarHuellas(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, PurgaDeHuellas, IntervaloDeLaPurgaDeHuellas, async (fabrica, ct) => await Caso<PurgarHuellasDeOrigenEntrada, ResultadoDeMantenimiento>(fabrica, new PurgarHuellasDeOrigenEntrada(), ct).ConfigureAwait(false), tiempo);

    public static TrabajoDeFondoDeReservas PurgarRegistros(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, PurgaDeRegistros, IntervaloDeLaPurgaDeRegistros, async (fabrica, ct) => await Caso<PurgarRegistrosCaducadosEntrada, ResultadoDeMantenimiento>(fabrica, new PurgarRegistrosCaducadosEntrada(), ct).ConfigureAwait(false), tiempo);

    public static TrabajoDeFondoDeReservas Depurar(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, DepuracionDiaria, IntervaloDeLaDepuracion, DepurarAntiguas, tiempo);

    private static TrabajoDeFondoDeReservas Crear(IServiceProvider sp, string nombre, TimeSpan intervalo, Func<IServiceScopeFactory, CancellationToken, Task> corrida, TimeProvider? tiempo) =>
        new(
            nombre,
            intervalo,
            corrida,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<EjecutorDeTrabajoDeFondo>(),
            sp.GetRequiredService<BloqueoDeInstanciaUnica>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            sp.GetRequiredService<IReloj>(),
            tiempo);

    /// <summary>
    /// Busca los pedidos con líneas vencidas y expira cada uno con su propio caso de uso, <b>su propia transacción y su propio bloqueo</b> (nunca uno grande): un pedido ocupado se omite y el próximo ciclo lo reintenta.
    /// Un rechazo por actor aborta la corrida; cualquier otro fallo se cuenta y la corrida termina con error después de procesar los demás.
    /// </summary>
    private static async Task ExpirarVencidas(IServiceScopeFactory fabrica, CancellationToken cancelacion)
    {
        var candidatos = await Caso<BuscarPedidosConVencidasEntrada, PedidosConVencidas>(fabrica, new BuscarPedidosConVencidasEntrada(PlazosDeReservas.PedidosPorCorrida), cancelacion).ConfigureAwait(false);

        var fallidos = 0;
        foreach (var pedido in candidatos.PedidoIds)
        {
            cancelacion.ThrowIfCancellationRequested();
            using var alcance = fabrica.CreateScope();
            var resultado = await alcance.ServiceProvider.GetRequiredService<ICasoDeUso<ExpirarReservasEntrada, ExpiracionDelPedido>>().Ejecutar(new ExpirarReservasEntrada(pedido), cancelacion).ConfigureAwait(false);
            if (resultado.EsExito || resultado.Error!.Clave == ClavesDelTrabajo.BloqueoOcupado)
            {
                continue;
            }

            ExigirActor(resultado.Error);
            fallidos++;
        }

        if (fallidos > 0)
        {
            throw new InvalidOperationException($"La expiración no pudo procesar {fallidos} pedido(s).");
        }
    }

    private static async Task DepurarAntiguas(IServiceScopeFactory fabrica, CancellationToken cancelacion) =>
        await Caso<DepurarReservasAntiguasEntrada, ResultadoDeMantenimiento>(fabrica, new DepurarReservasAntiguasEntrada(PlazosDeReservas.CabecerasPorCorrida), cancelacion).ConfigureAwait(false);

    /// <summary>Corre un caso de uso de mantenimiento en su propio alcance. Un rechazo por actor lanza <see cref="ActorDeTrabajoDeFondoAusenteException"/>; cualquier otro fallo, <see cref="InvalidOperationException"/>.</summary>
    private static async Task<TSalida> Caso<TEntrada, TSalida>(IServiceScopeFactory fabrica, TEntrada entrada, CancellationToken cancelacion)
    {
        using var alcance = fabrica.CreateScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<ICasoDeUso<TEntrada, TSalida>>().Ejecutar(entrada, cancelacion).ConfigureAwait(false);
        if (resultado.EsExito)
        {
            return resultado.Valor;
        }

        ExigirActor(resultado.Error!);
        throw new InvalidOperationException($"El mantenimiento de reservas falló ({resultado.Error!.Clave}).");
    }

    private static void ExigirActor(ErrorDeAplicacion error)
    {
        if (error.Categoria is CategoriaDeError.NoAutenticado or CategoriaDeError.Prohibido)
        {
            throw new ActorDeTrabajoDeFondoAusenteException();
        }
    }

    private static class ClavesDelTrabajo
    {
        public const string BloqueoOcupado = "bloqueo-ocupado";
    }
}

/// <summary>El servicio hospedado que corre los trabajos de <c>Reservas</c> cada uno en su intervalo.</summary>
public sealed class ServicioDeTrabajosDeReservas(IEnumerable<TrabajoDeFondoDeReservas> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}

/// <summary>Registro de los trabajos de fondo de <c>Reservas</c>.</summary>
public static class ComposicionDeTrabajosDeReservas
{
    /// <summary>
    /// Registra el servicio hospedado con los cuatro trabajos (expiración, purga de huellas, purga de registros y depuración diaria). <b>Solo en el modo normal</b>: en restauración o ensayo no corre ningún trabajo (PLA-19); el
    /// anfitrión pasa <paramref name="soloEnModoNormal"/> con el resultado de <c>ModoDeArranque</c>. Pide al anfitrión <see cref="NpgsqlDataSource"/> y <see cref="IRegistroDeTrabajosDeFondo"/>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeReservas(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (!soloEnModoNormal)
        {
            return servicios;
        }

        servicios.AgregarEjecutorDeTrabajoDeFondo();
        servicios.TryAddSingleton(sp => new BloqueoDeInstanciaUnica(sp.GetRequiredService<NpgsqlDataSource>()));
        servicios.AddSingleton<IHostedService>(sp => new ServicioDeTrabajosDeReservas(
        [
            TrabajosDeReservas.Expirar(sp),
            TrabajosDeReservas.PurgarHuellas(sp),
            TrabajosDeReservas.PurgarRegistros(sp),
            TrabajosDeReservas.Depurar(sp),
        ]));
        return servicios;
    }
}
