namespace BabelHub.Inventario.Application.Reservas;

// El contrato público del módulo Reservas (la raíz de su espacio de nombres): las entradas y salidas de sus casos de uso, que la Api llama por ICasoDeUso<TEntrada, TSalida> sin tocar el Interno (regla 13,
// como Fuentes), y los puertos que declara y que implementan otros módulos. Son modelos propios: ni DTOs de Shared ni tipos del Dominio. Ningún tipo que lleve un identificador de cliente lo imprime en
// su ToString.

/// <summary>Cómo recibe el cliente el pedido (CAR-08).</summary>
public enum TipoDeEntregaDeReserva
{
    Recojo,
    Domicilio,
}

/// <summary>El estado de una línea de reserva, o el de la reserva entera cuando todas las líneas coinciden.</summary>
public enum EstadoDeReserva
{
    Pendiente,
    Confirmada,
    Cerrada,
    Liberada,
    Expirada,
}

/// <summary>Por qué se libera una reserva (SEL-07, SEL-17, D15); la regla de qué libera cada motivo es del Dominio.</summary>
public enum MotivoDeLiberacionDeReserva
{
    CheckoutCancelado,
    PagoVencido,
    PedidoAnulado,
    CanceladoPorElCliente,
    FallaPosteriorAlPago,
    NoRecogido,
}

/// <summary>Por qué se cierra una reserva confirmada: la entrega en recojo o el paso a en reparto (SEL-11, SEL-21).</summary>
public enum MotivoDeCierreDeReserva
{
    Entrega,
    Reparto,
}

/// <summary>
/// Una línea impresa que se pide reservar o consultar: su identificador (el que asigna el E-commerce), la edición, la cantidad y, en la venta por sucursal, la sucursal elegida para atenderla (SEL-41): se
/// declara en todas las líneas impresas o en ninguna (SEL-42).
/// </summary>
public sealed record LineaSolicitada(Guid LineaId, Guid EdicionId, int Cantidad, string? CodigoDeSucursal = null);

/// <summary>
/// Reserva los impresos de un pedido (SEL-01): todas las líneas o ninguna. El cliente <b>no viaja aquí</b>: sale del actor verificado (un <c>clienteId</c> en el cuerpo se rechaza). Repetir el
/// <c>pedidoId</c> con una reserva pendiente la reemplaza (SEL-16).
/// </summary>
public sealed record ReservarEntrada(
    Guid PedidoId,
    TipoDeEntregaDeReserva TipoDeEntrega,
    string? CodigoDeSucursalDeRecojo,
    string? RegionDeEntrega,
    IReadOnlyList<LineaSolicitada> Lineas,
    string ClaveDeIdempotencia)
{
    /// <summary>La clave de idempotencia y las líneas no deben llegar a un registro por un <c>ToString</c> automático.</summary>
    public override string ToString() => nameof(ReservarEntrada);
}

/// <summary>Una línea con su estado y la fuente asignada (nunca se divide entre fuentes, SEL-25).</summary>
public sealed record LineaReservada(Guid LineaId, Guid EdicionId, int Cantidad, EstadoDeReserva Estado, string CodigoDeFuente, bool RequiereTraslado, int? PlazoEstimadoEnDias);

/// <summary>Un traslado vinculado al pedido (SEL-03); <see cref="FechaEstimadaDeLlegada"/> es nula si la ruta no está definida.</summary>
public sealed record TrasladoVinculadoAlPedido(Guid TrasladoId, Guid EdicionId, int Cantidad, string CodigoDeOrigen, string CodigoDeDestino, string Estado, DateOnly? FechaEstimadaDeLlegada);

