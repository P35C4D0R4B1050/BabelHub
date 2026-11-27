using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Application.Web.Indice;

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

/// <summary>La fuente real de sucursales: el Inventario por REST (<c>ListarSucursales</c>, operación pública de solo lectura). Un fallo del Inventario lanza <see cref="HttpRequestException"/>: la lista usa entonces la última conocida (D16).</summary>
public sealed class FuenteDeSucursalesDelInventario(BabelHub.Ecommerce.Application.Plataforma.IClienteDelInventario inventario) : IFuenteDeSucursales
{
    public async Task<IReadOnlyList<SucursalDeLaLista>> Listar(CancellationToken cancelacion = default)
    {
        var resultado = await inventario.ListarSucursales(cancelacion).ConfigureAwait(false);
        if (!resultado.Exitosa)
        {
            throw new HttpRequestException("El Inventario no devolvió la lista de sucursales.");
        }

        return [.. resultado.Valor.Select(s => new SucursalDeLaLista(s.Codigo, s.Nombre))];
    }
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
        // La versión vigente es la de Identidad (la misma fila con la que Identidad compara lo que la web envía al aceptar: IDE-57). Solo si Identidad no está compuesta (una prueba de la interfaz sola)
        // se cae a la provisional —y solo fuera de producción— o a «no disponible»; en el servicio compuesto la provisional nunca se usa.
        servicios.AddScoped<IVersionesDeTextosLegales>(sp =>
            sp.GetService<BabelHub.Ecommerce.Application.Identidad.IVersionesVigentesDeLosTextos>() is { } identidad
                ? new VersionesDeTextosDeIdentidad(identidad)
                : permitirVersionesProvisionales ? new VersionesDeTextosProvisionales() : new VersionesDeTextosNoDisponibles());

        servicios.AddScoped<ITextosLegales, ConsultarTextoLegal>();
        servicios.AddSingleton<IAreasDeInterfaz, AreasDeInterfaz>();
        servicios.AddSingleton<IMenuDeNavegacion, ResolverMenu>();
        // El índice infinito (web-indice-infinito T06): una función pura sin base de datos ni caché; solo necesita los bytes aleatorios criptográficos (AgregarSesionWeb ya los registra: TryAdd evita duplicarlos si el módulo se compone solo).
        servicios.TryAddSingleton<IAleatoriosSeguros, AleatoriosSeguros>();
        servicios.AddSingleton<IIndiceInfinito, ConsultarIndiceInfinito>();
        servicios.TryAddSingleton<IDesafioDelIngreso, DesafioDelIngresoSinIdentidad>();
        // La política de textos pendientes (T36): el estado y la aceptación los informa Identidad (provisionales y fallando cerrado hasta entonces); el tope de consultas es uno por proceso.
        servicios.TryAddSingleton<IEstadoDeCuentaDeLaWeb, EstadoDeCuentaSinIdentidad>();
        servicios.TryAddSingleton<IAceptacionDeTextosEnIdentidad, AceptacionDeTextosSinIdentidad>();
        // La fuente real es el Inventario; sin su cliente registrado (por ejemplo, en una prueba de la interfaz sola) sigue la fuente que falla.
        servicios.AddSingleton<IFuenteDeSucursales>(sp => sp.GetService<BabelHub.Ecommerce.Application.Plataforma.IClienteDelInventario>() is { } cliente ? new FuenteDeSucursalesDelInventario(cliente) : new FuenteDeSucursalesNoDisponible());
        servicios.AddSingleton<IListaDeSucursales, ListaDeSucursalesConUltimaConocida>();
        servicios.AddScoped<IPreferenciaDeSucursal, PreferenciaDeSucursal>();
        // El código QR del segundo factor (web-acceso): se genera aquí, sin terceros.
        servicios.TryAddSingleton<IGeneradorDeCodigoQr, CodecreteGeneradorDeCodigoQr>();
        return servicios;
    }

    /// <summary>
    /// Registra la política de textos pendientes (T36) y la aceptación (T38). Se llama <b>junto con la composición de la sesión</b> (almacén de sesiones, Identidad y Auditoría reales): la política usa el almacén
    /// para el retorno tras aceptar y el registro de seguridad para el evento del bloqueo, que no se registran en <see cref="AgregarModuloWeb"/>.
    /// </summary>
    public static IServiceCollection AgregarPoliticaDeTextosPendientes(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<LimitadorDeConsultasDeEstado>();
        servicios.TryAddScoped<IPoliticaDeAccesoPorTextos, ResolverAccesoConTextosPendientes>();
        servicios.TryAddScoped<IAceptacionDeTextos, AceptarTextosLegales>();
        return servicios;
    }
}
