using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>Una fila de la tabla de stock tal como se lee: físico y reservado de una pareja (fuente, edición).</summary>
public sealed record FilaDeStock(Guid FuenteId, Guid EdicionId, int Fisico, int Reservado);

/// <summary>Cómo terminó la escritura de una fuente: la restricción única de código o de nombre es la última defensa frente a dos altas simultáneas.</summary>
public enum ResultadoDeEscrituraDeFuente
{
    Escrita,
    CodigoRepetido,
    NombreRepetido,
}

/// <summary>Cómo terminó el guardado de la ficha de una edición.</summary>
public enum ResultadoDeGuardadoDeFicha
{
    Guardada,

    /// <summary>La edición tiene stock y la clave compuesta de formato impidió el cambio (la última defensa de «una digital nunca tiene stock»).</summary>
    FormatoEnConflicto,
}

/// <summary>Repositorio de fuentes (esquema <c>fuentes</c>). Las escrituras corren en la transacción del caso de uso; su restricción única es la última defensa.</summary>
public interface IRepositorioDeFuentes
{
    Task<Fuente?> PorId(Guid fuenteId, CancellationToken cancelacion = default);

    Task<Fuente?> PorCodigo(string codigo, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Fuente>> Listar(CancellationToken cancelacion = default);

    Task<IReadOnlyList<Fuente>> DeLaRegion(string region, CancellationToken cancelacion = default);

    /// <summary>Cuántas fuentes hay (para decidir si la que se registra es la primera y, por tanto, la de recepción, FUE-19).</summary>
    Task<int> Contar(CancellationToken cancelacion = default);

    /// <summary>La fuente por su código, bloqueada para actualizar (<c>SELECT … FOR UPDATE</c>): dos ediciones simultáneas no se pisan.</summary>
    Task<Fuente?> PorCodigoParaActualizar(string codigo, CancellationToken cancelacion = default);

    /// <summary>Inserta la fuente nueva; un código o un nombre repetido (también por una alta simultánea) se devuelve como resultado, no como excepción.</summary>
    Task<ResultadoDeEscrituraDeFuente> Agregar(Fuente fuente, CancellationToken cancelacion = default);

    /// <summary>Actualiza nombre, región y contacto de la fuente (el código no cambia); un nombre repetido se devuelve como resultado.</summary>
    Task<ResultadoDeEscrituraDeFuente> Actualizar(Fuente fuente, CancellationToken cancelacion = default);

    /// <summary>
    /// Pasa la marca de recepción de la fuente anterior (si la hay) a la nueva: primero la quita y luego la pone, porque el índice único parcial no admite dos a la
    /// vez ni siquiera dentro de la misma sentencia.
    /// </summary>
    Task CambiarRecepcion(Guid? anteriorId, Guid nuevaId, CancellationToken cancelacion = default);
}

/// <summary>Repositorio de rutas entre fuentes.</summary>
public interface IRepositorioDeRutas
{
    Task<RutaEntreFuentes?> Obtener(Guid origenId, Guid destinoId, CancellationToken cancelacion = default);

    Task<IReadOnlyList<RutaEntreFuentes>> HaciaDestino(Guid destinoId, CancellationToken cancelacion = default);

    /// <summary>Todas las rutas definidas.</summary>
    Task<IReadOnlyList<RutaEntreFuentes>> Todas(CancellationToken cancelacion = default);

    /// <summary>
    /// Crea o cambia la ruta y devuelve el plazo que tenía antes (<see langword="null"/> si no existía), leído bajo el bloqueo de la fila: con dos administradores
    /// simultáneos cada cambio ve el valor que de verdad reemplazó (FUE-14).
    /// </summary>
    Task<int?> Definir(RutaEntreFuentes ruta, CancellationToken cancelacion = default);
}

/// <summary>Repositorio de las fichas de edición (solo el formato).</summary>
public interface IRepositorioDeFichas
{
    Task<FichaDeEdicion?> PorId(Guid edicionId, CancellationToken cancelacion = default);

    /// <summary>Las fichas que existen de esas ediciones (las que no tienen ficha no figuran).</summary>
    Task<IReadOnlyList<FichaDeEdicion>> PorIds(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>Guarda la ficha (alta o cambio de formato). El conflicto con el stock existente se devuelve como resultado.</summary>
    Task<ResultadoDeGuardadoDeFicha> Guardar(FichaDeEdicion ficha, CancellationToken cancelacion = default);

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

    /// <summary><see langword="true"/> si la edición tiene físico o reservado distinto de cero en alguna fuente (una fila de ceros no cuenta). Con alcance de red: la Aplicación ya autorizó.</summary>
    Task<bool> HayExistencias(Guid edicionId, CancellationToken cancelacion = default);
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
