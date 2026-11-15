using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

// Los puertos propios del módulo Catálogo (plan §2): lo externo que la Aplicación necesita y la Infraestructura del módulo implementa. Son internos al módulo (regla 13):
// ningún otro módulo los usa. El Catálogo nunca abre un HttpClient propio: su adaptador del Inventario delega en el cliente de Plataforma (D01).

// ---------- Inventario ----------

/// <summary>El stock vendible de una edición según el Inventario, con el instante de la consulta.</summary>
public sealed record DisponibilidadDelInventario(Guid EdicionId, int VendibleEnLaRed, IReadOnlyList<DisponibilidadPorFuente> PorFuente, DateTimeOffset ConsultadaEn);

/// <summary>La respuesta de la disponibilidad de varias ediciones: las que el Inventario conoce y las que no (<c>noRegistradas</c>, que el Catálogo muestra agotadas).</summary>
public sealed record RespuestaDeDisponibilidad(IReadOnlyList<DisponibilidadDelInventario> Ediciones, IReadOnlyList<Guid> NoRegistradas);

/// <summary>Lo que se pide al Inventario para un ingreso: la clave de idempotencia es el nonce del token firmado (formato del contrato) y la carga es la que se guardó.</summary>
public sealed record SolicitudDeIngresoAlInventario(Guid EdicionId, Guid ClaveDeIdempotencia, int Cantidad, string? Referencia, string? Nota);

public enum TipoDeRespuestaDeIngreso
{
    /// <summary>201 bien formado: el ingreso se aplicó y trae su identificador.</summary>
    Creado,

    /// <summary>El Inventario respondió que no (4xx, 409): <see cref="RespuestaDeIngreso.CodigoDeError"/> es el código del <c>problem+json</c> bien formado o nulo si la respuesta tuvo una forma inesperada.</summary>
    Rechazado,

    /// <summary>Tiempo agotado, corte, 5xx, 429, cortacircuitos abierto o un 201 mal formado: no se sabe si se aplicó.</summary>
    SinRespuesta,
}

public sealed record RespuestaDeIngreso(TipoDeRespuestaDeIngreso Tipo, Guid? IngresoId = null, string? CodigoDeError = null, int? EstadoHttp = null)
{
    public static RespuestaDeIngreso Creado(Guid ingresoId) => new(TipoDeRespuestaDeIngreso.Creado, ingresoId);

    public static RespuestaDeIngreso SinRespuesta { get; } = new(TipoDeRespuestaDeIngreso.SinRespuesta);

    public static RespuestaDeIngreso Rechazado(int estadoHttp, string? codigoDeError) => new(TipoDeRespuestaDeIngreso.Rechazado, null, codigoDeError, estadoHttp);
}

/// <summary>Un ingreso de mercadería que el Inventario conserva (con su fecha y su usuario).</summary>
public sealed record IngresoDelInventario(Guid IngresoId, Guid EdicionId, string CodigoDeFuente, int Cantidad, string? Referencia, string? Nota, Guid? UsuarioId, DateTimeOffset Instante);

/// <summary>
/// El Inventario visto por el Catálogo (D01): solo lo que necesita. Nunca lanza por una falla del Inventario: los resultados «sin respuesta» se expresan en el valor devuelto (un 429, un
/// tiempo agotado y un cortacircuitos abierto son lo mismo) y el alta nunca se revierte por ellos (CAT-20). Las credenciales y los tokens no pasan por aquí: el cliente de <c>Plataforma</c> los toma
/// por dentro.
/// </summary>
public interface IInventarioDelCatalogo
{
    /// <summary><c>inventario.ediciones.registrar</c> (solo impresas, con el token de la administración, <c>PUT</c> idempotente). <see langword="true"/> si el Inventario confirmó la ficha.</summary>
    Task<bool> RegistrarEdicionImpresa(Guid edicionId, CancellationToken cancelacion = default);

    /// <summary><c>disponibilidad.consultar-varias</c> de hasta 50 ediciones impresas; <see langword="null"/> si el Inventario no respondió o respondió algo inválido.</summary>
    Task<RespuestaDeDisponibilidad?> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary><c>ingresos.registrar</c> con <c>Idempotency-Key</c> (el nonce del token firmado).</summary>
    Task<RespuestaDeIngreso> RegistrarIngreso(SolicitudDeIngresoAlInventario solicitud, CancellationToken cancelacion = default);

    /// <summary><c>ingresos.listar</c> de una edición (alcance de usuario con token); <see langword="null"/> si no respondió. Solo trae ingresos de la edición pedida.</summary>
    Task<IReadOnlyList<IngresoDelInventario>?> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default);
}

// ---------- Memoria de disponibilidad ----------

/// <summary>El último dato de disponibilidad de una edición con su instante. <see cref="Invalidada"/> (al registrar un ingreso) exige volver a consultar pero conserva el dato como respaldo.</summary>
public sealed record EntradaDeDisponibilidad(
    Guid EdicionId,
    int VendibleEnLaRed,
    IReadOnlyList<DisponibilidadPorFuente> PorFuente,
    DateTimeOffset ConsultadaEn,
    bool Invalidada = false);

/// <summary>
/// La memoria de disponibilidad (D03): caché de 30 segundos y respaldo de hasta 10 minutos si el Inventario falla. Por edición y por instancia; con un tope de entradas. Un lote fallido, un
/// 429 o <c>INV-LIMITE-EXCEDIDO</c> no invalidan lo guardado. Un dato nunca se guarda sin validar sus cantidades.
/// </summary>
public interface IMemoriaDeDisponibilidad
{
    EntradaDeDisponibilidad? Obtener(Guid edicionId);