/// <summary>
/// La reserva de un pedido: su estado, cuándo se creó, cuándo expira (nulo si ya no expira), sus líneas y, solo tras confirmarla, los traslados vinculados. <see cref="EsRepeticion"/> es verdadero cuando es la
/// respuesta original de una solicitud repetida con la misma clave (SEL-09): no cambió nada y el vendible no bajó otra vez.
/// </summary>
public sealed record ReservaDelPedido(
    Guid PedidoId,
    EstadoDeReserva Estado,
    DateTimeOffset CreadaEn,
    DateTimeOffset? ExpiraEn,
    IReadOnlyList<LineaReservada> Lineas,
    IReadOnlyList<TrasladoVinculadoAlPedido> Traslados,
    bool EsRepeticion = false);

/// <summary>
/// Una línea sin stock vendible suficiente y lo máximo que hay en la red (CAR-04, CAR-27). Con sucursal declarada, <see cref="DisponibleEnLaSucursal"/> es el vendible que esa sucursal tenía de la edición antes de
/// repartirlo entre las líneas de la solicitud (SEL-43); sin ella es nulo.
/// </summary>
public sealed record LineaSinStock(Guid LineaId, Guid EdicionId, int Solicitada, int DisponibleEnLaRed, int? DisponibleEnLaSucursal = null);

/// <summary>
/// Cómo viaja el detalle de una falta de stock dentro de <c>ErrorDeAplicacion.Errores</c> (el contrato de error solo conoce campo y clave): un <c>ErrorDeCampo</c> por línea, con el campo
/// <c>lineas.{lineaId}</c> y una clave <c>sin-stock:{edicionId}:{solicitada}:{disponibleEnLaRed}</c>, con una cuarta parte <c>:{disponibleEnLaSucursal}</c> en la venta por sucursal. La Api lo lee con
/// <see cref="TryLeer"/> para armar el cuerpo del 409; ningún valor del cliente viaja aquí.
/// </summary>
public static class DetalleDeFaltaDeStock
{
    public const string ClaveDeStockInsuficiente = "stock-insuficiente";

    private const string Prefijo = "lineas.";

    private const string PrefijoDeClave = "sin-stock:";

    public static Plataforma.ErrorDeCampo Codificar(LineaSinStock linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        var enLaSucursal = linea.DisponibleEnLaSucursal is { } disponible ? $":{disponible}" : string.Empty;
        return new Plataforma.ErrorDeCampo($"{Prefijo}{linea.LineaId:D}", $"{PrefijoDeClave}{linea.EdicionId:D}:{linea.Solicitada}:{linea.DisponibleEnLaRed}{enLaSucursal}");
    }

    public static bool TryLeer(Plataforma.ErrorDeCampo campo, out LineaSinStock? linea)
    {
        ArgumentNullException.ThrowIfNull(campo);
        linea = null;

        if (!campo.Campo.StartsWith(Prefijo, StringComparison.Ordinal) || !campo.Clave.StartsWith(PrefijoDeClave, StringComparison.Ordinal)
            || !Guid.TryParseExact(campo.Campo[Prefijo.Length..], "D", out var lineaId))
        {
            return false;
        }

        var partes = campo.Clave[PrefijoDeClave.Length..].Split(':');
        if (partes.Length is not (3 or 4) || !Guid.TryParseExact(partes[0], "D", out var edicionId)
            || !TryLeerCantidad(partes[1], out var solicitada)
            || !TryLeerCantidad(partes[2], out var disponible))
        {
            return false;
        }

        int? enLaSucursal = null;
        if (partes.Length == 4)
        {
            if (!TryLeerCantidad(partes[3], out var cantidad))
            {
                return false;
            }

            enLaSucursal = cantidad;
        }

        linea = new LineaSinStock(lineaId, edicionId, solicitada, disponible, enLaSucursal);
        return true;
    }

    private static bool TryLeerCantidad(string texto, out int cantidad) =>
        int.TryParse(texto, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cantidad);
}

/// <summary>Consulta previa de la selección de fuentes de un carrito, sin reservar (SEL-20).</summary>
public sealed record ConsultarSeleccionEntrada(TipoDeEntregaDeReserva TipoDeEntrega, string? CodigoDeSucursalDeRecojo, string? RegionDeEntrega, IReadOnlyList<LineaSolicitada> Lineas);

