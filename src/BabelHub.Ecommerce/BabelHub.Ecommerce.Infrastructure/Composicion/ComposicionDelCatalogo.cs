using Amazon.S3;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// Lo que el anfitrión aporta al módulo Catálogo: el cliente del almacén de objetos con su credencial propia y los contenedores, y las claves de firma del token de ingresos. <b>Ningún secreto vive aquí</b>: el anfitrión los lee de las
/// variables de entorno (<c>BABELHUB__Almacen__Catalogo__*</c> y <c>BABELHUB__Catalogo__ClaveDeIngresos</c> y su <c>…Anterior</c>) y falla al arrancar nombrando la variable si faltan o son débiles.
/// </summary>
public sealed record OpcionesDelCatalogo(
    IAmazonS3 ClienteDelAlmacen,
    string ClaveDeIngresos,
    string? ClaveDeIngresosAnterior = null,
    string ContenedorDeLibros = "libros",
    string ContenedorDePortadas = "portadas",
    TimeSpan? PlazoDeLaSolicitud = null,
    TimeSpan? PresupuestoDeIngresos = null)
{
    /// <summary>Lo que se imprime no lleva las claves de firma ni el cliente del almacén (SEG-10).</summary>
    private bool PrintMembers(System.Text.StringBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        constructor.Append("ContenedorDeLibros = ").Append(ContenedorDeLibros).Append(", ContenedorDePortadas = ").Append(ContenedorDePortadas);
        return true;
    }
}

