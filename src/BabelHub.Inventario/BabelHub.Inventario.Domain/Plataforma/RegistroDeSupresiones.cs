using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Domain.Plataforma;

/// <summary>Qué se hizo con la cuenta (PLA-37).</summary>
public enum TipoDeSupresion
{
    Cancelacion,
    Anonimizacion,
}

/// <summary>La intención se agrega antes de completar la supresión y el cierre al completarla (PLA-37).</summary>
public enum FaseDeSupresion
{
    Intencion,
    Cierre,
}

/// <summary>
/// Una entrada del registro de supresiones, que vive fuera de las bases respaldadas (PLA-38): el identificador opaco de la cuenta, el
/// tipo, la fase, la fecha y la suma de verificación de su contenido. No lleva ningún dato personal: el identificador es el
/// identificador interno aleatorio de la cuenta, nunca derivado del correo (Ley 29733). La clave es determinista, así que repetir una
/// entrada escribe el mismo objeto y el almacén responde «ya existe», que se trata como éxito (D15).
/// </summary>
public sealed partial record EntradaDeSupresion
{
    private EntradaDeSupresion(string identificadorOpaco, TipoDeSupresion tipo, FaseDeSupresion fase, DateTimeOffset fecha, string suma)
    {
        IdentificadorOpaco = identificadorOpaco;
        Tipo = tipo;
        Fase = fase;
        Fecha = fecha;
        Suma = suma;
    }

    public string IdentificadorOpaco { get; }

    public TipoDeSupresion Tipo { get; }

    public FaseDeSupresion Fase { get; }

    /// <summary>La fecha propia de esta entrada (la del cierre no es la de la intención), siempre en UTC.</summary>
    public DateTimeOffset Fecha { get; }

    /// <summary>SHA-256 del contenido canónico, en hexadecimal minúscula.</summary>
    public string Suma { get; }

    /// <summary>La clave del objeto en el almacén: <c>entradas/&lt;identificador&gt;/&lt;tipo&gt;-&lt;fase&gt;.json</c>.</summary>
    public string Clave => $"entradas/{IdentificadorOpaco}/{NombreDe(Tipo)}-{NombreDe(Fase)}.json";

    /// <summary>La forma canónica que se firma: cuatro líneas con campos fijos, la fecha en UTC con siete decimales.</summary>
    public string ContenidoCanonico => Canonico(IdentificadorOpaco, Tipo, Fase, Fecha);

    /// <summary>Crea una entrada nueva y calcula su suma.</summary>
    public static EntradaDeSupresion Crear(string identificadorOpaco, TipoDeSupresion tipo, FaseDeSupresion fase, DateTimeOffset fecha)
    {
        var id = ValidarIdentificador(identificadorOpaco);
        var utc = fecha.ToUniversalTime();
        return new EntradaDeSupresion(id, tipo, fase, utc, Sumar(Canonico(id, tipo, fase, utc)));
    }

    /// <summary>
    /// Reconstruye una entrada leída del almacén con la suma que traía. No la corrige: <see cref="SumaEsValida"/> dice si el contenido
    /// sigue coincidiendo con ella.
    /// </summary>
    public static EntradaDeSupresion Reconstruir(string identificadorOpaco, TipoDeSupresion tipo, FaseDeSupresion fase, DateTimeOffset fecha, string suma)
    {
        ArgumentNullException.ThrowIfNull(suma);
        if (!SumaHexadecimal().IsMatch(suma))
        {
            throw new ArgumentException("La suma debe ser un SHA-256 en hexadecimal minúscula.", nameof(suma));
        }

        return new EntradaDeSupresion(ValidarIdentificador(identificadorOpaco), tipo, fase, fecha.ToUniversalTime(), suma);
    }

