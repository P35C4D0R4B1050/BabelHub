using BabelHub.Ecommerce.Application.Devoluciones;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// T29 (AC-DEV-03, AC-DEV-07, AC-DEV-15, AC-DEV-16): la Aplicación real de Devoluciones sobre PostgreSQL real y el Inventario y Pagos simulados fieles a sus contratos: solicitudes simultáneas sobre la misma unidad, la confirmación de la recepción con el Inventario caído,
/// con la respuesta perdida y repetida, y la repetición simultánea sin reingresar ni reembolsar dos veces.
/// </summary>
[Collection(ColeccionPostgresDeDevoluciones.Nombre)]
public sealed class ConcurrenciaYReintentosDeDevolucionesTests(PostgresDeDevolucionesFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string?> EstadoDeLaLinea(Guid devolucionId) =>
        await bd.Consultar<string>($"SELECT estado FROM devoluciones.lineas_de_devolucion WHERE devolucion_id = '{devolucionId:D}'");

    private static async Task<Guid> Solicitada(EscenarioDeDevolucionesConPostgres e, Guid cliente, Guid pedidoId, Guid lineaId, int cantidad)
    {
        var resultado = await e.Solicitar(cliente, pedidoId, lineaId, cantidad);
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        return resultado.Valor.DevolucionId;
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public async Task Veinte_pares_de_solicitudes_simultaneas_sobre_la_misma_unidad_dejan_pasar_solo_a_una_y_la_otra_responde_cantidad_excedida()
    {
        var escenario = new EscenarioDeDevolucionesConPostgres(bd);
        for (var i = 0; i < 20; i++)
        {
            var (cliente, pedido, linea) = escenario.SembrarPedidoDeImpresos(cantidad: 1);

            var resultados = await Task.WhenAll(
                Task.Run(() => escenario.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1)),
                Task.Run(() => escenario.Solicitar(cliente, pedido.PedidoId, linea.LineaId, 1)));

            Assert.Equal(1, resultados.Count(r => r.EsExito));
            var rechazada = resultados.Single(r => !r.EsExito);
            Assert.Equal("cantidad-excedida", rechazada.Error!.Clave);
        }

        Assert.Equal(20, await bd.Consultar<long>("SELECT count(*) FROM devoluciones.devoluciones"));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM devoluciones.cupos_de_linea WHERE cantidad_comprometida > cantidad_comprada"));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Con_el_inventario_caido_la_recepcion_no_pasa_a_recibida_y_al_volver_reingresa_una_sola_vez_y_registra_un_solo_reembolso()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var devolucionId = await Solicitada(e, cliente, pedido.PedidoId, linea.LineaId, 1);
        Assert.True((await e.Aprobar(devolucionId)).EsExito);

        e.Inventario.Caido = true;
        var caida = await e.Recibir(devolucionId);

        Assert.False(caida.EsExito);
        Assert.Equal("aprobada", await EstadoDeLaLinea(devolucionId));
        Assert.Equal(0, e.Reembolsos.Registrados);

        e.Inventario.Caido = false;
        var recuperada = await e.Recibir(devolucionId);

        Assert.True(recuperada.EsExito, recuperada.Error?.Clave);
        Assert.Contains(await EstadoDeLaLinea(devolucionId), new[] { "recibida", "reembolsada" });
        Assert.Equal(1, e.Inventario.Aplicaciones(devolucionId));
        Assert.Equal(1, e.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Si_la_respuesta_del_reingreso_se_pierde_el_reintento_no_reingresa_dos_veces()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var devolucionId = await Solicitada(e, cliente, pedido.PedidoId, linea.LineaId, 1);
        Assert.True((await e.Aprobar(devolucionId)).EsExito);

        e.Inventario.PierdeLaRespuesta = true;
        var perdida = await e.Recibir(devolucionId);

        Assert.False(perdida.EsExito);
        Assert.Equal("aprobada", await EstadoDeLaLinea(devolucionId));

        e.Inventario.PierdeLaRespuesta = false;
        var reintento = await e.Recibir(devolucionId);

        Assert.True(reintento.EsExito, reintento.Error?.Clave);
        Assert.Equal(1, e.Inventario.Aplicaciones(devolucionId));
        Assert.Equal(1, e.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Veinte_recepciones_simultaneas_de_la_misma_devolucion_reingresan_y_reembolsan_una_sola_vez()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        for (var i = 0; i < 20; i++)
        {
            var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
            var devolucionId = await Solicitada(e, cliente, pedido.PedidoId, linea.LineaId, 1);
            Assert.True((await e.Aprobar(devolucionId)).EsExito);

            var resultados = await Task.WhenAll(Task.Run(() => e.Recibir(devolucionId)), Task.Run(() => e.Recibir(devolucionId)));

            Assert.Contains(resultados, r => r.EsExito);
            Assert.Equal(1, e.Inventario.Aplicaciones(devolucionId));
        }

        Assert.Equal(20, e.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Con_la_pasarela_caida_el_reembolso_queda_pendiente_y_la_conciliacion_lo_completa_una_sola_vez()
    {
        var e = new EscenarioDeDevolucionesConPostgres(bd);
        var (cliente, pedido, linea) = e.SembrarPedidoDeImpresos();
        var devolucionId = await Solicitada(e, cliente, pedido.PedidoId, linea.LineaId, 1);
        Assert.True((await e.Aprobar(devolucionId)).EsExito);
        e.Reembolsos.PasarelaCaida = true;

        var recibida = await e.Recibir(devolucionId);

        Assert.True(recibida.EsExito, recibida.Error?.Clave);
        Assert.Equal("recibida", await EstadoDeLaLinea(devolucionId));

        e.Reembolsos.PasarelaCaida = false;
        var conciliada = await e.Conciliar();
        var repetida = await e.Conciliar();

        Assert.True(conciliada.EsExito, conciliada.Error?.Clave);
        Assert.True(repetida.EsExito);
        Assert.Equal("reembolsada", await EstadoDeLaLinea(devolucionId));
        Assert.Equal(1, e.Reembolsos.Registrados);
    }
}
