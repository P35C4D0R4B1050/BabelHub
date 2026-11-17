namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Cómo terminó una llamada al Inventario.</summary>
public enum EstadoDeLlamadaAlInventario
{
    /// <summary>Respondió con el éxito del contrato y, si lleva cuerpo, con un valor válido.</summary>
    Exitosa,

    /// <summary>Respondió que no (400, 401, 403, 404, 409 o 422): <see cref="ResultadoDelInventario.Codigo"/> trae el <c>INV-…</c> estable. No es una falla del servicio.</summary>
    Rechazada,

    /// <summary>Respondió 429 (<c>INV-LIMITE-EXCEDIDO</c>): quien llama lo trata como «sin respuesta» y usa lo último que sabía.</summary>
    LimiteExcedido,

    /// <summary>No respondió a tiempo, cortó, el cortacircuitos estaba abierto, respondió 5xx o con un cuerpo que el contrato no admite, o no había sesión para una operación de usuario.</summary>
    NoDisponible,
}

/// <summary>
/// Resultado de una llamada al Inventario sin valor de retorno (204). <b>Una falla del Inventario nunca es una excepción</b>: es un estado. Nunca lleva el
/// texto del error remoto, el cuerpo de la respuesta ni el token (SEG-10).
/// </summary>
public record ResultadoDelInventario
{
    protected ResultadoDelInventario(EstadoDeLlamadaAlInventario estado, string? codigo, int? estadoHttp)
    {
        Estado = estado;
        Codigo = codigo;
        EstadoHttp = estadoHttp;
    }

    public EstadoDeLlamadaAlInventario Estado { get; }

    public bool Exitosa => Estado == EstadoDeLlamadaAlInventario.Exitosa;

    /// <summary>El <c>code</c> estable del problema (<c>INV-NO-ENCONTRADO</c>…); <see langword="null"/> si no hubo uno válido.</summary>
    public string? Codigo { get; }

    /// <summary>El estado HTTP de una respuesta rechazada o limitada; <see langword="null"/> si no hubo respuesta.</summary>
    public int? EstadoHttp { get; }

    public static ResultadoDelInventario Correcta { get; } = new(EstadoDeLlamadaAlInventario.Exitosa, null, null);

    public static ResultadoDelInventario Rechazo(int estadoHttp, string? codigo) => new(EstadoDeLlamadaAlInventario.Rechazada, codigo, estadoHttp);

    public static ResultadoDelInventario Limitada(string? codigo) => new(EstadoDeLlamadaAlInventario.LimiteExcedido, codigo, 429);

    public static ResultadoDelInventario SinRespuesta { get; } = new(EstadoDeLlamadaAlInventario.NoDisponible, null, null);
}

/// <summary>El resultado de una llamada al Inventario con valor de retorno; pedir el valor sin éxito es un error de programación.</summary>
public sealed record ResultadoDelInventario<T> : ResultadoDelInventario
{
    private readonly T? _valor;

    private ResultadoDelInventario(EstadoDeLlamadaAlInventario estado, string? codigo, int? estadoHttp, T? valor)
        : base(estado, codigo, estadoHttp)
    {
        _valor = valor;
    }

    public T Valor => Exitosa ? _valor! : throw new InvalidOperationException("El Inventario no respondió con un valor y no hay valor.");

    public static ResultadoDelInventario<T> ConValor(T valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return new ResultadoDelInventario<T>(EstadoDeLlamadaAlInventario.Exitosa, null, null, valor);
    }

    public static new ResultadoDelInventario<T> Rechazo(int estadoHttp, string? codigo) => new(EstadoDeLlamadaAlInventario.Rechazada, codigo, estadoHttp, default);

    public static new ResultadoDelInventario<T> Limitada(string? codigo) => new(EstadoDeLlamadaAlInventario.LimiteExcedido, codigo, 429, default);

    public static new ResultadoDelInventario<T> SinRespuesta { get; } = new(EstadoDeLlamadaAlInventario.NoDisponible, null, null, default);
}

/// <summary>Una edición que se da de alta en el Inventario con su formato (<c>impreso</c> o <c>digital</c>; solo las impresas tienen stock).</summary>
public sealed record EdicionARegistrar(Guid EdicionId, string Formato);

