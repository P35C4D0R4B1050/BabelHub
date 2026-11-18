using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BabelHub.Ecommerce.Api.Catalogo;

/// <summary>
/// Un trabajo de fondo del Catálogo (T23): corre su corrida <b>como «sistema» fijado explícitamente</b> (un trabajo sin actor es un defecto, no un permiso), en su propio alcance de servicios, y registra el resultado enumerado de cada corrida en
/// <c>plataforma.trabajos_de_fondo</c>. <b>Falla cerrada</b>: cualquier excepción, un resultado de fallo del caso de uso o una depuración con filas que no se pudieron borrar cuenta como <see cref="ResultadoDeTrabajo.Fallo"/> y nunca deja caer el servicio
/// (solo la cancelación del apagado ordenado se propaga); lo que no se pudo hacer se reintenta en la corrida siguiente.
/// </summary>
public sealed class TrabajoDeFondoDelCatalogo(
    string nombre,
    Func<IServiceProvider, CancellationToken, Task> corrida,
    IServiceScopeFactory fabrica,
    IRegistroDeTrabajosDeFondo registro,
    IEstablecedorDeActor actor,
    IReloj reloj,
    TimeSpan periodo,
    TimeProvider? tiempo = null)
{
    public string Nombre { get; } = nombre;

    /// <summary>Ejecuta la corrida ahora y la registra; devuelve su resultado. Solo la cancelación de quien lo ejecuta se propaga.</summary>
    public async Task<ResultadoDeTrabajo> EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        actor.Establecer(ActorActual.DelSistema());
        var resultado = ResultadoDeTrabajo.Exito;
        try
        {
            using var alcance = fabrica.CreateScope();
            await corrida(alcance.ServiceProvider, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            resultado = ResultadoDeTrabajo.Fallo;
        }
        finally
        {
            actor.Limpiar();
        }

        await Registrar(resultado, cancelacion).ConfigureAwait(false);
        return resultado;
    }

    /// <summary>Corre hasta que se cancele: una corrida al empezar y otra cada día.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(periodo, tiempo ?? TimeProvider.System);
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
}

/// <summary>Los trabajos de fondo del Catálogo, con sus nombres estables (minúsculas y guiones, sin datos) y su ejecución diaria (T23).</summary>
public static class TrabajosDelCatalogo
{
    public const string DepuracionDeArchivos = "catalogo-depuracion-de-archivos";

    public const string AnonimizacionDeIngresos = "catalogo-anonimizacion-de-ingresos";

    /// <summary>Una vez al día (las dos tareas son idempotentes: correr de más no cambia nada).</summary>
    public static readonly TimeSpan Periodo = TimeSpan.FromDays(1);

    public static IReadOnlyList<TrabajoDeFondoDelCatalogo> Crear(IServiceProvider servicios, TimeProvider? tiempo = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var fabrica = servicios.GetRequiredService<IServiceScopeFactory>();
        var registro = servicios.GetRequiredService<IRegistroDeTrabajosDeFondo>();
        var actor = servicios.GetRequiredService<IEstablecedorDeActor>();
        var reloj = servicios.GetRequiredService<IReloj>();

        return
        [
            new TrabajoDeFondoDelCatalogo(
                DepuracionDeArchivos,
                async (sp, ct) =>
                {
                    var resultado = await sp.GetRequiredService<IMantenimientoDelCatalogo>().DepurarArchivosSinUso(ct).ConfigureAwait(false);
                    if (!resultado.EsExito || resultado.Valor.Fallidas > 0)
                    {
                        throw new InvalidOperationException("La depuración de archivos no se completó.");
                    }
                },
                fabrica,
                registro,
                actor,
                reloj,
                Periodo,
                tiempo),
            new TrabajoDeFondoDelCatalogo(
                AnonimizacionDeIngresos,
                async (sp, ct) =>
                {
                    var resultado = await sp.GetRequiredService<IMantenimientoDelCatalogo>().AnonimizarIngresosSolicitados(ct).ConfigureAwait(false);
                    if (!resultado.EsExito)
                    {
                        throw new InvalidOperationException("La anonimización de ingresos no se completó.");
                    }
                },
                fabrica,
                registro,
                actor,
                reloj,
                Periodo,
                tiempo),
        ];
    }
}

/// <summary>El servicio en segundo plano que corre los trabajos del Catálogo en paralelo hasta el apagado (T23).</summary>
public sealed class ServicioDeTrabajosDelCatalogo(IReadOnlyList<TrabajoDeFondoDelCatalogo> trabajos) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) =>
        Task.WhenAll(trabajos.Select(trabajo => trabajo.Correr(cancelacion)));
}

public static class ComposicionDeTrabajosDelCatalogo
{
    /// <summary>
    /// Registra los trabajos de fondo del Catálogo (T23): la depuración de archivos sin uso y la anonimización de ingresos, cada uno en su alcance y como «sistema». Las instancias quedan disponibles para correrlas una vez; el servicio hospedado que las corre cada día
    /// se registra <b>solo en el modo normal</b>: en restauración o ensayo no corre ningún trabajo (PLA-19). El anfitrión aporta <see cref="IRegistroDeTrabajosDeFondo"/>, <see cref="IEstablecedorDeActor"/>, <see cref="IReloj"/> y el Catálogo compuesto.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDelCatalogo(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton(sp => TrabajosDelCatalogo.Crear(sp));
        if (soloEnModoNormal)
        {
            servicios.AddHostedService(sp => new ServicioDeTrabajosDelCatalogo(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDelCatalogo>>()));
        }

        return servicios;
    }
}
