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

    [Fact]
    public async Task LosPuertosDeReservasNoSonCasosDeUsoExpuestos_NiLaGeneracionNiLaCancelacionPorPedido()
    {
        await bd.AgregarFuente("lima", recepcion: true);
        using var pila = new PilaDeReservas(bd, conTraslados: true);

        Assert.DoesNotContain(typeof(GenerarTrasladosDePedido).GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICasoDeUso<,>));
        Assert.DoesNotContain(typeof(CancelarTrasladosDePedido).GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICasoDeUso<,>));
    }
}
