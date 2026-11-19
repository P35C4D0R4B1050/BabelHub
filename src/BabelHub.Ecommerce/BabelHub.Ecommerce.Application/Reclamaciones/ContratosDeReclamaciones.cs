using System.Text.Json.Serialization;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Reclamaciones;

/// <summary>El estado de una hoja tal como lo ve quien llama al módulo (modelo propio: el contrato público no expone el Dominio).</summary>
public enum EstadoDeLaHoja
{
    Pendiente,
    Respondido,
    Descartado,
}

/// <summary>El estado del aviso por correo de una respuesta.</summary>
public enum AvisoDeLaRespuesta
{
    Pendiente,
    Enviado,
}

/// <summary>
/// Lo que el consumidor escribe en el formulario (REC-03), tal cual y sin validar, más la clave de un solo uso que emitió el servidor y el token de la verificación de persona (SEG-31). Lleva
/// datos personales: su texto nunca los repite.
/// </summary>
public sealed record RegistrarHojaEntrada(
    [property: JsonIgnore] string? Nombre,
    [property: JsonIgnore] string? Documento,
    [property: JsonIgnore] string? Domicilio,
    [property: JsonIgnore] string? Correo,
    [property: JsonIgnore] string? BienContratado,
    [property: JsonIgnore] decimal? MontoReclamado,
    [property: JsonIgnore] string? Tipo,
    [property: JsonIgnore] string? Detalle,
    [property: JsonIgnore] string? PedidoReferencia,
    [property: JsonIgnore] string? ClaveDelFormulario,
    [property: JsonIgnore] string? TokenDeVerificacion)
{
    public override string ToString() => "RegistrarHojaEntrada { (redactada) }";
}

/// <summary>La constancia de una hoja registrada (REC-05): número, fecha, plazo de respuesta y los datos que el consumidor acaba de escribir. Lleva datos personales: su texto nunca los repite.</summary>
public sealed record ConstanciaDeHoja(
    string Numero,
    DateTimeOffset FechaDeRegistro,
    string Tipo,
    string BienContratado,
    decimal MontoReclamado,
    [property: JsonIgnore] string Detalle,
    [property: JsonIgnore] string? PedidoReferencia,
    [property: JsonIgnore] string Nombre,
    [property: JsonIgnore] string Documento,
    [property: JsonIgnore] string Domicilio,
    [property: JsonIgnore] string Correo,
    int PlazoDeRespuestaEnDias,
    DateOnly FechaLimiteDeRespuesta)
{
    public override string ToString() => $"ConstanciaDeHoja {{ Numero = {Numero} }}";
}

/// <summary>Una fila del listado de la administración: sin ningún dato personal (REC-10).</summary>
public sealed record ResumenDeHoja(Guid Id, string Numero, DateTimeOffset FechaDeRegistro, string Tipo, EstadoDeLaHoja Estado, AvisoDeLaRespuesta? Aviso);

public sealed record PaginaDeHojas(IReadOnlyList<ResumenDeHoja> Hojas, int Pagina, int Tamano, int Total);

/// <summary>El filtro del listado: sin estado, pendientes y respondidas (las descartadas se ocultan); con <see cref="EstadoDeLaHoja.Descartado"/>, solo ellas (REC-09, REC-12).</summary>
public sealed record ListarHojasEntrada(EstadoDeLaHoja? Estado = null, int Pagina = 1, int Tamano = 20);

public sealed record ResponderHojaEntrada(Guid HojaId, [property: JsonIgnore] string? Respuesta)
{
    /// <summary>Sin la respuesta, que es texto libre de la administración dirigido a una persona.</summary>
    public override string ToString() => $"ResponderHojaEntrada {{ HojaId = {HojaId} }}";
}

/// <summary>Lo que devuelven responder, descartar y reintentar el aviso: solo el número, el estado y el aviso, nunca datos personales (REC-07, REC-12).</summary>
public sealed record ResultadoDeLaHoja(string Numero, EstadoDeLaHoja Estado, AvisoDeLaRespuesta? Aviso);

