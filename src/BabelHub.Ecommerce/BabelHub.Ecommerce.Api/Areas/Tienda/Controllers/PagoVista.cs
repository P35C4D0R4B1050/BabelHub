using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Pagos;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>El motivo de un rechazo en palabras de la persona: nunca el texto de la pasarela (D13).</summary>
public static class TextosDelPago
{
    public static string Motivo(MotivoDeRechazoDePago motivo) => motivo switch
    {
        MotivoDeRechazoDePago.TarjetaRechazada => "Tu tarjeta fue rechazada. Prueba con otra tarjeta o con Yape.",
        MotivoDeRechazoDePago.FondosInsuficientes => "Tu medio de pago no tiene fondos suficientes.",
        MotivoDeRechazoDePago.TarjetaVencida => "Tu tarjeta está vencida.",
        MotivoDeRechazoDePago.CodigoDeSeguridadInvalido => "El código de seguridad no es válido.",
        MotivoDeRechazoDePago.YapeRechazado => "Yape rechazó el pago. Revisa tu aplicación o prueba con otro medio.",
        MotivoDeRechazoDePago.MedioNoPermitido => "Ese medio de pago no está permitido para esta compra.",
        MotivoDeRechazoDePago.AutenticacionRequerida => "Tu banco pidió una verificación que no se completó.",
        _ => "No pudimos procesar el pago con ese medio.",
    };

    public static string Estado(EstadoDelPagoDeLaCompra estado) => estado switch
    {
        EstadoDelPagoDeLaCompra.ListoParaPagar => "Listo para pagar",
        EstadoDelPagoDeLaCompra.ConfirmandoElPago => "Estamos confirmando tu pago",
        EstadoDelPagoDeLaCompra.Aprobado => "Pago aprobado: estamos terminando tu compra",
        EstadoDelPagoDeLaCompra.Completado => "Compra confirmada",
        _ => "Esta compra ya no admite pagos",
    };
}

/// <summary>La pantalla de pago (T28, T33): el resumen, la clave firmada del intento, la cuenta regresiva y el estado.</summary>
public sealed class PagoVista : ModeloBaseDePagina
{
    public Guid PedidoId { get; set; }

    public ClaveDeIntentoEmitida? Clave { get; set; }

    public EstadoDelPagoVista Estado { get; set; } = null!;

    public string EstadoTexto => TextosDelPago.Estado(Estado.Estado);

    public string? MotivoTexto => Estado.UltimoMotivo is { } motivo ? TextosDelPago.Motivo(motivo) : null;
}
