using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Catalogo;

/// <summary>El formato de un libro digital (CAT-05). El catálogo de formatos está en el Dominio, no en la base (R07).</summary>
public enum FormatoDigital
{
    Epub,
    Pdf,
}

public enum TipoDeLicencia
{
    /// <summary>Con vigencia: siempre tiene fecha de vencimiento (CAT-05).</summary>
    Comercial,

    /// <summary>Sin vigencia: nunca la tiene.</summary>
    DominioPublico,
}

/// <summary>Por qué una edición no se ofrece a la venta (D12): Carrito y Biblioteca lo distinguen. La licencia vencida y la que no incluye Perú se <b>derivan</b> de la licencia; la retirada y la baja son estados.</summary>
public enum MotivoDeNoDisponible
{
    LicenciaVencida,
    SinPeru,
    RetiradaDeLaVenta,
    DadaDeBaja,
}

/// <summary>Un error de campo de la licencia: el campo y una clave estable (nunca el valor recibido).</summary>
public sealed record ErrorDeLicencia(string Campo, string Clave);

/// <summary>El resultado de crear o cambiar una licencia: la licencia o los campos que fallaron.</summary>
public sealed class ResultadoDeLicencia
{
    private ResultadoDeLicencia(LicenciaDigital? licencia, IReadOnlyList<ErrorDeLicencia> errores)
    {
        Licencia = licencia;
        Errores = errores;
    }

    public LicenciaDigital? Licencia { get; }

    public IReadOnlyList<ErrorDeLicencia> Errores { get; }

    public bool EsExito => Licencia is not null;

    internal static ResultadoDeLicencia Exito(LicenciaDigital licencia) => new(licencia, []);

    internal static ResultadoDeLicencia Fallo(IReadOnlyList<ErrorDeLicencia> errores) => new(null, errores);
}

/// <summary>
/// La licencia de una edición digital (CAT-05 a CAT-08): titular de derechos, territorios, formato y tipo. Una licencia <b>comercial</b> siempre tiene vigencia y
/// esta debe ser <b>posterior a hoy</b> (hoy es el día de Lima) <i>solo al registrar o cambiar</i> la licencia; al rehidratarla no se vuelve a exigir, de modo que
/// editar el precio de una digital vencida no falla. Una de <b>dominio público</b> nunca tiene vigencia. «Vigente hasta» es inclusivo: el día del vencimiento
/// todavía rige. Una licencia vencida o que no incluye Perú retira la oferta pero no el acceso ya concedido (CAT-08).
/// </summary>
public sealed partial class LicenciaDigital : IEquatable<LicenciaDigital>
{
    public const int LongitudMaximaDelTitular = 200;

    /// <summary>Los códigos de país ISO 3166-1 alfa-2 son 249: más entradas que eso repiten territorios.</summary>
    public const int MaximoDeTerritorios = 250;

    public const string CodigoDePeru = "PE";

    private LicenciaDigital(TipoDeLicencia tipo, string titular, IReadOnlyList<string> territorios, DateOnly? vigenteHasta, FormatoDigital formato)
    {
        Tipo = tipo;
        Titular = titular;
        Territorios = territorios;
        VigenteHasta = vigenteHasta;
        Formato = formato;
    }

    public TipoDeLicencia Tipo { get; }

    public string Titular { get; }

    /// <summary>Códigos de país en mayúsculas, sin repetir y ordenados.</summary>
    public IReadOnlyList<string> Territorios { get; }

    public DateOnly? VigenteHasta { get; }

    public FormatoDigital Formato { get; }

    public bool IncluyePeru => Territorios.Contains(CodigoDePeru);

    /// <summary>Al <b>registrar o cambiar</b> la licencia: la vigencia de una comercial es posterior a hoy en la hora de Lima.</summary>
    public static ResultadoDeLicencia Crear(
        TipoDeLicencia tipo,
        string? titular,
        IEnumerable<string>? territorios,
        DateOnly? vigenteHasta,
        FormatoDigital formato,
        DateOnly hoyEnLima)
    {
        var errores = Validar(tipo, titular, territorios, vigenteHasta, formato, out var titularLimpio, out var territoriosLimpios);
        if (tipo == TipoDeLicencia.Comercial && vigenteHasta is { } vigencia && vigencia <= hoyEnLima)
        {
            errores.Add(new ErrorDeLicencia("vigenteHasta", "vigencia-no-posterior-a-hoy"));
        }

        return errores.Count == 0
            ? ResultadoDeLicencia.Exito(new LicenciaDigital(tipo, titularLimpio!, territoriosLimpios!, vigenteHasta, formato))
            : ResultadoDeLicencia.Fallo(errores);
    }

