using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>La composición de Envíos y sus trabajos de fondo (T15; ENV-07, ENV-11): resuelve lo que declara, sustituye al despacho provisional de Pedidos sin importar el orden y los trabajos registran, alertan y fallan cerrado.</summary>
public sealed class ComposicionYTrabajosDeEnviosTests
{
    public class DobleQueLanza : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) => throw new NotSupportedException();
    }

    private static T Doble<T>()
        where T : class => DispatchProxy.Create<T, DobleQueLanza>();

    public static ServiceCollection ConDependenciasPublicas() => ConDependencias();

    private static ServiceCollection ConDependencias(bool sinOperador = false)
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(Doble<IUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IActorActual>());
        servicios.AddSingleton(Doble<IDenegacionDeAcceso>());
        servicios.AddSingleton(Doble<IReloj>());
        servicios.AddSingleton(Doble<IControlDeLimites>());
        servicios.AddSingleton(Doble<IAuditoria>());
        servicios.AddSingleton(Doble<IPublicadorDeAlertasTecnicas>());
        servicios.AddSingleton(Doble<INotificador>());
        servicios.AddSingleton(Doble<IRegistroDeTrabajosDeFondo>());
        if (!sinOperador)
        {
            servicios.AddSingleton(Doble<IOperadorLogistico>());
        }

        return servicios;
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public void La_composicion_resuelve_los_contratos_publicos_los_casos_de_uso_y_el_repositorio_de_PostgreSQL()
    {
        var servicios = ConDependencias();
        servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: false);
        servicios.AgregarEnvios(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.NotNull(s.GetRequiredService<IEnviosDeLogistica>());
        Assert.NotNull(s.GetRequiredService<IEnviosDelCliente>());
        Assert.IsType<RepositorioDeEnviosPostgres>(s.GetRequiredService<IRepositorioDeEnvios>());
        foreach (var caso in new[] { typeof(CrearEnvio), typeof(CancelarEnviosDelPedido), typeof(SolicitarGuia), typeof(PasarAEnReparto), typeof(MarcarEntregado), typeof(ListarEnvios), typeof(ObtenerEnvio), typeof(ObtenerEnviosDeUnPedido), typeof(InformarProgresoAPedidos), typeof(RevisarEnviosEstancados) })
        {
            Assert.NotNull(s.GetRequiredService(caso));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public void Envios_sustituye_al_despacho_provisional_de_Pedidos_se_componga_antes_o_despues()
    {
        var pedidosPrimero = ConDependencias();
        pedidosPrimero.AgregarDoblesProvisionalesDePedidos(esProduccion: false);
        pedidosPrimero.AgregarEnvios(esProduccion: false);
        var enviosPrimero = ConDependencias();
        enviosPrimero.AgregarEnvios(esProduccion: false);
        enviosPrimero.AgregarDoblesProvisionalesDePedidos(esProduccion: false);

        foreach (var servicios in new[] { pedidosPrimero, enviosPrimero })
        {
            using var proveedor = servicios.BuildServiceProvider();
            using var alcance = proveedor.CreateScope();
            Assert.IsType<DespachoDeEnviosDeLaAplicacion>(alcance.ServiceProvider.GetRequiredService<IDespachoDeEnvios>());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void Envios_no_registra_ningun_aplicador_de_cancelacion_de_cuenta_porque_no_guarda_datos_personales()
    {
        var servicios = ConDependencias();
        servicios.AgregarEnvios(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();

        Assert.Empty(alcance.ServiceProvider.GetServices<IAplicadorDeCancelacion>());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-04")]
    public void Sin_operador_elegido_se_resuelve_el_nulo_y_la_eleccion_del_anfitrion_gana_se_componga_antes_o_despues()
    {
        var sinEleccion = ConDependencias(sinOperador: true);
        sinEleccion.AgregarEnvios(esProduccion: false);
        var antes = ConDependencias(sinOperador: true);
        antes.AgregarOperadorLogisticoDeEnvios(new OpcionesDeArranqueDeEnvios("simulador", "Test", false));
        antes.AgregarEnvios(esProduccion: false);
        var despues = ConDependencias(sinOperador: true);
        despues.AgregarEnvios(esProduccion: false);
        despues.AgregarOperadorLogisticoDeEnvios(new OpcionesDeArranqueDeEnvios("simulador", "Test", false));

        using var a = sinEleccion.BuildServiceProvider();
        using var b = antes.BuildServiceProvider();
        using var c = despues.BuildServiceProvider();

        Assert.IsType<NuloOperadorLogistico>(a.GetRequiredService<IOperadorLogistico>());
        Assert.IsType<SimuladorOperadorLogistico>(b.GetRequiredService<IOperadorLogistico>());
        Assert.IsType<SimuladorOperadorLogistico>(c.GetRequiredService<IOperadorLogistico>());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public void En_produccion_el_arranque_falla_mientras_el_cierre_de_reservas_siga_provisional()
    {
        var servicios = ConDependencias();

        var error = Assert.Throws<ConfiguracionDeEnviosInvalidaException>(() => servicios.AgregarEnvios(esProduccion: true));

        Assert.Contains("reservas", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public async Task El_cierre_de_reservas_provisional_falla_cerrado_con_un_resultado_incierto()
    {
        var servicios = ConDependencias();
        servicios.AgregarEnvios(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();

        var resultado = await alcance.ServiceProvider.GetRequiredService<IReservasDelEnvio>().Cerrar(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]);

        Assert.IsType<ResultadoDeCierreDeReservas.Incierto>(resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public void Los_dos_trabajos_tienen_nombres_e_intervalos_estables()
    {
        var servicios = ConDependencias();
        servicios.AgregarEnvios(esProduccion: false);
        servicios.AgregarTrabajosDeFondoDeEnvios(_ => (corrida, ct) => corrida(ct));
        using var proveedor = servicios.BuildServiceProvider();

        var trabajos = proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeEnvios>>();

        Assert.Equal(["envios-reintento-de-progreso", "envios-revision-de-estancados"], trabajos.Select(t => t.Nombre));
        Assert.Equal(TimeSpan.FromMinutes(1), TrabajosDeEnvios.CadaMinuto.Intervalo);
        Assert.Equal(TimeSpan.FromHours(1), TrabajosDeEnvios.PorHora.Intervalo);
        Assert.NotNull(proveedor.GetRequiredService<ServicioDeTrabajosDeEnvios>());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Una_excepcion_con_el_apagado_ya_pedido_no_se_propaga_desde_el_servicio_de_trabajos()
    {
        using var apagado = new CancellationTokenSource();
        var trabajo = Trabajo(async _ =>
        {
            await apagado.CancelAsync();
            throw new InvalidOperationException("conexion cerrada");
        }, new RegistroEspia(), new AlertasEspia());

        await trabajo.Correr(apagado.Token);
    }

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

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora { get; } = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));
    }

    private static TrabajoDeFondoDeEnvios Trabajo(Func<CancellationToken, Task> corrida, RegistroEspia registro, AlertasEspia alertas, int fallosParaAlertar = 3) =>
        new(
            "envios-prueba",
            corrida,
            registro,
            (c, ct) => c(ct),
            alertas,
            new RelojFijo(),
            new OpcionesDelTrabajoDeEnvios(TimeSpan.FromMinutes(1), fallosParaAlertar));

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Una_corrida_exitosa_se_registra_y_no_alerta()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();

        await Trabajo(_ => Task.CompletedTask, registro, alertas).EjecutarUnaVez();

        Assert.Equal([("envios-prueba", ResultadoDeTrabajo.Exito)], registro.Corridas);
        Assert.Empty(alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Tras_tres_fallos_seguidos_se_publica_la_alerta_sin_datos_y_el_trabajo_no_cae()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();
        var trabajo = Trabajo(_ => throw new InvalidOperationException("detalle interno"), registro, alertas);

        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Empty(alertas.Publicadas);
        await trabajo.EjecutarUnaVez();

        Assert.All(registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));
        var alerta = Assert.Single(alertas.Publicadas);
        Assert.Equal("trabajo-de-fondo-detenido", alerta.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Un_exito_reinicia_la_cuenta_de_fallos_seguidos()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();
        var falla = true;
        var trabajo = Trabajo(_ => falla ? throw new InvalidOperationException() : Task.CompletedTask, registro, alertas);

        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        falla = false;
        await trabajo.EjecutarUnaVez();
        falla = true;
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();

        Assert.Empty(alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task La_cancelacion_del_apagado_se_propaga_y_un_registro_o_una_alerta_que_fallan_no_tumban_el_trabajo()
    {
        var alertas = new AlertasEspia();
        var trabajo = new TrabajoDeFondoDeEnvios(
            "envios-prueba",
            _ => throw new InvalidOperationException(),
            Doble<IRegistroDeTrabajosDeFondo>(),
            (c, ct) => c(ct),
            Doble<IPublicadorDeAlertasTecnicas>(),
            new RelojFijo(),
            new OpcionesDelTrabajoDeEnvios(TimeSpan.FromMinutes(1), 1));

        await trabajo.EjecutarUnaVez();

        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Trabajo(ct => Task.FromCanceled(ct), new RegistroEspia(), alertas).EjecutarUnaVez(cancelado.Token));
    }
}
