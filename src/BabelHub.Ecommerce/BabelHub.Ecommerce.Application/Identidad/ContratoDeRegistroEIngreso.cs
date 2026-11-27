using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad;

/// <summary>Lo que la política de contraseñas exige (IDE-15), para que la interfaz lo diga sin repetir las cifras. La política completa (normalización y lista de contraseñas comunes) la aplica el servidor al guardar.</summary>
public static class ReglasDeContrasena
{
    public const int LongitudMinima = PoliticaDeContrasenas.LongitudMinima;

    public const int LongitudMaxima = PoliticaDeContrasenas.LongitudMaxima;
}

/// <summary>
/// Registro y verificación del correo (IDE-01, IDE-13, IDE-16, IDE-34, IDE-39, IDE-56, IDE-84, IDE-88). Las respuestas no revelan si el correo ya existe ni nada de la cuenta existente; los fallos son categorías
/// (<see cref="ErrorDeAplicacion"/>) y los de verificación usan un mensaje genérico (IDE-19).
/// </summary>
public interface IRegistroDeCuentas
{
    /// <summary>
    /// Crea una cuenta de cliente con el correo sin verificar y envía el código de verificación (en segundo plano). Si el correo ya existe, lo rechaza con el mismo fallo <c>correo-en-uso</c> sin datos de la cuenta (IDE-13), salvo el reemplazo de una cuenta de cliente sin verificar y sin pedidos ni
    /// reservas (IDE-84), que se hace en una transacción y, si cualquiera de sus puertos falla, se rechaza de forma indistinguible de IDE-13 (IDE-88).
    /// </summary>
    Task<Resultado<RespuestaUniforme>> Registrar(SolicitudDeAltaDeCuenta solicitud, CancellationToken cancelacion = default);

    /// <summary>
    /// Marca el correo como verificado si el código es el correcto y vigente y emite la credencial con ese dato (IDE-16). <b>Si la cuenta tiene el segundo factor activo</b> (IDE-37 no exige el correo verificado) verifica el correo pero
    /// <b>no emite sesión</b>: el resultado no trae credencial y la persona debe ingresar con su contraseña y su segundo factor (IDE-50). Cualquier fallo es genérico (IDE-19).
    /// </summary>
    Task<Resultado<ResultadoDelIngreso>> VerificarCorreo(string correo, ValorSecreto codigo, CancellationToken cancelacion = default);

    /// <summary>Envía un código nuevo al correo de una cuenta sin verificar, con el límite de SEG-19; responde lo mismo exista o no la cuenta (IDE-56, IDE-65).</summary>
    Task<Resultado<RespuestaUniforme>> ReenviarCodigo(string correo, CancellationToken cancelacion = default);
}

/// <summary>Ingreso en dos pasos (IDE-02, IDE-03, IDE-08, IDE-20 a IDE-23, IDE-42, IDE-50): la credencial solo sale cuando se cumplió todo lo que le toca a la persona.</summary>
public interface IIngreso
{
    /// <summary>
    /// Verifica el correo y la contraseña. Datos incorrectos, cuenta inexistente o desactivada, o un bloqueo temporal responden lo mismo (IDE-03, IDE-08). Con todo en orden y sin pasos pendientes, emite la
    /// credencial; si no, un desafío de 5 minutos con el paso que falta.
    /// </summary>
    Task<Resultado<ResultadoDelIngreso>> Ingresar(SolicitudDeIngreso solicitud, CancellationToken cancelacion = default);

    /// <summary>Presenta el código TOTP o un código de respaldo (una sola vez, IDE-22) con el desafío de un ingreso.</summary>
    Task<Resultado<ResultadoDelIngreso>> PresentarSegundoFactor(ValorSecreto desafio, ValorSecreto codigo, CancellationToken cancelacion = default);

    /// <summary>Empieza a configurar el segundo factor del desafío (IDE-21): entrega el secreto una sola vez; no se activa hasta confirmarlo con un código.</summary>
    Task<Resultado<SecretoDelSegundoFactor>> IniciarConfiguracionDelSegundoFactor(ValorSecreto desafio, CancellationToken cancelacion = default);

    /// <summary>Confirma el segundo factor con un código válido, entrega los 8 códigos de respaldo una sola vez y emite la credencial (IDE-21).</summary>
    Task<Resultado<ResultadoDelIngreso>> ConfirmarElSegundoFactor(ValorSecreto desafio, ValorSecreto codigo, CancellationToken cancelacion = default);

