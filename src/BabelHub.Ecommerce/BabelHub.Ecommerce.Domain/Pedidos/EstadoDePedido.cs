namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>
/// El estado derivado de un pedido (PED-20): se guarda desnormalizado para filtrar y paginar, y se recalcula <b>en la misma operación</b> que cambia una línea. Sus valores coinciden con los de la línea impresa que lo determina.
/// </summary>
public enum EstadoDePedido
{
    Confirmado,
    EnTransito,
    Recibido,
    EnPreparacion,
    ListoParaRecojo,
    Despachado,
    Entregado,
    NoRecogido,
    Anulado,
}

public static class EstadoDePedidoExtensiones
{
    /// <summary>Entregado y anulado son los estados finales: desde su último cambio corren los dos años de la retención (PED-34).</summary>
    public static bool EsFinal(this EstadoDePedido estado) => estado is EstadoDePedido.Entregado or EstadoDePedido.Anulado;
}

/// <summary>
/// La derivación pura del estado del pedido (PED-20, PED-30): <b>anulado</b> si todas sus líneas lo están; en otro caso, <b>ignorando las líneas anuladas y las digitales disponibles</b>: <b>entregado</b> si las demás están entregadas (también si no quedan
/// otras: un pedido solo digital con el acceso concedido, PED-12), <b>no recogido</b> si alguna lo está y las demás están entregadas o no recogidas y, si no, el de su <b>línea impresa menos avanzada</b> (confirmado, en tránsito, recibido, en preparación, listo para
/// recojo o despachado). Una línea no recogida cuenta como listo para recojo en el orden.
/// </summary>
public static class DerivacionDelEstadoDelPedido
{
    public static EstadoDePedido Derivar(IReadOnlyCollection<EstadoDeLineaImpresa> impresas, IReadOnlyCollection<EstadoDeLineaDigital> digitales)
    {
        ArgumentNullException.ThrowIfNull(impresas);
        ArgumentNullException.ThrowIfNull(digitales);
        if (impresas.Count + digitales.Count == 0)
        {
            throw new ArgumentException("Un pedido tiene al menos una línea.", nameof(impresas));
        }

        if (impresas.All(e => e == EstadoDeLineaImpresa.Anulado) && digitales.All(e => e == EstadoDeLineaDigital.Anulado))
        {
            return EstadoDePedido.Anulado;
        }

        var vivas = impresas.Where(e => e != EstadoDeLineaImpresa.Anulado).ToList();
        if (vivas.All(e => e == EstadoDeLineaImpresa.Entregado))
        {
            return EstadoDePedido.Entregado;
        }

        if (vivas.Any(e => e == EstadoDeLineaImpresa.NoRecogido) && vivas.All(e => e is EstadoDeLineaImpresa.Entregado or EstadoDeLineaImpresa.NoRecogido))
        {
            return EstadoDePedido.NoRecogido;
        }

        // La menos avanzada manda; entre iguales (un no recogido y un listo comparten orden) la que sigue pendiente.
        var menosAvanzada = vivas
            .Where(e => e != EstadoDeLineaImpresa.Entregado)
            .OrderBy(e => e.Orden())
            .ThenBy(e => e == EstadoDeLineaImpresa.NoRecogido ? 1 : 0)
            .First();
        return DeLaLinea(menosAvanzada);
    }

    private static EstadoDePedido DeLaLinea(EstadoDeLineaImpresa estado) => estado switch
    {
        EstadoDeLineaImpresa.Confirmado => EstadoDePedido.Confirmado,
        EstadoDeLineaImpresa.EnTransito => EstadoDePedido.EnTransito,
        EstadoDeLineaImpresa.Recibido => EstadoDePedido.Recibido,
        EstadoDeLineaImpresa.EnPreparacion => EstadoDePedido.EnPreparacion,
        EstadoDeLineaImpresa.ListoParaRecojo => EstadoDePedido.ListoParaRecojo,
        EstadoDeLineaImpresa.Despachado => EstadoDePedido.Despachado,
        EstadoDeLineaImpresa.NoRecogido => EstadoDePedido.NoRecogido,
        _ => throw new InvalidOperationException("Una línea entregada o anulada no determina el estado del pedido."),
    };
}