/// <summary>
/// La fuente que se seleccionaría para una línea impresa (o la falta de stock y lo que hay en la red). Con sucursal declarada no hay traslado ni plazo, y la falta de stock trae en
/// <see cref="DisponibleEnLaSucursal"/> el vendible de esa sucursal (SEL-44).
/// </summary>
public sealed record SeleccionDeLinea(Guid LineaId, Guid EdicionId, bool HayStock, string? CodigoDeFuente, bool RequiereTraslado, int? PlazoEstimadoEnDias, int DisponibleEnLaRed, int? DisponibleEnLaSucursal = null);

/// <summary>La respuesta de la consulta previa: una entrada por línea impresa (las digitales se ignoran) y cuándo se consultó.</summary>
public sealed record SeleccionConsultada(IReadOnlyList<SeleccionDeLinea> Lineas, DateTimeOffset ConsultadaEn);

/// <summary>Consulta la reserva de un pedido: el cliente dueño o administración (el de otro cliente, no encontrado).</summary>
public sealed record ConsultarReservaEntrada(Guid PedidoId);

/// <summary>Confirma las reservas de un pedido pagado (SEL-06); la clave de idempotencia se acota por pedido.</summary>
public sealed record ConfirmarReservasEntrada(Guid PedidoId, string ClaveDeIdempotencia)
{
    public override string ToString() => nameof(ConfirmarReservasEntrada);
}

/// <summary>Informa que el pago quedó pendiente de confirmación: extiende la expiración una sola vez (SEL-22).</summary>
public sealed record ExtenderReservasEntrada(Guid PedidoId);

/// <summary><see cref="Extendida"/> es falso si el aviso se ignoró porque ya venció, se confirmó, se liberó o se extendió (SEL-30).</summary>
public sealed record ExtensionDeReserva(bool Extendida, ReservaDelPedido Reserva);

/// <summary>Libera la reserva de un pedido o solo algunas líneas (<see cref="LineaIds"/> vacío o nulo son todas). Repetirla no cambia el stock (SEL-07).</summary>
public sealed record LiberarReservasEntrada(Guid PedidoId, MotivoDeLiberacionDeReserva Motivo, IReadOnlyList<Guid>? LineaIds);

/// <summary>Descuenta el físico de las líneas indicadas y cierra sus reservas confirmadas (SEL-11, SEL-21); <see cref="EnvioId"/> solo en el reparto.</summary>
public sealed record CerrarReservasEntrada(Guid PedidoId, MotivoDeCierreDeReserva Motivo, IReadOnlyList<Guid> LineaIds, Guid? EnvioId, string ClaveDeIdempotencia)
{
    public override string ToString() => nameof(CerrarReservasEntrada);
}

/// <summary>El resultado de cerrar una línea: cerrada ahora o <c>ya-cerrada</c> (el descuento ya estaba aplicado y no se repite).</summary>
public sealed record ResultadoDeCierreDeLinea(Guid LineaId, bool YaCerrada);

/// <summary>El resultado del cierre de las líneas de una reserva.</summary>
public sealed record CierreDeReserva(Guid PedidoId, IReadOnlyList<ResultadoDeCierreDeLinea> Lineas);

/// <summary>Cuántas reservas activas tiene un titular, sin datos de las ediciones (SEL-33). El identificador viaja en el cuerpo, nunca en la ruta.</summary>
public sealed record ConsultarPendientesEntrada(Guid TitularId)
{
    public override string ToString() => "ConsultarPendientesEntrada";
}

/// <summary>Pedidos con una línea confirmada, pendiente no vencida o en tránsito entre sucursales.</summary>
public sealed record PendientesDeTitular(int ReservasActivas);

