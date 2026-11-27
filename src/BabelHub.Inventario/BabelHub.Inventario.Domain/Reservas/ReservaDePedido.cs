namespace BabelHub.Inventario.Domain.Reservas;

/// <summary>
/// Una línea de la reserva de un pedido (D06): una edición impresa, su cantidad y la fuente de la que se atiende, con su propio estado porque cada línea se cierra por su cuenta y la unidad vendida debe
/// contar una sola vez (FUE-22). La máquina de estados es: pendiente → confirmada → cerrada (por entrega, reparto o traslado); pendiente o confirmada → liberada; pendiente → expirada. Cualquier otra transición
/// lanza <see cref="TransicionDeReservaInvalidaException"/>. La línea no sabe de stock ni de bloqueos: eso lo hace la Aplicación con la Infraestructura; aquí solo está la regla.
/// </summary>
public sealed class LineaDeReserva
{
    private LineaDeReserva(
        Guid id,
        Guid lineaId,
        Guid edicionId,
        Guid fuenteId,
        int cantidad,
        EstadoDeLinea estado,
        CausaDeCierre? cerradaPor,
        Guid? trasladoId,
        bool creadaPorTraslado,
        bool recreadaEnDestino,
        DateTimeOffset? confirmadaEn,
        DateTimeOffset? anuladaEn,
        DateTimeOffset estadoCambiadoEn)
    {
        Id = id;
        LineaId = lineaId;
        EdicionId = edicionId;
        FuenteId = fuenteId;
        Cantidad = cantidad;
        Estado = estado;
        CerradaPor = cerradaPor;
        TrasladoId = trasladoId;
        CreadaPorTraslado = creadaPorTraslado;
        RecreadaEnDestino = recreadaEnDestino;
        ConfirmadaEn = confirmadaEn;
        AnuladaEn = anuladaEn;
        EstadoCambiadoEn = estadoCambiadoEn;
    }

    public Guid Id { get; }

    /// <summary>El identificador de la línea del pedido, el que asigna el E-commerce.</summary>
    public Guid LineaId { get; }

    public Guid EdicionId { get; }

    public Guid FuenteId { get; }

    public int Cantidad { get; }

    public EstadoDeLinea Estado { get; private set; }

    public CausaDeCierre? CerradaPor { get; private set; }

    /// <summary>El traslado vinculado de la línea, que <c>Reservas</c> fija al confirmar con los traslados que devuelve el puerto.</summary>
    public Guid? TrasladoId { get; private set; }

    /// <summary>La línea que un traslado recrea en el destino: no es una venta (FUE-22, TRA-14).</summary>
    public bool CreadaPorTraslado { get; }

    /// <summary>Solo en una línea cerrada por traslado: si el destino ya la recreó.</summary>
    public bool RecreadaEnDestino { get; private set; }

    public DateTimeOffset? ConfirmadaEn { get; private set; }

    /// <summary>Cuándo se anuló la venta de la línea (el pedido se anuló o no se recogió): ya no cuenta como unidad vendida.</summary>
    public DateTimeOffset? AnuladaEn { get; private set; }

    public DateTimeOffset EstadoCambiadoEn { get; private set; }

    /// <summary>Una línea pendiente nueva.</summary>
    public static LineaDeReserva Pendiente(Guid id, Guid lineaId, Guid edicionId, Guid fuenteId, int cantidad, DateTimeOffset ahora)
    {
        if (id == Guid.Empty || lineaId == Guid.Empty || edicionId == Guid.Empty || fuenteId == Guid.Empty)
        {
            throw new ArgumentException("La línea necesita sus identificadores.");
        }

        return cantidad <= 0
            ? throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad es mayor que cero.")
            : new LineaDeReserva(id, lineaId, edicionId, fuenteId, cantidad, EstadoDeLinea.Pendiente, null, null, false, false, null, null, ahora);
    }

