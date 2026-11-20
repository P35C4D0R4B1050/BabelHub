using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>La configuración de Biblioteca es inválida para el entorno (por ejemplo, producción sin el almacén de libros): el arranque falla nombrando qué falta, sin ningún valor.</summary>
public sealed class ConfiguracionDeBibliotecaInvalidaException(string mensaje) : Exception(mensaje);

/// <summary>
/// La composición del módulo Biblioteca (T15; <c>ecommerce-biblioteca-digital</c>): los repositorios de PostgreSQL bajo <c>EjecutarComo(biblioteca)</c>, los casos de uso por alcance de solicitud, los contratos públicos y <b>la composición real</b>: Biblioteca es la implementación de
/// <see cref="IPosesionDeEdiciones"/> (Carrito), <see cref="IAccesosDeBiblioteca"/> (Pedidos), <see cref="IAccesosParaDevoluciones"/> (Devoluciones) e <see cref="IDescargasDeLaBiblioteca"/> (<c>web-base</c>) y <b>reemplaza</b> a los dobles provisionales de esos módulos esté o no ya registrado
/// uno (sin referenciar el tipo del doble y sin importar el orden). Con <c>esProduccion</c>, <b>sin el almacén de libros</b> (credencial de solo lectura, <c>Almacen:Biblioteca:</c>) el arranque falla: el contenido sintético en memoria nunca se publica. Necesita ya registrados: <see cref="IUnidadDeTrabajo"/>,
/// <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>, <see cref="IDenegacionDeAcceso"/>, <see cref="IReloj"/>, <see cref="IControlDeLimites"/>, <see cref="IAuditoria"/>, <see cref="IPublicadorDeAlertasTecnicas"/>, <see cref="IEstadoDeCuenta"/>, <see cref="IConsultaDeCatalogo"/> y
/// <see cref="IReferenciaDeArchivosDelCatalogo"/>.
/// </summary>
public static class ComposicionDeBiblioteca
{
    public static IServiceCollection AgregarBiblioteca(this IServiceCollection servicios, bool esProduccion, OpcionesDelAlmacenDeObjetos? almacenDeLibros = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // ---------- El almacén de libros: de solo lectura en producción; contenido sintético en memoria en desarrollo y pruebas ----------
        if (almacenDeLibros is not null)
        {
            var opciones = new OpcionesDelAlmacenDeLibros(almacenDeLibros.Endpoint, almacenDeLibros.Region, almacenDeLibros.ClaveDeAcceso, almacenDeLibros.ClaveSecreta, "libros", almacenDeLibros.RutaEnLaUrl);
            servicios.Replace(ServiceDescriptor.Singleton<IEntregaDeArchivos>(_ => new S3EntregaDeArchivos(ClienteDeLectura(opciones), opciones)));
        }
        else if (esProduccion)
        {
            throw new ConfiguracionDeBibliotecaInvalidaException("Biblioteca: falta el almacén de libros (BABELHUB__Almacen__Biblioteca__…, credencial de solo lectura): en producción no se publica con contenido sintético.");
        }
        else
        {
            servicios.TryAddSingleton<IEntregaDeArchivos, EntregaDeArchivosEnMemoria>();
        }

        // ---------- Infraestructura (por solicitud: usa la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDeBiblioteca>();
        servicios.TryAddScoped<IRepositorioDeAccesos, RepositorioDeAccesosPostgres>();
        servicios.TryAddScoped<IRepositorioDeDescargas, RepositorioDeDescargasPostgres>();

        // ---------- Aplicación ----------
        servicios.TryAddScoped<AutorizadorDeBiblioteca>();
        servicios.TryAddScoped<ProyectorDeAccesos>();
        servicios.TryAddScoped<ConcederAcceso>();
        servicios.TryAddScoped<RevocarAcceso>();
        servicios.TryAddScoped<ComprobarPosesion>();
        servicios.TryAddScoped<ListarMiBiblioteca>();
        servicios.TryAddScoped<ObtenerAcceso>();
        servicios.TryAddScoped<ConsultarBibliotecaDeUnCliente>();
        servicios.TryAddScoped<ConsumirDescarga>();
        servicios.TryAddScoped<OrquestadorDeDescargas>();
        servicios.TryAddScoped<PedirAyuda>();
        servicios.TryAddScoped<ListarAyudas>();
        servicios.TryAddScoped<RestituirDescargas>();
        servicios.TryAddScoped<CerrarAyuda>();
        servicios.TryAddScoped<AccesosParaDevolucionesDeLaAplicacion>();
        servicios.TryAddScoped<AccesosDeBibliotecaDeLaAplicacion>();
        servicios.TryAddScoped<EliminarBibliotecaDeUnaCuenta>();
        servicios.TryAddScoped<DepurarRegistrosDeDescargas>();
        servicios.TryAddScoped<DepurarAyudas>();
        servicios.TryAddScoped<PurgarClavesDeDescarga>();

        // ---------- Contratos públicos propios ----------
        servicios.TryAddScoped<IBibliotecaDelCliente, BibliotecaDelClienteDeLaAplicacion>();
        servicios.TryAddScoped<IAyudaDeLaBiblioteca, AyudaDeLaBibliotecaDeLaAplicacion>();
        servicios.TryAddScoped<IAdministracionDeLaBiblioteca, AdministracionDeLaBibliotecaDeLaAplicacion>();

        // ---------- La composición real: Biblioteca es la implementación; reemplaza a los dobles provisionales de Carrito, Pedidos, Devoluciones y web-base ----------
        servicios.Replace(ServiceDescriptor.Scoped<IPosesionDeEdiciones>(sp => sp.GetRequiredService<ComprobarPosesion>()));
        servicios.Replace(ServiceDescriptor.Scoped<IAccesosDeBiblioteca>(sp => sp.GetRequiredService<AccesosDeBibliotecaDeLaAplicacion>()));
        servicios.Replace(ServiceDescriptor.Scoped<IAccesosParaDevoluciones>(sp => sp.GetRequiredService<AccesosParaDevolucionesDeLaAplicacion>()));
        servicios.Replace(ServiceDescriptor.Scoped<IDescargasDeLaBiblioteca>(sp => sp.GetRequiredService<OrquestadorDeDescargas>()));

        // La parte de Biblioteca de la cancelación de la cuenta: se suma a la enumeración que Identidad recorre (no se reemplaza a nadie).
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IAplicadorDeCancelacion, EliminarBibliotecaDeUnaCuenta>());
        return servicios;
    }

    /// <summary>El cliente de solo lectura del almacén: plazo corto y <b>sin reintentos</b> (el <c>HEAD</c> tiene su propio plazo de 3 segundos, R04).</summary>
    private static Amazon.S3.IAmazonS3 ClienteDeLectura(OpcionesDelAlmacenDeLibros opciones) =>
        new Amazon.S3.AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(opciones.ClaveDeAcceso, opciones.ClaveSecreta.Revelar()),
            new Amazon.S3.AmazonS3Config
            {
                ServiceURL = opciones.Endpoint.ToString(),
                ForcePathStyle = opciones.RutaEnLaUrl,
                AuthenticationRegion = opciones.Region,
                RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED,
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(10),
            });
}

public static class ComposicionDeLosTrabajosDeBiblioteca
{
    /// <summary>
    /// Registra los trabajos de fondo de Biblioteca (purga horaria de claves y depuraciones diarias a las 03:30 de Lima) y el servicio que los corre. <paramref name="alcanceDeSistema"/> lo construye el anfitrión —la Api— con sus servicios y fija el actor «sistema» alrededor de cada corrida.
    /// Se llama <b>solo en modo normal</b> y después de <see cref="ComposicionDeBiblioteca.AgregarBiblioteca"/>; quien la llame añade también <c>AddHostedService&lt;ServicioDeTrabajosDeBiblioteca&gt;()</c>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeFondoDeBiblioteca(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistemaDeBiblioteca> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeBiblioteca.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            new AlertasTecnicasPorCorrida(sp.GetRequiredService<IServiceScopeFactory>()),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDeBiblioteca(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeBiblioteca>>()));
        return servicios;
    }
}
