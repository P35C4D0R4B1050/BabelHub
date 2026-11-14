using System.Globalization;

namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// Los nombres con los que la Api establece el actor «integración» (<c>ActorActual.DeIntegracion</c>) cuando una llamada llega con una credencial de servicio de
/// este alcance (AUD-23): son los mismos nombres de los alcances de <c>docs/03/08</c>. La Aplicación compara contra ellos, de modo que una credencial de otro alcance no sirve para
/// leer las alertas nuevas ni para entregar la baja de un titular aunque la Api se equivoque de ruta (defensa en profundidad, AGENTS.md §5).
/// </summary>
public static class IntegracionesDeAuditoria
{
    /// <summary>Alcance <c>lectura-alertas</c>: el E-commerce lee las alertas nuevas del Inventario (AUD-38).</summary>
    public const string LecturaDeAlertas = "lectura-alertas";

    /// <summary>Alcance <c>baja-de-titular</c>: el E-commerce entrega la baja de un titular al Inventario (AUD-32).</summary>
    public const string BajaDeTitular = "baja-de-titular";
}

/// <summary>El identificador de una alerta: el prefijo de su servicio y un GUID canónico en minúsculas; cualquier otra forma no llega al almacén ni al cliente HTTP.</summary>
public static class IdentificadorDeAlerta
{
    public static bool EsValido(string? id, string prefijo)
    {
        if (id is null || !id.StartsWith(prefijo, StringComparison.Ordinal) || id.Length != prefijo.Length + 36)
        {
            return false;
        }

        var texto = id[prefijo.Length..];
        return Guid.TryParseExact(texto, "D", out var guid) && guid != Guid.Empty && string.Equals(texto, guid.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
