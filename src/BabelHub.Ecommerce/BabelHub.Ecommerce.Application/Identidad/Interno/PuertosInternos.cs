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
