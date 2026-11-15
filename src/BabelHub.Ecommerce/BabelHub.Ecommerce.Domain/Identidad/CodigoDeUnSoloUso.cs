using System.Security.Cryptography;

namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>Para qué se envió el código (IDE-16, IDE-19): verificar el correo del registro o del cambio, o recuperar el acceso.</summary>
public enum PropositoDeCodigo
{
    VerificacionDeCorreo,
    RecuperacionDeAcceso,
}

public enum ResultadoDelCodigo
{
    Correcto,

    /// <summary>No coincide: cuenta un intento.</summary>
    Incorrecto,

    Vencido,

    /// <summary>Ya se usó o un código nuevo lo invalidó.</summary>
    NoVigente,

    /// <summary>Llegó a los 5 intentos: ya no sirve ni con el valor correcto (IDE-19).</summary>
    IntentosAgotados,
}

/// <summary>
/// El código de un solo uso enviado por correo (IDE-16, IDE-19, IDE-56; D05): 6 dígitos, <b>10 minutos</b> de vigencia, <b>5 intentos</b>, se consume al acertar, y un código nuevo del mismo propósito invalida el anterior. Aquí vive la
/// regla; que los intentos y el consumo se actualicen sin carreras con solicitudes paralelas lo garantiza la base con <c>UPDATE</c> condicional (D17). El código no se guarda: solo su huella con clave (HMAC); las huellas se comparan en
/// tiempo constante.
/// </summary>
public sealed class CodigoDeUnSoloUso
{
    public const int Digitos = 6;

    public const int MaximoDeIntentos = 5;

    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

    private CodigoDeUnSoloUso(Guid usuarioId, PropositoDeCodigo proposito, string huella, DateTimeOffset creado, int intentos, bool usado, bool invalidado)
    {
        UsuarioId = usuarioId;
        Proposito = proposito;
        Huella = huella;
        Creado = creado;
        Vence = creado + Vigencia;
        Intentos = intentos;
        Usado = usado;
        Invalidado = invalidado;
    }

    public Guid UsuarioId { get; }

    public PropositoDeCodigo Proposito { get; }

    /// <summary>El HMAC con clave del código (nunca el código).</summary>
    public string Huella { get; }

    public DateTimeOffset Creado { get; }

    public DateTimeOffset Vence { get; }

    public int Intentos { get; private set; }

    public bool Usado { get; private set; }

    public bool Invalidado { get; private set; }

    /// <summary>Un código de 6 dígitos (con ceros a la izquierda) generado con un generador criptográfico.</summary>
    public static string Generar() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static bool TieneLaForma(string? codigo) => codigo is { Length: Digitos } && codigo.All(char.IsAsciiDigit);

    public static CodigoDeUnSoloUso Emitir(Guid usuarioId, PropositoDeCodigo proposito, string huella, DateTimeOffset ahora)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El código es de un usuario.", nameof(usuarioId));
        }

        ArgumentException.ThrowIfNullOrEmpty(huella);
        return new CodigoDeUnSoloUso(usuarioId, proposito, huella, ahora, 0, usado: false, invalidado: false);
    }

    public static CodigoDeUnSoloUso Restaurar(Guid usuarioId, PropositoDeCodigo proposito, string huella, DateTimeOffset creado, int intentos, bool usado, bool invalidado) =>
        new(usuarioId, proposito, huella, creado, intentos, usado, invalidado);

    /// <summary>
    /// Presenta el código (su huella): lo acepta una sola vez, antes de que venza y dentro de los 5 intentos; un error cuenta un intento. El motivo exacto (vencido, usado, agotado) solo sirve al registro del evento (IDE-19): quien
    /// responde a la persona usa siempre el mismo mensaje genérico.
    /// </summary>
    public ResultadoDelCodigo Verificar(string huellaPresentada, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(huellaPresentada);
        if (Usado || Invalidado)
        {
            return ResultadoDelCodigo.NoVigente;
        }

        if (ahora >= Vence)
        {
            return ResultadoDelCodigo.Vencido;
        }

        if (Intentos >= MaximoDeIntentos)
        {
            return ResultadoDelCodigo.IntentosAgotados;
        }

        if (!Iguales(Huella, huellaPresentada))
        {
            Intentos++;
            return ResultadoDelCodigo.Incorrecto;
        }

        Usado = true;
        return ResultadoDelCodigo.Correcto;
    }

    /// <summary>Un código nuevo del mismo propósito invalida este (IDE-56): deja de servir aunque no venza.</summary>
    public void Invalidar() => Invalidado = true;

    private static bool Iguales(string a, string b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    public override string ToString() => "[código]";
}
