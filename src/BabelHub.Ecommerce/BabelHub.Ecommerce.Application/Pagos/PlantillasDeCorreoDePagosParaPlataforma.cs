using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pagos;

/// <summary>
/// Las plantillas de correo de Pagos, ofrecidas al renderizador de Plataforma (PLA-X; PAG-05, PAG-21): pago pendiente, aprobado, rechazado (con el motivo de la lista cerrada), vencido y reembolso automático (con el
/// importe). Son las mismas del módulo, con sus campos declarados: Plataforma pone solo <c>motivo</c> e <c>importe</c> y ningún dato de pago. La composición las registra junto a las de seguridad y las de reclamaciones.
/// </summary>
public sealed class PlantillasDeCorreoDePagosParaPlataforma : IProveedorDePlantillasDeCorreo
{
    public IReadOnlyDictionary<string, PlantillaDeCorreo> Plantillas { get; } =
        PlantillasDeCorreoDePagos.Catalogo.ToDictionary(p => p.Nombre, p => new PlantillaDeCorreo(p.Asunto, p.Cuerpo, p.Campos), StringComparer.Ordinal);
}
