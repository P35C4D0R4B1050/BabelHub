using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

internal sealed record ObtenerHojaEntrada(Guid HojaId);

/// <summary>
/// Abrir una hoja con sus datos personales desde la administración central (T07, REC-10, REC-15): solo el administrador central, que ve también las hojas de una cuenta cancelada y las anonimizadas (con sus valores ya
/// anonimizados). <b>La consulta se audita en la misma transacción y falla cerrada</b>: si no se puede auditar, la transacción se revierte y no se muestra nada (AGENTS §5, AUD-21).
/// </summary>
internal sealed class ObtenerHojaParaAdministracion(AutorizadorDeReclamaciones autorizador, IRepositorioDeHojas hojas, IUnidadDeTrabajo unidad, IAuditoria auditoria) : ICasoDeUso<ObtenerHojaEntrada, DetalleDeHoja>
{
    public async Task<Resultado<DetalleDeHoja>> Ejecutar(ObtenerHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<DetalleDeHoja>(rechazo);
        }

        try
        {
            return await unidad.Ejecutar(
                async token =>
                {
                    var hoja = await hojas.Obtener(entrada.HojaId, paraActualizar: false, token).ConfigureAwait(false);
                    if (hoja is null)
                    {
                        return FallosDeReclamaciones.De<DetalleDeHoja>(CategoriaDeError.NoEncontrado, ClavesDeReclamaciones.HojaNoEncontrada);
                    }

                    // Sin poder auditar, la transacción se revierte y el detalle no sale de aquí.
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.HojaDeReclamacionConsultada)
                        {
                            ObjetoTipo = "hoja-de-reclamacion",
                            ObjetoId = hoja.Numero.ToString(),
                        },
                        token).ConfigureAwait(false);
                    return Resultado<DetalleDeHoja>.Exito(MapeoDeHojas.DetalleDe(hoja));
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            return FallosDeReclamaciones.De<DetalleDeHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
        catch (HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<DetalleDeHoja>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}
