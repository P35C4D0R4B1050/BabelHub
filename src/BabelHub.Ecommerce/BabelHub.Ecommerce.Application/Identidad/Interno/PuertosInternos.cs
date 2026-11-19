using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La cuenta tal como la guarda y la lee el módulo: el usuario del Dominio con lo que lo acompaña en el esquema <c>identidad</c> (nombre, resumen de la contraseña, contraseña temporal, textos aceptados). Es mutable
/// porque el caso de uso la modifica y la guarda; que dos cambios simultáneos no se pisen lo garantiza el repositorio (bloqueo de la fila, D17).
/// </summary>
public sealed class CuentaDeIdentidad
{
    public required Usuario Usuario { get; set; }

    public required string Nombre { get; set; }

    /// <summary>El resumen Argon2id (PHC) de la contraseña; <see langword="null"/> si no hay (nunca la contraseña).</summary>
    public string? ResumenDeAcceso { get; set; }

    /// <summary>Si la contraseña es una temporal (IDE-23): hasta cuándo sirve; solo permite el desafío de cambio.</summary>
    public DateTimeOffset? ContrasenaTemporalHasta { get; set; }

    public bool DebeCambiarLaTemporal { get; set; }

    public required AceptacionDeTextos Aceptacion { get; set; }

    public required DateTimeOffset CreadaEn { get; init; }

    /// <summary>Si la cuenta ya se canceló (IDE-47): no ingresa ni se recupera.</summary>
    public DateTimeOffset? CanceladaEn { get; set; }

    /// <summary>El número de WhatsApp y el consentimiento para los avisos (IDE-11, IDE-31): el número solo lo ve su titular y nunca sale de Identidad.</summary>
    public ConsentimientoDeWhatsApp Whatsapp { get; set; } = ConsentimientoDeWhatsApp.SinNumero();
}

/// <summary>El repositorio de cuentas (EF Core, esquema <c>identidad</c>, T33): lo que lee un caso de uso, siempre dentro de la unidad de trabajo del caso de uso.</summary>
public interface IRepositorioDeCuentas
{
    Task<CuentaDeIdentidad?> PorCorreo(string correoNormalizado, CancellationToken cancelacion = default);

    /// <summary>La cuenta por su identificador; con <paramref name="paraActualizar"/> toma el bloqueo de la fila del usuario (<c>FOR UPDATE</c>), que serializa rotar y revocar renovaciones (D17).</summary>
    Task<CuentaDeIdentidad?> PorId(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default);

    /// <summary>Inserta la cuenta; <see langword="false"/> si el correo ya existe (índice único del correo normalizado): el registro simultáneo de dos personas deja una sola cuenta.</summary>
    Task<bool> Insertar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default);

    Task Guardar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default);

    /// <summary>Elimina una cuenta de cliente sin verificar que se reemplaza (IDE-84): no se hereda ningún dato.</summary>
    Task Eliminar(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>
    /// Cuántos administradores centrales activos hay. Se llama dentro de la transacción de la administración y <b>toma el bloqueo de sus filas</b> (<c>FOR UPDATE</c>): dos desactivaciones o degradaciones simultáneas
    /// no pueden dejar el sistema sin administrador (IDE-49, D17).
    /// </summary>
    Task<int> ContarAdministradoresActivos(CancellationToken cancelacion = default);

    /// <summary>Los identificadores de los usuarios activos con alguno de esos roles y no cancelados: los destinatarios de las alertas (administración central y, para algunos motivos, logística). Solo identificadores, nunca direcciones.</summary>
    Task<IReadOnlyList<Guid>> IdsActivosConRol(IReadOnlyCollection<RolDeUsuario> roles, CancellationToken cancelacion = default);

    /// <summary>El encargado activo de la sucursal, si lo hay (IDE-06); el índice único parcial de la base respalda la regla ante dos asignaciones simultáneas.</summary>
    Task<Domain.Identidad.EncargadoDeSucursal?> EncargadoActivoDe(Guid sucursal, CancellationToken cancelacion = default);
}

