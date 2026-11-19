using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Api.Pagos;

/// <summary>
/// La ráfaga de la entrada de avisos de la pasarela (PAG-19, SEG-05, SEG-26; AC-PAG-16): reserva un cupo de <c>avisos-de-la-pasarela-por-origen</c> (120 por minuto por origen) <b>antes</b> de que el aviso llegue a la verificación de la firma,
/// para que una entrada pública y sin sesión no pueda agotar el cómputo con firmas falsas. Cuenta <b>todo lo que la exención deja pasar</b> —la ruta, con o sin barra final y sus subrutas, con cualquier método— con el mismo criterio de ruta que <c>OpcionesDeLimitesHttp.RutasExentas</c> (por segmento, sin distinguir mayúsculas): lo exento nunca queda sin límite. Un exceso responde el 429 uniforme con <c>Retry-After</c> (o el 503 si el almacén de contadores no
/// está disponible: con el almacén caído la política cae al contador local, SEG-26, y el aviso legítimo se reintenta). La ruta está <b>exenta</b> del límite general sin sesión de 60 por minuto (SEG-18) y del tope previo —la pasarela
/// envía desde pocas direcciones y se bloquearía sola—: esta política es su único límite de entrada y por eso va aquí, no en el pipeline general.
/// </summary>
public sealed class LimiteDeAvisosDeLaPasarelaMiddleware(RequestDelegate siguiente, OpcionesDeErrores errores)
{
    public async Task InvokeAsync(HttpContext contexto, IControlDeLimites control)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (!contexto.Request.Path.StartsWithSegments(AvisosDeLaPasarelaEnLaApi.Ruta, StringComparison.OrdinalIgnoreCase))
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        var resultado = await control.Reservar(PoliticasDeLimite.AvisosDeLaPasarelaPorOrigen, cancelacion: contexto.RequestAborted).ConfigureAwait(false);
        if (resultado.EsPermitido)
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        await ProblemasHttp.EscribirLimite(contexto, errores, resultado).ConfigureAwait(false);
    }
}

public static class LimiteDeAvisosDeLaPasarelaExtensiones
{
    /// <summary>Va <b>después</b> del origen del borde (la política es por origen) y <b>antes</b> de la credencial, la sesión y los endpoints.</summary>
    public static IApplicationBuilder UsarLimiteDeAvisosDeLaPasarela(this IApplicationBuilder app) => app.UseMiddleware<LimiteDeAvisosDeLaPasarelaMiddleware>();
}
