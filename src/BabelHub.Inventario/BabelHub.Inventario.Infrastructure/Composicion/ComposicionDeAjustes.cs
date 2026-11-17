using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Ajustes.Interno;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Infrastructure.Ajustes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>La fuente de datos de la depuración de <c>Ajustes</c>: la del rol de depuración (<c>ajustes_depuracion</c>), distinta de la de la aplicación.</summary>
public sealed record OpcionesDelModuloDeAjustes(NpgsqlDataSource Depuracion);

/// <summary>
/// El registro de dependencias del módulo <c>Ajustes</c> (T11, T13). Vive fuera de la carpeta del módulo porque la Infraestructura de un módulo no puede depender de la de <c>Plataforma</c> (regla 13).
/// <see cref="AgregarModuloAjustes"/> se llama antes o después de <c>AgregarModuloFuentes</c>: la clase de lectura de lo devuelto por pedido <b>reemplaza</b> al adaptador nulo de <c>IReingresosPorDevolucion</c>. El anfitrión pone, además,
/// lo de <c>Fuentes</c> (unidad de trabajo, directorio, <c>IMovimientosDeStock</c>…), <c>IControlDeLimites</c>, <c>IAuditoria</c> e <c>IDenegacionDeAcceso</c>.
/// </summary>
public static class ComposicionDeAjustes
{
    /// <summary>
    /// Los casos de uso del módulo que se pueden repetir entero ante un interbloqueo: el reingreso (idempotente por devolución y edición) y la depuración. El ajuste manual y el ingreso <b>no</b>: consumen el presupuesto del
    /// personal en una transacción independiente y repetirlos lo contaría dos veces (D06); un interbloqueo es improbable porque el orden de bloqueo es único.
    /// </summary>
    public static IReadOnlyList<Type> CasosDeUsoIdempotentes { get; } = [typeof(ReingresarPorDevolucion), typeof(DepurarAjustes)];

    public static IServiceCollection AgregarModuloAjustes(this IServiceCollection servicios, OpcionesDelModuloDeAjustes opciones, OpcionesDeReintentoPorInterbloqueo? reintento = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);
        var opcionesDeReintento = reintento ?? new OpcionesDeReintentoPorInterbloqueo();

        servicios.TryAddSingleton<IElevacionDeAlcance>(sp => (IElevacionDeAlcance)sp.GetRequiredService<IUnidadDeTrabajo>());
        servicios.AddScoped<AccesoAAjustes>();
        servicios.AddScoped<IRepositorioDeAjustes, NpgsqlRepositorioDeAjustes>();
        servicios.AddScoped<IRepositorioDeIngresos, NpgsqlRepositorioDeIngresos>();
        servicios.AddScoped<IRepositorioDeOperacionesIdempotentes, NpgsqlRepositorioDeOperacionesIdempotentes>();
        servicios.AddSingleton<IDepuradorDeAjustes>(new PostgresDepuradorDeAjustes(opciones.Depuracion));

        // El adaptador real reemplaza al nulo de Fuentes, venga antes o después en el registro.
        servicios.Replace(ServiceDescriptor.Scoped<IReingresosPorDevolucion, ConsultarReingresosPorDevolucion>());

        servicios.AddScoped(sp => new AutorizadorDeAjustes(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.AddScoped(sp => new PresupuestoDelPersonal(sp.GetRequiredService<IControlDeLimites>(), sp.GetRequiredService<IReloj>()));

        servicios.AddSingleton<IValidador<RegistrarAjusteManualEntrada>, ValidadorDeRegistrarAjusteManual>();
        servicios.AddSingleton<IValidador<RegistrarIngresoDeMercaderiaEntrada>, ValidadorDeRegistrarIngreso>();
        servicios.AddSingleton<IValidador<ConsultarIngresosEntrada>, ValidadorDeConsultarIngresos>();
        servicios.AddSingleton<IValidador<ReingresarPorDevolucionEntrada>, ValidadorDeReingresarPorDevolucion>();
        servicios.AddSingleton<IValidador<ConsultarHistorialDeAjustesEntrada>, ValidadorDeConsultarHistorialDeAjustes>();

        servicios.Escritura<RegistrarAjusteManualEntrada, AjusteRegistrado, RegistrarAjusteManual>(opcionesDeReintento);
        servicios.Escritura<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado, RegistrarIngresoDeMercaderia>(opcionesDeReintento);
        servicios.Escritura<ReingresarPorDevolucionEntrada, ReingresoRegistrado, ReingresarPorDevolucion>(opcionesDeReintento);
        servicios.Escritura<DepurarAjustesEntrada, ResultadoDeDepuracionDeAjustes, DepurarAjustes>(opcionesDeReintento);

        // Las consultas son de solo lectura: una instantánea coherente (REPEATABLE READ), nunca una transacción de escritura.
        servicios.Lectura<ConsultarIngresosEntrada, IngresosConsultados, ConsultarIngresos>();
        servicios.Lectura<ConsultarHistorialDeAjustesEntrada, PaginaDeAjustes, ConsultarHistorialDeAjustes>();

        return servicios;
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

/// <summary>El trabajo de fondo de <c>Ajustes</c>: la depuración diaria (AJU-21), con el mismo mecanismo que los de <c>Reservas</c> (actor «sistema» explícito, bloqueo de instancia única, registro y alerta a las 24 horas).</summary>
public static class TrabajosDeAjustes
{
    public const string DepuracionDiaria = "ajustes-depuracion-diaria";

    public static readonly TimeSpan IntervaloDeLaDepuracion = TimeSpan.FromHours(24);

    public static TrabajoDeFondoDeReservas Depurar(IServiceProvider sp, TimeProvider? tiempo = null) =>
        new(
            DepuracionDiaria,
            IntervaloDeLaDepuracion,
            async (fabrica, cancelacion) =>
            {
                using var alcance = fabrica.CreateScope();
                var resultado = await alcance.ServiceProvider.GetRequiredService<ICasoDeUso<DepurarAjustesEntrada, ResultadoDeDepuracionDeAjustes>>().Ejecutar(new DepurarAjustesEntrada(), cancelacion).ConfigureAwait(false);
                if (!resultado.EsExito)
                {
                    // Sin el actor «sistema» (no autenticado o prohibido) la corrida falla a la vista: nunca pasa por «nada que depurar».
                    throw resultado.Error!.Categoria is CategoriaDeError.NoAutenticado or CategoriaDeError.Prohibido
                        ? new ActorDeTrabajoDeFondoAusenteException()
                        : new InvalidOperationException($"La depuración de ajustes falló ({resultado.Error.Clave}).");
                }
            },
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<EjecutorDeTrabajoDeFondo>(),
            sp.GetRequiredService<BloqueoDeInstanciaUnica>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            sp.GetRequiredService<IReloj>(),
            tiempo);
}

/// <summary>Registro del trabajo de depuración de <c>Ajustes</c>. Solo en el modo normal (PLA-19).</summary>
public static class ComposicionDeTrabajosDeAjustes
{
    public static IServiceCollection AgregarTrabajosDeAjustes(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (!soloEnModoNormal)
        {
            return servicios;
        }

        servicios.AgregarEjecutorDeTrabajoDeFondo();
        servicios.TryAddSingleton(sp => new BloqueoDeInstanciaUnica(sp.GetRequiredService<NpgsqlDataSource>()));
        servicios.AddHostedService(sp => new ServicioDeTrabajosDeReservas([TrabajosDeAjustes.Depurar(sp)]));
        return servicios;
    }
}
