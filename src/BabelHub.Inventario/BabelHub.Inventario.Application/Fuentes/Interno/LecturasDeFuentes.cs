using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// <see cref="IExistencias"/>: el vendible de una o varias ediciones, siempre en <b>una sola instantánea</b> (<c>REPEATABLE READ</c> de solo lectura): el stock, las reservas
/// vencidas sin procesar y las fuentes salen del mismo momento, así que el resultado nunca refleja un estado intermedio inválido (spec §6, D06). El vendible es
/// <c>max(0, físico − reservado + vencidas)</c> y las fuentes sin fila figuran en cero (FUE-05, FUE-06). Esta consulta siempre lee la base; solo la lista de fuentes de la consulta pública puede venir de una memoria de 5 s (D05, <see cref="CacheDeReferencias"/>).
/// El alcance del actor lo comprueba el caso de uso que llama (FUE-08): aquí se lee la red completa.
/// </summary>
public sealed class ConsultaDeExistencias(
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes,
    IRepositorioDeExistencias existencias,
    IReservasDeStock reservas,
    CacheDeReferencias? referencias = null) : IExistencias
{
    private const int TamanoMaximoDePagina = 100;
    private const string ClaveDeFuentes = "fuentes";

    public Task<IReadOnlyList<DisponibilidadDeEdicion>> Disponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Calcular(edicionIds, conReferenciasEnMemoria: false, cancelacion);

    /// <summary>
    /// Lo mismo que <see cref="Disponibilidad"/>, para la consulta pública: la lista de fuentes (código y nombre) puede venir de la memoria del proceso, a lo sumo 5 s atrasada (<see cref="CacheDeReferencias"/>).
    /// El stock y las vencidas salen siempre de la base. Quien decide algo con la disponibilidad (traslados, reservas) usa <see cref="Disponibilidad"/>.
    /// </summary>
    internal Task<IReadOnlyList<DisponibilidadDeEdicion>> DisponibilidadParaLaConsultaPublica(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Calcular(edicionIds, conReferenciasEnMemoria: true, cancelacion);

    private async Task<IReadOnlyList<DisponibilidadDeEdicion>> Calcular(IReadOnlyCollection<Guid> edicionIds, bool conReferenciasEnMemoria, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        var ediciones = edicionIds.Distinct().ToList();
        if (ediciones.Count == 0)
        {
            return [];
        }

        return await lectura.EjecutarLectura(
            async token =>
            {
                var todasLasFuentes = await ListarFuentes(conReferenciasEnMemoria, token).ConfigureAwait(false);
                var filas = (await existencias.Leer(ediciones, token).ConfigureAwait(false)).ToDictionary(f => (f.FuenteId, f.EdicionId));
                var vencidas = Sumar(await reservas.VencidasPorEdicion(ediciones, token).ConfigureAwait(false));

                var resultado = new List<DisponibilidadDeEdicion>(ediciones.Count);
                foreach (var edicion in ediciones)
                {
                    var desglose = todasLasFuentes
                        .Select(f => Existencia(f, edicion, filas.GetValueOrDefault((f.Id, edicion)), vencidas))
                        .ToList();
                    var total = (int)Math.Min(int.MaxValue, desglose.Sum(e => (long)e.Vendible));
                    resultado.Add(new DisponibilidadDeEdicion(edicion, total, desglose));
                }

                return (IReadOnlyList<DisponibilidadDeEdicion>)resultado;
            },
            cancelacion).ConfigureAwait(false);
    }

    private async Task<List<FuenteDeReferencia>> ListarFuentes(bool conReferenciasEnMemoria, CancellationToken cancelacion)
    {
        var usaMemoria = conReferenciasEnMemoria && referencias is not null;
        if (usaMemoria && referencias!.Obtener<List<FuenteDeReferencia>>(ClaveDeFuentes) is { } guardadas)
        {
            return guardadas;
        }

        var leidas = (await fuentes.Listar(cancelacion).ConfigureAwait(false))
            .OrderBy(f => f.Codigo, StringComparer.Ordinal)
            .Select(f => new FuenteDeReferencia(f.Id, f.Codigo, f.Nombre))
            .ToList();
        if (usaMemoria)
        {
            referencias!.Guardar(ClaveDeFuentes, leidas);
        }

        return leidas;
    }

    public async Task<PaginaDeExistencias> DeLaFuente(ConsultaDeExistenciasDeFuente consulta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);

        if (consulta.Pagina < 1 || consulta.Tamano is < 1 or > TamanoMaximoDePagina)
        {
            throw new ArgumentOutOfRangeException(nameof(consulta), $"La página empieza en 1 y el tamaño va de 1 a {TamanoMaximoDePagina}.");
        }

        return await lectura.EjecutarLectura(
            async token =>
            {
                var fuente = await fuentes.PorId(consulta.FuenteId, token).ConfigureAwait(false);
                if (fuente is null)
                {
                    return new PaginaDeExistencias([], 0, consulta.Pagina, consulta.Tamano);
                }

                var edicionesPedidas = consulta.EdicionIds?.Distinct().ToList();
                var filas = await existencias.LeerDeLaFuente(consulta.FuenteId, edicionesPedidas, token).ConfigureAwait(false);

                // Solo una pareja con unidades reservadas puede tener reservas vencidas: se piden las vencidas solo de esas ediciones.
                var conReservas = filas.Where(f => f.Reservado > 0).Select(f => f.EdicionId).Distinct().ToList();
                var vencidas = conReservas.Count == 0
                    ? new Dictionary<(Guid, Guid), int>()
                    : Sumar(await reservas.VencidasPorEdicion(conReservas, token).ConfigureAwait(false));

                var existenciasDeLaFuente = filas
                    .Select(f => Existencia(fuente, f.EdicionId, f, vencidas))
                    .Where(e => consulta.Estado is null || e.Estado == consulta.Estado)
                    .OrderBy(e => e.EdicionId, OrdenDeBloqueo.DeIdentificadores)
                    .ToList();

                var pagina = existenciasDeLaFuente.Skip((consulta.Pagina - 1) * consulta.Tamano).Take(consulta.Tamano).ToList();
                return new PaginaDeExistencias(pagina, existenciasDeLaFuente.Count, consulta.Pagina, consulta.Tamano);
            },
            cancelacion).ConfigureAwait(false);
    }

    internal static Dictionary<(Guid FuenteId, Guid EdicionId), int> Sumar(IReadOnlyList<UnidadesVencidas> vencidas) =>
        vencidas
            .GroupBy(v => (v.FuenteId, v.EdicionId))
            .ToDictionary(g => g.Key, g => (int)Math.Min(int.MaxValue, g.Sum(v => (long)Math.Max(0, v.Unidades))));

    internal static ExistenciaEnFuente Existencia(Fuente fuente, Guid edicionId, FilaDeStock? fila, IReadOnlyDictionary<(Guid FuenteId, Guid EdicionId), int> vencidas) =>
        Existencia(new FuenteDeReferencia(fuente.Id, fuente.Codigo, fuente.Nombre), edicionId, fila, vencidas);

    internal static ExistenciaEnFuente Existencia(FuenteDeReferencia fuente, Guid edicionId, FilaDeStock? fila, IReadOnlyDictionary<(Guid FuenteId, Guid EdicionId), int> vencidas)
    {
        var stock = fila is null ? ExistenciaDeStock.Vacia : new ExistenciaDeStock(fila.Fisico, fila.Reservado);

        // En una misma instantánea las vencidas son parte de lo reservado; el tope es la defensa de la fórmula (D06).
        var sinProcesar = Math.Min(vencidas.GetValueOrDefault((fuente.Id, edicionId)), stock.Reservado);
        var vendible = stock.Vendible(sinProcesar);

        return new ExistenciaEnFuente(
            fuente.Id,
            fuente.Codigo,
            fuente.Nombre,
            edicionId,
            stock.Fisico,
            stock.Reservado,
            vendible,
            Convertir(EstadoDeStockExtensiones.DeVendible(vendible)));
    }

    internal static EstadoDeExistencia Convertir(EstadoDeStock estado) => estado switch
    {
        EstadoDeStock.SinStock => EstadoDeExistencia.SinStock,
        EstadoDeStock.Bajo => EstadoDeExistencia.Bajo,
        EstadoDeStock.Normal => EstadoDeExistencia.Normal,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };
}

