using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>Una línea del pedido con lo que hace falta para tomar su cupo (DEV-03): su identificador, el pedido, lo comprado y las unidades que se piden.</summary>
internal sealed record CupoPedido(Guid LineaId, Guid PedidoId, int CantidadComprada, int Unidades);

/// <summary>Una página de devoluciones del Dominio con el total de las que cumplen el filtro.</summary>
internal sealed record PaginaDeDevolucionesDelDominio(IReadOnlyList<Devolucion> Devoluciones, int Total);

/// <summary>El filtro de la administración central: por tipo y por si es un reporte.</summary>
internal sealed record FiltroDeLaAdministracion(TipoDeDevolucion? Tipo, bool SoloReportes);

/// <summary>El almacén de Devoluciones no responde (o un bloqueo agotó su espera): los casos de uso lo traducen a «servicio no disponible» o a conflicto, nunca a un error 500.</summary>
internal sealed class DevolucionesNoDisponiblesException : Exception
{
    public DevolucionesNoDisponiblesException()
    {
    }

    public DevolucionesNoDisponiblesException(string mensaje)
        : base(mensaje)
    {
    }

    public DevolucionesNoDisponiblesException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}

/// <summary>
/// El repositorio del agregado <see cref="Devolucion"/> y de los cupos de línea (plan §3). Las escrituras y las lecturas con bloqueo van <b>dentro de la unidad de trabajo del llamador</b>; la Infraestructura abre su propio rol de esquema (<c>EjecutarComo(devoluciones)</c>) como Envíos y decide cómo lee fuera de una transacción (T22). <see cref="TomarCupos"/> y
/// <see cref="DevolverCupos"/> operan <b>en orden de <c>linea_id</c></b> (dos solicitudes con las mismas líneas no se interbloquean) con un <c>UPDATE</c> condicional: la atomicidad frente a solicitudes simultáneas es de la base (DEV-03).
/// No guarda la cuenta: Devoluciones localiza lo de una cuenta por el pedido (D01).
/// </summary>
internal interface IRepositorioDeDevoluciones
{
    /// <summary>La devolución con sus líneas y su historial; con <paramref name="paraActualizar"/> bloquea su fila (<c>FOR UPDATE</c> con <c>lock_timeout</c>).</summary>
    Task<Devolucion?> Obtener(Guid devolucionId, bool paraActualizar, CancellationToken cancelacion = default);

    Task Agregar(Devolucion devolucion, CancellationToken cancelacion = default);

    /// <summary>Guarda los cambios de la devolución y de sus líneas (estado, historial, marcadores y reembolso).</summary>
    Task Guardar(Devolucion devolucion, CancellationToken cancelacion = default);

    /// <summary>
    /// Compromete las unidades de cada línea en orden de <c>linea_id</c>, creando la fila del cupo desde lo comprado si no existe (<c>INSERT … ON CONFLICT DO NOTHING</c>). Devuelve <see langword="null"/> si todas alcanzaron o el
    /// identificador de la <b>primera línea que no alcanzó</b> (el llamador revierte su transacción: nada queda comprometido).
    /// </summary>
    Task<Guid?> TomarCupos(IReadOnlyList<CupoPedido> cupos, CancellationToken cancelacion = default);

    /// <summary>Devuelve las unidades de líneas rechazadas, también en orden de <c>linea_id</c> (DEV-18).</summary>
    Task DevolverCupos(IReadOnlyList<(Guid LineaId, int Unidades)> cupos, CancellationToken cancelacion = default);