/// <summary>Argon2id (T31): el resumen y la verificación, con el semáforo global de concurrencia y el cálculo señuelo para cuentas inexistentes (D03, IDE-03).</summary>
public interface IHasherDeContrasenas
{
    Task<string> Resumir(Contrasena contrasena, CancellationToken cancelacion = default);

    /// <summary>
    /// Verifica la contraseña contra su resumen. Con <paramref name="resumen"/> <see langword="null"/> (cuenta inexistente o sin contraseña) hace igualmente un cálculo señuelo del mismo costo y devuelve
    /// falso: el tiempo no depende de que la cuenta exista.
    /// </summary>
    Task<bool> Verificar(string? resumen, Contrasena presentada, CancellationToken cancelacion = default);
}

/// <summary>
/// El cálculo de contraseñas está saturado (el semáforo global de Argon2id, D03): no se pudo empezar en el tiempo de espera. La Api lo responde con 503 y <c>Retry-After</c>, sin decir nada de la cuenta; nunca se cae a un
/// cálculo más barato.
/// </summary>
public sealed class CalculoDeContrasenasSaturadoException : Exception
{
    public CalculoDeContrasenasSaturadoException()
        : base("El cálculo de contraseñas está saturado.")
    {
    }

    public CalculoDeContrasenasSaturadoException(string message)
        : base(message)
    {
    }

    public CalculoDeContrasenasSaturadoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>La lista local de contraseñas comunes o filtradas (al menos 10 000, IDE-59), ya normalizadas y en minúsculas.</summary>
public interface IListaDeContrasenasComunes
{
    bool EsComun(string normalizada);
}

/// <summary>
/// Las huellas con clave (HMAC con pimienta del entorno, T31) de lo que no se guarda en claro: los códigos de un solo uso, los códigos de respaldo y los valores opacos de las renovaciones y los desafíos
/// (SHA-256). El propósito va dentro del HMAC, de modo que un código de verificación no sirve para recuperar el acceso.
/// </summary>
public interface IHuellasDeIdentidad
{
    string DeCodigo(Guid usuarioId, PropositoDeCodigo proposito, string codigo);

    string DeCodigoDeRespaldo(Guid usuarioId, string codigoCanonico);

    /// <summary>SHA-256 en 64 caracteres hexadecimales de un valor opaco (la renovación o el desafío).</summary>
    string DeValorOpaco(string valor);
}

/// <summary>Los aleatorios criptográficos del módulo: identificadores, valores opacos de 256 bits y la contraseña temporal (CSPRNG).</summary>
public interface IGeneradorDeSecretos
{
    Guid NuevoIdentificador();

    /// <summary>Un valor opaco de 256 bits (base64url sin relleno): la renovación y el desafío.</summary>
    string ValorOpaco();

    /// <summary>El secreto de un segundo factor nuevo: 20 bytes (160 bits) de un CSPRNG, más que los 128 bits que exige el Dominio.</summary>
    byte[] SecretoDelSegundoFactor();

    /// <summary>La contraseña temporal del personal (IDE-23): de al menos 20 caracteres, generada con un CSPRNG.</summary>
    string ContrasenaTemporal();
}

/// <summary>
/// Los códigos de un solo uso (<c>identidad.codigos_de_un_solo_uso</c>, T33). <see cref="Emitir"/> invalida los anteriores del mismo propósito y guarda el nuevo en una operación;
/// <see cref="Verificar"/> es <b>atómico</b>: cuenta el intento y consume el código con un <c>UPDATE</c> condicional <b>antes</b> de comparar, de modo que solicitudes paralelas no superan los 5 intentos ni consumen
/// dos veces el mismo código (IDE-19, AC-IDE-17).
/// </summary>
public interface ICodigosDeUnSoloUso
{
    Task Emitir(CodigoDeUnSoloUso codigo, CancellationToken cancelacion = default);

    Task<ResultadoDelCodigo> Verificar(Guid usuarioId, PropositoDeCodigo proposito, string huellaPresentada, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Invalida el código vigente del propósito (por ejemplo, al cambiar la contraseña): deja de servir aunque no venza.</summary>
    Task Invalidar(Guid usuarioId, PropositoDeCodigo proposito, CancellationToken cancelacion = default);
}

/// <summary>Las renovaciones (<c>identidad.renovaciones</c>, T33): se guardan por su huella SHA-256 y se rotan o revocan bajo el bloqueo de la fila del usuario (D17).</summary>
public interface IRepositorioDeRenovaciones
{
    Task Guardar(Renovacion renovacion, CancellationToken cancelacion = default);

