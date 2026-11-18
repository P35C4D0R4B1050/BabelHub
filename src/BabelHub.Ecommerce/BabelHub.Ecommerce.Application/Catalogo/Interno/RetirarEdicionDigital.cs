using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// <c>RetirarEdicionDigital</c> (CAT-08, CAT-15, AC-CAT-05): retira de la venta una edición <b>digital</b> —se permite aunque sea la única de la obra y es definitivo en este plan—, con un motivo
/// obligatorio de hasta 300 caracteres. Deja de ofrecerse, audita, incrementa la versión de la obra y <b>no cambia los accesos ya concedidos</b> ni el archivo. No lleva versión de entrada (el plan
/// no la pide): la fila se toma con bloqueo, así que la operación se aplica sobre el estado actual.
/// </summary>
internal sealed class RetirarEdicionDigital(
    AutorizadorDelCatalogo autorizador,
    IValidador<RetirarEdicionDigitalEntrada> validador,
    IRepositorioDeObras obras,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<RetirarEdicionDigitalEntrada, ResultadoDeEscritura>
{
    public async Task<Resultado<ResultadoDeEscritura>> Ejecutar(RetirarEdicionDigitalEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDelCatalogo.Validacion<ResultadoDeEscritura>(validacion);
        }

        var obra = await obras.Obtener(entrada.ObraId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        if (obra is null)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ObraNoEncontrada());
        }

        var versionLeida = obra.Version;
        var retiro = obra.RetirarEdicionDigital(entrada.EdicionId, entrada.Motivo, reloj.Ahora);
        if (!retiro.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(retiro.Rechazo!));
        }

        if (await obras.Guardar(obra, versionLeida, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeObra.Escrita)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        await auditoria.Registrar(AuditoriaDelCatalogo.EdicionDigitalRetirada(retiro.Valor), cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeEscritura>.Exito(new ResultadoDeEscritura(obra.Id, obra.Version));
    }
}