    /// <summary>Las devoluciones de los pedidos dados, de la más reciente a la más antigua (las de una cuenta: sus pedidos salen del contrato de lectura de Pedidos).</summary>
    Task<PaginaDeDevolucionesDelDominio> ListarDePedidos(IReadOnlyCollection<Guid> pedidoIds, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Solo las de <b>impresos</b> con esa sucursal receptora (un encargado nunca ve un reporte ni un digital, DEV-28).</summary>
    Task<PaginaDeDevolucionesDelDominio> ListarDeLaSucursal(string codigoDeSucursal, int pagina, int tamano, CancellationToken cancelacion = default);

    Task<PaginaDeDevolucionesDelDominio> ListarDeLaAdministracion(FiltroDeLaAdministracion filtro, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Las líneas con el reembolso pendiente (recibida de un impreso o aprobada de un digital), de las más antiguas a las más recientes, hasta <paramref name="maximo"/>: la conciliación de reembolsos (T11).</summary>
    Task<IReadOnlyList<(Guid DevolucionId, Guid LineaDePedidoId)>> ObtenerConReembolsoPendiente(int maximo, CancellationToken cancelacion = default);
}

/// <summary>El resultado de pedir el reingreso de una devolución al Inventario, reducido a la lista cerrada que el orquestador necesita (D07).</summary>
internal abstract record ResultadoDelReingreso
{
    /// <summary>El Inventario sumó las unidades a la sucursal receptora (o respondió <c>yaAplicado</c>: un hecho que se acepta).</summary>
    public sealed record Reingresado : ResultadoDelReingreso;

    /// <summary>Un rechazo de negocio de la lista cerrada (<c>INV-NO-ENCONTRADO</c>, <c>INV-EDICION-NO-IMPRESA</c>, <c>INV-VALIDACION-INVALIDA</c>): garantiza que no se reingresó nada.</summary>
    public sealed record RechazoDeNegocio : ResultadoDelReingreso;

    /// <summary><c>INV-IDEMPOTENCIA-EN-CONFLICTO</c>: ya hubo un reingreso de esta devolución con otra carga (AJU-16): no limpia el marcador, alerta y se resuelve a mano.</summary>
    public sealed record ConflictoDeIdempotencia : ResultadoDelReingreso;

    /// <summary>Sin respuesta, plazo agotado, 5xx, 401, 408, 429, <c>INV-PROHIBIDO</c>, una respuesta inválida o una excepción: el reingreso pudo aplicarse.</summary>
    public sealed record Incierto : ResultadoDelReingreso;
}

/// <summary>Las líneas de un reingreso: la edición y la cantidad (de 1 a 20 con ediciones únicas).</summary>
internal sealed record LineaDeReingreso(Guid EdicionId, int Cantidad);

/// <summary>La carga de un reingreso, <b>inmutable desde el marcador</b> (D07): la sucursal receptora, el pedido y las líneas de la devolución.</summary>
internal sealed record ReingresoPorDevolucion(Guid DevolucionId, Guid PedidoId, string CodigoDeFuente, IReadOnlyList<LineaDeReingreso> Lineas);

/// <summary>Las sucursales activas del Inventario: la lista o «no disponible» (la solicitud responde <c>ECO-SERVICIO-NO-DISPONIBLE</c> sin escribir nada, D09).</summary>
internal abstract record ResultadoDeSucursalesActivas
{
    public sealed record Lista(IReadOnlyCollection<string> Codigos) : ResultadoDeSucursalesActivas;

    public sealed record NoDisponible : ResultadoDeSucursalesActivas;
}

/// <summary>
/// El Inventario visto por Devoluciones (plan §2): reingresar un impreso a la sucursal receptora con el alcance de usuario del encargado o la administración (idempotente por devolución) y listar las sucursales activas con la credencial
/// de servicio. <b>Un libro digital nunca llama aquí</b> (regla 12): una prueba de arquitectura lo vigila. Lo implementa la Infraestructura sobre <c>IClienteDelInventario</c>, sin tocar el token (D15).
/// </summary>
internal interface IInventarioDeDevoluciones
{
    Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion reingreso, CancellationToken cancelacion = default);

    Task<ResultadoDeSucursalesActivas> ListarSucursalesActivas(CancellationToken cancelacion = default);
}

/// <summary>
/// Resuelve el <b>código</b> de la sucursal de un actor (la receptora de una devolución se guarda por código): si no se puede resolver, el alcance se niega y se registra (plan §6). Lo implementa la Infraestructura sobre la lista de
/// sucursales del Inventario.
/// </summary>
internal interface IResolucionDeSucursal
{
    /// <summary>El código de la sucursal o <see langword="null"/> si no se puede resolver (no existe, no está activa o el Inventario no responde).</summary>
    Task<string?> CodigoDe(Guid sucursalId, CancellationToken cancelacion = default);
}