    /// <summary>La línea que un traslado crea ya confirmada en el destino al recibir la mercadería (no cuenta como venta).</summary>
    public static LineaDeReserva ConfirmadaPorTraslado(Guid id, Guid lineaId, Guid edicionId, Guid fuenteId, int cantidad, Guid trasladoId, DateTimeOffset ahora)
    {
        var linea = Pendiente(id, lineaId, edicionId, fuenteId, cantidad, ahora);
        return new LineaDeReserva(linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad, EstadoDeLinea.Confirmada, null, trasladoId, true, false, ahora, null, ahora);
    }

    /// <summary>Reconstruye una línea guardada (la Infraestructura): no valida transiciones, solo la coherencia de los datos.</summary>
    public static LineaDeReserva Rehidratar(
        Guid id,
        Guid lineaId,
        Guid edicionId,
        Guid fuenteId,
        int cantidad,
        EstadoDeLinea estado,
        CausaDeCierre? cerradaPor,
        Guid? trasladoId,
        bool creadaPorTraslado,
        bool recreadaEnDestino,
        DateTimeOffset? confirmadaEn,
        DateTimeOffset? anuladaEn,
        DateTimeOffset estadoCambiadoEn)
    {
        if ((estado == EstadoDeLinea.Cerrada) != cerradaPor.HasValue)
        {
            throw new ArgumentException("Solo una línea cerrada lleva la causa del cierre.", nameof(cerradaPor));
        }

        return cantidad <= 0
            ? throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad es mayor que cero.")
            : new LineaDeReserva(id, lineaId, edicionId, fuenteId, cantidad, estado, cerradaPor, trasladoId, creadaPorTraslado, recreadaEnDestino, confirmadaEn, anuladaEn, estadoCambiadoEn);
    }

    /// <summary>
    /// ¿Cuenta como una reserva activa? La confirmada sí; la pendiente solo si no venció: una vencida que nadie barrió todavía no cuenta (SEL-08, SEL-27). La fecha de expiración es la de la cabecera.
    /// </summary>
    public bool EstaActiva(DateTimeOffset? fechaDeExpiracion, DateTimeOffset ahora) => Estado switch
    {
        EstadoDeLinea.Confirmada => true,
        EstadoDeLinea.Pendiente => !ExpiracionDeReserva.EstaVencida(fechaDeExpiracion, ahora),
        _ => false,
    };

    /// <summary>Pendiente → confirmada, con <c>confirmada_en</c>. La vencida no se confirma: lo comprueba <see cref="ReservaDePedido.Confirmar"/>, que conoce la fecha.</summary>
    public void Confirmar(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeLinea.Pendiente)
        {
            throw TransicionDeReservaInvalidaException.De(Estado, "confirmar");
        }

        Estado = EstadoDeLinea.Confirmada;
        ConfirmadaEn = ahora;
        EstadoCambiadoEn = ahora;
    }

    /// <summary>Pendiente o confirmada → liberada.</summary>
    public void Liberar(DateTimeOffset ahora)
    {
        if (Estado is not (EstadoDeLinea.Pendiente or EstadoDeLinea.Confirmada))
        {
            throw TransicionDeReservaInvalidaException.De(Estado, "liberar");
        }

        Estado = EstadoDeLinea.Liberada;
        EstadoCambiadoEn = ahora;
    }

    /// <summary>Pendiente → expirada (una pendiente vencida que se libera sola).</summary>
    public void Expirar(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeLinea.Pendiente)
        {
            throw TransicionDeReservaInvalidaException.De(Estado, "expirar");
        }

        Estado = EstadoDeLinea.Expirada;
        EstadoCambiadoEn = ahora;
    }

    /// <summary>Confirmada → cerrada, con su causa. Una liberada, expirada, pendiente o ya cerrada no se cierra (SEL-28, SEL-29).</summary>
    public void Cerrar(CausaDeCierre causa, DateTimeOffset ahora)
    {
        if (Estado != EstadoDeLinea.Confirmada)
        {
            throw TransicionDeReservaInvalidaException.De(Estado, "cerrar");
        }

        Estado = EstadoDeLinea.Cerrada;
        CerradaPor = causa;
        RecreadaEnDestino = false;
        EstadoCambiadoEn = ahora;
    }

    /// <summary>Marca la venta de una línea cerrada por traslado como anulada, sin error y sin repetirse: el traslado en tránsito queda marcado de pedido anulado (TRA-19). Solo vale para esa línea.</summary>
    public void Anular(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeLinea.Cerrada || CerradaPor != CausaDeCierre.Traslado)
        {
            throw TransicionDeReservaInvalidaException.De(Estado, "anular");
        }

        AnuladaEn ??= ahora;
    }

    /// <summary>Fija el traslado vinculado de la línea (al confirmar la reserva).</summary>
    public void VincularTraslado(Guid trasladoId)
    {
        if (trasladoId == Guid.Empty)
        {
            throw new ArgumentException("El traslado necesita su identificador.", nameof(trasladoId));
        }

        TrasladoId = trasladoId;
    }

    /// <summary>La línea que consumió el despacho de un traslado se volvió a crear en el destino.</summary>
    public void MarcarRecreadaEnDestino()
    {
        if (Estado != EstadoDeLinea.Cerrada || CerradaPor != CausaDeCierre.Traslado)
        {
            throw TransicionDeReservaInvalidaException.De(Estado, "recrear en el destino");
        }

        RecreadaEnDestino = true;
    }
}

