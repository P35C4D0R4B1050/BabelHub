using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// T20: el registro de dependencias del módulo se resuelve completo y sin ciclos (con la validación de ámbitos y de construcción de .NET), cada caso de uso lleva el decorador que le corresponde (los de lectura,
/// la lectura coherente; los de escritura, la transacción, y solo los tres idempotentes el reintento por interbloqueo) y los adaptadores nulos de los puertos de otros módulos se rechazan en producción cuando el
/// módulo real ya existe.
/// </summary>
public sealed class ComposicionDeFuentesTests
{
    private sealed class DenegacionesNulas : IDenegacionDeAcceso
    {
        public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class AuditoriaNula : IAuditoria
    {
        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private static ServiceProvider Construir(Action<IServiceCollection>? ajustes = null)
    {
        var servicios = new ServiceCollection();
        var actor = new ActorDelFlujo();
        servicios.AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=no-se-conecta;Username=nadie;Password=nada-0123456789"));
        servicios.AddSingleton<IActorActual>(actor);
        servicios.AddSingleton<IEstablecedorDeActor>(actor);
        servicios.AddSingleton<IReloj>(new RelojDeIntegracion());
        servicios.AddSingleton<IPublicadorDeAlertasTecnicas>(new AlertasTecnicasEspia());
        servicios.AddScoped<IDenegacionDeAcceso, DenegacionesNulas>();
        servicios.AddScoped<IAuditoria, AuditoriaNula>();
        ajustes?.Invoke(servicios);
        servicios.AgregarModuloFuentes();
        return servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static readonly (Type Entrada, Type Salida, bool EsLectura)[] CasosDeUso =
    [
        (typeof(RegistrarFuenteEntrada), typeof(DetalleDeFuente), false),
        (typeof(EditarFuenteEntrada), typeof(DetalleDeFuente), false),
        (typeof(DesignarFuenteDeRecepcionEntrada), typeof(RecepcionDesignada), false),
        (typeof(RegistrarRutaEntrada), typeof(RutaDefinida), false),
        (typeof(RegistrarFichaDeEdicionEntrada), typeof(FichaDeEdicionGuardada), false),
        (typeof(ConsultarPlazoDeRutaEntrada), typeof(PlazoDeRutaConsultado), true),
        (typeof(ListarRutasEntrada), typeof(IReadOnlyList<RutaListada>), true),
        (typeof(ConsultarDisponibilidadEntrada), typeof(DisponibilidadConsultada), true),
        (typeof(ListarSucursalesEntrada), typeof(IReadOnlyList<SucursalPublica>), true),
        (typeof(ConsultarFuenteEntrada), typeof(DetalleDeFuente), true),
        (typeof(ListarFuentesEntrada), typeof(IReadOnlyList<FuenteDeStock>), true),
        (typeof(ConsultarExistenciasDeSucursalEntrada), typeof(PaginaDeExistencias), true),
        (typeof(ConsultarUnidadesVendidasEntrada), typeof(UnidadesVendidasConsultadas), true),
        (typeof(ConsultarContactoDeNotificacionEntrada), typeof(ContactoDeNotificacion), true),
    ];

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void ElContenedor_ResuelveTodosLosCasosDeUsoYElContratoPublicoSinCiclos()
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        foreach (var (entrada, salida, _) in CasosDeUso)
        {
            Assert.NotNull(ambito.ServiceProvider.GetRequiredService(typeof(ICasoDeUso<,>).MakeGenericType(entrada, salida)));
        }

        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IMovimientosDeStock>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IAplicadorDeMovimientos>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IExistencias>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IDirectorioDeFuentes>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IFichasDeEdiciones>());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void ElAplicadorSeRegistraConLaImplementacionSinLiberacionPrevia_NoConMovimientosDeStock_QueLoInyecta()
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        var aplicador = ambito.ServiceProvider.GetRequiredService<IAplicadorDeMovimientos>();
        var movimientos = ambito.ServiceProvider.GetRequiredService<IMovimientosDeStock>();

        Assert.Equal("AplicadorDeMovimientos", aplicador.GetType().Name);
        Assert.Equal("MovimientosDeStock", movimientos.GetType().Name);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void LosCasosDeLecturaLlevanLaLecturaCoherente_YLosDeEscrituraLaTransaccion_ConElReintentoSoloEnLosTresIdempotentes()
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        foreach (var (entrada, salida, esLectura) in CasosDeUso)
        {
            var caso = ambito.ServiceProvider.GetRequiredService(typeof(ICasoDeUso<,>).MakeGenericType(entrada, salida));
            var tipo = caso.GetType();

            if (esLectura)
            {
                Assert.Equal(typeof(LecturaCoherentePorCasoDeUso<,>), tipo.GetGenericTypeDefinition());
            }
            else
            {
                var esIdempotente = tipo.GetGenericTypeDefinition() == typeof(ReintentoPorInterbloqueo<,>);
                Assert.True(esIdempotente || tipo.GetGenericTypeDefinition() == typeof(TransaccionPorCasoDeUso<,>), tipo.Name);
                Assert.Equal(ComposicionDeFuentes.CasosDeUsoIdempotentes.Any(t => t.Name == NombreDelCaso(entrada)), esIdempotente);
            }
        }

        Assert.Equal(["RegistrarRuta", "RegistrarFichaDeEdicion", "DesignarFuenteDeRecepcion"], ComposicionDeFuentes.CasosDeUsoIdempotentes.Select(t => t.Name));
    }

