namespace BabelHub.Inventario.Domain.Reservas;

/// <summary>Una línea impresa con la sucursal elegida para atenderla (<see cref="FuenteId"/>).</summary>
public sealed record LineaPorSucursal(Guid LineaId, Guid EdicionId, Guid FuenteId, int Cantidad);

/// <summary>Si la sucursal cubre la línea y el vendible que esa sucursal tenía de la edición antes de repartirlo entre las líneas de la solicitud.</summary>
public sealed record AsignacionDeLinea(LineaPorSucursal Linea, bool Cubierta, int VendibleEnLaSucursal);

/// <summary>
/// La asignación de la venta por sucursal (SEL-41, SEL-43, D17): cada línea se atiende solo en su sucursal, sin elegir otra fuente ni dividirse. Las líneas se recorren en el orden de la solicitud y cada una
/// cabe si entra en lo que dejan las anteriores <b>cubiertas</b> de la misma edición y sucursal; la que no cabe no consume. No pasa por <see cref="IEstrategiaDeSeleccion"/>.
/// </summary>
public static class AsignacionPorSucursal
{
    /// <summary>Una entrada por línea, en el orden recibido. Una pareja sin fila de stock (o con vendible negativo) vale cero.</summary>
    public static IReadOnlyList<AsignacionDeLinea> Asignar(IReadOnlyList<LineaPorSucursal> lineas, IReadOnlyDictionary<(Guid FuenteId, Guid EdicionId), int> vendible)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(vendible);
        var apartado = new Dictionary<(Guid FuenteId, Guid EdicionId), int>();
        var resultado = new List<AsignacionDeLinea>(lineas.Count);
        foreach (var linea in lineas)
        {
            if (linea.Cantidad <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineas), "La cantidad es mayor que cero.");
            }

            var pareja = (linea.FuenteId, linea.EdicionId);
            var enLaSucursal = Math.Max(0, vendible.GetValueOrDefault(pareja));
            var yaApartado = apartado.GetValueOrDefault(pareja);
            var cubierta = linea.Cantidad <= enLaSucursal - yaApartado;
            if (cubierta)
            {
                apartado[pareja] = yaApartado + linea.Cantidad;
            }

            resultado.Add(new AsignacionDeLinea(linea, cubierta, enLaSucursal));
        }

        return resultado;
    }
}
