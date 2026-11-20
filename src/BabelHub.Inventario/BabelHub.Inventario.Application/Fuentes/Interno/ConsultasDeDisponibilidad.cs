using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// La disponibilidad de una o varias ediciones (FUE-05, FUE-07, FUE-15): el vendible de la red y el desglose por fuente, con las fuentes sin fila en cero y el vendible
/// ya con las vencidas sin procesar (FUE-03). Todo sale <b>de una sola instantánea</b> (<c>REPEATABLE READ</c>, D06): las fichas, el stock y las vencidas son del
/// mismo momento. Una edición sin ficha es «no registrada» (no encontrada, FUE-15) y una digital es «no impresa» (no tiene stock físico, FUE-07): el caso de uso
/// las separa y la Api decide cómo responder. Lo que sale no lleva físico ni reservado, que solo ve la sucursal dueña (FUE-08). Exige un actor con contexto
/// (usuario, sistema o integración), no un visitante ni la ausencia de actor.
/// </summary>
internal sealed class ConsultarDisponibilidad(
    AutorizadorDeFuentes autorizador,
    IValidador<ConsultarDisponibilidadEntrada> validador,
    ILecturaCoherente lectura,
    IRepositorioDeFichas fichas,
    ConsultaDeExistencias existencias,
    IReloj reloj,
    CacheDeReferencias? referencias = null) : ICasoDeUso<ConsultarDisponibilidadEntrada, DisponibilidadConsultada>
{
    public async Task<Resultado<DisponibilidadConsultada>> Ejecutar(ConsultarDisponibilidadEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirContexto(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<DisponibilidadConsultada>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<DisponibilidadConsultada>(validacion);
        }

        var pedidas = entrada.EdicionIds.Distinct().ToList();

        var consulta = await lectura.EjecutarLectura(
            async token =>
            {
                var conFicha = await FichasDe(pedidas, token).ConfigureAwait(false);
                var impresas = pedidas.Where(id => conFicha.TryGetValue(id, out var ficha) && ficha.PuedeTenerStock).ToList();
                var disponibilidad = impresas.Count == 0
                    ? []
                    : (await existencias.DisponibilidadParaLaConsultaPublica(impresas, token).ConfigureAwait(false)).ToDictionary(d => d.EdicionId);

                return new DisponibilidadConsultada(
                    [.. impresas.Where(disponibilidad.ContainsKey).Select(id => Publica(disponibilidad[id]))],
                    [.. pedidas.Where(id => !conFicha.ContainsKey(id))],
                    [.. pedidas.Where(id => conFicha.TryGetValue(id, out var ficha) && !ficha.PuedeTenerStock)],
                    reloj.Ahora);
            },
            cancelacion).ConfigureAwait(false);

        return Resultado<DisponibilidadConsultada>.Exito(consulta);
    }

    /// <summary>Las fichas de las ediciones pedidas: las que están en la memoria de 5 s (<see cref="CacheDeReferencias"/>) no van a la base; las demás sí, y solo se guardan las que existen.</summary>
    private async Task<Dictionary<Guid, FichaDeEdicion>> FichasDe(List<Guid> pedidas, CancellationToken cancelacion)
    {
        var resultado = new Dictionary<Guid, FichaDeEdicion>();
        var faltan = new List<Guid>();
        foreach (var id in pedidas)
        {
            if (referencias?.Obtener<FichaDeEdicion>("ficha:" + id.ToString("N")) is { } guardada)
            {
                resultado[id] = guardada;
            }
            else
            {
                faltan.Add(id);
            }
        }

        if (faltan.Count > 0)
        {
            foreach (var ficha in await fichas.PorIds(faltan, cancelacion).ConfigureAwait(false))
            {
                resultado[ficha.EdicionId] = ficha;
                referencias?.Guardar("ficha:" + ficha.EdicionId.ToString("N"), ficha);
            }
        }

        return resultado;
    }

    private static DisponibilidadPublicaDeEdicion Publica(DisponibilidadDeEdicion disponibilidad) =>
        new(
            disponibilidad.EdicionId,
            disponibilidad.VendibleDeLaRed,
            [.. disponibilidad.PorFuente.Select(f => new VendibleEnFuente(f.CodigoDeFuente, f.NombreDeFuente, f.Vendible, f.Estado))]);
}

/// <summary>Las sucursales (código, nombre y región) que ve cualquier visitante, sin contacto ni cifras de stock, ordenadas por código. Exige un actor con contexto, como la disponibilidad.</summary>
internal sealed class ListarSucursales(
    AutorizadorDeFuentes autorizador,
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes) : ICasoDeUso<ListarSucursalesEntrada, IReadOnlyList<SucursalPublica>>
{
    public async Task<Resultado<IReadOnlyList<SucursalPublica>>> Ejecutar(ListarSucursalesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirContexto(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<IReadOnlyList<SucursalPublica>>(rechazo);
        }

        var lista = await lectura.EjecutarLectura(
            async token => (await fuentes.Listar(token).ConfigureAwait(false))
                .OrderBy(f => f.Codigo, StringComparer.Ordinal)
                .Select(f => new SucursalPublica(f.Codigo, f.Nombre, f.Region))
                .ToList(),
            cancelacion).ConfigureAwait(false);

        return Resultado<IReadOnlyList<SucursalPublica>>.Exito(lista);
    }
}
