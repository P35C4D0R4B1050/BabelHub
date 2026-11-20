using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>
/// Corre una corrida de un trabajo de Pedidos como el actor «sistema»: lo aporta el anfitrión (la Api, T32), que es de los pocos lugares que fijan el actor (esta clase no usa <c>IEstablecedorDeActor</c>): fija «sistema», ejecuta la corrida con su cancelación y limpia el actor al terminar, aunque falle. Un trabajo de fondo sin actor es un defecto, no un permiso.
/// </summary>
public delegate Task AlcanceDeSistemaDePedidos(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Cuándo corre un trabajo de fondo de Pedidos, cuántos fallos seguidos hacen falta para avisar y el plazo máximo de una corrida.</summary>
public sealed record OpcionesDelTrabajoDePedidos(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3, TimeSpan? PlazoMaximoDeLaCorrida = null)
{
    public TimeSpan PlazoDeLaCorrida => PlazoMaximoDeLaCorrida ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Un trabajo de fondo de Pedidos (T30): corre su corrida cada intervalo <b>como «sistema»</b>, registra el resultado enumerado de cada corrida en <c>plataforma.trabajos_de_fondo</c> y, tras varios fallos seguidos, publica la alerta técnica <c>trabajo-de-fondo-detenido</c> (sin datos personales). <b>Falla cerrado</b>: un caso de uso
/// que rechaza (porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso. Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// </summary>
public sealed class TrabajoDeFondoDePedidos(
    string nombre,
    Func<CancellationToken, Task<bool>> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDePedidos alcanceDeSistema,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDePedidos opciones,
    TimeProvider? tiempo = null)
{
    public const string MotivoDelTrabajoDetenido = "trabajo-de-pedidos-sin-correr";

    private int _fallosSeguidos;

    public string Nombre { get; } = nombre;

    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            plazo.CancelAfter(opciones.PlazoDeLaCorrida);
            var hizoAlgo = true;
            await alcanceDeSistema(async ct => { hizoAlgo = await corrida(ct).ConfigureAwait(false); }, plazo.Token).ConfigureAwait(false);

            // Un tick en el que no tocaba trabajar (un trabajo diario fuera de su hora) no es un éxito: ni se registra ni reinicia la cuenta de fallos.
            if (hizoAlgo)
            {
                _fallosSeguidos = 0;
                await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            if (_fallosSeguidos >= opciones.FallosSeguidosParaAlertar && (_fallosSeguidos - opciones.FallosSeguidosParaAlertar) % opciones.FallosSeguidosParaAlertar == 0)
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
        catch (Exception) when (cancelacion.IsCancellationRequested)
        {
            // Una falla de la red o de la base con el apagado ya pedido: no se propaga ni detiene el anfitrión.
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
            await alertas.Publicar(new AlertaTecnica(MotivoDelTrabajoDetenido, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}

/// <summary>Los trabajos de fondo de Pedidos con sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, sus intervalos y sus corridas.</summary>
public static class TrabajosDePedidos
{
    public const string RevisionDePlazos = "pedidos-revision-de-plazos";
    public const string DepuracionDiaria = "pedidos-depuracion-diaria";
    public const string LiberacionDelContacto = "pedidos-liberacion-del-contacto";
    public const string ReanudacionDeOperaciones = "pedidos-reanudacion-de-operaciones";
    public const string RevisionSinEstadoFinal = "pedidos-revision-sin-estado-final";

    /// <summary>La hora de Lima (03:00) a la que corre la depuración diaria y la (04:00) de la revisión de lo que no llega a un estado final.</summary>
    public const int HoraDeLimaDeLaDepuracion = 3;
    public const int HoraDeLimaDeLaRevision = 4;

    /// <summary>Pasado este tiempo desde la última depuración con éxito, cada tick publica la alerta técnica (PED-39).</summary>
    public static readonly TimeSpan SinDepuracionDurante = TimeSpan.FromHours(25);

    public static readonly OpcionesDelTrabajoDePedidos CadaMinuto = new(TimeSpan.FromMinutes(1), FallosSeguidosParaAlertar: 3, PlazoMaximoDeLaCorrida: TimeSpan.FromMinutes(2));

    public static readonly OpcionesDelTrabajoDePedidos PorHora = new(TimeSpan.FromHours(1));

    /// <summary>Un trabajo diario corre desde su hora de Lima y mientras no haya corrido con éxito hoy: se mira cada hora, así que recupera el mismo día un corte (si falla, sigue intentando cada hora y los fallos seguidos alertan, PED-39).</summary>
    public static bool TocaHoy(IReloj reloj, DateTimeOffset? ultimaCorridaExitosa, int horaDeLima) =>
        reloj.Ahora.ToOffset(PoliticaDeRetencion.DesfaseDeLima).Hour >= horaDeLima && (ultimaCorridaExitosa is null || PoliticaDeRetencion.FechaEnLima(ultimaCorridaExitosa.Value) < reloj.HoyEnLima);

    /// <summary>Los cinco trabajos; cada corrida abre su propio alcance de servicios (los casos de uso dependen del actor, de la unidad de trabajo y de los repositorios, que son por alcance).</summary>
    public static IReadOnlyList<TrabajoDeFondoDePedidos> Todos(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDePedidos alcance, IPublicadorDeAlertasTecnicas alertas, IReloj reloj, TimeProvider? tiempo = null)
    {
        TrabajoDeFondoDePedidos Crear(string nombre, OpcionesDelTrabajoDePedidos opciones, Func<IServiceProvider, CancellationToken, Task<bool>> ejecutar) =>
            new(
                nombre,
                async ct =>
                {
                    using var ambito = fabrica.CreateScope();
                    return await ejecutar(ambito.ServiceProvider, ct).ConfigureAwait(false);
                },
                registro,
                alcance,
                alertas,
                reloj,
                opciones,
                tiempo);

        return
        [
            Crear(RevisionDePlazos, PorHora, async (p, ct) => ExigirExito(await p.GetRequiredService<MarcarLineasNoRecogidas>().Revisar(ct).ConfigureAwait(false))),
            Crear(LiberacionDelContacto, CadaMinuto, async (p, ct) => ExigirExito(await p.GetRequiredService<LiberarContactoDeIntenciones>().Liberar(ct).ConfigureAwait(false))),
            Crear(ReanudacionDeOperaciones, CadaMinuto, async (p, ct) => ExigirExito(await p.GetRequiredService<CompletarOperacionesEnCurso>().Reanudar(ct).ConfigureAwait(false))),
            Crear(
                DepuracionDiaria,
                PorHora,
                async (p, ct) =>
                {
                    var ultima = await p.GetRequiredService<IRepositorioDePedidos>().UltimaDepuracionExitosaEn(ct).ConfigureAwait(false);
                    if (TocaHoy(reloj, ultima, HoraDeLimaDeLaDepuracion))
                    {
                        return ExigirExito(await p.GetRequiredService<AnonimizarContactosVencidos>().Depurar(ct).ConfigureAwait(false));
                    }

                    // Aunque hoy no toque (o el trabajo no haya podido correr), una depuración sin éxito desde hace más de un día alerta en cada tick: nunca queda en silencio (PED-39).
                    if (ultima is { } ultimaVez && reloj.Ahora - ultimaVez >= SinDepuracionDurante)
                    {
                        await alertas.Publicar(new AlertaTecnica(AnonimizarContactosVencidos.AlertaSinCorrida, reloj.Ahora), ct).ConfigureAwait(false);
                    }

                    return false;
                }),
            Crear(
                RevisionSinEstadoFinal,
                PorHora,
                async (p, ct) =>
                    TocaHoy(reloj, null, HoraDeLimaDeLaRevision) && reloj.Ahora.ToOffset(PoliticaDeRetencion.DesfaseDeLima).Hour == HoraDeLimaDeLaRevision
                        ? ExigirExito(await p.GetRequiredService<RevisarPedidosSinEstadoFinal>().Revisar(ct).ConfigureAwait(false))
                        : false),
        ];
    }

    /// <summary>Un caso de uso que rechaza (por ejemplo, porque el actor no es «sistema») es una corrida fallida, nunca un éxito silencioso: falla cerrada. Devuelve <see langword="true"/> (hizo su trabajo).</summary>
    private static bool ExigirExito<T>(Resultado<T> resultado)
    {
        if (!resultado.EsExito)
        {
            throw new InvalidOperationException("El trabajo de fondo de Pedidos fue rechazado.");
        }

        return true;
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Pedidos en paralelo hasta el apagado (lo hospeda la Api, T32, solo en modo <c>Normal</c>).</summary>
public sealed class ServicioDeTrabajosDePedidos(IEnumerable<TrabajoDeFondoDePedidos> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