/// <summary>La baja de un titular de cuenta cancelada (SEL-34, SEL-35).</summary>
public sealed record ProcesarBajaDeTitularEntrada(Guid TitularId)
{
    public override string ToString() => "ProcesarBajaDeTitularEntrada";
}

/// <summary>La baja se procesó (o ya estaba procesada): repetirla no tiene efecto.</summary>
public sealed record BajaDeTitularProcesada(bool EraRepeticion);

// ---------- Puertos que declara Reservas y que implementan otros módulos ----------

/// <summary>Una solicitud de traslado desde una fuente hacia la sucursal de recojo, por edición (SEL-03).</summary>
public sealed record SolicitudDeTrasladoDePedido(Guid FuenteDeOrigenId, Guid EdicionId, int Cantidad);

/// <summary>
/// Puerto que <b>declara</b> <c>Reservas</c> y <b>implementa</b> <c>Traslados</c> (SEL-03): al confirmar un pedido de recojo, genera <b>en la misma transacción</b> una solicitud de traslado vinculada al pedido por
/// cada fuente y edición cuya fuente no es la sucursal de recojo, y devuelve los traslados (creados o ya existentes, también al repetir la confirmación).
/// </summary>
public interface IGeneradorDeTrasladosDePedido
{
    Task<IReadOnlyList<TrasladoVinculadoAlPedido>> Generar(Guid pedidoId, Guid sucursalDeRecojoId, IReadOnlyCollection<SolicitudDeTrasladoDePedido> solicitudes, CancellationToken cancelacion = default);
}

/// <summary>
/// Puerto que <b>declara</b> <c>Reservas</c> y <b>implementa</b> <c>Traslados</c> (TRA-15, TRA-19): se invoca <b>antes</b> de tocar líneas y stock, solo con los motivos que anulan el pedido y también en la repetición,
/// con los traslados de las líneas afectadas (que <c>Reservas</c> guarda en <c>traslado_id</c>: <c>Traslados</c> no conoce el <c>linea_id</c>, regla 13).
/// </summary>
public interface IAlLiberarPedido
{
    Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default);
}

/// <summary>
/// Puerto que <b>declara</b> <c>Reservas</c> (la clave del entorno justifica el puerto): calcula el identificador anónimo estable de un titular dado de baja (SEL-34), con HMAC y una clave que no permite recuperar el
/// original; el mismo titular da siempre el mismo identificador y titulares distintos, identificadores distintos.
/// </summary>
public interface IGeneradorDeIdentificadorAnonimo
{
    Guid Generar(Guid titularId);
}

// ---------- Para Traslados: consultar, consumir en el origen y recrear en el destino ----------

/// <summary>Cómo terminó un movimiento de la reserva de un pedido que pide <c>Traslados</c>.</summary>
public enum ResultadoDeMovimientoDeReserva
{
    Aplicado,

    /// <summary>La reserva no está confirmada (liberada, expirada o ya cerrada por otra operación): estado inválido, sin cambios (SEL-28, SEL-29).</summary>
    EstadoInvalido,

    /// <summary>La política de filas rechazó la escritura por alcance: quien llama la traduce a <c>INV-PROHIBIDO</c> con su evento.</summary>
    DeniegadoPorAlcance,

    /// <summary>El pedido no tiene una reserva de esa fuente y edición.</summary>
    NoEncontrada,
}

/// <summary>La reserva confirmada de un pedido en una fuente y edición: lo que <c>Traslados</c> necesita saber para despachar y recibir.</summary>
public sealed record ReservaConfirmadaDeLaFuente(Guid PedidoId, Guid FuenteId, Guid EdicionId, int Cantidad, Guid? TrasladoId);

