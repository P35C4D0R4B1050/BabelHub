using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T21 (PED-24; AC-PED-19): reintentar el reembolso de una anulación sin repetir los pasos ya hechos.</summary>
public sealed class ReintentarReembolsoDeAnulacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno(ActorDePrueba? actor = null)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public ReembolsosFalsos Reembolsos { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public ReintentarReembolsoDeAnulacion Caso => new(
            new AutorizadorDePedidos(actor ?? ActorDePrueba.DeAdministradorCentral(), Denegaciones, new ResolucionDeSucursalFalsa()),
            new ControlesDeGestionDePedidos(new LimitesPermisivosDePedidos(), new EstadoDeCuentaFalso(), Repositorio),
            Repositorio,
            Reembolsos,
            Auditoria);
    }

    private static string Numero() => "ABCD2345" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Random.Shared.Next(24)] + "K";

    /// <summary>Un pedido ya anulado (la anulación se completó y el reembolso quedó pendiente en Pagos).</summary>
    private static (PedidoDeDominio Pedido, Guid ReembolsoId) Anulado(Entorno entorno, EstadoDelReembolso estado = EstadoDelReembolso.Pendiente, OrigenDelReembolso origen = OrigenDelReembolso.Anulacion)
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Uno", null, 1, 50m, 50m, "LIM", false, null, null);
        var pedido = PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", Ahora), new("p-1", Ahora), null, [linea], Ahora)).Pedido!;
        pedido.AplicarAnulacion(pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado).Plan!, Ahora);
        entorno.Repositorio.Poner(pedido);
        var reembolsoId = Guid.NewGuid();
        var clave = OrquestadorDeAnulaciones.ClaveDelReembolso(origen == OrigenDelReembolso.Anulacion ? OrigenDeLaAnulacion.Administracion : OrigenDeLaAnulacion.Cliente, pedido.Id);
        entorno.Reembolsos.Pendientes[clave] = new ReembolsoPendienteVista(reembolsoId, origen, 50m, estado, MotivoPendienteDelReembolso.SinRespuesta, Ahora, 1);
        return (pedido, reembolsoId);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Al_reintentar_se_reembolsa_una_sola_vez_se_audita_y_el_pedido_sigue_anulado_sin_repetir_pasos()
    {
        var entorno = new Entorno();
        var (pedido, reembolsoId) = Anulado(entorno);

        var resultado = await entorno.Caso.Reintentar(pedido.Id, default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoVisibleDelPedido.Anulado, 50m, false), (resultado.Valor.Estado, resultado.Valor.Reembolso, resultado.Valor.ReembolsoPendiente));
        Assert.Equal([reembolsoId], entorno.Reembolsos.Reintentados);
        Assert.Empty(entorno.Reembolsos.Registrados);
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.ReembolsoDePedidoReintentado, auditoria.Accion);
        Assert.Equal("anulacion", auditoria.Detalle["origen"]);
    }

    [Fact]
    public async Task Si_la_pasarela_sigue_sin_responder_el_reembolso_sigue_pendiente_y_el_pedido_no_cambia()
    {
        var entorno = new Entorno();
        var (pedido, _) = Anulado(entorno);
        entorno.Reembolsos.EstadoAlProcesar = null;

        var resultado = await entorno.Caso.Reintentar(pedido.Id, default);

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.ReembolsoPendiente);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, resultado.Valor.Estado);
    }

    [Fact]
    public async Task Un_reembolso_ya_hecho_responde_exito_sin_reintentar_ni_auditar_y_la_cancelacion_del_cliente_tambien_se_reintenta()
    {
        var entorno = new Entorno();
        var (hecho, _) = Anulado(entorno, EstadoDelReembolso.Reembolsado);

        var repetido = await entorno.Caso.Reintentar(hecho.Id, default);

        Assert.True(repetido.EsExito);
        Assert.False(repetido.Valor.ReembolsoPendiente);
        Assert.Empty(entorno.Reembolsos.Reintentados);
        Assert.Empty(entorno.Auditoria.Solicitudes);

        var delCliente = new Entorno();
        var (cancelado, reembolsoId) = Anulado(delCliente, origen: OrigenDelReembolso.CancelacionDelCliente);

        var reintentado = await delCliente.Caso.Reintentar(cancelado.Id, default);

        Assert.True(reintentado.EsExito);
        Assert.Equal([reembolsoId], delCliente.Reembolsos.Reintentados);
        Assert.Equal("cancelacion", delCliente.Auditoria.Solicitudes.Single().Detalle["origen"]);
    }

    [Fact]
    public async Task Sin_reembolso_o_con_el_pedido_inexistente_o_sin_ser_administracion_se_rechaza()
    {
        var entorno = new Entorno();
        var sinReembolso = PedidoDeDominio.Crear(new DatosDePedidoNuevo(
            Guid.NewGuid(), Numero(), Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", Ahora), new("p-1", Ahora), null,
            [new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Uno", null, 1, 50m, 50m, "LIM", false, null, null)], Ahora)).Pedido!;
        entorno.Repositorio.Poner(sinReembolso);

        var estadoInvalido = await entorno.Caso.Reintentar(sinReembolso.Id, default);
        var inexistente = await entorno.Caso.Reintentar(Guid.NewGuid(), default);

        Assert.Equal("pedido-estado-invalido", estadoInvalido.Error!.Clave);
        Assert.Equal("no-encontrado", inexistente.Error!.Clave);
        Assert.Single(entorno.Denegaciones.Eventos);

        foreach (var actor in new[] { ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima), ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(Cuenta) })
        {
            var otro = new Entorno(actor);
            var (pedido, _) = Anulado(otro);

            var prohibido = await otro.Caso.Reintentar(pedido.Id, default);

            Assert.Equal("prohibido", prohibido.Error!.Clave);
            Assert.Empty(otro.Reembolsos.Reintentados);
        }
    }

    [Fact]
    public async Task Sin_poder_auditar_o_consultar_a_Pagos_no_se_reintenta_nada()
    {
        var sinAuditoria = new Entorno();
        var (pedido, _) = Anulado(sinAuditoria);
        sinAuditoria.Auditoria.Caida = true;

        var resultado = await sinAuditoria.Caso.Reintentar(pedido.Id, default);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(sinAuditoria.Reembolsos.Reintentados);

        var sinPagos = new Entorno();
        var (otro, _) = Anulado(sinPagos);
        sinPagos.Reembolsos.Falla = true;

        var caido = await sinPagos.Caso.Reintentar(otro.Id, default);

        Assert.Equal("servicio-no-disponible", caido.Error!.Clave);
        Assert.Empty(sinPagos.Reembolsos.Reintentados);
    }
}
