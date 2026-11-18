using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Aprueba un traslado solicitado (TRA-04): el origen o la administración central. Un traslado vinculado a un pedido también se aprueba (es lo único que se le puede hacer a mano, TRA-16).</summary>
internal sealed class AprobarTraslado(EjecutorDeTransiciones ejecutor, IValidador<AprobarTrasladoEntrada> validador) : ICasoDeUso<AprobarTrasladoEntrada, TrasladoRegistrado>
{
    public Task<Resultado<TrasladoRegistrado>> Ejecutar(AprobarTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return ejecutor.Ejecutar(
            OperacionDeTraslado.Aprobar,
            entrada.TrasladoId,
            () => validador.Validar(entrada),
            (traslado, ahora) =>
            {
                traslado.Aprobar(ahora);
                return Task.FromResult<ErrorDeAplicacion?>(null);
            },
            cancelacion);
    }
}

/// <summary>
/// Rechaza un traslado solicitado con motivo (TRA-05): el origen o la administración central. <b>Nunca uno vinculado a un pedido</b> (TRA-16): 422 con el mensaje de que solo puede aprobarse o anularse el pedido, <b>sin evento
/// de seguridad</b> (es una regla de negocio, no un permiso, incluso para la administración).
/// </summary>
internal sealed class RechazarTraslado(EjecutorDeTransiciones ejecutor, IValidador<RechazarTrasladoEntrada> validador) : ICasoDeUso<RechazarTrasladoEntrada, TrasladoRegistrado>
{
    public Task<Resultado<TrasladoRegistrado>> Ejecutar(RechazarTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return ejecutor.Ejecutar(
            OperacionDeTraslado.Rechazar,
            entrada.TrasladoId,
            () => validador.Validar(entrada),
            (traslado, ahora) =>
            {
                traslado.Rechazar(entrada.Motivo, ahora);
                return Task.FromResult<ErrorDeAplicacion?>(null);
            },
            cancelacion);
    }
}

/// <summary>
/// Cancela un traslado solicitado o aprobado no vinculado a un pedido, con motivo (TRA-18): solo el origen o la administración central; el destino recibe el rechazo de alcance. Uno vinculado no se cancela a mano (422, la
/// alternativa es anular el pedido, TRA-16).
/// </summary>
internal sealed class CancelarTraslado(EjecutorDeTransiciones ejecutor, IValidador<CancelarTrasladoEntrada> validador) : ICasoDeUso<CancelarTrasladoEntrada, TrasladoRegistrado>
{
    public Task<Resultado<TrasladoRegistrado>> Ejecutar(CancelarTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return ejecutor.Ejecutar(
            OperacionDeTraslado.Cancelar,
            entrada.TrasladoId,
            () => validador.Validar(entrada),
            (traslado, ahora) =>
            {
                traslado.Cancelar(entrada.Motivo, ahora);
                return Task.FromResult<ErrorDeAplicacion?>(null);
            },
            cancelacion);
    }
}

