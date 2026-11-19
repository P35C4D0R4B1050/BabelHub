using System.Globalization;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Una plantilla de correo del estado del pago: el nombre con el que <c>Plataforma</c> la registra en su catálogo, el asunto, el cuerpo y los únicos campos que admite. El texto sigue la guía de contenido
/// (<c>docs/06-experiencia-de-usuario/08-contenido-y-mensajes.md</c>): español del Perú, tuteo respetuoso, frases cortas, qué pasó y qué hacer. <b>Nunca</b> lleva datos de pago (tarjeta, token, referencia de la
/// pasarela), enlaces ni peticiones de datos o contraseñas (IDE-67).
/// </summary>
internal sealed record PlantillaDeCorreoDePago(string Nombre, string Asunto, string Cuerpo, IReadOnlyList<string> Campos);

/// <summary>
/// El catálogo de las cinco plantillas de correo de Pagos (T21, PAG-22, PAG-23). Pagos es la dueña del contenido; el carril de <c>Plataforma</c> las registra por su nombre en el catálogo de su
/// <c>EnviadorDeCorreoSmtp</c> (<c>specs/ecommerce-pagos/plantillas-de-correo.md</c> las lista para copiarlas sin reinterpretarlas). Una prueba fija que cada <c>{campo}</c> del texto esté declarado y que ningún
/// texto lleve datos de pago ni enlaces.
/// </summary>
internal static class PlantillasDeCorreoDePagos
{
    public const string Motivo = "motivo";
    public const string Importe = "importe";

    private static readonly PlantillaDeCorreoDePago[] Todas =
    [
        new("pago-pendiente", "Estamos confirmando tu pago", "Hola. Tu pago todavía se está confirmando con la pasarela. No tienes que hacer nada ni volver a pagar: te avisaremos apenas esté confirmado. Si no se confirma en 30 minutos, el pago vence y, si el cobro llegara a registrarse, te lo devolvemos automáticamente.", []),
        new("pago-aprobado", "Recibimos tu pago", "Hola. Tu pago fue confirmado y tu compra ya está en marcha. Puedes ver su detalle en Mis pedidos.", []),
        new("pago-rechazado", "No pudimos procesar tu pago", "Hola. Tu pago no se pudo completar: {motivo}. Puedes intentarlo de nuevo con el mismo medio o con otro mientras tu compra siga vigente.", [Motivo]),
        new("pago-vencido", "Tu pago venció", "Hola. No recibimos la confirmación de tu pago a tiempo y liberamos la reserva de tus libros. Si el cobro llegara a registrarse, te lo devolvemos automáticamente. Puedes volver a intentar la compra desde tu carrito.", []),
        new("reembolso-automatico", "Te devolvemos tu pago", "Hola. Tu pago de {importe} no pudo convertirse en una compra y lo estamos devolviendo al medio con el que pagaste. Tu banco puede tardar unos días en reflejarlo. No tienes que hacer nada.", [Importe]),
    ];

    public static IReadOnlyList<PlantillaDeCorreoDePago> Catalogo => Todas;

    public static PlantillaDeCorreoDePago Para(EventoDeAvisoDePago evento) => evento switch
    {
        EventoDeAvisoDePago.Pendiente => Todas[0],
        EventoDeAvisoDePago.Aprobado => Todas[1],
        EventoDeAvisoDePago.Rechazado => Todas[2],
        EventoDeAvisoDePago.Vencido => Todas[3],
        _ => Todas[4],
    };

    /// <summary>El motivo de un rechazo en palabras de la persona, de la lista cerrada (el texto de la pasarela nunca se muestra, D13).</summary>
    public static string TextoDelMotivo(MotivoDeRechazo? motivo) => motivo switch
    {
        MotivoDeRechazo.TarjetaRechazada => "tu tarjeta fue rechazada",
        MotivoDeRechazo.FondosInsuficientes => "los fondos no fueron suficientes",
        MotivoDeRechazo.TarjetaVencida => "tu tarjeta está vencida",
        MotivoDeRechazo.CodigoDeSeguridadInvalido => "el código de seguridad no es válido",
        MotivoDeRechazo.YapeRechazado => "Yape no aprobó el pago",
        MotivoDeRechazo.MedioNoPermitido => "ese medio de pago no está permitido",
        _ => "no pudimos confirmar el pago con tu medio",
    };

    /// <summary>Un importe en soles con el formato de la guía de contenido: <c>S/ 1 234,50</c>.</summary>
    public static string ImporteEnSoles(decimal valor) =>
        "S/ " + valor.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(',', ' ').Replace('.', ',');
}