/// <summary>
/// Contrato público para <c>Traslados</c> (SEL-11, SEL-28, SEL-29): consulta la reserva confirmada de un pedido por fuente y edición, la <b>consume en el origen</b> (el físico y lo reservado bajan, la línea pasa a cerrada
/// por traslado y queda marcada como no recreada en el destino) y la <b>recrea en el destino</b> (el físico y lo reservado suben, línea confirmada creada por traslado). Toman el bloqueo del pedido; quien llama autoriza antes.
/// </summary>
public interface IReservasParaTraslados
{
    Task<ReservaConfirmadaDeLaFuente?> ConsultarConfirmada(Guid pedidoId, Guid fuenteId, Guid edicionId, CancellationToken cancelacion = default);

    Task<ResultadoDeMovimientoDeReserva> ConsumirEnElOrigen(Guid pedidoId, Guid fuenteDeOrigenId, Guid edicionId, CancellationToken cancelacion = default);

    Task<ResultadoDeMovimientoDeReserva> RecrearEnElDestino(Guid pedidoId, Guid fuenteDeDestinoId, Guid edicionId, int cantidad, Guid trasladoId, CancellationToken cancelacion = default);
}

// ---------- El rechazo por límite de reservas (SEL-13, SEL-14, SEL-19, SEL-36) ----------

/// <summary>Los motivos de un rechazo por límite de reservas: son las claves de las políticas (los de SEL-14, SEL-19 y SEL-36) y la propia de SEL-13.</summary>
public static class MotivosDeLimiteDeReservas
{
    public const string PendientePorCliente = "reservas-pendiente-por-cliente";

    public const string PorHoraPorCliente = "reservas-por-hora-por-cliente";

    public const string PorHoraPorOrigen = "reservas-por-hora-por-origen";

    public const string ReemplazosPorPedido = "reservas-reemplazos-por-pedido";
}

/// <summary>Un rechazo por límite: el motivo, cuánto esperar y desde cuándo se puede reintentar. La respuesta es la misma para todos los motivos (SEL-15): solo cambian estos tres datos.</summary>
public sealed record RechazoPorLimite(string Motivo, int EsperaEnSegundos, DateTimeOffset DisponibleDesde);

/// <summary>
/// Cómo viaja un rechazo por límite dentro de <c>ErrorDeAplicacion.Errores</c> (con categoría <c>Limite</c> y clave <c>limite-excedido</c>): tres <c>ErrorDeCampo</c> (<c>limite</c> con el motivo,
/// <c>esperaEnSegundos</c> y <c>disponibleDesde</c> en ISO 8601 UTC). La Api lo lee con <see cref="TryLeer"/> para armar el 429; ningún dato del cliente viaja aquí.
/// </summary>
public static class DetalleDeLimiteDeReservas
{
    public const string ClaveDeLimiteExcedido = "limite-excedido";

    private const string Limite = "limite";

    private const string Espera = "esperaEnSegundos";

    private const string Desde = "disponibleDesde";

    public static IReadOnlyList<Plataforma.ErrorDeCampo> Codificar(RechazoPorLimite rechazo)
    {
        ArgumentNullException.ThrowIfNull(rechazo);
        return
        [
            new Plataforma.ErrorDeCampo(Limite, rechazo.Motivo),
            new Plataforma.ErrorDeCampo(Espera, rechazo.EsperaEnSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Plataforma.ErrorDeCampo(Desde, rechazo.DisponibleDesde.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    public static bool TryLeer(IReadOnlyList<Plataforma.ErrorDeCampo> errores, out RechazoPorLimite? rechazo)
    {
        ArgumentNullException.ThrowIfNull(errores);
        rechazo = null;
        var motivo = errores.FirstOrDefault(e => e.Campo == Limite)?.Clave;
        var espera = errores.FirstOrDefault(e => e.Campo == Espera)?.Clave;
        var desde = errores.FirstOrDefault(e => e.Campo == Desde)?.Clave;
        if (motivo is null || !int.TryParse(espera, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var segundos)
            || !DateTimeOffset.TryParseExact(desde, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var instante))
        {
            return false;
        }

        rechazo = new RechazoPorLimite(motivo, segundos, instante);
        return true;
    }
}
