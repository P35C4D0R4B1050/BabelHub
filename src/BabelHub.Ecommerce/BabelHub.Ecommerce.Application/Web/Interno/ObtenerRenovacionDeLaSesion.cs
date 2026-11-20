using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// La renovación de la sesión abierta de esta cookie, <b>solo en proceso</b> (T29, WEB-35): cambiar la contraseña o el segundo factor revoca las demás sesiones de la persona y conserva esta, y para eso Identidad necesita saber cuál es. Devuelve <see langword="null"/> si la cookie no tiene la forma, la sesión
/// no existe, es de otro host, no está abierta o venció, o si no se puede descifrar (la acción sigue sin conservar la sesión actual, que es lo seguro). El valor nunca se guarda ni se registra y nunca llega a la Api como texto que se imprima: es un <see cref="CredencialEnMemoria"/>.
/// </summary>
internal sealed class ObtenerRenovacionDeLaSesion(ServiciosDeSesion comun)
{
    public async Task<CredencialEnMemoria?> Ejecutar(string? valorDeLaCookie, HostWeb host, CancellationToken cancelacion)
    {
        if (!ValorDeCookieDeSesion.TieneFormaValida(valorDeLaCookie))
        {
            return null;
        }

        var fila = await comun.Sesiones.Cargar(ValorDeCookieDeSesion.Hash(valorDeLaCookie!), cancelacion).ConfigureAwait(false);
        if (fila is null || fila.Sesion.Host != AreasDeInterfaz.Aplicar(host) || fila.Sesion.Estado != EstadoDeSesion.Abierta
            || fila.Sesion.Evaluar(comun.Reloj.Ahora) is EvaluacionDeSesion.VencidaPorInactividad or EvaluacionDeSesion.VencidaPorTopeAbsoluto)
        {
            return null;
        }

        try
        {
            return comun.DescifrarRenovacion(fila) is { Length: > 0 } renovacion ? new CredencialEnMemoria(renovacion) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