/// <summary>
/// La cabecera de una solicitud de reserva de un pedido (SEL-01): el cliente, la forma de entrega, la expiración compartida por sus líneas pendientes, si ya se extendió y cuántos reemplazos lleva el pedido.
/// Un pedido puede tener varias cabeceras en el tiempo (un reemplazo, SEL-16, crea otra) pero solo la última tiene líneas activas. Una reserva expirada, liberada o confirmada deja de contar para el límite de
/// «una pendiente» (SEL-13); todas las iniciadas con éxito cuentan para los límites por hora.
/// </summary>
public sealed class ReservaDePedido
{
    private readonly List<LineaDeReserva> _lineas;

    private ReservaDePedido(
        Guid id,
        Guid pedidoId,
        Guid clienteId,
        TipoDeEntrega tipoDeEntrega,
        Guid? sucursalDeRecojoId,
        string? regionDeEntrega,
        DateTimeOffset iniciadaEn,
        DateTimeOffset? fechaDeExpiracion,
        bool extendida,
        int reemplazos,
        List<LineaDeReserva> lineas)
    {
        Id = id;
        PedidoId = pedidoId;
        ClienteId = clienteId;
        TipoDeEntrega = tipoDeEntrega;
        SucursalDeRecojoId = sucursalDeRecojoId;
        RegionDeEntrega = regionDeEntrega;
        IniciadaEn = iniciadaEn;
        FechaDeExpiracion = fechaDeExpiracion;
        Extendida = extendida;
        Reemplazos = reemplazos;
        _lineas = lineas;
    }

    public Guid Id { get; }

    public Guid PedidoId { get; }

    public Guid ClienteId { get; }

    public TipoDeEntrega TipoDeEntrega { get; }

    /// <summary>La sucursal de recojo común (solo en un recojo sin sucursal por línea): contra ella se autoriza la entrega (SEL-39).</summary>
    public Guid? SucursalDeRecojoId { get; }

    public string? RegionDeEntrega { get; }

    public DateTimeOffset IniciadaEn { get; }

    /// <summary>Nula cuando ya no expira (confirmada o sin líneas pendientes).</summary>
    public DateTimeOffset? FechaDeExpiracion { get; private set; }

    /// <summary>Ya se extendió la expiración de este pedido: se hereda en un reemplazo, porque la extensión es una sola por pedido (SEL-22).</summary>
    public bool Extendida { get; private set; }

    /// <summary>Cuántos reemplazos lleva el pedido: tope de <see cref="ContadorDeReemplazos.Maximo"/> (SEL-36).</summary>
    public int Reemplazos { get; }

    public IReadOnlyList<LineaDeReserva> Lineas => _lineas;

