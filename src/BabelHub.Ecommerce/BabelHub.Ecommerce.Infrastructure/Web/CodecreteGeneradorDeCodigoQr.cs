using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.Extensions.Logging;
using Net.Codecrete.QrCodeGenerator;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// El código QR con la biblioteca <c>Net.Codecrete.QrCodeGenerator</c> (web-acceso D01), corrección de errores media. Ante cualquier falla devuelve nulo y deja un aviso de texto fijo: ni el contenido ni la excepción (que podría
/// citarlo) llegan a un registro (ACC-14, ACC-16).
/// </summary>
public sealed partial class CodecreteGeneradorDeCodigoQr(ILogger<CodecreteGeneradorDeCodigoQr> registro) : IGeneradorDeCodigoQr
{
    public CodigoQr? Generar(ValorSecreto contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        try
        {
            var codigo = QrCode.EncodeText(contenido.Revelar(), QrCode.Ecc.Medium);
            return new CodigoQr(codigo.Size, codigo.GetModule);
        }
        catch (Exception)
        {
            AvisarQueNoSePudoGenerar();
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo generar el código QR del segundo factor; la pantalla muestra solo la clave.")]
    private partial void AvisarQueNoSePudoGenerar();
}
