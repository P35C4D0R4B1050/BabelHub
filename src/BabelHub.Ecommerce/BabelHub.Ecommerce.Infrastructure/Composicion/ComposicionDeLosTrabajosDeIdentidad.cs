using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>El registro del servicio hospedado que corre los trabajos de fondo de Identidad (T37 a T40).</summary>
public static class ComposicionDeLosTrabajosDeIdentidad
{
    /// <summary>
    /// Registra el servicio hospedado con los cinco trabajos (purga de vencidos, descarte de cancelaciones, rotación de la clave de firma, renovación de la caché de claves y entrega de bajas). <b>Solo en el modo
    /// normal</b>: en restauración o ensayo no corren; el anfitrión pasa <paramref name="soloEnModoNormal"/> con el resultado de <c>ModoDeArranque</c>. El anfitrión aporta el <paramref name="alcanceDeSistema"/> que fija el actor «sistema» (la regla de arquitectura limita quién lo fija). Requiere <see cref="CacheDeClavesDeVerificacion"/>, las
    /// alertas y el registro de trabajos ya registrados.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeIdentidad(this IServiceCollection servicios, bool soloEnModoNormal, Func<IServiceProvider, AlcanceDeSistemaDeIdentidad> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        if (!soloEnModoNormal)
        {
            return servicios;
        }

        servicios.AddHostedService(sp =>
        {
            var fabrica = sp.GetRequiredService<IServiceScopeFactory>();
            var registro = sp.GetRequiredService<IRegistroDeTrabajosDeFondo>();
            var actor = alcanceDeSistema(sp);
            var alertas = new AlertasPorCorridaDeIdentidad(fabrica);
            var reloj = sp.GetRequiredService<IReloj>();
            List<TrabajoDeFondoDeIdentidad> trabajos =
            [
                TrabajosDeIdentidad.Purga(fabrica, registro, actor, alertas, reloj),
                TrabajosDeIdentidad.Descarte(fabrica, registro, actor, alertas, reloj),
                TrabajosDeIdentidad.Rotacion(fabrica, registro, actor, alertas, reloj),
                TrabajosDeIdentidad.RenovacionDeClaves(sp.GetRequiredService<CacheDeClavesDeVerificacion>(), registro, actor, alertas, reloj),
                TrabajosDeIdentidad.Bajas(fabrica, registro, actor, alertas, reloj),
                // El correo de la cuenta de envío (IDE-66, IDE-78, IDE-80): el reintento de lo pendiente y, con IMAP configurado, el vaciado de Enviados.
                TrabajosDelCorreo.Reintento(fabrica, registro, actor, alertas, reloj),
            ];
            if (sp.GetService<LimpiezaDeEnviadosImap>() is { } limpieza)
            {
                trabajos.Add(TrabajosDelCorreo.Vaciado(limpieza, registro, actor, alertas, reloj));
            }

            return new ServicioDeTrabajosDeIdentidad(trabajos);
        });
        return servicios;
    }
}

/// <summary><see cref="IAlertas"/> para los trabajos de fondo, que viven más que una solicitud: cada alerta se publica en su propio alcance. Nunca interrumpe la corrida.</summary>
internal sealed class AlertasPorCorridaDeIdentidad(IServiceScopeFactory fabrica) : IAlertas
{
    public async Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarTecnica(motivo, cancelacion).ConfigureAwait(false);
    }

    public async Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarDeSeguridad(motivo, cancelacion).ConfigureAwait(false);
    }
}
