using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// La cuenta del pagador sobre los contratos de Identidad (PAG-21, PAG-25, PAG-31; reemplaza a <c>CuentaDelPagadorSinIdentidad</c>): puede pagar si <see cref="IEstadoDeCuenta"/> lo dice y, si no, el primer motivo en el orden
/// cuenta inactiva, correo sin verificar, textos pendientes; la marca de cancelación se lee con bloqueo (<see cref="IMarcaDeCancelacionDeCuenta"/>, orden global cuenta → pedido → …) y el correo del titular sale de
/// <see cref="ICorreoDelTitular"/> (puerto estrecho que también sirve al trabajo de fondo, sin actor titular) sin guardarse (D12). <b>Falla cerrada</b>: una cuenta que no existe es <see langword="null"/> y cualquier excepción de Identidad sube (Pagos la trata como «no se pudo saber» y no cobra).
/// </summary>
internal sealed class CuentaDelPagadorSobreIdentidad(IEstadoDeCuenta estados, IMarcaDeCancelacionDeCuenta marcas, ICorreoDelTitular correos) : ICuentaDelPagador
{
    public async Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default)
    {
        var estado = await estados.Consultar(cuentaId, cancelacion).ConfigureAwait(false);
        if (estado is null)
        {
            return null;
        }

        if (estado.CuentaCanceladaODesactivada)
        {
            return new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.CuentaInactiva);
        }

        if (!estado.CorreoVerificado)
        {
            return new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.CorreoSinVerificar);
        }

        if (estado.Textos == EstadoDeAceptacionDeLosTextos.Pendiente)
        {
            return new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.TextosPendientes);
        }

        // Una cancelación diferida de la cuenta también bloquea el cobro nuevo (IDE-71): Identidad lo resume en «puede comprar».
        return estado.PuedeComprar
            ? new EstadoDeLaCuentaParaPagar(true, null)
            : new EstadoDeLaCuentaParaPagar(false, MotivoDeCuentaQueNoPuedePagar.CancelacionDiferida);
    }

    public async Task<bool> CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion = default)
    {
        // Identidad solo ofrece la lectura con bloqueo (orden global cuenta → pedido → …): pedir una sin bloqueo sería tomarlo sin que quien llama lo espere.
        if (!bloquear)
        {
            throw new ArgumentException("La marca de cancelación solo se lee con bloqueo.", nameof(bloquear));
        }

        return await marcas.LeerConBloqueo(cuentaId, cancelacion).ConfigureAwait(false) == EstadoDeLaMarca.EnEjecucion;
    }

    public async Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default)
    {
        return await correos.Consultar(cuentaId, cancelacion).ConfigureAwait(false);
    }
}
