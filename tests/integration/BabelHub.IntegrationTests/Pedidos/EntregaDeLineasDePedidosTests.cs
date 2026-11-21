using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La entrega de líneas de recojo con la Aplicación real sobre PostgreSQL real (T33; PED-11, PED-25; AC-PED-08, AC-PED-09, AC-PED-21, AC-PED-22): la entrega descuenta el físico y cierra la reserva una vez por línea con su clave;
/// con el Inventario caído la línea no cambia y queda reintentable; un corte entre el REST y el tercer tiempo se completa por la reanudación <b>sin descontar dos veces</b> (la misma clave por línea); repetirla no repite el cierre.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class EntregaDeLineasDePedidosTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private static int _contador;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "ENTR2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "T";

    /// <summary>Un pedido de recojo de «LIM» con dos líneas listas para recoger.</summary>
    private async Task<Pedido> SembrarListo()
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var datos = Enumerable.Range(0, 2)
            .Select(i => new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, $"Libro {i}", null, 1, 40m, 40m, "LIM", false, null, null))
            .ToArray();
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 80m, null, new("t-1", ahora), new("p-1", ahora), null, datos, ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        var cargado = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true))!;
        var ids = cargado.Lineas.Select(l => l.Id).ToList();
        cargado.PrepararLineas(ids, ahora);
        cargado.MarcarListas(ids, ahora);
        await bd.Pedidos.Actualizar(cargado);
        return (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!;
    }

    private static Task<Resultado<LineasOperadas>> Entregar(EscenarioDePedidos escenario, Pedido pedido, IReadOnlyList<Guid>? lineas = null) =>
        escenario.Como(
            EscenarioDePedidos.DelEncargado(ResolucionDeSucursalFalsa.Lima),
            sp => sp.GetRequiredService<IGestionDeRecojo>().Entregar("LIM", new OperarLineasEntrada(pedido.Id, lineas ?? [.. pedido.Lineas.Select(l => l.Id)])));

    private async Task<IReadOnlyList<EstadoDeLineaImpresa?>> Estados(Guid pedidoId) => [.. (await bd.Pedidos.Obtener(pedidoId, conBloqueo: false))!.Lineas.Select(l => l.EstadoImpreso)];

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Entregar_descuenta_y_cierra_la_reserva_una_vez_por_linea_y_repetirlo_no_vuelve_a_cerrar()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarListo();

        var resultado = await Entregar(escenario, pedido);
        var repetida = await Entregar(escenario, pedido);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.All(await Estados(pedido.Id), e => Assert.Equal(EstadoDeLineaImpresa.Entregado, e));
        Assert.Equal(2, escenario.Reservas.Cierres.Count);
        Assert.Equal(escenario.Reservas.Cierres.Count, escenario.Reservas.Cierres.Select(c => c.Clave).Distinct().Count());
        // Repetir: las líneas ya entregadas no se cierran otra vez (la respuesta puede ser éxito sin cambios o estado inválido, nunca un segundo descuento).
        Assert.Equal(2, escenario.Reservas.Cierres.Count);
        Assert.NotNull(repetida);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Una_linea_no_recogida_no_se_entrega_se_rechaza_sin_llamar_al_Inventario_y_el_pedido_no_cambia()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var listo = await SembrarListo();
        var cargado = (await bd.Pedidos.Obtener(listo.Id, conBloqueo: true))!;
        Assert.NotEmpty(cargado.MarcarNoRecogidas(EscenarioDePedidos.InicioDelDia.AddHours(361), new HashSet<Guid>()));
        await bd.Pedidos.Actualizar(cargado);

        var resultado = await Entregar(escenario, listo);

        Assert.False(resultado.EsExito);
        Assert.Equal("pedido-estado-invalido", resultado.Error!.Clave);
        Assert.Empty(escenario.Reservas.Cierres);
        Assert.All(await Estados(listo.Id), e => Assert.Equal(EstadoDeLineaImpresa.NoRecogido, e));
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().EntregasEnCurso(listo.Id)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Con_el_Inventario_caido_las_lineas_no_cambian_la_entrega_queda_fallida_y_repetirla_la_completa()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarListo();
        escenario.Reservas.Respuesta = _ => ResultadoDeCerrarReserva.Incierto;

        var caida = await Entregar(escenario, pedido);

        Assert.False(caida.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, caida.Error!.Categoria);
        Assert.All(await Estados(pedido.Id), e => Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, e));

        // La entrega queda fallida y visible para la administración (sin datos personales); reintentar es repetir la operación cuando el Inventario vuelve.
        var fallidas = await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().ListarOperacionesFallidas(1, 20));
        Assert.True(fallidas.EsExito, fallidas.Error?.Clave);
        Assert.Single(fallidas.Valor.Operaciones);
        Assert.Equal(pedido.Id, fallidas.Valor.Operaciones[0].PedidoId);

        escenario.Reservas.Respuesta = _ => ResultadoDeCerrarReserva.Cerrada;
        var reintento = await Entregar(escenario, pedido);

        Assert.True(reintento.EsExito, reintento.Error?.Clave);
        Assert.All(await Estados(pedido.Id), e => Assert.Equal(EstadoDeLineaImpresa.Entregado, e));
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().EntregasEnCurso(pedido.Id)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-21")]
    public async Task Un_corte_tras_el_REST_se_completa_por_la_reanudacion_con_la_misma_clave_por_linea_sin_descontar_dos_veces()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarListo();
        escenario.Auditoria.CaeDespuesDe = 1;

        var cortada = await Entregar(escenario, pedido);

        Assert.False(cortada.EsExito);
        Assert.All(await Estados(pedido.Id), e => Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, e));
        var clavesDelPrimerIntento = escenario.Reservas.Cierres.Select(c => (c.Linea, c.Clave)).OrderBy(c => c.Linea).ToList();
        Assert.Equal(2, clavesDelPrimerIntento.Count);

        escenario.Auditoria.CaeDespuesDe = null;
        escenario.Reloj.Ahora = EscenarioDePedidos.InicioDelDia.AddMinutes(5);
        var reanudacion = await escenario.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<CompletarOperacionesEnCurso>().Reanudar());

        Assert.True(reanudacion.EsExito, reanudacion.Error?.Clave);
        Assert.All(await Estados(pedido.Id), e => Assert.Equal(EstadoDeLineaImpresa.Entregado, e));
        // Cada línea se cerró con la misma clave en el reintento (el Inventario la deduplica): nunca una clave nueva que descontara otra vez.
        foreach (var (linea, clave) in clavesDelPrimerIntento)
        {
            Assert.All(escenario.Reservas.Cierres.Where(c => c.Linea == linea), c => Assert.Equal(clave, c.Clave));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-22")]
    public async Task Una_entrega_parcial_deja_las_otras_lineas_como_estaban_y_las_claves_son_por_linea()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarListo();
        var primera = pedido.Lineas[0].Id;

        var resultado = await Entregar(escenario, pedido, [primera]);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var estados = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: false))!.Lineas.ToDictionary(l => l.Id, l => l.EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.Entregado, estados[primera]);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, estados[pedido.Lineas[1].Id]);
        Assert.Equal([primera], escenario.Reservas.Cierres.Select(c => c.Linea).ToList());
    }
}
