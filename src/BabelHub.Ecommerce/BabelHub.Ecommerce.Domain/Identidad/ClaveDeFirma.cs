using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Identidad;

public enum EstadoDeClaveDeFirma
{
    /// <summary>La única que firma credenciales nuevas (D01): siempre hay exactamente una.</summary>
    Activa,

    /// <summary>Ya no firma, pero sus credenciales siguen valiendo hasta que venzan (IDE-36).</summary>
    Anterior,

    /// <summary>Declarada comprometida (IDE-61): no se acepta ninguna credencial firmada con ella.</summary>
    Comprometida,

    /// <summary>La anterior cuyas credenciales ya vencieron: se conserva el registro, no se acepta.</summary>
    Retirada,
}

/// <summary>
/// Una clave de firma de credenciales (IDE-35, IDE-36, IDE-61; D01): se identifica por su <c>kid</c>, que va en cada credencial. La <b>privada nunca está aquí</b> (vive cifrada en la Infraestructura); esta clase solo lleva el
/// ciclo de vida: activa, anterior, comprometida o retirada.
/// </summary>
public sealed partial class ClaveDeFirma
{
    /// <summary>Cada 90 días desde la última rotación (IDE-36).</summary>
    public static readonly TimeSpan PeriodoDeRotacion = TimeSpan.FromDays(90);

    /// <summary>La vigencia de una credencial de acceso (15 minutos): la clave anterior se acepta hasta que venza la última credencial que firmó (IDE-36).</summary>
    public static readonly TimeSpan VigenciaDeLaCredencial = TimeSpan.FromMinutes(15);

    private ClaveDeFirma(string kid, DateTimeOffset creada, EstadoDeClaveDeFirma estado, DateTimeOffset? dejoDeFirmarEn, DateTimeOffset? comprometidaEn)
    {
        Kid = kid;
        Creada = creada;
        Estado = estado;
        DejoDeFirmarEn = dejoDeFirmarEn;
        ComprometidaEn = comprometidaEn;
    }

    public string Kid { get; }

    public DateTimeOffset Creada { get; }

    public EstadoDeClaveDeFirma Estado { get; private set; }

    /// <summary>Cuándo dejó de firmar (se rotó o se comprometió).</summary>
    public DateTimeOffset? DejoDeFirmarEn { get; private set; }

    public DateTimeOffset? ComprometidaEn { get; private set; }

    public static ClaveDeFirma Nueva(string kid, DateTimeOffset ahora) => new(ValidarKid(kid), ahora, EstadoDeClaveDeFirma.Activa, null, null);

    public static ClaveDeFirma Restaurar(string kid, DateTimeOffset creada, EstadoDeClaveDeFirma estado, DateTimeOffset? dejoDeFirmarEn, DateTimeOffset? comprometidaEn) =>
        new(ValidarKid(kid), creada, estado, dejoDeFirmarEn, comprometidaEn);

    /// <summary>
    /// ¿Se acepta una credencial firmada con esta clave? La activa, siempre; la anterior, hasta que venza la última credencial que firmó (<see cref="VigenciaDeLaCredencial"/> después de dejar de firmar, IDE-36); la
    /// comprometida y la retirada, nunca (IDE-61).
    /// </summary>
    public bool Acepta(DateTimeOffset ahora) => Estado switch
    {
        EstadoDeClaveDeFirma.Activa => true,
        EstadoDeClaveDeFirma.Anterior => DejoDeFirmarEn is { } desde && ahora < desde + VigenciaDeLaCredencial,
        _ => false,
    };

    internal void PasarAAnterior(DateTimeOffset ahora)
    {
        Estado = EstadoDeClaveDeFirma.Anterior;
        DejoDeFirmarEn = ahora;
    }

    internal void Comprometer(DateTimeOffset ahora)
    {
        Estado = EstadoDeClaveDeFirma.Comprometida;
        DejoDeFirmarEn ??= ahora;
        ComprometidaEn = ahora;
    }

    internal void Retirar() => Estado = EstadoDeClaveDeFirma.Retirada;

    private static string ValidarKid(string kid)
    {
        if (kid is null || !FormatoDelKid().IsMatch(kid))
        {
            throw new ArgumentException("El identificador de la clave (kid) tiene de 8 a 64 letras, dígitos, guiones o guiones bajos.", nameof(kid));
        }

        return kid;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDelKid();
}

/// <summary>Lo que ocurrió al declarar comprometida una clave: la clave nueva que firma desde ahora y la orden de revocar de inmediato todas las renovaciones opacas y publicar la revocación (IDE-61).</summary>
public sealed record ResultadoDeCompromiso(string KidComprometido, ClaveDeFirma ClaveActiva, bool RevocarRenovaciones, bool PublicarRevocacion);

/// <summary>
/// El juego de claves de firma (IDE-35, IDE-36, IDE-61, D01): <b>una sola clave activa</b> (la invariante que además garantiza un índice único parcial), la anterior mientras duren sus credenciales, y las comprometidas. Rota a los
/// 90 días; declarar comprometida una clave la rechaza de inmediato, rota si era la activa, revoca las renovaciones y publica la revocación. Los instantes llegan como parámetros.
/// </summary>
public sealed class JuegoDeClavesDeFirma
{
    private readonly List<ClaveDeFirma> _claves;

