using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// El registro de dependencias del módulo <c>Fuentes</c> (T20). El anfitrión pone lo que es de la plataforma y de otros módulos: <see cref="NpgsqlDataSource"/>, <see cref="IActorActual"/>, <see cref="IReloj"/>,
/// <c>IAuditoria</c>, <c>IDenegacionDeAcceso</c> e <c>IPublicadorDeAlertasTecnicas</c>. Todo lo demás se registra aquí:
/// <list type="bullet">
/// <item>La unidad de trabajo, el acceso al esquema, los repositorios y los bloqueos asesores. <c>IAplicadorDeMovimientos</c> se registra con <c>AplicadorDeMovimientos</c> y <b>no</b> con
/// <c>MovimientosDeStock</c>, que lo inyecta (si no, habría un ciclo).</item>
/// <item>Los casos de uso, cada uno con su decorador: los de <b>escritura</b> con <c>TransaccionPorCasoDeUso</c> y, los idempotentes (<c>RegistrarRuta</c>, <c>RegistrarFichaDeEdicion</c> y
/// <c>DesignarFuenteDeRecepcion</c>), con <c>ReintentoPorInterbloqueo</c> por fuera de la transacción; los de <b>lectura</b> con <c>LecturaCoherentePorCasoDeUso</c> (<c>REPEATABLE READ</c> de solo lectura,
/// D06), nunca con una transacción <c>READ COMMITTED</c>.</item>
/// <item>Los adaptadores nulos de los puertos de <c>Reservas</c> y <c>Ajustes</c> (<c>TryAdd</c>: un adaptador real registrado antes gana).</item>
/// </list>
/// </summary>
public static class ComposicionDeFuentes
{
    /// <summary>Los casos de uso del módulo que se pueden repetir entero ante un interbloqueo: sus claves naturales los hacen idempotentes (plan §5).</summary>
    public static IReadOnlyList<Type> CasosDeUsoIdempotentes { get; } = [typeof(RegistrarRuta), typeof(RegistrarFichaDeEdicion), typeof(DesignarFuenteDeRecepcion)];