/// <summary>Lo vendible de una edición en una fuente; el físico y el reservado no salen del Inventario (FUE-08).</summary>
public sealed record FuenteConVendible(string CodigoDeFuente, string Nombre, int Vendible, string EstadoDeStock);

/// <summary>La disponibilidad consolidada de una edición y su desglose por fuente.</summary>
public sealed record DisponibilidadDeLaEdicion(Guid EdicionId, int VendibleEnLaRed, IReadOnlyList<FuenteConVendible> Fuentes, DateTimeOffset ConsultadaEn);

/// <summary>La disponibilidad de varias ediciones; las que el Inventario no conoce van aparte, sin fallar toda la consulta.</summary>
public sealed record DisponibilidadDeVariasEdiciones(IReadOnlyList<DisponibilidadDeLaEdicion> Ediciones, IReadOnlyList<Guid> NoRegistradas);

/// <summary>Un ingreso de mercadería que se pide registrar: sin fuente va a la de recepción; la cantidad es un entero entre 1 y 5 000.</summary>
public sealed record IngresoARegistrar(Guid EdicionId, string? CodigoDeFuente, int Cantidad, string? Referencia, string? Nota);

/// <summary>Un ingreso de mercadería registrado, con su fecha y el usuario que lo hizo.</summary>
public sealed record IngresoRegistrado(Guid IngresoId, Guid EdicionId, string CodigoDeFuente, int Cantidad, string? Referencia, string? Nota, Guid? UsuarioId, DateTimeOffset Instante);

/// <summary>Una sucursal tal como la ve quien elige un recojo o una sucursal preferida.</summary>
public sealed record SucursalPublica(string Codigo, string Nombre, string Region);

/// <summary>
/// El puerto con el que el E-commerce llama al Inventario por REST (contratos v1, <c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c>): vive en el contrato público de
/// <c>Plataforma</c> con modelos propios (ningún DTO de <c>Shared</c> ni tipo del Dominio) y lo consumen los módulos mediante un puerto fino propio, sin abrir un <c>HttpClient</c>
/// (decisión D01 de <c>ecommerce-catalogo</c>, regla 13). La Infraestructura lo implementa con plazo de 10 segundos, reintentos solo de lo idempotente, cortacircuitos, la credencial
/// del alcance de cada operación y el token del usuario en las operaciones de usuario (nunca una cabecera de actor). <b>Una falla del Inventario nunca es una excepción</b>: es un
/// <see cref="EstadoDeLlamadaAlInventario"/>. Las operaciones de pendientes y bajas de un titular las agrega <c>identidad-seguridad</c> (T12 y T35) en este mismo puerto.
/// </summary>
public interface IClienteDelInventario
{
    /// <summary>
    /// <c>inventario.ediciones.registrar</c>: da de alta (o confirma) una edición con su formato. Es una operación de la administración: lleva el token del usuario de la sesión. <c>PUT</c> idempotente (204).
    /// Un formato distinto del ya registrado se rechaza con <c>INV-FORMATO-EN-CONFLICTO</c>.
    /// </summary>
    Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.disponibilidad.consultar-varias</c>: la disponibilidad de hasta 50 ediciones (alcance de lectura, sin token). Reenvía la dirección del cliente final con la credencial
    /// <c>origen-reenviado</c> si la solicitud en curso la tiene (SEG-17). Una lectura: se puede reintentar.
    /// </summary>
    Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>
    /// <c>inventario.ingresos.registrar</c>: registra un ingreso de mercadería (token de la administración central). La <paramref name="claveDeIdempotencia"/> (16 a 80 caracteres
    /// <c>[A-Za-z0-9_-]</c>) hace seguro el reintento: repetirla con la misma carga devuelve el ingreso original, con otra, <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>.
    /// </summary>
    Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary><c>inventario.ingresos.listar</c>: los últimos ingresos de una edición, del más reciente al más antiguo (hasta <paramref name="limite"/>, de 1 a 50; token del usuario, no es una lectura de servicio).</summary>
    Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default);

    /// <summary><c>inventario.sucursales.listar</c>: las sucursales para elegir recojo o sucursal preferida (alcance de lectura, sin token).</summary>
    Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default);
}
