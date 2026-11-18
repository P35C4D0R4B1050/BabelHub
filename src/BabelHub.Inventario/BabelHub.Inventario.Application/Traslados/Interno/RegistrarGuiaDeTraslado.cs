using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// Registra la guía de un traslado aprobado o en tránsito (TRA-12) sin cambiar su estado: logística o la administración central; el encargado y cualquier otro rol reciben el rechazo de rol. La auditoría guarda el valor nuevo de la
/// guía y si reemplazó a otra; la guía nunca viaja en un aviso (D07). Como no cambia el estado, no deja mensajes.
/// </summary>
internal sealed class RegistrarGuiaDeTraslado(EjecutorDeTransiciones ejecutor, IValidador<RegistrarGuiaDeTrasladoEntrada> validador) : ICasoDeUso<RegistrarGuiaDeTrasladoEntrada, TrasladoRegistrado>
{
    public async Task<Resultado<TrasladoRegistrado>> Ejecutar(RegistrarGuiaDeTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        try
        {
            var (leido, error) = await ejecutor.LeerYAutorizar(OperacionDeTraslado.RegistrarGuia, entrada.TrasladoId, () => validador.Validar(entrada), cancelacion).ConfigureAwait(false);
            if (leido is null)
            {
                return FallosDeTraslados.Fallo<TrasladoRegistrado>(error!);
            }

            var traslado = await ejecutor.Bloquear(leido, cancelacion).ConfigureAwait(false);
            if (traslado is null)
            {
                return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.NoEncontrado, ClavesDeTraslados.TrasladoNoEncontrado);
            }

            var version = traslado.Version;
            bool reemplazo;
            try
            {
                reemplazo = traslado.RegistrarGuia(entrada.Guia);
            }
            catch (TransicionDeTrasladoInvalidaException)
            {
                return FallosDeTraslados.De<TrasladoRegistrado>(CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido);
            }

            await ejecutor.GuardarAuditarYAvisar(traslado, version, ConversionDeTraslados.AuditarGuia(traslado, reemplazo), conMensajes: false, ejecutor.Ahora, cancelacion).ConfigureAwait(false);
            var (origen, destino) = await ejecutor.Fuentes(traslado, cancelacion).ConfigureAwait(false);
            return Resultado<TrasladoRegistrado>.Exito(ConversionDeTraslados.ARespuesta(traslado, origen, destino));
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeTraslados.BloqueoOcupado<TrasladoRegistrado>();
        }
    }
}
