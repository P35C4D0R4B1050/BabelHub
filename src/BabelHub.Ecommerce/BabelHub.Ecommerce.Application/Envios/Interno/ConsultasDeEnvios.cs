using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>El listado de logística (T09; ENV-08): solo logística y la administración central, por fecha de creación descendente, con el tamaño de página topado en 50 y <b>sin ningún dato de contacto</b>.</summary>
internal sealed class ListarEnvios(AutorizadorDeEnvios autorizador, IRepositorioDeEnvios envios) : ICasoDeUso<ListarEnviosEntrada, PaginaDeEnvios>
{
    public const int TamanoMaximo = 50;

    public async Task<Resultado<PaginaDeEnvios>> Ejecutar(ListarEnviosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirLogisticaOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeEnvios.Fallo<PaginaDeEnvios>(rechazo);
        }

        var pagina = Math.Max(1, entrada.Pagina);
        var tamano = Math.Clamp(entrada.Tamano, 1, TamanoMaximo);
        EstadoDeEnvio? estado = entrada.Estado is { } e ? (EstadoDeEnvio)(int)e : null;
        try
        {
            var listado = await envios.Listar(estado, pagina, tamano, cancelacion).ConfigureAwait(false);
            var filas = listado.Envios
                .Select(x => new ResumenDeEnvio(x.Id, x.PedidoId, x.CodigoDeFuente, MapeoDeEnvios.Estado(x.Estado), x.NumeroDeGuia?.ToString(), x.Operador, x.Lineas.Count, x.CreadoEn, x.GuiaGeneradaEn, x.EnRepartoEn, x.EntregadoEn))
                .ToList();
            return Resultado<PaginaDeEnvios>.Exito(new PaginaDeEnvios(filas, pagina, tamano, listado.Total));
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<PaginaDeEnvios>();
        }
    }
}

/// <summary>Un envío para logística (T09; ENV-08): en el orden rol → envío. Para logística y la administración central un identificador inexistente solo es «no encontrado», sin evento (alcance global).</summary>
internal sealed class ObtenerEnvio(AutorizadorDeEnvios autorizador, IRepositorioDeEnvios envios) : ICasoDeUso<Guid, DetalleDeEnvio>
{
    public async Task<Resultado<DetalleDeEnvio>> Ejecutar(Guid envioId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirLogisticaOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeEnvios.Fallo<DetalleDeEnvio>(rechazo);
        }

        try
        {
            var envio = await envios.Obtener(envioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            return envio is null
                ? FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado)
                : Resultado<DetalleDeEnvio>.Exito(MapeoDeEnvios.Detalle(envio));
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
        }
    }
}

/// <summary>
/// Los envíos de un pedido a domicilio del titular (T09; ENV-05): estado y guía de cada uno, o ninguno si todavía no se creó (la Web muestra «pendiente de guía»). La propiedad se comprueba con el contrato de lectura
/// de Pedidos; un pedido <b>ajeno o inexistente responde igual</b> (<c>ECO-NO-ENCONTRADO</c>, PED-41) con un acceso denegado por cada intento. Logística y la administración central también pueden consultarlo (alcance
/// global: un pedido inexistente solo es «no encontrado»); cualquier otro rol recibe «prohibido» con un acceso denegado.
/// </summary>
internal sealed class ObtenerEnviosDeUnPedido(IActorActual actores, IDenegacionDeAcceso denegaciones, IPedidosParaEnvios pedidos, IRepositorioDeEnvios envios) : ICasoDeUso<Guid, EnviosDeMiPedido>
{
    public async Task<Resultado<EnviosDeMiPedido>> Ejecutar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        var actor = actores.Actual is { Tipo: not TipoDeActor.Anonimo } a ? a : null;
        if (actor is null)
        {
            await Denegar(MotivosDeDenegacionDeEnvios.SinSesion, "ECO-NO-AUTENTICADO", cancelacion).ConfigureAwait(false);
            return FallosDeEnvios.De<EnviosDeMiPedido>(CategoriaDeError.NoAutenticado, ClavesDeEnvios.NoAutenticado);
        }

