using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeConfirmar : IValidador<ConfirmarReservasEntrada>
{
    public ResultadoDeValidacion Validar(ConfirmarReservasEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.PedidoId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio));
        }

        ReglasDeEntradaDeReservas.Clave(errores, entrada.ClaveDeIdempotencia);
        return ReglasDeEntradaDeReservas.Resultado(errores);
    }
}

/// <summary>
/// Confirma las reservas de un pedido pagado (SEL-06): solo la integración <c>expiracion-de-reservas</c> (sin token), bajo el bloqueo del pedido. Las líneas pendientes no vencidas pasan a confirmadas con
/// <c>confirmada_en</c> y la reserva deja de expirar; repetirla (con cualquier clave) cuando todo está confirmado es un éxito sin cambios, porque la confirmación de lo ya confirmado no cambia nada: la clave de
/// idempotencia solo se valida y el resultado es el mismo. Una reserva vencida, expirada o liberada se rechaza y obliga a reiniciar el pago. Si es de recojo, <b>en la misma transacción</b> pide a <c>Traslados</c>
/// (<see cref="IGeneradorDeTrasladosDePedido"/>) un traslado por cada fuente y edición cuya fuente no es la sucursal de recojo, fija el <c>traslado_id</c> de cada línea y devuelve los traslados vinculados, creados o
/// preexistentes, también al repetir (SEL-03). Cada línea confirmada se audita en la misma transacción: si no se puede auditar, no se confirma (AC-SEL-32). Un volumen anómalo de confirmaciones solo avisa.
/// </summary>
internal sealed class ConfirmarReservas(
    AutorizadorDeReservas autorizador,
    IValidador<ConfirmarReservasEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    IDirectorioDeFuentes directorio,
    IGeneradorDeTrasladosDePedido traslados,
    IAuditoria auditoria,
    AlertasDeVolumenDeReservas volumen,
    ConstructorDeLaReserva constructor,
    IReloj reloj) : ICasoDeUso<ConfirmarReservasEntrada, ReservaDelPedido>
{
    public async Task<Resultado<ReservaDelPedido>> Ejecutar(ConfirmarReservasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirIntegracionDeReservas(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ReservaDelPedido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<ReservaDelPedido>(validacion);
        }

        try
        {
            return await Confirmar(entrada, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeReservas.BloqueoOcupado<ReservaDelPedido>();
        }
    }

    private async Task<Resultado<ReservaDelPedido>> Confirmar(ConfirmarReservasEntrada entrada, CancellationToken cancelacion)
    {
        await bloqueos.Pedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);

        var ahora = reloj.Ahora;
        var actual = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (actual is null)
        {
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
        }

        bool hayCambio;
        try
        {
            // El Dominio decide: todo confirmado o cerrado no cambia nada; vencida, expirada o liberada obliga a reiniciar el pago.
            hayCambio = actual.Confirmar(ahora);
        }
        catch (TransicionDeReservaInvalidaException)
        {
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
        }

        if (hayCambio)
        {
            var pendientes = actual.Lineas.Count(l => l.Estado == EstadoDeLinea.Confirmada && l.ConfirmadaEn == ahora);
            var confirmadas = await reservas.ConfirmarLineas(entrada.PedidoId, ahora, cancelacion).ConfigureAwait(false);
            if (confirmadas.Count != pendientes)
            {
                // Una línea cambió entre la lectura y la sentencia: no se confirma a medias.
                return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
            }

            foreach (var linea in confirmadas)
            {
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.ReservaConfirmada)
                    {
                        ObjetoTipo = "pedido",
                        ObjetoId = linea.PedidoId.ToString("D"),
                        SucursalOrigen = linea.FuenteId,
                        Detalle = new Dictionary<string, string?>
                        {
                            ["edicionId"] = linea.EdicionId.ToString("D"),
                            ["cantidad"] = linea.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                    },
                    cancelacion).ConfigureAwait(false);
            }

            await volumen.Registrar(VolumenDeOperacionesDeReservas.Confirmaciones, VolumenDeOperacionesDeReservas.MotivoDeConfirmaciones, confirmadas.Count, cancelacion).ConfigureAwait(false);
        }

        var vinculados = await SolicitarTraslados(actual, cancelacion).ConfigureAwait(false);

        // Se vuelve a leer: lo cargado antes de la sentencia no trae el estado ni los traslados que quedaron.
        var guardada = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false) ?? actual;
        return Resultado<ReservaDelPedido>.Exito(await constructor.Construir(guardada, esRepeticion: !hayCambio, cancelacion, vinculados).ConfigureAwait(false));
    }

    /// <summary>Un traslado por cada fuente y edición cuya fuente no es la sucursal de recojo (nunca a domicilio, SEL-24), en la misma transacción; los devuelve y fija el <c>traslado_id</c> de cada línea.</summary>
    private async Task<IReadOnlyList<TrasladoVinculadoAlPedido>> SolicitarTraslados(ReservaDePedido reserva, CancellationToken cancelacion)
    {
        if (!reserva.EsDeRecojo || reserva.SucursalDeRecojoId is not { } sucursal)
        {
            return [];
        }

        var lineas = reserva.Lineas.Where(l => l.Estado == EstadoDeLinea.Confirmada && l.FuenteId != sucursal).ToList();
        if (lineas.Count == 0)
        {
            return [];
        }

        var solicitudes = lineas
            .GroupBy(l => (l.FuenteId, l.EdicionId))
            .Select(g => new SolicitudDeTrasladoDePedido(g.Key.FuenteId, g.Key.EdicionId, checked(g.Sum(l => l.Cantidad))))
            .OrderBy(s => s.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(s => s.FuenteDeOrigenId, OrdenDeBloqueo.DeIdentificadores)
            .ToList();

        var generados = await traslados.Generar(reserva.PedidoId, sucursal, solicitudes, cancelacion).ConfigureAwait(false);

        var codigos = (await directorio.Listar(cancelacion).ConfigureAwait(false)).ToDictionary(f => f.Codigo, f => f.Id, StringComparer.Ordinal);
        var vinculos = new List<(Guid LineaDeReservaId, Guid TrasladoId)>();
        foreach (var traslado in generados)
        {
            if (!codigos.TryGetValue(traslado.CodigoDeOrigen, out var origenId))
            {
                continue;
            }

            vinculos.AddRange(lineas.Where(l => l.FuenteId == origenId && l.EdicionId == traslado.EdicionId).Select(l => (l.Id, traslado.TrasladoId)));
        }

        await reservas.VincularTraslados(vinculos, cancelacion).ConfigureAwait(false);
        return generados;
    }
}
