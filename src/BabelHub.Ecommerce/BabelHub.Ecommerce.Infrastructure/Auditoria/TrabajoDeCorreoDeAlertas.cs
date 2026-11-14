using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

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
}