    /// <summary>
    /// Inicia una reserva con sus líneas pendientes y la expiración a 15 minutos. <paramref name="reemplazos"/> es la cuenta ya incrementada si esta reserva reemplaza a otra; <paramref name="extendida"/>,
    /// la marca heredada de la que reemplaza. Debe tener al menos una línea y no repetir la línea del pedido.
    /// </summary>
    public static ReservaDePedido Iniciar(
        Guid id,
        Guid pedidoId,
        Guid clienteId,
        TipoDeEntrega tipoDeEntrega,
        Guid? sucursalDeRecojoId,
        string? regionDeEntrega,
        DateTimeOffset iniciadaEn,
        IEnumerable<LineaDeReserva> lineas,
        int reemplazos = 0,
        bool extendida = false)
    {
        if (tipoDeEntrega == TipoDeEntrega.Recojo && sucursalDeRecojoId is null)
        {
            throw new ArgumentException("El recojo necesita la sucursal.", nameof(sucursalDeRecojoId));
        }

        if (tipoDeEntrega == TipoDeEntrega.Domicilio && string.IsNullOrWhiteSpace(regionDeEntrega))
        {
            throw new ArgumentException("El domicilio necesita la región de la dirección.", nameof(regionDeEntrega));
        }

        return Nueva(id, pedidoId, clienteId, tipoDeEntrega, sucursalDeRecojoId, regionDeEntrega, iniciadaEn, lineas, reemplazos, extendida);
    }

    /// <summary>
    /// Inicia una reserva de recojo por sucursal (SEL-41, D17): no hay una sucursal de recojo común porque cada línea se recoge en la suya, que es su fuente. El domicilio por sucursal usa
    /// <see cref="Iniciar"/>, que ya no lleva sucursal.
    /// </summary>
    public static ReservaDePedido IniciarPorSucursal(
        Guid id,
        Guid pedidoId,
        Guid clienteId,
        DateTimeOffset iniciadaEn,
        IEnumerable<LineaDeReserva> lineas,
        int reemplazos = 0,
        bool extendida = false) =>
        Nueva(id, pedidoId, clienteId, TipoDeEntrega.Recojo, null, null, iniciadaEn, lineas, reemplazos, extendida);

    private static ReservaDePedido Nueva(
        Guid id,
        Guid pedidoId,
        Guid clienteId,
        TipoDeEntrega tipoDeEntrega,
        Guid? sucursalDeRecojoId,
        string? regionDeEntrega,
        DateTimeOffset iniciadaEn,
        IEnumerable<LineaDeReserva> lineas,
        int reemplazos,
        bool extendida)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        var lista = lineas.ToList();
        if (id == Guid.Empty || pedidoId == Guid.Empty || clienteId == Guid.Empty)
        {
            throw new ArgumentException("La reserva necesita sus identificadores.");
        }

        if (lista.Count == 0 || lista.Select(l => l.LineaId).Distinct().Count() != lista.Count)
        {
            throw new ArgumentException("La reserva necesita al menos una línea y no repite la línea del pedido.", nameof(lineas));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(reemplazos);
        return new ReservaDePedido(id, pedidoId, clienteId, tipoDeEntrega, sucursalDeRecojoId, regionDeEntrega, iniciadaEn, ExpiracionDeReserva.AlIniciar(iniciadaEn), extendida, reemplazos, lista);
    }

    /// <summary>Reconstruye una cabecera guardada (la Infraestructura).</summary>
    public static ReservaDePedido Rehidratar(
        Guid id,
        Guid pedidoId,
        Guid clienteId,
        TipoDeEntrega tipoDeEntrega,
        Guid? sucursalDeRecojoId,
        string? regionDeEntrega,
        DateTimeOffset iniciadaEn,
        DateTimeOffset? fechaDeExpiracion,
        bool extendida,
        int reemplazos,
        IEnumerable<LineaDeReserva> lineas) =>
        new(id, pedidoId, clienteId, tipoDeEntrega, sucursalDeRecojoId, regionDeEntrega, iniciadaEn, fechaDeExpiracion, extendida, reemplazos, [.. lineas]);

    public bool EstaVencida(DateTimeOffset ahora) => ExpiracionDeReserva.EstaVencida(FechaDeExpiracion, ahora);

    public bool EsDeRecojo => TipoDeEntrega == TipoDeEntrega.Recojo;

