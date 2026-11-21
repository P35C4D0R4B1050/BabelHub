using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Los trabajos de fondo del correo de la cuenta de envío (IDE-66, IDE-78, IDE-80, AC-IDE-60): sin ellos lo que no tuvo cupo (alertas a la administración, avisos críticos, reclamos y pagos) se encolaba y nunca salía, y la carpeta
/// de Enviados de la cuenta nunca se vaciaba. Corren con el mismo mecanismo que los trabajos de Identidad (actor «sistema» fijado de forma explícita, resultado registrado en cada corrida, alerta técnica tras varios fallos) dentro del
/// servicio hospedado de <see cref="ComposicionDeLosTrabajosDeIdentidad"/>; viven en la composición (fuera de los módulos) porque unen a Plataforma con Identidad.
/// </summary>
public static class TrabajosDelCorreo
{
    public const string ReintentoDeLosPendientes = "correo-reintento-de-pendientes";

    public const string VaciadoDeEnviados = "correo-vaciado-de-enviados";

    /// <summary>Cada 5 minutos: la cuota se renueva a la medianoche de Lima y los reintentos fallidos esperan 1, 2, 4… minutos con tope de una hora.</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDelReintento = new(TimeSpan.FromMinutes(5));

    /// <summary>Cada 6 horas: lo enviado se elimina a los 7 días (IDE-66) y el vaciado no urge.</summary>
    public static readonly OpcionesDelTrabajoDeIdentidad IntervaloDelVaciado = new(TimeSpan.FromHours(6));

    /// <summary>El reintento de lo pendiente por prioridad y, en la misma corrida, la purga de lo que superó los 30 días o los intentos máximos (IDE-80).</summary>
    public static TrabajoDeFondoDeIdentidad Reintento(IServiceScopeFactory fabrica, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            ReintentoDeLosPendientes,
            async ct =>
            {
                using var servicios = fabrica.CreateScope();
                // La purga corre aunque el reintento falle: lo caducado (con la dirección cifrada) no debe quedarse por un fallo del transporte.
                try
                {
                    await servicios.ServiceProvider.GetRequiredService<ReintentoDeCorreosPendientes>().Procesar(ct).ConfigureAwait(false);
                }
                finally
                {
                    await servicios.ServiceProvider.GetRequiredService<PostgresColaDeCorreosPendientes>().PurgarCaducados(reloj.Ahora, CancellationToken.None).ConfigureAwait(false);
                }
            },
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDelReintento,
            tiempo);

    /// <summary>El vaciado de la carpeta de Enviados de la cuenta (cada correo queda copiado con direcciones y contenido): solo si hay IMAP configurado.</summary>
    public static TrabajoDeFondoDeIdentidad Vaciado(LimpiezaDeEnviadosImap limpieza, IRegistroDeTrabajosDeFondo registro, AlcanceDeSistemaDeIdentidad alcance, IAlertas alertas, IReloj reloj, TimeProvider? tiempo = null) =>
        new(
            VaciadoDeEnviados,
            async ct => await limpieza.Vaciar(reloj.Ahora, ct).ConfigureAwait(false),
            registro,
            alcance,
            alertas,
            reloj,
            IntervaloDelVaciado,
            tiempo);
}
