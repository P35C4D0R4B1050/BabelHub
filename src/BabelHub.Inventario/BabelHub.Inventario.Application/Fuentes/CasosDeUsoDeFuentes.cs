namespace BabelHub.Inventario.Application.Fuentes;

// Entradas y salidas de los casos de uso del módulo Fuentes. Viven en la raíz del módulo (contrato público) para que la Api las use sin tocar su Interno; los casos de
// uso mismos son ICasoDeUso<TEntrada, TSalida> de Plataforma. Son modelos propios del módulo (ni DTOs de Shared ni tipos del Dominio). El contacto de notificación es un
// dato personal: ningún tipo que lo lleve lo imprime en su ToString, para que un registro de diagnóstico nunca lo vuelque (privacidad, plan §3).

/// <summary>Registra una fuente nueva con stock cero (FUE-01, FUE-06). Solo la administración central.</summary>
public sealed record RegistrarFuenteEntrada(string Codigo, string Nombre, string Region, string? ContactoDeNotificacion)
{
    public override string ToString() => $"RegistrarFuenteEntrada {{ Codigo = {Codigo} }}";
}

/// <summary>Edita nombre, región y contacto de una fuente; el código no cambia (FUE-16). Solo la administración central.</summary>
public sealed record EditarFuenteEntrada(string Codigo, string Nombre, string Region, string? ContactoDeNotificacion)
{
    public override string ToString() => $"EditarFuenteEntrada {{ Codigo = {Codigo} }}";
}

/// <summary>
/// Una fuente con sus totales de stock (FUE-11): físico, reservado y vendible sumados de todas sus ediciones. El contacto de notificación solo lo ve la
/// administración central y nunca se imprime.
/// </summary>
public sealed record DetalleDeFuente(
    Guid Id,
    string Codigo,
    string Nombre,
    string Region,
    string? ContactoDeNotificacion,
    bool EsRecepcion,
    int FisicoTotal,
    int ReservadoTotal,
    int VendibleTotal)
{
    public override string ToString() => $"DetalleDeFuente {{ Codigo = {Codigo}, EsRecepcion = {EsRecepcion} }}";
}

/// <summary>Cambia la fuente de recepción (FUE-19, FUE-20). Solo la administración central.</summary>
public sealed record DesignarFuenteDeRecepcionEntrada(string Codigo);

/// <summary>Define o cambia el plazo, en días, del traslado de una fuente a otra (FUE-12). Solo la administración central.</summary>
public sealed record RegistrarRutaEntrada(string CodigoDeOrigen, string CodigoDeDestino, int Dias);

/// <summary>La ruta guardada y el plazo que tenía antes, si ya existía (el anterior solo se guarda en la auditoría, FUE-14).</summary>
public sealed record RutaDefinida(string CodigoDeOrigen, string CodigoDeDestino, int Dias, int? DiasAnteriores);

/// <summary>Pregunta el plazo entre dos fuentes (FUE-13). La administración central y los encargados.</summary>
public sealed record ConsultarPlazoDeRutaEntrada(string CodigoDeOrigen, string CodigoDeDestino);

/// <summary>Los días de la ruta de origen a destino; <see cref="Dias"/> es nulo si la ruta no está definida (FUE-13): no impide vender.</summary>
public sealed record PlazoDeRutaConsultado(string CodigoDeOrigen, string CodigoDeDestino, int? Dias);

/// <summary>Las rutas definidas, ordenadas por origen y destino.</summary>
public sealed record ListarRutasEntrada;

/// <summary>Una ruta definida entre dos fuentes.</summary>
public sealed record RutaListada(string CodigoDeOrigen, string CodigoDeDestino, int Dias);

/// <summary>Registra o actualiza la ficha de una edición con su formato (FUE-07, D03): <c>PUT</c> idempotente que hace el Catálogo con el token de una administradora.</summary>
public sealed record RegistrarFichaDeEdicionEntrada(Guid EdicionId, FormatoDeEdicionRegistrado Formato);

