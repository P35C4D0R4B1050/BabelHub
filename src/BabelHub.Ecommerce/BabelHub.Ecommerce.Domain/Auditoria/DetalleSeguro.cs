using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Auditoria;

/// <summary>Una clave permitida en el detalle de una acción y si su valor es un dato personal (AUD-06).</summary>
public sealed record ClaveDeDetalle
{
    private static readonly Regex Formato = new("^[a-z][A-Za-z0-9]{0,39}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public ClaveDeDetalle(string nombre, bool personal)
    {
        if (string.IsNullOrEmpty(nombre) || !Formato.IsMatch(nombre))
        {
            throw new ArgumentException("El nombre de una clave del detalle empieza con minúscula y solo lleva letras y dígitos (hasta 40).", nameof(nombre));
        }

        Nombre = nombre;
        Personal = personal;
    }

    public string Nombre { get; }

    /// <summary>Si el valor identifica a una persona: la depuración lo anonimiza (AUD-19) y la lista lo trata como personal.</summary>
    public bool Personal { get; }
}

/// <summary>
/// El detalle de un registro de auditoría (AUD-06, AUD-24): se construye <b>solo</b> con las claves que la acción declara (lo demás se descarta),
/// con los valores que tienen forma de tarjeta, token, enlace o dirección <b>redactados</b> (nunca se rechaza el registro por ello), con las claves
/// personales marcadas y, si supera <see cref="MaximoDeCaracteres"/>, truncado y marcado sin dejar un valor personal a medias ni sin marcar.
/// </summary>
public sealed partial class DetalleSeguro
{
    /// <summary>El tope del detalle serializado (AUD-24).</summary>
    public const int MaximoDeCaracteres = 1000;

    public const string ValorRedactado = "[redactado]";

    private const string Puntos = "…";
    private static readonly TimeSpan Espera = TimeSpan.FromMilliseconds(250);
    private static readonly JavaScriptEncoder Codificador = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private readonly Dictionary<string, string> _valores;
    private readonly HashSet<string> _personales;

    private DetalleSeguro(
        Dictionary<string, string> valores,
        HashSet<string> personales,
        string texto,
        bool truncado,
        IReadOnlyCollection<string> descartadas,
        IReadOnlyCollection<string> redactadas)
    {
        _valores = valores;
        _personales = personales;
        Texto = texto;
        Truncado = truncado;
        ClavesDescartadas = descartadas;
        ClavesRedactadas = redactadas;
    }

    /// <summary>Los valores que quedaron, en el orden de la lista de claves de la acción.</summary>
    public IReadOnlyDictionary<string, string> Valores => _valores;

    /// <summary>Las claves presentes cuyo valor es personal. Toda clave personal presente figura aquí.</summary>
    public IReadOnlyCollection<string> ClavesPersonales => _personales;

    /// <summary>El detalle serializado como JSON (un objeto de texto a texto), de como máximo <see cref="MaximoDeCaracteres"/> caracteres.</summary>
    public string Texto { get; }

    public bool Truncado { get; }

    /// <summary>Las claves recibidas que la acción no declara (se descartaron).</summary>
    public IReadOnlyCollection<string> ClavesDescartadas { get; }

    /// <summary>Las claves cuyo valor se redactó por tener forma de dato prohibido.</summary>
    public IReadOnlyCollection<string> ClavesRedactadas { get; }

    public bool EsPersonal(string clave) => _personales.Contains(clave);