    /// <summary>Cambia la contraseña temporal (IDE-23): la única acción que permite ese desafío; sigue el segundo factor, si le toca, antes de emitir la credencial.</summary>
    Task<Resultado<ResultadoDelIngreso>> CambiarLaContrasenaTemporal(ValorSecreto desafio, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default);

    /// <summary>
    /// El paso que le falta a un desafío vigente (para que la pantalla de ingreso muestre el correcto), o <see langword="null"/> si no existe, venció o ya se usó. No consume nada ni revela nada del usuario.
    /// </summary>
    Task<PasoPendienteDelIngreso?> PasoDelDesafio(ValorSecreto desafio, CancellationToken cancelacion = default);

    /// <summary>Descarta un desafío vigente (quien cancela el ingreso): lo marca usado. Idempotente; un desafío que no existe o ya no sirve no es un error.</summary>
    Task CancelarElDesafio(ValorSecreto desafio, CancellationToken cancelacion = default);
}

/// <summary>Sesiones (IDE-08, IDE-14, IDE-43, IDE-44, IDE-46, IDE-60): renovar, cerrar y cerrar todas.</summary>
public interface ISesiones
{
    /// <summary>
    /// Rota la renovación y emite una credencial nueva con el rol, la sucursal y el correo verificado <b>actuales</b>. La rechaza si venció su vencimiento absoluto, si se revocó, si el usuario está desactivado o si se
    /// emitió antes del corte de sesiones; reutilizarla (ya rotada) revoca toda su familia, deja un evento de seguridad y una alerta. El rechazo se devuelve como valor.
    /// </summary>
    Task<Resultado<CredencialDeSesion>> Renovar(ValorSecreto renovacion, CancellationToken cancelacion = default);

    /// <summary>Cierra la sesión: la renovación deja de servir (IDE-14). Es idempotente y no revela si la renovación existía.</summary>
    Task<Resultado<RespuestaUniforme>> Cerrar(ValorSecreto renovacion, CancellationToken cancelacion = default);

    /// <summary>Revoca todas las renovaciones de la persona y avanza el corte de sesiones (IDE-60).</summary>
    Task<Resultado<RespuestaUniforme>> CerrarTodas(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>Recuperación del acceso (IDE-17 a IDE-19, IDE-29, IDE-45, IDE-65): respuesta y tiempo uniformes, y el código solo a un cliente con el correo verificado.</summary>
public interface IRecuperacionDeAcceso
{
    /// <summary>Responde lo mismo exista o no la cuenta, sea de personal o no esté verificado su correo; el código sale solo si es de un cliente con el correo verificado y siempre en segundo plano.</summary>
    Task<Resultado<RespuestaUniforme>> Solicitar(string correo, CancellationToken cancelacion = default);

    /// <summary>Con el código correcto y vigente define la contraseña nueva, invalida el código, revoca todas las renovaciones y avisa al correo sin la contraseña. Cualquier fallo es genérico (IDE-19).</summary>
    Task<Resultado<RespuestaUniforme>> Confirmar(string correo, ValorSecreto codigo, ValorSecreto contrasenaNueva, CancellationToken cancelacion = default);
}

/// <summary>El estado de una cuenta para quien decide qué permitir (Carrito, Pagos, la política de la web): sin datos personales y sin caché.</summary>
public interface IEstadoDeCuenta
{
    /// <summary>El estado de la cuenta; <see langword="null"/> si no existe. Falla cerrada para quien la consume: si Identidad no responde, nadie puede comprar.</summary>
    Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>
    /// Registra que la persona aceptó los textos <b>citando la versión que se le mostró</b>: solo se acepta si es la vigente (IDE-43, IDE-57). Devuelve <see cref="ResultadoDeLaAceptacion.VersionCambiada"/> si hay
    /// otra vigente, para que la web muestre la nueva.
    /// </summary>
    Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default);
}

/// <summary>Lo que respondió Identidad al registrar la aceptación de los textos (IDE-43, IDE-57).</summary>
public enum ResultadoDeLaAceptacion
{
    Aceptada,

    /// <summary>La versión mostrada ya no es la vigente: se debe mostrar la nueva.</summary>
    VersionCambiada,

    NoDisponible,
}
