namespace BabelHub.Inventario.Application.Ajustes;

// El contrato público del módulo Ajustes (la raíz de su espacio de nombres): las entradas y salidas de sus casos de uso, que la Api llama por ICasoDeUso<TEntrada, TSalida> sin tocar el Interno (regla 13, como
// Fuentes y Reservas), y el detalle de los rechazos que viajan en ErrorDeAplicacion.Errores. Son modelos propios: ni DTOs de Shared ni tipos del Dominio. La nota y la referencia son texto libre de un empleado:
// ningún tipo que las lleve las imprime en su ToString, para que un registro de diagnóstico nunca las vuelque (AUD-06, plan §6). No se agregó una fachada IAjustesDeStock: ningún otro módulo usa estos casos
// (el único puerto de salida, IReingresosPorDevolucion, es de Fuentes); si Traslados o Devoluciones la pidieran, se agrega.

/// <summary>Registra un ajuste manual (AJU-01 a AJU-09, AJU-12, AJU-15, AJU-18, AJU-22): <see cref="Motivo"/> es merma, danio o correccion-de-conteo; el ingreso y la devolución tienen su operación.</summary>
public sealed record RegistrarAjusteManualEntrada(string CodigoDeFuente, Guid EdicionId, string Motivo, int? Cantidad, int? ConteoReal, string? Nota, string ClaveDeIdempotencia)
{
    public override string ToString() => nameof(RegistrarAjusteManualEntrada);
}

/// <summary>Un ajuste ya aplicado, o uno del historial (AJU-01, AJU-17): <see cref="UsuarioId"/> es nulo solo tras la depuración de los 2 años (AJU-21).</summary>
public sealed record AjusteRegistrado(
    Guid AjusteId,
    string CodigoDeFuente,
    Guid EdicionId,
    string Motivo,
    Guid? UsuarioId,
    DateTimeOffset Instante,
    int FisicoAnterior,
    int FisicoPosterior,
    string? Nota,
    string? Referencia)
{
    public override string ToString() => $"{nameof(AjusteRegistrado)} {{ AjusteId = {AjusteId} }}";
}

/// <summary>Registra un ingreso de mercadería en la fuente de recepción (AJU-13, AJU-14, AJU-19): solo la administración central; <see cref="CodigoDeFuente"/> ausente es la recepción.</summary>
public sealed record RegistrarIngresoDeMercaderiaEntrada(Guid EdicionId, string? CodigoDeFuente, int Cantidad, string? Referencia, string? Nota, string ClaveDeIdempotencia)
{
    public override string ToString() => nameof(RegistrarIngresoDeMercaderiaEntrada);
}

/// <summary>Un ingreso de mercadería: el del ajuste con motivo <c>ingreso-de-mercaderia</c> que se creó con él y el físico anterior y posterior de la fila.</summary>
public sealed record IngresoRegistrado(
    Guid IngresoId,
    Guid AjusteId,
    Guid EdicionId,
    string CodigoDeFuente,
    int Cantidad,
    string? Referencia,
    string? Nota,
    Guid? UsuarioId,
    DateTimeOffset Instante,
    int FisicoAnterior,
    int FisicoPosterior)
{
    public override string ToString() => $"{nameof(IngresoRegistrado)} {{ IngresoId = {IngresoId} }}";
}

/// <summary>Pide los últimos ingresos de mercadería (AJU-20), del más reciente al más antiguo: <see cref="Limite"/> de 1 a 50 y, si se indica, de una edición. Solo la administración central.</summary>
public sealed record ConsultarIngresosEntrada(Guid? EdicionId, int Limite);

/// <summary>Un ingreso de la lista de AJU-20: edición, cantidad, referencia, nota, usuario (nulo tras la depuración) y fecha.</summary>
public sealed record IngresoListado(Guid IngresoId, Guid EdicionId, string CodigoDeFuente, int Cantidad, string? Referencia, string? Nota, Guid? UsuarioId, DateTimeOffset Instante)
{
    public override string ToString() => $"{nameof(IngresoListado)} {{ IngresoId = {IngresoId} }}";
}

/// <summary>Los últimos ingresos de mercadería.</summary>
public sealed record IngresosConsultados(IReadOnlyList<IngresoListado> Ingresos);

/// <summary>Una línea de una devolución aceptada: la edición y las unidades que vuelven al stock.</summary>
public sealed record LineaDeReingreso(Guid EdicionId, int Cantidad);

/// <summary>
/// Reingresa los impresos de una devolución aceptada a la sucursal receptora (AJU-11, AJU-16, AJU-18): el reingreso es idempotente por devolución y edición; <see cref="PedidoId"/> es obligatorio para que las
/// unidades vendidas resten lo devuelto (<c>inventario-seleccion-fuente</c> D13). Como máximo 20 líneas con ediciones únicas.
/// </summary>
public sealed record ReingresarPorDevolucionEntrada(Guid DevolucionId, Guid PedidoId, string CodigoDeFuente, IReadOnlyList<LineaDeReingreso> Lineas);

/// <summary>El resultado de un reingreso: <see cref="YaAplicado"/> es verdadero si esa devolución ya se reingresó y no se sumó nada; los ajustes son los de sus líneas, nuevos o ya existentes.</summary>
public sealed record ReingresoRegistrado(Guid DevolucionId, bool YaAplicado, IReadOnlyList<AjusteRegistrado> Ajustes);