    /// <summary>
    /// El mismo detalle con el valor de esas claves reemplazado por una constante (AUD-19: nunca un hash estable). Las claves siguen marcadas como
    /// personales. Si la constante hiciera que el texto superara el máximo, los valores quedan vacíos (también una constante).
    /// </summary>
    internal DetalleSeguro ConValoresAnonimizados(IReadOnlyCollection<string> claves, string constante)
    {
        if (claves.Count == 0)
        {
            return this;
        }

        var reemplazadas = new Dictionary<string, string>(_valores, StringComparer.Ordinal);
        foreach (var clave in claves.Where(reemplazadas.ContainsKey))
        {
            reemplazadas[clave] = constante;
        }

        var texto = Serializar(reemplazadas);
        if (texto.Length > MaximoDeCaracteres)
        {
            foreach (var clave in claves.Where(reemplazadas.ContainsKey))
            {
                reemplazadas[clave] = string.Empty;
            }

            texto = Serializar(reemplazadas);
        }

        return new DetalleSeguro(reemplazadas, new HashSet<string>(_personales, StringComparer.Ordinal), texto, Truncado, ClavesDescartadas, ClavesRedactadas);
    }

    private static string Serializar(Dictionary<string, string> valores)
    {
        var texto = new StringBuilder("{");
        foreach (var (clave, valor) in valores)
        {
            texto.Append(texto.Length > 1 ? "," : string.Empty).Append('"').Append(clave).Append("\":\"").Append(Codificar(valor)).Append('"');
        }

        return texto.Append('}').ToString();
    }

    public static DetalleSeguro Construir(IReadOnlyCollection<ClaveDeDetalle> permitidas, IReadOnlyDictionary<string, string?> entrada)
    {
        ArgumentNullException.ThrowIfNull(permitidas);
        ArgumentNullException.ThrowIfNull(entrada);
        if (permitidas.Select(c => c.Nombre).Distinct(StringComparer.Ordinal).Count() != permitidas.Count)
        {
            throw new ArgumentException("Una clave del detalle se declaró más de una vez.", nameof(permitidas));
        }

        var declaradas = permitidas.Select(c => c.Nombre).ToHashSet(StringComparer.Ordinal);
        var descartadas = entrada.Keys.Where(k => !declaradas.Contains(k)).ToList();
        var redactadas = new List<string>();
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        var personales = new HashSet<string>(StringComparer.Ordinal);
        var texto = new StringBuilder("{");
        var truncado = false;

        foreach (var clave in permitidas)
        {
            if (!entrada.TryGetValue(clave.Nombre, out var recibido) || recibido is null)
            {
                continue;
            }

            var valor = Limpiar(recibido);
            var seguro = Redactar(valor, clave.Personal);
            if (!ReferenceEquals(seguro, valor) && !string.Equals(seguro, valor, StringComparison.Ordinal))
            {
                redactadas.Add(clave.Nombre);
            }

            var prefijo = (texto.Length > 1 ? "," : string.Empty) + "\"" + clave.Nombre + "\":\"";
            var disponible = MaximoDeCaracteres - texto.Length - prefijo.Length - 2; // cierra comilla y llave
            var codificado = Codificar(seguro);
            if (codificado.Length <= disponible)
            {
                texto.Append(prefijo).Append(codificado).Append('"');
                valores[clave.Nombre] = seguro;
                if (clave.Personal)
                {
                    personales.Add(clave.Nombre);
                }

                continue;
            }

            // No cabe. Una clave personal se omite entera (un valor personal a medias no se puede anonimizar con confianza); una que no es
            // personal se corta con puntos suspensivos. Después de la primera que no cabe, no se agrega nada más: el orden se conserva.
            truncado = true;
            if (!clave.Personal)
            {
                var cortado = CortarParaQueCaiga(seguro, disponible);
                if (cortado is not null)
                {
                    texto.Append(prefijo).Append(Codificar(cortado)).Append('"');
                    valores[clave.Nombre] = cortado;
                }
            }

            break;
        }

        texto.Append('}');
        return new DetalleSeguro(valores, personales, texto.ToString(), truncado, descartadas, redactadas);
    }

    private static string Limpiar(string valor)
    {
        if (!valor.Any(char.IsControl))
        {
            return valor;
        }

        var limpio = new StringBuilder(valor.Length);
        foreach (var caracter in valor)
        {
            limpio.Append(char.IsControl(caracter) ? ' ' : caracter);
        }

        return limpio.ToString();
    }

