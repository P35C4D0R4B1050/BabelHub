using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// Una fuente con sus totales de stock y su contacto (FUE-11): solo la administración central. Es una consulta pura: este caso de uso no depende de ninguna operación que
/// escriba stock (<see cref="IMovimientosDeStock"/>, <see cref="IAplicadorDeMovimientos"/>), así que desde esa vista no se puede modificar el stock (AC-FUE-09). El
/// encargado asignado lo compone la web con Identidad (D08).
/// </summary>
internal sealed class ConsultarFuente(
    AutorizadorDeFuentes autorizador,
    IValidador<ConsultarFuenteEntrada> validador,
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes,
    TotalesDeFuente totales) : ICasoDeUso<ConsultarFuenteEntrada, DetalleDeFuente>
{
    public async Task<Resultado<DetalleDeFuente>> Ejecutar(ConsultarFuenteEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<DetalleDeFuente>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<DetalleDeFuente>(validacion);
        }

        return await lectura.EjecutarLectura(
            async token =>
            {
                var fuente = await fuentes.PorCodigo(entrada.Codigo, token).ConfigureAwait(false);
                if (fuente is null)
                {
                    return FallosDeFuentes.NoEncontrada<DetalleDeFuente>();
                }

                var (fisico, reservado, vendible) = await totales.De(fuente, token).ConfigureAwait(false);
                return Resultado<DetalleDeFuente>.Exito(RegistrarFuente.Detalle(fuente, fisico, reservado, vendible));
            },
            cancelacion).ConfigureAwait(false);
    }
}

/// <summary>
/// Las fuentes visibles para quien consulta (FUE-08): la administración central ve todas y el encargado solo la suya; logística y cliente se rechazan con su evento.
/// Sin totales ni contacto: eso es <see cref="ConsultarFuente"/>.
/// </summary>
internal sealed class ListarFuentes(
    AutorizadorDeFuentes autorizador,
    ILecturaCoherente lectura,
    IRepositorioDeFuentes fuentes) : ICasoDeUso<ListarFuentesEntrada, IReadOnlyList<FuenteDeStock>>
{
    public async Task<Resultado<IReadOnlyList<FuenteDeStock>>> Ejecutar(ListarFuentesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<IReadOnlyList<FuenteDeStock>>(rechazo);
        }

        // El actor ya se comprobó: una administración ve la red completa y un encargado, solo su sucursal (un SucursalId nulo no ve ninguna).
        var actor = autorizador.Actor!;
        var visibles = await lectura.EjecutarLectura(
            async token => (await fuentes.Listar(token).ConfigureAwait(false))
                .Where(f => actor.EsAdministracionCentral() || actor.EsEncargadoDe(f.Id))
                .OrderBy(f => f.Codigo, StringComparer.Ordinal)
                .Select(f => new FuenteDeStock(f.Id, f.Codigo, f.Nombre, f.Region, f.EsRecepcion))
                .ToList(),
            cancelacion).ConfigureAwait(false);

        return Resultado<IReadOnlyList<FuenteDeStock>>.Exito(visibles);
    }
}

/// <summary>
/// Las existencias de una fuente (FUE-09, FUE-18): físico, reservado, vendible y estado de las ediciones que el Catálogo resolvió (filtro por <c>edicionId</c>, hasta 50) y
/// por estado, paginadas de 1 a 100. El alcance se compara <b>en la Aplicación</b> (FUE-08): la administración central o el encargado de esa fuente; el encargado de otra
/// fuente, logística y cliente reciben «prohibido» con su evento. El 403 va antes que el 404: para un encargado, una fuente inexistente se rechaza igual que una ajena, de
/// modo que no hay forma de preguntar qué códigos existen. La búsqueda por título, autor o ISBN la compone la web con el Catálogo (D03).
/// </summary>
internal sealed class ConsultarExistenciasDeSucursal(
    AutorizadorDeFuentes autorizador,
    IValidador<ConsultarExistenciasDeSucursalEntrada> validador,
    IRepositorioDeFuentes fuentes,
    IExistencias existencias) : ICasoDeUso<ConsultarExistenciasDeSucursalEntrada, PaginaDeExistencias>
{
    public async Task<Resultado<PaginaDeExistencias>> Ejecutar(ConsultarExistenciasDeSucursalEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // Primero el rol (sin leer nada: un visitante, un cliente o logística no provocan consultas); la fuente se resuelve solo para comparar el alcance fino.
        if (await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false) is { } sinRol)
        {
            return FallosDeFuentes.Fallo<PaginaDeExistencias>(sinRol);
        }

        var fuente = Fuente.EsCodigoValido(entrada.CodigoDeFuente)
            ? await fuentes.PorCodigo(entrada.CodigoDeFuente, cancelacion).ConfigureAwait(false)
            : null;

        if (await autorizador.ExigirFuente(fuente?.Id, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<PaginaDeExistencias>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<PaginaDeExistencias>(validacion);
        }

        if (fuente is null)
        {
            return FallosDeFuentes.NoEncontrada<PaginaDeExistencias>();
        }

        var pagina = await existencias
            .DeLaFuente(
                new ConsultaDeExistenciasDeFuente(fuente.Id, entrada.EdicionIds?.Distinct().ToList(), entrada.Estado, entrada.Pagina, entrada.Tamano),
                cancelacion)
            .ConfigureAwait(false);

        return Resultado<PaginaDeExistencias>.Exito(pagina);
    }
}
