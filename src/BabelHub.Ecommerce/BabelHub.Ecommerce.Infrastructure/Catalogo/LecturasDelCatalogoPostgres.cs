using System.Text;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// La lectura del catálogo sobre PostgreSQL (T15; CAT-09, CAT-10, D08, D10): búsqueda con <c>catalogo.normalizar</c> (sin tildes ni mayúsculas) sobre los índices de trigramas, con <b>los comodines escapados por este adaptador</b> (<c>%</c>, <c>_</c>
/// y <c>\</c>, <c>LIKE … ESCAPE '\'</c>; la Aplicación pasa el texto tal como llega) y todo valor como parámetro. Orden fijo por título e identificador, para una paginación estable. Una obra cumple si <b>alguna edición no dada de baja</b>
/// cumple a la vez formato y precio. Cada lectura abre su propia transacción corta si no hay una en curso.
/// </summary>
public sealed class LecturasDelCatalogoPostgres(AccesoAlEsquemaDelCatalogo acceso) : ILecturasDelCatalogo
{
    private const string Orden = "ORDER BY o.titulo, o.id";

    public Task<PaginaDeObrasDeLaBase> Buscar(FiltrosDeObras filtros, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtros);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var (condicion, parametros) = Condiciones(filtros);
                var total = await Escalar<long>(conexion, transaccion, $"SELECT count(*) FROM catalogo.obras o WHERE {condicion}", parametros, token).ConfigureAwait(false);
                var ids = await Identificadores(conexion, transaccion, $"SELECT o.id FROM catalogo.obras o WHERE {condicion} {Orden} OFFSET @desplazamiento LIMIT @tamano", parametros.Concat(new[] { new NpgsqlParameter("desplazamiento", (pagina - 1) * tamano), new NpgsqlParameter("tamano", tamano) }), token).ConfigureAwait(false);
                var obras = await Cargar(conexion, transaccion, ids, token).ConfigureAwait(false);
                return new PaginaDeObrasDeLaBase(obras, (int)Math.Min(int.MaxValue, total));
            },
            cancelacion);
    }

    public Task<(int Obras, int Ediciones)> ContarCandidatas(FiltrosDeObras filtros, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtros);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var (condicion, parametros) = Condiciones(filtros);
                var (condicionDeEdicion, parametrosDeEdicion) = CondicionDeEdicion(filtros, "e");
                await using var comando = new NpgsqlCommand(
                    $"SELECT count(DISTINCT o.id), count(e.id) FROM catalogo.obras o JOIN catalogo.ediciones e ON e.obra_id = o.id WHERE {condicion} AND {condicionDeEdicion}",
                    conexion,
                    transaccion);
                comando.Parameters.AddRange(parametros.Concat(parametrosDeEdicion).Select(p => new NpgsqlParameter(p.ParameterName, p.Value)).ToArray());
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                await filas.ReadAsync(token).ConfigureAwait(false);
                return ((int)Math.Min(int.MaxValue, filas.GetInt64(0)), (int)Math.Min(int.MaxValue, filas.GetInt64(1)));
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Obra>> TodasLasQueCumplen(FiltrosDeObras filtros, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtros);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var (condicion, parametros) = Condiciones(filtros);
                var ids = await Identificadores(conexion, transaccion, $"SELECT o.id FROM catalogo.obras o WHERE {condicion} {Orden}", parametros, token).ConfigureAwait(false);
                return (IReadOnlyList<Obra>)await Cargar(conexion, transaccion, ids, token).ConfigureAwait(false);
            },
            cancelacion);
    }

    public Task<Obra?> ObraPorId(Guid obraId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(async (conexion, transaccion, token) => (await Cargar(conexion, transaccion, [obraId], token).ConfigureAwait(false)).FirstOrDefault(), cancelacion);

    public Task<IReadOnlyList<EdicionConObra>> EdicionesPorId(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var ids = edicionIds.Distinct().ToArray();
                var obraIds = await Identificadores(conexion, transaccion, "SELECT DISTINCT e.obra_id FROM catalogo.ediciones e WHERE e.id = ANY(@ids)", [new NpgsqlParameter("ids", ids)], token).ConfigureAwait(false);
                var obras = await Cargar(conexion, transaccion, obraIds, token).ConfigureAwait(false);
                return (IReadOnlyList<EdicionConObra>)ids
                    .Select(id => obras.FirstOrDefault(o => o.BuscarEdicion(id) is not null) is { } obra ? new EdicionConObra(obra, obra.BuscarEdicion(id)!) : null)
                    .OfType<EdicionConObra>()
                    .ToList();
            },
            cancelacion);
    }

    public Task<IReadOnlyList<Obra>> ObrasAlAzar(int cantidad, IReadOnlyCollection<Guid> excluidas, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var ids = await Identificadores(
                    conexion,
                    transaccion,
                    "SELECT o.id FROM catalogo.obras o WHERE o.estado = 'activa' AND NOT (o.id = ANY(@excluidas)) ORDER BY random() LIMIT @cantidad",
                    [new NpgsqlParameter("excluidas", excluidas.ToArray()), new NpgsqlParameter("cantidad", cantidad)],
                    token).ConfigureAwait(false);
                return (IReadOnlyList<Obra>)await Cargar(conexion, transaccion, ids, token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<PortadaDeObra?> PortadaPorHuella(HuellaDeArchivo huella, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(huella);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Solo la huella de las portadas de obras (también dadas de baja): jamás la de un libro.
                await using var comando = new NpgsqlCommand("SELECT portada_clave, portada_tipo FROM catalogo.obras WHERE portada_huella = @huella ORDER BY id LIMIT 1", conexion, transaccion);
                comando.Parameters.AddWithValue("huella", huella.ComoBytes());
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    return null;
                }

                TiposDeArchivo.TryDesdeNombre(filas.GetString(1), out var tipo);
                return new PortadaDeObra(filas.GetString(0), tipo, huella);
            },
            cancelacion);
    }

    public Task<PaginaDeObrasDeLaBase> BuscarParaAdministracion(string? texto, bool incluirDadasDeBaja, int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var condicion = new StringBuilder(incluirDadasDeBaja ? "TRUE" : "o.estado = 'activa'");
                var parametros = new List<NpgsqlParameter>();
                if (!string.IsNullOrEmpty(texto))
                {
                    condicion.Append(" AND (catalogo.normalizar(o.titulo) LIKE catalogo.normalizar(@patron) ESCAPE '\\' OR catalogo.normalizar(o.autor) LIKE catalogo.normalizar(@patron) ESCAPE '\\' OR catalogo.normalizar(o.editorial) LIKE catalogo.normalizar(@patron) ESCAPE '\\')");
                    parametros.Add(new NpgsqlParameter("patron", Patron(texto)));
                }

                var total = await Escalar<long>(conexion, transaccion, $"SELECT count(*) FROM catalogo.obras o WHERE {condicion}", parametros.ToArray(), token).ConfigureAwait(false);
                parametros.Add(new NpgsqlParameter("desplazamiento", (pagina - 1) * tamano));
                parametros.Add(new NpgsqlParameter("tamano", tamano));
                var ids = await Identificadores(conexion, transaccion, $"SELECT o.id FROM catalogo.obras o WHERE {condicion} {Orden} OFFSET @desplazamiento LIMIT @tamano", parametros.ToArray(), token).ConfigureAwait(false);
                return new PaginaDeObrasDeLaBase(await Cargar(conexion, transaccion, ids, token).ConfigureAwait(false), (int)Math.Min(int.MaxValue, total));
            },
            cancelacion);

    /// <summary>
    /// Escapa los comodines de <c>LIKE</c> (<c>\</c>, <c>%</c> y <c>_</c>) del texto que escribió la persona y lo envuelve en <c>%…%</c> ya normalizado en minúsculas y sin tildes por la base. El texto nunca se concatena al SQL: va como parámetro
    /// y la normalización la hace <c>catalogo.normalizar</c> en la propia consulta (<see cref="Patron"/> devuelve el texto escapado; la función se aplica al patrón en SQL).
    /// </summary>
    internal static string Patron(string texto) => "%" + EscaparComodines(texto) + "%";

    internal static string EscaparComodines(string texto) => texto.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static (string Condicion, List<NpgsqlParameter> Parametros) Condiciones(FiltrosDeObras f)
    {
        var condicion = new StringBuilder("o.estado = 'activa'");
        var parametros = new List<NpgsqlParameter>();
        if (!string.IsNullOrEmpty(f.Texto))
        {
            condicion.Append(" AND catalogo.normalizar(o.titulo) LIKE catalogo.normalizar(@texto) ESCAPE '\\'");
            parametros.Add(new NpgsqlParameter("texto", Patron(f.Texto)));
        }

        if (!string.IsNullOrEmpty(f.Autor))
        {
            condicion.Append(" AND catalogo.normalizar(o.autor) LIKE catalogo.normalizar(@autor) ESCAPE '\\'");
            parametros.Add(new NpgsqlParameter("autor", Patron(f.Autor)));
        }

        if (!string.IsNullOrEmpty(f.Editorial))
        {
            condicion.Append(" AND catalogo.normalizar(o.editorial) LIKE catalogo.normalizar(@editorial) ESCAPE '\\'");
            parametros.Add(new NpgsqlParameter("editorial", Patron(f.Editorial)));
        }

        if (!string.IsNullOrEmpty(f.Categoria))
        {
            condicion.Append(" AND catalogo.normalizar(o.categoria) = catalogo.normalizar(@categoria)");
            parametros.Add(new NpgsqlParameter("categoria", f.Categoria));
        }

        var (edicion, deEdicion) = CondicionDeEdicion(f, "e");
        condicion.Append(" AND EXISTS (SELECT 1 FROM catalogo.ediciones e WHERE e.obra_id = o.id AND ").Append(edicion).Append(')');
        parametros.AddRange(deEdicion);
        return (condicion.ToString(), parametros);
    }

    /// <summary>La edición no dada de baja que cumple formato y rango de precio (la misma edición cumple todo a la vez).</summary>
    private static (string Condicion, List<NpgsqlParameter> Parametros) CondicionDeEdicion(FiltrosDeObras f, string alias)
    {
        var condicion = new StringBuilder($"{alias}.estado <> 'baja'");
        var parametros = new List<NpgsqlParameter>();
        if (f.Formato is { } formato)
        {
            condicion.Append($" AND {alias}.tipo = @formato");
            parametros.Add(new NpgsqlParameter("formato", formato == FormatoDeEdicion.Digital ? "digital" : "impresa"));
        }

        if (f.PrecioMinimo is { } minimo)
        {
            condicion.Append($" AND {alias}.precio >= @precioMin");
            parametros.Add(new NpgsqlParameter("precioMin", minimo));
        }

        if (f.PrecioMaximo is { } maximo)
        {
            condicion.Append($" AND {alias}.precio <= @precioMax");
            parametros.Add(new NpgsqlParameter("precioMax", maximo));
        }

        return (condicion.ToString(), parametros);
    }

    private static async Task<T> Escalar<T>(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, IEnumerable<NpgsqlParameter> parametros, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        comando.Parameters.AddRange(parametros.Select(p => new NpgsqlParameter(p.ParameterName, p.Value)).ToArray());
        return (T)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
    }

    private static async Task<List<Guid>> Identificadores(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, IEnumerable<NpgsqlParameter> parametros, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        comando.Parameters.AddRange(parametros.Select(p => new NpgsqlParameter(p.ParameterName, p.Value)).ToArray());
        var ids = new List<Guid>();
        await using var filas = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await filas.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            ids.Add(filas.GetGuid(0));
        }

        return ids;
    }

    /// <summary>Carga las obras con todas sus ediciones y conserva el orden de los identificadores (el orden fijo de la búsqueda).</summary>
    private static async Task<List<Obra>> Cargar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, IReadOnlyList<Guid> ids, CancellationToken cancelacion)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var obras = await RepositorioDeObrasPostgres.CargarVarias(conexion, transaccion, "WHERE id = ANY(@ids)", [new NpgsqlParameter("ids", ids.ToArray())], string.Empty, cancelacion).ConfigureAwait(false);
        var posicion = ids.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        return obras.OrderBy(o => posicion[o.Id]).ToList();
    }
}

/// <summary>El azar del catálogo para «Sorpréndeme» (<see cref="IAzarDelCatalogo"/>): el generador compartido de .NET, sin ningún efecto de seguridad (solo elige una obra entre las disponibles).</summary>
public sealed class AzarDelCatalogo : IAzarDelCatalogo
{
    public int Siguiente(int maximoExclusivo) => Random.Shared.Next(maximoExclusivo);
}
