namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>Por qué la fusión ajustó una línea del visitante (CAR-24): lista cerrada, la que se muestra en el aviso (D15).</summary>
public enum MotivoDeAjusteDeFusion
{
    /// <summary>No hay unidades vendibles en la red: no se suma nada.</summary>
    SinStock,

    /// <summary>La cuenta ya posee la edición digital (CAR-20): se descarta.</summary>
    YaPoseida,

    /// <summary>La edición dejó de venderse o ya no existe: se descarta.</summary>
    NoVendible,

    /// <summary>La suma pasaba del máximo disponible en la red: queda en ese máximo.</summary>
    RecortadaAlMaximo,

    /// <summary>Una digital que ya estaba en el otro carrito: queda en una unidad (CAR-03).</summary>
    DigitalAUnaUnidad,

    /// <summary>El carrito de la cuenta ya tiene 50 líneas: la línea del visitante no entra (D14).</summary>
    TopeDeLineas,

    /// <summary>La suma pasaba de 99 unidades: queda en 99 (D14).</summary>
    TopeDeUnidades,
}

/// <summary>Lo que el Catálogo dice de una edición al fusionar: si se vende, si es digital y, solo de un impreso, el máximo vendible en la red (nulo si no se pudo confirmar: la línea se acepta, D03).</summary>
public sealed record EdicionParaFusion(Guid EdicionId, bool EsDigital, bool EsVendible, int? MaximoDisponible);

/// <summary>Una línea del carrito que resulta de la fusión.</summary>
public sealed record LineaFusionada(Guid EdicionId, bool EsDigital, Cantidad Cantidad);

/// <summary>Un ajuste de la fusión con su motivo y la cantidad con que quedó la edición en la cuenta (0 si no quedó).</summary>
public sealed record AjusteDeFusion(Guid EdicionId, MotivoDeAjusteDeFusion Motivo, int CantidadFinal);

public sealed record ResultadoDeFusion(IReadOnlyList<LineaFusionada> Lineas, IReadOnlyList<AjusteDeFusion> Ajustes);

/// <summary>
/// La regla de fusión del carrito de un visitante con el de la cuenta (CAR-24, AC-CAR-22) como <b>función pura</b>: suma las cantidades de una misma edición hasta el máximo disponible (y 99), deja las digitales en una unidad,
/// descarta las que la cuenta ya posee o dejaron de venderse, aplica el tope de 50 líneas y devuelve los ajustes con su motivo. <b>Nunca quita ni reduce lo que la cuenta ya tenía</b>: solo limita lo que aporta el visitante.
/// Las líneas de la cuenta conservan su orden y las nuevas del visitante van después, en su orden; una edición que el Catálogo no conoce se trata como no vendible.
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

        var lineas = deLaCuenta.Select(l => new LineaFusionada(l.EdicionId, l.EsDigital, l.Cantidad)).ToList();
        var ajustes = new List<AjusteDeFusion>();

        foreach (var aporte in delVisitante)
        {
            var indice = lineas.FindIndex(l => l.EdicionId == aporte.EdicionId);
            var yaEnLaCuenta = indice >= 0 ? lineas[indice].Cantidad.Valor : 0;
            ediciones.TryGetValue(aporte.EdicionId, out var edicion);

            if (edicion is null || !edicion.EsVendible)
            {
                ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.NoVendible, yaEnLaCuenta));
                continue;
            }

            if (aporte.EsDigital)
            {
                FusionarDigital(aporte, yaEnLaCuenta, poseidasPorLaCuenta, lineas, ajustes);
                continue;
            }

            FusionarImpresa(aporte, edicion, yaEnLaCuenta, indice, lineas, ajustes);
        }

        // El tope de líneas: las de la cuenta ya estaban (no pasan de 50) y las nuevas del visitante entran en su orden hasta completarlo.
        var lineasDeLaCuenta = deLaCuenta.Count;
        var conTope = new List<LineaFusionada>(lineas.Count);
        var ajustesDeTope = new List<AjusteDeFusion>();
        for (var i = 0; i < lineas.Count; i++)
        {
            if (i >= lineasDeLaCuenta && conTope.Count >= Carrito.MaximoDeLineas)
            {
                ajustes.RemoveAll(a => a.EdicionId == lineas[i].EdicionId);
                ajustesDeTope.Add(new AjusteDeFusion(lineas[i].EdicionId, MotivoDeAjusteDeFusion.TopeDeLineas, 0));
                continue;
            }

            conTope.Add(lineas[i]);
        }

        // Los ajustes siguen el orden de las líneas del visitante: el del tope (que no se descubre hasta el final) se coloca donde estaba la línea.
        var todos = ajustes.Concat(ajustesDeTope).OrderBy(a => IndiceEnElVisitante(delVisitante, a.EdicionId)).ToList();
        return new ResultadoDeFusion(conTope, todos);
    }

    private static int IndiceEnElVisitante(IReadOnlyList<LineaDeCarrito> delVisitante, Guid edicionId)
    {
        for (var i = 0; i < delVisitante.Count; i++)
        {
            if (delVisitante[i].EdicionId == edicionId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static void FusionarDigital(LineaDeCarrito aporte, int yaEnLaCuenta, IReadOnlySet<Guid> poseidas, List<LineaFusionada> lineas, List<AjusteDeFusion> ajustes)
    {
        if (poseidas.Contains(aporte.EdicionId))
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.YaPoseida, yaEnLaCuenta));
            return;
        }

        if (yaEnLaCuenta > 0)
        {
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, MotivoDeAjusteDeFusion.DigitalAUnaUnidad, 1));
            return;
        }

        lineas.Add(new LineaFusionada(aporte.EdicionId, true, Cantidad.Uno));
    }

    private static void FusionarImpresa(LineaDeCarrito aporte, EdicionParaFusion edicion, int yaEnLaCuenta, int indice, List<LineaFusionada> lineas, List<AjusteDeFusion> ajustes)
    {
        var suma = yaEnLaCuenta + aporte.Cantidad.Valor;
        var maximo = edicion.MaximoDisponible;
        var limite = maximo is { } m && m < Cantidad.Maxima ? Math.Max(m, 0) : Cantidad.Maxima;
        var final = Math.Max(yaEnLaCuenta, Math.Min(suma, limite));

        if (final < suma)
        {
            var motivo = limite == Cantidad.Maxima && (maximo is null || maximo >= Cantidad.Maxima)
                ? MotivoDeAjusteDeFusion.TopeDeUnidades
                : limite == 0 ? MotivoDeAjusteDeFusion.SinStock : MotivoDeAjusteDeFusion.RecortadaAlMaximo;
            ajustes.Add(new AjusteDeFusion(aporte.EdicionId, motivo, final));
        }

        if (final == 0)
        {
            return;
        }

        var resultante = new LineaFusionada(aporte.EdicionId, false, Cantidad.Crear(final));
        if (indice >= 0)
        {
            lineas[indice] = resultante;
        }
        else
        {
            lineas.Add(resultante);
        }
    }
}
