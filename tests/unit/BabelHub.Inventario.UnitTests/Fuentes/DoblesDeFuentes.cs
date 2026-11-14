using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>El registro, en orden, de lo que los dobles recibieron: así una prueba comprueba el orden de los bloqueos.</summary>
internal sealed class BitacoraDeLlamadas
{
    public List<string> Llamadas { get; } = [];

    public void Anotar(string llamada) => Llamadas.Add(llamada);
}

/// <summary>Lectura coherente de prueba: ejecuta el trabajo y cuenta cuántas instantáneas se abrieron.</summary>
internal sealed class LecturaDirecta : ILecturaCoherente
{
    public int Instantaneas { get; private set; }

    public async Task<T> EjecutarLectura<T>(Func<CancellationToken, Task<T>> lectura, CancellationToken cancelacion = default)
    {
        Instantaneas++;
        return await lectura(cancelacion);
    }
}

internal sealed class RepositorioDeFuentesEnMemoria : IRepositorioDeFuentes
{
    public List<Fuente> Fuentes { get; } = [];

    public Fuente Agregar(string codigo, string? region = null, bool recepcion = false)
    {
        var fuente = Fuente.Registrar(Guid.NewGuid(), codigo, char.ToUpperInvariant(codigo[0]) + codigo[1..], region ?? codigo, null, recepcion);
        Fuentes.Add(fuente);
        return fuente;
    }

    public Task<Fuente?> PorId(Guid fuenteId, CancellationToken cancelacion = default) => Task.FromResult(Fuentes.FirstOrDefault(f => f.Id == fuenteId));

    public Task<Fuente?> PorCodigo(string codigo, CancellationToken cancelacion = default) => Task.FromResult(Fuentes.FirstOrDefault(f => f.Codigo == codigo));