/// <summary>Consulta la disponibilidad de una o varias ediciones (hasta 50; FUE-05, FUE-07, FUE-15). No lleva alcance por sucursal: solo el vendible.</summary>
public sealed record ConsultarDisponibilidadEntrada(IReadOnlyCollection<Guid> EdicionIds);

/// <summary>El vendible de una edición en una fuente; el físico y el reservado no salen de aquí (solo la sucursal dueña los ve, FUE-08).</summary>
public sealed record VendibleEnFuente(string CodigoDeFuente, string NombreDeFuente, int Vendible, EstadoDeExistencia Estado);

/// <summary>El vendible de la red y su desglose por fuente (las fuentes sin fila, en cero), sin cifras de físico ni de reservado.</summary>
public sealed record DisponibilidadPublicaDeEdicion(Guid EdicionId, int VendibleEnLaRed, IReadOnlyList<VendibleEnFuente> PorFuente);

/// <summary>
/// La respuesta de la consulta de disponibilidad: las ediciones impresas con su disponibilidad, las que el Inventario no conoce (sin ficha, FUE-15) y las que no
/// son impresas (FUE-07). La Api decide qué hace con cada lista: una sola edición desconocida es no encontrada; una digital, «no tiene stock físico».
/// </summary>
public sealed record DisponibilidadConsultada(
    IReadOnlyList<DisponibilidadPublicaDeEdicion> Ediciones,
    IReadOnlyList<Guid> NoRegistradas,
    IReadOnlyList<Guid> NoImpresas,
    DateTimeOffset ConsultadaEn);

/// <summary>Pide las sucursales (código, nombre y región): lo ve cualquier visitante.</summary>
public sealed record ListarSucursalesEntrada;

/// <summary>Una sucursal como la ve cualquier visitante: sin contacto ni cifras de stock.</summary>
public sealed record SucursalPublica(string Codigo, string Nombre, string Region);

/// <summary>Consulta una fuente con sus totales (FUE-11). Solo la administración central; ninguna operación de esta consulta escribe stock.</summary>
public sealed record ConsultarFuenteEntrada(string Codigo);

/// <summary>Lista las fuentes visibles: todas para la administración central y solo la suya para un encargado (FUE-08).</summary>
public sealed record ListarFuentesEntrada;

/// <summary>
/// Pide las existencias de una fuente (FUE-09): filtradas por las ediciones que el Catálogo resolvió (hasta 50) y por estado, paginadas de 1 a 100. La
/// administración central y el encargado de esa fuente.
/// </summary>
public sealed record ConsultarExistenciasDeSucursalEntrada(
    string CodigoDeFuente,
    IReadOnlyCollection<Guid>? EdicionIds,
    EstadoDeExistencia? Estado,
    int Pagina,
    int Tamano);

/// <summary>
/// Pide las unidades vendidas de los últimos 30 días (FUE-21): de una fuente (el encargado, la suya) o, sin código, de toda la red (solo la administración central).
/// </summary>
public sealed record ConsultarUnidadesVendidasEntrada(string? CodigoDeFuente);

/// <summary>Las unidades vendidas de una fuente en la ventana.</summary>
public sealed record UnidadesVendidasDeFuente(string CodigoDeFuente, int Unidades);

/// <summary>Las unidades vendidas en los últimos <see cref="Dias"/> días: el total y su desglose por fuente (las fuentes sin ventas, en cero).</summary>
public sealed record UnidadesVendidasConsultadas(int Dias, int Total, IReadOnlyList<UnidadesVendidasDeFuente> PorFuente);

/// <summary>Pide el número de contacto de notificación de una sucursal (solo el servicio de avisos o la administración central).</summary>
public sealed record ConsultarContactoDeNotificacionEntrada(string CodigoDeSucursal);

/// <summary>El contacto de notificación de una sucursal. Es un dato personal: no se imprime ni entra a registros.</summary>
public sealed record ContactoDeNotificacion(string CodigoDeSucursal, string Numero)
{
    public override string ToString() => $"ContactoDeNotificacion {{ CodigoDeSucursal = {CodigoDeSucursal} }}";
}
