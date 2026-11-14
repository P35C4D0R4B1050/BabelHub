using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>Lo que se guarda de una sesión: el agregado del Dominio y sus secretos cifrados (D02, D05).</summary>
public sealed class SesionGuardada
{
    public required SesionWeb Sesion { get; set; }

    public byte[]? CredencialCifrada { get; set; }

    public DateTimeOffset? CredencialVenceEn { get; set; }

    public byte[]? RenovacionCifrada { get; set; }

    /// <summary>Una caída entre la llamada a Identidad y la confirmación pudo rotar la renovación: la solicitud siguiente reintenta con la guardada (R10).</summary>
    public bool RenovacionIncierta { get; set; }

    /// <summary>La revocación de la renovación en Identidad no se confirmó: se reintenta hasta lograrse o hasta que venza la renovación (D05).</summary>
    public bool RevocacionPendiente { get; set; }

    public int ReintentosDeRevocacion { get; set; }

    public short VersionDeClave { get; set; }

    public string? RetornoTrasAceptar { get; set; }
}

/// <summary>
/// La fila de la sesión sigue bloqueada por otra solicitud después del plazo de espera (<c>lock_timeout</c>, 5 segundos): quien la pidió responde servicio no
/// disponible y la solicitud siguiente lo reintenta (plan §5). Nunca lleva datos de la sesión.
/// </summary>
public sealed class BloqueoDeSesionNoDisponibleException : Exception
{
    public BloqueoDeSesionNoDisponibleException()
        : base("La sesión está ocupada por otra solicitud.")
    {
    }

    public BloqueoDeSesionNoDisponibleException(string message)
        : base(message)
    {
    }

    public BloqueoDeSesionNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Puerto: la tabla de sesiones web (esquema <c>web</c>). <c>CargarParaRenovar</c> toma la fila con <c>SELECT … FOR UPDATE</c> y <c>lock_timeout</c>.</summary>
public interface IAlmacenDeSesionesWeb
{
    Task Insertar(SesionGuardada sesion, CancellationToken cancelacion = default);

    Task<SesionGuardada?> Cargar(HashDeSesion id, CancellationToken cancelacion = default);

    /// <summary>
    /// Toma la fila con <c>SELECT … FOR UPDATE</c> y un <c>lock_timeout</c> de 5 segundos; exige una unidad de trabajo activa (el bloqueo dura lo que ella).
    /// </summary>
    /// <exception cref="BloqueoDeSesionNoDisponibleException">Si otra solicitud la retiene más allá del plazo.</exception>
    Task<SesionGuardada?> CargarParaRenovar(HashDeSesion id, CancellationToken cancelacion = default);

    /// <summary>Guarda la fila que se cargó con <see cref="CargarParaRenovar"/>. Nunca reabre una fila ya cerrada (un identificador cerrado no vuelve a usarse, WEB-28).</summary>
    Task Guardar(SesionGuardada sesion, CancellationToken cancelacion = default);

    /// <summary>
    /// Actualiza <b>solo</b> la última actividad (<c>UPDATE … SET ultima_actividad WHERE estado = abierta</c>): una solicitud que solo registra actividad no debe pisar la credencial ni la
    /// renovación que otra acaba de rotar (R10).
    /// </summary>
    Task ActualizarActividad(HashDeSesion id, DateTimeOffset ultimaActividad, CancellationToken cancelacion = default);

    /// <summary>Las sesiones abiertas de un usuario, tomadas con <c>FOR UPDATE</c> y en un orden fijo (cerrar todas, plan §5).</summary>
    Task<IReadOnlyList<SesionGuardada>> CargarAbiertasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Puerto: la tabla de borradores (esquema <c>web</c>).</summary>
public interface IAlmacenDeBorradores
{
    Task<int> Contar(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>
    /// Toma el borrador con <c>FOR UPDATE</c>: dos entregas simultáneas del mismo borrador se serializan y la segunda ya no lo encuentra (entrega única). Los campos del
    /// borrador devuelto están vacíos; el cuerpo cifrado es lo que se guardó.
    /// </summary>
    Task<BorradorGuardado?> Cargar(Guid usuarioId, string plantillaDeRuta, CancellationToken cancelacion = default);

    /// <summary><c>INSERT … ON CONFLICT (usuario_id, plantilla_de_ruta) DO UPDATE</c>.</summary>
    Task Guardar(BorradorGuardado borrador, CancellationToken cancelacion = default);

    Task Borrar(Guid id, CancellationToken cancelacion = default);
}

/// <summary>Un borrador guardado: el agregado y su cuerpo cifrado.</summary>
public sealed record BorradorGuardado(BorradorDePantalla Borrador, byte[] CuerpoCifrado, short VersionDeClave);

public enum PropositoDeSecreto
{
    Credencial,
    Renovacion,
    Borrador,
}

public sealed record TextoCifrado(byte[] Bytes, short VersionDeClave);

/// <summary>Un texto cifrado que no se pudo descifrar (clave distinta, texto movido a otra fila, manipulado): equivale a una sesión inválida, nunca a un error 500 (D02).</summary>
public sealed class SecretoDeSesionIlegibleException : Exception
{
    public SecretoDeSesionIlegibleException()
        : base("No se pudo descifrar un secreto de la sesión.")
    {
    }
}

/// <summary>Puerto: cifra y descifra los secretos de la sesión con una subclave por propósito y datos asociados ligados a la fila (D02).</summary>
public interface ICifradorDeSecretosDeSesion
{
    TextoCifrado Cifrar(string texto, PropositoDeSecreto proposito, string datosAsociados);

