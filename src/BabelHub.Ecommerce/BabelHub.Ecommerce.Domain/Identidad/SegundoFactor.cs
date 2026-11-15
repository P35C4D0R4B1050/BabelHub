using System.Globalization;
using System.Security.Cryptography;

namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>
/// El código TOTP de RFC 6238 (IDE-20, IDE-51; D04): HMAC-SHA1, 6 dígitos y pasos de 30 segundos desde la época. Es pura: el secreto y el instante llegan como parámetros. Acepta el paso actual y uno hacia cada lado
/// (±30 s), y <b>solo</b> un paso posterior al último aceptado, de modo que un código ya usado se rechaza (IDE-41); que dos solicitudes simultáneas no acepten el mismo paso lo garantiza la base con un <c>UPDATE</c> condicional (D04).
/// </summary>
public static class Totp
{
    public const int Digitos = 6;

    public const int SegundosPorPaso = 30;

    /// <summary>Cuántos pasos se aceptan hacia cada lado del actual (IDE-51).</summary>
    public const int PasosDeTolerancia = 1;

    public static long PasoDe(DateTimeOffset instante) => instante.ToUnixTimeSeconds() / SegundosPorPaso;

    public static string Codigo(ReadOnlySpan<byte> secreto, long paso)
    {
        Span<byte> contador = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            contador[i] = (byte)(paso & 0xFF);
            paso >>= 8;
        }

        Span<byte> resumen = stackalloc byte[20];
        HMACSHA1.HashData(secreto, contador, resumen);
        var desplazamiento = resumen[^1] & 0x0F;
        var binario = ((resumen[desplazamiento] & 0x7F) << 24) | (resumen[desplazamiento + 1] << 16) | (resumen[desplazamiento + 2] << 8) | resumen[desplazamiento + 3];
        var modulo = 1;
        for (var i = 0; i < Digitos; i++)
        {
            modulo *= 10;
        }

        return (binario % modulo).ToString(CultureInfo.InvariantCulture).PadLeft(Digitos, '0');
    }

    /// <summary>
    /// El paso que acepta el código presentado, o nulo si ninguno de los tres pasos de la ventana lo produce o si el paso ya se usó (es igual o anterior a <paramref name="ultimoPasoUsado"/>). Compara en tiempo constante.
    /// </summary>
    public static long? PasoAceptado(ReadOnlySpan<byte> secreto, string? codigo, DateTimeOffset ahora, long? ultimoPasoUsado)
    {
        if (codigo is null || codigo.Length != Digitos || !codigo.All(char.IsAsciiDigit))
        {
            return null;
        }

        var actual = PasoDe(ahora);
        long? aceptado = null;
        for (var paso = actual - PasosDeTolerancia; paso <= actual + PasosDeTolerancia; paso++)
        {
            var esperado = Codigo(secreto, paso);
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(esperado), System.Text.Encoding.ASCII.GetBytes(codigo))
                && (ultimoPasoUsado is null || paso > ultimoPasoUsado))
            {
                aceptado = paso;
            }
        }

        return aceptado;
    }
}

/// <summary>Los códigos de respaldo (IDE-21, IDE-22, IDE-52): 8, de un solo uso y con al menos 50 bits de entropía cada uno (aquí, 60: doce símbolos de un alfabeto de 32). Solo se guardan sus huellas con clave (HMAC con pimienta).</summary>
public static class CodigosDeRespaldo
{
    public const int Cantidad = 8;

    public const int Simbolos = 12;

    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Genera los códigos legibles (se muestran una sola vez): <c>XXXX-XXXX-XXXX</c>.</summary>
    public static IReadOnlyList<string> Generar()
    {
        var codigos = new List<string>(Cantidad);
        for (var i = 0; i < Cantidad; i++)
        {
            var simbolos = new char[Simbolos];
            for (var j = 0; j < Simbolos; j++)
            {
                simbolos[j] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
            }

            codigos.Add($"{new string(simbolos, 0, 4)}-{new string(simbolos, 4, 4)}-{new string(simbolos, 8, 4)}");
        }

        return codigos;
    }

