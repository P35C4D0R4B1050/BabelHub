using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Web;

public sealed record OpcionesDelTrabajoDePurgaDeSesiones
{
    /// <summary>Cada cuánto corre la purga: es el plazo real de revocación de una sesión vencida sin solicitudes (plan §1, D05).</summary>
    public TimeSpan Intervalo { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Con tantas corridas fallidas seguidas se publica una alerta técnica: si la purga se detiene, las renovaciones de las sesiones vencidas dejan de revocarse.</summary>
    public int FallosSeguidosParaAlertar { get; init; } = 3;
}

/// <summary>
/// Trabajo de fondo de la purga de sesiones y borradores (T13, plan §5): corre <see cref="PurgarSesionesWeb"/> cada 5 minutos y registra cada corrida en
/// <c>plataforma.trabajos_de_fondo</c> (<see cref="IRegistroDeTrabajosDeFondo"/>). Una corrida fallida no lo detiene: queda registrada, la siguiente la reintenta y, si se repite, avisa
/// por una alerta técnica. La Api lo ejecuta como servicio hospedado y <b>solo en el modo normal</b>: en <c>Ensayo</c> y <c>Restauracion</c> está apagado (PLA-19, PLA-43), porque
/// el ensayo no debe revocar renovaciones de producción y al restaurar el paso de restauración vacía las tablas.
/// </summary>
public sealed class TrabajoDePurgaDeSesionesWeb(
    PurgarSesionesWeb purga,
    IRegistroDeTrabajosDeFondo registro,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelTrabajoDePurgaDeSesiones? opciones = null,
    TimeProvider? tiempo = null)
{
    public const string Nombre = "purgar-sesiones-web";
    public const string MotivoDeAlerta = "purga-de-sesiones-web-detenida";

    private readonly OpcionesDelTrabajoDePurgaDeSesiones _opciones = opciones ?? new OpcionesDelTrabajoDePurgaDeSesiones();
    private int _fallosSeguidos;

    /// <summary>Una corrida ahora. Solo la cancelación de quien la ejecuta se propaga; devuelve lo que hizo o <see langword="null"/> si falló.</summary>
    public async Task<ResultadoDePurga?> EjecutarUnaVez(CancellationToken cancelacion = default)
    {
        try
        {
            var resultado = await purga.Ejecutar(cancelacion).ConfigureAwait(false);
            _fallosSeguidos = 0;
            await Registrar(ResultadoDeTrabajo.Exito, cancelacion).ConfigureAwait(false);
            return resultado;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            _fallosSeguidos++;
            await Registrar(ResultadoDeTrabajo.Fallo, cancelacion).ConfigureAwait(false);
            if (_fallosSeguidos == _opciones.FallosSeguidosParaAlertar)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }

            return null;
        }
    }

    /// <summary>Corre hasta que se cancele: una corrida al empezar y otra en cada intervalo.</summary>
    public async Task Correr(CancellationToken cancelacion)
    {
        using var temporizador = new PeriodicTimer(_opciones.Intervalo, tiempo ?? TimeProvider.System);
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
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlerta, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Si ni siquiera se puede alertar, el trabajo sigue intentando en la próxima corrida.
        }
    }
}
