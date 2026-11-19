using BabelHub.Ecommerce.Application.Plataforma.Interno;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// La configuración de la cuenta de envío (T34; IDE-66): el servidor SMTP con <b>STARTTLS obligatorio</b>, la cuenta y su credencial (variables de entorno, nunca el repositorio), el remitente genérico (sin dominio propio por
/// ahora, bloqueo B08) y, para limpiar la carpeta de enviados, el servidor IMAP. La credencial no se imprime.
/// </summary>
public sealed class OpcionesDelCorreoSmtp
{
    public OpcionesDelCorreoSmtp(string servidor, int puerto, string cuenta, string credencial, string remitente, string nombreDelRemitente = "BabelHub")
    {
        if (string.IsNullOrWhiteSpace(servidor) || servidor.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("El servidor SMTP es un nombre de host.", nameof(servidor));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(puerto, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(puerto, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(cuenta);
        ArgumentException.ThrowIfNullOrWhiteSpace(credencial);
        if (!MailboxAddress.TryParse(remitente, out _))
        {
            throw new ArgumentException("El remitente es una dirección de correo válida.", nameof(remitente));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(nombreDelRemitente);
        Servidor = servidor;
        Puerto = puerto;
        Cuenta = cuenta;
        Credencial = credencial;
        Remitente = remitente;
        NombreDelRemitente = nombreDelRemitente;
    }

    public string Servidor { get; }

    public int Puerto { get; }

    public string Cuenta { get; }

    public string Credencial { get; }

    public string Remitente { get; }

    public string NombreDelRemitente { get; }

    public TimeSpan Espera { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>El servidor IMAP de la misma cuenta (para vaciar la carpeta de enviados); <see langword="null"/> si no se limpia.</summary>
    public string? ServidorImap { get; init; }

    public int PuertoImap { get; init; } = 993;

    public string CarpetaDeEnviados { get; init; } = "[Gmail]/Enviados";

    public override string ToString() => "[configuración del correo]";
}

/// <summary>
/// El transporte SMTP de la cuenta de envío (T34; IDE-65, IDE-66) con MailKit: conexión con <b>STARTTLS obligatorio</b> (nunca en claro), plazo de 15 segundos y un mensaje de texto plano para cada destinatario. Lanza si no
/// pudo entregar el mensaje al servidor; nunca decide cuota ni cola. Una dirección o un asunto con saltos de línea no pasan (MimeKit valida las cabeceras). Ni el contenido ni las direcciones se registran.
/// </summary>
public sealed class SmtpTransporteDeCorreo(OpcionesDelCorreoSmtp opciones) : ITransporteDeCorreo
{
    public async Task Enviar(MensajeDeCorreo mensaje, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        if (mensaje.Destinatarios.Count == 0)
        {
            throw new ArgumentException("El correo necesita al menos un destinatario.", nameof(mensaje));
        }

        var correo = new MimeMessage();
        correo.From.Add(new MailboxAddress(opciones.NombreDelRemitente, opciones.Remitente));
        foreach (var destinatario in mensaje.Destinatarios)
        {
            // Una dirección con espacios, comas o saltos de línea no es una sola dirección: se rechaza antes de tocar la red.
            if (destinatario.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>') || !MailboxAddress.TryParse(destinatario, out var direccion))
            {
                throw new ArgumentException("Un destinatario no es una dirección válida.", nameof(mensaje));
            }

            correo.To.Add(direccion);
        }

        correo.Subject = mensaje.Asunto;
        correo.Body = new TextPart(TextFormat.Plain) { Text = mensaje.Cuerpo };

        using var cliente = new SmtpClient { Timeout = (int)opciones.Espera.TotalMilliseconds };
        await cliente.ConnectAsync(opciones.Servidor, opciones.Puerto, SecureSocketOptions.StartTls, cancelacion).ConfigureAwait(false);
        await cliente.AuthenticateAsync(opciones.Cuenta, opciones.Credencial, cancelacion).ConfigureAwait(false);
        await cliente.SendAsync(correo, cancelacion).ConfigureAwait(false);
        await cliente.DisconnectAsync(quit: true, cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// El transporte cuando <b>no hay una cuenta de envío configurada</b> (T34): <b>falla cerrado</b>. Nunca finge entregar: lanza siempre, de modo que lo que se reintenta se encola y se publica la alerta técnica en vez de
/// darse por enviado. Es el valor por defecto en producción; un doble que acepta todo solo existe en las pruebas.
/// </summary>
public sealed class TransporteDeCorreoNoConfigurado : ITransporteDeCorreo
{
    public Task Enviar(MensajeDeCorreo mensaje, CancellationToken cancelacion = default) =>
        throw new InvalidOperationException("No hay una cuenta de envío de correo configurada.");
}

/// <summary>
/// El vaciado de la carpeta de enviados de la cuenta (T34; IDE-66): con una cuenta sencilla de Gmail cada correo queda copiado en <b>Enviados</b>, con direcciones y contenido; cada 7 días se eliminan los de más de 7 días
/// (se marcan como eliminados y se expulsan). Conecta por IMAP con TLS directo y sin guardar nada.
/// </summary>
public sealed class LimpiezaDeEnviadosImap(OpcionesDelCorreoSmtp opciones)
{
    public static readonly TimeSpan Antiguedad = TimeSpan.FromDays(7);

    /// <summary>Elimina los correos enviados antes del límite y devuelve cuántos; con la cuenta sin IMAP configurado no hace nada.</summary>
    public async Task<int> Vaciar(DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        if (opciones.ServidorImap is null)
        {
            return 0;
        }

        using var cliente = new ImapClient { Timeout = (int)opciones.Espera.TotalMilliseconds };
        await cliente.ConnectAsync(opciones.ServidorImap, opciones.PuertoImap, SecureSocketOptions.SslOnConnect, cancelacion).ConfigureAwait(false);
        await cliente.AuthenticateAsync(opciones.Cuenta, opciones.Credencial, cancelacion).ConfigureAwait(false);
        var carpeta = await cliente.GetFolderAsync(opciones.CarpetaDeEnviados, cancelacion).ConfigureAwait(false);
        await carpeta.OpenAsync(FolderAccess.ReadWrite, cancelacion).ConfigureAwait(false);
        var viejos = await carpeta.SearchAsync(SearchQuery.DeliveredBefore(ahora.UtcDateTime - Antiguedad), cancelacion).ConfigureAwait(false);
        if (viejos.Count > 0)
        {
            await carpeta.AddFlagsAsync(viejos, MessageFlags.Deleted, silent: true, cancelacion).ConfigureAwait(false);
            await carpeta.ExpungeAsync(cancelacion).ConfigureAwait(false);
        }

        await cliente.DisconnectAsync(quit: true, cancelacion).ConfigureAwait(false);
        return viejos.Count;
    }
}