    Task<Renovacion?> PorHuella(string huella, CancellationToken cancelacion = default);

    /// <summary>Marca la renovación como usada con un <c>UPDATE … WHERE usada_en IS NULL</c>: <see langword="false"/> si otra solicitud ya la usó (rotación única).</summary>
    Task<bool> MarcarUsada(string huella, DateTimeOffset ahora, CancellationToken cancelacion = default);

    Task RevocarFamilia(Guid usuarioId, Guid familia, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Revoca todas las renovaciones vigentes del usuario (cerrar todas, recuperación, cambio de contraseña).</summary>
    Task RevocarTodas(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Revoca TODAS las renovaciones vigentes de todos los usuarios: la clave de firma se declaró comprometida (IDE-61).</summary>
    Task RevocarLasDeTodos(DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Revoca las renovaciones vigentes salvo una familia (cambio de contraseña o del segundo factor: «las demás», IDE-44).</summary>
    Task RevocarLasDemas(Guid usuarioId, Guid familiaQueSeConserva, DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>Los desafíos de ingreso (<c>identidad.desafios_de_ingreso</c>): se guardan por la huella de su valor opaco.</summary>
public interface IRepositorioDeDesafios
{
    Task Guardar(DesafioDeIngreso desafio, string huella, CancellationToken cancelacion = default);

    Task<DesafioDeIngreso?> PorHuella(string huella, CancellationToken cancelacion = default);

    /// <summary>Lo marca usado con un <c>UPDATE … WHERE NOT usado</c>: <see langword="false"/> si otra solicitud ya lo consumió (se consume una sola vez).</summary>
    Task<bool> MarcarUsado(Guid desafioId, CancellationToken cancelacion = default);
}

/// <summary>El segundo factor de un usuario guardado: el Dominio y el secreto cifrado (AES-GCM con el usuario en el AAD, T31).</summary>
public sealed record SegundoFactorGuardado(SegundoFactor Factor, byte[] SecretoCifrado);

/// <summary>El almacén del segundo factor (<c>identidad.segundo_factor</c> y <c>codigos_de_respaldo</c>, T33): las actualizaciones condicionales atómicas que impiden aceptar dos veces un paso o un código (D04).</summary>
public interface ISegundoFactorAlmacen
{
    Task<SegundoFactorGuardado?> Obtener(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Guarda el secreto de un segundo factor <b>sin confirmar</b> (IDE-21); reemplaza el que estuviera sin confirmar.</summary>
    Task GuardarSinConfirmar(Guid usuarioId, byte[] secretoCifrado, CancellationToken cancelacion = default);

    /// <summary>Activa el segundo factor y guarda las huellas de los códigos de respaldo.</summary>
    Task Activar(Guid usuarioId, long pasoConfirmado, IReadOnlyList<string> huellasDeRespaldo, CancellationToken cancelacion = default);

    /// <summary>Acepta un paso TOTP con <c>UPDATE … SET ultimo_paso_usado = @paso WHERE ultimo_paso_usado IS NULL OR ultimo_paso_usado &lt; @paso</c>: una fila es aceptado; dos solicitudes simultáneas con el mismo código aceptan solo una.</summary>
    Task<bool> AceptarPaso(Guid usuarioId, long paso, CancellationToken cancelacion = default);

    /// <summary>Consume un código de respaldo con <c>UPDATE … WHERE usado_en IS NULL</c>: <see langword="false"/> si ya estaba usado.</summary>
    Task<bool> ConsumirCodigoDeRespaldo(Guid usuarioId, string huella, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Desactiva el segundo factor de un cliente (IDE-37): elimina el secreto y los códigos de respaldo.</summary>
    Task Desactivar(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Reemplaza los códigos de respaldo por otros 8 (IDE-52): los anteriores dejan de servir en la misma operación.</summary>
    Task ReemplazarCodigosDeRespaldo(Guid usuarioId, IReadOnlyList<string> huellasNuevas, CancellationToken cancelacion = default);
}

/// <summary>Los avisos de seguridad que se muestran en la cuenta (<c>identidad.avisos_de_cuenta</c>, IDE-53, IDE-76, IDE-79): sin datos sensibles.</summary>
public interface IAvisosDeCuentaAlmacen
{
    Task<IReadOnlyList<AvisoDeCuenta>> Listar(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Marca el aviso del usuario como visto con un <c>UPDATE</c> condicional: <see langword="false"/> si no existe o es de otro usuario.</summary>
    Task<bool> MarcarVisto(Guid usuarioId, Guid avisoId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Deja un aviso en la cuenta (sin datos sensibles: solo el tipo del catálogo y la fecha); se llama dentro de la transacción del hecho que lo causa.</summary>
    Task Agregar(Guid usuarioId, string tipo, DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>
/// Los dispositivos reconocidos de un usuario (<c>identidad.dispositivos</c>, IDE-55, D11): solo la huella SHA-256 de la marca y la región. <see cref="Guardar"/> inserta o actualiza por (usuario, huella) y
/// conserva como máximo <see cref="Maximo"/> por usuario, eliminando los más antiguos: iniciar sesión muchas veces sin la marca no hace crecer la tabla sin límite.
/// </summary>
public interface IRepositorioDeDispositivos
{
    public const int Maximo = 10;

    Task<IReadOnlyList<Dispositivo>> Listar(Guid usuarioId, CancellationToken cancelacion = default);

    Task Guardar(Dispositivo dispositivo, CancellationToken cancelacion = default);
}

/// <summary>Las solicitudes sobre datos personales (<c>identidad.solicitudes_de_datos</c>, IDE-12, IDE-26): solo identificador, tipo, estado, fechas y la respuesta o el motivo (IDE-69).</summary>
public interface IRepositorioDeSolicitudesDeDatos
{
    Task Guardar(SolicitudDeDatos solicitud, CancellationToken cancelacion = default);

    /// <summary>La solicitud por su identificador; con <paramref name="paraActualizar"/> toma el bloqueo de su fila: dos respuestas simultáneas no se pisan.</summary>
    Task<SolicitudDeDatos?> Obtener(Guid solicitudId, bool paraActualizar = false, CancellationToken cancelacion = default);

    Task<IReadOnlyList<SolicitudDeDatos>> DelUsuario(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Todas, para la lista de la administración; la ordenación por fecha límite la hace el Dominio.</summary>
    Task<IReadOnlyList<SolicitudDeDatos>> Todas(CancellationToken cancelacion = default);
}

/// <summary>Las solicitudes de cancelación de cuenta (<c>identidad.solicitudes_de_cancelacion</c>, IDE-27, IDE-68, IDE-81): como máximo una abierta (diferida o esperando la reconfirmación) por usuario.</summary>
public interface IRepositorioDeCancelaciones
{
    Task Guardar(SolicitudDeCancelacion solicitud, CancellationToken cancelacion = default);

    /// <summary>La solicitud abierta (diferida o esperando la reconfirmación) del usuario; con <paramref name="paraActualizar"/> toma el bloqueo de su fila.</summary>
    Task<SolicitudDeCancelacion?> AbiertaDe(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default);

    /// <summary>La más reciente del usuario, abierta o cerrada.</summary>
    Task<SolicitudDeCancelacion?> UltimaDe(Guid usuarioId, CancellationToken cancelacion = default);

    /// <summary>Las abiertas para la revisión periódica, con <c>FOR UPDATE SKIP LOCKED</c> en la base: dos revisiones simultáneas no procesan la misma.</summary>
    Task<IReadOnlyList<SolicitudDeCancelacion>> AbiertasParaRevisar(int limite, CancellationToken cancelacion = default);
}

/// <summary>
/// Quien ejecuta la cancelación de la cuenta en una sola transacción (T23, IDE-47, IDE-70, IDE-75): desactiva, revoca, cancela avisos, pide a los módulos anonimizar, desvincular o eliminar, audita y anonimiza. Se llama <b>dentro</b>
/// de la transacción que reconfirma o solicita; si algo falla lanza y todo se revierte.
/// </summary>
public interface IEjecutorDeLaCancelacion
{
    Task Ejecutar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default);
}

/// <summary>
/// La parte de la matriz de cancelación (IDE-47, IDE-69, IDE-89) que es del propio esquema <c>identidad</c>, aplicada bajo <c>SET LOCAL ROLE</c> de ese esquema y <b>dentro de la transacción de la cancelación</b>: elimina
/// el segundo factor y sus códigos de respaldo, los códigos de un solo uso, los desafíos, las renovaciones, los dispositivos y la región, los cambios de correo pendientes (con el correo nuevo), los avisos de la cuenta,
/// y deja en las solicitudes sobre datos personales (y en las de cancelación) solo identificador, tipo, estado y fechas. No toca el registro de la cuenta: eso lo anonimiza el caso de uso.
/// </summary>
public interface IPurgaDeIdentidad
{
    Task EliminarLosDatosDe(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>La baja de un titular que se entrega al Inventario y cuyo cierre se agrega al registro de supresiones (<c>identidad.bajas_titulares</c> y <c>cierres_pendientes</c>, IDE-64, IDE-75): solo el identificador opaco y fechas.</summary>
public sealed class BajaDeTitular(Guid usuarioId, DateTimeOffset creada, DateTimeOffset? entregadaEn = null, DateTimeOffset? cerradaEn = null)
{
    /// <summary>El identificador opaco: el identificador interno aleatorio de la cuenta, nunca derivado del correo.</summary>
    public Guid IdentificadorOpaco { get; } = usuarioId;

    public DateTimeOffset Creada { get; } = creada;

    public DateTimeOffset? EntregadaEn { get; set; } = entregadaEn;

    public DateTimeOffset? CerradaEn { get; set; } = cerradaEn;

    public bool Pendiente => EntregadaEn is null || CerradaEn is null;
}

/// <summary>Las bajas pendientes: se agregan en la misma transacción que la cancelación y se reintentan hasta completarse, sin deshacer nada.</summary>
public interface IRepositorioDeBajas
{
    Task Agregar(BajaDeTitular baja, CancellationToken cancelacion = default);

    /// <summary>Las que aún no se entregaron o no se cerraron. Entregar y cerrar son idempotentes, así que dos reintentos simultáneos (dos instancias, o el paso de restauración y el trabajo) a lo sumo repiten un paso sin efecto; el trabajo de fondo (T40) puede tomar cada baja con <c>FOR UPDATE SKIP LOCKED</c> en una transacción por baja.</summary>
    Task<IReadOnlyList<BajaDeTitular>> Pendientes(int limite, CancellationToken cancelacion = default);

    Task Guardar(BajaDeTitular baja, CancellationToken cancelacion = default);
}

/// <summary>
/// El juego de claves de firma (<c>identidad.claves_de_firma</c>, IDE-35, IDE-36, IDE-61): <see cref="Cargar"/> con <c>paraActualizar</c> toma el bloqueo del juego completo, de modo que dos rotaciones o declaraciones
/// simultáneas se serializan; el índice único parcial de «una sola clave activa» impide que una doble rotación deje a una clave fuera del JWKS. Guardar es una operación de la misma transacción.
/// </summary>
public interface IAlmacenDeClavesDeFirma
{
    Task<JuegoDeClavesDeFirma> Cargar(bool paraActualizar, CancellationToken cancelacion = default);

    Task Guardar(JuegoDeClavesDeFirma juego, CancellationToken cancelacion = default);

    /// <summary>Los parámetros públicos (JWK sin la privada) de cada <c>kid</c> pedido: lo que se publica para verificar.</summary>
    Task<IReadOnlyDictionary<string, string>> ParametrosPublicos(IReadOnlyCollection<string> kids, CancellationToken cancelacion = default);
}

/// <summary>
/// Genera un par de claves ES256 nuevo: guarda la <b>privada cifrada</b> con la clave del entorno (nunca sale de Identidad) y devuelve la clave del Dominio (con su <c>kid</c> aleatorio) y los parámetros públicos. Se llama
/// dentro de la transacción de la rotación: si esta se revierte, la privada generada queda sin uso.
/// </summary>
public interface IGeneradorDeClavesDeFirma
{
    Task<(ClaveDeFirma Clave, string ParametrosPublicos)> Generar(DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>El cambio de correo en curso de un usuario (<c>identidad.cambios_de_correo_pendientes</c>, IDE-53, IDE-54): como máximo uno por usuario; <see cref="Guardar"/> reemplaza el anterior.</summary>
public interface IRepositorioDeCambiosDeCorreo
{
    Task<CambioDeCorreoPendiente?> Obtener(Guid usuarioId, CancellationToken cancelacion = default);

    Task Guardar(CambioDeCorreoPendiente cambio, CancellationToken cancelacion = default);
}

/// <summary>El cifrado de los secretos del segundo factor (AES-GCM, nonce aleatorio, clave versionada por propósito, AAD ligado al usuario, D04).</summary>
public interface ICifradorDeSecretos
{
    byte[] Cifrar(ReadOnlySpan<byte> secreto, Guid usuarioId);

    byte[] Descifrar(ReadOnlySpan<byte> cifrado, Guid usuarioId);
}

/// <summary>Las versiones vigentes de los términos, la privacidad y el reembolso (<c>identidad.versiones_de_textos</c>, IDE-34, IDE-43, IDE-57).</summary>
public interface IVersionesDeTextosVigentes
{
    Task<VersionesDeTextos> Obtener(CancellationToken cancelacion = default);
}

/// <summary>La credencial de acceso emitida: el JWT y su vencimiento.</summary>
public sealed record CredencialEmitida(string Jwt, DateTimeOffset ExpiraEn)
{
    /// <summary>No imprime el JWT (AGENTS.md §5): un token no aparece en ningún registro.</summary>
    public override string ToString() => "[credencial]";
}

/// <summary>Lo que lleva la credencial de acceso (IDE-02): el usuario, su rol, su sucursal si es encargado y si su correo está verificado.</summary>
public sealed record DatosDeLaCredencial(Guid UsuarioId, RolDeUsuario Rol, Guid? Sucursal, bool CorreoVerificado);

/// <summary>El emisor de credenciales ES256 (T32): firma con la clave activa, con <c>aud</c> y <c>kid</c>, de 15 minutos. La clave privada nunca sale de él.</summary>
public interface IEmisorDeCredenciales
{
    Task<CredencialEmitida> Emitir(DatosDeLaCredencial datos, DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>Una solicitud de código ya aceptada, para el trabajo en segundo plano (IDE-65): lleva solo lo necesario y nunca el código ni el resultado de la búsqueda de la cuenta.</summary>
public sealed record SolicitudDeCodigoEnSegundoPlano(string CorreoNormalizado, PropositoDeCodigo Proposito);

/// <summary>
/// La cola en memoria, acotada y no persistente de los códigos (T34, IDE-65, D05): <see cref="Encolar"/> es síncrono y de tiempo constante; si el canal está lleno descarta en silencio y devuelve falso (la
/// respuesta es la misma y se publica una alerta técnica). Un código no se reintenta: vence a los 10 minutos.
/// </summary>
public interface IColaDeCodigos
{
    bool Encolar(SolicitudDeCodigoEnSegundoPlano solicitud);
}

/// <summary>La fila idéntica del camino síncrono de una solicitud de código (<c>identidad.solicitudes_de_codigo</c>): se inserta exista o no la cuenta, con la huella del correo; se purga a los 10 minutos.</summary>
public interface IRegistroDeSolicitudesDeCodigo
{
    Task Registrar(string huellaDelCorreo, PropositoDeCodigo proposito, DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>
/// Los identificadores opacos de las cuentas canceladas cuyos registros aislados aún no se anonimizaron (<c>identidad.por_anonimizar</c>, AUD-19, AUD-31): se llena al ejecutar la cancelación, en la misma transacción, y
/// la depuración los consume; tras restaurar un respaldo se vuelve a poblar desde las cuentas canceladas.
/// </summary>
public interface IPorAnonimizar
{
    /// <summary>Idempotente: agregar un identificador que ya está no cambia nada (la reaplicación tras restaurar lo repite).</summary>
    Task Agregar(Guid usuarioId, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default);
}
