using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// Solicita un traslado de reposición (TRA-01, TRA-20). Orden fijo (plan §5): (1) el rol (administración o encargado; logística y cliente, rechazados con acceso denegado), (2) la forma de la entrada, (3) el <b>presupuesto</b>
/// <c>traslados-del-personal</c> del usuario (solo aquí; transacción independiente: cuenta aunque se rechace), (4) el destino y el <b>alcance</b> sobre él (el encargado solo hacia su sucursal: otro destino, o uno que no existe, es
/// <c>INV-PROHIBIDO</c> con un único evento), (5) el origen, (6) la ficha de la edición, (7) las reglas de la solicitud con el vendible del origen (origen distinto de destino, cantidad positiva, edición impresa y cantidad no mayor
/// que el vendible, con un mensaje uniforme y sin cifras), (8) la fila <c>solicitado</c> con su número y (9) la auditoría y el aviso a las dos sucursales, en la misma transacción. No mueve stock: el vendible se lee, no se bloquea;
/// el despacho lo vuelve a comprobar con el stock bloqueado.
/// </summary>
internal sealed class SolicitarTraslado(
    AutorizadorDeTraslados autorizador,
    IValidador<SolicitarTrasladoEntrada> validador,
    PresupuestoDeTraslados presupuesto,
    IDirectorioDeFuentes fuentes,
    IFichasDeEdiciones fichas,
    IExistencias existencias,
    IRepositorioDeTraslados traslados,
    IBuzonDeSalida buzon,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<SolicitarTrasladoEntrada, TrasladoRegistrado>
{
    public async Task<Resultado<TrasladoRegistrado>> Ejecutar(SolicitarTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirRol(OperacionDeTraslado.Solicitar, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<TrasladoRegistrado>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeTraslados.Validacion<TrasladoRegistrado>(validacion);
        }

        var usuarioId = autorizador.UsuarioId!.Value;
        if (await presupuesto.Consumir(usuarioId, cancelacion).ConfigureAwait(false) is { } sinPresupuesto)
        {
            return FallosDeTraslados.Fallo<TrasladoRegistrado>(sinPresupuesto);
        }

        try
        {
            return await Solicitar(entrada, usuarioId, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeTraslados.BloqueoOcupado<TrasladoRegistrado>();
        }
    }

    private async Task<Resultado<TrasladoRegistrado>> Solicitar(SolicitarTrasladoEntrada entrada, Guid usuarioId, CancellationToken cancelacion)
    {
        var destino = await fuentes.PorCodigo(entrada.CodigoDeDestino, cancelacion).ConfigureAwait(false);
        var origen = await fuentes.PorCodigo(entrada.CodigoDeOrigen, cancelacion).ConfigureAwait(false);

        // El alcance se decide con el destino (TRA-09, TRA-20): un destino ajeno o inexistente es un 403 para el encargado, así no averigua qué códigos existen.
        var destinoId = destino?.Id ?? Guid.Empty;
        if (await autorizador.Exigir(OperacionDeTraslado.Solicitar, origen?.Id ?? Guid.Empty, destinoId, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return FallosDeTraslados.Fallo<TrasladoRegistrado>(fueraDeAlcance);
        }

        if (origen is null || destino is null)
        {
            return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeTraslados.FuenteNoEncontrada);
        }

        var ficha = await fichas.PorId(entrada.EdicionId, cancelacion).ConfigureAwait(false);
        if (ficha is null)
        {
            return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeTraslados.EdicionNoEncontrada);
        }

        var vendible = ficha.EsImpresa ? await VendibleDelOrigen(origen.Id, entrada.EdicionId, cancelacion).ConfigureAwait(false) : 0;
        if (ReglasDeSolicitud.Evaluar(origen.Id, destino.Id, entrada.Cantidad, ficha.EsImpresa, vendible) is { } motivo)
        {
            return Rechazar(motivo);
        }

        var ahora = reloj.Ahora;
        var traslado = Traslado.SolicitarReposicion(Guid.NewGuid(), entrada.EdicionId, origen.Id, destino.Id, entrada.Cantidad, entrada.Motivo, usuarioId, ahora);
        await traslados.Insertar(traslado, cancelacion).ConfigureAwait(false);

        await auditoria.Registrar(ConversionDeTraslados.Auditar(traslado, estadoAnterior: null), cancelacion).ConfigureAwait(false);
        await buzon.Insertar(ConversionDeTraslados.Mensajes(traslado, origen.Codigo, destino.Codigo, ahora), cancelacion).ConfigureAwait(false);

        return Resultado<TrasladoRegistrado>.Exito(ConversionDeTraslados.ARespuesta(traslado, origen, destino));
    }

    private async Task<int> VendibleDelOrigen(Guid origenId, Guid edicionId, CancellationToken cancelacion)
    {
        var disponibilidad = await existencias.Disponibilidad([edicionId], cancelacion).ConfigureAwait(false);
        return disponibilidad.FirstOrDefault(d => d.EdicionId == edicionId)?.PorFuente.FirstOrDefault(f => f.FuenteId == origenId)?.Vendible ?? 0;
    }

    /// <summary>
    /// Las solicitudes inválidas se rechazan con un mensaje uniforme que nunca lleva cifras de otras sucursales: origen igual al destino o cantidad no positiva, 400; edición digital, 422; cantidad mayor que el vendible del origen, 409
    /// <c>stock-insuficiente</c>.
    /// </summary>
    private static Resultado<TrasladoRegistrado> Rechazar(MotivoDeRechazoDeSolicitud motivo) =>
        motivo switch
        {
            MotivoDeRechazoDeSolicitud.MismoOrigenYDestino => FallosDeTraslados.Invalido<TrasladoRegistrado>("codigoDeDestino", ConstructorDeValidacion.FormatoInvalido),
            MotivoDeRechazoDeSolicitud.CantidadNoPositiva => FallosDeTraslados.Invalido<TrasladoRegistrado>("cantidad", ConstructorDeValidacion.FueraDeRango),
            MotivoDeRechazoDeSolicitud.EdicionNoImpresa => FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.EdicionNoImpresa),
            _ => FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente),
        };
}

