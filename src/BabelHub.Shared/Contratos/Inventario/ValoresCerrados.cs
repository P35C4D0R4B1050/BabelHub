namespace BabelHub.Shared.Contratos.Inventario;

/// <summary>Formato de una edición para el Inventario: solo las impresas tienen stock (FUE-07, SEL-12).</summary>
public static class FormatosDeEdicion
{
    public const string Impreso = "impreso";

    public const string Digital = "digital";

    public static readonly IReadOnlyList<string> Todos = [Impreso, Digital];
}

/// <summary>Estado de stock de una edición en una fuente según su vendible (FUE-18): 0, de 1 a 2 y de 3 o más.</summary>
public static class EstadosDeStock
{
    public const string SinStock = "sin-stock";

    public const string Bajo = "bajo";

    public const string Normal = "normal";

    public static readonly IReadOnlyList<string> Todos = [SinStock, Bajo, Normal];
}

/// <summary>Cómo recibe el cliente un pedido con impresos (CAR-08).</summary>
public static class TiposDeEntrega
{
    public const string Recojo = "recojo";

    public const string Domicilio = "domicilio";

    public static readonly IReadOnlyList<string> Todos = [Recojo, Domicilio];
}

/// <summary>Estados de una reserva y de sus líneas (<c>inventario-seleccion-fuente</c> §5).</summary>
public static class EstadosDeReserva
{
    public const string Pendiente = "pendiente";

    public const string Confirmada = "confirmada";

    public const string Liberada = "liberada";

    public const string Expirada = "expirada";

    public const string Cerrada = "cerrada";

    public static readonly IReadOnlyList<string> Todos = [Pendiente, Confirmada, Liberada, Expirada, Cerrada];
}

/// <summary>Por qué se cierra una reserva confirmada: se descuenta el físico al entregar un pedido de recojo (SEL-11) o al pasar a en reparto un envío (SEL-21).</summary>
public static class MotivosDeCierre
{
    public const string Entrega = "entrega";

    public const string Reparto = "reparto";

    public static readonly IReadOnlyList<string> Todos = [Entrega, Reparto];
}

/// <summary>Por qué el E-commerce libera una reserva (SEL-07, SEL-17); <c>pedido-anulado</c> y <c>cancelado-por-el-cliente</c> cancelan también los traslados vinculados (TRA-15, TRA-19).</summary>
public static class MotivosDeLiberacion
{
    public const string CheckoutCancelado = "checkout-cancelado";

    public const string PedidoAnulado = "pedido-anulado";

    public const string CanceladoPorElCliente = "cancelado-por-el-cliente";

    public const string NoRecogido = "no-recogido";

    public const string PagoVencido = "pago-vencido";

    public const string FallaPosteriorAlPago = "falla-posterior-al-pago";

    public static readonly IReadOnlyList<string> Todos =
        [CheckoutCancelado, PedidoAnulado, CanceladoPorElCliente, NoRecogido, PagoVencido, FallaPosteriorAlPago];
}

/// <summary>Claves de las políticas de límite de reservas, que viajan como <c>motivo</c> de la respuesta 429 (SEL-13, SEL-14, SEL-19).</summary>
public static class MotivosDeLimiteDeReservas
{
    public const string PendientePorCliente = "reservas-pendiente-por-cliente";

    public const string PorHoraPorCliente = "reservas-por-hora-por-cliente";

    public const string PorHoraPorOrigen = "reservas-por-hora-por-origen";

    public static readonly IReadOnlyList<string> Todos = [PendientePorCliente, PorHoraPorCliente, PorHoraPorOrigen];
}