    /// <summary>Un recojo sin sucursal de cabecera: cada línea se recoge en la sucursal que la atiende (SEL-41, SEL-45).</summary>
    public bool EsRecojoPorSucursal => EsDeRecojo && SucursalDeRecojoId is null;

    /// <summary>Dónde se entrega una línea de un recojo: la sucursal de la cabecera o, si no la hay, la fuente de la línea.</summary>
    public Guid SucursalDeEntregaDe(LineaDeReserva linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        return SucursalDeRecojoId ?? linea.FuenteId;
    }

    /// <summary>Las líneas que cuentan como reserva activa en este instante: las confirmadas y las pendientes no vencidas.</summary>
    public IEnumerable<LineaDeReserva> LineasActivas(DateTimeOffset ahora) => _lineas.Where(l => l.EstaActiva(FechaDeExpiracion, ahora));

    /// <summary>Tiene una línea pendiente que todavía no venció: cuenta para el límite de «una pendiente» (SEL-13) y es la que un reemplazo sustituye (SEL-16).</summary>
    public bool TienePendienteVigente(DateTimeOffset ahora) => _lineas.Any(l => l.Estado == EstadoDeLinea.Pendiente) && !EstaVencida(ahora);

    /// <summary>
    /// Confirma las líneas pendientes no vencidas (SEL-06): quedan confirmadas con <c>confirmada_en</c> y la reserva ya no expira. Repetirlo cuando todo está confirmado o cerrado no cambia nada
    /// (devuelve <see langword="false"/>). Una reserva vencida, expirada o liberada se rechaza: obliga a reiniciar el pago (spec §6).
    /// </summary>
    public bool Confirmar(DateTimeOffset ahora)
    {
        if (_lineas.All(l => l.Estado is EstadoDeLinea.Confirmada or EstadoDeLinea.Cerrada))
        {
            return false;
        }

        if (_lineas.Any(l => l.Estado is EstadoDeLinea.Liberada or EstadoDeLinea.Expirada) || EstaVencida(ahora))
        {
            throw new TransicionDeReservaInvalidaException("La reserva venció o se liberó: el pago debe reiniciarse.");
        }

        foreach (var linea in _lineas.Where(l => l.Estado == EstadoDeLinea.Pendiente))
        {
            linea.Confirmar(ahora);
        }

        FechaDeExpiracion = null;
        return true;
    }

    /// <summary>
    /// Extiende la expiración a 30 minutos desde el inicio del pago, una sola vez por pedido (SEL-22); devuelve falso (y no cambia nada) si ya se extendió, ya venció, se confirmó o se liberó (SEL-30).
    /// </summary>
    public bool Extender(DateTimeOffset ahora)
    {
        if (!_lineas.Any(l => l.Estado == EstadoDeLinea.Pendiente))
        {
            return false;
        }

        if (ExpiracionDeReserva.Extender(IniciadaEn, FechaDeExpiracion, Extendida, ahora) is not { } nueva)
        {
            return false;
        }

        FechaDeExpiracion = nueva;
        Extendida = true;
        return true;
    }
}

/// <summary>
/// El tope de reemplazos de la reserva pendiente de un pedido (SEL-36, D14): como máximo <see cref="Maximo"/>. La cuenta se hereda en cada reemplazo (liberar la anterior por reemplazo no la reinicia) y solo
/// la cierran confirmar, liberar por otro motivo o expirar la reserva. Un reemplazo rechazado por falta de stock o por el tope no la incrementa (SEL-37).
/// </summary>
public static class ContadorDeReemplazos
{
    public const int Maximo = 3;

    /// <summary>¿Se admite un reemplazo más?</summary>
    public static bool Admite(int reemplazosActuales) => reemplazosActuales >= 0 && reemplazosActuales < Maximo;

    /// <summary>La cuenta tras un reemplazo aceptado; lanza si ya se alcanzó el tope.</summary>
    public static int Siguiente(int reemplazosActuales) =>
        Admite(reemplazosActuales)
            ? reemplazosActuales + 1
            : throw new InvalidOperationException("El pedido ya alcanzó el tope de reemplazos.");
}
