using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Traslados;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// El registro de dependencias del módulo <c>Traslados</c> (T13). Vive fuera de la carpeta del módulo porque la Infraestructura de un módulo no puede depender de la de <c>Plataforma</c> (regla 13). Implementa los dos puertos que declara
/// <c>Reservas</c>: <see cref="IGeneradorDeTrasladosDePedido"/> <b>reemplaza</b> al adaptador que falla a la vista y <see cref="IAlLiberarPedido"/> se suma a los receptores. El anfitrión pone, además, lo de <c>Fuentes</c> y de
/// <c>Reservas</c> (unidad de trabajo, directorio, <c>IMovimientosDeStock</c>, <c>IReservasParaTraslados</c>…), <c>IControlDeLimites</c>, <c>IAuditoria</c> e <c>IDenegacionDeAcceso</c>. Los dos puertos <b>no</b> se registran como
/// casos de uso expuestos: ninguna <c>ICasoDeUso</c> los alcanza.
/// </summary>
public static class ComposicionDeTraslados
{
    /// <summary>
    /// Los casos de uso del módulo que se pueden repetir entero ante un interbloqueo: las transiciones sobre un traslado existente (la transacción anterior se revirtió completa y el evento de acceso denegado es uno por solicitud).
    /// La solicitud <b>no</b>: consume el presupuesto del personal en una transacción independiente y repetirla lo contaría dos veces.
    /// </summary>
    public static IReadOnlyList<Type> CasosDeUsoIdempotentes { get; } =
        [typeof(AprobarTraslado), typeof(RechazarTraslado), typeof(CancelarTraslado), typeof(DespacharTraslado), typeof(RecibirTraslado), typeof(RegistrarGuiaDeTraslado)];

    public static IServiceCollection AgregarModuloTraslados(this IServiceCollection servicios, OpcionesDeReintentoPorInterbloqueo? reintento = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var opcionesDeReintento = reintento ?? new OpcionesDeReintentoPorInterbloqueo();

        servicios.AddScoped<AccesoATraslados>();
        servicios.AddScoped<IRepositorioDeTraslados, NpgsqlRepositorioDeTraslados>();
        servicios.AddScoped<IBuzonDeSalida, NpgsqlBuzonDeSalida>();

        servicios.AddScoped(sp => new AutorizadorDeTraslados(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.AddScoped(sp => new PresupuestoDeTraslados(sp.GetRequiredService<IControlDeLimites>(), sp.GetRequiredService<IReloj>()));
        servicios.AddScoped<EjecutorDeTransiciones>();

        servicios.AddSingleton<IValidador<SolicitarTrasladoEntrada>, ValidadorDeSolicitarTraslado>();
        servicios.AddSingleton<IValidador<ConsultarOrigenesPosiblesEntrada>, ValidadorDeConsultarOrigenesPosibles>();
        servicios.AddSingleton<IValidador<AprobarTrasladoEntrada>>(new ValidadorDeTrasladoPorId<AprobarTrasladoEntrada>(e => e.TrasladoId));
        servicios.AddSingleton<IValidador<DespacharTrasladoEntrada>>(new ValidadorDeTrasladoPorId<DespacharTrasladoEntrada>(e => e.TrasladoId));
        servicios.AddSingleton<IValidador<RecibirTrasladoEntrada>>(new ValidadorDeTrasladoPorId<RecibirTrasladoEntrada>(e => e.TrasladoId));
        servicios.AddSingleton<IValidador<RechazarTrasladoEntrada>, ValidadorDeRechazarTraslado>();
        servicios.AddSingleton<IValidador<CancelarTrasladoEntrada>, ValidadorDeCancelarTraslado>();
        servicios.AddSingleton<IValidador<RegistrarGuiaDeTrasladoEntrada>, ValidadorDeRegistrarGuiaDeTraslado>();

        servicios.Escritura<SolicitarTrasladoEntrada, TrasladoRegistrado, SolicitarTraslado>(opcionesDeReintento);
        servicios.Escritura<AprobarTrasladoEntrada, TrasladoRegistrado, AprobarTraslado>(opcionesDeReintento);
        servicios.Escritura<RechazarTrasladoEntrada, TrasladoRegistrado, RechazarTraslado>(opcionesDeReintento);
        servicios.Escritura<CancelarTrasladoEntrada, TrasladoRegistrado, CancelarTraslado>(opcionesDeReintento);
        servicios.Escritura<DespacharTrasladoEntrada, TrasladoRegistrado, DespacharTraslado>(opcionesDeReintento);
        servicios.Escritura<RecibirTrasladoEntrada, TrasladoRegistrado, RecibirTraslado>(opcionesDeReintento);
        servicios.Escritura<RegistrarGuiaDeTrasladoEntrada, TrasladoRegistrado, RegistrarGuiaDeTraslado>(opcionesDeReintento);

        // Una consulta es de solo lectura: una instantánea coherente (REPEATABLE READ), nunca una transacción de escritura.
        servicios.Lectura<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles, ConsultarOrigenesPosibles>();

        // Los puertos de Reservas: el adaptador real reemplaza al que falla a la vista, y el receptor de anulaciones se suma a los demás. No son casos de uso expuestos.
        servicios.Replace(ServiceDescriptor.Scoped<IGeneradorDeTrasladosDePedido, GenerarTrasladosDePedido>());
        servicios.AddScoped<IAlLiberarPedido, CancelarTrasladosDePedido>();

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
