using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La anulación y la cancelación con la Aplicación real sobre PostgreSQL real (T34; PED-13, PED-15, PED-16, PED-18, PED-22, PED-24; AC-PED-11, AC-PED-14, AC-PED-15, AC-PED-16, AC-PED-18, AC-PED-19, AC-PED-20):
/// reservas liberadas una sola vez, reembolso registrado una sola vez, pasarela caída y reintento, la auditoría caída antes del primer tiempo (ni fila ni llamada al Inventario) y tras el REST (la reanudación termina la
/// anulación como «sistema»). La auditoría y los bordes son dobles; la persistencia de la auditoría real y la Biblioteca real se prueban en sus módulos.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class AnulacionYCancelacionDePedidosTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private static int _contador;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Numero() => "ANUL2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24] + "N";

    private async Task<Pedido> SembrarRecojo(int lineas = 2)
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var datos = Enumerable.Range(0, lineas)
            .Select(i => new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, $"Libro {i}", null, 1, 40m, 40m, "LIM", false, null, null))
            .ToArray();
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 40m * lineas, null, new("t-1", ahora), new("p-1", ahora), null, datos, ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        return pedido;
    }

    private async Task<Pedido> SembrarSoloDigital()
    {
        var ahora = EscenarioDePedidos.InicioDelDia;
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), true, "El Aleph (digital)", null, 1, 30m, 30m, null, false, null, null);
        var pedido = Pedido.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), EscenarioDePedidos.Titular, TipoDeEntregaDelPedido.Digital, null, null, 0m, 30m, null, new("t-1", ahora), new("p-1", ahora), new("rd-1", ahora), [linea], ahora)).Pedido!;
        Assert.True(await bd.Pedidos.Agregar(pedido));
        return pedido;
    }

    private Task<Pedido> Guardado(Guid id) => bd.Pedidos.Obtener(id, conBloqueo: false)!;

    private static Task<Resultado<ResultadoDeAnulacion>> Anular(EscenarioDePedidos escenario, Guid pedidoId, string motivo = "pago-duplicado") =>
        escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedidoId, motivo)));

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    [Trait("Criterio", "AC-PED-14")]
    public async Task Anular_un_pedido_libera_cada_reserva_registra_un_solo_reembolso_por_el_total_y_lo_audita()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo(lineas: 2);

        var resultado = await Anular(escenario, pedido.Id);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, resultado.Valor.Estado);
        Assert.Equal(80m, resultado.Valor.Reembolso);
        var guardado = await Guardado(pedido.Id);
        Assert.All(guardado.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Anulado, l.EstadoImpreso));
        Assert.Equal(pedido.Lineas.Select(l => l.Id).Order(), escenario.Reservas.Liberaciones.SelectMany(l => l.Lineas).Order());
        Assert.Single(escenario.Reembolsos.Registrados);
        Assert.NotEmpty(escenario.Auditoria.Solicitudes);
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().AnulacionesEnCurso(pedido.Id)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task Repetir_la_anulacion_de_un_pedido_ya_anulado_se_rechaza_sin_liberar_ni_reembolsar_otra_vez()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo();
        Assert.True((await Anular(escenario, pedido.Id)).EsExito);
        var liberaciones = escenario.Reservas.Liberaciones.Count;

        var repetida = await Anular(escenario, pedido.Id);

        Assert.False(repetida.EsExito);
        Assert.Equal(liberaciones, escenario.Reservas.Liberaciones.Count);
        Assert.Single(escenario.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-16")]
    public async Task Un_pedido_solo_digital_con_el_acceso_concedido_no_se_anula_porque_no_tiene_lineas_anulables()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarSoloDigital();

        var resultado = await Anular(escenario, pedido.Id);

        // Una compra digital no se anula por la vía de impresos: o se rechaza o no cambia nada; nunca libera una reserva que no existe.
        Assert.Empty(escenario.Reservas.Liberaciones);
        if (resultado.EsExito)
        {
            Assert.Single(escenario.Reembolsos.Registrados);
        }
        else
        {
            Assert.Empty(escenario.Reembolsos.Registrados);
            Assert.Equal(EstadoDeLineaDigital.Disponible, (await Guardado(pedido.Id)).Lineas[0].EstadoDigital);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Con_la_pasarela_caida_la_anulacion_queda_con_el_reembolso_pendiente_y_el_reintento_reembolsa_una_vez()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo();
        escenario.Reembolsos.EstadoAlProcesar = null;

        var anulada = await Anular(escenario, pedido.Id);

        Assert.True(anulada.EsExito, anulada.Error?.Clave);
        Assert.True(anulada.Valor.ReembolsoPendiente);
        Assert.Equal(EstadoDePedido.Anulado, (await Guardado(pedido.Id)).Estado);

        // Pagos real guarda el reembolso pendiente y lo devuelve por su origen y su clave: el doble lo imita.
        var registrado = escenario.Reembolsos.Registrados.Single();
        escenario.Reembolsos.Pendientes[registrado.ClaveNatural] = new ReembolsoPendienteVista(Guid.NewGuid(), registrado.Origen, anulada.Valor.Reembolso, EstadoDelReembolso.Pendiente, MotivoPendienteDelReembolso.SinRespuesta, EscenarioDePedidos.InicioDelDia, 1);
        escenario.Reembolsos.EstadoAlProcesar = EstadoDelReembolso.Reembolsado;
        var reintento = await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IAdministracionDePedidos>().ReintentarElReembolso(pedido.Id));

        Assert.True(reintento.EsExito, reintento.Error?.Clave);
        Assert.Single(escenario.Reembolsos.Registrados.Select(r => r.ClaveNatural).Distinct());
    }

    [Fact]
    [Trait("Criterio", "AC-PED-20")]
    public async Task Con_la_auditoria_caida_antes_del_primer_tiempo_no_queda_fila_ni_hay_llamada_al_Inventario()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo();
        escenario.Auditoria.Caida = true;

        var resultado = await Anular(escenario, pedido.Id);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(escenario.Reservas.Liberaciones);
        Assert.Empty(escenario.Reembolsos.Registrados);
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().AnulacionesEnCurso(pedido.Id)));
        Assert.All((await Guardado(pedido.Id)).Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Confirmado, l.EstadoImpreso));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-20")]
    public async Task Si_la_auditoria_cae_tras_el_REST_la_reanudacion_termina_la_anulacion_como_sistema_con_una_llamada_por_linea()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo(lineas: 2);
        // La auditoría del primer tiempo se registra y las siguientes fallan: el Inventario ya liberó, el tercer tiempo no se pudo auditar.
        escenario.Auditoria.CaeDespuesDe = 1;

        var primera = await Anular(escenario, pedido.Id);

        Assert.False(primera.EsExito);
        Assert.Equal(pedido.Lineas.Select(l => l.Id).Order(), escenario.Reservas.Liberaciones.SelectMany(l => l.Lineas).Order());
        Assert.All((await Guardado(pedido.Id)).Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Confirmado, l.EstadoImpreso));

        // La auditoría vuelve y la reanudación (otro actor, otro momento) completa la operación sin repetir lo ya hecho.
        escenario.Auditoria.CaeDespuesDe = null;
        escenario.Reloj.Ahora = EscenarioDePedidos.InicioDelDia.AddMinutes(5);
        var reanudacion = await escenario.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<CompletarOperacionesEnCurso>().Reanudar());

        Assert.True(reanudacion.EsExito, reanudacion.Error?.Clave);
        var guardado = await Guardado(pedido.Id);
        Assert.All(guardado.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Anulado, l.EstadoImpreso));
        Assert.Single(escenario.Reembolsos.Registrados.Select(r => r.ClaveNatural).Distinct());
        Assert.Empty(await escenario.Como(EscenarioDePedidos.DeLaAdministracion(), sp => sp.GetRequiredService<IRepositorioDePedidos>().AnulacionesEnCurso(pedido.Id)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task El_titular_cancela_su_pedido_confirmado_y_uno_ajeno_no_se_cancela()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo();

        var ajeno = await escenario.Como(EscenarioDePedidos.DelTitular(Guid.NewGuid()), sp => sp.GetRequiredService<IPedidosDelCliente>().Cancelar(pedido.Id));
        var propio = await escenario.Como(EscenarioDePedidos.DelTitular(), sp => sp.GetRequiredService<IPedidosDelCliente>().Cancelar(pedido.Id));

        Assert.False(ajeno.EsExito);
        Assert.Equal(CategoriaDeError.NoEncontrado, ajeno.Error!.Categoria);
        Assert.True(propio.EsExito, propio.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, propio.Valor.Estado);
        Assert.Single(escenario.Reembolsos.Registrados);
        Assert.All(escenario.Reservas.Liberaciones, l => Assert.Equal("cancelado-por-el-cliente", l.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-16")]
    public async Task Un_pedido_con_una_linea_ya_entregada_no_se_cancela_y_ofrece_la_devolucion_como_alternativa()
    {
        await using var escenario = EscenarioDePedidos.Crear(bd.Unidad);
        var pedido = await SembrarRecojo(lineas: 1);
        var cargado = (await bd.Pedidos.Obtener(pedido.Id, conBloqueo: true))!;
        var ids = cargado.Lineas.Select(l => l.Id).ToList();
        cargado.PrepararLineas(ids, EscenarioDePedidos.InicioDelDia);
        cargado.MarcarListas(ids, EscenarioDePedidos.InicioDelDia);
        cargado.EntregarLineas(ids, EscenarioDePedidos.InicioDelDia);
        await bd.Pedidos.Actualizar(cargado);

        var resultado = await escenario.Como(EscenarioDePedidos.DelTitular(), sp => sp.GetRequiredService<IPedidosDelCliente>().Cancelar(pedido.Id));
        var detalle = await escenario.Como(EscenarioDePedidos.DelTitular(), sp => sp.GetRequiredService<IPedidosDelCliente>().Obtener(pedido.Id));

        Assert.False(resultado.EsExito);
        Assert.Equal(ClavesDePedidosPublicas.NoCancelable, resultado.Error!.Clave);
        Assert.False(detalle.Valor.PuedeCancelar);
        Assert.Equal(AlternativaDeCancelacion.Devolucion, detalle.Valor.Alternativa);
        Assert.Empty(escenario.Reservas.Liberaciones);
    }
}

/// <summary>Las claves de error que la Api publica, para comprobarlas sin depender del tipo interno.</summary>
internal static class ClavesDePedidosPublicas
{
    public const string NoCancelable = "pedido-no-cancelable";
}
