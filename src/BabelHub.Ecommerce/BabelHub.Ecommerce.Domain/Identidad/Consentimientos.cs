using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Identidad;

public enum TextoLegal
{
    Terminos,
    Privacidad,
    Reembolso,
}

/// <summary>Las versiones vigentes de los términos, la privacidad y el reembolso (IDE-57): cada una es un identificador de versión no vacío (por ejemplo <c>2026-11</c>).</summary>
public sealed record VersionesDeTextos
{
    public VersionesDeTextos(string terminos, string privacidad, string reembolso)
    {
        Terminos = Validar(terminos, nameof(terminos));
        Privacidad = Validar(privacidad, nameof(privacidad));
        Reembolso = Validar(reembolso, nameof(reembolso));
    }

    public string Terminos { get; }

    public string Privacidad { get; }

    public string Reembolso { get; }

    public string De(TextoLegal texto) => texto switch
    {
        TextoLegal.Terminos => Terminos,
        TextoLegal.Privacidad => Privacidad,
        _ => Reembolso,
    };

    private static string Validar(string version, string campo) =>
        string.IsNullOrWhiteSpace(version) || version.Length > 32 ? throw new ArgumentException("La versión de un texto es un identificador de hasta 32 caracteres.", campo) : version.Trim();
}

public enum ResultadoDeAceptacion
{
    Aceptada,

    /// <summary>La versión citada no es la vigente (IDE-57): se rechaza.</summary>
    VersionNoVigente,

    /// <summary>Faltó aceptar un texto o declarar la mayoría de edad en el registro (IDE-34, IDE-39).</summary>
    Incompleta,
}

/// <summary>
/// Lo que una persona aceptó (IDE-34, IDE-39, IDE-43, IDE-57, IDE-58): la versión y la fecha de cada texto, y la declaración de ser mayor de 18 años con su fecha. <b>Rechaza una aceptación que cite otra versión que la vigente</b>;
/// mientras falte aceptar la versión vigente de algún texto, la persona puede ver su cuenta y sus pedidos pero no iniciar pagos ni hacer cambios sensibles (IDE-58).
/// </summary>
public sealed class AceptacionDeTextos
{
    private readonly Dictionary<TextoLegal, (string Version, DateTimeOffset Fecha)> _aceptadas = [];

    private AceptacionDeTextos(DateTimeOffset? mayorDeEdadEn) => MayorDeEdadDeclaradaEn = mayorDeEdadEn;

    /// <summary>Cuándo declaró ser mayor de 18 años (IDE-39); nulo si no lo declaró.</summary>
    public DateTimeOffset? MayorDeEdadDeclaradaEn { get; private set; }

    public static AceptacionDeTextos Vacia() => new(null);

    public static AceptacionDeTextos Restaurar(DateTimeOffset? mayorDeEdadEn, IEnumerable<(TextoLegal Texto, string Version, DateTimeOffset Fecha)> aceptadas)
    {
        var aceptacion = new AceptacionDeTextos(mayorDeEdadEn);
        foreach (var (texto, version, fecha) in aceptadas)
        {
            aceptacion._aceptadas[texto] = (version, fecha);
        }

        return aceptacion;
    }

    public IReadOnlyDictionary<TextoLegal, (string Version, DateTimeOffset Fecha)> Aceptadas => _aceptadas;

    public string? VersionAceptada(TextoLegal texto) => _aceptadas.TryGetValue(texto, out var aceptada) ? aceptada.Version : null;

    /// <summary>
    /// El registro (IDE-34, IDE-39): exige aceptar los términos y la privacidad <b>en su versión vigente</b> y declarar la mayoría de edad; sin alguno de los tres, no hay cuenta. El reembolso se acepta al iniciar el primer pago
    /// (<see cref="Aceptar"/>).
    /// </summary>
    public static (AceptacionDeTextos? Aceptacion, ResultadoDeAceptacion Resultado) EnElRegistro(
        VersionesDeTextos vigentes,
        string? terminosCitados,
        string? privacidadCitada,
        bool mayorDeEdad,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(vigentes);
        if (string.IsNullOrWhiteSpace(terminosCitados) || string.IsNullOrWhiteSpace(privacidadCitada) || !mayorDeEdad)
        {
            return (null, ResultadoDeAceptacion.Incompleta);
        }

        if (terminosCitados != vigentes.Terminos || privacidadCitada != vigentes.Privacidad)
        {
            return (null, ResultadoDeAceptacion.VersionNoVigente);
        }

        var aceptacion = new AceptacionDeTextos(ahora);
        aceptacion._aceptadas[TextoLegal.Terminos] = (vigentes.Terminos, ahora);
        aceptacion._aceptadas[TextoLegal.Privacidad] = (vigentes.Privacidad, ahora);
        return (aceptacion, ResultadoDeAceptacion.Aceptada);
    }

