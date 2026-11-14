using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Domain.Web;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>La clave de sesiones no es válida (falta, es corta, casi no varía o se repite); el mensaje nombra la variable y el motivo, <b>nunca el valor</b>.</summary>
public sealed class ClaveDeSesionesInvalidaException : Exception
{
    public ClaveDeSesionesInvalidaException(IReadOnlyList<string> problemas)
        : base("La clave de sesiones no es válida: " + string.Join("; ", problemas))
    {
        Problemas = problemas;
    }

    public ClaveDeSesionesInvalidaException()
        : this(["sin detalle"])
    {
    }

    public ClaveDeSesionesInvalidaException(string message)
        : this([message])
    {
    }

    public ClaveDeSesionesInvalidaException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problemas = [message];
    }

    public IReadOnlyList<string> Problemas { get; }
}

/// <summary>
/// La clave maestra de sesiones <b>de un host</b> (plan §3, §6; <c>docs/05-seguridad/07-operacion-segura.md</c> §3.2): <c>BABELHUB__Web__ClaveDeSesionesTienda</c> o
/// <c>…Gestion</c>, con su <c>…Anterior</c> durante una rotación en dos pasos. Solo la VM de ese host la tiene. Obligatoria, de al menos 32 bytes, con variedad de
/// caracteres y distinta de la anterior y de la del otro host si esta apareciera. El identificador de cada clave (<see cref="IdentificadorDeClave"/>) se deriva de la
/// propia clave, de modo que la rotación no depende de un contador que alguien deba mantener.
/// </summary>
public sealed class ConfiguracionDeSesionesWeb
{
    public const int LongitudMinimaEnBytes = 32;
    private const int CaracteresDistintosMinimos = 8;

    private ConfiguracionDeSesionesWeb(HostDeSesion host, string rutaVigente, string vigente, string? rutaAnterior, string? anterior)
    {
        Host = host;
        Vigente = Encoding.UTF8.GetBytes(vigente);
        Anterior = anterior is null ? null : Encoding.UTF8.GetBytes(anterior);
        Secretos = anterior is null ? [(rutaVigente, vigente)] : [(rutaVigente, vigente), (rutaAnterior!, anterior)];
    }

    public HostDeSesion Host { get; }

    public byte[] Vigente { get; }

    /// <summary>La clave de la rotación en curso: solo descifra; nunca cifra.</summary>
    public byte[]? Anterior { get; }

    /// <summary>
    /// Las rutas y los valores que el servicio debe comprobar <b>contra sus demás secretos</b> (que no se repitan): lo hace quien compone el servicio con su lector de secretos
    /// común; esta clase solo conoce las suyas.
    /// </summary>
    public IReadOnlyList<(string Ruta, string Valor)> Secretos { get; }

    /// <summary>El nombre de la variable de entorno de una ruta de configuración (<c>Web:ClaveDeSesionesTienda</c> → <c>BABELHUB__Web__ClaveDeSesionesTienda</c>).</summary>
    public static string Variable(string ruta) => "BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal);

    public static string RutaDeLaClave(HostDeSesion host) => host == HostDeSesion.Tienda ? "Web:ClaveDeSesionesTienda" : "Web:ClaveDeSesionesGestion";

    /// <summary>
    /// Lee y valida la clave de <paramref name="host"/>. Junta todos los problemas antes de fallar y los nombra por la variable, nunca por su valor.
    /// </summary>
    /// <exception cref="ClaveDeSesionesInvalidaException">Si falta, es débil, o se repite.</exception>
    public static ConfiguracionDeSesionesWeb Leer(IConfiguration configuracion, HostDeSesion host)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var problemas = new List<string>();
        var rutaVigente = RutaDeLaClave(host);
        var rutaAnterior = rutaVigente + "Anterior";
        var vigente = Leer(configuracion, rutaVigente, obligatoria: true, problemas);
        var anterior = Leer(configuracion, rutaAnterior, obligatoria: false, problemas);

        if (vigente is not null && anterior is not null && string.Equals(vigente, anterior, StringComparison.Ordinal))
        {
            problemas.Add($"{Variable(rutaAnterior)}: es igual a {Variable(rutaVigente)}");
        }

        // La del otro host no debería estar en esta VM, pero si apareciera (desarrollo) no puede ser la misma: una clave comprometida no abriría las sesiones del otro host.
        var rutaDelOtro = RutaDeLaClave(host == HostDeSesion.Tienda ? HostDeSesion.Gestion : HostDeSesion.Tienda);
        var delOtro = configuracion[rutaDelOtro];
        if (vigente is not null && !string.IsNullOrWhiteSpace(delOtro) && string.Equals(vigente, delOtro, StringComparison.Ordinal))
        {
            problemas.Add($"{Variable(rutaVigente)}: es igual a {Variable(rutaDelOtro)}");
        }

        if (problemas.Count > 0 || vigente is null)
        {
            throw new ClaveDeSesionesInvalidaException(problemas);
        }

        var resultado = new ConfiguracionDeSesionesWeb(host, rutaVigente, vigente, rutaAnterior, anterior);
        if (resultado.Anterior is not null && IdentificadorDeClave(resultado.Vigente) == IdentificadorDeClave(resultado.Anterior))
        {
            throw new ClaveDeSesionesInvalidaException([$"{Variable(rutaAnterior)}: coincide en su identificador con {Variable(rutaVigente)}; use otra clave"]);
        }

        return resultado;
    }

    /// <summary>
    /// Una clave de prueba o de un servicio que ya validó sus secretos: la misma comprobación, sin leer la configuración.
    /// </summary>
    public static ConfiguracionDeSesionesWeb Crear(HostDeSesion host, string vigente, string? anterior = null) =>
        Leer(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [RutaDeLaClave(host)] = vigente,
                        [RutaDeLaClave(host) + "Anterior"] = anterior,
                    })
                .Build(),
            host);

    /// <summary>
    /// El identificador de la clave que se guarda en <c>version_de_clave</c> (smallint): 16 bits derivados con HKDF de la propia clave, sin revelar nada útil de ella. Identifica
    /// con qué clave se cifró una fila y por tanto cuándo hay que re-cifrar.
    /// </summary>
    public static short IdentificadorDeClave(byte[] clave)
    {
        ArgumentNullException.ThrowIfNull(clave);
        var bytes = HKDF.DeriveKey(HashAlgorithmName.SHA256, clave, 2, salt: null, info: "babelhub/web/identificador-de-clave"u8.ToArray());
        return BitConverter.ToInt16(bytes, 0);
    }

    private static string? Leer(IConfiguration configuracion, string ruta, bool obligatoria, List<string> problemas)
    {
        var valor = configuracion[ruta];
        if (string.IsNullOrWhiteSpace(valor))
        {
            if (obligatoria)
            {
                problemas.Add($"{Variable(ruta)}: falta");
            }

            return null;
        }

        if (Encoding.UTF8.GetByteCount(valor) < LongitudMinimaEnBytes)
        {
            problemas.Add($"{Variable(ruta)}: es débil (menos de {LongitudMinimaEnBytes} bytes)");
            return null;
        }

        if (valor.Distinct().Count() < CaracteresDistintosMinimos)
        {
            problemas.Add($"{Variable(ruta)}: es débil (casi no varía)");
            return null;
        }

        return valor;
    }
}
