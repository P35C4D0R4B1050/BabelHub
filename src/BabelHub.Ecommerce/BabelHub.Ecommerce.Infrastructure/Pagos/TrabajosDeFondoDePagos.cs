using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Corre una corrida de un trabajo como el actor «sistema»: lo aporta el anfitrión (la Api, T29), que es de los pocos lugares que fijan el actor: fija «sistema», ejecuta la corrida con su cancelación y limpia el actor al terminar, aunque falle.
/// </summary>
public delegate Task AlcanceDeSistemaDePagos(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Cada cuánto corre un trabajo de fondo de Pagos y cuántos fallos seguidos hacen falta para avisar.</summary>
public sealed record OpcionesDelTrabajoDePagos(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3);

/// <summary>
/// Los trabajos de fondo de Pagos (T27): sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, su cadencia y su construcción sobre los casos de uso de T19 a T24. Cada corrida atiende hasta <see cref="TandaPorCorrida"/> elementos y crea su propio
/// alcance de servicios (los casos de uso dependen de la unidad de trabajo y de los repositorios, que son por alcance).
/// </summary>
public static class TrabajosDeFondoDePagos
{
    public const string ConsultaDeCobrosPendientes = "pagos-consulta-de-cobros-pendientes";

    public const string RetomaDeComprasAprobadas = "pagos-retoma-de-compras-aprobadas";

    public const string CierreDeIntencionesVencidas = "pagos-cierre-de-intenciones-vencidas";

    public const string ReintentoDeReembolsos = "pagos-reintento-de-reembolsos";

    public const string AvisosACarrito = "pagos-avisos-a-carrito";

    public const string AvisosDePago = "pagos-avisos-de-pago";

    public const string PurgaDeContacto = "pagos-purga-de-contacto";

    public const string DepuracionDeIntenciones = "pagos-depuracion-de-intenciones";

    public const string PurgaDeAvisos = "pagos-purga-de-avisos";

    /// <summary>Sin una corrida exitosa en 24 horas se publica una alerta técnica (AUD-31).</summary>
    public static readonly TimeSpan SinCorrerHasta = TimeSpan.FromHours(24);

    public const int TandaPorCorrida = 50;

    public static readonly OpcionesDelTrabajoDePagos CadaMinuto = new(TimeSpan.FromMinutes(1));

    public static readonly OpcionesDelTrabajoDePagos PorHora = new(TimeSpan.FromHours(1));

    public static readonly OpcionesDelTrabajoDePagos CadaSeisHoras = new(TimeSpan.FromHours(6));

    public static readonly OpcionesDelTrabajoDePagos PorDia = new(TimeSpan.FromHours(24));

    /// <summary>Los nueve trabajos del módulo.</summary>
    public static IReadOnlyList<TrabajoDeFondoDePagos> Todos(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDePagos alcanceDeSistema, IPublicadorDeAlertasTecnicas alertas, IReloj reloj, TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDePagos Crear<TCaso>(string nombre, OpcionesDelTrabajoDePagos opciones, Func<TCaso, CancellationToken, Task> ejecutar)
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
            Crear<ConsultarPagosPendientes>(ConsultaDeCobrosPendientes, CadaMinuto, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<ReanudarComprasAprobadas>(RetomaDeComprasAprobadas, CadaMinuto, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<CerrarIntencionesVencidas>(CierreDeIntencionesVencidas, CadaMinuto, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<ReintentarReembolsosPendientes>(ReintentoDeReembolsos, CadaMinuto, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<AvisarACarrito>(AvisosACarrito, CadaMinuto, (caso, ct) => caso.EnviarPendientes(TandaPorCorrida, ct)),
            Crear<EnviarAvisosDePago>(AvisosDePago, CadaMinuto, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<PurgarContactoDeIntenciones>(PurgaDeContacto, PorHora, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<DepurarIntencionesAntiguas>(DepuracionDeIntenciones, CadaSeisHoras, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
            Crear<PurgarAvisosDePagos>(PurgaDeAvisos, PorDia, (caso, ct) => caso.Ejecutar(TandaPorCorrida, ct)),
        ];
    }
}

/// <summary>
/// Un trabajo de fondo de Pagos (T27): corre su corrida cada intervalo <b>dentro del <see cref="AlcanceDeSistemaDePagos"/> que le da el anfitrión</b>, que fija el actor «sistema» (sin ese contexto los casos de uso lo rechazan: es un defecto, no un
/// permiso), registra el resultado enumerado de cada corrida en <c>plataforma.trabajos_de_fondo</c> y publica la alerta técnica <c>trabajo-de-pagos-sin-correr</c> tras varios fallos seguidos y cuando <b>no hay una corrida exitosa en 24 horas</b>. Nunca deja
/// caer el servicio: solo se propaga la cancelación del apagado ordenado. La alerta no lleva datos personales. Falla cerrada: una corrida que lanza cuenta como fallo.
/// </summary>
public sealed class TrabajoDeFondoDePagos(
    string nombre,
    Func<CancellationToken, Task> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDePagos alcanceDeSistema,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDePagos opciones,
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
            var porInactividad = _ultimaExitosa is { } ultima && ahora - ultima >= TrabajosDeFondoDePagos.SinCorrerHasta && (_ultimaAlertaDeInactividad is not { } previa || ahora - previa >= TrabajosDeFondoDePagos.SinCorrerHasta);
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
            await alertas.Publicar(new AlertaTecnica("trabajo-de-pagos-sin-correr", reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Pagos en paralelo hasta el apagado (T27; lo hospeda la Api en T29).</summary>
public sealed class ServicioDeTrabajosDePagos(IEnumerable<TrabajoDeFondoDePagos> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
