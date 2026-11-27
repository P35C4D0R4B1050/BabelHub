using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>La dirección <c>otpauth</c> con la que una aplicación de autenticación agrega la cuenta: la misma para el enlace y para el código QR (web-acceso ACC-13). Lleva el secreto: solo se escribe en la respuesta que ya lo muestra.</summary>
public static class ConfiguracionDelSegundoFactor
{
    public static string Direccion(SecretoDelSegundoFactor secreto)
    {
        ArgumentNullException.ThrowIfNull(secreto);
        return $"otpauth://totp/{Uri.EscapeDataString(secreto.Emisor)}:{Uri.EscapeDataString(secreto.Cuenta)}?secret={secreto.SecretoBase32.Revelar()}&issuer={Uri.EscapeDataString(secreto.Emisor)}";
    }
}
