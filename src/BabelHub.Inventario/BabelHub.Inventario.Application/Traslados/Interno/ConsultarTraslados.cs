using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Las fuentes por identificador, leídas una vez por consulta: el código de origen y de destino de cada traslado que se devuelve.</summary>
internal static class CodigosDeFuentes
{
    public static async Task<IReadOnlyDictionary<Guid, FuenteDeStock>> Cargar(IDirectorioDeFuentes fuentes, CancellationToken cancelacion) =>
        (await fuentes.Listar(cancelacion).ConfigureAwait(false)).ToDictionary(f => f.Id);

    public static TrasladoRegistrado Convertir(Traslado traslado, IReadOnlyDictionary<Guid, FuenteDeStock> fuentes) =>
        fuentes.TryGetValue(traslado.OrigenId, out var origen) && fuentes.TryGetValue(traslado.DestinoId, out var destino)
            ? ConversionDeTraslados.ARespuesta(traslado, origen, destino)
            : throw new InvalidOperationException("Un traslado apunta a una fuente que no existe.");
}

/// <summary>
/// El detalle de un traslado (TRA-12, TRA-21): número, estados, fechas, guía y fecha estimada o «por confirmar». El alcance se compara en la Aplicación con la matriz de <see cref="PermisosDeTraslado"/>: un cliente u otro
/// rol, 403 con un único evento antes de leer; el encargado, solo si su sucursal es el origen o el destino (y un traslado que no ve o que no existe es el mismo 403, AM04); logística y administración, cualquiera (404 si no existe).
/// </summary>
internal sealed class ConsultarTraslado(
    AutorizadorDeTraslados autorizador,
    IValidador<ConsultarTrasladoEntrada> validador,
    IDirectorioDeFuentes fuentes,
    IRepositorioDeTraslados traslados) : ICasoDeUso<ConsultarTrasladoEntrada, TrasladoRegistrado>
{
    public async Task<Resultado<TrasladoRegistrado>> Ejecutar(ConsultarTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirRol(OperacionDeTraslado.Consultar, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<TrasladoRegistrado>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeTraslados.Validacion<TrasladoRegistrado>(validacion);
        }

        var traslado = await traslados.PorId(entrada.TrasladoId, cancelacion).ConfigureAwait(false);
        if (traslado is null)
        {
            return FallosDeTraslados.Fallo<TrasladoRegistrado>(await autorizador.TrasladoNoVisible(cancelacion).ConfigureAwait(false));
        }

        if (await autorizador.Exigir(OperacionDeTraslado.Consultar, traslado.OrigenId, traslado.DestinoId, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return FallosDeTraslados.Fallo<TrasladoRegistrado>(fueraDeAlcance);
        }

        return Resultado<TrasladoRegistrado>.Exito(CodigosDeFuentes.Convertir(traslado, await CodigosDeFuentes.Cargar(fuentes, cancelacion).ConfigureAwait(false)));
    }
}

/// <summary>
/// La lista de traslados con filtros (TRA-12): el encargado solo ve los de su sucursal como origen o destino (sin <c>codigoDeFuente</c> se filtra a la suya; con el de otra sucursal, o con uno que no existe, 403 con un único
/// evento, AUD-08); logística y administración ven todos y pueden filtrar por cualquier fuente (un código inexistente da una página vacía, no un 404); cliente y otros roles, 403 con evento. La demora mínima, en horas, filtra los
/// <c>solicitado</c> desde hace al menos ese tiempo (TRA-22).
/// </summary>
internal sealed class ListarTraslados(
    AutorizadorDeTraslados autorizador,
    IValidador<ListarTrasladosEntrada> validador,
    IDirectorioDeFuentes fuentes,
    IRepositorioDeTraslados traslados,
    IReloj reloj) : ICasoDeUso<ListarTrasladosEntrada, PaginaDeTraslados>
{
    public async Task<Resultado<PaginaDeTraslados>> Ejecutar(ListarTrasladosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirRol(OperacionDeTraslado.Consultar, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<PaginaDeTraslados>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeTraslados.Validacion<PaginaDeTraslados>(validacion);
        }

        var todas = await CodigosDeFuentes.Cargar(fuentes, cancelacion).ConfigureAwait(false);
        var pedida = entrada.CodigoDeFuente is null ? null : todas.Values.FirstOrDefault(f => f.Codigo == entrada.CodigoDeFuente);

        Guid? fuenteId = pedida?.Id;
        if (autorizador.Rol == RolDeTraslado.EncargadoDeSucursal)
        {
            // El alcance se compara aquí: el encargado solo pide su sucursal (la política de filas es la segunda capa).
            var propia = autorizador.Sucursal;
            if (entrada.CodigoDeFuente is null)
            {
                fuenteId = propia;
            }
            else if (pedida is null || propia != pedida.Id)
            {
                return FallosDeTraslados.Fallo<PaginaDeTraslados>(await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false));
            }
        }
        else if (entrada.CodigoDeFuente is not null && pedida is null)
        {
            return Resultado<PaginaDeTraslados>.Exito(new PaginaDeTraslados([], 0, entrada.Pagina, entrada.Tamano));
        }

        var filtro = new FiltroDeTraslados(
            entrada.Estado is null ? null : EstadosDeTraslado.DeIdentificador(entrada.Estado),
            fuenteId,
            entrada.Vinculado,
            entrada.DemoraMinimaEnHoras is { } horas ? reloj.Ahora.AddHours(-horas) : null);
        var pagina = await traslados.Listar(filtro, entrada.Pagina, entrada.Tamano, cancelacion).ConfigureAwait(false);

        return Resultado<PaginaDeTraslados>.Exito(new PaginaDeTraslados([.. pagina.Elementos.Select(t => CodigosDeFuentes.Convertir(t, todas))], pagina.Total, entrada.Pagina, entrada.Tamano));
    }
}

/// <summary>
/// Los traslados de un pedido (TRA-11): el personal con el mismo alcance que el detalle. El encargado solo recibe los que tienen a su sucursal como origen o destino (también se filtra aquí, además de la política de filas);
/// logística y administración, todos; cliente y otros roles, 403 con evento. Un pedido sin traslados da una lista vacía.
/// </summary>
internal sealed class ConsultarTrasladosDeUnPedido(
    AutorizadorDeTraslados autorizador,
    IValidador<ConsultarTrasladosDeUnPedidoEntrada> validador,
    IDirectorioDeFuentes fuentes,
    IRepositorioDeTraslados traslados) : ICasoDeUso<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido>
{
    public async Task<Resultado<TrasladosDeUnPedido>> Ejecutar(ConsultarTrasladosDeUnPedidoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirRol(OperacionDeTraslado.Consultar, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<TrasladosDeUnPedido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeTraslados.Validacion<TrasladosDeUnPedido>(validacion);
        }

        var todos = await traslados.DelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        var visibles = autorizador.Rol == RolDeTraslado.EncargadoDeSucursal
            ? todos.Where(t => autorizador.Sucursal is { } s && (t.OrigenId == s || t.DestinoId == s)).ToList()
            : todos.ToList();

        var codigos = await CodigosDeFuentes.Cargar(fuentes, cancelacion).ConfigureAwait(false);
        return Resultado<TrasladosDeUnPedido>.Exito(new TrasladosDeUnPedido([.. visibles.Select(t => CodigosDeFuentes.Convertir(t, codigos))]));
    }
}