    /// <summary>Si la suma que trae la entrada es la de su contenido (comparación en tiempo constante).</summary>
    public bool SumaEsValida() =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Suma), Encoding.ASCII.GetBytes(Sumar(ContenidoCanonico)));

    public static string NombreDe(TipoDeSupresion tipo) => tipo switch
    {
        TipoDeSupresion.Cancelacion => "cancelacion",
        TipoDeSupresion.Anonimizacion => "anonimizacion",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    public static string NombreDe(FaseDeSupresion fase) => fase switch
    {
        FaseDeSupresion.Intencion => "intencion",
        FaseDeSupresion.Cierre => "cierre",
        _ => throw new ArgumentOutOfRangeException(nameof(fase)),
    };

    internal static string Sumar(string contenido) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenido))).ToLowerInvariant();

    private static string Canonico(string identificador, TipoDeSupresion tipo, FaseDeSupresion fase, DateTimeOffset fecha) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"identificadorOpaco={identificador}\ntipo={NombreDe(tipo)}\nfase={NombreDe(fase)}\nfecha={fecha.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffffffZ}");

    private static string ValidarIdentificador(string identificador)
    {
        ArgumentNullException.ThrowIfNull(identificador);
        if (!IdentificadorValido().IsMatch(identificador))
        {
            throw new ArgumentException("El identificador opaco debe ser el identificador interno aleatorio de la cuenta (16 a 64 letras, dígitos, guiones), nunca un dato personal.", nameof(identificador));
        }

        return identificador;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{15,63}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex IdentificadorValido();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SumaHexadecimal();
}

/// <summary>Una entrada tal como la devuelve el almacén: su clave real y su hora de modificación en él (no la que diga su contenido).</summary>
public sealed record EntradaLeida(string ClaveEnElAlmacen, EntradaDeSupresion Entrada, DateTimeOffset ModificadaEn);

/// <summary>
/// El sello que VM-AUX escribe cada hora en <c>sellos/</c>: cuántas entradas había y la suma de ese conjunto. El conjunto sellado es el
/// de las entradas cuya hora de modificación en el almacén es anterior al instante del sello menos <see cref="Margen"/>, para que una
/// entrada que se está escribiendo mientras se sella no cambie el resultado.
/// </summary>
public sealed record SelloDelRegistro
{
    /// <summary>Tiempo que se deja sin sellar para no contar lo que se escribe mientras se sella.</summary>
    public static readonly TimeSpan Margen = TimeSpan.FromMinutes(15);

    public SelloDelRegistro(DateTimeOffset instante, int cantidad, string sumaDelConjunto)
    {
        ArgumentNullException.ThrowIfNull(sumaDelConjunto);
        if (cantidad < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad no puede ser negativa.");
        }

        Instante = instante.ToUniversalTime();
        Cantidad = cantidad;
        SumaDelConjunto = sumaDelConjunto;
    }

    public DateTimeOffset Instante { get; }

    public int Cantidad { get; }

    public string SumaDelConjunto { get; }

    /// <summary>Las entradas que este sello cubre: las modificadas antes de su instante menos el margen.</summary>
    public IEnumerable<EntradaLeida> ConjuntoSellado(IEnumerable<EntradaLeida> entradas) =>
        entradas.Where(e => e.ModificadaEn < Instante - Margen);

    /// <summary>
    /// La suma de un conjunto de entradas, en forma canónica: una línea «clave, tabulación, suma» por entrada, ordenadas por clave en
    /// orden ordinal, cada una terminada en salto de línea, y el SHA-256 de ese texto en UTF-8. Es la misma forma que calcula el script
    /// de VM-AUX (se comprueba con una prueba de conformidad).
    /// </summary>
    public static string SumaDelConjuntoDe(IEnumerable<EntradaLeida> entradas)
    {
        ArgumentNullException.ThrowIfNull(entradas);
        var texto = new StringBuilder();
        foreach (var entrada in entradas.OrderBy(e => e.ClaveEnElAlmacen, StringComparer.Ordinal))
        {
            texto.Append(entrada.ClaveEnElAlmacen).Append('\t').Append(entrada.Entrada.Suma).Append('\n');
        }

        return EntradaDeSupresion.Sumar(texto.ToString());
    }

