namespace BabelHub.Inventario.Application.Traslados;

// El contrato público del módulo Traslados (la raíz de su espacio de nombres): las entradas y salidas de sus casos de uso, que la Api llama por ICasoDeUso<TEntrada, TSalida> sin tocar el Interno (regla 13, como
// Fuentes, Reservas y Ajustes), y el detalle de los rechazos que viajan en ErrorDeAplicacion.Errores. Son modelos propios: ni DTOs de Shared ni tipos del Dominio. Ningún otro módulo usa estos casos (los puertos que
// Traslados implementa para Reservas, IGeneradorDeTrasladosDePedido e IAlLiberarPedido, viven en la raíz de Reservas y no son alcanzables desde la Api). El motivo, el motivo de cierre y la guía son texto libre de
// un empleado: ningún tipo que los lleve los imprime en su ToString, para que un registro de diagnóstico nunca los vuelque (AUD-06, plan §6, D07).

/// <summary>Solicita un traslado de reposición (TRA-01, TRA-20): origen y destino por código, existentes y distintos. El encargado solo hacia su propia sucursal.</summary>
public sealed record SolicitarTrasladoEntrada(Guid EdicionId, int Cantidad, string CodigoDeOrigen, string CodigoDeDestino, string? Motivo)
{
    public override string ToString() => nameof(SolicitarTrasladoEntrada);
}

/// <summary>Aprueba un traslado solicitado (TRA-04): el origen o la administración central.</summary>
public sealed record AprobarTrasladoEntrada(Guid TrasladoId);

/// <summary>Rechaza un traslado solicitado, con motivo (TRA-05): nunca uno vinculado a un pedido (TRA-16).</summary>
public sealed record RechazarTrasladoEntrada(Guid TrasladoId, string Motivo)
{
    public override string ToString() => $"{nameof(RechazarTrasladoEntrada)} {{ TrasladoId = {TrasladoId} }}";
}

/// <summary>Cancela un traslado solicitado o aprobado no vinculado a un pedido, con motivo (TRA-18): el origen o la administración central.</summary>
public sealed record CancelarTrasladoEntrada(Guid TrasladoId, string Motivo)
{
    public override string ToString() => $"{nameof(CancelarTrasladoEntrada)} {{ TrasladoId = {TrasladoId} }}";
}

/// <summary>Despacha un traslado aprobado (TRA-06, TRA-07): el origen o la administración central.</summary>
public sealed record DespacharTrasladoEntrada(Guid TrasladoId);

/// <summary>Recibe un traslado en tránsito (TRA-08, TRA-14, TRA-17): el destino o la administración central.</summary>
public sealed record RecibirTrasladoEntrada(Guid TrasladoId);

/// <summary>Registra la guía de un traslado aprobado o en tránsito sin cambiar su estado (TRA-12): logística o la administración central.</summary>
public sealed record RegistrarGuiaDeTrasladoEntrada(Guid TrasladoId, string Guia)
{
    public override string ToString() => $"{nameof(RegistrarGuiaDeTrasladoEntrada)} {{ TrasladoId = {TrasladoId} }}";
}

/// <summary>Un traslado como lo ven las pantallas y la Api: el estado va como su identificador (<c>solicitado</c>, <c>aprobado</c>, <c>en-transito</c>, <c>recibido</c>, <c>rechazado</c> o <c>cancelado</c>).</summary>
public sealed record TrasladoRegistrado(
    Guid TrasladoId,
    long Numero,
    Guid EdicionId,
    string CodigoDeOrigen,
    string CodigoDeDestino,
    int Cantidad,
    string Estado,
    string? Motivo,
    string? MotivoDeCierre,
    Guid? PedidoId,
    bool EsVinculado,
    bool PedidoAnulado,
    DateOnly? FechaEstimadaDeLlegada,
    bool EstimadaPorConfirmar,
    string? Guia,
    Guid? SolicitadoPor,
    DateTimeOffset SolicitadoEn,
    DateTimeOffset? AprobadoEn,
    DateTimeOffset? DespachadoEn,
    DateTimeOffset? RecibidoEn,
    DateTimeOffset? CerradoEn,
    long Version)
{
    public override string ToString() => $"{nameof(TrasladoRegistrado)} {{ TrasladoId = {TrasladoId}, Estado = {Estado} }}";
}

/// <summary>Pide las fuentes que pueden ser el origen de un traslado de una edición hacia un destino (TRA-02): solo las que tienen vendible, sin cantidades.</summary>
public sealed record ConsultarOrigenesPosiblesEntrada(Guid EdicionId, string CodigoDeDestino);

/// <summary>Una fuente con vendible de la edición: solo su código y su nombre, nunca cuántas unidades tiene (TRA-02, plan §6).</summary>
public sealed record OrigenPosible(string Codigo, string Nombre);

/// <summary>Los orígenes posibles de una solicitud, los de ruta más corta primero y luego por código.</summary>
public sealed record OrigenesPosibles(IReadOnlyList<OrigenPosible> Origenes);

// ---------- Cómo viajan los rechazos con datos dentro de ErrorDeAplicacion.Errores ----------

/// <summary>Cuánto esperar tras superar el presupuesto del personal (SEG-30) y desde cuándo reintentar: la misma respuesta uniforme de los demás límites.</summary>
public sealed record RechazoDePresupuestoDeTraslados(string Motivo, int EsperaEnSegundos, DateTimeOffset DisponibleDesde);

/// <summary>Cómo viaja el rechazo por el presupuesto <c>traslados-del-personal</c> dentro de <c>ErrorDeAplicacion.Errores</c> (categoría <c>Limite</c>, clave <c>limite-excedido</c>): motivo, espera e instante ISO 8601 UTC.</summary>
public static class DetalleDeLimiteDeTraslados
{
    public const string ClaveDeLimiteExcedido = "limite-excedido";

    private const string Limite = "limite";

    private const string Espera = "esperaEnSegundos";

    private const string Desde = "disponibleDesde";

    private const string Formato = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static IReadOnlyList<Plataforma.ErrorDeCampo> Codificar(RechazoDePresupuestoDeTraslados rechazo)
    {
        ArgumentNullException.ThrowIfNull(rechazo);
        return
        [
            new Plataforma.ErrorDeCampo(Limite, rechazo.Motivo),
            new Plataforma.ErrorDeCampo(Espera, rechazo.EsperaEnSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Plataforma.ErrorDeCampo(Desde, rechazo.DisponibleDesde.ToUniversalTime().ToString(Formato, System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    public static bool TryLeer(IReadOnlyList<Plataforma.ErrorDeCampo> errores, out RechazoDePresupuestoDeTraslados? rechazo)
    {
        ArgumentNullException.ThrowIfNull(errores);
        rechazo = null;
        var motivo = errores.FirstOrDefault(e => e.Campo == Limite)?.Clave;
        var espera = errores.FirstOrDefault(e => e.Campo == Espera)?.Clave;
        var desde = errores.FirstOrDefault(e => e.Campo == Desde)?.Clave;
        if (motivo is null || !int.TryParse(espera, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var segundos)
            || !DateTimeOffset.TryParseExact(desde, Formato, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var instante))
        {
            return false;
        }

        rechazo = new RechazoDePresupuestoDeTraslados(motivo, segundos, instante);
        return true;
    }
}
