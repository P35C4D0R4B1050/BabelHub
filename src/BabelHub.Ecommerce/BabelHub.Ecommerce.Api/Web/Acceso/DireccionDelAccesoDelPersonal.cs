using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Api.Web.Acceso;

/// <summary>
/// La dirección del host de gestión a la que lleva el enlace «Acceso del personal» de la tienda (web-acceso ACC-10 a ACC-12): <c>BABELHUB__Web__DireccionDeGestion</c>. Es <b>opcional</b> (sin ella no hay enlace) y sale solo de la
/// configuración, nunca de la solicitud. Solo la fábrica <see cref="Leer"/> la crea, ya validada: una dirección segura de un solo origen. El mensaje de error nombra la variable y nunca su valor.
/// </summary>
public sealed class DireccionDelAccesoDelPersonal
{
    public const string Ruta = "Web:DireccionDeGestion";

    private DireccionDelAccesoDelPersonal(string enlace) => Enlace = enlace;

    /// <summary>El origen configurado con su <c>/</c> final: lo que se escribe en el <c>href</c>.</summary>
    public string Enlace { get; }

    /// <summary>
    /// Ausente, vacía o con solo espacios: nulo y ningún problema (ACC-11). Mal formada: nulo y un problema en el <paramref name="lector"/> (ACC-12). <paramref name="permitirHttpLocal"/> (solo <c>Development</c> y <c>Test</c>) admite
    /// <c>http</c> hacia la propia máquina.
    /// </summary>
    internal static DireccionDelAccesoDelPersonal? Leer(IConfiguration configuracion, LectorDeSecretos lector, bool permitirHttpLocal)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentNullException.ThrowIfNull(lector);
        var valor = configuracion[Ruta];
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        if (Valida(valor, permitirHttpLocal) is { } direccion)
        {
            return new DireccionDelAccesoDelPersonal(direccion.GetLeftPart(UriPartial.Authority) + "/");
        }

        lector.Problema(Ruta, "no es una dirección segura de un solo origen (https://host o https://host:puerto, sin espacios, usuario, ruta, consulta ni fragmento)");
        return null;
    }

    private static Uri? Valida(string valor, bool permitirHttpLocal)
    {
        // Se mira el texto escrito y no solo lo que entiende Uri, que perdona espacios, barras invertidas y una «?» o un «#» vacíos. Solo ASCII y sin «%»: un homógrafo o un
        // carácter invisible pegado en el nombre no pasa (un dominio con tildes se escribe en su forma punycode).
        if (valor.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c) || c is '\\' or '%' or '?' or '#' or '@' or '"' or '\'' or '<' or '>')
            || !(valor.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || valor.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            || !Uri.TryCreate(valor, UriKind.Absolute, out var direccion)
            || direccion.Host.Length == 0
            || direccion.UserInfo.Length > 0
            || direccion.AbsolutePath != "/")
        {
            return null;
        }

        // Una ruta escrita que Uri normaliza («/./», «/x/..») tampoco es un origen: después de la autoridad solo puede ir una «/».
        var trasElEsquema = valor[(valor.IndexOf("//", StringComparison.Ordinal) + 2)..];
        var corte = trasElEsquema.IndexOf('/', StringComparison.Ordinal);
        if (corte >= 0 && corte != trasElEsquema.Length - 1)
        {
            return null;
        }

        return direccion.Scheme == Uri.UriSchemeHttps || (permitirHttpLocal && direccion.Scheme == Uri.UriSchemeHttp && direccion.IsLoopback) ? direccion : null;
    }
}