    /// <summary>Acepta un texto citando su versión: solo la vigente se acepta (IDE-57); guarda la versión y la fecha (IDE-43).</summary>
    public ResultadoDeAceptacion Aceptar(VersionesDeTextos vigentes, TextoLegal texto, string? versionCitada, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(vigentes);
        if (versionCitada != vigentes.De(texto))
        {
            return ResultadoDeAceptacion.VersionNoVigente;
        }

        _aceptadas[texto] = (versionCitada, ahora);
        return ResultadoDeAceptacion.Aceptada;
    }

    /// <summary>Los textos cuya versión vigente la persona aún no aceptó: se le piden en su siguiente ingreso o renovación de sesión (IDE-43).</summary>
    public IReadOnlyList<TextoLegal> Pendientes(VersionesDeTextos vigentes)
    {
        ArgumentNullException.ThrowIfNull(vigentes);
        return [.. Enum.GetValues<TextoLegal>().Where(t => VersionAceptada(t) != vigentes.De(t))];
    }

    public bool EstaAlDia(VersionesDeTextos vigentes) => Pendientes(vigentes).Count == 0;

    /// <summary>Mientras falte aceptar algún texto vigente no se inician pagos ni se hacen cambios sensibles (IDE-58); ver la cuenta y los pedidos siempre se puede.</summary>
    public bool PuedeIniciarPagosOHacerCambiosSensibles(VersionesDeTextos vigentes) => EstaAlDia(vigentes);

    public bool PuedeVerSuCuentaYSusPedidos => true;
}

/// <summary>Un número de WhatsApp en formato E.164 (IDE-31): <c>+</c>, un código de país que no empieza con 0 y entre 8 y 15 dígitos en total.</summary>
public readonly partial record struct NumeroDeWhatsApp
{
    private NumeroDeWhatsApp(string valor) => Valor = valor;

    public string Valor { get; }

    public static bool TryCrear(string? texto, out NumeroDeWhatsApp numero)
    {
        var limpio = texto?.Trim();
        if (limpio is not null && FormatoE164().IsMatch(limpio))
        {
            numero = new NumeroDeWhatsApp(limpio);
            return true;
        }

        numero = default;
        return false;
    }

    public override string ToString() => Valor is null ? "[sin número]" : "[número]";

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoE164();
}

/// <summary>
/// El número de WhatsApp y el consentimiento para los avisos (IDE-11, IDE-31): el consentimiento es <b>para un número</b>; al cambiar de número queda sin efecto hasta que la persona lo confirme para el nuevo; revocarlo guarda
/// la fecha y los avisos siguientes no se envían.
/// </summary>
public sealed class ConsentimientoDeWhatsApp
{
    private ConsentimientoDeWhatsApp(NumeroDeWhatsApp? numero, bool consentido, DateTimeOffset? fecha)
    {
        Numero = numero;
        Consentido = consentido;
        Fecha = fecha;
    }

    public NumeroDeWhatsApp? Numero { get; private set; }

    public bool Consentido { get; private set; }

    /// <summary>La fecha de la última decisión (consentir, revocar o perder el efecto por un número nuevo).</summary>
    public DateTimeOffset? Fecha { get; private set; }

    public static ConsentimientoDeWhatsApp SinNumero() => new(null, false, null);

    public static ConsentimientoDeWhatsApp Restaurar(NumeroDeWhatsApp? numero, bool consentido, DateTimeOffset? fecha) => new(numero, consentido && numero is not null, fecha);

    /// <summary>Guarda un número válido (rechaza el inválido sin tocar nada) y deja sin efecto el consentimiento hasta confirmarlo para ese número (IDE-31).</summary>
    public bool GuardarNumero(string? texto, DateTimeOffset ahora)
    {
        if (!NumeroDeWhatsApp.TryCrear(texto, out var nuevo))
        {
            return false;
        }

        if (Numero is { } actual && actual.Valor == nuevo.Valor)
        {
            return true;
        }

        Numero = nuevo;
        Consentido = false;
        Fecha = ahora;
        return true;
    }

    /// <summary>Consiente recibir avisos para el número guardado, que la persona confirma citándolo (IDE-11, IDE-31).</summary>
    public bool Consentir(string? numeroConfirmado, DateTimeOffset ahora)
    {
        if (Numero is not { } actual || !NumeroDeWhatsApp.TryCrear(numeroConfirmado, out var confirmado) || confirmado.Valor != actual.Valor)
        {
            return false;
        }

        Consentido = true;
        Fecha = ahora;
        return true;
    }

    /// <summary>Revoca el consentimiento: se guarda la fecha y los avisos siguientes no se envían (IDE-11).</summary>
    public void Revocar(DateTimeOffset ahora)
    {
        Consentido = false;
        Fecha = ahora;
    }

    public bool PuedeRecibirAvisos => Numero is not null && Consentido;

    /// <summary>La cancelación de la cuenta elimina el número y el consentimiento (IDE-69).</summary>
    public void Eliminar()
    {
        Numero = null;
        Consentido = false;
        Fecha = null;
    }
}