    void Guardar(EntradaDeDisponibilidad entrada);

    /// <summary>Marca el dato como vencido para la caché (se vuelve a consultar) sin descartar el respaldo.</summary>
    void Invalidar(Guid edicionId);
}

// ---------- Claves de ingreso ----------

/// <summary>El token firmado que se muestra con el formulario de ingreso: su nonce de 128 bits es la clave de idempotencia; el token mismo nunca se guarda ni se registra.</summary>
public sealed record TokenDeIngresoEmitido(string Token, Guid Nonce, DateTimeOffset VenceEn)
{
    public override string ToString() => $"TokenDeIngresoEmitido {{ VenceEn = {VenceEn:O} }}";
}

/// <summary>
/// Emite y verifica el token de ingreso (D04): nonce de 128 bits, usuario, edición y 30 minutos de vigencia, firmado con la clave propia del entorno (y la anterior para la rotación), con
/// comparación en tiempo constante. <see cref="Verificar"/> devuelve el nonce si el token es auténtico, del mismo usuario y edición y no venció según el reloj; en cualquier otro caso
/// <see langword="null"/>, sin distinguir la causa.
/// </summary>
public interface IEmisorDeClavesDeIngreso
{
    TokenDeIngresoEmitido Emitir(Guid usuarioId, Guid edicionId, DateTimeOffset ahora);

    Guid? Verificar(string? token, Guid usuarioId, Guid edicionId, DateTimeOffset ahora);
}

// ---------- Almacén de objetos ----------

/// <summary>El almacén de objetos no responde: la subida se rechaza (CAT-16) y un trabajo de fondo lo reintenta más tarde. Nunca lleva datos del archivo.</summary>
public sealed class AlmacenNoDisponibleException : Exception
{
    public AlmacenNoDisponibleException()
        : base("El almacén de archivos no responde.")
    {
    }

    public AlmacenNoDisponibleException(string message)
        : base(message)
    {
    }

    public AlmacenNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public enum MotivoDeRechazoDeArchivo
{
    /// <summary>Los primeros bytes no son los de un tipo admitido para la finalidad (un ejecutable renombrado, una portada que es un PDF).</summary>
    TipoNoValido,

    /// <summary>Los bytes realmente leídos superan el tope (50 MiB un libro, 2 MiB una portada).</summary>
    DemasiadoGrande,

    /// <summary>Una imagen con más de 4 096 píxeles por lado o de 16 millones de píxeles.</summary>
    DimensionesInvalidas,

    /// <summary>La estructura no es la de un EPUB o un PDF (ZIP truncado, sin <c>mimetype</c>, tamaños incoherentes, sin <c>%%EOF</c>).</summary>
    EstructuraInvalida,
}

/// <summary>El archivo escrito y verificado: su tipo, el tamaño real y su huella SHA-256 calculada durante la escritura.</summary>
public sealed record ArchivoEscrito(TipoDeArchivo Tipo, long TamanoEnBytes, HuellaDeArchivo Huella);

/// <summary>El resultado de escribir un archivo: el archivo escrito o el motivo del rechazo (en cuyo caso el almacén ya no conserva el objeto).</summary>
public sealed record ResultadoDeEscrituraEnAlmacen(ArchivoEscrito? Archivo, MotivoDeRechazoDeArchivo? Rechazo)
{
    public bool Escrito => Archivo is not null;

    public static ResultadoDeEscrituraEnAlmacen Correcto(ArchivoEscrito archivo) => new(archivo, null);

    public static ResultadoDeEscrituraEnAlmacen Rechazado(MotivoDeRechazoDeArchivo motivo) => new(null, motivo);
}

/// <summary>
/// El almacén de objetos privado (S3): no es un contrato público; la biblioteca define su propio puerto de entrega con URL firmada (D13). <b>Nada pasa por el disco local ni se carga completo en
/// memoria</b> (regla 11, D14): se escribe en streaming y se verifica por lecturas por rango.
/// </summary>
public interface IAlmacenDeArchivos
{
    /// <summary>
    /// Escribe el flujo bajo la clave aleatoria: verifica los primeros bytes <b>antes</b> de escribir, cuenta el tope con los bytes realmente leídos, calcula la huella durante la escritura y
    /// verifica después la estructura por lecturas por rango. Si rechaza, borra lo escrito. Lanza <see cref="AlmacenNoDisponibleException"/> si el almacén no responde.
    /// </summary>
    Task<ResultadoDeEscrituraEnAlmacen> Escribir(string claveEnAlmacen, FinalidadDeArchivo finalidad, Stream contenido, CancellationToken cancelacion = default);

    /// <summary>Lee hasta <paramref name="destino"/>.Length bytes desde <paramref name="desde"/> (lectura por rango); devuelve cuántos leyó.</summary>
    Task<int> LeerRango(string claveEnAlmacen, long desde, Memory<byte> destino, CancellationToken cancelacion = default);

    /// <summary>Abre una portada para servirla; <see langword="null"/> si el objeto no existe.</summary>
    Task<Stream?> AbrirPortada(string claveEnAlmacen, CancellationToken cancelacion = default);

    /// <summary>Borra el objeto; borrar uno que no existe no es un error.</summary>
    Task Borrar(string claveEnAlmacen, CancellationToken cancelacion = default);
}