    private JuegoDeClavesDeFirma(IEnumerable<ClaveDeFirma> claves)
    {
        _claves = [.. claves];
        if (_claves.Count(c => c.Estado == EstadoDeClaveDeFirma.Activa) != 1)
        {
            throw new ArgumentException("Debe haber exactamente una clave activa.", nameof(claves));
        }

        if (_claves.Select(c => c.Kid).Distinct(StringComparer.Ordinal).Count() != _claves.Count)
        {
            throw new ArgumentException("Los identificadores de clave no se repiten.", nameof(claves));
        }
    }

    public IReadOnlyList<ClaveDeFirma> Claves => _claves;

    public ClaveDeFirma Activa => _claves.Single(c => c.Estado == EstadoDeClaveDeFirma.Activa);

    public IReadOnlyList<string> Revocadas => [.. _claves.Where(c => c.Estado == EstadoDeClaveDeFirma.Comprometida).Select(c => c.Kid)];

    public static JuegoDeClavesDeFirma Iniciar(ClaveDeFirma primera)
    {
        ArgumentNullException.ThrowIfNull(primera);
        return new JuegoDeClavesDeFirma([primera]);
    }

    public static JuegoDeClavesDeFirma Restaurar(IEnumerable<ClaveDeFirma> claves) => new(claves);

    /// <summary>Desde la última rotación pasaron 90 días (IDE-36).</summary>
    public bool DebeRotar(DateTimeOffset ahora) => ahora - Activa.Creada >= ClaveDeFirma.PeriodoDeRotacion;

    /// <summary>
    /// Rota la clave de firma (IDE-36): la activa pasa a anterior (sus credenciales valen hasta que venzan) y <paramref name="nueva"/> firma desde ahora. Retira las anteriores cuyas credenciales ya vencieron. La rotación se
    /// registra en la auditoría (lo hace la Aplicación).
    /// </summary>
    public void Rotar(ClaveDeFirma nueva, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(nueva);
        if (nueva.Estado != EstadoDeClaveDeFirma.Activa || _claves.Any(c => c.Kid == nueva.Kid))
        {
            throw new ArgumentException("La clave nueva es activa y con un kid que no existe.", nameof(nueva));
        }

        Activa.PasarAAnterior(ahora);
        _claves.Add(nueva);
        RetirarVencidas(ahora);
    }

    /// <summary>
    /// Declara comprometida la clave <paramref name="kid"/> (IDE-61): se rechaza de inmediato toda credencial firmada con ella; si era la activa se rota con <paramref name="claveNueva"/> (obligatoria en ese caso); se ordena revocar
    /// las renovaciones y publicar la revocación. Es idempotente (repetirla tras un fallo parcial vuelve a ordenar la revocación) y devuelve nulo solo si el kid no existe.
    /// </summary>
    public ResultadoDeCompromiso? DeclararComprometida(string kid, ClaveDeFirma? claveNueva, DateTimeOffset ahora)
    {
        var comprometida = _claves.FirstOrDefault(c => c.Kid == kid);
        if (comprometida is null)
        {
            return null;
        }

        if (comprometida.Estado == EstadoDeClaveDeFirma.Activa)
        {
            if (claveNueva is null || claveNueva.Estado != EstadoDeClaveDeFirma.Activa || _claves.Any(c => c.Kid == claveNueva.Kid))
            {
                throw new ArgumentException("Declarar comprometida la clave activa exige una clave nueva activa con un kid que no existe.", nameof(claveNueva));
            }

            comprometida.Comprometer(ahora);
            _claves.Add(claveNueva);
        }
        else if (comprometida.Estado != EstadoDeClaveDeFirma.Comprometida)
        {
            comprometida.Comprometer(ahora);
        }

        RetirarVencidas(ahora);
        return new ResultadoDeCompromiso(kid, Activa, RevocarRenovaciones: true, PublicarRevocacion: true);
    }

    /// <summary>¿Se acepta una credencial con este <c>kid</c>? Solo si la clave existe y la acepta (un kid desconocido se rechaza).</summary>
    public bool Acepta(string? kid, DateTimeOffset ahora) => kid is not null && _claves.FirstOrDefault(c => c.Kid == kid) is { } clave && clave.Acepta(ahora);

    /// <summary>Las claves que se publican para verificar: la activa y las anteriores que aún se aceptan (nunca las comprometidas).</summary>
    public IReadOnlyList<ClaveDeFirma> Publicables(DateTimeOffset ahora) => [.. _claves.Where(c => c.Acepta(ahora))];

    private void RetirarVencidas(DateTimeOffset ahora)
    {
        foreach (var clave in _claves.Where(c => c.Estado == EstadoDeClaveDeFirma.Anterior && !c.Acepta(ahora)))
        {
            clave.Retirar();
        }
    }
}
