using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Cierra sesiones (WEB-28, T08): el cierre voluntario invalida la sesión web en el servidor <b>aunque la revocación de la renovación quede pendiente</b>, y
/// «cerrar todas» cierra las filas del usuario y después llama a Identidad, en una sola unidad de trabajo (orden de bloqueo: primero las filas web, después
/// Identidad). Ninguno de los dos conserva el borrador: son decisiones de la persona.
/// </summary>
internal sealed class CerrarSesionWeb(ServiciosDeSesion comun, IUnidadDeTrabajo unidad, IPublicadorDeAlertasTecnicas alertas)
{
    public async Task Cerrar(string valorDeLaCookie, CancellationToken cancelacion)
    {
        if (!ValorDeCookieDeSesion.TieneFormaValida(valorDeLaCookie))
        {
            return;
        }

        var id = ValorDeCookieDeSesion.Hash(valorDeLaCookie);
        await unidad.Ejecutar(
            async ct =>
            {
                var fila = await comun.Sesiones.CargarParaRenovar(id, ct).ConfigureAwait(false);
                if (fila is null || fila.Sesion.Estado != EstadoDeSesion.Abierta)
                {
                    return true;
                }

                await comun.CerrarYRevocar(fila, MotivoDeCierre.Voluntario, ct).ConfigureAwait(false);
                await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task CerrarTodasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion)
    {
        await unidad.Ejecutar(
            async ct =>
            {
                var abiertas = await comun.Sesiones.CargarAbiertasDeUnUsuario(usuarioId, ct).ConfigureAwait(false);
                foreach (var fila in abiertas)
                {
                    // Las filas web primero y después Identidad: sin revocar una por una, porque Identidad revoca todas las renovaciones.
                    fila.Sesion.Cerrar(MotivoDeCierre.Voluntario, comun.Reloj.Ahora);
                    fila.CredencialCifrada = null;
                    fila.CredencialVenceEn = null;
                    fila.RetornoTrasAceptar = null;
                }

                var confirmada = false;
                try
                {
                    confirmada = await comun.Identidad.CerrarTodasLasSesiones(usuarioId, ct).ConfigureAwait(false);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Identidad no responde: las filas web quedan cerradas y la revocación pendiente.
                }

                foreach (var fila in abiertas)
                {
                    if (confirmada)
                    {
                        fila.RenovacionCifrada = null;
                        fila.RevocacionPendiente = false;
                    }
                    else
                    {
                        fila.RevocacionPendiente = fila.RenovacionCifrada is not null;
                    }

                    await comun.Sesiones.Guardar(fila, ct).ConfigureAwait(false);
                }

                if (!confirmada && abiertas.Count > 0)
                {
                    await alertas.Publicar(new AlertaTecnica("revocacion-de-sesiones-pendiente", comun.Reloj.Ahora), ct).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
    }
}
