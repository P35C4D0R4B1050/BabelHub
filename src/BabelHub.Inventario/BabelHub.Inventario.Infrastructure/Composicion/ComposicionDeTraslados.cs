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
using Microsoft.Extensions.Hosting;
using Npgsql;

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
        [typeof(AprobarTraslado), typeof(RechazarTraslado), typeof(CancelarTraslado), typeof(DespacharTraslado), typeof(RecibirTraslado), typeof(RegistrarGuiaDeTraslado), typeof(RevisarTrasladosSinAprobar), typeof(DepurarTraslados)];

    public static IServiceCollection AgregarModuloTraslados(this IServiceCollection servicios, OpcionesDeReintentoPorInterbloqueo? reintento = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var opcionesDeReintento = reintento ?? new OpcionesDeReintentoPorInterbloqueo();

        servicios.AddScoped<AccesoATraslados>();
        servicios.AddScoped<IRepositorioDeTraslados, NpgsqlRepositorioDeTraslados>();
        servicios.AddScoped<IBuzonDeSalida, NpgsqlBuzonDeSalida>();
        servicios.AddScoped<IColaDeMensajesSalientes, NpgsqlColaDeMensajesSalientes>();

        // Sin la salida real configurada, un mensaje queda pendiente (nunca se pierde) hasta que se agregue con AgregarSalidaAlEcommerce.
        servicios.TryAddScoped<IEnvioAlEcommerce, EnvioAlEcommerceNoConfigurado>();

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
        servicios.AddSingleton<IValidador<ConsultarTrasladoEntrada>>(new ValidadorDeTrasladoPorId<ConsultarTrasladoEntrada>(e => e.TrasladoId));
        servicios.AddSingleton<IValidador<ListarTrasladosEntrada>, ValidadorDeListarTraslados>();
        servicios.AddSingleton<IValidador<ConsultarTrasladosDeUnPedidoEntrada>, ValidadorDeConsultarTrasladosDeUnPedido>();

        servicios.Escritura<SolicitarTrasladoEntrada, TrasladoRegistrado, SolicitarTraslado>(opcionesDeReintento);
        servicios.Escritura<AprobarTrasladoEntrada, TrasladoRegistrado, AprobarTraslado>(opcionesDeReintento);
        servicios.Escritura<RechazarTrasladoEntrada, TrasladoRegistrado, RechazarTraslado>(opcionesDeReintento);
        servicios.Escritura<CancelarTrasladoEntrada, TrasladoRegistrado, CancelarTraslado>(opcionesDeReintento);
        servicios.Escritura<DespacharTrasladoEntrada, TrasladoRegistrado, DespacharTraslado>(opcionesDeReintento);
        servicios.Escritura<RecibirTrasladoEntrada, TrasladoRegistrado, RecibirTraslado>(opcionesDeReintento);
        servicios.Escritura<RegistrarGuiaDeTrasladoEntrada, TrasladoRegistrado, RegistrarGuiaDeTraslado>(opcionesDeReintento);

        // Una consulta es de solo lectura: una instantánea coherente (REPEATABLE READ), nunca una transacción de escritura.
        servicios.Lectura<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles, ConsultarOrigenesPosibles>();
        servicios.Lectura<ConsultarTrasladoEntrada, TrasladoRegistrado, ConsultarTraslado>();
        servicios.Lectura<ListarTrasladosEntrada, PaginaDeTraslados, ListarTraslados>();
        servicios.Lectura<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido, ConsultarTrasladosDeUnPedido>();

        // Los casos de uso del sistema: la entrega del buzón maneja sus propias transacciones cortas (reclamar y registrar) y envía fuera de toda transacción, así que no lleva el decorador; la revisión de plazos y la depuración sí.
        servicios.AddScoped<EntregarMensajesSalientes>();
        servicios.AddScoped<ICasoDeUso<EntregarMensajesSalientesEntrada, ResultadoDeLaEntrega>>(sp => sp.GetRequiredService<EntregarMensajesSalientes>());
        servicios.Escritura<RevisarTrasladosSinAprobarEntrada, ResultadoDeLaRevision, RevisarTrasladosSinAprobar>(opcionesDeReintento);
        servicios.Escritura<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados, DepurarTraslados>(opcionesDeReintento);

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

/// <summary>La salida al E-commerce mientras no está configurada: <b>nunca pierde un mensaje</b> (lo deja pendiente y reintenta) y a las 24 horas la entrega alerta; el anfitrión de producción registra el adaptador real.</summary>
internal sealed class EnvioAlEcommerceNoConfigurado : IEnvioAlEcommerce
{
    public Task<ResultadoDeEnvio> EnviarAviso(AvisoDeTrasladoParaEnviar aviso, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeEnvio.Reintentable);

    public Task<ResultadoDeEnvio> InformarEstadoDelPedido(EstadoDeTrasladoDePedidoParaEnviar estado, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeEnvio.Reintentable);
}

/// <summary>Registro de la salida real al E-commerce (T19): el cliente de servicio con la credencial <c>solicitar-aviso</c>, el plazo de 10 segundos y la resiliencia. El anfitrión pone, además, <see cref="OpcionesDeCredencialesDeSalida"/>.</summary>
public static class ComposicionDeLaSalidaAlEcommerce
{
    /// <param name="servicios">El registro de dependencias.</param>
    /// <param name="direccion">La dirección del E-commerce: <b>siempre HTTPS</b>, porque la credencial viaja en una cabecera (docs/05/03, plan §6).</param>
    /// <param name="opciones">Plazos y resiliencia del cliente.</param>
    /// <param name="permitirHttp">Solo para pruebas con un servidor en el bucle local: admite <c>http</c>. Nunca en producción.</param>
    public static IServiceCollection AgregarSalidaAlEcommerce(this IServiceCollection servicios, Uri direccion, OpcionesDelClienteDeServicio? opciones = null, bool permitirHttp = false)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(direccion);
        if (!direccion.IsAbsoluteUri || (direccion.Scheme != Uri.UriSchemeHttps && !(permitirHttp && direccion.Scheme == Uri.UriSchemeHttp)))
        {
            throw new ArgumentException("La salida al E-commerce va por HTTPS: la credencial de servicio viaja en una cabecera.", nameof(direccion));
        }

        // Sin seguir redirecciones: .NET solo retira «Authorization» al redirigir, así que la cabecera de la credencial de servicio llegaría al destino del «Location». Un 3xx es un error permanente.
        servicios.AgregarClienteDeServicio(HttpEnvioAlEcommerce.NombreDelCliente, direccion, opciones)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddTypedClient<IEnvioAlEcommerce, HttpEnvioAlEcommerce>();
        return servicios;
    }
}

