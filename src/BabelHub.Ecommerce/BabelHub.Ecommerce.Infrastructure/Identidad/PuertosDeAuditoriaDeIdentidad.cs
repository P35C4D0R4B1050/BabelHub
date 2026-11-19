using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// El registro de los puertos que <b>Auditoría define e Identidad implementa</b> (identidad-seguridad T27 y T36; D09, D17): escribir, leer y depurar los registros de <c>identidad.registros</c> y listar las cuentas
/// canceladas por anonimizar. Está en el espacio de nombres de Identidad (el único, además de Auditoría, que puede depender de estos puertos: AUD-23) para que la composición general no los toque; los demás módulos no
/// pueden inyectarlos, porque falsificarían la auditoría con un actor arbitrario.
/// </summary>
public static class PuertosDeAuditoriaDeIdentidad
{
    /// <param name="servicios">El registro de servicios.</param>
    /// <param name="depuracion">La fuente de datos del rol de depuración (<c>auditoria_depuracion</c>), que la aplicación no comparte con ningún otro rol (AUD-19).</param>
    public static IServiceCollection AgregarPuertosDeAuditoriaDeIdentidad(this IServiceCollection servicios, Func<IServiceProvider, NpgsqlDataSource> depuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(depuracion);
        servicios.TryAddScoped<IAlmacenDeRegistrosAislados, PostgresAlmacenDeRegistrosAislados>();
        servicios.TryAddScoped<IFuenteDeRegistrosAislados, PostgresFuenteDeRegistrosAislados>();
        servicios.TryAddScoped<IDepuradorDeRegistrosAislados>(sp => new PostgresDepuradorDeRegistrosAislados(depuracion(sp)));
        servicios.TryAddScoped<ICuentasCanceladasPendientes, CuentasCanceladasPendientesDeIdentidad>();
        return servicios;
    }
}