    public Task<IReadOnlyList<Fuente>> Listar(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<Fuente>>([.. Fuentes]);

    public Task<IReadOnlyList<Fuente>> DeLaRegion(string region, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Fuente>>([.. Fuentes.Where(f => f.Region == region)]);

    /// <summary>Si tiene valor, la próxima escritura responde eso (la restricción única ante una alta simultánea que la lectura previa no vio).</summary>
    public ResultadoDeEscrituraDeFuente? RespuestaForzada { get; set; }

    public List<Fuente> Escritas { get; } = [];

    public Task<int> Contar(CancellationToken cancelacion = default) => Task.FromResult(Fuentes.Count);

    public Task<Fuente?> PorCodigoParaActualizar(string codigo, CancellationToken cancelacion = default) => PorCodigo(codigo, cancelacion);

    public Task<ResultadoDeEscrituraDeFuente> Agregar(Fuente fuente, CancellationToken cancelacion = default)
    {
        if (RespuestaForzada is { } forzada)
        {
            return Task.FromResult(forzada);
        }

        if (Fuentes.Any(f => f.Codigo == fuente.Codigo))
        {
            return Task.FromResult(ResultadoDeEscrituraDeFuente.CodigoRepetido);
        }

        if (Fuentes.Any(f => f.Nombre == fuente.Nombre))
        {
            return Task.FromResult(ResultadoDeEscrituraDeFuente.NombreRepetido);
        }

        Fuentes.Add(fuente);
        Escritas.Add(fuente);
        return Task.FromResult(ResultadoDeEscrituraDeFuente.Escrita);
    }

    public Task<ResultadoDeEscrituraDeFuente> Actualizar(Fuente fuente, CancellationToken cancelacion = default)
    {
        if (RespuestaForzada is { } forzada)
        {
            return Task.FromResult(forzada);
        }

        if (Fuentes.Any(f => f.Id != fuente.Id && f.Nombre == fuente.Nombre))
        {
            return Task.FromResult(ResultadoDeEscrituraDeFuente.NombreRepetido);
        }

        Fuentes[Fuentes.FindIndex(f => f.Id == fuente.Id)] = fuente;
        Escritas.Add(fuente);
        return Task.FromResult(ResultadoDeEscrituraDeFuente.Escrita);
    }

    public Task CambiarRecepcion(Guid? anteriorId, Guid nuevaId, CancellationToken cancelacion = default)
    {
        for (var i = 0; i < Fuentes.Count; i++)
        {
            Fuentes[i] = Fuentes[i].ConRecepcion(Fuentes[i].Id == nuevaId);
        }

        return Task.CompletedTask;
    }
}

internal sealed class RepositorioDeRutasEnMemoria : IRepositorioDeRutas
{
    public List<RutaEntreFuentes> Rutas { get; } = [];

    public Task<RutaEntreFuentes?> Obtener(Guid origenId, Guid destinoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Rutas.FirstOrDefault(r => r.OrigenId == origenId && r.DestinoId == destinoId));

    public Task<IReadOnlyList<RutaEntreFuentes>> HaciaDestino(Guid destinoId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<RutaEntreFuentes>>([.. Rutas.Where(r => r.DestinoId == destinoId)]);

    public Task<IReadOnlyList<RutaEntreFuentes>> Todas(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<RutaEntreFuentes>>([.. Rutas]);

    public List<RutaEntreFuentes> Definidas { get; } = [];

    public Task<int?> Definir(RutaEntreFuentes ruta, CancellationToken cancelacion = default)
    {
        var indice = Rutas.FindIndex(r => r.OrigenId == ruta.OrigenId && r.DestinoId == ruta.DestinoId);
        int? anterior = null;
        if (indice >= 0)
        {
            anterior = Rutas[indice].PlazoEnDias;
            Rutas[indice] = ruta;
        }
        else
        {
            Rutas.Add(ruta);
        }

        Definidas.Add(ruta);
        return Task.FromResult(anterior);
    }
}

internal sealed class RepositorioDeFichasEnMemoria(BitacoraDeLlamadas? bitacora = null) : IRepositorioDeFichas
{
    public Dictionary<Guid, FichaDeEdicion> Fichas { get; } = [];

    public FichaDeEdicion Agregar(FormatoDeEdicion formato = FormatoDeEdicion.Impreso, Guid? edicionId = null)
    {
        var ficha = FichaDeEdicion.Crear(edicionId ?? Guid.NewGuid(), formato);
        Fichas[ficha.EdicionId] = ficha;
        return ficha;
    }

    public Task<FichaDeEdicion?> PorId(Guid edicionId, CancellationToken cancelacion = default) => Task.FromResult(Fichas.GetValueOrDefault(edicionId));

    public Task<IReadOnlyList<FichaDeEdicion>> PorIds(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<FichaDeEdicion>>([.. edicionIds.Distinct().Where(Fichas.ContainsKey).Select(id => Fichas[id])]);

    /// <summary>Si tiene valor, el próximo guardado responde eso (la clave compuesta de formato ante una carrera que la lectura previa no vio).</summary>
    public ResultadoDeGuardadoDeFicha? RespuestaForzada { get; set; }

    public List<FichaDeEdicion> Guardadas { get; } = [];

    public Task<ResultadoDeGuardadoDeFicha> Guardar(FichaDeEdicion ficha, CancellationToken cancelacion = default)
    {
        if (RespuestaForzada is { } forzada)
        {
            return Task.FromResult(forzada);
        }

        Fichas[ficha.EdicionId] = ficha;
        Guardadas.Add(ficha);
        return Task.FromResult(ResultadoDeGuardadoDeFicha.Guardada);
    }

    public Task<bool> SonTodasImpresas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("fichas.SonTodasImpresas");
        return Task.FromResult(edicionIds.All(id => Fichas.TryGetValue(id, out var ficha) && ficha.PuedeTenerStock));
    }
}

/// <summary>
/// La tabla de stock en memoria con la semántica de la primitiva de escritura (la regla del Dominio) y de las operaciones de bloqueo, para probar la orquestación
/// sin base de datos. Anota en la bitácora cada operación y los movimientos que recibió.
/// </summary>
internal sealed class RepositorioDeExistenciasEnMemoria(RepositorioDeFuentesEnMemoria fuentes, BitacoraDeLlamadas? bitacora = null) : IRepositorioDeExistencias
{
    public Dictionary<(Guid FuenteId, Guid EdicionId), (int Fisico, int Reservado)> Filas { get; } = [];

    /// <summary>Parejas que el actor no ve (la política de filas las oculta): para probar «denegado por alcance».</summary>
    public HashSet<Guid> FuentesFueraDelAlcance { get; } = [];

    public List<MovimientoDeStock> Movimientos { get; } = [];

    public List<MovimientoDeStock> Liberaciones { get; } = [];

    public IReadOnlyList<IReadOnlyCollection<Guid>> LecturasDeEdiciones => _lecturas;

    private readonly List<IReadOnlyCollection<Guid>> _lecturas = [];

    public void Poner(Guid fuenteId, Guid edicionId, int fisico, int reservado) => Filas[(fuenteId, edicionId)] = (fisico, reservado);

    public Task GarantizarFilas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("existencias.GarantizarFilas");
        foreach (var edicion in edicionIds)
        {
            foreach (var fuente in fuentes.Fuentes)
            {
                Filas.TryAdd((fuente.Id, edicion), (0, 0));
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FilaDeStock>> BloquearFilas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("existencias.BloquearFilas");
        return Task.FromResult(Leidas(edicionIds));
    }

    public Task<ResultadoDeAplicacion> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar($"existencias.Aplicar({movimiento.DeltaFisico},{movimiento.DeltaReservado})");
        Movimientos.Add(movimiento);
        return Task.FromResult(Ejecutar(movimiento, conAlcanceDeRed: false));
    }

    public Task<ResultadoDeAplicacion> AplicarLiberacionDeVencidas(MovimientoDeStock movimiento, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar($"existencias.AplicarLiberacion({movimiento.DeltaFisico},{movimiento.DeltaReservado})");
        Liberaciones.Add(movimiento);
        return Task.FromResult(Ejecutar(movimiento, conAlcanceDeRed: true));
    }

    public Task<IReadOnlyList<FilaDeStock>> Leer(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("existencias.Leer");
        _lecturas.Add(edicionIds);
        return Task.FromResult(Leidas(edicionIds));
    }

    public Task<IReadOnlyList<FilaDeStock>> LeerDeLaFuente(Guid fuenteId, IReadOnlyCollection<Guid>? edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("existencias.LeerDeLaFuente");
        return Task.FromResult<IReadOnlyList<FilaDeStock>>(
            [.. Filas
                .Where(p => p.Key.FuenteId == fuenteId && (edicionIds is null || edicionIds.Contains(p.Key.EdicionId)))
                .Select(p => new FilaDeStock(p.Key.FuenteId, p.Key.EdicionId, p.Value.Fisico, p.Value.Reservado))
                .OrderBy(f => f.EdicionId, OrdenDeBloqueo.DeIdentificadores)]);
    }

    public Task<bool> HayExistencias(Guid edicionId, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("existencias.HayExistencias");
        return Task.FromResult(Filas.Keys.Any(p => p.EdicionId == edicionId));
    }

    private IReadOnlyList<FilaDeStock> Leidas(IReadOnlyCollection<Guid> edicionIds) =>
        [.. Filas
            .Where(p => edicionIds.Contains(p.Key.EdicionId))
            .Select(p => new FilaDeStock(p.Key.FuenteId, p.Key.EdicionId, p.Value.Fisico, p.Value.Reservado))
            .OrderBy(f => f.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(f => f.FuenteId, OrdenDeBloqueo.DeIdentificadores)];

    private ResultadoDeAplicacion Ejecutar(MovimientoDeStock m, bool conAlcanceDeRed)
    {
        if (!Filas.TryGetValue((m.FuenteId, m.EdicionId), out var fila))
        {
            return ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.NoAplicable);
        }

        if (!conAlcanceDeRed && FuentesFueraDelAlcance.Contains(m.FuenteId))
        {
            return ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.DeniegadoPorAlcance);
        }

        var anterior = new ExistenciaDeStock(fila.Fisico, fila.Reservado);
        var resultado = anterior.Aplicar(m.DeltaFisico, m.DeltaReservado);
        if (!resultado.Aplicado)
        {
            return ResultadoDeAplicacion.Fallo(ResultadoDeMovimiento.NoAplicable);
        }

        Filas[(m.FuenteId, m.EdicionId)] = (resultado.Existencia.Fisico, resultado.Existencia.Reservado);
        return ResultadoDeAplicacion.Exito(new CantidadesDeStock(anterior.Fisico, anterior.Reservado), new CantidadesDeStock(resultado.Existencia.Fisico, resultado.Existencia.Reservado));
    }
}

/// <summary>
/// <c>IReservasDeStock</c> de prueba: devuelve lo configurado y anota las llamadas. Sirve de doble de <c>Reservas</c>, que aún no existe.
/// </summary>
internal sealed class ReservasDeStockDoble(BitacoraDeLlamadas? bitacora = null) : IReservasDeStock
{
    public List<UnidadesVencidas> Vencidas { get; } = [];

    public List<UnidadesLiberadas> Liberadas { get; } = [];

    public PedidosQueApartanUnidades Pedidos { get; set; } = new([], 0);

    public int LlamadasALiberar { get; private set; }

    public int LlamadasAVencidas { get; private set; }

    /// <summary>Las ediciones que se pidieron en cada consulta de vencidas.</summary>
    public List<IReadOnlyCollection<Guid>> EdicionesPedidasDeVencidas { get; } = [];

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("reservas.VencidasPorEdicion");
        LlamadasAVencidas++;
        EdicionesPedidasDeVencidas.Add(edicionIds);
        return Task.FromResult<IReadOnlyList<UnidadesVencidas>>([.. Vencidas.Where(v => edicionIds.Contains(v.EdicionId))]);
    }

    public Task<IReadOnlyList<UnidadesLiberadas>> LiberarVencidas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("reservas.LiberarVencidas");
        LlamadasALiberar++;
        return Task.FromResult<IReadOnlyList<UnidadesLiberadas>>([.. Liberadas.Where(l => edicionIds.Contains(l.EdicionId))]);
    }

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult(Pedidos);
}
