namespace BabelHub.Inventario.Application.Fuentes;

/// <summary>
/// <para>
/// El orden único de bloqueo de las parejas (edición, fuente): el mismo que usan las sentencias <c>ORDER BY edicion_id, fuente_id</c> de PostgreSQL. PostgreSQL
/// ordena un <c>uuid</c> por sus bytes en orden canónico, que es el de su texto hexadecimal en minúsculas; <see cref="Guid.CompareTo(Guid)"/> compara los
/// mismos campos sin signo y de izquierda a derecha, así que coincide. Una prueba lo comprueba contra el texto para que un cambio del tipo no lo rompa en silencio.
/// </para>
/// <para>
/// Es parte del contrato público de <c>Fuentes</c> porque <c>Reservas</c>, <c>Ajustes</c> y <c>Traslados</c> aplican sus movimientos en este mismo orden (D04).
/// </para>
/// </summary>
public static class OrdenDeBloqueo
{
    public static IComparer<Guid> DeIdentificadores { get; } = Comparer<Guid>.Create(Comparar);

    public static int Comparar(Guid a, Guid b) => a.CompareTo(b);

    /// <summary>Compara dos parejas por (edición, fuente).</summary>
    public static int Comparar(Guid edicionA, Guid fuenteA, Guid edicionB, Guid fuenteB)
    {
        var porEdicion = Comparar(edicionA, edicionB);
        return porEdicion != 0 ? porEdicion : Comparar(fuenteA, fuenteB);
    }
}
