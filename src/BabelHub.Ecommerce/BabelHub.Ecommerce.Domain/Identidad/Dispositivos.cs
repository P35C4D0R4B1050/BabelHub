using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>
/// El dispositivo reconocido de un usuario (IDE-53 a IDE-55, D11): una marca aleatoria opaca de 256 bits que viaja en una cookie y de la que solo se guarda su SHA-256, y la región (el país que entrega el borde) del ingreso en que
/// se emitió. <b>No se guarda la ubicación exacta ni la dirección</b>. Un dispositivo puede estar <b>congelado</b> para los cambios sensibles mientras un cambio de correo no reconocido está pendiente (IDE-54).
/// </summary>
public sealed partial class Dispositivo
{
    private Dispositivo(Guid usuarioId, string huellaDeLaMarca, string region, DateTimeOffset emitido, DateTimeOffset? congeladoHasta)
    {
        UsuarioId = usuarioId;
        HuellaDeLaMarca = huellaDeLaMarca;
        Region = region;
        Emitido = emitido;
        CongeladoHasta = congeladoHasta;
    }

    public Guid UsuarioId { get; }

    /// <summary>El SHA-256 en hexadecimal minúsculo (64 caracteres) de la marca de 256 bits.</summary>
    public string HuellaDeLaMarca { get; }

    /// <summary>El país (código de dos letras mayúsculas) que dio el borde; «ZZ» si el borde no lo entregó.</summary>
    public string Region { get; }

    public DateTimeOffset Emitido { get; }

    public DateTimeOffset? CongeladoHasta { get; private set; }

    public static Dispositivo Emitir(Guid usuarioId, string huellaDeLaMarca, string? region, DateTimeOffset ahora) =>
        new(usuarioId, ValidarHuella(huellaDeLaMarca), NormalizarRegion(region), ahora, null);

    public static Dispositivo Restaurar(Guid usuarioId, string huellaDeLaMarca, string region, DateTimeOffset emitido, DateTimeOffset? congeladoHasta) =>
        new(usuarioId, ValidarHuella(huellaDeLaMarca), NormalizarRegion(region), emitido, congeladoHasta);

    /// <summary>El país de dos letras mayúsculas, o «ZZ» si no hay uno válido: nunca un texto libre.</summary>
    public static string NormalizarRegion(string? region)
    {
        var candidato = region?.Trim().ToUpperInvariant();
        return candidato is not null && FormatoDeRegion().IsMatch(candidato) ? candidato : RegionDesconocida;
    }

    public const string RegionDesconocida = "ZZ";

    public bool EstaCongelado(DateTimeOffset ahora) => CongeladoHasta is { } hasta && ahora < hasta;

    public void Congelar(DateTimeOffset hasta) => CongeladoHasta = hasta;

    public void Descongelar() => CongeladoHasta = null;

    /// <summary>Un cambio sensible (correo, contraseña, segundo factor) se permite desde un dispositivo que no está congelado (IDE-54).</summary>
    public bool PermiteCambiosSensibles(DateTimeOffset ahora) => !EstaCongelado(ahora);

    private static string ValidarHuella(string huella)
    {
        if (huella is null || huella.Length != 64 || !huella.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException("La huella de la marca es un SHA-256 de 64 caracteres hexadecimales en minúscula.", nameof(huella));
        }

        return huella;
    }

