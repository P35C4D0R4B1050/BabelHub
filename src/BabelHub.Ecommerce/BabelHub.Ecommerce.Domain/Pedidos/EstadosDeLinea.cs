namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>
/// Los estados de una línea impresa de un pedido (PED-02): confirmado, en tránsito entre sucursales (solo con traslado), recibido en la sucursal (tras el traslado), en preparación, listo para recojo (solo recojo) o despachado (solo domicilio),
/// entregado, no recogido (solo recojo) y anulado. Una devolución posterior no cambia el estado de la línea.
/// </summary>
public enum EstadoDeLineaImpresa
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

/// <summary>Los estados de una línea digital (PED-03): nace <see cref="Disponible"/> cuando la biblioteca concede el acceso y puede anularse.</summary>
public enum EstadoDeLineaDigital
{
    Disponible,
    Anulado,
}

/// <summary>
/// Lo que decide el camino de una línea impresa: si espera un traslado desde otra sucursal y si el pedido es de recojo (listo para recojo, no recogido) o a domicilio (despachado).
/// </summary>
public readonly record struct ContextoDeLinea(bool RequiereTraslado, bool EsDeRecojo);

/// <summary>
/// La máquina de transiciones de una línea (PED-02, PED-19, PED-30): <c>confirmado → [en tránsito → recibido →] en preparación → listo para recojo | despachado → entregado</c>, con <c>no recogido</c> (desde listo para recojo, solo recojo) y <c>anulado</c>
/// (lo que no salió: nunca despachado, entregado ni anulado). Un salto (de confirmado a entregado) se rechaza; una línea con traslado no se prepara sin recibirse y una sin traslado no pasa por el tránsito; entregado y anulado son terminales.
/// Un no recogido todavía puede entregarse o anularse (PED-16, PED-31).
/// </summary>
public static class MaquinaDeEstadosDeLinea
{
    public static bool Permite(EstadoDeLineaImpresa desde, EstadoDeLineaImpresa hacia, ContextoDeLinea contexto)
    {
        if (desde == hacia)
        {
            return false;
        }

        if (hacia == EstadoDeLineaImpresa.Anulado)
        {
            return desde.SeAnula();
        }

        return (desde, hacia) switch
        {
            (EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnTransito) => contexto.RequiereTraslado,
            (EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnPreparacion) => !contexto.RequiereTraslado,
            (EstadoDeLineaImpresa.EnTransito, EstadoDeLineaImpresa.Recibido) => contexto.RequiereTraslado,
            (EstadoDeLineaImpresa.Recibido, EstadoDeLineaImpresa.EnPreparacion) => contexto.RequiereTraslado,
            (EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.ListoParaRecojo) => contexto.EsDeRecojo,
            (EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.Despachado) => !contexto.EsDeRecojo,
            (EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.NoRecogido) => contexto.EsDeRecojo,
            (EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.Entregado) => contexto.EsDeRecojo,
            (EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.Entregado) => contexto.EsDeRecojo,
            (EstadoDeLineaImpresa.Despachado, EstadoDeLineaImpresa.Entregado) => !contexto.EsDeRecojo,
            _ => false,
        };
    }

    public static bool PermiteDigital(EstadoDeLineaDigital desde, EstadoDeLineaDigital hacia) =>
        desde == EstadoDeLineaDigital.Disponible && hacia == EstadoDeLineaDigital.Anulado;
}

public static class EstadoDeLineaImpresaExtensiones
{
    /// <summary>Lo que no salió se puede anular (PED-13): confirmado, en tránsito, recibido, en preparación, listo para recojo y no recogido; nunca lo despachado, lo entregado ni lo ya anulado.</summary>
    public static bool SeAnula(this EstadoDeLineaImpresa estado) => estado is
        EstadoDeLineaImpresa.Confirmado or EstadoDeLineaImpresa.EnTransito or EstadoDeLineaImpresa.Recibido or EstadoDeLineaImpresa.EnPreparacion
        or EstadoDeLineaImpresa.ListoParaRecojo or EstadoDeLineaImpresa.NoRecogido;

    /// <summary>La línea ya salió hacia el cliente (despachada o entregada): no se anula (PED-27).</summary>
    public static bool Salio(this EstadoDeLineaImpresa estado) => estado is EstadoDeLineaImpresa.Despachado or EstadoDeLineaImpresa.Entregado;

    public static bool EsTerminal(this EstadoDeLineaImpresa estado) => estado is EstadoDeLineaImpresa.Entregado or EstadoDeLineaImpresa.Anulado;

    /// <summary>El orden de avance para PED-20 y PED-30: confirmado, en tránsito, recibido, en preparación, listo para recojo o despachado (igual), entregado. Un no recogido cuenta como listo para recojo (de donde viene); un anulado no tiene orden.</summary>
    public static int? OrdenONulo(this EstadoDeLineaImpresa estado) => estado switch
    {
        EstadoDeLineaImpresa.Confirmado => 1,
        EstadoDeLineaImpresa.EnTransito => 2,
        EstadoDeLineaImpresa.Recibido => 3,
        EstadoDeLineaImpresa.EnPreparacion => 4,
        EstadoDeLineaImpresa.ListoParaRecojo or EstadoDeLineaImpresa.Despachado or EstadoDeLineaImpresa.NoRecogido => 5,
        EstadoDeLineaImpresa.Entregado => 6,
        _ => null,
    };

    /// <summary>El orden de un estado que lo tiene; pedirlo de un anulado es un error de programación.</summary>
    public static int Orden(this EstadoDeLineaImpresa estado) =>
        estado.OrdenONulo() ?? throw new InvalidOperationException("Una línea anulada no tiene orden de avance.");
}