/// <summary>
/// <see cref="IDirectorioDeFuentes"/> y <see cref="IFichasDeEdiciones"/>: la lectura de fuentes, rutas y fichas para los otros módulos, cada consulta en una instantánea
/// coherente. Una ruta sin definir responde <see langword="null"/> y no impide vender (FUE-13, spec §6).
/// </summary>
public sealed class DirectorioDeFuentes(
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes,
    IRepositorioDeRutas rutas,
    IRepositorioDeFichas fichas) : IDirectorioDeFuentes, IFichasDeEdiciones
{
    public Task<FuenteDeStock?> PorId(Guid fuenteId, CancellationToken cancelacion = default) =>
        lectura.EjecutarLectura(async token => Convertir(await fuentes.PorId(fuenteId, token).ConfigureAwait(false)), cancelacion);

    public Task<FuenteDeStock?> PorCodigo(string codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(codigo);

        // Un código con forma inválida no puede existir: se responde «no existe» sin consultar.
        return Fuente.EsCodigoValido(codigo)
            ? lectura.EjecutarLectura(async token => Convertir(await fuentes.PorCodigo(codigo, token).ConfigureAwait(false)), cancelacion)
            : Task.FromResult<FuenteDeStock?>(null);
    }

    public Task<IReadOnlyList<FuenteDeStock>> Listar(CancellationToken cancelacion = default) =>
        lectura.EjecutarLectura(
            async token => Ordenar(await fuentes.Listar(token).ConfigureAwait(false)),
            cancelacion);

    public Task<FuenteDeStock?> FuenteDeRecepcion(CancellationToken cancelacion = default) =>
        lectura.EjecutarLectura(
            async token => Convertir((await fuentes.Listar(token).ConfigureAwait(false)).SingleOrDefault(f => f.EsRecepcion)),
            cancelacion);

    public Task<IReadOnlyList<FuenteDeStock>> DeLaRegion(string region, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        return lectura.EjecutarLectura(
            async token => Ordenar(await fuentes.DeLaRegion(region, token).ConfigureAwait(false)),
            cancelacion);
    }

    public Task<int?> PlazoDeRuta(Guid origenId, Guid destinoId, CancellationToken cancelacion = default) =>
        lectura.EjecutarLectura(
            async token => (await rutas.Obtener(origenId, destinoId, token).ConfigureAwait(false))?.PlazoEnDias,
            cancelacion);

    public Task<IReadOnlyList<RutaDeTraslado>> RutasHaciaDestino(Guid destinoId, CancellationToken cancelacion = default) =>
        lectura.EjecutarLectura(
            async token => (IReadOnlyList<RutaDeTraslado>)(await rutas.HaciaDestino(destinoId, token).ConfigureAwait(false))
                .Select(r => new RutaDeTraslado(r.OrigenId, r.DestinoId, r.PlazoEnDias))
                .OrderBy(r => r.PlazoEnDias)
                .ThenBy(r => r.OrigenId, OrdenDeBloqueo.DeIdentificadores)
                .ToList(),
            cancelacion);

    Task<FichaDeEdicionRegistrada?> IFichasDeEdiciones.PorId(Guid edicionId, CancellationToken cancelacion) =>
        lectura.EjecutarLectura(
            async token =>
            {
                var ficha = await fichas.PorId(edicionId, token).ConfigureAwait(false);
                return ficha is null
                    ? null
                    : new FichaDeEdicionRegistrada(ficha.EdicionId, ficha.Formato == FormatoDeEdicion.Impreso ? FormatoDeEdicionRegistrado.Impreso : FormatoDeEdicionRegistrado.Digital);
            },
            cancelacion);

    private static IReadOnlyList<FuenteDeStock> Ordenar(IReadOnlyList<Fuente> lista) =>
        [.. lista.OrderBy(f => f.Codigo, StringComparer.Ordinal).Select(f => Convertir(f)!)];

    private static FuenteDeStock? Convertir(Fuente? fuente) =>
        fuente is null ? null : new FuenteDeStock(fuente.Id, fuente.Codigo, fuente.Nombre, fuente.Region, fuente.EsRecepcion);
}
