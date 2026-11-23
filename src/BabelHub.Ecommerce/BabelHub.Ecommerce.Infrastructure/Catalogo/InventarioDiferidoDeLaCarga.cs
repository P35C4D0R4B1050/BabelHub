using BabelHub.Ecommerce.Application.Catalogo.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// El Inventario «diferido» de la carga inicial (D04): la carga del catálogo <b>no llama al Inventario</b>. El registro de la edición impresa y el ingreso de mercadería de la administración usan el token de la sesión de una persona, que la orden no
/// tiene ni debe acuñar; la ficha y el stock los registra <c>cargar-stock</c> dentro del propio Inventario, con el plan que deja esta carga. Responde «sin respuesta» (que el alta nunca convierte en un fallo, CAT-20) y no llama a nadie.
/// </summary>
public sealed class InventarioDiferidoDeLaCarga : IInventarioDelCatalogo
{
    public Task<bool> RegistrarEdicionImpresa(Guid edicionId, CancellationToken cancelacion = default) => Task.FromResult(false);

    public Task<RespuestaDeDisponibilidad?> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => Task.FromResult<RespuestaDeDisponibilidad?>(null);

    public Task<RespuestaDeIngreso> RegistrarIngreso(SolicitudDeIngresoAlInventario solicitud, CancellationToken cancelacion = default) => Task.FromResult(RespuestaDeIngreso.SinRespuesta);

    public Task<IReadOnlyList<IngresoDelInventario>?> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<IngresoDelInventario>?>(null);
}
