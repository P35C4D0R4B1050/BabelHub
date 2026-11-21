using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T14: la composición del Libro de Reclamaciones y sus trabajos de fondo sin base de datos (AC-REC-15, REC-17): los contratos públicos y los casos de uso se resuelven sin dobles provisionales, el correo verificado sale del contrato de Identidad con falla cerrada, la anonimización diaria
/// espera hasta las 04:00 de Lima, corre al arrancar y registra cada corrida, una corrida que falla o es rechazada queda como fallo sin dejar caer al servicio, y el servicio hospedado recibe los dos trabajos.
/// </summary>
public sealed class ComposicionYTrabajosDeReclamacionesTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public class ProxyQueNoHaceNada : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException("Un doble de composición no se llama.");
    }

    private static T Falso<T>()
        where T : class => DispatchProxy.Create<T, ProxyQueNoHaceNada>();

    /// <summary>Una cuenta de Identidad que solo responde <c>Consultar</c>.</summary>
    public class ProxyDeCorreo : DispatchProxy
    {
        public Func<Guid, Task<string?>> Consultar { get; set; } = _ => throw new NotSupportedException();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(ICorreoVerificadoDeSistema.Consultar) ? Consultar((Guid)args![0]!) : throw new NotSupportedException();
    }

    private static ServiceCollection ConExternos()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(Falso<IUnidadDeTrabajo>());
        servicios.AddSingleton(Falso<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Falso<IActorActual>());
        servicios.AddSingleton(Falso<IDenegacionDeAcceso>());
        servicios.AddSingleton(Falso<IReloj>());
        servicios.AddSingleton(Falso<IControlDeLimites>());
        servicios.AddSingleton(Falso<IExigenciaDeVerificacionDePersona>());
        servicios.AddSingleton(Falso<ICalculadorDeHuella>());
        servicios.AddSingleton(Falso<IEnviadorDeCorreo>());
        servicios.AddSingleton(Falso<IAuditoria>());
        servicios.AddSingleton(Falso<IPublicadorDeAlertasTecnicas>());
        servicios.AddSingleton(Falso<IRegistroDeTrabajosDeFondo>());
        servicios.AddSingleton(Falso<ICorreoVerificadoDeSistema>());
        return servicios;
    }

    [Fact]
    public void Se_resuelven_los_contratos_publicos_los_receptores_de_Identidad_y_los_casos_de_uso_sin_dobles_provisionales()
    {
        var servicios = ConExternos().AgregarReclamaciones();
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var alcance = proveedor.CreateScope();
        var sp = alcance.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IReclamaciones>());
        Assert.NotNull(sp.GetRequiredService<IAdministracionDeReclamaciones>());
        Assert.NotNull(sp.GetRequiredService<IHojasDelConsumidor>());
        Assert.Contains(sp.GetServices<IPendientesDeCuenta>(), p => p is ContarHojasPendientesDeUnCorreo);
        Assert.Contains(sp.GetServices<IAplicadorDeCancelacion>(), p => p is MarcarHojasDeUnaCuentaCancelada);
        Assert.IsType<CorreoVerificadoDeLaCuenta>(sp.GetRequiredService<ICorreoVerificadoDeLaCuenta>());
        Assert.IsType<RepositorioDeHojasPostgres>(sp.GetRequiredService<IRepositorioDeHojas>());
        Assert.IsType<AnonimizadorDeHojasPostgres>(sp.GetRequiredService<IAnonimizadorDeHojas>());
        Assert.NotNull(sp.GetRequiredService<AnonimizarHojasVencidas>());
        Assert.NotNull(sp.GetRequiredService<RevisarHojasSinAnonimizar>());
        Assert.NotNull(sp.GetRequiredService<ResponderHoja>());
    }

    [Fact]
    public void Los_dos_trabajos_se_registran_con_nombres_unicos_y_el_servicio_hospedado_los_recibe_completos()
    {
        var servicios = ConExternos().AgregarReclamaciones();
        servicios.AgregarTrabajosDeFondoDeReclamaciones(_ => (corrida, cancelacion) => corrida(cancelacion));
        servicios.AddHostedService<ServicioDeTrabajosDeReclamaciones>();
        using var proveedor = servicios.BuildServiceProvider();

        var servicio = Assert.IsType<ServicioDeTrabajosDeReclamaciones>(proveedor.GetRequiredService<IEnumerable<Microsoft.Extensions.Hosting.IHostedService>>().Single());

        Assert.Equal(["reclamaciones-anonimizacion-diaria", "reclamaciones-revision-de-la-anonimizacion"], servicio.Trabajos.Select(t => t.Nombre));
    }

    // ---------- Correo verificado ----------

    private static CorreoVerificadoDeLaCuenta Adaptador(Func<Guid, Task<string?>> consultar)
    {
        var proxy = DispatchProxy.Create<ICorreoVerificadoDeSistema, ProxyDeCorreo>();
        ((ProxyDeCorreo)(object)proxy).Consultar = consultar;
        return new CorreoVerificadoDeLaCuenta(proxy);
    }

    [Fact]
    public async Task Un_correo_verificado_sale_normalizado_y_uno_sin_verificar_es_nulo()
    {
        // Identidad entrega el correo verificado (o nulo si no lo hay); el adaptador lo normaliza para el Dominio de Reclamaciones.
        Assert.Equal("ana@example.com", await Adaptador(_ => Task.FromResult<string?>("  Ana@Example.COM ")).De(Guid.NewGuid()));
        Assert.Null(await Adaptador(_ => Task.FromResult<string?>(null)).De(Guid.NewGuid()));
    }

    [Fact]
    public async Task Las_fallas_de_Identidad_se_propagan_y_nunca_se_convierten_en_sin_correo_verificado()
    {
        var lanza = Adaptador(_ => throw new HttpRequestException("Identidad cayó"));
        var sinCuenta = Adaptador(_ => throw new InvalidOperationException("Identidad no entregó la cuenta."));

        await Assert.ThrowsAsync<HttpRequestException>(() => lanza.De(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinCuenta.De(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => sinCuenta.De(Guid.Empty));
    }

    // ---------- Programación ----------

    [Theory]
    [InlineData(2026, 11, 19, 8, 59, 0, 0, 1)]
    [InlineData(2026, 11, 19, 9, 0, 0, 24, 0)]
    [InlineData(2026, 11, 19, 12, 0, 0, 21, 0)]
    [InlineData(2026, 11, 19, 0, 0, 0, 9, 0)]
    [InlineData(2026, 11, 19, 4, 30, 0, 4, 30)]
    public void La_anonimizacion_espera_hasta_las_cuatro_de_Lima_que_son_las_nueve_UTC_y_a_la_hora_exacta_la_de_manana(int anio, int mes, int dia, int hora, int minuto, int segundo, int horasEsperadas, int minutosEsperados)
    {
        var espera = TrabajosDeFondoDeReclamaciones.EsperaHastaLaProximaAnonimizacion(new DateTimeOffset(anio, mes, dia, hora, minuto, segundo, TimeSpan.Zero));

        Assert.Equal(new TimeSpan(horasEsperadas, minutosEsperados, 0), espera);
    }

    [Fact]
    public void Cerca_de_la_medianoche_de_Lima_la_proxima_corrida_es_la_madrugada_siguiente()
    {
        // 23:00 en Lima del 19 es las 04:00 UTC del 20: quedan 5 horas hasta las 04:00 de Lima del 20 (09:00 UTC).
        Assert.Equal(TimeSpan.FromHours(5), TrabajosDeFondoDeReclamaciones.EsperaHastaLaProximaAnonimizacion(new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero)));
    }

    // ---------- Ejecutor ----------

    private sealed class RelojManual(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; private set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));
    }

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("El registro no responde.");
            }

            Corridas.Add((trabajo, resultado));
            return Task.CompletedTask;
        }
    }

    private static TrabajoDeFondoDeReclamaciones Crear(Func<CancellationToken, Task> corrida, RegistroEspia registro, List<string> alcances, bool alIniciar = true, TimeSpan? espera = null) =>
        new(
            "reclamaciones-prueba",
            corrida,
            registro,
            async (c, ct) =>
            {
                alcances.Add("abierto");
                await c(ct);
            },
            new RelojManual(Inicio),
            _ => espera ?? TimeSpan.FromHours(1),
            alIniciar);

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Una_corrida_exitosa_va_dentro_del_alcance_de_sistema_y_se_registra()
    {
        var registro = new RegistroEspia();
        var alcances = new List<string>();
        var corridas = 0;

        await Crear(_ => { corridas++; return Task.CompletedTask; }, registro, alcances).EjecutarUnaVez();

        Assert.Equal((1, 1), (corridas, alcances.Count));
        Assert.Equal(("reclamaciones-prueba", ResultadoDeTrabajo.Exito), Assert.Single(registro.Corridas));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Una_corrida_que_falla_se_registra_como_fallo_sin_propagar_y_un_fallo_del_registro_no_detiene_el_trabajo()
    {
        var registro = new RegistroEspia();
        var trabajo = Crear(_ => throw new InvalidOperationException("la base cayó"), registro, []);

        await trabajo.EjecutarUnaVez();
        registro.Falla = true;
        await trabajo.EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Fallo, Assert.Single(registro.Corridas).Resultado);
    }

    [Fact]
    public async Task La_cancelacion_del_apagado_no_se_registra_como_fallo()
    {
        using var origen = new CancellationTokenSource();
        var registro = new RegistroEspia();
        var trabajo = Crear(async ct => { await origen.CancelAsync(); ct.ThrowIfCancellationRequested(); }, registro, []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trabajo.EjecutarUnaVez(origen.Token));

        Assert.Empty(registro.Corridas);
    }

    [Fact]
    public async Task Correr_hace_una_corrida_al_empezar_si_se_pidio_y_termina_con_la_cancelacion_sin_lanzar()
    {
        using var origen = new CancellationTokenSource();
        var corridas = 0;
        var alIniciar = Crear(_ => { corridas++; origen.Cancel(); return Task.CompletedTask; }, new RegistroEspia(), [], alIniciar: true);

        await alIniciar.Correr(origen.Token);

        Assert.Equal(1, corridas);
    }

    [Fact]
    public async Task Sin_correr_al_iniciar_no_corre_hasta_que_llega_su_hora()
    {
        using var origen = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var corridas = 0;
        var trabajo = Crear(_ => { corridas++; return Task.CompletedTask; }, new RegistroEspia(), [], alIniciar: false, espera: TimeSpan.FromHours(1));

        await trabajo.Correr(origen.Token);

        Assert.Equal(0, corridas);
    }
}
