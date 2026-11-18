using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// El <b>orquestador sin transacción</b> (D15): encadena, <b>fuera</b> de toda transacción, el registro de la edición impresa en el Inventario, el ingreso y el marcado de su resultado en una transacción corta aparte. «El
/// Inventario no responde» es un resultado de éxito con aviso, nunca un fallo: el alta o el ingreso ya confirmados no se revierten por él (CAT-20). Todo con un presupuesto total de 15 segundos.
/// </summary>
internal sealed class OrquestadorDeIngresos(IInventarioDelCatalogo inventario, ICasoDeUso<MarcarResultadoDeIngresoEntrada, EstadoDeIngresoSolicitado> marcar)
{
    public static readonly TimeSpan Presupuesto = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Pide al Inventario el ingreso de la fila: primero registra la ficha de la edición impresa (D11: también antes de cada ingreso); si no se pudo confirmar, <b>nada se envió</b> y en el primer envío la fila pasa a
    /// <c>Fallido</c> (en un reintento, como un intento anterior pudo aplicarse, sigue <c>Pendiente</c>). Después envía el ingreso con la clave y la carga de la fila y marca el resultado según
    /// <see cref="ClasificadorDeIngreso"/>.
    /// </summary>
    public async Task<EstadoDeIngresoSolicitado> Enviar(IngresoSolicitado fila, bool esReintento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(fila);
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(Presupuesto);

        if (!await inventario.RegistrarEdicionImpresa(fila.EdicionId, plazo.Token).ConfigureAwait(false))
        {
            return await Marcar(fila.Id, esReintento ? ClasificacionDeIngreso.Pendiente : ClasificacionDeIngreso.Fallido, null, cancelacion).ConfigureAwait(false);
        }

        var respuesta = await inventario.RegistrarIngreso(new SolicitudDeIngresoAlInventario(fila.EdicionId, fila.ClaveDeIdempotencia, fila.Cantidad, fila.Referencia, fila.Nota), plazo.Token).ConfigureAwait(false);
        return await Marcar(fila.Id, ClasificadorDeIngreso.Clasificar(respuesta, esReintento), respuesta.IngresoId, cancelacion).ConfigureAwait(false);
    }

    /// <summary>Registra la ficha de una edición impresa tras el commit del alta, con o sin stock inicial y sin hacer fallar el alta (D11); <see langword="false"/> si el Inventario no la confirmó.</summary>
    public async Task<bool> RegistrarFicha(Guid edicionId, CancellationToken cancelacion)
    {
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        plazo.CancelAfter(Presupuesto);
        return await inventario.RegistrarEdicionImpresa(edicionId, plazo.Token).ConfigureAwait(false);
    }

    private async Task<EstadoDeIngresoSolicitado> Marcar(Guid ingresoSolicitadoId, ClasificacionDeIngreso clasificacion, Guid? ingresoId, CancellationToken cancelacion)
    {
        var marcado = await marcar.Ejecutar(new MarcarResultadoDeIngresoEntrada(ingresoSolicitadoId, clasificacion, ingresoId), cancelacion).ConfigureAwait(false);
        return marcado.EsExito ? marcado.Valor : EstadoDeIngresoSolicitado.Pendiente;
    }
}

/// <summary><c>EmitirClaveDeIngreso</c>: emite el token firmado al <b>mostrar</b> el formulario de ingreso (un <c>GET</c> que no escribe nada). Solo administración central; la edición debe ser una impresa no dada de baja.</summary>
internal sealed class EmitirClaveDeIngreso(
    AutorizadorDelCatalogo autorizador,
    IValidador<EmitirClaveDeIngresoEntrada> validador,
    IRepositorioDeObras obras,
    IEmisorDeClavesDeIngreso emisor,
    IReloj reloj) : ICasoDeUso<EmitirClaveDeIngresoEntrada, ClaveDeIngresoEmitida>
{
    public async Task<Resultado<ClaveDeIngresoEmitida>> Ejecutar(EmitirClaveDeIngresoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ClaveDeIngresoEmitida>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ClaveDeIngresoEmitida>(validacion);
        }

        var edicion = (await obras.ObtenerPorEdicion(entrada.EdicionId, paraActualizar: false, cancelacion).ConfigureAwait(false))?.BuscarEdicion(entrada.EdicionId);
        if (edicion is null)
        {
            return FallosDelCatalogo.Fallo<ClaveDeIngresoEmitida>(TraductorDeRechazos.De(new RechazoDeObra(RechazoDeCatalogo.EdicionNoEncontrada)));
        }

        if (edicion.EsDigital || edicion.EstaDadaDeBaja)
        {
            return FallosDelCatalogo.Validacion<ClaveDeIngresoEmitida>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("edicionId", "solo-edicion-impresa-activa")]));
        }

        var token = emisor.Emitir(autorizador.UsuarioId!.Value, entrada.EdicionId, reloj.Ahora);
        return Resultado<ClaveDeIngresoEmitida>.Exito(new ClaveDeIngresoEmitida(token.Token, token.VenceEn));
    }
}

