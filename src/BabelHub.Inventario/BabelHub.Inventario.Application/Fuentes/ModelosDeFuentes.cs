namespace BabelHub.Inventario.Application.Fuentes;

// Modelos del contrato público del módulo Fuentes. Son propios del módulo (no son DTOs de Shared ni tipos del Dominio): los demás módulos del
// Inventario (Reservas, Ajustes, Traslados) dependen solo de la raíz de este espacio de nombres (AGENTS.md regla 13, D01).

/// <summary>El estado de stock de una edición en una fuente, según su vendible (FUE-18): sin stock con 0, bajo de 1 a 2 y normal desde 3.</summary>
public enum EstadoDeExistencia
{
    SinStock,
    Bajo,
    Normal,
}

/// <summary>El formato de una edición: solo las impresas tienen stock.</summary>
public enum FormatoDeEdicionRegistrado
{
    Impreso,
    Digital,
}

/// <summary>Una fuente (sucursal) como la ven los otros módulos: sin el contacto de notificación, que solo sale por su consulta propia (FUE-01).</summary>
public sealed record FuenteDeStock(Guid Id, string Codigo, string Nombre, string Region, bool EsRecepcion);

/// <summary>El plazo estimado, en días, del traslado desde una fuente hacia otra (FUE-12). Es direccional.</summary>
public sealed record RutaDeTraslado(Guid OrigenId, Guid DestinoId, int PlazoEnDias);

/// <summary>Lo que el Inventario sabe de una edición: su formato (D03). Sin ficha, la edición es inexistente (FUE-15).</summary>
public sealed record FichaDeEdicionRegistrada(Guid EdicionId, FormatoDeEdicionRegistrado Formato)
{
    public bool EsImpresa => Formato == FormatoDeEdicionRegistrado.Impreso;
}

/// <summary>
/// Físico, reservado y vendible de una edición en una fuente, con su estado. El vendible ya descuenta lo reservado y devuelve las reservas
/// pendientes vencidas que nadie procesó (FUE-03): <c>max(0, físico − reservado + vencidas)</c>.
/// </summary>
public sealed record ExistenciaEnFuente(Guid FuenteId, string CodigoDeFuente, string NombreDeFuente, Guid EdicionId, int Fisico, int Reservado, int Vendible, EstadoDeExistencia Estado);

/// <summary>El vendible de una edición en toda la red y su desglose por fuente (FUE-05). Las fuentes sin fila figuran en cero.</summary>
public sealed record DisponibilidadDeEdicion(Guid EdicionId, int VendibleDeLaRed, IReadOnlyList<ExistenciaEnFuente> PorFuente);

/// <summary>Lo que se pide de las existencias de una fuente: filtros por edición y por estado, y una página de 1 a 100 elementos (FUE-09).</summary>
public sealed record ConsultaDeExistenciasDeFuente(Guid FuenteId, IReadOnlyCollection<Guid>? EdicionIds, EstadoDeExistencia? Estado, int Pagina, int Tamano);

/// <summary>Una página de existencias de una fuente y el total que cumple los filtros.</summary>
public sealed record PaginaDeExistencias(IReadOnlyList<ExistenciaEnFuente> Elementos, int Total, int Pagina, int Tamano);

/// <summary>Unidades físicas y reservadas de una pareja (fuente, edición).</summary>
public sealed record CantidadesDeStock(int Fisico, int Reservado);

/// <summary>
/// Un cambio que se aplica al stock de una pareja (fuente, edición): <paramref name="DeltaFisico"/> y <paramref name="DeltaReservado"/> pueden
/// ser negativos, y el resultado debe cumplir <c>0 ≤ reservado ≤ físico</c> (FUE-04).
/// </summary>
public sealed record MovimientoDeStock(Guid FuenteId, Guid EdicionId, int DeltaFisico, int DeltaReservado);

/// <summary>Cómo terminó un <see cref="MovimientoDeStock"/>.</summary>
public enum ResultadoDeMovimiento
{
    /// <summary>Se aplicó: el resultado trae las cantidades anterior y posterior.</summary>
    Aplicado,

    /// <summary>El resultado dejaría un estado imposible (por ejemplo, falta de stock): no se cambió nada. El llamador lo traduce a la causa que le corresponde.</summary>
    NoAplicable,

