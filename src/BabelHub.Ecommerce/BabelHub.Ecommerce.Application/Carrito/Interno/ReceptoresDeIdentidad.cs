using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// La eliminación del carrito y de los datos de entrega de una cuenta (CAR-32, CAR-33; T22; AC-CAR-28, AC-CAR-29), que Identidad pide <b>dentro de su transacción única y bajo <c>SET LOCAL ROLE rol_carrito</c></b> (el rol lo fija la Infraestructura):
/// <b>borra siempre el checkout —con su contacto, sus líneas y sus aceptaciones— y luego el carrito, con sus líneas y sus avisos</b> (orden de bloqueo de D10), de modo que un retiro de edición concurrente, que bloquea en el mismo orden, no se cruza.
/// Es <b>idempotente ante filas ausentes</b> (una reaplicación tras restaurar una copia, PLA-19) y <b>falla sin efecto parcial</b>: si no puede eliminar algo o no puede auditarlo, lanza, y Identidad revierte todo y lo informa para que la cuenta no se
/// reemplace o la cancelación no se ejecute. Nunca llama a Pagos ni al Inventario (las reservas pendientes ya impidieron la cancelación por IDE-27) y no usa el carrito de ningún visitante.
/// </summary>
internal sealed class EliminarCarritoYContacto(ServiciosDelCarrito servicios)
{
    public async Task Eliminar(Guid cuentaId, string motivo, CancellationToken cancelacion = default)
    {
        if (cuentaId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(cuentaId));
        }

        // El checkout primero y el carrito después (D10).
        await servicios.Checkouts.EliminarDeCuenta(cuentaId, cancelacion).ConfigureAwait(false);
        await servicios.Carritos.EliminarDeCuenta(cuentaId, cancelacion).ConfigureAwait(false);

        // La auditoría va en la misma transacción: si no se puede registrar, la excepción sube y todo se revierte. No lleva a la cuenta ni datos de entrega.
        await servicios.Auditoria.CarritoEliminadoPorIdentidad(motivo, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// <c>ICarritoDeCuentaReemplazada</c> (IDE-84, IDE-88; CAR-32): Carrito elimina el carrito, los avisos y el checkout de una cuenta de cliente sin verificar que se reemplaza; la cuenta nueva parte sin carrito heredado. Si no puede, lanza y el reemplazo no se hace.
/// </summary>
internal sealed class CarritoDeCuentaReemplazada(EliminarCarritoYContacto eliminar) : ICarritoDeCuentaReemplazada
{
    public Task Eliminar(Guid usuarioId, CancellationToken cancelacion = default) => eliminar.Eliminar(usuarioId, "reemplazo", cancelacion);
}

/// <summary>
/// La parte de la cancelación de la cuenta que le toca a Carrito (<c>IAplicadorDeCancelacion</c>, IDE-47, IDE-70; CAR-33): elimina carrito, avisos, datos de entrega y checkout en la misma operación. No usa el identificador opaco (Carrito no guarda copia
/// anonimizada de nada: lo elimina). Si no puede, lanza y la cancelación no se ejecuta, sin efecto parcial.
/// </summary>
internal sealed class AplicadorDeCancelacionDelCarrito(EliminarCarritoYContacto eliminar) : IAplicadorDeCancelacion
{
    public const string NombreDelModulo = "carrito";

    public string Modulo => NombreDelModulo;

    public Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default) => eliminar.Eliminar(usuarioId, "cancelacion", cancelacion);
}
