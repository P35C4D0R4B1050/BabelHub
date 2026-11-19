using System.Globalization;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

internal enum ResultadoDelLimiteDeCobro
{
    Permitido,
    Excedido,

    /// <summary>El almacén de contadores no responde: el cobro falla cerrado y no se cobra (SEG-26).</summary>
    NoDisponible,
}

internal sealed record DecisionDelLimiteDeCobro(ResultadoDelLimiteDeCobro Resultado, int? EsperaEnSegundos = null)
{
    public static DecisionDelLimiteDeCobro Permitido { get; } = new(ResultadoDelLimiteDeCobro.Permitido);

    public static DecisionDelLimiteDeCobro NoDisponible { get; } = new(ResultadoDelLimiteDeCobro.NoDisponible);

    public Resultado<T> ComoFallo<T>() => Resultado == ResultadoDelLimiteDeCobro.Excedido
        ? FallosDePagos.De<T>(CategoriaDeError.Limite, ClavesDePagos.LimiteExcedido, [new ErrorDeCampo(ClavesDePagos.EsperaEnSegundos, (EsperaEnSegundos ?? 1).ToString(CultureInfo.InvariantCulture))])
        : FallosDePagos.De<T>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
}

/// <summary>
/// Los límites del cobro (PAG-04, PAG-17, D18, AM38): 5 intentos por intención y hora, 10 por cuenta y 20 por origen (valores iniciales y configurables en las políticas de la plataforma), contados <b>después</b> de resolver una
/// clave repetida y con el <c>intentoId</c> como clave del llamador, de modo que un doble clic no consume cupo. Si el almacén de contadores no responde, el cobro <b>falla cerrado</b>: no se cobra (SEG-26). El exceso deja su
/// evento de seguridad (lo publica el motor) y responde con la espera sugerida.
/// </summary>
internal sealed class LimitadorDeCobros(IControlDeLimites limites)
{
    public async Task<DecisionDelLimiteDeCobro> Reservar(Guid pedidoId, Guid cuentaId, Guid intentoId, CancellationToken cancelacion)
    {
        var clave = intentoId.ToString("N");
        var intentos = new (ClaveDePolitica Politica, string? Sujeto)[]
        {
            (PoliticasDeLimite.CobrosPorIntencion, pedidoId.ToString("N")),
            (PoliticasDeLimite.CobrosPorCuenta, cuentaId.ToString("N")),
            (PoliticasDeLimite.CobrosPorOrigen, null),
        };

        foreach (var (politica, sujeto) in intentos)
        {
            ResultadoDeLimite resultado;
            try
            {
                resultado = await limites.Reservar(politica, sujeto, claveDelLlamador: clave, cancelacion: cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return DecisionDelLimiteDeCobro.NoDisponible;
            }

            switch (resultado.Estado)
            {
                case EstadoDeLimite.Excedido:
                    return new DecisionDelLimiteDeCobro(ResultadoDelLimiteDeCobro.Excedido, resultado.EsperaEnSegundos);
                case EstadoDeLimite.NoDisponible:
                    return DecisionDelLimiteDeCobro.NoDisponible;
            }
        }

        return DecisionDelLimiteDeCobro.Permitido;
    }
}
