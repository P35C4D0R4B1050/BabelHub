using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Devoluciones;

/// <summary>
/// La resolución del <b>código</b> de la sucursal de un actor (plan §6, DEV-13): el identificador del token se compara con el que <see cref="IdentificadorDeSucursal"/> deriva de cada código de las sucursales <b>activas</b> del Inventario
/// (la equivalencia canónica D20; el mismo helper que usan Identidad y Pedidos, nunca otra copia). Falla cerrado: una sucursal que no está activa, un identificador desconocido o el Inventario caído no se resuelven (el alcance se niega y se registra).
/// </summary>
internal sealed class ResolucionDeSucursalDeDevoluciones(IInventarioDeDevoluciones inventario) : IResolucionDeSucursal
{
    public async Task<string?> CodigoDe(Guid sucursalId, CancellationToken cancelacion = default)
    {
        if (sucursalId == Guid.Empty)
        {
            return null;
        }

        var activas = await inventario.ListarSucursalesActivas(cancelacion).ConfigureAwait(false);
        if (activas is not ResultadoDeSucursalesActivas.Lista lista)
        {
            return null;
        }

        return lista.Codigos.FirstOrDefault(codigo => IdentificadorDeSucursal.DesdeCodigo(codigo) == sucursalId);
    }
}
