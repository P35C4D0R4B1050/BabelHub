namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>
/// El motivo de una anulación (D05): de <b>lista cerrada</b> y sin nota libre (un texto libre agregaría datos personales sin finalidad, RNF23). La administración central usa <see cref="PagoDuplicado"/>, <see cref="NoRecogido"/> u <see cref="Otro"/>; la cancelación del cliente,
/// <see cref="CanceladoPorElCliente"/>. La lista es inicial y se amplía sin cambiar la spec.
/// </summary>
public enum MotivoDeAnulacion
{
    PagoDuplicado,
    NoRecogido,
    Otro,
    CanceladoPorElCliente,
}

public static class MotivosDeAnulacion
{
    public static string Codigo(MotivoDeAnulacion motivo) => motivo switch
    {
        MotivoDeAnulacion.PagoDuplicado => "pago-duplicado",
        MotivoDeAnulacion.NoRecogido => "no-recogido",
        MotivoDeAnulacion.Otro => "otro",
        MotivoDeAnulacion.CanceladoPorElCliente => "cancelado-por-el-cliente",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    /// <summary>Lee un código exacto de la lista cerrada (sin mayúsculas, espacios ni texto libre).</summary>
    public static bool TryParse(string? codigo, out MotivoDeAnulacion motivo)
    {
        foreach (var candidato in Enum.GetValues<MotivoDeAnulacion>())
        {
            if (string.Equals(Codigo(candidato), codigo, StringComparison.Ordinal))
            {
                motivo = candidato;
                return true;
            }
        }

        motivo = default;
        return false;
    }

    /// <summary>Los motivos que puede usar la administración central (no el de la cancelación del cliente).</summary>
    public static bool EsDeLaAdministracion(MotivoDeAnulacion motivo) => motivo != MotivoDeAnulacion.CanceladoPorElCliente;
}

public enum OrigenDeLaAnulacion
{
    Administracion,
    Cliente,
}

/// <summary>Por qué no se anula o no se cancela un pedido: la Aplicación lo traduce a un error estable con su alternativa (devolución, política de reembolso digital).</summary>
public enum RechazoDeAnulacion
{
    /// <summary>La administración no puede usar el motivo «cancelado por el cliente».</summary>
    MotivoNoCorresponde,

    PedidoYaAnulado,

    /// <summary>No queda ninguna impresa por salir (también un pedido solo digital): corresponde una devolución (PED-23).</summary>
    NoQuedaImpresaPorSalir,

    /// <summary>El pedido tiene una línea digital: el cliente sigue la política de reembolso digital (PED-18).</summary>
    IncluyeDigital,

    /// <summary>Un impreso ya está en tránsito, en preparación o es posterior: corresponde la devolución una vez entregado (PED-18).</summary>
    ImpresoYaEnCamino,
}

/// <summary>
/// Lo que se anula y se reembolsa: las líneas impresas que no salieron, las digitales (solo si ninguna impresa salió, D10), el envío que se devuelve (todo si ninguna impresa salió ni se anuló antes y, si no, el de las sucursales que no llegaron a enviar, PED-45), el monto —que lo incluye— y, en la cancelación del cliente, los traslados vinculados que
/// hay que cancelar (PED-17).
/// </summary>
public sealed record PlanDeAnulacion(
    OrigenDeLaAnulacion Origen,
    MotivoDeAnulacion Motivo,
    IReadOnlyList<Guid> LineasImpresas,
    IReadOnlyList<Guid> LineasDigitales,
    decimal Envio,
    decimal Monto,
    IReadOnlyList<Guid> TrasladosACancelar)
{
    public bool IncluyeEnvio => Envio > 0m;
}

public sealed class ResultadoDePlanDeAnulacion
{
    private ResultadoDePlanDeAnulacion(PlanDeAnulacion? plan, RechazoDeAnulacion? rechazo)
    {
        Plan = plan;
        Rechazo = rechazo;
    }

    public bool EsValido => Plan is not null;

    public PlanDeAnulacion? Plan { get; }

    public RechazoDeAnulacion? Rechazo { get; }

    internal static ResultadoDePlanDeAnulacion Con(PlanDeAnulacion plan) => new(plan, null);

