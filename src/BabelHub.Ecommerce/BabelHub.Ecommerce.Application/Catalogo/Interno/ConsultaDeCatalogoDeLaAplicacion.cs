using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// La lectura pública del catálogo (<c>BuscarObras</c>, <c>ObtenerObra</c>, <c>ObtenerEdiciones</c>, <c>ObtenerObraAlAzar</c> y <c>ConsultarDisponibilidad</c>; CAT-09, CAT-10, CAT-12, CAT-13, CAT-18, CAT-21). No exige
/// sesión y sus modelos excluyen todo campo interno. Primero se filtra por la base y el Inventario solo se consulta por lotes de 50 ediciones impresas y sobre a lo más 500 ediciones candidatas
/// (<c>busqueda-demasiado-amplia</c> si hay más), con un presupuesto de 10 llamadas y 3 segundos por solicitud; el Inventario caído nunca hace fallar la lectura (CAT-12).
/// </summary>
internal sealed class ConsultaDeCatalogoDeLaAplicacion(
    ILecturasDelCatalogo lecturas,
    ResolutorDeDisponibilidad resolutor,
    IReloj reloj,
    IAzarDelCatalogo azar,
    TimeSpan? plazoDeLaSolicitud = null) : IConsultaDeCatalogo, IDisponibilidadDeEdiciones
{
    /// <summary>El tope de ediciones candidatas del filtro de disponibilidad (D08).</summary>
    public const int MaximoDeEdicionesCandidatas = 500;

    public const int LotesDeMuestreo = 3;

    public const int TamanoDelLoteDeMuestreo = 50;

    public static readonly TimeSpan PlazoPorOmision = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _plazo = plazoDeLaSolicitud ?? PlazoPorOmision;

    private readonly ValidadorDeBusquedaDeObras _validadorDeBusqueda = new();

    public async Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(busqueda);
        var validacion = _validadorDeBusqueda.Validar(busqueda);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<PaginaDeObras>(validacion);
        }

        var filtros = new FiltrosDeObras(busqueda.Texto?.Trim(), busqueda.Autor?.Trim(), busqueda.Editorial?.Trim(), busqueda.Categoria?.Trim(), busqueda.Formato, busqueda.PrecioMinimo, busqueda.PrecioMaximo);
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(_plazo);
        var presupuesto = new PresupuestoDeLlamadas();

        if (!busqueda.SoloDisponibles)
        {
            var pagina = await lecturas.Buscar(filtros, busqueda.Pagina, busqueda.Tamano, cancelacion).ConfigureAwait(false);
            var disponibles = await resolutor.Resolver(pagina.Obras.SelectMany(Vigentes).ToList(), presupuesto, plazo.Token, cancelacion).ConfigureAwait(false);
            return Resultado<PaginaDeObras>.Exito(new PaginaDeObras(pagina.Obras.Select(o => Resumir(o, disponibles)).ToList(), busqueda.Pagina, busqueda.Tamano, pagina.Total));
        }

        var (_, candidatas) = await lecturas.ContarCandidatas(filtros, cancelacion).ConfigureAwait(false);
        if (candidatas > MaximoDeEdicionesCandidatas)
        {
            return FallosDelCatalogo.De<PaginaDeObras>(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.BusquedaDemasiadoAmplia);
        }

        var obras = await lecturas.TodasLasQueCumplen(filtros, cancelacion).ConfigureAwait(false);
        var ediciones = obras.SelectMany(o => Vigentes(o).Where(e => CumpleFormatoYPrecio(e, filtros))).ToList();
        var datos = await resolutor.Resolver(ediciones, presupuesto, plazo.Token, cancelacion).ConfigureAwait(false);

        // Una obra cumple si alguna edición cumple a la vez formato, precio y disponibilidad; una edición sin dato o «no se puede confirmar» no cumple.
        var cumplen = obras
            .Where(o => Vigentes(o).Any(e => CumpleFormatoYPrecio(e, filtros) && datos.TryGetValue(e.Id, out var d) && d.Estado is EstadoDeDisponibilidad.Disponible or EstadoDeDisponibilidad.DisponibleAlInstante))
            .ToList();
        var pagina2 = cumplen.Skip((busqueda.Pagina - 1) * busqueda.Tamano).Take(busqueda.Tamano).ToList();
        var restantes = pagina2.SelectMany(Vigentes).Where(e => !datos.ContainsKey(e.Id)).ToList();
        var resto = await resolutor.Resolver(restantes, presupuesto, plazo.Token, cancelacion).ConfigureAwait(false);
        var todos = datos.Concat(resto).ToDictionary(p => p.Key, p => p.Value);
        return Resultado<PaginaDeObras>.Exito(new PaginaDeObras(pagina2.Select(o => Resumir(o, todos)).ToList(), busqueda.Pagina, busqueda.Tamano, cumplen.Count));
    }

    public async Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default)
    {
        if (obraId == Guid.Empty)
        {
            return FallosDelCatalogo.Validacion<ResumenDeObra>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("obraId", ConstructorDeValidacion.FormatoInvalido)]));
        }

        var obra = await lecturas.ObraPorId(obraId, cancelacion).ConfigureAwait(false);

        // La ficha pública de una obra dada de baja responde «no encontrada» (CAT-13); sus datos siguen legibles por identificador para los pedidos (ObtenerEdiciones).
        if (obra is null || obra.EstaDadaDeBaja)
        {
            return FallosDelCatalogo.Fallo<ResumenDeObra>(TraductorDeRechazos.ObraNoEncontrada());
        }

        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(_plazo);
        var datos = await resolutor.Resolver(Vigentes(obra).ToList(), new PresupuestoDeLlamadas(), plazo.Token, cancelacion).ConfigureAwait(false);
        return Resultado<ResumenDeObra>.Exito(Resumir(obra, datos));
    }

    public async Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        var validacion = ValidadorDeEdicionesPorIdentificador.Validar(edicionIds);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<IReadOnlyList<EdicionConsultada>>(validacion);
        }

        var hoy = reloj.HoyEnLima;
        var encontradas = await lecturas.EdicionesPorId(edicionIds, cancelacion).ConfigureAwait(false);
        IReadOnlyList<EdicionConsultada> resultado = encontradas
            .Select(c => new EdicionConsultada(
                c.Edicion.Id,
                c.Obra.Id,
                c.Obra.Titulo,
                c.Edicion.EsDigital ? FormatoDeEdicion.Digital : FormatoDeEdicion.Impresa,
                c.Edicion.Isbn.Isbn13,
                c.Edicion.Precio.Valor,
                Estado(c.Edicion),
                c.Edicion.MotivoDeNoDisponiblePara(hoy) is { } motivo ? ResolutorDeDisponibilidad.Traducir(motivo) : null))
            .ToList();
        return Resultado<IReadOnlyList<EdicionConsultada>>.Exito(resultado);
    }

    public async Task<Resultado<IReadOnlyList<DisponibilidadDeEdicion>>> Consultar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        var validacion = ValidadorDeEdicionesPorIdentificador.Validar(edicionIds);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<IReadOnlyList<DisponibilidadDeEdicion>>(validacion);
        }

        var encontradas = await lecturas.EdicionesPorId(edicionIds, cancelacion).ConfigureAwait(false);
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(_plazo);
        var datos = await resolutor.Resolver(encontradas.Select(c => c.Edicion).ToList(), new PresupuestoDeLlamadas(), plazo.Token, cancelacion).ConfigureAwait(false);
        IReadOnlyList<DisponibilidadDeEdicion> resultado = edicionIds.Where(datos.ContainsKey).Select(id => datos[id]).ToList();
        return Resultado<IReadOnlyList<DisponibilidadDeEdicion>>.Exito(resultado);
    }

    public async Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default)
    {
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(_plazo);
        var presupuesto = new PresupuestoDeLlamadas(LotesDeMuestreo);
        var vistas = new HashSet<Guid>();

        for (var lote = 0; lote < LotesDeMuestreo; lote++)
        {
            var muestra = await lecturas.ObrasAlAzar(TamanoDelLoteDeMuestreo, vistas, cancelacion).ConfigureAwait(false);
            if (muestra.Count == 0)
            {
                break;
            }

            vistas.UnionWith(muestra.Select(o => o.Id));
            var datos = await resolutor.Resolver(muestra.SelectMany(Vigentes).ToList(), presupuesto, plazo.Token, cancelacion).ConfigureAwait(false);

            // Una digital vigente cuenta como disponible sin consultar al Inventario; una impresa solo si el Inventario respondió ahora (con el Inventario caído solo se ofrecen digitales o ninguna).
            var disponibles = muestra
                .Where(o => Vigentes(o).Any(e => datos.TryGetValue(e.Id, out var d) && (d.Estado == EstadoDeDisponibilidad.DisponibleAlInstante || (d.Estado == EstadoDeDisponibilidad.Disponible && d.Frescura == FrescuraDeLaDisponibilidad.Reciente))))
                .ToList();
            if (disponibles.Count > 0)
            {
                return Resultado<ObraAlAzar?>.Exito(new ObraAlAzar(disponibles[azar.Siguiente(disponibles.Count)].Id));
            }
        }

        return Resultado<ObraAlAzar?>.Exito(null);
    }

    private IEnumerable<Edicion> Vigentes(Obra obra) => obra.Ediciones.Where(e => !e.EstaDadaDeBaja);

    private static bool CumpleFormatoYPrecio(Edicion edicion, FiltrosDeObras filtros) =>
        (filtros.Formato is null || (filtros.Formato == FormatoDeEdicion.Digital) == edicion.EsDigital)
        && (filtros.PrecioMinimo is null || edicion.Precio.Valor >= filtros.PrecioMinimo)
        && (filtros.PrecioMaximo is null || edicion.Precio.Valor <= filtros.PrecioMaximo);

    private static EstadoDeEdicionConsultado Estado(Edicion edicion) =>
        edicion.Estado switch
        {
            EstadoDeEdicion.Activa => EstadoDeEdicionConsultado.Activa,
            EstadoDeEdicion.RetiradaDeVenta => EstadoDeEdicionConsultado.RetiradaDeVenta,
            _ => EstadoDeEdicionConsultado.DadaDeBaja,
        };

    private ResumenDeObra Resumir(Obra obra, IReadOnlyDictionary<Guid, DisponibilidadDeEdicion> disponibilidad) =>
        new(
            obra.Id,
            obra.Titulo,
            obra.Autor,
            obra.Editorial,
            obra.Categoria,
            obra.Portada.Huella.Hex,
            Vigentes(obra)
                .Select(e => new EdicionDeObra(
                    e.Id,
                    e.EsDigital ? FormatoDeEdicion.Digital : FormatoDeEdicion.Impresa,
                    e.Isbn.Isbn13,
                    e.Precio.Valor,
                    disponibilidad.TryGetValue(e.Id, out var d) ? d : new DisponibilidadDeEdicion(e.Id, EstadoDeDisponibilidad.NoSePuedeConfirmar, null, [], FrescuraDeLaDisponibilidad.NoSePuedeConfirmar, null, null)))
                .ToList(),
            obra.Descripcion);
}
