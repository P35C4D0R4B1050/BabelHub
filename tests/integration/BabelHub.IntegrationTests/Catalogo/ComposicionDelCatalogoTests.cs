using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.IntegrationTests.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>La composición real del Catálogo (T18, plan §5): qué casos de uso van dentro de su transacción y cuáles no, los contratos públicos que quedan registrados y la auditoría que queda en la misma base.</summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class ComposicionDelCatalogoTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private CatalogoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static bool EsTransaccional(object casoDeUso) => casoDeUso.GetType() is { IsGenericType: true } tipo && tipo.GetGenericTypeDefinition() == typeof(TransaccionPorCasoDeUso<,>);

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public void Los_casos_de_uso_que_escriben_van_dentro_de_su_transaccion_y_los_que_hacen_llamadas_externas_no()
    {
        using var alcance = _host.Servicios.CreateScope();
        var sp = alcance.ServiceProvider;

        var transaccionales = new object[]
        {
            sp.GetRequiredService<ICasoDeUso<RegistrarObraEntrada, ResultadoDeAlta>>(),
            sp.GetRequiredService<ICasoDeUso<RegistrarEdicionEntrada, ResultadoDeAlta>>(),
            sp.GetRequiredService<ICasoDeUso<EditarObraEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<EditarEdicionEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<DarDeBajaObraEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<DarDeBajaEdicionEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<RetirarEdicionDigitalEntrada, ResultadoDeEscritura>>(),
            sp.GetRequiredService<ICasoDeUso<RegistrarIngresoEntrada, IngresoPedido>>(),
            sp.GetRequiredService<ICasoDeUso<MarcarResultadoDeIngresoEntrada, EstadoDeIngresoSolicitado>>(),
            sp.GetRequiredService<ICasoDeUso<EmitirClaveDeIngresoEntrada, ClaveDeIngresoEmitida>>(),
        };
        var noTransaccionales = new object[]
        {
            sp.GetRequiredService<ICasoDeUso<SubirArchivoEntrada, ArchivoSubido>>(),
            sp.GetRequiredService<ICasoDeUso<ReintentarIngresoEntrada, ResultadoDeIngreso>>(),
            sp.GetRequiredService<ICasoDeUso<ConciliarIngresoEntrada, ResultadoDeIngreso>>(),
            sp.GetRequiredService<ICasoDeUso<ListarIngresosEntrada, HistorialDeIngresos>>(),
            sp.GetRequiredService<OrquestadorDeIngresos>(),
        };

        Assert.All(transaccionales, caso => Assert.True(EsTransaccional(caso), caso.GetType().Name));
        Assert.All(noTransaccionales, caso => Assert.False(EsTransaccional(caso), caso.GetType().Name));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public void Los_contratos_publicos_del_catalogo_quedan_registrados_y_la_auditoria_real_comparte_la_unidad_de_trabajo()
    {
        using var alcance = _host.Servicios.CreateScope();
        var sp = alcance.ServiceProvider;

        Assert.NotNull(sp.GetService<IConsultaDeCatalogo>());
        Assert.NotNull(sp.GetService<IDisponibilidadDeEdiciones>());
        Assert.NotNull(sp.GetService<IAdministracionDelCatalogo>());
        Assert.NotNull(sp.GetService<IArchivosDelCatalogo>());
        Assert.NotNull(sp.GetService<IIngresosDeMercaderia>());
        Assert.NotNull(sp.GetService<IRetiroDeEdicionDeCarritos>());
        Assert.Equal("AuditoriaDeAplicacion", sp.GetRequiredService<IAuditoria>().GetType().Name);
        Assert.Same(_host.Servicios.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IUnidadDeTrabajo>());
        Assert.Same(_host.Servicios.GetRequiredService<LimitadorDeSubidas>(), sp.GetRequiredService<LimitadorDeSubidas>());
    }
}
