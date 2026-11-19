using System.Globalization;
using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// El costo de envío fijo de <c>Carrito:CostoDeEnvio</c> (CAR-23, T26a): soles con a lo más dos decimales y no negativo, con punto decimal, <b>validado al arrancar</b> (si falta o no es válido el servicio no arranca y la excepción nombra la clave, no un
/// valor sin sentido). Es un valor, no un secreto. Un cambio en la configuración solo surte efecto al reiniciar: el checkout congela el envío al pasar al pago, así que un cambio no afecta una compra ya congelada.
/// </summary>
public sealed class AppSettingsOpcionesDeEnvio : ICostoDeEnvioConfigurado
{
    public const string Clave = "Carrito:CostoDeEnvio";

    private AppSettingsOpcionesDeEnvio(CostoDeEnvio valor) => Valor = valor;

    public CostoDeEnvio Valor { get; }

    public static AppSettingsOpcionesDeEnvio Cargar(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        return Desde(configuracion[Clave]);
    }

    public static AppSettingsOpcionesDeEnvio Desde(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new InvalidOperationException($"Falta la configuración «{Clave}»: el costo de envío fijo en soles (por ejemplo 12.00).");
        }

        if (!decimal.TryParse(texto, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var soles) || !CostoDeEnvio.TryCrear(soles, out var costo))
        {
            throw new InvalidOperationException($"La configuración «{Clave}» no es un costo válido: soles, con punto decimal, de 0 en adelante y con a lo más dos decimales.");
        }

        return new AppSettingsOpcionesDeEnvio(costo);
    }
}

/// <summary>
/// Los identificadores del módulo (D04, T26a): GUID v4 de un generador criptográfico y la cookie del visitante de 256 bits. El valor en claro de la cookie solo lo escribe la Presentación; el servidor guarda su SHA-256 (<c>HuellaDeLaCookie</c>).
/// </summary>
public sealed class CriptoGeneradorDeIdentificadores : IGeneradorDeIdentificadores
{
    /// <summary>Un GUID versión 4 (aleatorio) del generador criptográfico del sistema.</summary>
    public Guid NuevoId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>32 bytes aleatorios en base64url sin relleno: 43 caracteres del alfabeto <c>[A-Za-z0-9_-]</c>.</summary>
    public string NuevaCookieDeVisitante() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
