using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// La composición de Devoluciones y sus trabajos de fondo (T22; DEV-14, DEV-21, DEV-27; AC-DEV-13, AC-DEV-29, AC-DEV-32): resuelve lo que declara, suma su parte de los pendientes y de la cancelación de la cuenta sin reemplazar a nadie, la guarda de producción
/// nombra los dobles provisionales, la hora diaria de Lima es la de las 03:45 y el ejecutor registra, alerta y no deja caer el servicio.
/// </summary>
public sealed class ComposicionYTrabajosDeDevolucionesTests
{
    public class DobleQueLanza : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) => throw new NotSupportedException();
    }

    private static T Doble<T>()
        where T : class => DispatchProxy.Create<T, DobleQueLanza>();

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            Corridas.Add((trabajo, resultado));
            return Task.CompletedTask;
        }
    }

    private sealed class AlertasEspia : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Publicadas { get; } = [];

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private static ServiceCollection ConDependencias(bool conInventario = false, bool conAccesos = false)
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(Doble<IUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IActorActual>());
        servicios.AddSingleton(Doble<IDenegacionDeAcceso>());
        servicios.AddSingleton<IReloj>(new RelojFijo(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)));
        servicios.AddSingleton(Doble<IControlDeLimites>());
        servicios.AddSingleton(Doble<IAuditoria>());
        servicios.AddSingleton(Doble<IPublicadorDeAlertasTecnicas>());
        servicios.AddSingleton(Doble<INotificador>());
        servicios.AddSingleton(Doble<IEstadoDeCuenta>());
        servicios.AddSingleton(Doble<IMarcaDeCancelacionDeCuenta>());
        servicios.AddSingleton(Doble<IReembolsos>());
        servicios.AddSingleton(Doble<IAccesosDeBiblioteca>());
        servicios.AddSingleton(Doble<IPedidosParaDevoluciones>());
        servicios.AddSingleton(Doble<IRegistroDeTrabajosDeFondo>());
        if (conInventario)
        {
            servicios.AddSingleton(Doble<IClienteDelInventario>());
        }

        if (conAccesos)
        {
            servicios.AddSingleton(Doble<IAccesosParaDevoluciones>());
        }

        return servicios;
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public void La_composicion_resuelve_los_contratos_publicos_los_casos_de_uso_y_el_repositorio_de_PostgreSQL()
    {
        var servicios = ConDependencias(conInventario: true, conAccesos: true);
        servicios.AgregarDevoluciones(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.NotNull(s.GetRequiredService<IDevolucionesDelCliente>());
        Assert.NotNull(s.GetRequiredService<IGestionDeDevoluciones>());
        Assert.IsType<RepositorioDeDevolucionesPostgres>(s.GetRequiredService<IRepositorioDeDevoluciones>());
        Assert.IsType<RestInventarioDeDevoluciones>(s.GetRequiredService<IInventarioDeDevoluciones>());
        foreach (var caso in new[]
        {
            typeof(SolicitarDevolucionDeImpreso), typeof(SolicitarReembolsoDeDigital), typeof(ReportarArchivoDefectuoso), typeof(DecidirReporte), typeof(AprobarYRechazarDevolucion), typeof(ConfirmarRecepcion),
            typeof(ConciliarReembolsosDeDevoluciones), typeof(RechazarDevolucionesVencidas), typeof(RevisarDevolucionesEstancadas), typeof(RedactarDevolucionesVencidas), typeof(RevisarDepuracionDeDevoluciones), typeof(ConsultasDeDevoluciones),
        })
        {
            Assert.NotNull(s.GetRequiredService(caso));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public void Devoluciones_suma_su_parte_de_los_pendientes_y_de_la_cancelacion_sin_reemplazar_las_de_otros_modulos()
    {
        var servicios = ConDependencias(conInventario: true, conAccesos: true);
        servicios.AddScoped<IPendientesDeCuenta>(_ => Doble<IPendientesDeCuenta>());
        servicios.AddScoped<IAplicadorDeCancelacion>(_ => Doble<IAplicadorDeCancelacion>());
        servicios.AgregarDevoluciones(esProduccion: false);
        servicios.AgregarDevoluciones(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();

        var pendientes = alcance.ServiceProvider.GetServices<IPendientesDeCuenta>().ToList();
        var aplicadores = alcance.ServiceProvider.GetServices<IAplicadorDeCancelacion>().ToList();

        Assert.Equal(2, pendientes.Count);
        Assert.Equal(2, aplicadores.Count);
        Assert.Single(pendientes, p => p is ContarPendientesDeCuenta);
        Assert.Single(aplicadores, a => a is RedactarDevolucionesDeUnaCuenta);
        Assert.Contains(aplicadores, a => a is RedactarDevolucionesDeUnaCuenta { Modulo: "devoluciones" });
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public void En_produccion_la_guarda_nombra_los_dobles_provisionales_y_sin_ellos_el_arranque_continua()
    {
        var sinNada = ConDependencias();
        var conTodo = ConDependencias(conInventario: true, conAccesos: true);

        var falla = Assert.Throws<ConfiguracionDeDevolucionesInvalidaException>(() => sinNada.AgregarDevoluciones(esProduccion: true));
        conTodo.AgregarDevoluciones(esProduccion: true);

        Assert.Contains(nameof(IInventarioDeDevoluciones), falla.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IAccesosParaDevoluciones), falla.Message, StringComparison.Ordinal);
        using var proveedor = conTodo.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();
        Assert.IsType<RestInventarioDeDevoluciones>(alcance.ServiceProvider.GetRequiredService<IInventarioDeDevoluciones>());
        Assert.IsType<ResolucionDeSucursalDeDevoluciones>(alcance.ServiceProvider.GetRequiredService<BabelHub.Ecommerce.Application.Devoluciones.Interno.IResolucionDeSucursal>());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Sin_el_cliente_del_inventario_el_doble_provisional_y_la_resolucion_fallan_cerrado()
    {
        var servicios = ConDependencias(conAccesos: true);
        servicios.AgregarDevoluciones(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        var reingreso = await s.GetRequiredService<IInventarioDeDevoluciones>().Reingresar(new ReingresoPorDevolucion(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [new LineaDeReingreso(Guid.NewGuid(), 1)]));
        var sucursales = await s.GetRequiredService<IInventarioDeDevoluciones>().ListarSucursalesActivas();
        var codigo = await s.GetRequiredService<BabelHub.Ecommerce.Application.Devoluciones.Interno.IResolucionDeSucursal>().CodigoDe(Guid.NewGuid());

        Assert.IsType<ResultadoDelReingreso.Incierto>(reingreso);
        Assert.IsType<ResultadoDeSucursalesActivas.NoDisponible>(sucursales);
        Assert.Null(codigo);
    }

    // ---------- Los trabajos ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-29")]
    [InlineData(7, 0, 1, 45)]
    [InlineData(8, 44, 0, 1)]
    [InlineData(8, 45, 24, 0)]
    [InlineData(9, 0, 23, 45)]
    public void La_depuracion_corre_a_las_0345_de_Lima_y_si_ya_paso_la_hora_a_la_de_manana(int horaUtc, int minuto, int horasEsperadas, int minutosEsperados)
    {
        // 03:45 de Lima son las 08:45 UTC.
        var ahora = new DateTimeOffset(2026, 11, 20, horaUtc, minuto, 0, TimeSpan.Zero);

        var espera = TrabajoDeFondoDeDevoluciones.EsperaHasta(new TimeOnly(3, 45), ahora);

        Assert.Equal(new TimeSpan(horasEsperadas, minutosEsperados, 0), espera);
        Assert.Equal(new TimeOnly(3, 45), TrabajosDeDevoluciones.DiariaALas0345DeLima.HoraEnLima);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public void Los_tres_trabajos_tienen_sus_nombres_estables_y_sus_horarios()
    {
        var trabajos = TrabajosDeDevoluciones.Todos(Doble<IServiceScopeFactory>(), new RegistroEspia(), (c, t) => c(t), new AlertasEspia(), new RelojFijo(DateTimeOffset.UtcNow));

        Assert.Equal(["devoluciones-revision-de-plazos", "devoluciones-conciliacion-de-reembolsos", "devoluciones-depuracion"], trabajos.Select(t => t.Nombre));
        Assert.Equal(TimeSpan.FromHours(1), TrabajosDeDevoluciones.PorHora.Intervalo);
        Assert.Equal(TimeSpan.FromMinutes(5), TrabajosDeDevoluciones.CadaCincoMinutos.Intervalo);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Cada_corrida_corre_como_sistema_se_registra_y_tras_tres_fallos_seguidos_alerta_sin_dejar_caer_el_servicio()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();
        var alcances = 0;
        var falla = false;
        var trabajo = new TrabajoDeFondoDeDevoluciones(
            "devoluciones-prueba",
            _ => falla ? throw new InvalidOperationException("no debe salir") : Task.CompletedTask,
            registro,
            async (corrida, token) =>
            {
                alcances++;
                await corrida(token);
            },
            alertas,
            new RelojFijo(DateTimeOffset.UtcNow),
            new OpcionesDelTrabajoDeDevoluciones(TimeSpan.FromMinutes(1)));

        await trabajo.EjecutarUnaVez();
        falla = true;
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Empty(alertas.Publicadas);
        await trabajo.EjecutarUnaVez();

        Assert.Equal(4, alcances);
        Assert.Equal([ResultadoDeTrabajo.Exito, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo], registro.Corridas.Select(c => c.Resultado));
        Assert.Equal("trabajo-de-fondo-detenido", Assert.Single(alertas.Publicadas).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Un_caso_de_uso_que_rechaza_porque_el_actor_no_es_sistema_es_una_corrida_fallida_y_la_cancelacion_se_propaga_sin_registrar()
    {
        var registro = new RegistroEspia();
        var trabajo = new TrabajoDeFondoDeDevoluciones(
            "devoluciones-prueba",
            _ => throw new InvalidOperationException("rechazado"),
            registro,
            (corrida, token) => corrida(token),
            new AlertasEspia(),
            new RelojFijo(DateTimeOffset.UtcNow),
            new OpcionesDelTrabajoDeDevoluciones(TimeSpan.FromMinutes(1)));
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        await trabajo.EjecutarUnaVez();
        var apagado = await Record.ExceptionAsync(() => trabajo.Correr(cancelada.Token));

        Assert.Equal(ResultadoDeTrabajo.Fallo, Assert.Single(registro.Corridas).Resultado);
        Assert.Null(apagado);
    }
}
