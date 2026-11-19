using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Los puertos que la capa web espera de Identidad (identidad-seguridad T42, reducido; web-base R12), implementados sobre el <b>contrato público</b> de Identidad (<see cref="IEstadoDeCuenta"/> e
/// <see cref="ISesiones"/>) y nunca sobre su interior (regla 13). Viven en la composición porque unen dos módulos. Ninguno guarda ni devuelve datos personales; los valores secretos solo viajan hacia la capa servidor.
/// </summary>
public sealed class EstadoDeCuentaDeLaWebSobreIdentidad(IEstadoDeCuenta estado) : IEstadoDeCuentaDeLaWeb
{
    /// <summary>Sin caché y sin inventar: una cuenta desconocida o un estado que no se entiende es <see cref="EstadoDeAceptacionDeTextos.Desconocido"/> y la web falla cerrada (WEB-45).</summary>
    public async Task<EstadoDeAceptacionDeTextos> Aceptacion(Guid usuarioId, CancellationToken cancelacion = default)
    {
        var cuenta = await estado.Consultar(usuarioId, cancelacion).ConfigureAwait(false);
        return cuenta?.Textos switch
        {
            EstadoDeAceptacionDeLosTextos.AlDia => EstadoDeAceptacionDeTextos.Aceptados,
            EstadoDeAceptacionDeLosTextos.Pendiente => EstadoDeAceptacionDeTextos.Pendientes,
            _ => EstadoDeAceptacionDeTextos.Desconocido,
        };
    }
}

public sealed class AceptacionDeTextosSobreIdentidad(IEstadoDeCuenta estado) : IAceptacionDeTextosEnIdentidad
{
    public async Task<ResultadoDeAceptacion> Aceptar(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) =>
        await estado.AceptarLosTextos(usuarioId, versionMostrada, cancelacion).ConfigureAwait(false) switch
        {
            ResultadoDeLaAceptacion.Aceptada => ResultadoDeAceptacion.Aceptada,
            ResultadoDeLaAceptacion.VersionCambiada => ResultadoDeAceptacion.VersionCambiada,
            _ => ResultadoDeAceptacion.NoDisponible,
        };
}

/// <summary>
/// <see cref="ISesionesDeIdentidad"/> (la renovación, la revocación y el cierre total que pide la sesión web) sobre <see cref="ISesiones"/>: un rechazo de la renovación (vencida, revocada, reutilizada, usuario
/// desactivado) es <see cref="ResultadoDeRenovacion.Rechazada"/> y cierra la sesión; una falla de Identidad es <see cref="ResultadoDeRenovacion.Transitoria"/> y no la cierra (WEB-31).
/// </summary>
public sealed class SesionesDeIdentidadParaLaWeb(ISesiones sesiones) : ISesionesDeIdentidad
{
    public async Task<ResultadoDeRenovacion> Renovar(string renovacion, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(renovacion);
        try
        {
            var resultado = await sesiones.Renovar(new ValorSecreto(renovacion), cancelacion).ConfigureAwait(false);
            if (resultado.EsExito)
            {
                var c = resultado.Valor;
                return new ResultadoDeRenovacion.Renovada(c.Credencial.Revelar(), c.CredencialExpiraEn, c.Renovacion.Revelar());
            }

            return resultado.Error!.Categoria is CategoriaDeError.NoAutenticado or CategoriaDeError.Prohibido or CategoriaDeError.Validacion
                ? new ResultadoDeRenovacion.Rechazada()
                : new ResultadoDeRenovacion.Transitoria();
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return new ResultadoDeRenovacion.Transitoria();
        }
    }

    public async Task<bool> RevocarRenovacion(string renovacion, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(renovacion);
        try
        {
            return (await sesiones.Cerrar(new ValorSecreto(renovacion), cancelacion).ConfigureAwait(false)).EsExito;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<bool> CerrarTodasLasSesiones(Guid usuarioId, CancellationToken cancelacion = default)
    {
        try
        {
            return (await sesiones.CerrarTodas(usuarioId, cancelacion).ConfigureAwait(false)).EsExito;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return false;
        }
    }
}
