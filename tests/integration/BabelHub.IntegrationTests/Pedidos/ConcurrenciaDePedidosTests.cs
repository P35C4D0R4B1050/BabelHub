using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// Las operaciones simultáneas sobre un mismo pedido, con la Aplicación real sobre PostgreSQL real (T33; PED-21, PED-22, PED-25; AC-PED-08, AC-PED-09, AC-PED-17, AC-PED-18, AC-PED-21): cada par de operaciones
/// (cancelar, anular, preparar, marcar listas, entregar) corre <b>20 veces a la vez</b> sobre un pedido de recojo con una línea lista y otra confirmada. No importa quién gane: importa que <b>nunca</b> haya una línea con la
/// reserva cerrada y liberada a la vez, ni dos reembolsos distintos, ni una operación que quede «en curso» sin dueño, ni una excepción que escape al llamador (todo fallo es un <see cref="Resultado{T}"/>).
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class ConcurrenciaDePedidosTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private const int Repeticiones = 20;

    private static readonly Guid Sucursal = ResolucionDeSucursalFalsa.Lima;

    private static int _contador;

    public enum Operacion
    {
        Cancelar,
        Anular,
        Preparar,
        MarcarListas,
        Entregar,
    }

    public static TheoryData<Operacion, Operacion> Pares()
    {
        var datos = new TheoryData<Operacion, Operacion>();
        var todas = Enum.GetValues<Operacion>();
        for (var i = 0; i < todas.Length; i++)
        {
            for (var j = i; j < todas.Length; j++)
            {
                datos.Add(todas[i], todas[j]);
            }
        }

        return datos;
    }

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "ABCD2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "K";

    /// <summary>Un pedido de recojo de la sucursal «LIM» con dos líneas: la primera ya lista para recoger y la segunda solo confirmada.</summary>
    private async Task<Pedido> SembrarPedido()
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var lineas = new[]
        {
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "LIM", false, null, null),
            new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "El Aleph", null, 1, 40m, 40m, "LIM", false, null, null),
        };
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 90m, null, new("t-1", ahora), new("p-1", ahora), null, lineas, ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        var cargado = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true))!;
        var primera = new[] { cargado.Lineas[0].Id };
        cargado.PrepararLineas(primera, ahora);
        cargado.MarcarListas(primera, ahora);
        await bd.Pedidos.Actualizar(cargado);
        return (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
    }

    private static async Task<string> Ejecutar(EscenarioDePedidos escenario, Operacion operacion, Pedido pedido)
    {
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        var entrada = new OperarLineasEntrada(pedido.Id, ids);
        var resultado = operacion switch
        {
            Operacion.Cancelar => await escenario.Como(EscenarioDePedidos.DelTitular(pedido.CuentaId), async sp => Describir(await sp.GetRequiredService<IPedidosDelCliente>().Cancelar(pedido.Id))),
            Operacion.Anular => await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), async sp => Describir(await sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido.Id, "otro")))),
            Operacion.Preparar => await escenario.Como(EscenarioDePedidos.DelEncargado(Sucursal), async sp => Describir(await sp.GetRequiredService<IGestionDeRecojo>().Preparar("LIM", entrada))),
            Operacion.MarcarListas => await escenario.Como(EscenarioDePedidos.DelEncargado(Sucursal), async sp => Describir(await sp.GetRequiredService<IGestionDeRecojo>().MarcarListas("LIM", entrada))),
            _ => await escenario.Como(EscenarioDePedidos.DelEncargado(Sucursal), async sp => Describir(await sp.GetRequiredService<IGestionDeRecojo>().Entregar("LIM", entrada))),
        };
        return resultado;
    }

    private static string Describir<T>(Resultado<T> resultado) => resultado.EsExito ? "ok" : "error:" + resultado.Error!.Clave;

    [Theory]
    [MemberData(nameof(Pares))]
    [Trait("Criterio", "AC-PED-17")]
    [Trait("Criterio", "AC-PED-18")]
    [Trait("Criterio", "AC-PED-21")]
    public async Task Dos_operaciones_simultaneas_sobre_el_mismo_pedido_nunca_dejan_una_reserva_cerrada_y_liberada_ni_dos_reembolsos_ni_una_operacion_huerfana(Operacion primera, Operacion segunda)
    {
        for (var vuelta = 0; vuelta < Repeticiones; vuelta++)
        {
            await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
            var pedido = await SembrarPedido();
            var salida = new TaskCompletionSource();

            var a = Task.Run(async () =>
            {
                await salida.Task;
                return await Ejecutar(escenario, primera, pedido);
            });
            var b = Task.Run(async () =>
            {
                await salida.Task;
                return await Ejecutar(escenario, segunda, pedido);
            });
            salida.SetResult();
            var resultados = await Task.WhenAll(a, b);

            await Comprobar(escenario, pedido, $"{primera}+{segunda} (vuelta {vuelta}): {string.Join(" | ", resultados)}");
        }
    }

    private async Task Comprobar(EscenarioDePedidos escenario, Pedido sembrado, string contexto)
    {
        var final = (await bd.Pedidos.Obtener(sembrado.Id, conBloqueo: false))!;
        var cerradas = escenario.Reservas.Cierres.Select(c => c.Linea).ToHashSet();
        var liberadas = escenario.Reservas.Liberaciones.SelectMany(l => l.Lineas).ToHashSet();

        // Una reserva se cierra (sale el libro) o se libera (vuelve el stock), nunca las dos cosas.
        Assert.True(!cerradas.Overlaps(liberadas), $"Una línea quedó con la reserva cerrada y liberada. {contexto}");

        // Lo cerrado en el Inventario coincide con lo entregado y lo liberado con lo anulado o no recogido: el Inventario y Pedidos no se contradicen.
        foreach (var linea in final.Lineas)
        {
            if (liberadas.Contains(linea.Id))
            {
                Assert.True(linea.EstadoImpreso is EstadoDeLineaImpresa.Anulado or EstadoDeLineaImpresa.NoRecogido, $"Una reserva liberada de una línea que sigue vigente ({linea.EstadoImpreso}). {contexto}");
            }

            if (linea.EstadoImpreso == EstadoDeLineaImpresa.Entregado)
            {
                Assert.True(cerradas.Contains(linea.Id), $"Una línea entregada sin que el Inventario cerrara su reserva. {contexto}");
            }
        }

        // Un solo reembolso por pedido: cancelar y anular a la vez no pagan dos veces.
        Assert.True(escenario.Reembolsos.Registrados.Select(r => r.ClaveNatural).Distinct().Count() <= 1, $"Dos reembolsos distintos para el mismo pedido. {contexto}");

        // Nada queda «en curso» sin dueño: cada operación terminó completada, fallida o descartada.
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().AnulacionesEnCurso(sembrado.Id)));
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().EntregasEnCurso(sembrado.Id)));
    }
}
