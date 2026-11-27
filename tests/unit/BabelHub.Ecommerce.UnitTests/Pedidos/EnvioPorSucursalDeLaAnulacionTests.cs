using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>
/// T51 (PED-22, PED-44, PED-45; AC-PED-42, AC-PED-43): la confirmación copia el envío de cada sucursal sin duplicarlo, la anulación pide a Pagos el importe que guardó —también al reanudar— y lo recalcula si algo sale antes de aplicarse.
/// </summary>
public sealed class EnvioPorSucursalDeLaAnulacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno
    {
        public ActorDePrueba Actor { get; } = ActorDePrueba.DeAdministradorCentral();

        public RelojFijo Reloj { get; } = new(Ahora);

        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DespachoDeEnviosFalso Despacho { get; } = new();

        public ReservasFalsas Reservas { get; } = new();

        public ReembolsosFalsos Reembolsos { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        private AutorizadorDePedidos Autorizador => new(Actor, new DenegacionesDelCatalogoEspia(), new ResolucionDeSucursalFalsa());

        private ControlesDeGestionDePedidos Controles => new(new LimitesPermisivosDePedidos(), new EstadoDeCuentaFalso(), Repositorio);

        private OrquestadorDeAnulaciones Orquestador => new(Autorizador, Controles, Repositorio, Unidad, Despacho, Reservas, Reembolsos, new AccesosDeBibliotecaFalsos(), Auditoria, new NotificadorDePedidosEspia(), new AlertasTecnicasEspia(), new GeneradorDeIdentificadoresFalso(), Reloj);

        private EntregarLineasDelPedido Entregas => new(Autorizador, Controles, Repositorio, Unidad, Reservas, Auditoria, new NotificadorDePedidosEspia(), new GeneradorDeIdentificadoresFalso(), Reloj);

        public ConfirmarCompra Confirmar => new(Repositorio, new MarcaDeCancelacionFalsa(), new AccesosDeBibliotecaFalsos(), new GeneradorDeNumerosFalso(), new AuditoriaDelCatalogoEspia(), new NotificadorDePedidosEspia(), Reloj);

        public AnularPedido Anular => new(Autorizador, Controles, Orquestador);

        public CompletarOperacionesEnCurso Reanudacion => new(Autorizador, Repositorio, Unidad, Orquestador, Entregas, new AlertasTecnicasEspia(), Reloj);

        public async Task<Pedido> Comprar(CompraAprobada compra)
        {
            Assert.Equal(ResultadoDeConfirmarCompra.Creado, await Confirmar.Confirmar(compra));
            return Repositorio.Pedidos.Single(p => p.Id == compra.PedidoId);
        }
    }

    private static LineaDeCompraAprobada Impresa(string sucursal, decimal importe) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", null, 1, importe, importe, sucursal, false, null, null);

    /// <summary>La compra de AC-PED-42: Ayacucho (S/ 30), dos líneas de Cusco (S/ 18,50) y Huanta (S/ 25).</summary>
    private static CompraAprobada TresSucursales()
    {
        LineaDeCompraAprobada[] lineas = [Impresa("ayacucho", 50m), Impresa("cusco", 40m), Impresa("cusco", 45m), Impresa("huanta", 20m)];
        return new CompraAprobada(
            Guid.NewGuid(), Cuenta, TipoDeEntregaDeCompra.Domicilio, null, "ayacucho", 73.50m, 155m + 73.50m, new ContactoDeLaCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho"),
            [new(TextoLegalDeCompra.Terminos, "t-1", Ahora), new(TextoLegalDeCompra.Privacidad, "p-1", Ahora)], lineas,
            [new("ayacucho", 30m), new("cusco", 18.50m), new("huanta", 25m)]);
    }

    private static void Despachar(Pedido pedido, params LineaDePedido[] lineas)
    {
        var ids = lineas.Select(l => l.Id).ToList();
        pedido.PrepararLineas(ids, Ahora);
        pedido.MarcarListas(ids, Ahora);
        pedido.DespacharLineas(ids, Ahora);
    }

    // ---------- La copia al confirmar (PED-44) ----------

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public async Task Confirmar_la_compra_guarda_el_envio_de_cada_sucursal_una_vez_y_repetir_el_aviso_no_lo_cambia()
    {
        var entorno = new Entorno();
        var compra = TresSucursales();

        var pedido = await entorno.Comprar(compra);
        var repetida = await entorno.Confirmar.Confirmar(compra with { EnviosPorSucursal = [new("ayacucho", 73.50m)] });

        Assert.Equal(ResultadoDeConfirmarCompra.Creado, repetida);
        Assert.Single(entorno.Repositorio.Pedidos);
        Assert.Equal(new EnvioDeSucursal[] { new("ayacucho", 30m), new("cusco", 18.50m), new("huanta", 25m) }, pedido.EnviosPorSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public async Task Una_compra_sin_detalle_crea_el_pedido_sin_el_y_una_cuyo_detalle_no_cuadra_no_lo_crea_para_que_Pagos_compense()
    {
        var entorno = new Entorno();

        var sinDetalle = await entorno.Comprar(TresSucursales() with { EnviosPorSucursal = null });
        var incoherente = await entorno.Confirmar.Confirmar(TresSucursales() with { EnviosPorSucursal = [new("ayacucho", 1m)] });

        Assert.Null(sinDetalle.EnviosPorSucursal);
        Assert.Equal(ResultadoDeConfirmarCompra.Fallo, incoherente);
        Assert.Single(entorno.Repositorio.Pedidos);
    }

    // ---------- El envío que se devuelve (PED-45) ----------

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public async Task Anular_reembolsa_las_lineas_anuladas_y_el_envio_de_las_sucursales_que_no_enviaron()
    {
        var entorno = new Entorno();
        var pedido = await entorno.Comprar(TresSucursales());
        Despachar(pedido, pedido.Lineas[1]);
        entorno.Despacho.Resultado.Add(new ResultadoDeCancelarEnvio(Guid.NewGuid(), DestinoDelEnvioAlCancelar.Mantenido, [pedido.Lineas[1].Id]));

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal(55m, reembolso.Envio);
        Assert.Equal([50m, 45m, 20m], reembolso.Lineas.Select(l => l.Monto));
        Assert.Equal(170m, resultado.Valor.Reembolso);
        Assert.Equal(55m, Assert.Single(entorno.Repositorio.Anulaciones).Envio);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public async Task Si_una_linea_sale_antes_de_aplicarse_la_anulacion_se_recalcula_sin_el_envio_de_su_sucursal()
    {
        var entorno = new Entorno();
        var pedido = await entorno.Comprar(TresSucursales());
        Despachar(pedido, pedido.Lineas[1]);
        var deHuanta = pedido.Lineas[3].Id;
        entorno.Reservas.RespuestaPorLineas = lineas => lineas.Contains(deHuanta) ? ResultadoDeLiberarReserva.YaCerrada : ResultadoDeLiberarReserva.Liberada;

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal(30m, reembolso.Envio);
        Assert.Equal([50m, 45m], reembolso.Lineas.Select(l => l.Monto));
        Assert.Equal(125m, resultado.Valor.Reembolso);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public async Task Si_el_registro_del_reembolso_falla_la_reanudacion_pide_el_mismo_envio_guardado_una_sola_vez()
    {
        var entorno = new Entorno();
        var pedido = await entorno.Comprar(TresSucursales());
        Despachar(pedido, pedido.Lineas[1]);
        entorno.Reembolsos.FallaAlRegistrar = true;
        Assert.False((await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default)).EsExito);
        entorno.Reembolsos.FallaAlRegistrar = false;
        entorno.Reloj.Ahora += TimeSpan.FromMinutes(1);
        entorno.Actor.Establecer(ActorActual.DelSistema());

        await entorno.Reanudacion.Reanudar(default);
        await entorno.Reanudacion.Reanudar(default);

        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal(55m, reembolso.Envio);
        Assert.Equal(EstadoDeOperacion.Completada, Assert.Single(entorno.Repositorio.Anulaciones).Estado);
    }
}
