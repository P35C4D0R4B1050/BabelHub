using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

/// <summary>
/// La anonimización diaria de las hojas que cumplieron dos años (T12, REC-17): <b>una sola transacción</b> en la que la función de base de datos anonimiza y se audita el conteo devuelto (acción
/// <c>HojaDeReclamacionAnonimizada</c> con <c>causa = conservacion</c> y <c>cantidad</c>, sin datos personales, con el actor «sistema»). Si no se puede anonimizar o no se puede auditar, la transacción se revierte y ese día
/// no pasa nada: la corrida siguiente lo reintenta. Solo la corre el actor «sistema».
/// </summary>
internal sealed class AnonimizarHojasVencidas(AutorizadorDeReclamaciones autorizador, IAnonimizadorDeHojas anonimizador, IUnidadDeTrabajo unidad, IAuditoria auditoria)
{
    public const string CausaDeConservacion = "conservacion";

    public async Task<Resultado<int>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReclamaciones.Fallo<int>(rechazo);
        }

        try
        {
            var cantidad = await unidad.Ejecutar(
                async token =>
                {
                    var anonimizadas = await anonimizador.Anonimizar(token).ConfigureAwait(false);
                    if (anonimizadas > 0)
                    {
                        await auditoria.Registrar(
                            new SolicitudDeRegistro(AccionesAuditables.HojaDeReclamacionAnonimizada)
                            {
                                Detalle = new Dictionary<string, string?>
                                {
                                    ["causa"] = CausaDeConservacion,
                                    ["cantidad"] = anonimizadas.ToString(CultureInfo.InvariantCulture),
                                },
                            },
                            token).ConfigureAwait(false);
                    }

                    return anonimizadas;
                },
                cancelacion).ConfigureAwait(false);
            return Resultado<int>.Exito(cantidad);
        }
        catch (Exception ex) when (ex is AuditoriaNoDisponibleException or HojasNoDisponiblesException)
        {
            return FallosDeReclamaciones.De<int>(CategoriaDeError.NoDisponible, ClavesDeReclamaciones.ServicioNoDisponible);
        }
    }
}

/// <summary>
/// La revisión de que la anonimización corre (T12, REC-17): si pasaron <b>24 horas sin corrida</b> (o nunca corrió), publica una alerta técnica a la administración; no cambia nada. La alerta lleva solo el motivo y el
/// instante. Se pone en marcha el día del despliegue con una primera corrida de la anonimización para no alertar sin motivo.
/// </summary>
internal sealed class RevisarHojasSinAnonimizar(IAnonimizadorDeHojas anonimizador, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string MotivoDeAlerta = "anonimizacion-de-hojas-sin-correr";

    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(24);

    /// <summary>Devuelve si publicó la alerta. Si no puede saber cuándo corrió, también alerta (no se asume que corrió).</summary>
    public async Task<Resultado<bool>> Ejecutar(CancellationToken cancelacion = default)
    {
        var ahora = reloj.Ahora;
        DateTimeOffset? ultima;
        try
        {
            ultima = await anonimizador.UltimaCorridaEn(cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ultima = null;
        }

        // Una corrida «en el futuro» (desfase de reloj entre la base y la aplicación) no es confiable: se alerta.
        if (ultima is { } corrida && corrida <= ahora && ahora - corrida < EsperaMaxima)
        {
            return Resultado<bool>.Exito(false);
        }

        await alertas.Publicar(new AlertaTecnica(MotivoDeAlerta, ahora), CancellationToken.None).ConfigureAwait(false);
        return Resultado<bool>.Exito(true);
    }
}
