using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>Una fila de la tabla de stock tal como se lee: físico y reservado de una pareja (fuente, edición).</summary>
public sealed record FilaDeStock(Guid FuenteId, Guid EdicionId, int Fisico, int Reservado);

/// <summary>Repositorio de fuentes (esquema <c>fuentes</c>). Aquí solo la lectura; las escrituras llegan con los casos de uso que las necesitan.</summary>
public interface IRepositorioDeFuentes
{
    Task<Fuente?> PorId(Guid fuenteId, CancellationToken cancelacion = default);

    Task<Fuente?> PorCodigo(string codigo, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Fuente>> Listar(CancellationToken cancelacion = default);

    Task<IReadOnlyList<Fuente>> DeLaRegion(string region, CancellationToken cancelacion = default);
}

/// <summary>Repositorio de rutas entre fuentes.</summary>
public interface IRepositorioDeRutas
{
    Task<RutaEntreFuentes?> Obtener(Guid origenId, Guid destinoId, CancellationToken cancelacion = default);

    Task<IReadOnlyList<RutaEntreFuentes>> HaciaDestino(Guid destinoId, CancellationToken cancelacion = default);
}

/// <summary>Repositorio de las fichas de edición (solo el formato).</summary>
public interface IRepositorioDeFichas
{
    Task<FichaDeEdicion?> PorId(Guid edicionId, CancellationToken cancelacion = default);

    /// <summary><see langword="true"/> si todas las ediciones tienen ficha y todas son impresas (las únicas que tienen stock).</summary>
    Task<bool> SonTodasImpresas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}

/// <summary>
/// Repositorio de la tabla de stock, de la que <c>Fuentes</c> es dueña. Cada operación de escritura es una sola sentencia. Quién la usa y con qué
/// alcance se decide aquí: las que dicen «con alcance de red» las implementa el adaptador de Infraestructura con la elevación (D09); la Aplicación
/// no puede elevar el alcance por su cuenta.
/// </summary>
public interface IRepositorioDeExistencias
{
    /// <summary>Crea con ceros la fila de cada fuente para cada edición que no la tenga (<c>INSERT … ON CONFLICT DO NOTHING</c>, en orden edición, fuente). Con alcance de red.</summary>
    Task GarantizarFilas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>Bloquea (<c>SELECT … FOR UPDATE</c>) las filas de <b>todas las fuentes</b> de esas ediciones en orden (edición, fuente) y las devuelve en ese orden. Con alcance de red.</summary>
    Task<IReadOnlyList<FilaDeStock>> BloquearFilas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>
    /// La primitiva única de escritura: un <c>UPDATE</c> condicional con la regla <c>0 ≤ reservado ≤ físico</c> y las cantidades anterior y posterior. Con el
    /// alcance del actor: si no afecta filas, comprueba con elevación si la fila existe pero el actor no la ve y responde «denegado por alcance».
    /// </summary>
    Task<ResultadoDeAplicacion> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion = default);

    /// <summary>La misma primitiva, con alcance de red, para los deltas de las liberaciones de vencidas de <c>Bloquear</c> (una acción del sistema ya auditada).</summary>
    Task<ResultadoDeAplicacion> AplicarLiberacionDeVencidas(MovimientoDeStock movimiento, CancellationToken cancelacion = default);

    /// <summary>Las filas de esas ediciones en todas las fuentes, sin bloquear (para leer en una instantánea). Con alcance de red: la Aplicación ya autorizó.</summary>
    Task<IReadOnlyList<FilaDeStock>> Leer(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>Todas las filas de una fuente, opcionalmente solo las de esas ediciones, ordenadas por edición. Con alcance de red: la Aplicación ya autorizó.</summary>
    Task<IReadOnlyList<FilaDeStock>> LeerDeLaFuente(Guid fuenteId, IReadOnlyCollection<Guid>? edicionIds, CancellationToken cancelacion = default);
}

/// <summary>
/// <para>
/// El orden único de bloqueo de las parejas (edición, fuente): el mismo que usan las sentencias <c>ORDER BY edicion_id, fuente_id</c> de PostgreSQL. PostgreSQL
/// ordena un <c>uuid</c> por sus bytes en orden canónico, que es el de su texto hexadecimal en minúsculas; <see cref="Guid.CompareTo(Guid)"/> compara los
/// mismos campos sin signo y de izquierda a derecha, así que coincide. Una prueba lo comprueba contra el texto para que un cambio del tipo no lo rompa en silencio.
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
