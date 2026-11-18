using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>Una edición con la obra a la que pertenece (para leer su título y su estado por identificador).</summary>
public sealed record EdicionConObra(Obra Obra, Edicion Edicion);

/// <summary>Los filtros de la búsqueda que decide la base (CAT-09): texto, autor, editorial, categoría, formato y rango de precio. La disponibilidad no la decide la base (D08).</summary>
public sealed record FiltrosDeObras(string? Texto, string? Autor, string? Editorial, string? Categoria, FormatoDeEdicion? Formato, decimal? PrecioMinimo, decimal? PrecioMaximo);

/// <summary>Una página de obras de la búsqueda por la base.</summary>
public sealed record PaginaDeObrasDeLaBase(IReadOnlyList<Obra> Obras, int Total);

/// <summary>
/// La lectura del catálogo (puerto de la Aplicación que implementa la Infraestructura con el índice de trigramas y <c>unaccent</c>, T15). Una obra cumple si <b>alguna edición no dada de baja</b> cumple a la
/// vez formato y precio y la obra cumple el texto, el autor, la editorial y la categoría; el orden es fijo (título e identificador, para una paginación estable) y los comodines del texto ya vienen escapados por el
/// adaptador. Solo trae obras activas, con todas sus ediciones no dadas de baja.
/// </summary>
public interface ILecturasDelCatalogo
{
    /// <summary>Una página de la búsqueda sin filtro de disponibilidad, con el total de obras que cumplen.</summary>
    Task<PaginaDeObrasDeLaBase> Buscar(FiltrosDeObras filtros, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Cuántas ediciones candidatas (que cumplen formato y precio) y cuántas obras tiene la búsqueda, para el tope de 500 ediciones del filtro de disponibilidad.</summary>
    Task<(int Obras, int Ediciones)> ContarCandidatas(FiltrosDeObras filtros, CancellationToken cancelacion = default);

    /// <summary>Todas las obras que cumplen (con orden fijo); solo se pide con el filtro de disponibilidad y tras comprobar el tope de 500 ediciones candidatas.</summary>
    Task<IReadOnlyList<Obra>> TodasLasQueCumplen(FiltrosDeObras filtros, CancellationToken cancelacion = default);

    /// <summary>Una obra con todas sus ediciones por su identificador, también dada de baja; <see langword="null"/> si no existe.</summary>
    Task<Obra?> ObraPorId(Guid obraId, CancellationToken cancelacion = default);

    /// <summary>Las ediciones por identificador, también las dadas de baja y las de una obra dada de baja (CAT-13, CAT-18); las inexistentes se omiten.</summary>
    Task<IReadOnlyList<EdicionConObra>> EdicionesPorId(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>Hasta <paramref name="cantidad"/> obras activas en orden aleatorio, sin las excluidas (el muestreo de «Sorpréndeme», sin conteo exacto).</summary>
    Task<IReadOnlyList<Obra>> ObrasAlAzar(int cantidad, IReadOnlyCollection<Guid> excluidas, CancellationToken cancelacion = default);
}

/// <summary>Elige al azar (puerto para poder probar «Sorpréndeme» con un resultado conocido).</summary>
public interface IAzarDelCatalogo
{
    /// <summary>Un entero de 0 a <paramref name="maximoExclusivo"/> − 1.</summary>
    int Siguiente(int maximoExclusivo);
}

/// <summary>El presupuesto de llamadas al Inventario de una solicitud pública (plan §6): a lo más 10 llamadas; lo que no entra se trata como «sin respuesta».</summary>
public sealed class PresupuestoDeLlamadas(int llamadas = ResolutorDeDisponibilidad.MaximoDeLlamadas)
{
    public int Restantes { get; private set; } = llamadas;

    public bool Tomar()
    {
        if (Restantes <= 0)
        {
            return false;
        }

        Restantes--;
        return true;
    }
}

/// <summary>
/// Resuelve la disponibilidad de ediciones (CAT-10, CAT-12, CAT-21, D03): una digital vigente es «disponible al instante» sin consultar al Inventario (regla 12) y una que no se ofrece lo explica; una
/// impresa usa la memoria si el dato tiene menos de 30 segundos y no fue invalidado, si no pide al Inventario lotes de hasta 50. Si el Inventario no responde (o responde algo inválido, SEG-13) usa el último
/// dato de menos de 10 minutos («puede estar desactualizada»; exactamente 10 minutos ya es «más antigua») o, si no hay, «no se puede confirmar». Una impresa que el Inventario no conoce se muestra
/// agotada. El instante guardado es el de esta solicitud (el del Catálogo), nunca el que informa el Inventario.
/// </summary>
public sealed class ResolutorDeDisponibilidad(IInventarioDelCatalogo inventario, IMemoriaDeDisponibilidad memoria, IReloj reloj)
{
    public const int MaximoDeLlamadas = 10;

    public const int TamanoDeLote = 50;

    public static readonly TimeSpan VigenciaDeLaCache = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan RespaldoMaximo = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyDictionary<Guid, DisponibilidadDeEdicion>> Resolver(IReadOnlyCollection<Edicion> ediciones, PresupuestoDeLlamadas presupuesto, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(ediciones);
        ArgumentNullException.ThrowIfNull(presupuesto);
        var hoy = reloj.HoyEnLima;
        var ahora = reloj.Ahora;
        var resultado = new Dictionary<Guid, DisponibilidadDeEdicion>();
        var porConsultar = new List<Edicion>();

        foreach (var edicion in ediciones.DistinctBy(e => e.Id))
        {
            var motivo = edicion.MotivoDeNoDisponiblePara(hoy);
            if (motivo is not null)
            {
                resultado[edicion.Id] = new DisponibilidadDeEdicion(edicion.Id, EstadoDeDisponibilidad.NoSeOfrece, null, [], FrescuraDeLaDisponibilidad.Reciente, null, Traducir(motivo.Value));
            }
            else if (edicion.EsDigital)
            {
                resultado[edicion.Id] = new DisponibilidadDeEdicion(edicion.Id, EstadoDeDisponibilidad.DisponibleAlInstante, null, [], FrescuraDeLaDisponibilidad.Reciente, null, null);
            }
            else if (memoria.Obtener(edicion.Id) is { Invalidada: false } entrada && EsReciente(ahora - entrada.ConsultadaEn))
            {
                resultado[edicion.Id] = Desde(entrada, FrescuraDeLaDisponibilidad.Reciente);
            }
            else
            {
                porConsultar.Add(edicion);
            }
        }

        foreach (var lote in porConsultar.Chunk(TamanoDeLote))
        {
            var ids = lote.Select(e => e.Id).ToList();
            var respuesta = presupuesto.Tomar() ? Validar(await inventario.ConsultarDisponibilidad(ids, cancelacion).ConfigureAwait(false), ids) : null;

            foreach (var edicion in lote)
            {
                resultado[edicion.Id] = respuesta is null ? Respaldo(edicion.Id, ahora) : Aplicar(edicion.Id, respuesta, ahora);
            }
        }

        return resultado;
    }

    private DisponibilidadDeEdicion Aplicar(Guid edicionId, RespuestaDeDisponibilidad respuesta, DateTimeOffset ahora)
    {
        if (respuesta.Ediciones.FirstOrDefault(d => d.EdicionId == edicionId) is { } dato)
        {
            var entrada = new EntradaDeDisponibilidad(edicionId, dato.VendibleEnLaRed, dato.PorFuente, ahora);
            memoria.Guardar(entrada);
            return Desde(entrada, FrescuraDeLaDisponibilidad.Reciente);
        }

        // El Inventario no conoce la edición (sin ficha): agotada, y no se guarda un dato que ocultaría un registro posterior.
        return respuesta.NoRegistradas.Contains(edicionId)
            ? new DisponibilidadDeEdicion(edicionId, EstadoDeDisponibilidad.Agotada, 0, [], FrescuraDeLaDisponibilidad.Reciente, ahora, null)
            : Respaldo(edicionId, ahora);
    }

    private DisponibilidadDeEdicion Respaldo(Guid edicionId, DateTimeOffset ahora)
    {
        if (memoria.Obtener(edicionId) is { } entrada && ahora - entrada.ConsultadaEn is { } edad && edad >= TimeSpan.Zero && edad < RespaldoMaximo)
        {
            return Desde(entrada, FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada);
        }

        return new DisponibilidadDeEdicion(edicionId, EstadoDeDisponibilidad.NoSePuedeConfirmar, null, [], FrescuraDeLaDisponibilidad.NoSePuedeConfirmar, null, null);
    }

    private static DisponibilidadDeEdicion Desde(EntradaDeDisponibilidad entrada, FrescuraDeLaDisponibilidad frescura) =>
        new(entrada.EdicionId, entrada.VendibleEnLaRed > 0 ? EstadoDeDisponibilidad.Disponible : EstadoDeDisponibilidad.Agotada, entrada.VendibleEnLaRed, entrada.PorFuente, frescura, entrada.ConsultadaEn, null);

    private static bool EsReciente(TimeSpan edad) => edad >= TimeSpan.Zero && edad < VigenciaDeLaCache;

    /// <summary>La respuesta del Inventario se valida antes de usarla o guardarla (SEG-13): solo ediciones pedidas, sin repetir, con cantidades no negativas; si no, cuenta como «sin respuesta».</summary>
    private static RespuestaDeDisponibilidad? Validar(RespuestaDeDisponibilidad? respuesta, IReadOnlyCollection<Guid> pedidas)
    {
        if (respuesta is null)
        {
            return null;
        }

        var ids = respuesta.Ediciones.Select(e => e.EdicionId).Concat(respuesta.NoRegistradas).ToList();
        var valida = ids.All(pedidas.Contains)
            && ids.Distinct().Count() == ids.Count
            && respuesta.Ediciones.All(e => e.VendibleEnLaRed >= 0 && e.PorFuente.All(f => f.Vendible >= 0 && !string.IsNullOrWhiteSpace(f.CodigoDeFuente)));
        return valida ? respuesta : null;
    }

    internal static MotivoDeNoDisponibleDeEdicion Traducir(MotivoDeNoDisponible motivo) =>
        motivo switch
        {
            MotivoDeNoDisponible.LicenciaVencida => MotivoDeNoDisponibleDeEdicion.LicenciaVencida,
            MotivoDeNoDisponible.SinPeru => MotivoDeNoDisponibleDeEdicion.SinPeru,
            MotivoDeNoDisponible.RetiradaDeLaVenta => MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta,
            _ => MotivoDeNoDisponibleDeEdicion.DadaDeBaja,
        };
}