/// <summary>
/// Registrar una hoja de reclamación (REC-02). No exige sesión ni consulta a Identidad: el Libro sigue disponible aunque Identidad no responda. Repetir el envío con la misma clave del
/// formulario devuelve la misma constancia sin crear otra hoja; con otro contenido, un conflicto.
/// </summary>
public interface IReclamaciones
{
    Task<Resultado<ConstanciaDeHoja>> Registrar(RegistrarHojaEntrada entrada, CancellationToken cancelacion = default);
}

/// <summary>Las operaciones de la administración central sobre las hojas (REC-07, REC-08, REC-09, REC-12, REC-13, REC-16). Cualquier otro rol recibe «prohibido».</summary>
public interface IAdministracionDeReclamaciones
{
    Task<Resultado<PaginaDeHojas>> Listar(ListarHojasEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Abre una hoja con sus datos personales y <b>audita la consulta en la misma transacción</b>: si no se puede auditar, no se muestra (REC-15).</summary>
    Task<Resultado<DetalleDeHoja>> Obtener(Guid hojaId, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeLaHoja>> Responder(ResponderHojaEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeLaHoja>> Descartar(Guid hojaId, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeLaHoja>> ReintentarAviso(Guid hojaId, CancellationToken cancelacion = default);
}

/// <summary>
/// La hoja completa para la administración central (REC-15): <b>con los datos personales</b>. Por eso su texto y su serialización no los repiten (ningún registro, error ni auditoría los lleva, RNF07, RNF23); solo
/// la vista de la administración los lee de sus propiedades.
/// </summary>
public sealed record DetalleDeHoja(
    Guid Id,
    string Numero,
    DateTimeOffset FechaDeRegistro,
    string Tipo,
    EstadoDeLaHoja Estado,
    [property: JsonIgnore] string Nombre,
    [property: JsonIgnore] string Documento,
    [property: JsonIgnore] string Domicilio,
    [property: JsonIgnore] string Correo,
    [property: JsonIgnore] string BienContratado,
    decimal MontoReclamado,
    [property: JsonIgnore] string Detalle,
    string? PedidoReferencia,
    [property: JsonIgnore] string? Respuesta,
    DateTimeOffset? RespondidaEn,
    AvisoDeLaRespuesta? Aviso,
    bool DeCuentaCancelada,
    bool Anonimizada,
    DateOnly FechaLimiteDeRespuesta)
{
    public override string ToString() => $"DetalleDeHoja {{ Numero = {Numero}, Estado = {Estado} }}";
}

/// <summary>Una hoja de quien la escribió (REC-14): número, fecha, tipo, estado y respuesta, también las descartadas; sin documento ni domicilio. El texto de la respuesta no se repite al imprimirla.</summary>
public sealed record MiHoja(string Numero, DateTimeOffset FechaDeRegistro, string Tipo, EstadoDeLaHoja Estado, [property: JsonIgnore] string? Respuesta, DateTimeOffset? RespondidaEn)
{
    public override string ToString() => $"MiHoja {{ Numero = {Numero}, Estado = {Estado} }}";
}

public sealed record PaginaDeMisHojas(IReadOnlyList<MiHoja> Hojas, int Pagina, int Tamano, int Total);

public sealed record ListarMisHojasEntrada(int Pagina = 1, int Tamano = 20);

/// <summary>Las hojas del consumidor con una sesión de <b>correo verificado</b> igual al de contacto de la hoja, y solo si la hoja no es de una cuenta cancelada (REC-10, REC-14, REC-20). Cualquier otro caso es «no encontrada».</summary>
public interface IHojasDelConsumidor
{
    Task<Resultado<PaginaDeMisHojas>> Listar(ListarMisHojasEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Por el número con formato <c>AAAA-NNNNNN</c>.</summary>
    Task<Resultado<MiHoja>> Obtener(string? numero, CancellationToken cancelacion = default);
}
