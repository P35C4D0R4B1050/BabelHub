using BabelHub.Ecommerce.Api.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Auditoria;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Inv = BabelHub.Inventario;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>
/// Los umbrales de alerta de seguridad con <c>Auditoria</c> real (T29 de <c>transversal-seguridad</c>; AC-SEG-09, AC-SEG-23; SEG-09, SEG-27): con exactamente 10 accesos denegados (Inventario) o 20 ingresos fallidos no hay alerta;
/// con 11 y 21 sí, una sola vez; la alerta del Inventario llega a la lista de la administración central y a un solo correo del E-commerce; y repetida dentro de los 15 minutos es una sola entrada con el contador y sin otro
/// correo, y pasados los 15 minutos una nueva entrada con su correo. <b>El tope de 24 correos al día lo aplica el puerto de correo (IDE-82), que construye <c>identidad-seguridad</c> T11 y T34</b>: aquí se prueba que Auditoría
/// respeta su resultado «suprimido» (la alerta queda en la lista sin correo y no se reintenta); el tope mismo se prueba con ese adaptador. El reloj de cada servicio es controlable y avanza entre pruebas.
/// </summary>
public sealed class UmbralesDeAlertaConAuditoriaRealTests(PostgresDeAuditoriaFixture bdEco, AuditoriaInventario.PostgresDeAuditoriaFixture bdInv)
    : IClassFixture<PostgresDeAuditoriaFixture>, IClassFixture<AuditoriaInventario.PostgresDeAuditoriaFixture>, IAsyncLifetime
{
    private static long _dias = Random.Shared.Next(1, 20_000);

    private sealed class RelojDelEcommerce(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class RelojDelInventario(DateTimeOffset inicio) : Inv.Application.Plataforma.IReloj
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class CorreoEspia : IEnvioDeCorreoDeAlertas
    {
        public List<CorreoDeAlerta> Enviados { get; } = [];

        public ResultadoDeEnvioDeAlerta Respuesta { get; set; } = ResultadoDeEnvioDeAlerta.Enviado;

        public Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default)
        {
            lock (Enviados)
            {
                Enviados.Add(correo);
            }

            return Task.FromResult(Respuesta);
        }
    }

    private sealed class IdentidadDePrueba : IFuenteDeRegistrosAislados, IDestinatariosDeAlertas
    {
        public int IngresosFallidos { get; set; }

        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => Task.FromResult(new PaginaDeRegistros([], null));

        public Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default) => Task.FromResult(IngresosFallidos);

        public Task<IReadOnlyList<DestinatarioDeAlerta>> Activos(CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([new DestinatarioDeAlerta(Guid.NewGuid()), new DestinatarioDeAlerta(Guid.NewGuid())]);

        public Task<IReadOnlyList<DestinatarioDeAlerta>> DeLogistica(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([]);
    }

    private sealed class OrigenEco : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class OrigenInv : Inv.Application.Plataforma.IOrigenDeSolicitud
    {
        public Inv.Application.Plataforma.OrigenDeSolicitud Actual => Inv.Application.Plataforma.OrigenDeSolicitud.Desconocido;
    }

    private sealed class SinPlantillas : IPlantillasDeRuta
    {
        public IReadOnlyCollection<string> Todas => [];
    }

    private sealed class SinPlantillasInv : Inv.Application.Auditoria.IPlantillasDeRuta
    {
        public IReadOnlyCollection<string> Todas => [];
    }

    private sealed class TokenNulo : ITokenDelUsuarioDeLaSesion
    {
        public Task<ValorSecreto?> Actual(CancellationToken cancelacion = default) => Task.FromResult<ValorSecreto?>(null);
    }

    private InventarioDeAuditoriaHost _host = null!;

    public async Task InitializeAsync() => _host = await InventarioDeAuditoriaHost.Crear(bdInv);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static DateTimeOffset BaseDeLaPrueba() => new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc)).AddDays(Interlocked.Increment(ref _dias)).AddHours(10);

    private ServiceProvider ProveedorDelEcommerce(RelojDelEcommerce reloj, IdentidadDePrueba identidad, CorreoEspia correo)
    {
        var actor = new ActorDelFlujo();
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton(bdEco.Acceso);
        servicios.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>()));
        servicios.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<IActorActual>(actor);
        servicios.AddSingleton<IEstablecedorDeActor>(actor);
        servicios.AddSingleton<IReloj>(reloj);
        servicios.AddScoped<IOrigenDeSolicitud, OrigenEco>();
        servicios.AddSingleton<IPlantillasDeRuta, SinPlantillas>();
        servicios.AddSingleton<ITokenDelUsuarioDeLaSesion, TokenNulo>();
        servicios.AddSingleton<IFuenteDeRegistrosAislados>(identidad);
        servicios.AddSingleton<IDestinatariosDeAlertas>(identidad);
        servicios.AddSingleton<IEnvioDeCorreoDeAlertas>(correo);
        servicios.AddSingleton(new OpcionesDeCredencialesDeSalida(
        [
            new(AlcanceDeServicio.LecturaDeAlertas, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeAlertas)),
            new(AlcanceDeServicio.ConsultaDeAuditoria, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeConsulta)),
            new(AlcanceDeServicio.MarcarAlerta, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeMarca)),
            new(AlcanceDeServicio.Historial, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeHistorial)),
        ]));
        servicios.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(bdEco.Depuracion, new ClavesDeCursorDelEntorno(new string('e', 40), "v1")));
        servicios.AgregarClienteDeAuditoriaDelInventario(new Uri("https://inventario.interno/"));
        servicios.AddHttpClient(HttpClienteDeAuditoriaDelInventario.NombreDelCliente).ConfigurePrimaryHttpMessageHandler(() => _host.CrearManejador());
        return servicios.BuildServiceProvider();
    }

    private ServiceProvider ProveedorDelInventario(RelojDelInventario reloj)
    {
        var actor = new Inv.Infrastructure.Plataforma.ActorDelFlujo();
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton(bdInv.Acceso);
        servicios.AddSingleton(sp => new Inv.Infrastructure.Plataforma.UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>(), actor: sp.GetRequiredService<Inv.Application.Plataforma.IActorActual>()));
        servicios.AddSingleton<Inv.Application.Plataforma.IUnidadDeTrabajo>(sp => sp.GetRequiredService<Inv.Infrastructure.Plataforma.UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<Inv.Application.Plataforma.IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<Inv.Infrastructure.Plataforma.UnidadDeTrabajoPostgres>());
        servicios.AddSingleton<Inv.Application.Plataforma.IActorActual>(actor);
        servicios.AddSingleton<Inv.Application.Plataforma.IEstablecedorDeActor>(actor);
        servicios.AddSingleton<Inv.Application.Plataforma.IReloj>(reloj);
        servicios.AddScoped<Inv.Application.Plataforma.IOrigenDeSolicitud, OrigenInv>();
        servicios.AddSingleton<Inv.Application.Auditoria.IPlantillasDeRuta, SinPlantillasInv>();
        Inv.Infrastructure.Auditoria.ComposicionDeAuditoria.AgregarModuloAuditoria(servicios, new Inv.Infrastructure.Auditoria.OpcionesDelModuloDeAuditoria(bdInv.Depuracion, new Inv.Infrastructure.Auditoria.ClavesDeCursorDelEntorno(new string((char)105, 40), "v1")));
        return servicios.BuildServiceProvider();
    }

    private static async Task<IReadOnlyList<ResultadoDeUmbral>> Evaluar(ServiceProvider proveedor, IEstadoDeUmbrales estados, IReloj reloj, IReadOnlyList<UmbralDeAlerta> umbrales)
    {
        using var alcance = proveedor.CreateScope();
        var servicios = alcance.ServiceProvider;
        return await new EvaluarUmbralesDeAlerta(umbrales, servicios.GetRequiredService<IContadorDeEventosDeSeguridad>(), servicios.GetRequiredService<IPublicadorDeAlertasDeSeguridad>(), estados, reloj).Ejecutar();
    }

    private static async Task<ResultadoDeEnvioDeAlertas> Enviar(ServiceProvider proveedor)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<AgruparYEnviarAlertas>().Ejecutar();
    }

    private async Task<long> Alertas(Func<string, Task<long>> consulta, string motivo, DateTimeOffset desde) =>
        await consulta($"SELECT count(*) FROM auditoria.alertas WHERE motivo = '{motivo}' AND clase = 'seguridad' AND primera_ocurrencia >= '{desde:O}'");

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task ConDiezAccesosDenegadosEnElInventarioNoHayAlerta_ConOnce_UnaSolaAlertaQueLlegaALaListaYAUnSoloCorreo()
    {
        var inicio = BaseDeLaPrueba();
        var relojInv = new RelojDelInventario(inicio);
        var relojEco = new RelojDelEcommerce(inicio);
        var correo = new CorreoEspia();
        await using var inv = ProveedorDelInventario(relojInv);
        await using var eco = ProveedorDelEcommerce(relojEco, new IdentidadDePrueba(), correo);
        var estados = new Inv.Infrastructure.Plataforma.EstadoDeUmbralesEnMemoria();
        var umbralesInv = new[] { Inv.Domain.Plataforma.UmbralesDeAlerta.AccesosDenegados };

        for (var i = 1; i <= 10; i++)
        {
            await DenegarEnElInventario(inv, relojInv, inicio.AddSeconds(i * 5));
        }

        relojInv.Ahora = inicio.AddSeconds(60);
        var conDiez = await EvaluarInventario(inv, estados, relojInv, umbralesInv);
        Assert.False(conDiez.Superado);
        Assert.Equal(0L, await Alertas(bdInv.Consultar<long>, "accesos-denegados", inicio));

        await DenegarEnElInventario(inv, relojInv, inicio.AddSeconds(65));
        relojInv.Ahora = inicio.AddSeconds(120);
        var conOnce = await EvaluarInventario(inv, estados, relojInv, umbralesInv);
        Assert.True(conOnce.Superado);
        Assert.True(conOnce.AlertaPublicada);
        Assert.Equal(1L, await Alertas(bdInv.Consultar<long>, "accesos-denegados", inicio));

        // El E-commerce la lee del Inventario con la credencial de lectura de alertas y envía un solo correo; las siguientes corridas no repiten.
        relojEco.Ahora = inicio.AddSeconds(130);
        var primera = await Enviar(eco);
        var segunda = await Enviar(eco);
        Assert.Equal(1, primera.Enviadas);
        Assert.Equal(0, segunda.Enviadas);
        var enviado = Assert.Single(correo.Enviados);
        Assert.Equal(ServicioAuditado.Inventario, enviado.Servicio);
        Assert.Equal("accesos-denegados", enviado.Motivo.Valor);
        Assert.Equal(ClaseDeAlertaConsultada.Seguridad, enviado.Clase);

        // Y aparece en la lista de la administración central (con el actor de un administrador central).
        using var alcance = eco.CreateScope();
        eco.GetRequiredService<IEstablecedorDeActor>().Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central"));
        var lista = await alcance.ServiceProvider.GetRequiredService<IConsultaDeAlertas>().Listar(null);
        Assert.True(lista.EsExito);
        Assert.Contains(lista.Valor.Alertas, a => a.Servicio == ServicioAuditado.Inventario && a.Motivo.Valor == "accesos-denegados" && a.Id.StartsWith("inv:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task ConVeinteIngresosFallidosNoHayAlerta_ConVeintiuno_UnaSolaAlertaYUnSoloCorreo()
    {
        var inicio = BaseDeLaPrueba();
        var relojEco = new RelojDelEcommerce(inicio.AddMinutes(1));
        var identidad = new IdentidadDePrueba { IngresosFallidos = 20 };
        var correo = new CorreoEspia();
        await using var eco = ProveedorDelEcommerce(relojEco, identidad, correo);
        var estados = new EstadoDeUmbralesEnMemoria();
        var umbrales = new[] { UmbralesDeAlerta.IngresosFallidos };

        var conVeinte = (await Evaluar(eco, estados, relojEco, umbrales)).Single();
        Assert.False(conVeinte.Superado);
        Assert.Equal(0L, await Alertas(bdEco.Consultar<long>, "ingresos-fallidos", inicio));

        identidad.IngresosFallidos = 21;
        relojEco.Ahora = inicio.AddMinutes(2);
        var conVeintiuno = (await Evaluar(eco, estados, relojEco, umbrales)).Single();
        Assert.True(conVeintiuno.AlertaPublicada);
        Assert.Equal(1L, await Alertas(bdEco.Consultar<long>, "ingresos-fallidos", inicio));

        // Mientras siga superado no se vuelve a alertar (no infla las ocurrencias) y el correo sale una sola vez.
        relojEco.Ahora = inicio.AddMinutes(3);
        Assert.False((await Evaluar(eco, estados, relojEco, umbrales)).Single().AlertaPublicada);
        await Enviar(eco);
        await Enviar(eco);
        var enviado = Assert.Single(correo.Enviados);
        Assert.Equal(ServicioAuditado.Ecommerce, enviado.Servicio);
        Assert.Equal("ingresos-fallidos", enviado.Motivo.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task UnaAlertaRepetidaEnQuinceMinutosEsUnaSolaEntradaConElContador_YPasadosLosQuinceMinutosUnaNuevaConSuCorreo()
    {
        var inicio = BaseDeLaPrueba();
        var relojEco = new RelojDelEcommerce(inicio);
        var correo = new CorreoEspia();
        await using var eco = ProveedorDelEcommerce(relojEco, new IdentidadDePrueba(), correo);

        await Publicar(eco, "accesos-denegados");
        await Enviar(eco);
        Assert.Single(correo.Enviados);

        // Dos ocurrencias más dentro de los 15 minutos desde la primera: la misma entrada con el contador en 3 y ningún otro correo.
        relojEco.Ahora = inicio.AddMinutes(5);
        await Publicar(eco, "accesos-denegados");
        relojEco.Ahora = inicio.AddMinutes(14);
        await Publicar(eco, "accesos-denegados");
        await Enviar(eco);
        Assert.Single(correo.Enviados);
        Assert.Equal(1L, await Alertas(bdEco.Consultar<long>, "accesos-denegados", inicio));
        Assert.Equal(3, await bdEco.Consultar<int>($"SELECT ocurrencias FROM auditoria.alertas WHERE motivo = 'accesos-denegados' AND primera_ocurrencia >= '{inicio:O}'"));

        // Pasados los 15 minutos: una nueva entrada y un correo.
        relojEco.Ahora = inicio.AddMinutes(16);
        await Publicar(eco, "accesos-denegados");
        await Enviar(eco);
        Assert.Equal(2, correo.Enviados.Count);
        Assert.Equal(2L, await Alertas(bdEco.Consultar<long>, "accesos-denegados", inicio));

        // El puerto de correo, fuente única del tope diario, no lo envía: la alerta queda en la lista sin correo y no se reintenta.
        correo.Respuesta = ResultadoDeEnvioDeAlerta.Suprimido;
        relojEco.Ahora = inicio.AddMinutes(40);
        await Publicar(eco, "accesos-denegados");
        var suprimida = await Enviar(eco);
        var reintento = await Enviar(eco);
        Assert.Equal(1, suprimida.Suprimidas);
        Assert.Equal(0, reintento.Suprimidas + reintento.Enviadas);
        Assert.Equal(3L, await Alertas(bdEco.Consultar<long>, "accesos-denegados", inicio));
        Assert.Equal(3, correo.Enviados.Count);
    }

    private static async Task Publicar(ServiceProvider eco, string motivo)
    {
        using var alcance = eco.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarDeSeguridad(new CodigoDeAlerta(motivo));
    }

    private static async Task DenegarEnElInventario(ServiceProvider inv, RelojDelInventario reloj, DateTimeOffset instante)
    {
        reloj.Ahora = instante;
        using var alcance = inv.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<Inv.Application.Auditoria.IDenegacionDeAcceso>()
            .Registrar(new Inv.Application.Auditoria.DenegacionDeAcceso("fuera-de-rol", "/api/inventario/v1/auditoria"));
    }

    private static async Task<Inv.Application.Plataforma.Interno.ResultadoDeUmbral> EvaluarInventario(
        ServiceProvider inv, Inv.Application.Plataforma.Interno.IEstadoDeUmbrales estados, Inv.Application.Plataforma.IReloj reloj, IReadOnlyList<Inv.Domain.Plataforma.UmbralDeAlerta> umbrales)
    {
        using var alcance = inv.CreateScope();
        var servicios = alcance.ServiceProvider;
        var resultados = await new Inv.Application.Plataforma.Interno.EvaluarUmbralesDeAlerta(
            umbrales,
            servicios.GetRequiredService<Inv.Application.Plataforma.IContadorDeEventosDeSeguridad>(),
            servicios.GetRequiredService<Inv.Application.Plataforma.IPublicadorDeAlertasDeSeguridad>(),
            estados,
            reloj).Ejecutar();
        return resultados.Single();
    }
}