    public static IServiceCollection AgregarModuloFuentes(this IServiceCollection servicios, OpcionesDeReintentoPorInterbloqueo? reintento = null, OpcionesDeBloqueosAsesores? bloqueos = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var opcionesDeReintento = reintento ?? new OpcionesDeReintentoPorInterbloqueo();

        // Plataforma de datos: una sola unidad de trabajo, vista por sus puertos.
        servicios.TryAddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>(), actor: sp.GetRequiredService<IActorActual>()));
        servicios.TryAddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.TryAddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.TryAddSingleton<ILecturaCoherente>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.TryAddSingleton<IBloqueosAsesores>(sp => new NpgsqlBloqueosAsesores(sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>(), bloqueos));

        servicios.AgregarEjecutorDeTrabajoDeFondo();

        // Repositorios y piezas del contrato público.
        servicios.AgregarAccesoAFuentes();
        servicios.AddScoped<IRepositorioDeFuentes, NpgsqlRepositorioDeFuentes>();
        servicios.AddScoped<IRepositorioDeRutas, NpgsqlRepositorioDeRutas>();
        servicios.AddScoped<IRepositorioDeFichas, NpgsqlRepositorioDeFichas>();
        servicios.AddScoped<IRepositorioDeExistencias, NpgsqlRepositorioDeExistencias>();
        servicios.AddScoped<IAplicadorDeMovimientos, AplicadorDeMovimientos>();
        servicios.AddScoped<IMovimientosDeStock, MovimientosDeStock>();
        servicios.AddScoped<IExistencias, ConsultaDeExistencias>();
        servicios.AddScoped<DirectorioDeFuentes>();
        servicios.AddScoped<IDirectorioDeFuentes>(sp => sp.GetRequiredService<DirectorioDeFuentes>());
        servicios.AddScoped<IFichasDeEdiciones>(sp => sp.GetRequiredService<DirectorioDeFuentes>());

        // Los puertos de otros módulos: nulos hasta que existan.
        servicios.TryAddSingleton<IReservasDeStock, NuloReservasDeStock>();
        servicios.TryAddSingleton<IOrigenDeUnidadesVendidas, NuloOrigenDeUnidadesVendidas>();
        servicios.TryAddSingleton<IReingresosPorDevolucion, NuloReingresosPorDevolucion>();

        // Los casos de uso.
        servicios.AddScoped(sp => new AutorizadorDeFuentes(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.AddScoped<TotalesDeFuente>();

        servicios.AddSingleton<IValidador<RegistrarFuenteEntrada>, ValidadorDeRegistrarFuente>();
        servicios.AddSingleton<IValidador<EditarFuenteEntrada>, ValidadorDeEditarFuente>();
        servicios.AddSingleton<IValidador<DesignarFuenteDeRecepcionEntrada>, ValidadorDeDesignarRecepcion>();
        servicios.AddSingleton<IValidador<RegistrarRutaEntrada>, ValidadorDeRegistrarRuta>();
        servicios.AddSingleton<IValidador<ConsultarPlazoDeRutaEntrada>, ValidadorDeConsultarPlazo>();
        servicios.AddSingleton<IValidador<RegistrarFichaDeEdicionEntrada>, ValidadorDeRegistrarFicha>();
        servicios.AddSingleton<IValidador<ConsultarDisponibilidadEntrada>, ValidadorDeConsultarDisponibilidad>();
        servicios.AddSingleton<IValidador<ConsultarFuenteEntrada>, ValidadorDeConsultarFuente>();
        servicios.AddSingleton<IValidador<ConsultarExistenciasDeSucursalEntrada>, ValidadorDeConsultarExistencias>();
        servicios.AddSingleton<IValidador<ConsultarUnidadesVendidasEntrada>, ValidadorDeConsultarUnidadesVendidas>();
        servicios.AddSingleton<IValidador<ConsultarContactoDeNotificacionEntrada>, ValidadorDeConsultarContacto>();

        servicios.Escritura<RegistrarFuenteEntrada, DetalleDeFuente, RegistrarFuente>(opcionesDeReintento);
        servicios.Escritura<EditarFuenteEntrada, DetalleDeFuente, EditarFuente>(opcionesDeReintento);
        servicios.Escritura<DesignarFuenteDeRecepcionEntrada, RecepcionDesignada, DesignarFuenteDeRecepcion>(opcionesDeReintento);
        servicios.Escritura<RegistrarRutaEntrada, RutaDefinida, RegistrarRuta>(opcionesDeReintento);
        servicios.Escritura<RegistrarFichaDeEdicionEntrada, FichaDeEdicionGuardada, RegistrarFichaDeEdicion>(opcionesDeReintento);

        servicios.Lectura<ConsultarPlazoDeRutaEntrada, PlazoDeRutaConsultado, ConsultarPlazoDeRuta>();
        servicios.Lectura<ListarRutasEntrada, IReadOnlyList<RutaListada>, ListarRutas>();
        servicios.Lectura<ConsultarDisponibilidadEntrada, DisponibilidadConsultada, ConsultarDisponibilidad>();
        servicios.Lectura<ListarSucursalesEntrada, IReadOnlyList<SucursalPublica>, ListarSucursales>();
        servicios.Lectura<ConsultarFuenteEntrada, DetalleDeFuente, ConsultarFuente>();
        servicios.Lectura<ListarFuentesEntrada, IReadOnlyList<FuenteDeStock>, ListarFuentes>();
        servicios.Lectura<ConsultarExistenciasDeSucursalEntrada, PaginaDeExistencias, ConsultarExistenciasDeSucursal>();
        servicios.Lectura<ConsultarUnidadesVendidasEntrada, UnidadesVendidasConsultadas, ConsultarUnidadesVendidas>();
        servicios.Lectura<ConsultarContactoDeNotificacionEntrada, ContactoDeNotificacion, ConsultarContactoDeNotificacion>();

        return servicios;
    }

    /// <summary>
    /// La comprobación de arranque de producción: ningún puerto de <c>Reservas</c> o <c>Ajustes</c> debe estar resuelto por un adaptador nulo si el módulo real ya existe en la Infraestructura. Devuelve los
    /// puertos en ese estado (vacío si todo está bien); en producción, quien arranca falla si no está vacío.
    /// </summary>
    public static IReadOnlyList<string> AdaptadoresNulosConModuloReal(IServiceProvider proveedor)
    {
        ArgumentNullException.ThrowIfNull(proveedor);

        var ensamblado = typeof(ComposicionDeFuentes).Assembly;
        object[] resueltos =
        [
            proveedor.GetRequiredService<IReservasDeStock>(),
            proveedor.GetRequiredService<IOrigenDeUnidadesVendidas>(),
            proveedor.GetRequiredService<IReingresosPorDevolucion>(),
        ];

        return [.. resueltos
            .OfType<IAdaptadorNuloDeFuentes>()
            .Where(nulo => AdaptadoresNulosDeFuentes.ExisteAdaptadorReal(nulo.Puerto, ensamblado))
            .Select(nulo => nulo.Puerto.Name)];
    }

    /// <summary>Falla el arranque de producción si un puerto sigue resuelto por un adaptador nulo cuando su módulo real ya existe. Fuera de producción no hace nada.</summary>
    public static void ExigirAdaptadoresReales(IServiceProvider proveedor, bool esProduccion)
    {
        if (!esProduccion)
        {
            return;
        }

        var nulos = AdaptadoresNulosConModuloReal(proveedor);
        if (nulos.Count > 0)
        {
            throw new InvalidOperationException($"La composición de producción usa un adaptador nulo para {string.Join(", ", nulos)} aunque el módulo real ya existe.");
        }
    }

    private static void Escritura<TEntrada, TSalida, TCaso>(this IServiceCollection servicios, OpcionesDeReintentoPorInterbloqueo reintento)
        where TCaso : class, ICasoDeUso<TEntrada, TSalida>
    {
        var idempotente = CasosDeUsoIdempotentes.Contains(typeof(TCaso));
        servicios.AddScoped<TCaso>();
        servicios.AddScoped<ICasoDeUso<TEntrada, TSalida>>(sp =>
        {
            ICasoDeUso<TEntrada, TSalida> caso = new TransaccionPorCasoDeUso<TEntrada, TSalida>(sp.GetRequiredService<TCaso>(), sp.GetRequiredService<IUnidadDeTrabajo>());
            return idempotente
                ? new ReintentoPorInterbloqueo<TEntrada, TSalida>(caso, sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>(), reintento, esIdempotente: true)
                : caso;
        });
    }

    private static void Lectura<TEntrada, TSalida, TCaso>(this IServiceCollection servicios)
        where TCaso : class, ICasoDeUso<TEntrada, TSalida>
    {
        servicios.AddScoped<TCaso>();
        servicios.AddScoped<ICasoDeUso<TEntrada, TSalida>>(sp => new LecturaCoherentePorCasoDeUso<TEntrada, TSalida>(sp.GetRequiredService<TCaso>(), sp.GetRequiredService<ILecturaCoherente>()));
    }
}
