using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Identidad;

/// <summary>
/// El contrato público del módulo <c>Identidad</c> (regla 13: los demás módulos y la Api dependen solo de esta raíz, con modelos propios y nunca de su <c>Interno</c>, su Dominio o su esquema). Aquí están los
/// <b>modelos</b> comunes; las interfaces de cada operación van en <c>ContratoDeRegistroEIngreso.cs</c> y <c>ContratoDeLaCuenta.cs</c> y los puertos que otros módulos implementan, en <c>PuertosDeIdentidad.cs</c>.
/// </summary>
public enum RolDeCuenta
{
    Cliente,
    EncargadoDeSucursal,
    Logistica,
    AdministradorCentral,
}

/// <summary>
/// La credencial de acceso (JWT de 15 minutos, ES256) y la renovación opaca de un ingreso (IDE-02, IDE-46). Los dos valores son secretos: <b>solo los lee la capa servidor</b>, que los guarda cifrados en
/// <c>web.sesiones_web</c>; nunca llegan al navegador (AGENTS.md §3.9) y su texto no aparece en ninguna representación.
/// </summary>
public sealed record CredencialDeSesion(
    ValorSecreto Credencial,
    DateTimeOffset CredencialExpiraEn,
    ValorSecreto Renovacion,
    DateTimeOffset RenovacionExpiraEn,
    Guid UsuarioId,
    RolDeCuenta Rol,
    Guid? Sucursal,
    bool CorreoVerificado);

/// <summary>El paso que falta para terminar el ingreso (IDE-20, IDE-21, IDE-23, IDE-42).</summary>
public enum PasoPendienteDelIngreso
{
    PresentarSegundoFactor,
    ConfigurarSegundoFactor,
    CambiarContrasenaTemporal,
}

/// <summary>
/// El desafío de un ingreso en dos pasos (IDE-42): un identificador opaco de 256 bits, de 5 minutos como máximo, que solo permite el paso que dice y no emite ninguna credencial. Es secreto de la capa
/// servidor (viaja en su propia cookie y nunca en la URL).
/// </summary>
public sealed record DesafioDelIngreso(ValorSecreto Identificador, PasoPendienteDelIngreso Paso, DateTimeOffset VenceEn);

/// <summary>El secreto para configurar el segundo factor: se muestra una sola vez (IDE-21).</summary>
public sealed record SecretoDelSegundoFactor(ValorSecreto SecretoBase32, string Emisor, string Cuenta);

/// <summary>
/// Cómo termina un paso del ingreso: con la credencial (ya está dentro) o con otro desafío (falta un paso). Tras configurar el segundo factor trae además los 8 códigos de respaldo, que se muestran una sola vez
/// (IDE-21); al usar uno, cuántos quedan (IDE-22).
/// </summary>
public sealed record ResultadoDelIngreso
{
    public CredencialDeSesion? Credencial { get; init; }

    public DesafioDelIngreso? Desafio { get; init; }

    public IReadOnlyList<ValorSecreto> CodigosDeRespaldo { get; init; } = [];

    public int? CodigosDeRespaldoRestantes { get; init; }

    public bool Completo => Credencial is not null;
}

/// <summary>El nombre, el correo y la contraseña del registro, con las versiones de los textos que la persona aceptó y su declaración de mayoría de edad (IDE-01, IDE-34, IDE-39).</summary>
public sealed record SolicitudDeAltaDeCuenta(
    string Nombre,
    string Correo,
    ValorSecreto Contrasena,
    string? VersionDeTerminos,
    string? VersionDePrivacidad,
    bool DeclaraSerMayorDeEdad);

/// <summary>
/// Lo que responden el registro, el reenvío, la recuperación y el cierre de sesión: <b>siempre lo mismo</b> haya o no una cuenta con ese correo (IDE-13, IDE-17), sin el identificador de la cuenta ni ningún dato de ella. Le dice a la persona que revise su correo, si aplica, para
/// verificarlo.
/// </summary>
public sealed record RespuestaUniforme;

/// <summary>Las credenciales de un ingreso: el correo y la contraseña tal como se escribieron y la marca opaca del dispositivo, si la capa servidor la trae (IDE-53).</summary>
public sealed record SolicitudDeIngreso(string Correo, ValorSecreto Contrasena, ValorSecreto? MarcaDeDispositivo = null, string? Region = null);

/// <summary>Lo que hace falta para decidir si una persona puede comprar o ver sus datos (IDE-27, IDE-29, IDE-58, WEB-46): lo informa <see cref="IEstadoDeCuenta"/>.</summary>
public enum EstadoDeAceptacionDeLosTextos
{
    /// <summary>Aceptó la versión vigente de cada texto.</summary>
    AlDia,

    /// <summary>Falta aceptar la versión vigente de algún texto: puede ver su cuenta y sus pedidos pero no iniciar pagos ni hacer cambios sensibles (IDE-58).</summary>
    Pendiente,
}

/// <summary>
/// El estado de una cuenta para quien necesita decidir qué permitir (Carrito, Pagos, la política de la web): <b>un valor sin datos personales</b>. «Puede comprar» es falso si la cuenta está desactivada o
/// cancelada, con una cancelación diferida o en ejecución (IDE-27, IDE-71), con el correo sin verificar (IDE-29) o con textos pendientes (IDE-58).
/// </summary>
public sealed record EstadoDeCuenta(
    Guid UsuarioId,
    bool PuedeComprar,
    EstadoDeAceptacionDeLosTextos Textos,
    bool CorreoVerificado,
    bool CuentaCanceladaODesactivada,
    bool TieneWhatsAppConConsentimiento);

/// <summary>
/// El contacto de avisos de un cliente tal como lo necesita <b>solo</b> Notificaciones al enviar (NOT-03, NOT-08): si consiente, si la cuenta sigue vigente y el número en formato E.164. El número sale de Identidad únicamente
/// por este puerto, hacia el envío; ninguna pantalla ni otro módulo lo recibe, y nunca se imprime.
/// </summary>
public sealed record ContactoDeAvisosDelCliente(bool CuentaCanceladaODesactivada, bool Consiente, string? NumeroE164)
{
    /// <summary>No imprime el número.</summary>
    public override string ToString() => "[contacto de avisos]";
}

/// <summary>
/// Lo que Notificaciones lee de Identidad justo antes de enviar un aviso a un cliente (NOT-03, NOT-08): su consentimiento y su número vigentes, <b>sin caché</b>. <see langword="null"/> si la cuenta no existe. Si Identidad no
/// responde, la excepción sube y el envío se aplaza sin consumir un intento (falla cerrada: nunca se envía sin conocer el consentimiento).
/// </summary>
public interface IContactoDeAvisosDelCliente
{
    Task<ContactoDeAvisosDelCliente?> Consultar(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>
/// El correo del titular de una cuenta para quien debe <b>enviarle</b> un aviso sin ser él (el trabajo de fondo de avisos de pago, PAG-22, PAG-23): solo el correo, sin caché, sin guardarlo y sin registrar denegaciones
/// (el actor es el sistema). <see langword="null"/> si la cuenta no existe, está cancelada o desactivada, o si su correo no está verificado; si Identidad no responde, la excepción sube y quien llama reintenta.
/// </summary>
public interface ICorreoDelTitular
{
    Task<string?> Consultar(Guid usuarioId, CancellationToken cancelacion = default);
}