    /// <summary>La política de filas rechazó la escritura porque la fila está fuera del alcance del actor. El llamador la traduce a <c>INV-PROHIBIDO</c> y registra el acceso denegado.</summary>
    DeniegadoPorAlcance,

    /// <summary>La edición no existe o no es impresa (o la fuente no existe): no hay stock que cambiar.</summary>
    EdicionInexistenteONoImpresa,
}

/// <summary>El resultado de aplicar un movimiento: cómo terminó y, si se aplicó, las cantidades de la fila antes y después.</summary>
public sealed record ResultadoDeAplicacion(ResultadoDeMovimiento Resultado, CantidadesDeStock? Anterior, CantidadesDeStock? Posterior)
{
    public bool Aplicado => Resultado == ResultadoDeMovimiento.Aplicado;

    public static ResultadoDeAplicacion Exito(CantidadesDeStock anterior, CantidadesDeStock posterior) => new(ResultadoDeMovimiento.Aplicado, anterior, posterior);

    public static ResultadoDeAplicacion Fallo(ResultadoDeMovimiento resultado) =>
        resultado == ResultadoDeMovimiento.Aplicado
            ? throw new ArgumentException("Un fallo no puede ser «aplicado».", nameof(resultado))
            : new ResultadoDeAplicacion(resultado, null, null);
}

/// <summary>La fila de stock de una pareja (fuente, edición) ya bloqueada, con las cantidades vigentes tras liberar las reservas vencidas.</summary>
public sealed record ExistenciaBloqueada(Guid FuenteId, Guid EdicionId, int Fisico, int Reservado);

/// <summary>Cómo terminó un bloqueo de filas de stock.</summary>
public enum ResultadoDeBloqueo
{
    /// <summary>Todas las filas de todas las fuentes de las ediciones quedaron bloqueadas.</summary>
    Bloqueado,

    /// <summary>Alguna edición no existe o no es impresa: no se bloqueó nada.</summary>
    EdicionInexistenteONoImpresa,
}

/// <summary>El resultado de <c>Bloquear</c>: las filas bloqueadas (una por fuente y edición, con ceros si no existía) o el rechazo.</summary>
public sealed record BloqueoDeStock(ResultadoDeBloqueo Resultado, IReadOnlyList<ExistenciaBloqueada> Filas)
{
    public bool EstaBloqueado => Resultado == ResultadoDeBloqueo.Bloqueado;

    /// <summary>La fila bloqueada de la pareja; lanza si no es parte del bloqueo.</summary>
    public ExistenciaBloqueada Fila(Guid fuenteId, Guid edicionId) =>
        Filas.FirstOrDefault(f => f.FuenteId == fuenteId && f.EdicionId == edicionId)
        ?? throw new InvalidOperationException("La pareja de fuente y edición no es parte del bloqueo.");

    public static BloqueoDeStock Con(IReadOnlyList<ExistenciaBloqueada> filas) => new(ResultadoDeBloqueo.Bloqueado, filas);

    public static BloqueoDeStock Rechazado() => new(ResultadoDeBloqueo.EdicionInexistenteONoImpresa, []);
}

/// <summary>Unidades de reservas pendientes ya vencidas que nadie procesó, por pareja (fuente, edición). Siguen sumadas en lo reservado (D02).</summary>
public sealed record UnidadesVencidas(Guid FuenteId, Guid EdicionId, int Unidades);

/// <summary>Unidades que una liberación de vencidas devolvió, por pareja: solo las de las líneas que de verdad cambiaron de estado. Bajan lo reservado.</summary>
public sealed record UnidadesLiberadas(Guid FuenteId, Guid EdicionId, int Unidades);

/// <summary>Los pedidos que apartan unidades de una fuente y una edición (solo identificadores, sin datos de clientes) y el total que apartan.</summary>
public sealed record PedidosQueApartanUnidades(IReadOnlyList<Guid> PedidoIds, int TotalDePedidos);

/// <summary>Unidades vendidas de un pedido, ya atribuidas a la fuente que despacha o entrega (FUE-22): una vez por pedido y edición.</summary>
public sealed record VentaAtribuida(Guid PedidoId, Guid EdicionId, Guid FuenteId, int Unidades);

/// <summary>Unidades de un pedido que volvieron al stock por una devolución, sumadas en todas las sucursales.</summary>
public sealed record UnidadesReingresadas(Guid PedidoId, Guid EdicionId, int Unidades);

