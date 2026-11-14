using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>Puerto: de dónde sale la lista de sucursales (el Inventario por REST, <c>sucursales.listar</c>). Falla si no responde.</summary>
public interface IFuenteDeSucursales
{
    Task<IReadOnlyList<SucursalDeLaLista>> Listar(CancellationToken cancelacion = default);
}

/// <summary>
/// Hasta que el cliente del Inventario tenga <c>sucursales.listar</c> (hallazgo R12 de <c>web-base</c> para el carril A) no hay fuente de sucursales:
/// siempre falla y el selector no se ofrece. No se inventa una lista.
/// </summary>
public sealed class FuenteDeSucursalesNoDisponible : IFuenteDeSucursales
{
    public Task<IReadOnlyList<SucursalDeLaLista>> Listar(CancellationToken cancelacion = default) =>
        throw new HttpRequestException("El Inventario no ofrece todavía la lista de sucursales.");
}

/// <summary>
/// La lista de sucursales con su <b>última lista conocida</b> (D16): si la fuente falla se devuelve la última que respondió, marcada como no
/// actualizada, para que el valor guardado de la persona se siga validando; sin ninguna lista conocida no hay nada que validar.
/// </summary>
public sealed class ListaDeSucursalesConUltimaConocida(IFuenteDeSucursales fuente, TimeProvider? reloj = null) : IListaDeSucursales
{
    /// <summary>La lista de la fuente se reutiliza este tiempo: la interfaz la pide en cada página y la fuente es una llamada REST.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);

    /// <summary>Tras un fallo de la fuente no se vuelve a llamar antes de este plazo, para que una caída del Inventario no demore cada página.</summary>
    public static readonly TimeSpan EsperaTrasUnFallo = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private readonly Lock _candado = new();
    private IReadOnlyList<SucursalDeLaLista>? _ultima;
    private DateTimeOffset _consultadaEn;
    private DateTimeOffset _noReintentarAntesDe;

    public async Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default)
    {
        var ahora = _reloj.GetUtcNow();
        lock (_candado)
        {
            if (_ultima is not null && ahora - _consultadaEn < Vigencia)
            {
                return new ListaDeSucursales(_ultima, true);
            }

            if (ahora < _noReintentarAntesDe)
            {
                return _ultima is null ? null : new ListaDeSucursales(_ultima, false);
            }
        }

        try
        {
            var actual = await fuente.Listar(cancelacion).ConfigureAwait(false);
            if (actual.Count > 0)
            {
                lock (_candado)
                {
                    _ultima = actual;
                    _consultadaEn = ahora;
                }

                return new ListaDeSucursales(actual, true);
            }
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Se usa la última lista conocida.
        }

        lock (_candado)
        {
            _noReintentarAntesDe = ahora + EsperaTrasUnFallo;
            return _ultima is null ? null : new ListaDeSucursales(_ultima, false);
        }
    }
}

public static class ModuloWebExtensiones
{
    /// <summary>
    /// Registra el módulo <c>Web</c>: textos legales, preferencia de sucursal y sus adaptadores provisionales (hasta que existan los de Identidad y el Inventario).
    /// La versión provisional de los textos solo se compone si <paramref name="permitirVersionesProvisionales"/> (nunca en producción): sin ella el texto es «no disponible».
    /// </summary>
    public static IServiceCollection AgregarModuloWeb(this IServiceCollection servicios, bool permitirVersionesProvisionales)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton<IContenidoDeTextosLegales, ContenidoDeTextosEmbebido>();
        if (permitirVersionesProvisionales)
        {
            servicios.AddSingleton<IVersionesDeTextosLegales, VersionesDeTextosProvisionales>();
        }
        else
        {
            servicios.AddSingleton<IVersionesDeTextosLegales, VersionesDeTextosNoDisponibles>();
        }

        servicios.AddScoped<ITextosLegales, ConsultarTextoLegal>();
        servicios.AddSingleton<IFuenteDeSucursales, FuenteDeSucursalesNoDisponible>();
        servicios.AddSingleton<IListaDeSucursales, ListaDeSucursalesConUltimaConocida>();
        servicios.AddScoped<IPreferenciaDeSucursal, PreferenciaDeSucursal>();
        return servicios;
    }
}
