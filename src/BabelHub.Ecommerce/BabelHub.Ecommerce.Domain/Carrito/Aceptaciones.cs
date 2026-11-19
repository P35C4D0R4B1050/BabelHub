namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>Los textos legales que se aceptan al comprar (CAR-12, CAR-13): los términos y la privacidad siempre, y la política de reembolso de libros digitales si hay digitales.</summary>
public enum TextoLegal
{
    Terminos,
    Privacidad,
    ReembolsoDigital,
}

/// <summary>La aceptación de un texto: su versión y la fecha en que la persona la aceptó (CAR-22). La fecha es la <b>primera</b> de esa versión y no entra en la huella de instantánea.</summary>
public sealed record AceptacionDeTexto(TextoLegal Texto, string Version, DateTimeOffset AceptadoEn);

/// <summary>Lo que dejó una revalidación de los textos: cuáles aceptaciones se invalidaron porque cambió la versión vigente (CAR-28), o el rechazo si el checkout no estaba en edición.</summary>
public sealed record ResultadoDeRevalidacion(IReadOnlyList<TextoLegal> Invalidadas, RechazoDeCheckout? Rechazo);

/// <summary>
/// Las aceptaciones de un checkout (CAR-12, CAR-13, CAR-22, CAR-28): una por texto, con su versión y su fecha. Aceptar otra vez la misma versión no cambia nada (conserva la primera fecha: un doble clic no cambia la huella) y una versión
/// nueva reemplaza a la anterior. Si cambia la versión vigente de un texto antes de pagar, su aceptación se invalida y se vuelve a pedir.
/// </summary>
public sealed class AceptacionesDelCheckout
{
    public const int LongitudMaximaDeLaVersion = 40;

    private readonly List<AceptacionDeTexto> _aceptadas;

    internal AceptacionesDelCheckout(IEnumerable<AceptacionDeTexto>? aceptadas = null) => _aceptadas = [.. aceptadas ?? []];

    public IReadOnlyList<AceptacionDeTexto> Aceptadas => _aceptadas;

    /// <summary>Los textos que hay que aceptar: términos y privacidad siempre y el reembolso digital si hay digitales (CAR-12, CAR-13).</summary>
    public static IReadOnlyList<TextoLegal> Requeridas(bool hayDigitales) =>
        hayDigitales ? [TextoLegal.Terminos, TextoLegal.Privacidad, TextoLegal.ReembolsoDigital] : [TextoLegal.Terminos, TextoLegal.Privacidad];

    /// <summary>Los textos requeridos que aún no tienen una aceptación vigente, en el orden de <see cref="Requeridas"/>.</summary>
    public IReadOnlyList<TextoLegal> PorAceptar(bool hayDigitales) => [.. Requeridas(hayDigitales).Where(t => !_aceptadas.Exists(a => a.Texto == t))];

    /// <summary>Guarda la aceptación y dice si cambió algo: la misma versión ya aceptada no cambia nada ni la fecha.</summary>
    internal bool Aceptar(TextoLegal texto, string version, DateTimeOffset ahora)
    {
        ValidarVersion(version);
        var indice = _aceptadas.FindIndex(a => a.Texto == texto);
        if (indice >= 0 && _aceptadas[indice].Version == version)
        {
            return false;
        }

        var nueva = new AceptacionDeTexto(texto, version, ahora);
        if (indice >= 0)
        {
            _aceptadas[indice] = nueva;
        }
        else
        {
            _aceptadas.Add(nueva);
        }

        return true;
    }

    /// <summary>
    /// Invalida las aceptaciones de los textos requeridos cuya versión ya no es la vigente (CAR-28); si no se conoce la vigente, se invalida (no se puede comparar). Devuelve cuáles. Una aceptación de un texto que ya no se exige no se toca.
    /// </summary>
    internal IReadOnlyList<TextoLegal> InvalidarLasQueCambiaron(IReadOnlyDictionary<TextoLegal, string> vigentes, bool hayDigitales)
    {
        var requeridas = Requeridas(hayDigitales);
        var invalidadas = _aceptadas
            .Where(a => requeridas.Contains(a.Texto) && !(vigentes.TryGetValue(a.Texto, out var vigente) && vigente == a.Version))
            .Select(a => a.Texto)
            .ToList();
        _aceptadas.RemoveAll(a => invalidadas.Contains(a.Texto));
        return invalidadas;
    }

    private static void ValidarVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > LongitudMaximaDeLaVersion || version.Any(char.IsControl))
        {
            throw new ArgumentException("La versión de un texto legal es obligatoria, de hasta 40 caracteres y sin caracteres de control.", nameof(version));
        }
    }
}