    /// <summary>Reconstruye una licencia guardada: valida su forma pero no vuelve a exigir una vigencia futura.</summary>
    public static LicenciaDigital Rehidratar(
        TipoDeLicencia tipo,
        string titular,
        IEnumerable<string> territorios,
        DateOnly? vigenteHasta,
        FormatoDigital formato)
    {
        var errores = Validar(tipo, titular, territorios, vigenteHasta, formato, out var titularLimpio, out var territoriosLimpios);
        return errores.Count == 0
            ? new LicenciaDigital(tipo, titularLimpio!, territoriosLimpios!, vigenteHasta, formato)
            : throw new ArgumentException("La licencia guardada no cumple la forma del catálogo: " + string.Join(", ", errores.Select(e => e.Campo)), nameof(titular));
    }

    /// <summary>Si rige hoy: la de dominio público siempre; la comercial hasta el día de su vencimiento, inclusive.</summary>
    public bool EstaVigente(DateOnly hoyEnLima) => Tipo == TipoDeLicencia.DominioPublico || hoyEnLima <= VigenteHasta;

    /// <summary>El motivo por el que no se ofrece, o <see langword="null"/> si se ofrece. Una vencida se explica como vencida aunque tampoco incluya Perú.</summary>
    public MotivoDeNoDisponible? MotivoDeNoDisponiblePara(DateOnly hoyEnLima)
    {
        if (!EstaVigente(hoyEnLima))
        {
            return MotivoDeNoDisponible.LicenciaVencida;
        }

        return IncluyePeru ? null : MotivoDeNoDisponible.SinPeru;
    }

    public bool Equals(LicenciaDigital? other) =>
        other is not null
        && Tipo == other.Tipo
        && Formato == other.Formato
        && VigenteHasta == other.VigenteHasta
        && string.Equals(Titular, other.Titular, StringComparison.Ordinal)
        && Territorios.SequenceEqual(other.Territorios, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as LicenciaDigital);

    public override int GetHashCode() => HashCode.Combine(Tipo, Formato, VigenteHasta, Titular, Territorios.Count);

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodigoDePais();

    private static List<ErrorDeLicencia> Validar(
        TipoDeLicencia tipo,
        string? titular,
        IEnumerable<string>? territorios,
        DateOnly? vigenteHasta,
        FormatoDigital formato,
        out string? titularLimpio,
        out IReadOnlyList<string>? territoriosLimpios)
    {
        var errores = new List<ErrorDeLicencia>();

        titularLimpio = titular?.Trim();
        if (string.IsNullOrEmpty(titularLimpio))
        {
            errores.Add(new ErrorDeLicencia("titular", "obligatorio"));
        }
        else if (titularLimpio.Length > LongitudMaximaDelTitular)
        {
            errores.Add(new ErrorDeLicencia("titular", "demasiado-largo"));
        }

        var lista = territorios?.ToList() ?? [];
        if (lista.Count == 0)
        {
            errores.Add(new ErrorDeLicencia("territorios", "obligatorio"));
            territoriosLimpios = null;
        }
        else if (lista.Count > MaximoDeTerritorios || lista.Any(t => t is null || !CodigoDePais().IsMatch(t.Trim().ToUpperInvariant())))
        {
            errores.Add(new ErrorDeLicencia("territorios", "formato-invalido"));
            territoriosLimpios = null;
        }
        else
        {
            territoriosLimpios = lista.Select(t => t.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
        }

        if (!Enum.IsDefined(tipo))
        {
            errores.Add(new ErrorDeLicencia("tipo", "formato-invalido"));
        }
        else if (tipo == TipoDeLicencia.Comercial && vigenteHasta is null)
        {
            errores.Add(new ErrorDeLicencia("vigenteHasta", "obligatorio"));
        }
        else if (tipo == TipoDeLicencia.DominioPublico && vigenteHasta is not null)
        {
            errores.Add(new ErrorDeLicencia("vigenteHasta", "no-aplica"));
        }

        if (!Enum.IsDefined(formato))
        {
            errores.Add(new ErrorDeLicencia("formato", "formato-invalido"));
        }

        return errores;
    }
}