    /// <summary>La forma canónica con la que se calcula la huella: sin guiones ni espacios y en mayúsculas.</summary>
    public static string Canonico(string? codigo) => string.Concat((codigo ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static bool TieneLaForma(string? codigo) => Canonico(codigo) is { Length: Simbolos } canonico && canonico.All(c => Alfabeto.Contains(c, StringComparison.Ordinal));
}

public enum EstadoDelSegundoFactor
{
    /// <summary>El secreto existe pero no se confirmó con un código: no cuenta como activo (IDE-21).</summary>
    SinConfirmar,
    Activo,
}

public enum ResultadoDeSegundoFactor
{
    Aceptado,
    Rechazado,
    NoEstaActivo,
}

/// <summary>El resultado de usar un código de respaldo (IDE-22): si se aceptó y cuántos quedan.</summary>
public readonly record struct ResultadoDeRespaldo(bool Aceptado, int Restantes);

/// <summary>
/// El segundo factor de un usuario (IDE-20 a IDE-22, IDE-41, IDE-50 a IDE-52): se <b>activa solo tras confirmar</b> con un código TOTP válido; acepta un paso solo si es posterior al último aceptado; sus 8 códigos de
/// respaldo se guardan como huellas y cada uno sirve una vez; regenerarlos invalida los anteriores y restablecerlo lo deja sin efecto. El secreto llega en claro solo para validar (la Infraestructura lo guarda cifrado con
/// AES-GCM, D04).
/// </summary>
public sealed class SegundoFactor
{
    private readonly HashSet<string> _huellasDisponibles = new(StringComparer.Ordinal);

    private SegundoFactor(Guid usuarioId, EstadoDelSegundoFactor estado, long? ultimoPaso)
    {
        UsuarioId = usuarioId;
        Estado = estado;
        UltimoPasoUsado = ultimoPaso;
    }

    public Guid UsuarioId { get; }

    public EstadoDelSegundoFactor Estado { get; private set; }

    public long? UltimoPasoUsado { get; private set; }

    public bool EstaActivo => Estado == EstadoDelSegundoFactor.Activo;

    public int CodigosDeRespaldoRestantes => _huellasDisponibles.Count;

    /// <summary>Un segundo factor recién creado: sin confirmar y sin códigos de respaldo.</summary>
    public static SegundoFactor Nuevo(Guid usuarioId)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El segundo factor es de un usuario.", nameof(usuarioId));
        }

        return new SegundoFactor(usuarioId, EstadoDelSegundoFactor.SinConfirmar, null);
    }

    public static SegundoFactor Restaurar(Guid usuarioId, EstadoDelSegundoFactor estado, long? ultimoPaso, IEnumerable<string> huellasDisponibles)
    {
        var factor = new SegundoFactor(usuarioId, estado, ultimoPaso);
        foreach (var huella in huellasDisponibles)
        {
            factor._huellasDisponibles.Add(huella);
        }

        return factor;
    }

    public IReadOnlyCollection<string> HuellasDisponibles => _huellasDisponibles;

