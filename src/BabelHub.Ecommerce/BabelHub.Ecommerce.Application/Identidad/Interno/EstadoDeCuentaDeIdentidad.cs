using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// <see cref="IEstadoDeCuenta"/> (identidad-seguridad IDE-27, IDE-29, IDE-43, IDE-58, IDE-71; T13): lo que Carrito, Pagos, Notificaciones y la política de la web leen para decidir qué permitir, <b>sin datos
/// personales y sin caché</b>. «Puede comprar» exige una cuenta activa y no cancelada, el correo verificado, los textos vigentes aceptados y ninguna solicitud de cancelación abierta. Si el repositorio falla, la
/// excepción sube: quien consulta falla cerrado (CAR-37, WEB-46).
/// </summary>
public sealed class EstadoDeCuentaDeIdentidad(
    IRepositorioDeCuentas cuentas,
    IRepositorioDeCancelaciones cancelaciones,
    IVersionesDeTextosVigentes versiones,
    AlcanceDeLaCuenta alcance,
    IUnidadDeTrabajo unidad,
    IReloj reloj) : IEstadoDeCuenta
{
    /// <summary>El separador y las claves del identificador del conjunto de textos que muestra la web: <c>terminos:V|privacidad:V|reembolso:V</c> (<c>AceptarTextosLegales</c>).</summary>
    internal const char SeparadorDelConjunto = '|';

    public async Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            return null;
        }

        var cuenta = await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return null;
        }

        var vigentes = await versiones.Obtener(cancelacion).ConfigureAwait(false);
        var abierta = await cancelaciones.AbiertaDe(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        var canceladaODesactivada = cuenta.CanceladaEn is not null || !cuenta.Usuario.Activo;
        var textosAlDia = cuenta.Aceptacion.EstaAlDia(vigentes);
        var puedeComprar = !canceladaODesactivada && abierta is null && cuenta.Usuario.CorreoVerificado && textosAlDia;
        return new EstadoDeCuenta(
            usuarioId,
            puedeComprar,
            textosAlDia ? EstadoDeAceptacionDeLosTextos.AlDia : EstadoDeAceptacionDeLosTextos.Pendiente,
            cuenta.Usuario.CorreoVerificado,
            canceladaODesactivada,
            cuenta.Whatsapp.Consentido && cuenta.Whatsapp.Numero is not null);
    }

    public async Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty || string.IsNullOrWhiteSpace(versionMostrada) || versionMostrada.Length > 200)
        {
            return ResultadoDeLaAceptacion.VersionCambiada;
        }

        // Solo el titular acepta por sí mismo (AGENTS.md §5): otro actor no registra el consentimiento de nadie; la denegación queda registrada sin interrumpir.
        if (await alcance.Comprobar(usuarioId, cancelacion).ConfigureAwait(false) is not null)
        {
            return ResultadoDeLaAceptacion.NoDisponible;
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var vigentes = await versiones.Obtener(ct).ConfigureAwait(false);
                if (!string.Equals(versionMostrada, ConjuntoVigente(vigentes), StringComparison.Ordinal))
                {
                    return ResultadoDeLaAceptacion.VersionCambiada;
                }

                var cuenta = await cuentas.PorId(usuarioId, paraActualizar: true, ct).ConfigureAwait(false);
                if (cuenta is null || cuenta.CanceladaEn is not null || !cuenta.Usuario.Activo)
                {
                    return ResultadoDeLaAceptacion.NoDisponible;
                }

                var ahora = reloj.Ahora;
                foreach (var texto in new[] { TextoLegal.Terminos, TextoLegal.Privacidad, TextoLegal.Reembolso })
                {
                    if (cuenta.Aceptacion.Aceptar(vigentes, texto, vigentes.De(texto), ahora) != ResultadoDeAceptacion.Aceptada)
                    {
                        return ResultadoDeLaAceptacion.VersionCambiada;
                    }
                }

                await cuentas.Guardar(cuenta, ct).ConfigureAwait(false);
                return ResultadoDeLaAceptacion.Aceptada;
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>El identificador del conjunto vigente, con el mismo formato que arma la web: <c>terminos:V|privacidad:V|reembolso:V</c>.</summary>
    internal static string ConjuntoVigente(VersionesDeTextos vigentes) =>
        string.Join(SeparadorDelConjunto, $"terminos:{vigentes.Terminos}", $"privacidad:{vigentes.Privacidad}", $"reembolso:{vigentes.Reembolso}");
}
