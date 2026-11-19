using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T23 (PED-31; AC-PED-26): los pendientes de pedidos de una cuenta, sin datos de otra.</summary>
public sealed class ContarPendientesDePedidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static int _contador;

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static PedidoDeDominio Pedido(Guid cuenta, params bool[] esDigital)
    {
        var lineas = esDigital.Select((digital, i) => new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), digital, $"Libro {i}", null, 1, 50m, 50m, digital ? null : "LIM", false, null, null)).ToArray();
        var tipo = esDigital.All(d => d) ? TipoDeEntregaDelPedido.Digital : TipoDeEntregaDelPedido.Recojo;
        return PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? "LIM" : null, null, 0m, 50m * lineas.Length, null, new("t-1", Ahora), new("p-1", Ahora), esDigital.Any(d => d) ? new("rd-1", Ahora) : null, lineas, Ahora)).Pedido!;
    }

    [Fact]
    [Trait("Criterio", "AC-PED-26")]
    public async Task Cuenta_los_pedidos_con_una_impresa_sin_cerrar_y_los_reembolsos_pendientes_de_la_cuenta_y_nada_de_otra()
    {
        var (primero, segundo, tercero, cuarto, ajeno) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repositorio = new RepositorioDePedidosFalso();

        // 1: una línea en preparación y otra ya entregada.
        var enPreparacion = Pedido(primero, false, false);
        enPreparacion.PrepararLineas([enPreparacion.Lineas[0].Id, enPreparacion.Lineas[1].Id], Ahora);
        enPreparacion.MarcarListas([enPreparacion.Lineas[1].Id], Ahora);
        enPreparacion.EntregarLineas([enPreparacion.Lineas[1].Id], Ahora);
        // 2: una línea no recogida.
        var noRecogido = Pedido(segundo, false);
        noRecogido.PrepararLineas([noRecogido.Lineas[0].Id], Ahora - TimeSpan.FromDays(20));
        noRecogido.MarcarListas([noRecogido.Lineas[0].Id], Ahora - TimeSpan.FromDays(20));
        noRecogido.MarcarNoRecogidas(Ahora, new HashSet<Guid>());
        // 3: solo digital. 4: todo anulado con un reembolso pendiente. Y un cliente ajeno con pendientes.
        var soloDigital = Pedido(tercero, true);
        var anulado = Pedido(cuarto, false);
        anulado.AplicarAnulacion(anulado.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, Ahora);
        foreach (var pedido in new[] { enPreparacion, noRecogido, soloDigital, anulado, Pedido(ajeno, false), Pedido(ajeno, false) })
        {
            repositorio.Poner(pedido);
        }

        var reembolsos = new ReembolsosFalsos();
        reembolsos.PendientesPorCuenta[(cuarto, OrigenDelReembolso.Anulacion)] = 1;
        reembolsos.PendientesPorCuenta[(ajeno, OrigenDelReembolso.Anulacion)] = 5;
        var caso = new ContarPendientesDePedidos(repositorio, reembolsos);

        var cuentas = new List<int> { await caso.Contar(primero), await caso.Contar(segundo), await caso.Contar(tercero), await caso.Contar(cuarto), await caso.Contar(ajeno) };

        Assert.Equal(new List<int> { 1, 1, 0, 1, 7 }, cuentas);
    }

    [Fact]
    public async Task Los_reembolsos_de_cancelacion_del_cliente_cuentan_y_si_no_puede_contar_lanza_en_vez_de_asumir_cero()
    {
        var cuenta = Guid.NewGuid();
        var reembolsos = new ReembolsosFalsos();
        reembolsos.PendientesPorCuenta[(cuenta, OrigenDelReembolso.CancelacionDelCliente)] = 2;

        Assert.Equal(2, await new ContarPendientesDePedidos(new RepositorioDePedidosFalso(), reembolsos).Contar(cuenta));

        reembolsos.FallaAlContar = true;
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new ContarPendientesDePedidos(new RepositorioDePedidosFalso(), reembolsos).Contar(cuenta));
        await Assert.ThrowsAsync<ArgumentException>(() => new ContarPendientesDePedidos(new RepositorioDePedidosFalso(), reembolsos).Contar(Guid.Empty));
    }
}