/// <summary>Los trabajos de fondo de <c>Traslados</c> con sus nombres estables e intervalos (plan §5): la entrega del buzón cada 30 segundos, la revisión de plazos cada hora y la depuración diaria.</summary>
public static class TrabajosDeTraslados
{
    public const string EntregaDeMensajes = "traslados-entrega-de-mensajes";

    public const string RevisionDePlazos = "traslados-revision-de-plazos";

    public const string DepuracionDiaria = "traslados-depuracion-diaria";

    public static readonly TimeSpan IntervaloDeLaEntrega = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan IntervaloDeLaRevision = TimeSpan.FromHours(1);

    public static readonly TimeSpan IntervaloDeLaDepuracion = TimeSpan.FromHours(24);

    public static TrabajoDeFondoDeReservas Entregar(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, EntregaDeMensajes, IntervaloDeLaEntrega, tiempo, proveedor => proveedor.GetRequiredService<ICasoDeUso<EntregarMensajesSalientesEntrada, ResultadoDeLaEntrega>>().Ejecutar(new EntregarMensajesSalientesEntrada(), default));

    public static TrabajoDeFondoDeReservas Revisar(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, RevisionDePlazos, IntervaloDeLaRevision, tiempo, proveedor => proveedor.GetRequiredService<ICasoDeUso<RevisarTrasladosSinAprobarEntrada, ResultadoDeLaRevision>>().Ejecutar(new RevisarTrasladosSinAprobarEntrada(), default));

    public static TrabajoDeFondoDeReservas Depurar(IServiceProvider sp, TimeProvider? tiempo = null) =>
        Crear(sp, DepuracionDiaria, IntervaloDeLaDepuracion, tiempo, proveedor => proveedor.GetRequiredService<ICasoDeUso<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>>().Ejecutar(new DepurarTrasladosEntrada(), default));

    private static TrabajoDeFondoDeReservas Crear<T>(IServiceProvider sp, string nombre, TimeSpan intervalo, TimeProvider? tiempo, Func<IServiceProvider, Task<Resultado<T>>> caso) =>
        new(
            nombre,
            intervalo,
            async (fabrica, cancelacion) =>
            {
                using var alcance = fabrica.CreateScope();
                var resultado = await caso(alcance.ServiceProvider).WaitAsync(cancelacion).ConfigureAwait(false);
                if (!resultado.EsExito)
                {
                    // Sin el actor «sistema» (no autenticado o prohibido) la corrida falla a la vista: nunca pasa por «nada que hacer».
                    throw resultado.Error!.Categoria is CategoriaDeError.NoAutenticado or CategoriaDeError.Prohibido
                        ? new ActorDeTrabajoDeFondoAusenteException()
                        : new InvalidOperationException($"El trabajo {nombre} falló ({resultado.Error.Clave}).");
                }
            },
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<EjecutorDeTrabajoDeFondo>(),
            sp.GetRequiredService<BloqueoDeInstanciaUnica>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            sp.GetRequiredService<IReloj>(),
            tiempo);
}

/// <summary>Registro de los trabajos de fondo de <c>Traslados</c> (entrega del buzón, revisión de plazos y depuración). Solo en el modo normal (PLA-19).</summary>
public static class ComposicionDeTrabajosDeTraslados
{
    public static IServiceCollection AgregarTrabajosDeTraslados(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (!soloEnModoNormal)
        {
            return servicios;
        }

        servicios.AgregarEjecutorDeTrabajoDeFondo();
        servicios.TryAddSingleton(sp => new BloqueoDeInstanciaUnica(sp.GetRequiredService<NpgsqlDataSource>()));
        servicios.AddSingleton<IHostedService>(sp => new ServicioDeTrabajosDeReservas([TrabajosDeTraslados.Entregar(sp), TrabajosDeTraslados.Revisar(sp), TrabajosDeTraslados.Depurar(sp)]));
        return servicios;
    }
}
