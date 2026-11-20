using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web;

/// <summary>El host que sirve la interfaz: la tienda o la gestión del personal (AD15, D03). Tipo propio del contrato: la Api no ve el Dominio.</summary>
public enum HostWeb
{
    Tienda,
    Gestion,
}

/// <summary>El rol de quien mira la interfaz, tal como sale de la credencial verificada.</summary>
public enum RolWeb
{
    Visitante,
    Cliente,
    Encargado,
    Logistica,
    AdministradorCentral,
}

/// <summary>Las áreas de la interfaz (WEB-02).</summary>
public enum AreaWeb
{
    Tienda,
    Sucursal,
    Administracion,
}

/// <summary>Quién es la persona según la credencial <b>verificada</b> en esta solicitud (D04): nunca sale de columnas de la tabla.</summary>
public sealed record ActorVerificado(Guid UsuarioId, RolWeb Rol, Guid? SucursalId);

/// <summary>
/// Una credencial en memoria: el texto del JWT solo existe durante la solicitud (D02). No se serializa ni se imprime: <see cref="ToString"/> no la
/// devuelve y <see cref="Revelar"/> solo lo llama la implementación del token de la sesión, que lo entrega al manejador de salida hacia el Inventario.
/// </summary>
public sealed class CredencialEnMemoria
{
    private readonly string _valor;

    public CredencialEnMemoria(string valor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valor);
        _valor = valor;
    }

    /// <summary>El texto del JWT: solo lo lee <c>HttpTokenDelUsuarioDeLaSesion</c> (Api) para entregarlo como <see cref="ValorSecreto"/>. Es un método y no una propiedad para que ningún serializador lo emita.</summary>
    public string Revelar() => _valor;

    public override string ToString() => "[credencial]";
}

/// <summary>El resultado de un ingreso correcto de Identidad: quién es, con qué rol y las dos credenciales que se guardan (cifradas) en el servidor.</summary>
public sealed record IngresoCompletado(Guid UsuarioId, RolWeb Rol, string Credencial, DateTimeOffset CredencialVenceEn, string Renovacion)
{
    public override string ToString() => $"IngresoCompletado {{ UsuarioId = {UsuarioId}, Rol = {Rol} }}";
}

/// <summary>Lo que hace falta para abrir una sesión web tras un ingreso correcto (WEB-28).</summary>
public sealed record SolicitudDeInicioDeSesion(HostWeb Host, IngresoCompletado Ingreso, string? ValorDeLaCookieAnterior, bool TieneMarcaDeDispositivoValida)
{
    /// <summary>No imprime la cookie anterior (una sesión viva) ni las credenciales (CTL-21).</summary>
    public override string ToString() => $"SolicitudDeInicioDeSesion {{ Host = {Host} }}";
}

/// <summary>La sesión abierta: el valor de la cookie opaca (32 bytes aleatorios en base64url) y, si hay que emitirla, la marca de dispositivo nueva.</summary>
public sealed record SesionIniciada(string ValorDeLaCookie, string? MarcaDeDispositivoNueva)
{
    public override string ToString() => "SesionIniciada";
}

/// <summary>Quién origina la solicitud: solo una acción de la persona renueva la inactividad (WEB-27); una consulta automática nunca.</summary>
public enum OrigenDeLaAccion
{
    AccionDelUsuario,
    ConsultaAutomatica,
}

public enum EstadoDeCarga
{
    Valida,
    Inexistente,
    Cerrada,
    VencidaPorInactividad,
    VencidaPorTopeAbsoluto,
    OtroHost,
    ServicioNoDisponible,
}

/// <summary>El resultado de cargar la sesión de una cookie: su estado y, si es válida, de quién es.</summary>
public sealed record ResultadoDeCarga(EstadoDeCarga Estado, Guid? UsuarioId);

public enum EstadoDeCredencial
{
    /// <summary>La credencial es válida: el actor y el token están disponibles para esta solicitud.</summary>
    Asegurada,

    /// <summary>Identidad rechazó la renovación o la credencial no se pudo verificar tras renovarla: la sesión se cerró (y el borrador se conserva).</summary>
    SesionCerrada,

    /// <summary>Identidad o el verificador no responden: servicio no disponible, sin cerrar la sesión ni usar una credencial vencida.</summary>
    ServicioNoDisponible,

    /// <summary>La sesión no existe o ya no es utilizable.</summary>
    SinSesion,
}

/// <summary>El resultado de asegurar la credencial de una sesión: el actor verificado y el token en memoria, o por qué no hay.</summary>
public sealed record ResultadoDeCredencial(EstadoDeCredencial Estado, ActorVerificado? Actor, CredencialEnMemoria? Credencial);

