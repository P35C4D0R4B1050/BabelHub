using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>El trabajo del correo de las alertas (solo E-commerce, T18 y T27; AUD-29, AUD-38, AUD-40): cada 5 minutos envía las alertas propias pendientes y las nuevas del Inventario.</summary>
public static class TrabajoDeCorreoDeAlertas
{
    public static TrabajoDeFondoDeAuditoria Crear(
        AgruparYEnviarAlertas enviar,
        IRegistroDeTrabajosDeFondo registro,
        IEstablecedorDeActor actor,
        BabelHub.Ecommerce.Application.Auditoria.IAlertas alertas,
        IReloj reloj,
        TimeProvider? tiempo = null) =>
        new(TrabajosDeAuditoria.CorreoDeAlertas, async ct => await enviar.Ejecutar(ct).ConfigureAwait(false), registro, actor, alertas, reloj, TrabajosDeAuditoria.IntervaloDelCorreo, tiempo);

    /// <summary>El mismo trabajo con una corrida por alcance de servicios (la del anfitrión): los casos de uso dependen del actor y de la unidad de trabajo, que son por alcance.</summary>
    public static TrabajoDeFondoDeAuditoria Crear(
        IServiceScopeFactory fabrica,
        IRegistroDeTrabajosDeFondo registro,
        IEstablecedorDeActor actor,
        BabelHub.Ecommerce.Application.Auditoria.IAlertas alertas,
        IReloj reloj,
        TimeProvider? tiempo = null) =>
        new(
            TrabajosDeAuditoria.CorreoDeAlertas,
            async ct =>
            {
                using var alcance = fabrica.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<AgruparYEnviarAlertas>().Ejecutar(ct).ConfigureAwait(false);
            },
            registro,
            actor,
            alertas,
            reloj,
            TrabajosDeAuditoria.IntervaloDelCorreo,
            tiempo);
}