/// <summary>Pide el historial de ajustes de una fuente (AJU-17): del más reciente al más antiguo, con tamaño de página de 1 a 100. El encargado de esa fuente o la administración central.</summary>
public sealed record ConsultarHistorialDeAjustesEntrada(string CodigoDeFuente, int Pagina, int Tamano);

/// <summary>Una página del historial de ajustes y el total de ajustes de la fuente.</summary>
public sealed record PaginaDeAjustes(IReadOnlyList<AjusteRegistrado> Elementos, int Total, int Pagina, int Tamano);

// ---------- Cómo viajan los rechazos con datos dentro de ErrorDeAplicacion.Errores ----------

/// <summary>
/// El detalle de una merma o un daño rechazados por superar el vendible (AJU-04): el físico, lo reservado y el vendible de la fila ya bloqueada, que la Api devuelve en el 409. Ningún dato de personas.
/// </summary>
public static class DetalleDeStockInsuficienteDeAjuste
{
    private const string Fisico = "fisico";

    private const string Reservado = "reservado";

    private const string Vendible = "vendible";

    public static IReadOnlyList<Plataforma.ErrorDeCampo> Codificar(int fisico, int reservado, int vendible) =>
    [
        new Plataforma.ErrorDeCampo(Fisico, fisico.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Plataforma.ErrorDeCampo(Reservado, reservado.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Plataforma.ErrorDeCampo(Vendible, vendible.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];

    public static bool TryLeer(IReadOnlyList<Plataforma.ErrorDeCampo> errores, out (int Fisico, int Reservado, int Vendible) detalle)
    {
        ArgumentNullException.ThrowIfNull(errores);
        detalle = default;
        if (Leer(errores, Fisico) is not { } fisico || Leer(errores, Reservado) is not { } reservado || Leer(errores, Vendible) is not { } vendible)
        {
            return false;
        }

        detalle = (fisico, reservado, vendible);
        return true;
    }

    private static int? Leer(IReadOnlyList<Plataforma.ErrorDeCampo> errores, string campo) =>
        int.TryParse(errores.FirstOrDefault(e => e.Campo == campo)?.Clave, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var valor) ? valor : null;
}

/// <summary>
/// El detalle de un conteo menor que lo reservado (AJU-06): los primeros pedidos que reservan las unidades (identificadores opacos, sin datos de clientes) y el total. Un <c>ErrorDeCampo</c> <c>pedido</c> por cada uno y
/// uno <c>total</c>.
/// </summary>
public static class DetalleDeConteoMenorQueReservas
{
    private const string Pedido = "pedido";

    private const string Total = "total";

    public static IReadOnlyList<Plataforma.ErrorDeCampo> Codificar(IReadOnlyList<Guid> pedidoIds, int total) =>
    [
        .. pedidoIds.Select(p => new Plataforma.ErrorDeCampo(Pedido, p.ToString("D"))),
        new Plataforma.ErrorDeCampo(Total, total.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];

    public static bool TryLeer(IReadOnlyList<Plataforma.ErrorDeCampo> errores, out IReadOnlyList<Guid> pedidoIds, out int total)
    {
        ArgumentNullException.ThrowIfNull(errores);
        pedidoIds = [.. errores.Where(e => e.Campo == Pedido).Select(e => Guid.TryParse(e.Clave, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
        return int.TryParse(errores.FirstOrDefault(e => e.Campo == Total)?.Clave, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out total);
    }
}

/// <summary>Cuánto esperar tras superar el presupuesto del personal (SEG-30) y desde cuándo reintentar: la misma respuesta uniforme de los demás límites.</summary>
public sealed record RechazoDePresupuestoDelPersonal(string Motivo, int EsperaEnSegundos, DateTimeOffset DisponibleDesde);

/// <summary>Cómo viaja el rechazo por el presupuesto <c>ajustes-del-personal</c> dentro de <c>ErrorDeAplicacion.Errores</c> (categoría <c>Limite</c>, clave <c>limite-excedido</c>): motivo, espera e instante ISO 8601 UTC.</summary>
public static class DetalleDeLimiteDeAjustes
{
    public const string ClaveDeLimiteExcedido = "limite-excedido";

    private const string Limite = "limite";

    private const string Espera = "esperaEnSegundos";

    private const string Desde = "disponibleDesde";

    private const string Formato = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static IReadOnlyList<Plataforma.ErrorDeCampo> Codificar(RechazoDePresupuestoDelPersonal rechazo)
    {
        ArgumentNullException.ThrowIfNull(rechazo);
        return
        [
            new Plataforma.ErrorDeCampo(Limite, rechazo.Motivo),
            new Plataforma.ErrorDeCampo(Espera, rechazo.EsperaEnSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Plataforma.ErrorDeCampo(Desde, rechazo.DisponibleDesde.ToUniversalTime().ToString(Formato, System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    public static bool TryLeer(IReadOnlyList<Plataforma.ErrorDeCampo> errores, out RechazoDePresupuestoDelPersonal? rechazo)
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

        rechazo = new RechazoDePresupuestoDelPersonal(motivo, segundos, instante);
        return true;
    }
}