/// <summary>
/// Contrato público de la sesión web (WEB-04, WEB-27, WEB-28, WEB-31, WEB-32): la cookie lleva solo un identificador opaco y todo lo demás vive en el
/// servidor. La credencial del usuario nunca llega al navegador.
/// </summary>
public interface ISesionesWeb
{
    /// <summary>
    /// Abre una sesión con un identificador nuevo tras un ingreso correcto; cierra y revoca la que presentó la cookie. Rechaza la pareja rol y host que no
    /// corresponde con el mismo error que una credencial inválida y revoca la renovación emitida.
    /// </summary>
    Task<Resultado<SesionIniciada>> Iniciar(SolicitudDeInicioDeSesion solicitud, CancellationToken cancelacion = default);

    /// <summary>Carga la sesión de la cookie: vencimiento por inactividad o tope absoluto, host, y la actividad (solo si es una acción de la persona).</summary>
    Task<ResultadoDeCarga> Cargar(string? valorDeLaCookie, HostWeb host, OrigenDeLaAccion origen, CancellationToken cancelacion = default);

    /// <summary>Asegura una credencial vigente: la renueva si le quedan menos de 60 segundos y la verifica (firma, <c>kid</c>, <c>aud</c> y revocaciones).</summary>
    Task<ResultadoDeCredencial> AsegurarCredencial(string valorDeLaCookie, HostWeb host, CancellationToken cancelacion = default);

    /// <summary>Cierre voluntario: invalida la sesión en el servidor aunque la revocación de la renovación quede pendiente.</summary>
    Task Cerrar(string valorDeLaCookie, CancellationToken cancelacion = default);

    /// <summary>Cierra todas las sesiones web de un usuario y las renovaciones en Identidad, en una sola unidad de trabajo (motivo «voluntario»).</summary>
    Task CerrarTodasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>La renovación de la sesión abierta de esta cookie, solo en proceso, para que Identidad conserve la sesión actual al revocar las demás (cambio de contraseña o de segundo factor); <see langword="null"/> si no hay sesión abierta o no se puede leer.</summary>
    Task<CredencialEnMemoria?> RenovacionDeLaSesion(string valorDeLaCookie, HostWeb host, CancellationToken cancelacion = default);
}

/// <summary>Contrato público: qué áreas ve un rol y qué host sirve cada una (WEB-02, D03).</summary>
public interface IAreasDeInterfaz
{
    IReadOnlyList<AreaWeb> AreasDe(RolWeb rol);

    bool VeLaArea(RolWeb rol, AreaWeb area);

    HostWeb HostDeLaArea(AreaWeb area);

    /// <summary>Un cliente solo ingresa por la tienda y el personal solo por la gestión.</summary>
    bool ParejaDeRolYHostValida(RolWeb rol, HostWeb host);
}

/// <summary>Contrato público del borrador de pantalla (WEB-25, D06).</summary>
public interface IBorradoresDePantalla
{
    /// <summary>
    /// Conserva lo que la persona tenía en pantalla solo si su sesión <b>venció</b> o se le rechazó la renovación (nunca tras un cierre voluntario) y el
    /// token antifalsificación es válido para esa sesión. Devuelve si se guardó.
    /// </summary>
    Task<bool> Conservar(ConservarBorrador entrada, CancellationToken cancelacion = default);

    /// <summary>Entrega lo conservado una sola vez y solo al mismo usuario; lo borra al entregarlo.</summary>
    Task<IReadOnlyDictionary<string, string>?> Recuperar(Guid usuarioId, string plantillaDeRuta, CancellationToken cancelacion = default);
}

/// <summary>
/// Lo que se conserva: la cookie de la sesión vencida, la plantilla de la ruta (nunca la dirección), los campos marcados y <b>cómo se comprueba el token antifalsificación</b>.
/// El token solo puede validarse contra el usuario de la fila retenida (el módulo la carga por el identificador de la cookie): <paramref name="ValidarElToken"/> recibe ese
/// usuario y devuelve si el token de la solicitud es válido para él y para esa sesión. No se guarda nada si no se invoca o si devuelve <see langword="false"/> (D06).
/// </summary>
public sealed record ConservarBorrador(string ValorDeLaCookie, string PlantillaDeRuta, IReadOnlyDictionary<string, string> Campos, Func<Guid, CancellationToken, Task<bool>> ValidarElToken)
{
    public override string ToString() => $"ConservarBorrador {{ PlantillaDeRuta = {PlantillaDeRuta} }}";
}