/// <summary>
/// <c>ListarIngresos</c> (CAT-22): los últimos ingresos de una edición con su fecha y su usuario, del Inventario (hasta 50), más los pedidos aquí que aún no constan (<c>Pendiente</c>, con el aviso «ingreso pendiente de …») y los
/// fallidos. Si el Inventario no responde (o responde ingresos de otra edición, SEG-13) la lista trae solo lo local y lo avisa; nunca falla por eso. Solo administración central.
/// </summary>
internal sealed class ListarIngresos(
    AutorizadorDelCatalogo autorizador,
    IValidador<ListarIngresosEntrada> validador,
    IRepositorioDeObras obras,
    IRepositorioDeIngresos ingresos,
    IInventarioDelCatalogo inventario) : ICasoDeUso<ListarIngresosEntrada, HistorialDeIngresos>
{
    public const int Maximo = 50;

    public async Task<Resultado<HistorialDeIngresos>> Ejecutar(ListarIngresosEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<HistorialDeIngresos>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<HistorialDeIngresos>(validacion);
        }

        if ((await obras.ObtenerPorEdicion(entrada.EdicionId, paraActualizar: false, cancelacion).ConfigureAwait(false))?.BuscarEdicion(entrada.EdicionId) is null)
        {
            return FallosDelCatalogo.Fallo<HistorialDeIngresos>(TraductorDeRechazos.De(new RechazoDeObra(RechazoDeCatalogo.EdicionNoEncontrada)));
        }

        var remotos = await inventario.ListarIngresos(entrada.EdicionId, Maximo, cancelacion).ConfigureAwait(false);
        if (remotos is not null && remotos.Any(i => i.EdicionId != entrada.EdicionId))
        {
            remotos = null;
        }

        var locales = await ingresos.DeLaEdicion(entrada.EdicionId, Maximo, cancelacion).ConfigureAwait(false);
        var lista = new List<IngresoDeLaLista>();
        if (remotos is not null)
        {
            lista.AddRange(remotos.Select(i => new IngresoDeLaLista(i.IngresoId, null, i.Cantidad, i.Referencia, i.UsuarioId, i.Instante, null)));
        }

        foreach (var local in locales)
        {
            // Lo que ya consta en el Inventario no se repite; si el Inventario no respondió, lo registrado aquí sirve de respaldo.
            var yaConsta = remotos is not null && local.IngresoId is { } id && remotos.Any(r => r.IngresoId == id);
            if (!yaConsta && (local.Estado != EstadoDeIngreso.Registrado || remotos is null))
            {
                lista.Add(new IngresoDeLaLista(local.IngresoId, local.Id, local.Cantidad, local.Referencia, local.UsuarioId == Guid.Empty ? null : local.UsuarioId, local.SolicitadoEn, MarcarResultadoDeIngreso.ADelDominio(local.Estado)));
            }
        }

        return Resultado<HistorialDeIngresos>.Exito(new HistorialDeIngresos(entrada.EdicionId, lista.OrderByDescending(i => i.Instante).Take(Maximo).ToList(), remotos is null));
    }
}

