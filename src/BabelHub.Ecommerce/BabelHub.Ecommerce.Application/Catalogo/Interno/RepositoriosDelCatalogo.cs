using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

// Los repositorios del módulo (puertos de la Aplicación que implementa la Infraestructura del Catálogo sobre el esquema `catalogo`, con EjecutarComo(catalogo)). Todo acceso va dentro de una transacción:
// la del caso de uso o, en una lectura sin transacción (la consulta pública, la portada, la lista de ingresos), una transacción corta propia del adaptador o del llamador (`IUnidadDeTrabajo`). «ParaActualizar» es un bloqueo de fila (FOR UPDATE) que el adaptador toma; los dobles de prueba lo ignoran.

public enum ResultadoDeEscrituraDeObra
{
    Escrita,

    /// <summary>Alguno de los ISBN-13 ya existe en otra edición (índice único; <c>INSERT … ON CONFLICT DO NOTHING</c>): nada se escribió.</summary>
    IsbnDuplicado,

    /// <summary>La versión de la obra ya no es la que leyó quien edita (<c>UPDATE … WHERE version = @leida</c> afectó 0 filas).</summary>
    ConflictoDeVersion,
}

public interface IRepositorioDeObras
{
    Task<Obra?> Obtener(Guid obraId, bool paraActualizar, CancellationToken cancelacion = default);

    /// <summary>La obra que contiene la edición (también dada de baja), o <see langword="null"/>.</summary>
    Task<Obra?> ObtenerPorEdicion(Guid edicionId, bool paraActualizar, CancellationToken cancelacion = default);

    /// <summary>Inserta la obra con sus ediciones, licencias y archivos; el ISBN repetido se detecta por el índice único, no por una consulta previa (CAT-17, D02).</summary>
    Task<ResultadoDeEscrituraDeObra> Agregar(Obra obra, CancellationToken cancelacion = default);

    /// <summary>Guarda los cambios de la obra y de sus ediciones con la concurrencia optimista de su versión: <paramref name="versionLeida"/> es la que se cargó (la obra ya trae la nueva).</summary>
    Task<ResultadoDeEscrituraDeObra> Guardar(Obra obra, long versionLeida, CancellationToken cancelacion = default);

    /// <summary>El título de la obra cuya edición usa el ISBN-13, leído <b>después</b> del rechazo (una lectura nueva, sin una transacción abortada) y solo tras comprobar el rol.</summary>
    Task<string?> TituloDeLaEdicionConIsbn(string isbn13, CancellationToken cancelacion = default);
}

public interface IRepositorioDeReferenciasDeArchivo
{
    /// <summary>La referencia por su identificador; para consumirla se toma con <c>FOR UPDATE</c>, de modo que el consumo y el trabajo de depuración no compitan en el borde de las 24 horas.</summary>
    Task<ReferenciaDeArchivo?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default);

    Task Agregar(ReferenciaDeArchivo referencia, CancellationToken cancelacion = default);

    /// <summary>Guarda el cambio de estado de una referencia existente (<c>subiendo</c> → <c>lista</c> → <c>consumida</c>…).</summary>
    Task Guardar(ReferenciaDeArchivo referencia, CancellationToken cancelacion = default);

    /// <summary>
    /// Pasa a <c>descarte</c> el objeto reemplazado de una portada o de un libro (D05): un <b>upsert por la clave del almacén</b> —si la fila <c>consumida</c> existe pasa a <c>descarte</c> y, si ya se
    /// purgó, se inserta una <c>descarte</c> con el actor del reemplazo como usuario y el instante del reemplazo como creación y vencimiento—, para que el trabajo de fondo borre el objeto.
    /// </summary>
    Task EncolarDescarte(string claveEnAlmacen, FinalidadDeArchivo finalidad, Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Las filas que el trabajo de fondo debe depurar: <c>subiendo</c> más viejas que el margen, <c>lista</c> vencidas y <c>descarte</c>; con <c>FOR UPDATE SKIP LOCKED</c>.</summary>
    Task<IReadOnlyList<ReferenciaDeArchivo>> Depurables(DateTimeOffset ahora, TimeSpan margenDeSubida, int maximo, CancellationToken cancelacion = default);

    /// <summary>Elimina la fila (el objeto ya se borró del almacén); no toca el objeto.</summary>
    Task Eliminar(Guid id, CancellationToken cancelacion = default);
}

public interface IRepositorioDeIngresos
{
    /// <summary>
    /// Inserta la fila <c>Pendiente</c>. Devuelve <see langword="false"/> si ya hay un <c>Pendiente</c> de esa edición (índice único parcial) o si la clave de idempotencia ya existe (única):
    /// nada se escribió.
    /// </summary>
    Task<bool> Agregar(IngresoSolicitado ingreso, CancellationToken cancelacion = default);

    Task<IngresoSolicitado?> Obtener(Guid ingresoSolicitadoId, bool paraActualizar, CancellationToken cancelacion = default);

    /// <summary>La fila con esa clave de idempotencia (el nonce del token), de cualquier estado.</summary>
    Task<IngresoSolicitado?> PorClave(Guid claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary>El único <c>Pendiente</c> de la edición, si lo hay.</summary>
    Task<IngresoSolicitado?> PendienteDeLaEdicion(Guid edicionId, CancellationToken cancelacion = default);

    /// <summary>Guarda una transición: <b>condicional</b> (<c>WHERE estado = pendiente</c>); devuelve <see langword="false"/> si la fila ya no estaba pendiente (0 filas afectadas).</summary>
    Task<bool> GuardarTransicion(IngresoSolicitado ingreso, CancellationToken cancelacion = default);

    /// <summary>Los ingresos solicitados de la edición, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<IngresoSolicitado>> DeLaEdicion(Guid edicionId, int maximo, CancellationToken cancelacion = default);

    /// <summary>Los identificadores del Inventario que ya constan en otras filas <c>Registrado</c> (para no constatar dos veces el mismo ingreso).</summary>
    Task<IReadOnlySet<Guid>> IngresosYaConstatados(IReadOnlyCollection<Guid> ingresoIds, CancellationToken cancelacion = default);
}