    [GeneratedRegex("^[A-Z]{2}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoDeRegion();
}

/// <summary>Lo que se sabe de quien pide un cambio de correo (IDE-55): si lleva la marca de un ingreso anterior completo y si su región coincide con la de ingresos anteriores.</summary>
public readonly record struct ConfianzaDeLaSolicitud(bool DispositivoReconocido, bool RegionHabitual)
{
    /// <summary>Solo un dispositivo reconocido <b>y</b> una región habitual aplican el cambio de correo tras verificar el nuevo (IDE-53); cualquier otra combinación lo deja pendiente (IDE-54).</summary>
    public bool PermiteAplicarElCambio => DispositivoReconocido && RegionHabitual;
}

public static class ConfianzaDeDispositivos
{
    /// <summary>
    /// Evalúa la solicitud contra lo guardado: el dispositivo se reconoce por la huella de su marca (solo uno que no está congelado cuenta como reconocido para decidir un cambio) y la región es habitual si coincide con la de algún
    /// dispositivo del usuario. Una región desconocida («ZZ») nunca es habitual.
    /// </summary>
    public static ConfianzaDeLaSolicitud Evaluar(string? huellaDeLaMarcaPresentada, string? regionDelBorde, IReadOnlyCollection<Dispositivo> dispositivosDelUsuario, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(dispositivosDelUsuario);
        var reconocido = huellaDeLaMarcaPresentada is { Length: 64 } && dispositivosDelUsuario.Any(d => d.HuellaDeLaMarca == huellaDeLaMarcaPresentada && d.PermiteCambiosSensibles(ahora));
        var region = Dispositivo.NormalizarRegion(regionDelBorde);
        var habitual = region != Dispositivo.RegionDesconocida && dispositivosDelUsuario.Any(d => d.Region == region);
        return new ConfianzaDeLaSolicitud(reconocido, habitual);
    }
}

public enum EstadoDelCambioDeCorreo
{
    /// <summary>Esperando el código del correo nuevo, desde un dispositivo reconocido (IDE-53).</summary>
    EsperandoVerificacion,

    /// <summary>No se aplicó: dispositivo o región no reconocidos; el titular puede confirmarlo o rechazarlo desde un dispositivo reconocido en 24 horas (IDE-54).</summary>
    PendienteDeConfirmacion,

    Aplicado,

    Rechazado,

    /// <summary>Pasaron las 24 horas sin confirmarlo (IDE-54).</summary>
    Descartado,
}

public enum ResultadoDelCambioDeCorreo
{
    Hecho,
    FueraDePlazo,
    DesdeDispositivoNoReconocido,
    NoCorresponde,
}

/// <summary>
/// El cambio de correo en curso (IDE-16, IDE-53, IDE-54): desde un dispositivo reconocido y una región habitual espera la verificación del correo nuevo y se aplica; en otro caso <b>no se aplica</b>, queda pendiente
/// <b>24 horas</b> y el titular lo confirma o lo rechaza desde un dispositivo reconocido; vencido el plazo, se descarta. Quien lo pide queda con sus cambios sensibles congelados mientras tanto (el llamador usa
/// <see cref="Dispositivo.Congelar"/> con <see cref="Vence"/>).
/// </summary>
public sealed class CambioDeCorreoPendiente
{
    public static readonly TimeSpan Plazo = TimeSpan.FromHours(24);

    private CambioDeCorreoPendiente(Guid id, Guid usuarioId, string correoNuevo, DateTimeOffset creado, EstadoDelCambioDeCorreo estado)
    {
        Id = id;
        UsuarioId = usuarioId;
        CorreoNuevo = correoNuevo;
        Creado = creado;
        Vence = creado + Plazo;
        Estado = estado;
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public string CorreoNuevo { get; }

    public DateTimeOffset Creado { get; }

    public DateTimeOffset Vence { get; }

    public EstadoDelCambioDeCorreo Estado { get; private set; }

    public static CambioDeCorreoPendiente Pedir(Guid id, Guid usuarioId, string correoNuevo, ConfianzaDeLaSolicitud confianza, DateTimeOffset ahora)
    {
        if (id == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El cambio necesita su identificador y su titular.");
        }

        return new CambioDeCorreoPendiente(
            id,
            usuarioId,
            Usuario.NormalizarCorreo(correoNuevo),
            ahora,
            confianza.PermiteAplicarElCambio ? EstadoDelCambioDeCorreo.EsperandoVerificacion : EstadoDelCambioDeCorreo.PendienteDeConfirmacion);
    }

    public static CambioDeCorreoPendiente Restaurar(Guid id, Guid usuarioId, string correoNuevo, DateTimeOffset creado, EstadoDelCambioDeCorreo estado) =>
        new(id, usuarioId, Usuario.NormalizarCorreo(correoNuevo), creado, estado);

    /// <summary>Si el cambio no se aplicó por no reconocerse, los cambios sensibles del dispositivo que lo pidió se congelan hasta el vencimiento (IDE-54).</summary>
    public bool CongelaAlDispositivo => Estado == EstadoDelCambioDeCorreo.PendienteDeConfirmacion;

    /// <summary>
    /// El código del correo nuevo es correcto: si el cambio esperaba la verificación y está en plazo, se aplica (IDE-53, sin avisar al correo anterior). Un cambio pendiente de confirmación no se aplica por verificar el correo
    /// nuevo: necesita la confirmación del titular.
    /// </summary>
    public ResultadoDelCambioDeCorreo CorreoNuevoVerificado(DateTimeOffset ahora)
    {
        if (Estado != EstadoDelCambioDeCorreo.EsperandoVerificacion)
        {
            return ResultadoDelCambioDeCorreo.NoCorresponde;
        }

        if (ahora >= Vence)
        {
            Estado = EstadoDelCambioDeCorreo.Descartado;
            return ResultadoDelCambioDeCorreo.FueraDePlazo;
        }

        Estado = EstadoDelCambioDeCorreo.Aplicado;
        return ResultadoDelCambioDeCorreo.Hecho;
    }

    /// <summary>El titular lo confirma desde un dispositivo reconocido en las 24 horas (IDE-54): queda por verificar el correo nuevo.</summary>
    public ResultadoDelCambioDeCorreo ConfirmarElTitular(DateTimeOffset ahora, bool desdeDispositivoReconocido)
    {
        var previo = Antes(ahora, desdeDispositivoReconocido);
        if (previo is { } resultado)
        {
            return resultado;
        }

        Estado = EstadoDelCambioDeCorreo.EsperandoVerificacion;
        return ResultadoDelCambioDeCorreo.Hecho;
    }

    /// <summary>El titular lo rechaza desde un dispositivo reconocido (IDE-54): no se aplica y se descongela el dispositivo.</summary>
    public ResultadoDelCambioDeCorreo RechazarElTitular(DateTimeOffset ahora, bool desdeDispositivoReconocido)
    {
        var previo = Antes(ahora, desdeDispositivoReconocido);
        if (previo is { } resultado)
        {
            return resultado;
        }

        Estado = EstadoDelCambioDeCorreo.Rechazado;
        return ResultadoDelCambioDeCorreo.Hecho;
    }

    /// <summary>Vencidas las 24 horas, el cambio se descarta (IDE-54). Devuelve verdadero si lo descartó.</summary>
    public bool DescartarSiVencio(DateTimeOffset ahora)
    {
        if (Estado is EstadoDelCambioDeCorreo.PendienteDeConfirmacion or EstadoDelCambioDeCorreo.EsperandoVerificacion && ahora >= Vence)
        {
            Estado = EstadoDelCambioDeCorreo.Descartado;
            return true;
        }

        return false;
    }

    private ResultadoDelCambioDeCorreo? Antes(DateTimeOffset ahora, bool desdeDispositivoReconocido)
    {
        if (Estado != EstadoDelCambioDeCorreo.PendienteDeConfirmacion)
        {
            return ResultadoDelCambioDeCorreo.NoCorresponde;
        }

        if (ahora >= Vence)
        {
            Estado = EstadoDelCambioDeCorreo.Descartado;
            return ResultadoDelCambioDeCorreo.FueraDePlazo;
        }

        return desdeDispositivoReconocido ? null : ResultadoDelCambioDeCorreo.DesdeDispositivoNoReconocido;
    }
}
