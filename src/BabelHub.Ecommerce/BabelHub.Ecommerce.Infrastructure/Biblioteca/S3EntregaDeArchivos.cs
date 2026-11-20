using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>
/// La firma SigV4 de la URL de descarga (T14, D02), calculada <b>sin red y de forma determinista</b> con el HMAC de la biblioteca base de .NET y el instante recibido: el mismo instante produce la misma URL, de modo que la repetición de una clave reconstruye el mismo enlace sin guardarlo
/// (BIB-05, SEG-10). El <c>GetPreSignedURL</c> del SDK no se usa porque firma con el reloj del cliente y no acepta el instante. Solo firma un <c>GET</c> con <c>host</c> como única cabecera firmada y <c>UNSIGNED-PAYLOAD</c>.
/// </summary>
internal static class FirmaDeEnlaceSigV4
{
    private const string Algoritmo = "AWS4-HMAC-SHA256";

    public static Uri Firmar(
        Uri endpoint,
        bool rutaEnLaUrl,
        string contenedor,
        string clave,
        string region,
        string claveDeAcceso,
        string claveSecreta,
        DateTimeOffset firmadoEn,
        int vigenciaEnSegundos,
        string? contentDisposition)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var utc = firmadoEn.ToUniversalTime();
        var fechaYHora = utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var fecha = utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var alcance = $"{fecha}/{region}/s3/aws4_request";

        var autoridad = rutaEnLaUrl ? endpoint.Authority : $"{contenedor}.{endpoint.Authority}";
        var ruta = (rutaEnLaUrl ? "/" + Codificar(contenedor, conBarras: false) + "/" : "/") + Codificar(clave, conBarras: true);

        var parametros = new List<(string Nombre, string Valor)>
        {
            ("X-Amz-Algorithm", Algoritmo),
            ("X-Amz-Credential", $"{claveDeAcceso}/{alcance}"),
            ("X-Amz-Date", fechaYHora),
            ("X-Amz-Expires", vigenciaEnSegundos.ToString(CultureInfo.InvariantCulture)),
            ("X-Amz-SignedHeaders", "host"),
        };
        if (contentDisposition is not null)
        {
            parametros.Add(("response-content-disposition", contentDisposition));
        }

        var consulta = string.Join('&', parametros.Select(p => (Nombre: Codificar(p.Nombre, false), Valor: Codificar(p.Valor, false))).OrderBy(p => p.Nombre, StringComparer.Ordinal).Select(p => $"{p.Nombre}={p.Valor}"));
        var canonica = $"GET\n{ruta}\n{consulta}\nhost:{autoridad}\n\nhost\nUNSIGNED-PAYLOAD";
        var aFirmar = $"{Algoritmo}\n{fechaYHora}\n{alcance}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonica)))}";

        var llave = Hmac(Encoding.UTF8.GetBytes("AWS4" + claveSecreta), fecha);
        llave = Hmac(llave, region);
        llave = Hmac(llave, "s3");
        llave = Hmac(llave, "aws4_request");
        var firma = Convert.ToHexStringLower(Hmac(llave, aFirmar));

