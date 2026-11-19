using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T22 (PED-21, PED-24; AC-PED-09, 19, 22): la reanudación de las anulaciones y entregas que quedaron en curso, solo con lo local y <c>liberar</c>, nunca <c>cerrar</c>.</summary>
public sealed class CompletarOperacionesEnCursoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Administrador = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Encargado = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static int _contador;

    private sealed class Entorno
    {
        public Entorno()
        {
            Actor = ActorDePrueba.DeAdministradorCentral(Administrador);
            Reloj = new RelojFijo(Ahora);
        }

        public ActorDePrueba Actor { get; }

        public RelojFijo Reloj { get; }

        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public DespachoDeEnviosFalso Despacho { get; } = new();

        public ReservasFalsas Reservas { get; } = new();

        public ReembolsosFalsos Reembolsos { get; } = new();

        public AccesosDeBibliotecaFalsos Accesos { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        private AutorizadorDePedidos Autorizador => new(Actor, Denegaciones, new ResolucionDeSucursalFalsa());

        private ControlesDeGestionDePedidos Controles => new(new LimitesPermisivosDePedidos(), new EstadoDeCuentaFalso(), Repositorio);

        private OrquestadorDeAnulaciones Orquestador => new(Autorizador, Controles, Repositorio, Unidad, Despacho, Reservas, Reembolsos, Accesos, Auditoria, Notificador, Alertas, new GeneradorDeIdentificadoresFalso(), Reloj);

        private EntregarLineasDelPedido Entregas => new(Autorizador, Controles, Repositorio, Unidad, Reservas, Auditoria, Notificador, new GeneradorDeIdentificadoresFalso(), Reloj);

        public AnularPedido Anular => new(Autorizador, Controles, Orquestador);

        public CompletarOperacionesEnCurso Reanudacion => new(Autorizador, Repositorio, Unidad, Orquestador, Entregas, Alertas, Reloj);

        public void ComoSistema(TimeSpan transcurrido)
        {
            Reloj.Ahora += transcurrido;
            Actor.Establecer(ActorActual.DelSistema());
        }
    }

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static PedidoDeDominio Recojo(int lineas, bool listas)
    {
        var datos = Enumerable.Range(1, lineas).Select(i => new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, $"Libro {i}", null, 1, 50m, 50m, "LIM", false, null, null)).ToArray();
        var pedido = PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m * lineas, null, new("t-1", Ahora), new("p-1", Ahora), null, datos, Ahora)).Pedido!;
        if (listas)
        {
            var ids = pedido.Lineas.Select(l => l.Id).ToList();
            pedido.PrepararLineas(ids, Ahora);
            pedido.MarcarListas(ids, Ahora);
        }

        return pedido;
    }

    private static Entrega EntregaDe(PedidoDeDominio pedido) =>
        Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [.. pedido.Lineas.Select(l => l.Id)], new IniciadorDeOperacion(Encargado, "encargado-de-sucursal", ResolucionDeSucursalFalsa.Lima), Ahora);

    // ---------- Anulaciones ----------

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Una_anulacion_cuyo_tiempo_3_cayo_se_completa_con_lo_local_y_su_compleccion_se_audita_como_sistema_con_el_iniciador()
    {
        var entorno = new Entorno();
        var pedido = Recojo(1, listas: false);
        entorno.Repositorio.Poner(pedido);
        entorno.Reembolsos.FallaAlRegistrar = true;
        var primera = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "pago-duplicado"), default);
        Assert.Equal("servicio-no-disponible", primera.Error!.Clave);
        entorno.Reembolsos.FallaAlRegistrar = false;
        entorno.ComoSistema(TimeSpan.FromMinutes(1));

        var resultado = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(1, 0, 0, 0), resultado.Valor);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDeOperacion.Completada, entorno.Repositorio.Anulaciones[0].Estado);
        Assert.Single(entorno.Reembolsos.Registrados);
        // Las reservas ya estaban liberadas: la reanudación no las libera otra vez.
        Assert.Single(entorno.Reservas.Liberaciones);
        var compleccion = entorno.Auditoria.Solicitudes.Last(s => s.Accion == AccionesAuditables.EstadoDePedidoCambiado);
        Assert.Equal(Administrador.ToString("D"), compleccion.Detalle["iniciadaPor"]);
        Assert.Equal("pago-duplicado", entorno.Auditoria.Solicitudes.Single(s => s.Accion == AccionesAuditables.PedidoAnulado).Detalle["causa"]);
        Assert.Single(entorno.Reembolsos.Procesados);
    }

    [Fact]
    public async Task Una_anulacion_cuya_liberacion_fallo_repite_liberar_y_se_completa_cuando_el_Inventario_responde()
    {
        var entorno = new Entorno();
        var pedido = Recojo(1, listas: false);
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.NoSePudo;
        await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.Liberada;
        entorno.ComoSistema(TimeSpan.FromMinutes(2));

        var resultado = await entorno.Reanudacion.Reanudar();

        Assert.Equal(1, resultado.Valor.Completadas);
        Assert.Equal(2, entorno.Reservas.Liberaciones.Count);
        Assert.Empty(entorno.Reservas.Cierres);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, pedido.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Una_operacion_cuya_espera_no_llego_no_se_toca_y_pasados_15_minutos_queda_fallida_con_una_alerta_tecnica()
    {
        var entorno = new Entorno();
        var pedido = Recojo(1, listas: false);
        entorno.Repositorio.Poner(pedido);
        entorno.Reembolsos.FallaAlRegistrar = true;
        await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);
        entorno.ComoSistema(TimeSpan.Zero);

        var antes = await entorno.Reanudacion.Reanudar();
        entorno.Reloj.Ahora += TimeSpan.FromMinutes(16);
        var despues = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(0, 0, 0, 0), antes.Valor);
        Assert.Equal(new ResultadoDeLaReanudacion(0, 0, 1, 0), despues.Valor);
        Assert.Equal(EstadoDeOperacion.Fallida, entorno.Repositorio.Anulaciones[0].Estado);
        Assert.Contains(entorno.Alertas.Alertas, a => a.Motivo == CompletarOperacionesEnCurso.AlertaOperacionFallida);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Una_anulacion_cuyas_lineas_otra_operacion_ya_anulo_se_descarta()
    {
        var entorno = new Entorno();
        var pedido = Recojo(1, listas: false);
        entorno.Repositorio.Poner(pedido);
        var plan = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!;
        entorno.Repositorio.Anulaciones.Add(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, plan, new IniciadorDeOperacion(Administrador, "administrador-central", null), Ahora));
        pedido.AplicarAnulacion(plan, Ahora);
        entorno.ComoSistema(TimeSpan.FromMinutes(1));

        var resultado = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(0, 1, 0, 0), resultado.Valor);
        Assert.Equal(EstadoDeOperacion.Descartada, entorno.Repositorio.Anulaciones[0].Estado);
        Assert.Empty(entorno.Reembolsos.Registrados);
    }

    // ---------- Entregas ----------

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Una_entrega_con_el_cierre_confirmado_pasa_las_lineas_a_entregadas_sin_llamar_a_cerrar_y_audita_como_sistema_con_el_iniciador()
    {
        var entorno = new Entorno();
        var pedido = Recojo(2, listas: true);
        var entrega = EntregaDe(pedido);
        foreach (var linea in pedido.Lineas)
        {
            entrega.ConfirmarCierre(linea.Id, ResultadoDelCierre.Cerrada, Ahora);
        }

        entorno.Repositorio.Poner(pedido);
        entorno.Repositorio.Entregas.Add(entrega);
        entorno.ComoSistema(TimeSpan.FromMinutes(1));

        var resultado = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(1, 0, 0, 0), resultado.Valor);
        Assert.All(pedido.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Entregado, l.EstadoImpreso));
        Assert.Equal(EstadoDeOperacion.Completada, entrega.Estado);
        Assert.Empty(entorno.Reservas.Cierres);
        Assert.All(entorno.Auditoria.Solicitudes.Where(s => s.Accion == AccionesAuditables.LineaDePedidoEntregada), s => Assert.Equal(Encargado.ToString("D"), s.Detalle["iniciadaPor"]));
    }

    [Fact]
    public async Task Una_entrega_parcial_aplica_entregado_a_la_linea_confirmada_nunca_cierra_la_otra_y_a_los_15_minutos_queda_fallida_con_alerta()
    {
        var entorno = new Entorno();
        var pedido = Recojo(2, listas: true);
        var entrega = EntregaDe(pedido);
        entrega.ConfirmarCierre(pedido.Lineas[0].Id, ResultadoDelCierre.Cerrada, Ahora);
        entorno.Repositorio.Poner(pedido);
        entorno.Repositorio.Entregas.Add(entrega);
        entorno.ComoSistema(TimeSpan.FromMinutes(1));

        var primera = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(0, 0, 0, 1), primera.Valor);
        Assert.Equal(EstadoDeLineaImpresa.Entregado, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, pedido.Lineas[1].EstadoImpreso);
        Assert.Equal(EstadoDeOperacion.EnCurso, entrega.Estado);
        Assert.Empty(entorno.Reservas.Cierres);

        entorno.Reloj.Ahora += TimeSpan.FromMinutes(16);
        var segunda = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(0, 0, 1, 0), segunda.Valor);
        Assert.Equal(EstadoDeOperacion.Fallida, entrega.Estado);
        Assert.Contains(entorno.Alertas.Alertas, a => a.Motivo == CompletarOperacionesEnCurso.AlertaOperacionFallida);
        Assert.Empty(entorno.Reservas.Cierres);
    }

    [Fact]
    public async Task Una_entrega_sin_ningun_cierre_confirmado_pasada_la_espera_corta_queda_fallida_sin_alerta_para_que_el_encargado_la_reintente()
    {
        var entorno = new Entorno();
        var pedido = Recojo(1, listas: true);
        var entrega = EntregaDe(pedido);
        entorno.Repositorio.Poner(pedido);
        entorno.Repositorio.Entregas.Add(entrega);
        entorno.ComoSistema(TimeSpan.FromMinutes(3));

        var resultado = await entorno.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(0, 0, 1, 0), resultado.Valor);
        Assert.Equal(EstadoDeOperacion.Fallida, entrega.Estado);
        Assert.Empty(entorno.Alertas.Alertas);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, pedido.Lineas[0].EstadoImpreso);
        Assert.Empty(entorno.Reservas.Cierres);
    }

    // ---------- Común ----------

    [Fact]
    public async Task Solo_el_sistema_reanuda_y_una_operacion_ya_resuelta_no_se_toca()
    {
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima), ActorDePrueba.DeCliente(Cuenta) })
        {
            var entorno = new Entorno();
            entorno.Actor.Establecer(actor.Actual);

            var prohibido = await entorno.Reanudacion.Reanudar();

            Assert.Equal("prohibido", prohibido.Error!.Clave);
        }

        var resuelta = new Entorno();
        var pedido = Recojo(1, listas: true);
        var entrega = EntregaDe(pedido);
        entrega.ConfirmarCierre(pedido.Lineas[0].Id, ResultadoDelCierre.Cerrada, Ahora);
        entrega.Completar(Ahora);
        resuelta.Repositorio.Poner(pedido);
        resuelta.Repositorio.Entregas.Add(entrega);
        resuelta.ComoSistema(TimeSpan.FromMinutes(3));

        var resultado = await resuelta.Reanudacion.Reanudar();

        Assert.Equal(new ResultadoDeLaReanudacion(0, 0, 0, 0), resultado.Valor);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, pedido.Lineas[0].EstadoImpreso);
    }
}
