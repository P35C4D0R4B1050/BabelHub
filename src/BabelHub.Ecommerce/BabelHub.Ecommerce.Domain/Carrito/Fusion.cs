namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>Por qué la fusión ajustó una línea del visitante (CAR-24): lista cerrada, la que se muestra en el aviso (D15).</summary>
public enum MotivoDeAjusteDeFusion
{
    /// <summary>No hay unidades vendibles en la sucursal de la línea: no se suma nada.</summary>
    SinStock,

    /// <summary>La cuenta ya posee la edición digital (CAR-20): se descarta.</summary>
    YaPoseida,

    /// <summary>La edición dejó de venderse o ya no existe: se descarta.</summary>
    NoVendible,

    /// <summary>La suma pasaba del máximo disponible en la sucursal: queda en ese máximo.</summary>
    RecortadaAlMaximo,

    /// <summary>Una digital que ya estaba en el otro carrito: queda en una unidad (CAR-03).</summary>
    DigitalAUnaUnidad,

    /// <summary>El carrito de la cuenta ya tiene 30 líneas: la línea del visitante no entra (D14).</summary>
    TopeDeLineas,

    /// <summary>La suma pasaba de 20 unidades. Ya no se produce (una adición que no cabe abre otra línea, CAR-40): se conserva por los avisos guardados.</summary>
    TopeDeUnidades,
}

/// <summary>
/// Lo que el Catálogo dice de una edición al fusionar: si se vende, si es digital y, solo de un impreso, el vendible de cada sucursal (nulo si no se pudo confirmar: las líneas se aceptan, D03; una sucursal que no aparece no tiene
/// unidades).
/// </summary>
public sealed record EdicionParaFusion(Guid EdicionId, bool EsDigital, bool EsVendible, IReadOnlyDictionary<string, int>? VendiblePorSucursal);

/// <summary>Una línea del carrito que resulta de la fusión.</summary>
public sealed record LineaFusionada(Guid LineaId, Guid EdicionId, bool EsDigital, string? CodigoDeSucursal, Cantidad Cantidad);

/// <summary>Un ajuste de la fusión con su motivo y las unidades con que quedó la cuenta (en la sucursal de la línea, si es un impreso; 0 si no quedó).</summary>
public sealed record AjusteDeFusion(Guid EdicionId, MotivoDeAjusteDeFusion Motivo, int CantidadFinal);

public sealed record ResultadoDeFusion(IReadOnlyList<LineaFusionada> Lineas, IReadOnlyList<AjusteDeFusion> Ajustes);

/// <summary>
/// La regla de fusión del carrito de un visitante con el de la cuenta (CAR-24, CAR-40, AC-CAR-22, AC-CAR-33) como <b>función pura</b>: cada línea del visitante se agrega como una adición (suma a la línea más antigua de esa edición y
/// sucursal que la admita entera, o abre otra), recortada al vendible de su sucursal (contando las líneas que la cuenta ya tiene en ella) y al tope de 30 líneas; las digitales quedan en una unidad, se descartan las que la cuenta ya posee
/// o dejaron de venderse, y una línea anterior sin sucursal se conserva sin sumarla a otra (CAR-45). <b>Nunca quita ni reduce lo que la cuenta ya tenía</b>. Las líneas de la cuenta conservan su orden y las nuevas del visitante van después,
/// en su orden; una edición que el Catálogo no conoce se trata como no vendible.
/// </summary>
public static class Fusion
{
    public static ResultadoDeFusion Fusionar(
        IReadOnlyList<LineaDeCarrito> deLaCuenta,
        IReadOnlyList<LineaDeCarrito> delVisitante,
        IReadOnlyDictionary<Guid, EdicionParaFusion> ediciones,
        IReadOnlySet<Guid> poseidasPorLaCuenta)
    {
        ArgumentNullException.ThrowIfNull(deLaCuenta);
        ArgumentNullException.ThrowIfNull(delVisitante);
        ArgumentNullException.ThrowIfNull(ediciones);
        ArgumentNullException.ThrowIfNull(poseidasPorLaCuenta);

        // Las líneas se arman como en el carrito para aplicar la misma regla de adición (CAR-40); lo que la cuenta ya tenía va primero.
        var lineas = deLaCuenta.ToList();
        var ajustes = new List<AjusteDeFusion>();

        foreach (var aporte in delVisitante)
        {
            ediciones.TryGetValue(aporte.EdicionId, out var edicion);
            if (edicion is null || !edicion.EsVendible)
            {
                ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.NoVendible, UnidadesDe(lineas, aporte)));
                continue;
            }