    internal static ResultadoDePlanDeAnulacion Rechazado(RechazoDeAnulacion rechazo) => new(null, rechazo);
}

public sealed partial class Pedido
{
    /// <summary>
    /// Planea la anulación de la administración central (PED-13, PED-16, PED-22, PED-23, PED-27, D10): anula las impresas que no salieron (confirmadas, en tránsito, recibidas, en preparación, listas o no recogidas); <b>se rechaza si no queda ninguna</b> (también un pedido solo
    /// digital); las digitales disponibles se anulan solo si ninguna impresa salió; el envío se reembolsa solo si ninguna impresa salió ni se anuló antes. <paramref name="lineasQueSalieron"/> son las líneas que Envíos mantuvo o el Inventario reveló ya cerradas (un 409): se tratan como
    /// salidas aunque el pedido aún no lo refleje, y el plan se recalcula sin ellas, sin el envío y sin las digitales.
    /// </summary>
    public ResultadoDePlanDeAnulacion PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion motivo, IReadOnlySet<Guid>? lineasQueSalieron = null)
    {
        if (!Enum.IsDefined(motivo) || !MotivosDeAnulacion.EsDeLaAdministracion(motivo))
        {
            return ResultadoDePlanDeAnulacion.Rechazado(RechazoDeAnulacion.MotivoNoCorresponde);
        }

        if (Estado == EstadoDePedido.Anulado)
        {
            return ResultadoDePlanDeAnulacion.Rechazado(RechazoDeAnulacion.PedidoYaAnulado);
        }

        EstadoDeLineaImpresa Efectivo(LineaDePedido l) => lineasQueSalieron is not null && lineasQueSalieron.Contains(l.Id) ? EstadoDeLineaImpresa.Despachado : l.EstadoImpreso!.Value;
        var impresas = _lineas.Where(l => !l.EsDigital).ToList();
        var porSalir = impresas.Where(l => Efectivo(l).SeAnula()).ToList();
        if (porSalir.Count == 0)
        {
            return ResultadoDePlanDeAnulacion.Rechazado(RechazoDeAnulacion.NoQuedaImpresaPorSalir);
        }

        var algunaSalio = impresas.Any(l => Efectivo(l).Salio());
        var digitales = algunaSalio ? [] : _lineas.Where(l => l.EsDigital && l.EstadoDigital == EstadoDeLineaDigital.Disponible).ToList();
        var envio = impresas.All(l => Efectivo(l).SeAnula())
            ? CostoDeEnvio
            : EnvioDeLasSucursalesQueNoEnviaron(impresas, Efectivo);
        var monto = porSalir.Sum(l => l.Importe) + digitales.Sum(l => l.Importe) + envio;
        return ResultadoDePlanDeAnulacion.Con(new PlanDeAnulacion(
            OrigenDeLaAnulacion.Administracion, motivo, [.. porSalir.Select(l => l.Id)], [.. digitales.Select(l => l.Id)], envio, monto, []));
    }

    /// <summary>
    /// PED-45: una sucursal <b>no llegó a enviar</b> si es la fuente de alguna línea impresa y ninguna de las suyas salió (contando las que Envíos mantuvo o el Inventario reveló cerradas, que <paramref name="efectivo"/> ya da por despachadas) ni la anuló una operación anterior, que ya habría devuelto su envío. El importe de una sucursal sin líneas no
    /// se devuelve y un pedido sin detalle por sucursal no devuelve envío cuando algo salió.
    /// </summary>
    private decimal EnvioDeLasSucursalesQueNoEnviaron(IReadOnlyList<LineaDePedido> impresas, Func<LineaDePedido, EstadoDeLineaImpresa> efectivo)
    {
        var sinEnviar = impresas
            .GroupBy(l => SucursalDeLaLinea(l)!, StringComparer.Ordinal)
            .Where(g => g.All(l => efectivo(l).SeAnula()))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        return EnviosPorSucursal?.Where(e => sinEnviar.Contains(e.CodigoDeSucursal)).Sum(e => e.Importe) ?? 0m;
    }

    /// <summary>
    /// Planea la cancelación del cliente (PED-17, PED-18): solo un pedido formado <b>solo por impresas, todas confirmadas</b> (con o sin un traslado aprobado, pero no en tránsito ni posteriores), con el motivo «cancelado por el cliente»; reembolsa el total, incluido el envío, y
    /// pide cancelar sus traslados vinculados. Con una digital o con un impreso ya en camino se rechaza indicando la alternativa.
    /// </summary>
    public ResultadoDePlanDeAnulacion PlanearCancelacionDelCliente()
    {
        if (Estado == EstadoDePedido.Anulado)
        {
            return ResultadoDePlanDeAnulacion.Rechazado(RechazoDeAnulacion.PedidoYaAnulado);
        }

        if (_lineas.Any(l => l.EsDigital))
        {
            return ResultadoDePlanDeAnulacion.Rechazado(RechazoDeAnulacion.IncluyeDigital);
        }

        if (_lineas.Any(l => l.EstadoImpreso != EstadoDeLineaImpresa.Confirmado))
        {
            return ResultadoDePlanDeAnulacion.Rechazado(RechazoDeAnulacion.ImpresoYaEnCamino);
        }

        return ResultadoDePlanDeAnulacion.Con(new PlanDeAnulacion(
            OrigenDeLaAnulacion.Cliente,
            MotivoDeAnulacion.CanceladoPorElCliente,
            [.. _lineas.Select(l => l.Id)],
            [],
            CostoDeEnvio,
            Total,
            [.. _lineas.Where(l => l.TrasladoId is not null).Select(l => l.TrasladoId!.Value)]));
    }

    /// <summary>
    /// Aplica el plan: pasa a anuladas las líneas impresas y digitales del plan que todavía se pueden anular y recalcula el pedido en la misma operación (fija la fecha final si queda anulado). Devuelve cuántas líneas cambiaron; repetirlo no cambia nada.
    /// </summary>
    public int AplicarAnulacion(PlanDeAnulacion plan, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var cambiadas = 0;
        // Las digitales solo se anulan si ninguna impresa salió (D10), aunque el plan las traiga.
        var algunaSalio = _lineas.Any(l => l.EstadoImpreso is { } e && e.Salio());
        foreach (var linea in _lineas)
        {
            if (plan.LineasImpresas.Contains(linea.Id) && linea.PasarA(EstadoDeLineaImpresa.Anulado, ahora))
            {
                cambiadas++;
            }
            else if (!algunaSalio && plan.LineasDigitales.Contains(linea.Id) && linea.AnularDigital(ahora))
            {
                cambiadas++;
            }
        }

        if (cambiadas > 0)
        {
            RecalcularEstado(ahora);
        }

        return cambiadas;
    }
}
