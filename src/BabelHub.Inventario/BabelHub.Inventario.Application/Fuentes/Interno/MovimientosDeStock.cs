using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// <see cref="IAplicadorDeMovimientos"/>: el cambio atómico de una fila de stock. No libera vencidas ni usa <see cref="IReservasDeStock"/>, para que <c>Reservas</c> lo use
/// al expirar sin un ciclo de inyección (D02). La regla <c>0 ≤ reservado ≤ físico</c> la hace cumplir la sentencia del repositorio (D01); el Dominio
/// (<see cref="ExistenciaDeStock.Aplicar"/>) la espeja y una prueba de paridad las compara.
/// </summary>
public sealed class AplicadorDeMovimientos(IRepositorioDeExistencias existencias) : IAplicadorDeMovimientos
{
    public Task<ResultadoDeAplicacion> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(movimiento);

        if (movimiento.FuenteId == Guid.Empty || movimiento.EdicionId == Guid.Empty)
        {
            throw new ArgumentException("El movimiento necesita la fuente y la edición.", nameof(movimiento));
        }

        if (movimiento.DeltaFisico == 0 && movimiento.DeltaReservado == 0)
        {
            throw new ArgumentException("Un movimiento sin cambios no tiene sentido.", nameof(movimiento));
        }

        return existencias.Aplicar(movimiento, cancelacion);
    }
}

/// <summary>
/// <see cref="IMovimientosDeStock"/>: <c>Bloquear</c> deja las filas de todas las fuentes de las ediciones listas para cambiar, sin interbloqueos, y <c>Aplicar</c> cambia una.
/// El orden es el de la jerarquía de D04 y nada toca el stock antes de tener todos sus bloqueos: (1) garantizar las filas, (2) liberar las vencidas
/// por <see cref="IReservasDeStock"/> (líneas bloqueadas por id y pasadas a expiradas, con <c>ReservaExpirada</c> auditada allí), (3) bloquear las filas de todas las
/// fuentes en orden (edición, fuente) y (4) solo entonces aplicar los deltas agregados y ordenados de lo liberado.
/// </summary>
public sealed class MovimientosDeStock(
    IRepositorioDeFichas fichas,
    IRepositorioDeExistencias existencias,
    IReservasDeStock reservas,
    IAplicadorDeMovimientos aplicador) : IMovimientosDeStock
{
    public Task<ResultadoDeAplicacion> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion = default) =>
        aplicador.Aplicar(movimiento, cancelacion);

    public async Task<BloqueoDeStock> Bloquear(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        if (edicionIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Una edición no tiene identificador.", nameof(edicionIds));
        }

        var ediciones = edicionIds.Distinct().Order(OrdenDeBloqueo.DeIdentificadores).ToList();
        if (ediciones.Count == 0)
        {
            return BloqueoDeStock.Con([]);
        }

        // Solo las ediciones impresas tienen stock (regla 12): una digital o sin ficha no se bloquea ni se crea.
        if (!await fichas.SonTodasImpresas(ediciones, cancelacion).ConfigureAwait(false))
        {
            return BloqueoDeStock.Rechazado();
        }

        await existencias.GarantizarFilas(ediciones, cancelacion).ConfigureAwait(false);
        var liberadas = await reservas.LiberarVencidas(ediciones, cancelacion).ConfigureAwait(false);
        var filas = (await existencias.BloquearFilas(ediciones, cancelacion).ConfigureAwait(false))
            .ToDictionary(f => (f.FuenteId, f.EdicionId));

        foreach (var delta in Agregar(liberadas))
        {
            if (!filas.TryGetValue((delta.FuenteId, delta.EdicionId), out var fila))
            {
                throw new InvalidOperationException("La liberación de vencidas nombra una fila de stock que no se bloqueó.");
            }

            // El Dominio decide si la fila bloqueada admite devolver esas unidades; la sentencia del repositorio lo vuelve a exigir.
            if (!new ExistenciaDeStock(fila.Fisico, fila.Reservado).DevolverApartado(delta.Unidades).Aplicado)
            {
                throw new InvalidOperationException("La liberación de vencidas devuelve más unidades de las que la fila tiene reservadas.");
            }

            var resultado = await existencias
                .AplicarLiberacionDeVencidas(new MovimientoDeStock(delta.FuenteId, delta.EdicionId, 0, -delta.Unidades), cancelacion)
                .ConfigureAwait(false);
            if (!resultado.Aplicado || resultado.Posterior is not { } posterior)
            {
                throw new InvalidOperationException("No se pudo aplicar la liberación de vencidas sobre una fila ya bloqueada.");
            }

            filas[(delta.FuenteId, delta.EdicionId)] = fila with { Fisico = posterior.Fisico, Reservado = posterior.Reservado };
        }

        return BloqueoDeStock.Con(
            [.. filas.Values
                .OrderBy(f => f.EdicionId, OrdenDeBloqueo.DeIdentificadores)
                .ThenBy(f => f.FuenteId, OrdenDeBloqueo.DeIdentificadores)
                .Select(f => new ExistenciaBloqueada(f.FuenteId, f.EdicionId, f.Fisico, f.Reservado))]);
    }

    /// <summary>Suma las liberaciones de una misma pareja y las ordena por (edición, fuente); ignora las vacías y rechaza una negativa.</summary>
    private static IEnumerable<UnidadesLiberadas> Agregar(IReadOnlyList<UnidadesLiberadas> liberadas)
    {
        ArgumentNullException.ThrowIfNull(liberadas);

        if (liberadas.Any(l => l.Unidades < 0))
        {
            throw new InvalidOperationException("Una liberación de vencidas no puede devolver unidades negativas.");
        }

        return liberadas
            .Where(l => l.Unidades > 0)
            .GroupBy(l => (l.FuenteId, l.EdicionId))
            .Select(g => new UnidadesLiberadas(g.Key.FuenteId, g.Key.EdicionId, checked(g.Sum(l => l.Unidades))))
            .OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(l => l.FuenteId, OrdenDeBloqueo.DeIdentificadores);
    }
}
