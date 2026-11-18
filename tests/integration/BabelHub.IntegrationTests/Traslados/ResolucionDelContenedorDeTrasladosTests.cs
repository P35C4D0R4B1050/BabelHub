using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T25: el contenedor de producción resuelve todas las piezas de <c>Traslados</c> con las de <c>Fuentes</c> y de <c>Reservas</c> sin ciclos de inyección, venga <c>Traslados</c> antes o después de <c>Reservas</c> en el registro: el
/// generador real reemplaza al adaptador que falla a la vista, el receptor de anulaciones se suma a los demás y los puertos de <c>Reservas</c> no se exponen como casos de uso. La pila se construye con validación de ámbitos y de
/// construcción.
/// </summary>
public sealed class ResolucionDelContenedorDeTrasladosTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElContenedorResuelveTodasLasPiezasDeTrasladosSinCiclos_EnCualquierOrdenDeRegistro(bool reservasPrimero)
    {
        await bd.AgregarFuente("lima", recepcion: true);
        using var pila = new PilaDeReservas(bd, reservasPrimero: reservasPrimero, conTraslados: true);
        using var ambito = pila.Proveedor.CreateScope();
        var sp = ambito.ServiceProvider;

        Assert.IsType<GenerarTrasladosDePedido>(sp.GetRequiredService<IGeneradorDeTrasladosDePedido>());
        Assert.Contains(sp.GetRequiredService<IEnumerable<IAlLiberarPedido>>(), r => r is CancelarTrasladosDePedido);
        Assert.IsType<ReservasParaTraslados>(sp.GetRequiredService<IReservasParaTraslados>());
        Assert.NotNull(sp.GetRequiredService<EntregarMensajesSalientes>());

        // Todos los casos de uso del contrato público se resuelven por ICasoDeUso.
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<SolicitarTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<AprobarTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<RechazarTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<CancelarTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<DespacharTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<RecibirTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<RegistrarGuiaDeTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<ConsultarOrigenesPosiblesEntrada, OrigenesPosibles>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<ConsultarTrasladoEntrada, TrasladoRegistrado>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<ListarTrasladosEntrada, PaginaDeTraslados>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<ConsultarTrasladosDeUnPedidoEntrada, TrasladosDeUnPedido>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<RevisarTrasladosSinAprobarEntrada, ResultadoDeLaRevision>>());
        Assert.NotNull(sp.GetRequiredService<ICasoDeUso<DepurarTrasladosEntrada, ResultadoDeLaDepuracionDeTraslados>>());
    }

    [Fact]
    public async Task LosPuertosDeReservasNoSonCasosDeUsoExpuestos_NiLaGeneracionNiLaCancelacionPorPedido()
    {
        await bd.AgregarFuente("lima", recepcion: true);
        using var pila = new PilaDeReservas(bd, conTraslados: true);

        Assert.DoesNotContain(typeof(GenerarTrasladosDePedido).GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICasoDeUso<,>));
        Assert.DoesNotContain(typeof(CancelarTrasladosDePedido).GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICasoDeUso<,>));
    }
}