public static class ComposicionDelCatalogo
{
    /// <summary>
    /// Compone el módulo Catálogo (T18): repositorios de PostgreSQL bajo <c>EjecutarComo(catalogo)</c>, el almacén S3, el adaptador del Inventario sobre <see cref="IClienteDelInventario"/>, la memoria de disponibilidad, el emisor de tokens de ingreso, el limitador
    /// de subidas y los casos de uso con su <b>transacción por caso de uso</b> (alta, edición, baja, retiro, ingreso y su marcado, y la emisión de la clave: los demás no son transaccionales por diseño, plan §5). El anfitrión aporta <see cref="IUnidadDeTrabajo"/>,
    /// <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>, <see cref="IDenegacionDeAcceso"/>, <see cref="IAuditoria"/>, <see cref="IReloj"/> y <see cref="IClienteDelInventario"/>. Se llama <b>antes</b> de
    /// <see cref="CatalogoPendienteExtensiones.AgregarCatalogoPendiente"/> (un registro real anterior se respeta).
    /// </summary>
    public static IServiceCollection AgregarCatalogo(this IServiceCollection servicios, OpcionesDelCatalogo opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        // ---------- Infraestructura (singletons: sin estado por solicitud) ----------
        servicios.TryAddSingleton<IMemoriaDeDisponibilidad>(new MemoriaDeDisponibilidadEnMemoria());
        servicios.TryAddSingleton<IAlmacenDeArchivos>(new S3AlmacenDeArchivos(opciones.ClienteDelAlmacen, opciones.ContenedorDeLibros, opciones.ContenedorDePortadas));
        servicios.TryAddSingleton<IEmisorDeClavesDeIngreso>(new EmisorDeClavesDeIngresoHmac(opciones.ClaveDeIngresos, opciones.ClaveDeIngresosAnterior));
        servicios.TryAddSingleton<IAzarDelCatalogo, AzarDelCatalogo>();
        servicios.TryAddSingleton<LimitadorDeSubidas>();

        // ---------- Infraestructura (por solicitud: usa la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDelCatalogo>();
        servicios.TryAddScoped<IRepositorioDeObras, RepositorioDeObrasPostgres>();
        servicios.TryAddScoped<IRepositorioDeReferenciasDeArchivo, RepositorioDeReferenciasPostgres>();
        servicios.TryAddScoped<IRepositorioDeIngresos, RepositorioDeIngresosPostgres>();
        servicios.TryAddScoped<ILecturasDelCatalogo, LecturasDelCatalogoPostgres>();
        servicios.TryAddScoped<IInventarioDelCatalogo, ClienteDelInventarioDelCatalogo>();

        // ---------- Aplicación ----------
        servicios.TryAddSingleton<IValidador<RegistrarObraEntrada>, ValidadorDeRegistrarObra>();
        servicios.TryAddSingleton<IValidador<EditarObraEntrada>, ValidadorDeEditarObra>();
        servicios.TryAddSingleton<IValidador<DarDeBajaObraEntrada>, ValidadorDeDarDeBajaObra>();
        servicios.TryAddSingleton<IValidador<RegistrarEdicionEntrada>, ValidadorDeRegistrarEdicion>();
        servicios.TryAddSingleton<IValidador<EditarEdicionEntrada>, ValidadorDeEditarEdicion>();
        servicios.TryAddSingleton<IValidador<DarDeBajaEdicionEntrada>, ValidadorDeDarDeBajaEdicion>();
        servicios.TryAddSingleton<IValidador<RetirarEdicionDigitalEntrada>, ValidadorDeRetirarEdicionDigital>();
        servicios.TryAddSingleton<IValidador<EmitirClaveDeIngresoEntrada>, ValidadorDeEmitirClaveDeIngreso>();
        servicios.TryAddSingleton<IValidador<RegistrarIngresoEntrada>, ValidadorDeRegistrarIngreso>();
        servicios.TryAddSingleton<IValidador<ReintentarIngresoEntrada>, ValidadorDeReintentarIngreso>();
        servicios.TryAddSingleton<IValidador<ConciliarIngresoEntrada>, ValidadorDeConciliarIngreso>();
        servicios.TryAddSingleton<IValidador<ListarIngresosEntrada>, ValidadorDeListarIngresos>();
        servicios.TryAddSingleton<IValidador<SubirArchivoEntrada>, ValidadorDeSubirArchivo>();
        servicios.TryAddSingleton<IValidador<ListarObrasDeAdministracionEntrada>, ValidadorDeListarObrasDeAdministracion>();

        servicios.TryAddScoped(sp => new AutorizadorDelCatalogo(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.TryAddScoped(sp => new ConsumirReferenciaDeArchivo(sp.GetRequiredService<IRepositorioDeReferenciasDeArchivo>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped(sp => new ConstructorDeEdiciones(sp.GetRequiredService<ConsumirReferenciaDeArchivo>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped(sp => new SolicitudDeIngresosIniciales(sp.GetRequiredService<IRepositorioDeIngresos>(), sp.GetRequiredService<IEmisorDeClavesDeIngreso>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped(sp => new ResolutorDeDisponibilidad(sp.GetRequiredService<IInventarioDelCatalogo>(), sp.GetRequiredService<IMemoriaDeDisponibilidad>(), sp.GetRequiredService<IReloj>()));

        servicios.TryAddScoped(sp => new ConsultaDeCatalogoDeLaAplicacion(
            sp.GetRequiredService<ILecturasDelCatalogo>(), sp.GetRequiredService<ResolutorDeDisponibilidad>(), sp.GetRequiredService<IReloj>(), sp.GetRequiredService<IAzarDelCatalogo>(), opciones.PlazoDeLaSolicitud));
        servicios.TryAddScoped<IConsultaDeCatalogo>(sp => sp.GetRequiredService<ConsultaDeCatalogoDeLaAplicacion>());
        servicios.TryAddScoped<IDisponibilidadDeEdiciones>(sp => sp.GetRequiredService<ConsultaDeCatalogoDeLaAplicacion>());
        // Solo para Biblioteca (D13, D17): la clave del archivo, su formato, su huella y su tamaño; una arquitectura lo exige.
        servicios.AgregarLaReferenciaDeArchivos();

        // Los casos de uso que escriben van dentro de su transacción (un fallo, también el de la auditoría, lo revierte todo).
        AgregarTransaccional(servicios, sp => new RegistrarObra(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<RegistrarObraEntrada>>(), sp.GetRequiredService<ConsumirReferenciaDeArchivo>(), sp.GetRequiredService<ConstructorDeEdiciones>(),
            sp.GetRequiredService<SolicitudDeIngresosIniciales>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new RegistrarEdicion(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<RegistrarEdicionEntrada>>(), sp.GetRequiredService<ConstructorDeEdiciones>(), sp.GetRequiredService<SolicitudDeIngresosIniciales>(),
            sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IAuditoria>()));
        AgregarTransaccional(servicios, sp => new EditarObra(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<EditarObraEntrada>>(), sp.GetRequiredService<ConsumirReferenciaDeArchivo>(), sp.GetRequiredService<IRepositorioDeObras>(),
            sp.GetRequiredService<IRepositorioDeReferenciasDeArchivo>(), sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new EditarEdicion(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<EditarEdicionEntrada>>(), sp.GetRequiredService<ConsumirReferenciaDeArchivo>(), sp.GetRequiredService<ConstructorDeEdiciones>(),
            sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IRepositorioDeReferenciasDeArchivo>(), sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new DarDeBajaObra(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<DarDeBajaObraEntrada>>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IRetiroDeEdicionDeCarritos>(),
            sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new DarDeBajaEdicion(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<DarDeBajaEdicionEntrada>>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IRetiroDeEdicionDeCarritos>(),
            sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new RetirarEdicionDigital(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<RetirarEdicionDigitalEntrada>>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new MarcarResultadoDeIngreso(sp.GetRequiredService<IRepositorioDeIngresos>(), sp.GetRequiredService<IMemoriaDeDisponibilidad>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new RegistrarIngresoDeMercaderia(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<RegistrarIngresoEntrada>>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IRepositorioDeIngresos>(),
            sp.GetRequiredService<IEmisorDeClavesDeIngreso>(), sp.GetRequiredService<IReloj>()));
        AgregarTransaccional(servicios, sp => new EmitirClaveDeIngreso(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<EmitirClaveDeIngresoEntrada>>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IEmisorDeClavesDeIngreso>(), sp.GetRequiredService<IReloj>()));

        // Los que no son transaccionales por diseño (plan §5): cada uno abre sus propias transacciones cortas y nunca mantiene una abierta durante una llamada externa.
        servicios.TryAddScoped<ICasoDeUso<SubirArchivoEntrada, ArchivoSubido>>(sp => new SubirArchivo(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<SubirArchivoEntrada>>(), sp.GetRequiredService<IRepositorioDeReferenciasDeArchivo>(), sp.GetRequiredService<IAlmacenDeArchivos>(),
            sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IReloj>(), sp.GetRequiredService<LimitadorDeSubidas>(),
            sp.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), sp.GetRequiredService<IOrigenDeSolicitud>()));
        servicios.TryAddScoped(sp => new ObtenerPortada(sp.GetRequiredService<ILecturasDelCatalogo>(), sp.GetRequiredService<IAlmacenDeArchivos>()));
        servicios.TryAddScoped<IArchivosDelCatalogo>(sp => new ArchivosDelCatalogoDeLaAplicacion(sp.GetRequiredService<ICasoDeUso<SubirArchivoEntrada, ArchivoSubido>>(), sp.GetRequiredService<ObtenerPortada>()));

        servicios.TryAddScoped(sp => new OrquestadorDeIngresos(
            sp.GetRequiredService<IInventarioDelCatalogo>(), sp.GetRequiredService<ICasoDeUso<MarcarResultadoDeIngresoEntrada, EstadoDeIngresoSolicitado>>(), opciones.PresupuestoDeIngresos));
        servicios.TryAddScoped<ICasoDeUso<ReintentarIngresoEntrada, ResultadoDeIngreso>>(sp => new ReintentarIngresoPendiente(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<ReintentarIngresoEntrada>>(), sp.GetRequiredService<IRepositorioDeIngresos>(), sp.GetRequiredService<IUnidadDeTrabajo>(),
            sp.GetRequiredService<OrquestadorDeIngresos>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped<ICasoDeUso<ConciliarIngresoEntrada, ResultadoDeIngreso>>(sp => new ConciliarIngresoPendiente(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<ConciliarIngresoEntrada>>(), sp.GetRequiredService<IRepositorioDeIngresos>(), sp.GetRequiredService<IInventarioDelCatalogo>(),
            sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IMemoriaDeDisponibilidad>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped<ICasoDeUso<ListarIngresosEntrada, HistorialDeIngresos>>(sp => new ListarIngresos(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<ListarIngresosEntrada>>(), sp.GetRequiredService<IRepositorioDeObras>(), sp.GetRequiredService<IRepositorioDeIngresos>(),
            sp.GetRequiredService<IInventarioDelCatalogo>(), sp.GetRequiredService<IUnidadDeTrabajo>()));
        servicios.TryAddScoped(sp => new LecturasDeAdministracion(
            sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IValidador<ListarObrasDeAdministracionEntrada>>(), sp.GetRequiredService<ILecturasDelCatalogo>(), sp.GetRequiredService<IInventarioDelCatalogo>(), sp.GetRequiredService<IReloj>()));

        servicios.TryAddScoped<IAdministracionDelCatalogo>(sp => new AdministracionDelCatalogoDeLaAplicacion(
            sp.GetRequiredService<ICasoDeUso<RegistrarObraEntrada, ResultadoDeAlta>>(),
            sp.GetRequiredService<ICasoDeUso<EditarObraEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<DarDeBajaObraEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<RegistrarEdicionEntrada, ResultadoDeAlta>>(),
            sp.GetRequiredService<ICasoDeUso<EditarEdicionEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<DarDeBajaEdicionEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<RetirarEdicionDigitalEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<LecturasDeAdministracion>(),
            sp.GetRequiredService<OrquestadorDeIngresos>(),
            sp.GetRequiredService<IRepositorioDeIngresos>()));
        servicios.TryAddScoped<IIngresosDeMercaderia>(sp => new IngresosDeMercaderiaDeLaAplicacion(
            sp.GetRequiredService<ICasoDeUso<EmitirClaveDeIngresoEntrada, ClaveDeIngresoEmitida>>(),
            sp.GetRequiredService<ICasoDeUso<RegistrarIngresoEntrada, IngresoPedido>>(),
            sp.GetRequiredService<OrquestadorDeIngresos>(),
            sp.GetRequiredService<ICasoDeUso<ReintentarIngresoEntrada, ResultadoDeIngreso>>(),
            sp.GetRequiredService<ICasoDeUso<ConciliarIngresoEntrada, ResultadoDeIngreso>>(),
            sp.GetRequiredService<ICasoDeUso<ListarIngresosEntrada, HistorialDeIngresos>>()));

        servicios.TryAddScoped<IAnonimizadorDelCatalogo, AnonimizadorDelCatalogoPostgres>();
        servicios.TryAddScoped<IMantenimientoDelCatalogo>(sp => new MantenimientoDelCatalogoDeLaAplicacion(sp.GetRequiredService<DepurarArchivosSinUso>(), sp.GetRequiredService<AnonimizarIngresosSolicitados>()));
        servicios.TryAddScoped(sp => new AnonimizarIngresosSolicitados(
            sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IAnonimizadorDelCatalogo>(), sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IAuditoria>(), sp.GetRequiredService<IReloj>()));
        servicios.TryAddScoped(sp => new DepurarArchivosSinUso(
            sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IRepositorioDeReferenciasDeArchivo>(), sp.GetRequiredService<IAlmacenDeArchivos>(), sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IReloj>()));

        // Mientras Carrito no exista, quitar una edición de los carritos no hace nada (D07); el retiro real, cuando exista, se registra antes y esta línea se respeta.
        servicios.AgregarRetiroNuloDeCarritos();
        return servicios;
    }

    /// <summary>Registra el caso de uso envuelto en <see cref="TransaccionPorCasoDeUso{TEntrada, TSalida}"/>: la transacción es del decorador, no del caso de uso.</summary>
    private static void AgregarTransaccional<TEntrada, TSalida>(IServiceCollection servicios, Func<IServiceProvider, ICasoDeUso<TEntrada, TSalida>> fabrica) =>
        servicios.TryAddScoped<ICasoDeUso<TEntrada, TSalida>>(sp => new TransaccionPorCasoDeUso<TEntrada, TSalida>(fabrica(sp), sp.GetRequiredService<IUnidadDeTrabajo>()));
}
