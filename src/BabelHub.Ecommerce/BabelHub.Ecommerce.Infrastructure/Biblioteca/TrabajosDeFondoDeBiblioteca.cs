using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>Corre una corrida de un trabajo de Biblioteca como el actor «sistema»: lo aporta el anfitrión (la Api), que es de los pocos lugares que fijan el actor: fija «sistema», ejecuta la corrida y limpia el actor al terminar, aunque falle. Un trabajo de fondo sin actor es un defecto, no un permiso.</summary>
public delegate Task AlcanceDeSistemaDeBiblioteca(Func<CancellationToken, Task> corrida, CancellationToken cancelacion);

/// <summary>Cuándo se mira un trabajo de fondo de Biblioteca, cuántos fallos seguidos hacen falta para avisar y el plazo máximo de una corrida.</summary>
public sealed record OpcionesDelTrabajoDeBiblioteca(TimeSpan Intervalo, int FallosSeguidosParaAlertar = 3, TimeSpan? PlazoMaximoDeLaCorrida = null)
{
    public TimeSpan PlazoDeLaCorrida => PlazoMaximoDeLaCorrida ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Un trabajo de fondo de Biblioteca (T15; BIB-26, BIB-29, D02): corre <b>como «sistema»</b>, registra el resultado de cada corrida <b>que hizo algo</b> en <c>plataforma.trabajos_de_fondo</c> (un tick sin trabajo debido no cuenta como éxito) y, tras varios fallos seguidos, publica la alerta técnica
/// <c>trabajo-de-fondo-detenido</c> sin datos personales. <b>Falla cerrado</b>: una corrida rechazada (porque el actor no es «sistema» o porque no pudo auditar) es una corrida fallida, nunca un éxito silencioso. Nunca deja caer el servicio: solo se propaga la cancelación del apagado ordenado.
/// La corrida devuelve «verdadero» si tocaba correr y corrió.
/// </summary>
public sealed class TrabajoDeFondoDeBiblioteca(
    string nombre,
    Func<CancellationToken, Task<bool>> corrida,
    IRegistroDeTrabajosDeFondo registro,
    AlcanceDeSistemaDeBiblioteca alcanceDeSistema,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDeBiblioteca opciones,
    TimeProvider? tiempo = null)
{
    public const string MotivoDelTrabajoDetenido = "trabajo-de-fondo-detenido";

    private int _fallosSeguidos;

    public string Nombre { get; } = nombre;

    public async Task EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            var corrio = false;
            using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            plazo.CancelAfter(opciones.PlazoDeLaCorrida);
            await alcanceDeSistema(async token => corrio = await corrida(token).ConfigureAwait(false), plazo.Token).ConfigureAwait(false);
            _fallosSeguidos = 0;
            if (corrio)
            {
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

/// <summary>Los trabajos de fondo de Biblioteca con sus nombres estables en <c>plataforma.trabajos_de_fondo</c>, sus intervalos y sus corridas.</summary>
public static class TrabajosDeBiblioteca
{
    public const string PurgaDeClaves = "biblioteca-purga-de-claves";

    public const string DepuracionDeDescargas = "biblioteca-depuracion-de-descargas";

    public const string DepuracionDeAyudas = "biblioteca-depuracion-de-ayudas";

    /// <summary>La hora de Lima (03:30) desde la que corre cada depuración diaria (D09).</summary>
    public static readonly TimeSpan HoraDeLimaDeLaDepuracion = new(3, 30, 0);

    private static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    /// <summary>Las claves vencidas se purgan cada hora.</summary>
    public static readonly OpcionesDelTrabajoDeBiblioteca PorHora = new(TimeSpan.FromHours(1));

    /// <summary>Las depuraciones diarias se miran cada 10 minutos: corren desde su hora de Lima y, si un corte las impidió, <b>recuperan el mismo día</b> (son idempotentes por día de Lima).</summary>
    public static readonly OpcionesDelTrabajoDeBiblioteca CadaDiezMinutos = new(TimeSpan.FromMinutes(10), FallosSeguidosParaAlertar: 3);

    /// <summary>«Toca hoy» una depuración diaria: ya pasó la hora de Lima y todavía no corrió con éxito en este día de Lima.</summary>
    public static bool TocaHoy(IReloj reloj, DateOnly? ultimoDiaEjecutado) =>
        reloj.Ahora.ToOffset(DesfaseDeLima).TimeOfDay >= HoraDeLimaDeLaDepuracion && ultimoDiaEjecutado != reloj.HoyEnLima;

    public static IReadOnlyList<TrabajoDeFondoDeBiblioteca> Todos(
        IServiceScopeFactory fabrica,
        IRegistroDeTrabajosDeFondo registro,
        AlcanceDeSistemaDeBiblioteca alcance,
        IPublicadorDeAlertasTecnicas alertas,
        IReloj reloj,
        TimeProvider? tiempo = null)
    {
        DateOnly? ultimaDeDescargas = null;
        DateOnly? ultimaDeAyudas = null;

        TrabajoDeFondoDeBiblioteca Crear(string nombre, OpcionesDelTrabajoDeBiblioteca opciones, Func<IServiceProvider, CancellationToken, Task<bool>> ejecutar) =>
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
            Crear(
                PurgaDeClaves,
                PorHora,
                async (p, ct) =>
                {
                    ExigirExito(await p.GetRequiredService<PurgarClavesDeDescarga>().Ejecutar(ct).ConfigureAwait(false));
                    return true;
                }),
            Crear(
                DepuracionDeDescargas,
                CadaDiezMinutos,
                async (p, ct) =>
                {
                    if (!TocaHoy(reloj, ultimaDeDescargas))
                    {
                        return false;
                    }

                    ExigirExito(await p.GetRequiredService<DepurarRegistrosDeDescargas>().Ejecutar(ct).ConfigureAwait(false));
                    ultimaDeDescargas = reloj.HoyEnLima;
                    return true;
                }),
            Crear(
                DepuracionDeAyudas,
                CadaDiezMinutos,
                async (p, ct) =>
                {
                    if (!TocaHoy(reloj, ultimaDeAyudas))
                    {
                        return false;
                    }

                    ExigirExito(await p.GetRequiredService<DepurarAyudas>().Ejecutar(ct).ConfigureAwait(false));
                    ultimaDeAyudas = reloj.HoyEnLima;
                    return true;
                }),
        ];
    }

    /// <summary>Un caso de uso que rechaza (porque el actor no es «sistema» o porque la auditoría no está disponible) es una corrida fallida, nunca un éxito silencioso: falla cerrada.</summary>
    private static void ExigirExito<T>(Resultado<T> resultado)
    {
        if (!resultado.EsExito)
        {
            throw new InvalidOperationException("El trabajo de fondo de Biblioteca fue rechazado.");
        }
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos de Biblioteca en paralelo hasta el apagado (lo hospeda la Api).</summary>
public sealed class ServicioDeTrabajosDeBiblioteca(IEnumerable<TrabajoDeFondoDeBiblioteca> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}