    private static string Codificar(string valor) => JsonEncodedText.Encode(valor, Codificador).ToString();

    /// <summary>El valor cortado con puntos suspensivos para que su forma escapada quepa en <paramref name="disponible"/>; null si no cabe ni un trozo útil.</summary>
    private static string? CortarParaQueCaiga(string valor, int disponible)
    {
        const int Minimo = 8;
        var puntos = Codificar(Puntos).Length;
        var bajo = 0;
        var alto = valor.Length;
        while (bajo < alto)
        {
            var medio = (bajo + alto + 1) / 2;
            var prefijo = valor[..medio];
            if (medio > 0 && char.IsHighSurrogate(prefijo[^1]))
            {
                prefijo = prefijo[..^1];
            }

            if (Codificar(prefijo).Length + puntos <= disponible)
            {
                bajo = medio;
            }
            else
            {
                alto = medio - 1;
            }
        }

        if (bajo < Minimo)
        {
            return null;
        }

        var corte = valor[..bajo];
        if (char.IsHighSurrogate(corte[^1]))
        {
            corte = corte[..^1];
        }

        return corte + Puntos;
    }

    // ---------- Redacción ----------

    /// <summary>
    /// Devuelve el valor tal cual o <see cref="ValorRedactado"/> si parece un dato prohibido (SEG-10): tarjeta (13 a 19 dígitos que pasan Luhn),
    /// token, enlace o dirección IP; y un correo, salvo en una clave declarada personal. Ante un texto que no se puede analizar a tiempo, se redacta.
    /// </summary>
    private static string Redactar(string valor, bool personal)
    {
        try
        {
            if (TieneFormaDeTarjeta(valor) || Token().IsMatch(valor) || Enlace().IsMatch(valor) || DireccionIp().IsMatch(valor) || (!personal && Correo().IsMatch(valor)))
            {
                return ValorRedactado;
            }

            return valor;
        }
        catch (RegexMatchTimeoutException)
        {
            return ValorRedactado;
        }
    }

    private static bool TieneFormaDeTarjeta(string valor)
    {
        foreach (Match candidato in PosibleTarjeta().Matches(valor))
        {
            var digitos = new string([.. candidato.Value.Where(char.IsDigit)]);
            if (digitos.Length is >= 13 and <= 19 && PasaLuhn(digitos))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PasaLuhn(string digitos)
    {
        var suma = 0;
        var doble = false;
        for (var i = digitos.Length - 1; i >= 0; i--)
        {
            var d = digitos[i] - '0';
            if (doble)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            suma += d;
            doble = !doble;
        }

        return suma % 10 == 0;
    }

    [GeneratedRegex(@"(?<![\d])(?:\d[ -]?){13,19}(?![\d])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex PosibleTarjeta();

    [GeneratedRegex(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*|\bBearer\s+\S+|AGE-SECRET-KEY-|\b[0-9a-fA-F]{32,}\b|(?=[A-Za-z0-9_+/=-]*d)(?=[A-Za-z0-9_+/=-]*[A-Za-z])[A-Za-z0-9_+/=-]{40,}|\b(?:sk|pk|rk)_(?:live|test)_[A-Za-z0-9]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex Token();

    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.\-]*://|\bwww\.", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex Enlace();

    // IPv4, e IPv6 en su forma completa (ocho grupos) o con «::»: una hora como 10:30:00 no es una dirección.
    [GeneratedRegex(
        @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])|(?<![0-9a-f:])(?:(?:[0-9a-f]{1,4}:){7}[0-9a-f]{1,4}|(?:[0-9a-f]{1,4}:){1,7}:(?:[0-9a-f]{1,4}(?::[0-9a-f]{1,4}){0,6})?|::[0-9a-f]{1,4}(?::[0-9a-f]{1,4}){0,6})(?![0-9a-f:])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex DireccionIp();

    [GeneratedRegex(@"[^@\s]+@[^@\s]+\.[^@\s]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Correo();
}
