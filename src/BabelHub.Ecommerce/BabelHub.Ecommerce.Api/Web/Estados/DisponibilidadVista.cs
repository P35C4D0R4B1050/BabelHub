namespace BabelHub.Ecommerce.Api.Web.Estados;

/// <summary>Los estados de disponibilidad que se muestran (docs/06 §1: nunca solo con color, OB01).</summary>
public enum EstadoDeDisponibilidad
{
    EnTuSucursal,
    EnOtrasSucursales,
    Agotado,
    DisponibleAlInstante,
    NoSePuedeConfirmar,

    /// <summary>Hay stock en la red (el carrito no sabe la sucursal de la persona): no promete una sucursal.</summary>
    EnLaRed,
}

/// <summary>Lo que pinta <c>bh-disponibilidad</c>: el estado, su texto y su icono (el color lo da la clase de daisyUI del estado). No simula stock: solo muestra lo que informan Catálogo e Inventario.</summary>
public sealed record DisponibilidadVista(EstadoDeDisponibilidad Estado, string Texto, string Icono, string ClaseDeColor)
{
    public static DisponibilidadVista EnTuSucursal() =>
        new(EstadoDeDisponibilidad.EnTuSucursal, "En tu sucursal", "exito", "badge-success");

    public static DisponibilidadVista EnOtrasSucursales(int cantidad) =>
        new(EstadoDeDisponibilidad.EnOtrasSucursales, cantidad == 1 ? "En 1 sucursal más" : $"En {cantidad} sucursales más", "sucursal", "badge-info");

    public static DisponibilidadVista Agotado() =>
        new(EstadoDeDisponibilidad.Agotado, "Agotado", "agotado", "badge-error");

    /// <summary>Un libro digital vigente: se entrega al instante y no consulta al Inventario.</summary>
    public static DisponibilidadVista DisponibleAlInstante() =>
        new(EstadoDeDisponibilidad.DisponibleAlInstante, "Disponible al instante", "exito", "badge-success");

    public static DisponibilidadVista NoSePuedeConfirmar() =>
        new(EstadoDeDisponibilidad.NoSePuedeConfirmar, "No se puede confirmar ahora", "aviso", "badge-warning");

    /// <summary>Hay stock vendible en la red: se usa donde no se conoce la sucursal de la persona (carrito).</summary>
    public static DisponibilidadVista EnLaRed() =>
        new(EstadoDeDisponibilidad.EnLaRed, "Hay stock en la red", "exito", "badge-success");
}