    /// <exception cref="SecretoDeSesionIlegibleException">Si no se puede descifrar.</exception>
    string Descifrar(byte[] cifrado, short versionDeClave, PropositoDeSecreto proposito, string datosAsociados);
}

/// <summary>Puerto: bytes aleatorios criptográficos, para el valor de la cookie y la marca de dispositivo.</summary>
public interface IAleatoriosSeguros
{
    byte[] Bytes(int cantidad);
}

public abstract record ResultadoDeRenovacion
{
    /// <summary>Identidad rotó las credenciales: la nueva credencial y su vencimiento, y la nueva renovación.</summary>
    public sealed record Renovada(string Credencial, DateTimeOffset CredencialVenceEn, string Renovacion) : ResultadoDeRenovacion
    {
        public override string ToString() => "Renovada";
    }

    /// <summary>Identidad rechazó la renovación (usuario desactivado, renovación revocada, cambio de rol, clave comprometida): se cierra la sesión.</summary>
    public sealed record Rechazada : ResultadoDeRenovacion;

    /// <summary>Un fallo transitorio (tiempo agotado, error técnico): la sesión no se cierra y no se usa una credencial vencida.</summary>
    public sealed record Transitoria : ResultadoDeRenovacion;
}

/// <summary>Puerto hacia el contrato público de Identidad (<c>ISesiones</c>): renovar, revocar la renovación y cerrar todas las sesiones de un usuario.</summary>
public interface ISesionesDeIdentidad
{
    Task<ResultadoDeRenovacion> Renovar(string renovacion, CancellationToken cancelacion = default);

    /// <summary>Revoca la renovación; <see langword="true"/> solo si Identidad confirma la revocación.</summary>
    Task<bool> RevocarRenovacion(string renovacion, CancellationToken cancelacion = default);

    Task<bool> CerrarTodasLasSesiones(Guid usuarioId, CancellationToken cancelacion = default);
}

public enum EstadoDeVerificacion
{
    Valida,
    Vencida,

    /// <summary>La firma, el <c>kid</c> (revocado o desconocido) o la <c>aud</c> no son válidos.</summary>
    Invalida,

    /// <summary>El verificador no tiene claves vigentes (el JWKS no responde, IDE-61): no se cierra ninguna sesión.</summary>
    SinClavesVigentes,
}

public sealed record VerificacionDeCredencial(EstadoDeVerificacion Estado, ActorVerificado? Actor);

/// <summary>Puerto: la misma verificación de la credencial que hace la Api (firma, <c>kid</c>, <c>aud</c>, vencimiento y revocaciones).</summary>
public interface IVerificadorDeCredencial
{
    VerificacionDeCredencial Verificar(string credencial, DateTimeOffset ahora);
}

public enum MotivoDeDenegacionWeb
{
    ParejaDeRolYHostInvalida,
    SesionDeOtroHost,
    SecretoIlegible,
    IdentificadorDeSesionCerrado,
}

/// <summary>Puerto hacia <c>Auditoría</c> (<c>IDenegacionDeAcceso</c>): un acceso denegado propio de la web, con un motivo fijo y sin datos de la sesión. <b>Nunca lanza</b>: un registro de seguridad no interrumpe un caso de uso (el adaptador captura sus fallos, AUD-12).</summary>
public interface IRegistroDeSeguridadDeLaWeb
{
    Task AccesoDenegado(MotivoDeDenegacionWeb motivo, Guid? usuarioId, CancellationToken cancelacion = default);
}
