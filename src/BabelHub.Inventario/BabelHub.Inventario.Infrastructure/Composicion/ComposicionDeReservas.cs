using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Inventario.Infrastructure.Composicion;

/// <summary>
/// El registro de dependencias del módulo <c>Reservas</c>, que se completa con cada tarea. Vive fuera de la carpeta del módulo porque la Infraestructura de un módulo no puede depender de la de <c>Plataforma</c>
/// (regla 13). <see cref="AgregarModuloReservas"/> se llama <b>después o antes</b> de <c>AgregarModuloFuentes</c> (el orden no importa): el repositorio y <c>IReservasDeStock</c> reales <b>reemplazan</b> al adaptador nulo
/// que <c>Fuentes</c> registra hasta que <c>Reservas</c> exista (T09). El anfitrión pone, además de lo de <c>Fuentes</c>, lo que es de la plataforma y de otros módulos (<c>IAuditoria</c>, <c>IDenegacionDeAcceso</c>).
/// </summary>
public static class ComposicionDeReservas
{
    /// <summary>
    /// Los casos de uso del módulo que se pueden repetir entero ante un interbloqueo: la clave de idempotencia (reservar) o su naturaleza (expirar) los hace idempotentes, y un interbloqueo revierte la transacción
    /// completa antes de cualquier efecto (plan §5).
    /// </summary>
    public static IReadOnlyList<Type> CasosDeUsoIdempotentes { get; } = [typeof(ReservarUnidades), typeof(ExpirarReservasVencidas)];

    /// <summary>
    /// Registra el módulo: la estrategia de selección elegida por nombre (T06), el acceso y el repositorio de reservas, <c>IReservasDeStock</c> (que reemplaza al nulo de <c>Fuentes</c>), el ejecutor «sistema» y los
    /// casos de uso con sus decoradores: los de <b>escritura</b> con <c>TransaccionPorCasoDeUso</c> y, como son idempotentes, con <c>ReintentoPorInterbloqueo</c> por fuera de la transacción.
    /// </summary>
    public static IServiceCollection AgregarModuloReservas(
        this IServiceCollection servicios,
        IConfiguration configuracion,
        IEnumerable<IEstrategiaDeSeleccion>? alternativas = null,
        OpcionesDeReintentoPorInterbloqueo? reintento = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);
        var opcionesDeReintento = reintento ?? new OpcionesDeReintentoPorInterbloqueo();

        servicios.AgregarEstrategiasDeSeleccion(configuracion, alternativas);
        servicios.AgregarEjecutorComoSistema();

        servicios.AddScoped<AccesoAReservas>();
        servicios.AddScoped<IRepositorioDeReservas, NpgsqlRepositorioDeReservas>();

        // El adaptador real reemplaza al nulo de Fuentes, venga antes o después en el registro.
        servicios.Replace(ServiceDescriptor.Scoped<IReservasDeStock, ReservasDeStock>());

        servicios.AddScoped(sp => new AutorizadorDeReservas(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.AddScoped(sp => new SelectorDeFuentes(sp.GetRequiredService<IDirectorioDeFuentes>(), sp.GetRequiredService<IEstrategiaDeSeleccion>()));
        servicios.AddScoped<ConstructorDeLaReserva>();

        servicios.AddSingleton<IValidador<ReservarEntrada>, ValidadorDeReservar>();

        servicios.Escritura<ReservarEntrada, ReservaDelPedido, ReservarUnidades>(opcionesDeReintento);
        servicios.Escritura<ExpirarReservasEntrada, ExpiracionDelPedido, ExpirarReservasVencidas>(opcionesDeReintento);

        return servicios;
    }

    /// <summary>
    /// Registra <c>SeleccionPorCercania</c> y las <paramref name="alternativas"/> (por ejemplo, una de prueba) y la estrategia activa según <c>Inventario:EstrategiaDeSeleccion</c>. El nombre inexistente falla al
    /// resolver la estrategia, y <see cref="ExigirEstrategiaDeSeleccion"/> lo hace a propósito en el arranque (SEL-10).
    /// </summary>
    public static IServiceCollection AgregarEstrategiasDeSeleccion(this IServiceCollection servicios, IConfiguration configuracion, IEnumerable<IEstrategiaDeSeleccion>? alternativas = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);
        var registradas = new List<IEstrategiaDeSeleccion> { new SeleccionPorCercania() };
        registradas.AddRange(alternativas ?? []);

        servicios.AddSingleton(new RegistroDeEstrategiasDeSeleccion(registradas));
        servicios.AddSingleton(sp => sp.GetRequiredService<RegistroDeEstrategiasDeSeleccion>().Obtener(ConfiguracionDeLaSeleccion.Nombre(configuracion)));
        return servicios;
    }

    /// <summary>Resuelve la estrategia activa al arrancar: si el nombre configurado no existe, el arranque falla con <see cref="EstrategiaDeSeleccionDesconocidaException"/>.</summary>
    public static IEstrategiaDeSeleccion ExigirEstrategiaDeSeleccion(IServiceProvider proveedor)
    {
        ArgumentNullException.ThrowIfNull(proveedor);
        return proveedor.GetRequiredService<IEstrategiaDeSeleccion>();
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
}