        return new Uri($"{endpoint.Scheme}://{autoridad}{ruta}?{consulta}&X-Amz-Signature={firma}");
    }

    /// <summary>La codificación de URI de AWS: todo lo que no es sin reservar (A–Z, a–z, 0–9, <c>-</c>, <c>_</c>, <c>.</c>, <c>~</c>) va en <c>%XX</c> mayúscula sobre UTF-8; la barra solo se conserva en la ruta.</summary>
    internal static string Codificar(string valor, bool conBarras)
    {
        var resultado = new StringBuilder();
        foreach (var octeto in Encoding.UTF8.GetBytes(valor))
        {
            var c = (char)octeto;
            if (octeto < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' || (conBarras && c == '/')))
            {
                resultado.Append(c);
            }
            else
            {
                resultado.Append('%').Append(octeto.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return resultado.ToString();
    }

    private static byte[] Hmac(byte[] llave, string datos) => HMACSHA256.HashData(llave, Encoding.UTF8.GetBytes(datos));
}

/// <summary>
/// El almacén de objetos visto desde Biblioteca sobre S3 (T14, AD19, RNF14): <b>solo lectura</b> con la credencial <c>biblioteca-lector</c> (leer en <c>libros</c>). <c>ExisteArchivo</c> es un <c>HEAD</c> con plazo de 3 segundos y sin reintento (un 404 es «no existe»; un plazo agotado o un fallo, «no disponible»);
/// <c>FirmarEnlace</c> firma localmente (<see cref="FirmaDeEnlaceSigV4"/>) con <c>Content-Disposition: attachment</c>, el nombre saneado y 300 segundos como máximo. <b>Nunca registra la URL ni ningún parámetro <c>X-Amz-*</c></b> (SEG-10) y el archivo nunca pasa por el servidor (BIB-14).
/// </summary>
internal sealed partial class S3EntregaDeArchivos(IAmazonS3 almacen, OpcionesDelAlmacenDeObjetos opciones, string contenedor = "libros", TimeSpan? plazoDelHead = null) : IEntregaDeArchivos
{
    /// <summary>El plazo del <c>HEAD</c> (R04).</summary>
    public static readonly TimeSpan PlazoPorOmision = TimeSpan.FromSeconds(3);

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaClave();

    public async Task<ExistenciaDelArchivo> ExisteArchivo(string claveEnAlmacen, CancellationToken cancelacion = default)
    {
        if (!FormaDeLaClave().IsMatch(claveEnAlmacen))
        {
            return ExistenciaDelArchivo.NoExiste;
        }

        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(plazoDelHead ?? PlazoPorOmision);
        try
        {
            await almacen.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = contenedor, Key = claveEnAlmacen }, plazo.Token).ConfigureAwait(false);
            return ExistenciaDelArchivo.Existe;
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return ExistenciaDelArchivo.NoExiste;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Un plazo agotado, una red caída, un 403 o un 5xx: no se consume nada y no se ofrece reportar (R04). Nunca se registra el mensaje: puede traer la solicitud firmada.
            return ExistenciaDelArchivo.NoDisponible;
        }
    }

    public Uri FirmarEnlace(string claveEnAlmacen, string nombreDeArchivo, DateTimeOffset firmadoEn, int vigenciaEnSegundos)
    {
        if (!FormaDeLaClave().IsMatch(claveEnAlmacen))
        {
            throw new InvalidOperationException("La clave del archivo no es válida.");
        }

        if (vigenciaEnSegundos is < 1 or > 300)
        {
            throw new InvalidOperationException("El enlace dura entre 1 y 300 segundos.");
        }

        return FirmaDeEnlaceSigV4.Firmar(
            opciones.Endpoint,
            opciones.RutaEnLaUrl,
            contenedor,
            claveEnAlmacen,
            opciones.Region,
            opciones.ClaveDeAcceso,
            opciones.ClaveSecreta.Revelar(),
            firmadoEn,
            vigenciaEnSegundos,
            ContentDisposition(nombreDeArchivo));
    }

    /// <summary><c>attachment</c> con el nombre saneado: una forma ASCII para los navegadores viejos y la forma UTF-8 (RFC 5987). Sin comillas ni caracteres de control (el nombre ya vino saneado de Aplicación; aquí se repite por defensa).</summary>
    internal static string ContentDisposition(string nombre)
    {
        var limpio = new string([.. nombre.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/' or ';'))]).Trim();
        if (limpio.Length == 0)
        {
            limpio = "libro";
        }

        var ascii = new string([.. limpio.Select(c => c is >= ' ' and <= '~' ? c : '_')]);
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{FirmaDeEnlaceSigV4.Codificar(limpio, conBarras: false)}";
    }
}

/// <summary>
/// El almacén de entrega con contenido sintético en memoria, <b>solo para desarrollo y pruebas</b> (AD19, regla 11: nunca archivos de prueba en el repositorio): todo archivo «existe» salvo los que se marquen como ausentes, y la URL es una dirección inválida (<c>.invalid</c>) que no sirve nada. Se
/// registra únicamente fuera de producción; la guarda de arranque lo rechaza en producción (T15).
/// </summary>
internal sealed class EntregaDeArchivosEnMemoria : IEntregaDeArchivos
{
    private readonly HashSet<string> _ausentes = [];

    public void MarcarAusente(string claveEnAlmacen) => _ausentes.Add(claveEnAlmacen);

    public Task<ExistenciaDelArchivo> ExisteArchivo(string claveEnAlmacen, CancellationToken cancelacion = default) =>
        Task.FromResult(_ausentes.Contains(claveEnAlmacen) ? ExistenciaDelArchivo.NoExiste : ExistenciaDelArchivo.Existe);

    public Uri FirmarEnlace(string claveEnAlmacen, string nombreDeArchivo, DateTimeOffset firmadoEn, int vigenciaEnSegundos) =>
        new($"https://almacen-de-desarrollo.invalid/libros/{Uri.EscapeDataString(claveEnAlmacen)}?X-Amz-Date={firmadoEn.ToUniversalTime().UtcTicks}&X-Amz-Expires={vigenciaEnSegundos}");
}
