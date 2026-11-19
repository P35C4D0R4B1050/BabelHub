using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>
/// La revisión de envíos estancados (T11; ENV-12, spec §6, R03 y R05): corre como el actor «sistema» y publica una alerta técnica por cada envío que lleva <b>más de 24 horas con el despacho iniciado sin pasar a en reparto</b>
/// (el Inventario no respondió y logística debe reintentar) y, una sola vez, por cada envío <b>cancelado cuya guía quedó sin uso</b> en el operador (logística debe avisarle fuera del sistema; la alerta sale en la ventana
/// de 24 a 25 horas tras la cancelación, de modo que la corrida horaria la publica una vez). Las alertas <b>no llevan datos del destinatario ni el número de guía</b>. Devuelve cuántas alertas publicó.
/// </summary>
internal sealed class RevisarEnviosEstancados(IActorActual actores, IRepositorioDeEnvios envios, IPublicadorDeAlertasTecnicas alertas, IReloj reloj) : ICasoDeUso<int, int>
{
    public static readonly TimeSpan Umbral = TimeSpan.FromHours(24);

    public static readonly TimeSpan Ventana = TimeSpan.FromHours(1);

    public async Task<Resultado<int>> Ejecutar(int maximo, CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDeEnvios.De<int>(CategoriaDeError.Prohibido, ClavesDeEnvios.Prohibido);
        }

        var tope = Math.Clamp(maximo, 1, 500);
        var ahora = reloj.Ahora;
        try
        {
            var conDespacho = await envios.ObtenerConDespachoIniciadoAntesDe(ahora - Umbral, tope, cancelacion).ConfigureAwait(false);
            var sinUso = await envios.ObtenerCanceladosConGuiaEntre(ahora - Umbral - Ventana, ahora - Umbral, tope, cancelacion).ConfigureAwait(false);
            var publicadas = 0;
            foreach (var motivo in Enumerable.Repeat(ClavesDeEnvios.MotivoDeAlertaDeDependencia, conDespacho.Count).Concat(Enumerable.Repeat(ClavesDeEnvios.AlertaGuiaSinUso, sinUso.Count)))
            {
                try
                {
                    await alertas.Publicar(new AlertaTecnica(motivo, ahora), cancelacion).ConfigureAwait(false);
                    publicadas++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Una alerta que no se publica nunca interrumpe la revisión.
                }
            }

            return Resultado<int>.Exito(publicadas);
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<int>();
        }
    }
}