/// <summary>
/// Las fuentes que pueden ser el origen de un traslado de una edición hacia un destino (TRA-02): solo las que tienen stock vendible, con su código y su nombre y <b>sin cantidades</b> (el encargado de un destino no averigua
/// cuánto tiene cada sucursal). Las de ruta más corta hacia el destino primero (las sin ruta al final) y luego por código. Las mismas reglas de alcance que la solicitud: administración, o el encargado solo para su sucursal.
/// </summary>
internal sealed class ConsultarOrigenesPosibles(
    AutorizadorDeTraslados autorizador,
    IValidador<ConsultarOrigenesPosiblesEntrada> validador,
    IDirectorioDeFuentes fuentes,
    IFichasDeEdiciones fichas,
    IExistencias existencias) : ICasoDeUso<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles>
{
    public async Task<Resultado<OrigenesPosibles>> Ejecutar(ConsultarOrigenesPosiblesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirRol(OperacionDeTraslado.Solicitar, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeTraslados.Fallo<OrigenesPosibles>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeTraslados.Validacion<OrigenesPosibles>(validacion);
        }

        var destino = await fuentes.PorCodigo(entrada.CodigoDeDestino, cancelacion).ConfigureAwait(false);
        if (await autorizador.Exigir(OperacionDeTraslado.Solicitar, Guid.Empty, destino?.Id ?? Guid.Empty, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
        {
            return FallosDeTraslados.Fallo<OrigenesPosibles>(fueraDeAlcance);
        }

        if (destino is null)
        {
            return FallosDeTraslados.De<OrigenesPosibles>(CategoriaDeError.NoEncontrado, ClavesDeTraslados.FuenteNoEncontrada);
        }

        var ficha = await fichas.PorId(entrada.EdicionId, cancelacion).ConfigureAwait(false);
        if (ficha is null)
        {
            return FallosDeTraslados.De<OrigenesPosibles>(CategoriaDeError.NoEncontrado, ClavesDeTraslados.EdicionNoEncontrada);
        }

        if (!ficha.EsImpresa)
        {
            return FallosDeTraslados.De<OrigenesPosibles>(CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.EdicionNoImpresa);
        }

        var disponibilidad = await existencias.Disponibilidad([entrada.EdicionId], cancelacion).ConfigureAwait(false);
        var conVendible = disponibilidad.FirstOrDefault(d => d.EdicionId == entrada.EdicionId)?.PorFuente.Where(f => f.Vendible > 0 && f.FuenteId != destino.Id).ToList() ?? [];
        var rutas = (await fuentes.RutasHaciaDestino(destino.Id, cancelacion).ConfigureAwait(false)).ToDictionary(r => r.OrigenId, r => r.PlazoEnDias);

        var origenes = conVendible
            .OrderBy(f => rutas.TryGetValue(f.FuenteId, out var plazo) ? plazo : int.MaxValue)
            .ThenBy(f => f.CodigoDeFuente, StringComparer.Ordinal)
            .Select(f => new OrigenPosible(f.CodigoDeFuente, f.NombreDeFuente))
            .ToList();
        return Resultado<OrigenesPosibles>.Exito(new OrigenesPosibles(origenes));
    }
}