/// <summary>
/// <c>ReintentarIngresoPendiente</c> (CAT-22, D04): un <c>Pendiente</c> se reintenta con <b>su</b> clave y su carga (nunca una nueva) y <b>solo por el usuario de la fila</b> (el ámbito de la clave en el Inventario incluye al
/// usuario); otro administrador recibe «prohibido» con el acceso denegado registrado. El rol se comprueba <b>antes</b> de buscar la fila; una fila de otra edición es «no encontrada»; una que ya no está <c>Pendiente</c>,
/// <c>conflicto</c>; una de más de 24 horas, regla de negocio (se concilia). El primer paso (validar y anotar el intento) es una transacción corta; la llamada al Inventario la hace el orquestador sin transacción y en un
/// reintento todo resultado distinto de 201 deja la fila <c>Pendiente</c>. Un Inventario que no responde es éxito (<c>Pendiente</c>), no un fallo.
/// </summary>
internal sealed class ReintentarIngresoPendiente(
    AutorizadorDelCatalogo autorizador,
    IValidador<ReintentarIngresoEntrada> validador,
    IRepositorioDeIngresos ingresos,
    IUnidadDeTrabajo unidad,
    OrquestadorDeIngresos orquestador,
    IReloj reloj) : ICasoDeUso<ReintentarIngresoEntrada, ResultadoDeIngreso>
{
    public async Task<Resultado<ResultadoDeIngreso>> Ejecutar(ReintentarIngresoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeIngreso>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeIngreso>(validacion);
        }

        var preparado = await unidad.Ejecutar(token => Preparar(entrada, token), cancelacion).ConfigureAwait(false);
        if (!preparado.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeIngreso>(preparado.Error!);
        }

        var fila = preparado.Valor;
        var estado = await orquestador.Enviar(fila, esReintento: true, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeIngreso>.Exito(new ResultadoDeIngreso(fila.Id, fila.EdicionId, estado));
    }

    private async Task<Resultado<IngresoSolicitado>> Preparar(ReintentarIngresoEntrada entrada, CancellationToken cancelacion)
    {
        var fila = await ingresos.Obtener(entrada.IngresoSolicitadoId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (fila is null || (entrada.EdicionId is { } edicion && fila.EdicionId != edicion))
        {
            return FallosDelCatalogo.De<IngresoSolicitado>(CategoriaDeError.NoEncontrado, ClavesDelCatalogo.IngresoNoEncontrado);
        }

        if (!fila.EsDelUsuario(autorizador.UsuarioId!.Value))
        {
            return FallosDelCatalogo.Fallo<IngresoSolicitado>(await autorizador.RechazarRecursoAjeno(cancelacion).ConfigureAwait(false));
        }

        if (!fila.EstaPendiente)
        {
            return FallosDelCatalogo.De<IngresoSolicitado>(CategoriaDeError.Conflicto, ClavesDelCatalogo.IngresoYaResuelto);
        }

        if (fila.ReintentoVencido(reloj.Ahora))
        {
            return FallosDelCatalogo.De<IngresoSolicitado>(CategoriaDeError.ReglaDeNegocio, ClavesDelCatalogo.ReglaDeNegocio, [new ErrorDeCampo("ingresoId", "pendiente-vencido")]);
        }

        fila.RegistrarReintento(reloj.Ahora);
        return await ingresos.GuardarTransicion(fila, cancelacion).ConfigureAwait(false)
            ? Resultado<IngresoSolicitado>.Exito(fila)
            : FallosDelCatalogo.De<IngresoSolicitado>(CategoriaDeError.Conflicto, ClavesDelCatalogo.IngresoYaResuelto);
    }
}

/// <summary>
/// El ingreso de mercadería desde el catálogo (CAT-20, CAT-22): <b>no transaccional</b>. Registrar emite la fila <c>Pendiente</c> en su transacción, después llama al Inventario y marca el resultado en otra; la repetición de
/// la misma clave devuelve su estado sin enviar otra vez. Cada operación comprueba el rol de administración central.
/// </summary>
internal sealed class IngresosDeMercaderiaDeLaAplicacion(
    ICasoDeUso<EmitirClaveDeIngresoEntrada, ClaveDeIngresoEmitida> emitir,
    ICasoDeUso<RegistrarIngresoEntrada, IngresoPedido> pedir,
    OrquestadorDeIngresos orquestador,
    ICasoDeUso<ReintentarIngresoEntrada, ResultadoDeIngreso> reintentar,
    ICasoDeUso<ConciliarIngresoEntrada, ResultadoDeIngreso> conciliar,
    ICasoDeUso<ListarIngresosEntrada, HistorialDeIngresos> listar) : IIngresosDeMercaderia
{
    public Task<Resultado<ClaveDeIngresoEmitida>> EmitirClave(EmitirClaveDeIngresoEntrada entrada, CancellationToken cancelacion = default) => emitir.Ejecutar(entrada, cancelacion);

    public async Task<Resultado<ResultadoDeIngreso>> Registrar(RegistrarIngresoEntrada entrada, CancellationToken cancelacion = default)
    {
        var pedido = await pedir.Ejecutar(entrada, cancelacion).ConfigureAwait(false);
        if (!pedido.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeIngreso>(pedido.Error!);
        }

        var fila = pedido.Valor.Fila;
        var estado = pedido.Valor.EsNueva
            ? await orquestador.Enviar(fila, esReintento: false, cancelacion).ConfigureAwait(false)
            : MarcarResultadoDeIngreso.ADelDominio(fila.Estado);
        return Resultado<ResultadoDeIngreso>.Exito(new ResultadoDeIngreso(fila.Id, fila.EdicionId, estado));
    }

    public Task<Resultado<ResultadoDeIngreso>> Reintentar(ReintentarIngresoEntrada entrada, CancellationToken cancelacion = default) => reintentar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeIngreso>> Conciliar(ConciliarIngresoEntrada entrada, CancellationToken cancelacion = default) => conciliar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<HistorialDeIngresos>> Listar(ListarIngresosEntrada entrada, CancellationToken cancelacion = default) => listar.Ejecutar(entrada, cancelacion);
}
