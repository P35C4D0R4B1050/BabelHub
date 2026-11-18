using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// <c>DarDeBajaObra</c> (CAT-13, CAT-18, CAT-19, AC-CAT-10, AC-CAT-15): da de baja la obra y todas sus ediciones. La ficha pública deja de responder pero los datos siguen legibles por identificador
/// para los pedidos y traslados en curso; las ediciones se quitan de los carritos por <see cref="IRetiroDeEdicionDeCarritos"/> <b>en la misma transacción</b> (si el puerto falla, la baja no se aplica);
/// se incrementa la versión y se audita la obra y cada edición. Los archivos y accesos de las digitales se conservan (BIB-09).
/// </summary>
internal sealed class DarDeBajaObra(
    AutorizadorDelCatalogo autorizador,
    IValidador<DarDeBajaObraEntrada> validador,
    IRepositorioDeObras obras,
    IRetiroDeEdicionDeCarritos carritos,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<DarDeBajaObraEntrada, ResultadoDeEscritura>
{
    public async Task<Resultado<ResultadoDeEscritura>> Ejecutar(DarDeBajaObraEntrada entrada, CancellationToken cancelacion = default)
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

        if (!obra.CoincideLaVersion(entrada.Version))
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var versionLeida = obra.Version;
        var baja = obra.DarDeBaja(reloj.Ahora);
        if (!baja.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(baja.Rechazo!));
        }

        if (await obras.Guardar(obra, versionLeida, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeObra.Escrita)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var afectadas = baja.Valor.EdicionesDadasDeBaja;
        if (afectadas.Count > 0)
        {
            // En la misma transacción: si Carrito no responde, la excepción sube y la baja no se aplica (D07).
            await carritos.Retirar(afectadas.Select(e => e.Id).ToList(), cancelacion).ConfigureAwait(false);
        }

        await auditoria.Registrar(AuditoriaDelCatalogo.ObraDadaDeBaja(obra), cancelacion).ConfigureAwait(false);
        foreach (var edicion in afectadas)
        {
            await auditoria.Registrar(AuditoriaDelCatalogo.EdicionDadaDeBaja(edicion), cancelacion).ConfigureAwait(false);
        }

        return Resultado<ResultadoDeEscritura>.Exito(new ResultadoDeEscritura(obra.Id, obra.Version));
    }
}

/// <summary>
/// <c>DarDeBajaEdicion</c> (CAT-13, CAT-18, CAT-19, AC-CAT-15): da de baja una edición. La baja de la última edición no dada de baja de la obra se rechaza (<c>ECO-OBRA-SIN-EDICIONES</c>; se da de baja
/// la obra). La edición se quita de los carritos en la misma transacción, se incrementa la versión de la obra y se audita; la licencia y el archivo de una digital se conservan para los accesos ya
/// concedidos. La edición sigue legible por identificador para los pedidos y traslados en curso.
/// </summary>
internal sealed class DarDeBajaEdicion(
    AutorizadorDelCatalogo autorizador,
    IValidador<DarDeBajaEdicionEntrada> validador,
    IRepositorioDeObras obras,
    IRetiroDeEdicionDeCarritos carritos,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<DarDeBajaEdicionEntrada, ResultadoDeEscritura>
{
    public async Task<Resultado<ResultadoDeEscritura>> Ejecutar(DarDeBajaEdicionEntrada entrada, CancellationToken cancelacion = default)
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

        if (!obra.CoincideLaVersion(entrada.Version))
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        var versionLeida = obra.Version;
        var baja = obra.DarDeBajaEdicion(entrada.EdicionId, reloj.Ahora);
        if (!baja.EsExito)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.De(baja.Rechazo!));
        }

        if (await obras.Guardar(obra, versionLeida, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeObra.Escrita)
        {
            return FallosDelCatalogo.Fallo<ResultadoDeEscritura>(TraductorDeRechazos.ConflictoDeVersion());
        }

        await carritos.Retirar([baja.Valor.Id], cancelacion).ConfigureAwait(false);
        await auditoria.Registrar(AuditoriaDelCatalogo.EdicionDadaDeBaja(baja.Valor), cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeEscritura>.Exito(new ResultadoDeEscritura(obra.Id, obra.Version));
    }
}
