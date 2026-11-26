using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// El estado del pago de una compra para la web (PAG-07, PAG-14): lo que el cliente puede esperar o hacer. Solo lee, sin bloquear ni escribir (una lectura de la pantalla de estado no compite con el cobro): la cuenta sale del actor; un pedido ajeno o
/// inexistente responde igual y deja un acceso denegado (SEG-09). Responde también cuando el checkout ya está <c>pagado</c> o <c>cancelado</c>, porque se apoya en la intención y no en Carrito. <c>PuedeReintentar</c> solo si se puede pagar ahora
/// (<see cref="IntencionDePago.ValidarCobro"/> lo permite y el cobro no está cerrado); <c>CobroDisponible</c> es falso sin ninguna pasarela (DD-110); <c>UltimoMotivo</c> es el del intento más reciente si fue rechazado.
/// </summary>
internal sealed class ObtenerEstadoDelPago(AutorizadorDePagos autorizador, IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, IReloj reloj, OpcionesDelCobro opciones) : ICasoDeUso<Guid, EstadoDelPagoVista>
{
    public async Task<Resultado<EstadoDelPagoVista>> Ejecutar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSesion(cancelacion).ConfigureAwait(false) is { } sinSesion)
        {
            return Resultado<EstadoDelPagoVista>.Fallo(sinSesion);
        }

        if (pedidoId == Guid.Empty)
        {
            return FallosDePagos.De<EstadoDelPagoVista>(CategoriaDeError.Validacion, ClavesDePagos.ValidacionFallida, [new ErrorDeCampo("pedidoId", "invalido")]);
        }

        var cuentaId = autorizador.CuentaId!.Value;
        var (intencion, ultimo) = await unidad.Ejecutar<(IntencionDePago?, IntentoDeCobro?)>(
            async token =>
            {
                var leida = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token).ConfigureAwait(false);
                if (leida is null || !leida.EsDeLaCuenta(cuentaId))
                {
                    return (null, null);
                }

                return (leida, await repositorio.ObtenerUltimoIntento(leida.Id, token).ConfigureAwait(false));
            },
            cancelacion).ConfigureAwait(false);
        if (intencion is null)
        {
            return Resultado<EstadoDelPagoVista>.Fallo(await autorizador.RechazarAjena(cancelacion).ConfigureAwait(false));
        }

        var estado = intencion.Estado switch
        {
            EstadoDeIntencion.Abierta => EstadoDelPagoDeLaCompra.ListoParaPagar,
            EstadoDeIntencion.ConCobro => EstadoDelPagoDeLaCompra.ConfirmandoElPago,
            EstadoDeIntencion.Aprobada => EstadoDelPagoDeLaCompra.Aprobado,
            EstadoDeIntencion.Completada => EstadoDelPagoDeLaCompra.Completado,
            _ => EstadoDelPagoDeLaCompra.Cerrado,
        };
        var limite = intencion.Estado switch
        {
            EstadoDeIntencion.Abierta => intencion.VenceEn,
            EstadoDeIntencion.ConCobro => intencion.LimitePendienteEn,
            _ => (DateTimeOffset?)null,
        };
        var puedeReintentar = !opciones.Cerrado && intencion.Estado == EstadoDeIntencion.Abierta && intencion.ValidarCobro(reloj.Ahora) == ValidacionDelCobro.Permitido;
        MotivoDeRechazoDePago? motivo = ultimo is { Estado: EstadoDeIntento.Rechazado, MotivoDeRechazo: { } m } ? MapeoDeMotivos.APublico(m) : null;

        return Resultado<EstadoDelPagoVista>.Exito(new EstadoDelPagoVista(estado, limite, intencion.ReservaExpiraEn, motivo, puedeReintentar, CobroDisponible: !opciones.Cerrado));
    }
}

/// <summary>El contrato público <see cref="IPagos"/> sobre sus casos de uso: emitir la clave del intento, cobrar y consultar el estado (PAG-01 a PAG-09).</summary>
internal sealed class PagosDelCliente(
    ICasoDeUso<EmitirClaveDeIntentoEntrada, ClaveDeIntentoEmitida> emitirClave,
    ICasoDeUso<CobrarEntrada, ResultadoDelCobroDelCliente> cobrar,
    ICasoDeUso<Guid, EstadoDelPagoVista> obtenerEstado) : IPagos
{
    public Task<Resultado<ClaveDeIntentoEmitida>> EmitirClaveDeIntento(EmitirClaveDeIntentoEntrada entrada, CancellationToken cancelacion = default) => emitirClave.Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDelCobroDelCliente>> Cobrar(CobrarEntrada entrada, CancellationToken cancelacion = default) => cobrar.Ejecutar(entrada, cancelacion);

    public Task<Resultado<EstadoDelPagoVista>> ObtenerEstado(Guid pedidoId, CancellationToken cancelacion = default) => obtenerEstado.Ejecutar(pedidoId, cancelacion);
}