    private static string NombreDelCaso(Type entrada) => entrada.Name.Replace("Entrada", string.Empty, StringComparison.Ordinal) switch
    {
        "RegistrarRuta" => "RegistrarRuta",
        "RegistrarFichaDeEdicion" => "RegistrarFichaDeEdicion",
        "DesignarFuenteDeRecepcion" => "DesignarFuenteDeRecepcion",
        var otro => otro,
    };

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void SinLosModulosReales_ResolverNulosEsValidoEnDesarrollo_YEnProduccionTodavia()
    {
        using var proveedor = Construir();

        Assert.Empty(ComposicionDeFuentes.AdaptadoresNulosConModuloReal(proveedor));
        ComposicionDeFuentes.ExigirAdaptadoresReales(proveedor, esProduccion: true);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void UnAdaptadorRealRegistradoPrimero_GanaAlNulo()
    {
        using var proveedor = Construir(s => s.AddSingleton<IReservasDeStock, ReservasDeStockFijo>());
        using var ambito = proveedor.CreateScope();

        Assert.IsType<ReservasDeStockFijo>(ambito.ServiceProvider.GetRequiredService<IReservasDeStock>());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public void LaDeteccionDelModuloReal_MiraSiExisteUnaImplementacionQueNoSeaElNulo()
    {
        var nulo = typeof(ComposicionDeFuentes).Assembly.GetTypes().Single(t => t.Name == "AdaptadoresNulosDeFuentes");
        var detectar = nulo.GetMethod("ExisteAdaptadorReal")!;

        // En la Infraestructura todavía no hay un Reservas ni un Ajustes reales; en el ensamblado de pruebas sí hay una implementación (el doble) y se detecta.
        Assert.False((bool)detectar.Invoke(null, [typeof(IReservasDeStock), typeof(ComposicionDeFuentes).Assembly])!);
        Assert.False((bool)detectar.Invoke(null, [typeof(IOrigenDeUnidadesVendidas), typeof(ComposicionDeFuentes).Assembly])!);
        Assert.False((bool)detectar.Invoke(null, [typeof(IReingresosPorDevolucion), typeof(ComposicionDeFuentes).Assembly])!);
        Assert.True((bool)detectar.Invoke(null, [typeof(IReservasDeStock), typeof(ReservasDeStockFijo).Assembly])!);
    }
}
