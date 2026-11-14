using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Auditoria;

/// <summary>Una clave permitida en el detalle de una acción y si su valor es un dato personal (AUD-06).</summary>
public sealed record ClaveDeDetalle
{
    private static readonly Regex Formato = new(@"^[a-z][A-Za-z0-9]{0,39}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

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

    /// <summary>El valor sustituto cuando lo recibido no está en <see cref="Valores"/> (nunca se rechaza el registro, AUD-04).</summary>
    public const string ValorNoPermitido = "[no-permitido]";

    /// <summary>
    /// Largo máximo del valor (nulo: sin tope propio, solo rige el del detalle completo). Un texto de una solicitud puede arrastrar nombres u otros datos
    /// personales que la redacción por forma no ve: una clave que no es un código lleva un tope corto (AUD-06, AUD-19).
    /// </summary>
    public int? LargoMaximo
    {
        get;
        init
        {
            if (value is < 1 or > DetalleSeguro.MaximoDeCaracteres)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "El largo máximo de una clave está entre 1 y el tope del detalle.");
            }

            field = value;
        }
    }

    /// <summary>
    /// El conjunto cerrado de valores admitidos (nulo: cualquier texto, con el tope de largo). Un valor que no está se reemplaza por
    /// <see cref="ValorNoPermitido"/>: el catálogo de códigos es cerrado y un texto libre no entra donde se espera un código.
    /// </summary>
    public IReadOnlyCollection<string>? Valores
    {
        get;
        init => field = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    internal string Ajustar(string valor)
    {
        if (Valores is not null)
        {
            return Valores.Contains(valor, StringComparer.Ordinal) ? valor : ValorNoPermitido;
        }

        if (LargoMaximo is int tope && valor.Length > tope)
        {
            var corte = tope - 1;
            if (corte > 0 && char.IsHighSurrogate(valor[corte - 1]))
            {
                corte--;
            }

            return valor[..corte] + "…";
        }

        return valor;
    }
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

    /// <summary>
    /// Un valor más largo que esto no se analiza: se redacta entero (el detalle cabe en <see cref="MaximoDeCaracteres"/> y un texto enorme solo sirve para
    /// gastar CPU en los patrones).
    /// </summary>
    public const int LargoMaximoAnalizable = 4 * MaximoDeCaracteres;

    private const string Puntos = "…";
    private static readonly JavaScriptEncoder Codificador = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private readonly Dictionary<string, string> _valores;
    private readonly HashSet<string> _personales;

    private DetalleSeguro(Dictionary<string, string> valores, HashSet<string> personales, string texto, bool truncado, int descartadas, IReadOnlyCollection<string> redactadas)
    {
        _valores = valores;
        _personales = personales;
        Valores = new ReadOnlyDictionary<string, string>(valores);
        ClavesPersonales = Array.AsReadOnly(personales.ToArray());
        Texto = texto;
        Truncado = truncado;
        CantidadDeClavesDescartadas = descartadas;
        ClavesRedactadas = redactadas;
    }

    /// <summary>Los valores que quedaron, en el orden de la lista de claves de la acción (de solo lectura).</summary>
    public IReadOnlyDictionary<string, string> Valores { get; }

    /// <summary>Las claves presentes cuyo valor es personal. Toda clave personal presente figura aquí.</summary>
    public IReadOnlyCollection<string> ClavesPersonales { get; }

    /// <summary>El detalle serializado como JSON (un objeto de texto a texto), de como máximo <see cref="MaximoDeCaracteres"/> caracteres.</summary>
    public string Texto { get; }

    public bool Truncado { get; }

    /// <summary>Cuántas claves recibidas no estaban declaradas (se descartaron). Los nombres no se conservan: los controla quien hace la solicitud.</summary>
    public int CantidadDeClavesDescartadas { get; }

    /// <summary>Las claves (declaradas) cuyo valor se redactó por tener forma de dato prohibido.</summary>
    public IReadOnlyCollection<string> ClavesRedactadas { get; }

    public bool EsPersonal(string clave) => _personales.Contains(clave);

    public static DetalleSeguro Construir(IReadOnlyCollection<ClaveDeDetalle> permitidas, IReadOnlyDictionary<string, string?> entrada)
    {
        ArgumentNullException.ThrowIfNull(permitidas);
        ArgumentNullException.ThrowIfNull(entrada);
        if (permitidas.Select(c => c.Nombre).Distinct(StringComparer.Ordinal).Count() != permitidas.Count)
        {
            throw new ArgumentException("Una clave del detalle se declaró más de una vez.", nameof(permitidas));
        }

        var declaradas = permitidas.Select(c => c.Nombre).ToHashSet(StringComparer.Ordinal);
        var descartadas = entrada.Keys.Count(k => !declaradas.Contains(k));
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
            var seguro = Redactar(clave.Ajustar(valor), clave.Personal);
            if (!string.Equals(seguro, valor, StringComparison.Ordinal))
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

    /// <summary>
    /// El mismo detalle con el valor de esas claves <b>personales</b> reemplazado por una constante (AUD-19: nunca un hash estable). Las claves no
    /// personales o ausentes se ignoran. Si la constante hiciera que el texto superara el máximo, los valores quedan vacíos (también una constante).
    /// </summary>
    internal DetalleSeguro ConValoresAnonimizados(IReadOnlyCollection<string> claves, string constante)
    {
        var aAnonimizar = claves.Where(c => _valores.ContainsKey(c) && _personales.Contains(c)).ToList();
        if (aAnonimizar.Count == 0)
        {
            return this;
        }

        var reemplazadas = new Dictionary<string, string>(_valores, StringComparer.Ordinal);
        foreach (var clave in aAnonimizar)
        {
            reemplazadas[clave] = constante;
        }

        var texto = Serializar(reemplazadas);
        if (texto.Length > MaximoDeCaracteres)
        {
            foreach (var clave in aAnonimizar)
            {
                reemplazadas[clave] = string.Empty;
            }

            texto = Serializar(reemplazadas);
        }

        return new DetalleSeguro(reemplazadas, new HashSet<string>(_personales, StringComparer.Ordinal), texto, Truncado, CantidadDeClavesDescartadas, ClavesRedactadas);
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

    /// <summary>Caracteres de control a espacio y sustitutos sueltos (UTF-16 inválido, que haría fallar la codificación) a U+FFFD.</summary>
    private static string Limpiar(string valor)
    {
        var limpio = new StringBuilder(valor.Length);
        for (var i = 0; i < valor.Length; i++)
        {
            var caracter = valor[i];
            if (char.IsHighSurrogate(caracter) && i + 1 < valor.Length && char.IsLowSurrogate(valor[i + 1]))
            {
                limpio.Append(caracter).Append(valor[++i]);
            }
            else if (char.IsSurrogate(caracter))
            {
                limpio.Append('�');
            }
            else
            {
                limpio.Append(char.IsControl(caracter) ? ' ' : caracter);
            }
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
    /// Devuelve el valor tal cual o <see cref="ValorRedactado"/> si parece un dato prohibido (SEG-10): tarjeta (13 a 19 dígitos en grupos, con
    /// cualquier separador, que pasan Luhn), token, enlace o dirección IP; y un correo, salvo en una clave declarada personal. Un valor demasiado largo
    /// o un análisis que excede el plazo se redactan: ante la duda, redacta.
    /// </summary>
    private static string Redactar(string valor, bool personal)
    {
        if (valor.Length > LargoMaximoAnalizable)
        {
            return ValorRedactado;
        }

        try
        {
            if (TieneFormaDeTarjeta(valor) || TieneDireccionIp(valor) || Token().IsMatch(valor) || Enlace().IsMatch(valor) || (!personal && Correo().IsMatch(valor)))
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

    /// <summary>
    /// Una tarjeta son grupos de dígitos separados por cualquier cosa que no sea una letra o un dígito (espacios, guiones, puntos, barras, espacios
    /// Unicode…), de 13 a 19 dígitos en total. Se prueba Luhn en <b>cada tramo de grupos consecutivos</b>, no solo en el más largo: «pedido 12 4111 1111
    /// 1111 1111» contiene una tarjeta. Un número seguido de 20 o más dígitos (un identificador) no es una tarjeta.
    /// </summary>
    private static bool TieneFormaDeTarjeta(string valor)
    {
        var grupos = new List<string>();
        var actual = new StringBuilder();

        bool Cerrar()
        {
            if (actual.Length > 0)
            {
                grupos.Add(actual.ToString());
                actual.Clear();
            }

            return PruebaGrupos(grupos);
        }

        foreach (var caracter in valor)
        {
            if (char.IsDigit(caracter))
            {
                actual.Append((char)('0' + (int)char.GetNumericValue(caracter)));
            }
            else if (char.IsLetter(caracter))
            {
                if (Cerrar())
                {
                    return true;
                }

                grupos.Clear();
            }
            else if (actual.Length > 0)
            {
                grupos.Add(actual.ToString());
                actual.Clear();
            }
        }

        return Cerrar();
    }

    private static bool PruebaGrupos(List<string> grupos)
    {
        for (var inicio = 0; inicio < grupos.Count; inicio++)
        {
            var digitos = new StringBuilder();
            for (var fin = inicio; fin < grupos.Count; fin++)
            {
                digitos.Append(grupos[fin]);
                if (digitos.Length > 19)
                {
                    break;
                }

                if (digitos.Length >= 13 && PasaLuhn(digitos.ToString()))
                {
                    return true;
                }
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

    /// <summary>
    /// Una dirección IPv4 (cuatro grupos con puntos) o IPv6 (al menos dos «:») que <see cref="IPAddress.TryParse(string, out IPAddress)"/> acepta,
    /// aunque la rodee puntuación («192.168.1.1.», «ip:fe80::1», «fe80::1:»). Una hora como 10:30:00 o una versión como 2.1 no lo son.
    /// </summary>
    private static bool TieneDireccionIp(string valor)
    {
        foreach (Match candidato in CandidatoDeDireccion().Matches(valor))
        {
            var texto = candidato.Value;
            foreach (var variante in new[] { texto, texto.TrimEnd('.', ':'), texto.TrimStart(':').TrimEnd('.', ':'), texto.TrimStart(':', '.') })
            {
                var cuatroGrupos = variante.Count(c => c == '.') == 3 && !variante.Contains(':', StringComparison.Ordinal);
                var ipv6 = variante.Count(c => c == ':') >= 2;
                if ((cuatroGrupos || ipv6) && IPAddress.TryParse(variante, out _))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [GeneratedRegex(@"[0-9A-Fa-f:.]{3,45}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CandidatoDeDireccion();

    [GeneratedRegex(
        @"\b(?:Bearer|Basic)\s+\S+|\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*|AGE-SECRET-KEY-|(?<![0-9a-f])[0-9a-f]{32,}(?![0-9a-f])|(?<![A-Za-z0-9_+/=-])(?=[A-Za-z0-9_+/=-]*\d)(?=[A-Za-z0-9_+/=-]*[A-Za-z])[A-Za-z0-9_+/=-]{40,}|\b(?:sk|pk|rk)_(?:live|test)_[A-Za-z0-9]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex Token();

    [GeneratedRegex(@"[a-z][a-z0-9+.\-]*://|\bwww\.|\b(?:javascript|data|mailto|file|vbscript):|[?&](?:firma|signature|sig|token|key|expira|expires)=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex Enlace();

    [GeneratedRegex(@"(?<![^@\s])[^@\s]+@[^@\s]+\.[^@\s]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Correo();
}
