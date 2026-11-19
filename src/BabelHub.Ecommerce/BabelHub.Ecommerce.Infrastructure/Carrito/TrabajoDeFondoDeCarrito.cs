using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// Corre una corrida de un trabajo como el actor «sistema»: lo aporta el anfitrión (la Api, T29), que es de los pocos lugares que fijan el actor (regla de arquitectura): fija «sistema», ejecuta la corrida con su cancelación y limpia el actor al terminar, aunque falle.
/// </summary>
public delegate Task AlcanceDeSistema(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Los trabajos de fondo de Carrito: sus nombres estables en <c>plataforma.trabajos_de_fondo</c> y cada cuánto corren.</summary>
public static class TrabajosDeFondoDelCarrito
{
    public const string PurgaDeVisitantes = "carrito-purga-de-visitantes";

    public const string PurgaDeAvisos = "carrito-purga-de-avisos";

    public const string ContactoYCheckoutsVencidos = "carrito-contacto-y-checkouts-vencidos";

    public const string RecuperacionDeReservando = "carrito-recuperacion-de-reservando";

    public const string LiberacionDeReservas = "carrito-liberacion-de-reservas";

    public const string RevisionDePagosPendientes = "carrito-revision-de-pagos-pendientes";

    /// <summary>Sin una corrida exitosa en 24 horas se publica una alerta técnica (AUD-31).</summary>
    public static readonly TimeSpan SinCorrerHasta = TimeSpan.FromHours(24);

    public static readonly OpcionesDelTrabajoDelCarrito PorHora = new(TimeSpan.FromHours(1));

    public static readonly OpcionesDelTrabajoDelCarrito CadaSeisHoras = new(TimeSpan.FromHours(6));

    /// <summary>El de <c>reservando</c> atascado (más de 2 minutos) y las liberaciones pendientes: cada minuto.</summary>
    public static readonly OpcionesDelTrabajoDelCarrito CadaMinuto = new(TimeSpan.FromMinutes(1));

    public static readonly OpcionesDelTrabajoDelCarrito CadaCincoMinutos = new(TimeSpan.FromMinutes(5));

    /// <summary>
    /// Los seis trabajos del módulo con una corrida por alcance de servicios (los casos de uso dependen del actor, de la unidad de trabajo y de los repositorios, que son por alcance): purga de carritos de visitante, de avisos, del contacto y los checkouts
    /// vencidos, recuperación de <c>reservando</c>, reintento de liberaciones y revisión de pagos pendientes.
    /// </summary>
    public static IReadOnlyList<TrabajoDeFondoDeCarrito> Todos(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistema alcanceDeSistema, IAlertasDelCarrito alertas, IReloj reloj, TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDeCarrito Crear<TCaso>(string nombre, OpcionesDelTrabajoDelCarrito opciones, Func<TCaso, CancellationToken, Task> ejecutar)
            where TCaso : notnull =>
            new(
                nombre,
                async ct =>
                {
                    using var alcance = fabrica.CreateScope();
                    await ejecutar(alcance.ServiceProvider.GetRequiredService<TCaso>(), ct).ConfigureAwait(false);
                },
                registro,
                alcanceDeSistema,
                alertas,
                reloj,
                opciones,
                tiempo);

        return
        [
            Crear<PurgarCarritosDeVisitante>(PurgaDeVisitantes, PorHora, async (caso, ct) => ExigirExito(await caso.Ejecutar(new TrabajoDelCarritoEntrada(), ct).ConfigureAwait(false))),
            Crear<PurgarAvisosVistos>(PurgaDeAvisos, CadaSeisHoras, async (caso, ct) => ExigirExito(await caso.Ejecutar(new TrabajoDelCarritoEntrada(), ct).ConfigureAwait(false))),
            Crear<PurgarContactoYCheckoutsVencidos>(ContactoYCheckoutsVencidos, PorHora, async (caso, ct) => ExigirExito(await caso.Ejecutar(new TrabajoDelCarritoEntrada(), ct).ConfigureAwait(false))),
            Crear<RecuperarCheckoutsReservando>(RecuperacionDeReservando, CadaMinuto, async (caso, ct) => ExigirExito(await caso.Ejecutar(new TrabajoDelCarritoEntrada(), ct).ConfigureAwait(false))),
            Crear<LiberarReservasPendientes>(LiberacionDeReservas, CadaMinuto, async (caso, ct) => ExigirExito(await caso.Ejecutar(new LiberarReservasPendientesEntrada(), ct).ConfigureAwait(false))),
            Crear<RevisarCheckoutsPendientes>(RevisionDePagosPendientes, CadaCincoMinutos, async (caso, ct) => ExigirExito(await caso.Ejecutar(new TrabajoDelCarritoEntrada(), ct).ConfigureAwait(false))),
        ];
    }

    /// <summary>Un caso de uso que rechaza (por ejemplo, porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso: falla cerrada.</summary>
    private static void ExigirExito<T>(Resultado<T> resultado)
    {
        if (!resultado.EsExito)
        {
            throw new InvalidOperationException("El trabajo de fondo del carrito fue rechazado.");
        }
    }
}

/// <summary>Cuándo corre un trabajo de fondo de Carrito y cuántos fallos seguidos hacen falta para avisar.</summary>
public sealed record OpcionesDelTrabajoDelCarrito(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3);

/// <summary>
/// Un trabajo de fondo de Carrito (T26a; CAR-29, CAR-34): corre su corrida cada intervalo <b>dentro del <see cref="AlcanceDeSistema"/> que le da el anfitrión</b>, que fija el actor «sistema» (sin ese contexto los casos de uso lo rechazan: es un defecto, no un permiso), registra el resultado enumerado de
/// cada corrida en <c>plataforma.trabajos_de_fondo</c> y publica una alerta técnica tras varios fallos seguidos y cuando <b>no hay una corrida exitosa en 24 horas</b> (AUD-31). Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// La alerta no lleva datos personales: solo el tipo y un conteo.
/// </summary>
public sealed class TrabajoDeFondoDeCarrito(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistema alcanceDeSistema,
    IAlertasDelCarrito alertas,
    IReloj reloj,
    OpcionesDelTrabajoDelCarrito opciones,
    TimeProvider? tiempo = null)
{
    private int _fallosSeguidos;
    private DateTimeOffset? _ultimaExitosa;
    private DateTimeOffset? _ultimaAlertaDeInactividad;

    public string Nombre { get; } = nombre;

    /// <summary>Ejecuta la corrida ahora y la registra. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        _ultimaExitosa ??= reloj.Ahora;
        try
        {
            await alcanceDeSistema(corrida, cancelacion).ConfigureAwait(false);
            _fallosSeguidos = 0;
            _ultimaExitosa = reloj.Ahora;
            _ultimaAlertaDeInactividad = null;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            var porFallosSeguidos = _fallosSeguidos >= opciones.FallosSeguidosParaAlertar && (_fallosSeguidos - opciones.FallosSeguidosParaAlertar) % opciones.FallosSeguidosParaAlertar == 0;
            var ahora = reloj.Ahora;
            var porInactividad = _ultimaExitosa is { } ultima && ahora - ultima >= TrabajosDeFondoDelCarrito.SinCorrerHasta && (_ultimaAlertaDeInactividad is not { } previa || ahora - previa >= TrabajosDeFondoDelCarrito.SinCorrerHasta);
            if (porInactividad)
            {
                _ultimaAlertaDeInactividad = ahora;
            }

            if (porFallosSeguidos || porInactividad)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Corre hasta que se cancele: una corrida al empezar y otra en cada intervalo.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(opciones.Intervalo, tiempo ?? TimeProvider.System);
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
            await registro.Registrar(Nombre, resultado, reloj.Ahora, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El registro de la corrida es informativo: si falla, el trabajo sigue.
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(TipoDeAlertaDelCarrito.TrabajoSinCorrer, 1, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Carrito en paralelo hasta el apagado (T26a; lo hospeda la Api en T29).</summary>
public sealed class ServicioDeTrabajosDelCarrito(IEnumerable<TrabajoDeFondoDeCarrito> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