            if (aporte.EsDigital)
            {
                FusionarDigital(aporte, poseidasPorLaCuenta, lineas, ajustes);
                continue;
            }

            FusionarImpresa(aporte, edicion, lineas, ajustes);
        }

        return new ResultadoDeFusion([.. lineas.Select(l => new LineaFusionada(l.LineaId, l.EdicionId, l.EsDigital, l.CodigoDeSucursal, l.Cantidad))], ajustes);
    }

    /// <summary>Las unidades que la cuenta tiene de esa línea del visitante: de la digital, o de la edición en su sucursal.</summary>
    private static int UnidadesDe(List<LineaDeCarrito> lineas, LineaDeCarrito aporte) =>
        lineas.Where(l => l.EdicionId == aporte.EdicionId && !l.RetiroPendiente && l.CodigoDeSucursal == aporte.CodigoDeSucursal).Sum(l => l.Cantidad.Valor);

    private static void FusionarDigital(LineaDeCarrito aporte, IReadOnlySet<Guid> poseidas, List<LineaDeCarrito> lineas, List<AjusteDeFusion> ajustes)
    {
        var yaEnLaCuenta = lineas.Exists(l => l.EdicionId == aporte.EdicionId);
        if (poseidas.Contains(aporte.EdicionId))
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.YaPoseida, yaEnLaCuenta ? 1 : 0));
            return;
        }

        if (yaEnLaCuenta)
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.DigitalAUnaUnidad, 1));
            return;
        }

        AgregarLinea(aporte, Cantidad.Uno, lineas, ajustes);
    }

    private static void FusionarImpresa(LineaDeCarrito aporte, EdicionParaFusion edicion, List<LineaDeCarrito> lineas, List<AjusteDeFusion> ajustes)
    {
        // Una línea anterior sin sucursal no se puede medir contra ningún stock ni sumar a otra: se conserva tal cual (CAR-45).
        if (aporte.CodigoDeSucursal is not { } sucursal)
        {
            AgregarLinea(aporte, aporte.Cantidad, lineas, ajustes);
            return;
        }

        var yaHay = UnidadesDe(lineas, aporte);
        int? maximo = edicion.VendiblePorSucursal is { } porSucursal ? Math.Max(porSucursal.GetValueOrDefault(sucursal), 0) : null;
        var entran = maximo is { } m ? Math.Min(aporte.Cantidad.Valor, Math.Max(m - yaHay, 0)) : aporte.Cantidad.Valor;
        if (entran == 0)
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, maximo == 0 ? MotivoDeAjusteDeFusion.SinStock : MotivoDeAjusteDeFusion.RecortadaAlMaximo, yaHay));
            return;
        }

        if (Carrito.LineaQueAdmite(lineas, aporte.EdicionId, sucursal, entran) is { } indice)
        {
            lineas[indice] = lineas[indice].ConCantidad(Cantidad.Crear(lineas[indice].Cantidad.Valor + entran));
        }
        else if (!AgregarLinea(aporte, Cantidad.Crear(entran), lineas, ajustes))
        {
            return;
        }

        if (entran < aporte.Cantidad.Valor)
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.RecortadaAlMaximo, yaHay + entran));
        }
    }

    /// <summary>Abre una línea con la identidad de la del visitante, como la más nueva (al aplicarla recibe el instante de la fusión); con 30 líneas no entra y se avisa.</summary>
    private static bool AgregarLinea(LineaDeCarrito aporte, Cantidad cantidad, List<LineaDeCarrito> lineas, List<AjusteDeFusion> ajustes)
    {
        if (lineas.Count >= Carrito.MaximoDeLineas)
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.TopeDeLineas, UnidadesDe(lineas, aporte)));
            return false;
        }

        lineas.Add(LineaDeCarrito.Crear(aporte.LineaId, aporte.EdicionId, aporte.EsDigital, aporte.CodigoDeSucursal, cantidad, DateTimeOffset.MaxValue));
        return true;
    }
}
