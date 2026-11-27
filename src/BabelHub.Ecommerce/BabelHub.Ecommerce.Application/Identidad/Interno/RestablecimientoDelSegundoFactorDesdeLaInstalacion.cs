using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// Restablece el segundo factor de la única administración desde el equipo del servicio (identidad-recuperacion-del-segundo-factor; RSF-01 a RSF-16). En una sola transacción, con <b>todas</b> las cuentas de administración bloqueadas: comprueba que ninguna otra pueda
/// ingresar, borra el segundo factor, anula los ingresos pendientes, revoca las sesiones y lo audita con el actor «sistema» (si no se puede auditar, no se aplica). No toca la contraseña. El aviso por correo va después de confirmar y nunca deshace nada.
/// </summary>
public sealed class RestablecimientoDelSegundoFactorDesdeLaInstalacion(
    IRepositorioDeCuentas cuentas,
    ISegundoFactorAlmacen segundoFactor,
    IRepositorioDeDesafios desafios,
    RevocacionDeSesiones revocacion,
    AvisosDeCuenta avisos,
    AlcanceDeLaCuenta alcance,
    IActorActual actor,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : IRestablecimientoDesdeLaInstalacion
{
    public async Task<RestablecimientoDesdeLaInstalacion> Restablecer(string? entorno, string? modo, string? correo, CancellationToken cancelacion = default)
    {
        // El orden importa: fuera de los entornos admitidos no se toca ningún dato, ni siquiera para registrar la denegación (RSF-02).
        if (!ReglasDeRestablecimientoDesdeLaInstalacion.EntornoPermitido(entorno, modo))
        {
            return new(ResultadoDelRestablecimientoDesdeLaInstalacion.EntornoNoPermitido);
        }

        if (actor.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            await alcance.Denegar(cancelacion, "fuera-de-rol").ConfigureAwait(false);
            return new(ResultadoDelRestablecimientoDesdeLaInstalacion.NoPermitido);
        }

        if (string.IsNullOrWhiteSpace(correo) || !EntradasDeIdentidad.TryNormalizarElCorreo(correo, out var normalizado))
        {
            return new(ResultadoDelRestablecimientoDesdeLaInstalacion.CorreoInvalido);
        }

        var (resultado, restablecida) = await unidad.Ejecutar(
            async ct =>
            {
                // La cuenta se toma de esta lectura bloqueada y de ninguna anterior: la revocación la guarda entera.
                var administraciones = await cuentas.BloquearLasAdministraciones(ct).ConfigureAwait(false);
                var objetivo = administraciones.FirstOrDefault(c => string.Equals(c.Usuario.Correo, normalizado, StringComparison.Ordinal));
                var activo = objetivo is not null && await segundoFactor.Obtener(objetivo.Usuario.Id, ct).ConfigureAwait(false) is { Factor.EstaActivo: true };
                var decision = ReglasDeRestablecimientoDesdeLaInstalacion.Decidir(objetivo is null ? null : Acceso(objetivo), activo, administraciones.Select(Acceso), reloj.Ahora);
                if (decision != DecisionDelRestablecimientoDesdeLaInstalacion.Permitido)
                {
                    return (Rechazo(decision), (Usuario?)null);
                }

                var usuarioId = objetivo!.Usuario.Id;
                await segundoFactor.Desactivar(usuarioId, ct).ConfigureAwait(false);
                await desafios.AnularLosDelUsuario(usuarioId, ct).ConfigureAwait(false);
                await revocacion.Todas(objetivo, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion) { ObjetoTipo = "usuario", ObjetoId = usuarioId.ToString("D") },
                    ct).ConfigureAwait(false);
                return (ResultadoDelRestablecimientoDesdeLaInstalacion.Restablecido, (Usuario?)objetivo.Usuario);
            },
            cancelacion).ConfigureAwait(false);

        if (restablecida is null)
        {
            return new(resultado);
        }

        var enviado = false;
        try
        {
            enviado = await avisos.SegundoFactorRestablecido(restablecida, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // El restablecimiento ya está aplicado: el aviso nunca lo deshace ni lo hace fallar (RSF-12).
        }

        return new(resultado, enviado);
    }

    private static AccesoDeUnaAdministracion Acceso(CuentaDeIdentidad c) =>
        new(c.Usuario.Id, c.Usuario.Rol, c.Usuario.Activo, c.CanceladaEn is not null, c.DebeCambiarLaTemporal, c.ContrasenaTemporalHasta);

    private static ResultadoDelRestablecimientoDesdeLaInstalacion Rechazo(DecisionDelRestablecimientoDesdeLaInstalacion decision) => decision switch
    {
        DecisionDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva => ResultadoDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva,
        DecisionDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion => ResultadoDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion,
        DecisionDelRestablecimientoDesdeLaInstalacion.SinSegundoFactor => ResultadoDelRestablecimientoDesdeLaInstalacion.SinSegundoFactor,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Decisión sin rechazo."),
    };
}
