using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Carga la sesión de la cookie (WEB-27, WEB-28, WEB-31, T06): valida la forma antes de tocar la base, rechaza un identificador cerrado o de otro host, vence por
/// inactividad o por tope absoluto (cerrando, revocando la renovación y borrando los secretos), y solo una acción de la persona renueva la actividad, como máximo
/// una vez por minuto. Una consulta automática nunca renueva ni revive una sesión.
/// </summary>
internal sealed class CargarSesionWeb(ServiciosDeSesion comun)
{
    public async Task<ResultadoDeCarga> Ejecutar(string? valorDeLaCookie, HostWeb host, OrigenDeLaAccion origen, CancellationToken cancelacion)
    {
        if (!ValorDeCookieDeSesion.TieneFormaValida(valorDeLaCookie))
        {
            return new ResultadoDeCarga(EstadoDeCarga.Inexistente, null);
        }

        var fila = await comun.Sesiones.Cargar(ValorDeCookieDeSesion.Hash(valorDeLaCookie!), cancelacion).ConfigureAwait(false);
        if (fila is null)
        {
            return new ResultadoDeCarga(EstadoDeCarga.Inexistente, null);
        }

        var sesion = fila.Sesion;
        if (sesion.Host != AreasDeInterfaz.Aplicar(host))
        {
            await comun.Seguridad.AccesoDenegado(MotivoDeDenegacionWeb.SesionDeOtroHost, sesion.UsuarioId, cancelacion).ConfigureAwait(false);
            return new ResultadoDeCarga(EstadoDeCarga.OtroHost, null);
        }

        if (sesion.Estado != EstadoDeSesion.Abierta)
        {
            // Un identificador cerrado o vencido nunca vuelve a usarse (WEB-28): se rechaza y queda un evento.
            await comun.Seguridad.AccesoDenegado(MotivoDeDenegacionWeb.IdentificadorDeSesionCerrado, sesion.UsuarioId, cancelacion).ConfigureAwait(false);
            return new ResultadoDeCarga(EstadoDeCarga.Cerrada, null);
        }

        var ahora = comun.Reloj.Ahora;
        var evaluacion = sesion.Evaluar(ahora);
        if (evaluacion is EvaluacionDeSesion.VencidaPorInactividad or EvaluacionDeSesion.VencidaPorTopeAbsoluto)
        {
            var tope = evaluacion == EvaluacionDeSesion.VencidaPorTopeAbsoluto;
            var bloqueada = await comun.Sesiones.CargarParaRenovar(sesion.Id, cancelacion).ConfigureAwait(false) ?? fila;
            if (bloqueada.Sesion.Estado == EstadoDeSesion.Abierta)
            {
                await comun.CerrarYRevocar(bloqueada, tope ? MotivoDeCierre.VencidaPorTopeAbsoluto : MotivoDeCierre.VencidaPorInactividad, cancelacion).ConfigureAwait(false);
                await comun.Sesiones.Guardar(bloqueada, cancelacion).ConfigureAwait(false);
            }

            return new ResultadoDeCarga(tope ? EstadoDeCarga.VencidaPorTopeAbsoluto : EstadoDeCarga.VencidaPorInactividad, null);
        }

        // Solo una acción de la persona cuenta (WEB-27); la escritura se limita a una por minuto.
        if (origen == OrigenDeLaAccion.AccionDelUsuario && sesion.RegistrarAccionDelUsuario(ahora))
        {
            await comun.Sesiones.Guardar(fila, cancelacion).ConfigureAwait(false);
        }

        return new ResultadoDeCarga(EstadoDeCarga.Valida, sesion.UsuarioId);
    }
}