    /// <summary>
    /// Confirma la configuración con un código TOTP válido y la activa, y guarda las huellas de los 8 códigos de respaldo que se le entregan (IDE-21): sin un código válido no se activa. Un segundo factor ya activo no se confirma de nuevo.
    /// </summary>
    public ResultadoDeSegundoFactor Confirmar(ReadOnlySpan<byte> secreto, string? codigo, DateTimeOffset ahora, IReadOnlyList<string> huellasDeRespaldo)
    {
        ArgumentNullException.ThrowIfNull(huellasDeRespaldo);
        if (EstaActivo)
        {
            return ResultadoDeSegundoFactor.NoEstaActivo;
        }

        if (huellasDeRespaldo.Count != CodigosDeRespaldo.Cantidad || huellasDeRespaldo.Distinct(StringComparer.Ordinal).Count() != CodigosDeRespaldo.Cantidad)
        {
            throw new ArgumentException($"Se entregan {CodigosDeRespaldo.Cantidad} códigos de respaldo distintos.", nameof(huellasDeRespaldo));
        }

        if (Totp.PasoAceptado(secreto, codigo, ahora, UltimoPasoUsado) is not { } paso)
        {
            return ResultadoDeSegundoFactor.Rechazado;
        }

        UltimoPasoUsado = paso;
        Estado = EstadoDelSegundoFactor.Activo;
        Sustituir(huellasDeRespaldo);
        return ResultadoDeSegundoFactor.Aceptado;
    }

    /// <summary>Presenta un código TOTP al ingresar (IDE-20, IDE-50): se acepta uno de la ventana ±1 que no sea de un paso ya usado (IDE-41).</summary>
    public ResultadoDeSegundoFactor Presentar(ReadOnlySpan<byte> secreto, string? codigo, DateTimeOffset ahora)
    {
        if (!EstaActivo)
        {
            return ResultadoDeSegundoFactor.NoEstaActivo;
        }

        if (Totp.PasoAceptado(secreto, codigo, ahora, UltimoPasoUsado) is not { } paso)
        {
            return ResultadoDeSegundoFactor.Rechazado;
        }

        UltimoPasoUsado = paso;
        return ResultadoDeSegundoFactor.Aceptado;
    }

    /// <summary>Usa un código de respaldo en lugar del TOTP (IDE-22): se acepta una sola vez y se indica cuántos quedan.</summary>
    public ResultadoDeRespaldo UsarCodigoDeRespaldo(string huella)
    {
        ArgumentException.ThrowIfNullOrEmpty(huella);
        if (!EstaActivo || !_huellasDisponibles.Remove(huella))
        {
            return new ResultadoDeRespaldo(false, _huellasDisponibles.Count);
        }

        return new ResultadoDeRespaldo(true, _huellasDisponibles.Count);
    }

    /// <summary>
    /// Regenera los códigos de respaldo (IDE-52): exige un código TOTP vigente (la contraseña la comprueba la Aplicación), invalida los anteriores y deja los 8 nuevos.
    /// </summary>
    public ResultadoDeSegundoFactor RegenerarCodigosDeRespaldo(ReadOnlySpan<byte> secreto, string? codigo, DateTimeOffset ahora, IReadOnlyList<string> huellasNuevas)
    {
        ArgumentNullException.ThrowIfNull(huellasNuevas);
        if (huellasNuevas.Count != CodigosDeRespaldo.Cantidad || huellasNuevas.Distinct(StringComparer.Ordinal).Count() != CodigosDeRespaldo.Cantidad)
        {
            throw new ArgumentException($"Se entregan {CodigosDeRespaldo.Cantidad} códigos de respaldo distintos.", nameof(huellasNuevas));
        }

        var presentado = Presentar(secreto, codigo, ahora);
        if (presentado != ResultadoDeSegundoFactor.Aceptado)
        {
            return presentado;
        }

        Sustituir(huellasNuevas);
        return ResultadoDeSegundoFactor.Aceptado;
    }

    /// <summary>El restablecimiento por la administración (IDE-49 impide el propio): el segundo factor queda sin confirmar y sin códigos, y el usuario debe configurarlo de nuevo (IDE-21).</summary>
    public void Restablecer()
    {
        Estado = EstadoDelSegundoFactor.SinConfirmar;
        UltimoPasoUsado = null;
        _huellasDisponibles.Clear();
    }

    private void Sustituir(IReadOnlyList<string> huellas)
    {
        _huellasDisponibles.Clear();
        foreach (var huella in huellas)
        {
            _huellasDisponibles.Add(huella);
        }
    }
}