        var esPersonal = RolesDeEnvios.PuedeOperarEnvios(actor);
        var esCliente = actor is { Tipo: TipoDeActor.Usuario, UsuarioId: not null, Rol: RolesDeEnvios.Cliente };
        if (!esPersonal && !esCliente)
        {
            await Denegar(MotivosDeDenegacionDeEnvios.FueraDeRol, "ECO-PROHIBIDO", cancelacion).ConfigureAwait(false);
            return FallosDeEnvios.De<EnviosDeMiPedido>(CategoriaDeError.Prohibido, ClavesDeEnvios.Prohibido);
        }

        var lectura = await pedidos.Obtener(pedidoId, cancelacion).ConfigureAwait(false);
        if (!lectura.EsExito)
        {
            if (lectura.Error!.Categoria != CategoriaDeError.NoEncontrado)
            {
                return FallosDeEnvios.NoDisponible<EnviosDeMiPedido>();
            }

            return await NoEncontrado(esCliente, cancelacion).ConfigureAwait(false);
        }

        // La cuenta opaca del pedido es el identificador del titular. Un pedido ajeno responde como uno inexistente.
        if (esCliente && lectura.Valor.CuentaId != actor.UsuarioId)
        {
            return await NoEncontrado(true, cancelacion).ConfigureAwait(false);
        }

        try
        {
            var delPedido = await envios.ObtenerDelPedido(pedidoId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            var filas = delPedido
                .Select(e => new EnvioDeMiPedido(e.Id, e.CodigoDeFuente, MapeoDeEnvios.Estado(e.Estado), e.NumeroDeGuia?.ToString(), e.Operador, e.EntregadoEn))
                .ToList();
            return Resultado<EnviosDeMiPedido>.Exito(new EnviosDeMiPedido(pedidoId, filas));
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<EnviosDeMiPedido>();
        }
    }

    private async Task<Resultado<EnviosDeMiPedido>> NoEncontrado(bool conDenegacion, CancellationToken cancelacion)
    {
        if (conDenegacion)
        {
            await Denegar(MotivosDeDenegacionDeEnvios.RecursoAjeno, "ECO-NO-ENCONTRADO", cancelacion).ConfigureAwait(false);
        }

        return FallosDeEnvios.De<EnviosDeMiPedido>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.PedidoNoEncontrado);
    }

    private Task Denegar(string motivo, string codigoDeError, CancellationToken cancelacion) =>
        denegaciones.Registrar(new DenegacionDeAcceso(motivo, ruta: null) { CodigoDeError = codigoDeError }, cancelacion);
}

/// <summary>La implementación de <see cref="IEnviosDeLogistica"/> por los casos de uso de Envíos.</summary>
internal sealed class EnviosDeLogisticaDeLaAplicacion(ListarEnvios listar, ObtenerEnvio obtener, SolicitarGuia solicitarGuia, PasarAEnReparto pasarAEnReparto, MarcarEntregado marcarEntregado) : IEnviosDeLogistica
{
    public Task<Resultado<PaginaDeEnvios>> Listar(ListarEnviosEntrada entrada, CancellationToken cancelacion = default) => listar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<DetalleDeEnvio>> Obtener(Guid envioId, CancellationToken cancelacion = default) => obtener.Ejecutar(envioId, cancelacion);

    public Task<Resultado<DetalleDeEnvio>> SolicitarGuia(Guid envioId, CancellationToken cancelacion = default) => solicitarGuia.Ejecutar(envioId, cancelacion);

    public Task<Resultado<DetalleDeEnvio>> PasarAEnReparto(Guid envioId, CancellationToken cancelacion = default) => pasarAEnReparto.Ejecutar(envioId, cancelacion);

    public Task<Resultado<DetalleDeEnvio>> MarcarEntregado(Guid envioId, CancellationToken cancelacion = default) => marcarEntregado.Ejecutar(envioId, cancelacion);
}

/// <summary>La implementación de <see cref="IEnviosDelCliente"/>.</summary>
internal sealed class EnviosDelClienteDeLaAplicacion(ObtenerEnviosDeUnPedido obtener) : IEnviosDelCliente
{
    public Task<Resultado<EnviosDeMiPedido>> ObtenerDeUnPedido(Guid pedidoId, CancellationToken cancelacion = default) => obtener.Ejecutar(pedidoId, cancelacion);
}