    /// <summary>El sello de un instante: cuenta y suma el conjunto que le corresponde.</summary>
    public static SelloDelRegistro Sellar(DateTimeOffset instante, IEnumerable<EntradaLeida> entradas)
    {
        ArgumentNullException.ThrowIfNull(entradas);
        var vacio = new SelloDelRegistro(instante, 0, string.Empty);
        var conjunto = vacio.ConjuntoSellado(entradas).ToList();
        return new SelloDelRegistro(instante, conjunto.Count, SumaDelConjuntoDe(conjunto));
    }
}

public enum EstadoDeVerificacionDelRegistro
{
    /// <summary>Todo coincide: el servicio puede abrirse al público.</summary>
    Valido,

    /// <summary>No se pudo leer el registro: el servicio no se abre (PLA-19).</summary>
    RegistroAusente,

    /// <summary>No hay un sello contra el cual verificar: el servicio no se abre salvo la vía de excepción de la persona responsable.</summary>
    SelloAusente,

    /// <summary>Una entrada no coincide con su suma o con su clave: alguien la cambió.</summary>
    EntradaAlterada,

    /// <summary>Hay menos entradas que las selladas: se borró alguna.</summary>
    EntradasAusentes,

    /// <summary>La cantidad o la suma del conjunto no coinciden con el sello (por ejemplo, una entrada agregada o sustituida).</summary>
    SumaDelConjuntoNoCoincide,
}

/// <summary>El resultado de verificar el registro: si puede abrirse el servicio y, si no, por qué y qué claves lo causan (nunca datos personales: solo claves).</summary>
public sealed record ResultadoDeVerificacionDelRegistro(EstadoDeVerificacionDelRegistro Estado, IReadOnlyList<string> ClavesAfectadas)
{
    public bool PermiteAbrir => Estado == EstadoDeVerificacionDelRegistro.Valido;
}

/// <summary>
/// Verifica el registro de supresiones antes de abrir el servicio tras una restauración (PLA-19): que el registro esté, que cada entrada
/// coincida con su suma y con su clave, y que el conjunto cubierto por el sello tenga la cantidad y la suma selladas. Ante cualquier
/// diferencia el servicio no se abre. Solo compara el conjunto sellado: lo escrito después del sello (menos el margen) no cuenta.
/// </summary>
public static class VerificadorDelRegistro
{
    /// <param name="entradas">Las entradas leídas del almacén, o <c>null</c> si el registro no se pudo leer.</param>
    /// <param name="sello">El sello más reciente anterior al instante del respaldo restaurado, o <c>null</c> si no existe.</param>
    public static ResultadoDeVerificacionDelRegistro Verificar(IReadOnlyCollection<EntradaLeida>? entradas, SelloDelRegistro? sello)
    {
        if (entradas is null)
        {
            return new(EstadoDeVerificacionDelRegistro.RegistroAusente, []);
        }

        var alteradas = entradas
            .Where(e => !e.Entrada.SumaEsValida() || !string.Equals(e.ClaveEnElAlmacen, e.Entrada.Clave, StringComparison.Ordinal))
            .Select(e => e.ClaveEnElAlmacen)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (alteradas.Count > 0)
        {
            return new(EstadoDeVerificacionDelRegistro.EntradaAlterada, alteradas);
        }

        if (sello is null)
        {
            return new(EstadoDeVerificacionDelRegistro.SelloAusente, []);
        }

        var conjunto = sello.ConjuntoSellado(entradas).ToList();
        if (conjunto.Count < sello.Cantidad)
        {
            return new(EstadoDeVerificacionDelRegistro.EntradasAusentes, []);
        }

        if (conjunto.Count != sello.Cantidad
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(SelloDelRegistro.SumaDelConjuntoDe(conjunto)),
                Encoding.ASCII.GetBytes(sello.SumaDelConjunto)))
        {
            return new(EstadoDeVerificacionDelRegistro.SumaDelConjuntoNoCoincide, []);
        }

        return new(EstadoDeVerificacionDelRegistro.Valido, []);
    }
}
