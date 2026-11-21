using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T16 (PED-11, PED-25, PED-29; AC-PED-09): la entrega de tres tiempos con el cierre por línea y su clave.</summary>
public sealed class EntregarLineasDelPedidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Actor = actor ?? ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima);
        }

        public ActorDePrueba Actor { get; }

        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public ReservasFalsas Reservas { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public LimitesPermisivosDePedidos Limites { get; } = new();

        public EntregarLineasDelPedido Caso => new(
            new AutorizadorDePedidos(Actor, Denegaciones, new ResolucionDeSucursalFalsa()),
            new ControlesDeGestionDePedidos(Limites, new EstadoDeCuentaFalso(), Repositorio),
            Repositorio,
            Unidad,
            Reservas,
            Auditoria,
            Notificador,
            new GeneradorDeIdentificadoresFalso(),
            new RelojFijo(Ahora));
    }

    private static PedidoDeDominio ListoParaRecojo(params string[] titulos)
    {
        var lineas = titulos.Select(t => new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, t, null, 1, 50m, 50m, "LIM", false, null, null)).ToArray();
        var datos = new DatosDePedidoNuevo(Guid.NewGuid(), "ABCD234567", Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m * lineas.Length, null, new("t-1", Ahora), new("p-1", Ahora), null, lineas, Ahora);
        var pedido = PedidoDeDominio.Crear(datos).Pedido!;
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        pedido.PrepararLineas(ids, Ahora);
        pedido.MarcarListas(ids, Ahora);
        return pedido;
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Entregar_cierra_cada_linea_con_su_clave_y_las_pasa_a_entregadas_con_la_fecha_y_el_pedido_queda_entregado()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno", "Dos");
        entorno.Repositorio.Poner(pedido);
        var ids = pedido.Lineas.Select(l => l.Id).ToList();

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, ids), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.Entregado, resultado.Valor.Estado);
        Assert.All(resultado.Valor.Lineas, l => Assert.Equal(ResultadoVisibleDeLinea.Aplicada, l.Resultado));
        Assert.All(pedido.Lineas, l => Assert.Equal((EstadoDeLineaImpresa.Entregado, (DateTimeOffset?)Ahora), (l.EstadoImpreso!.Value, l.EntregadaEn)));
        Assert.Equal(pedido.Lineas.Select(l => ClaveDeCierreDeLinea.Para(pedido.Id, l.Id)).Order(), entorno.Reservas.Cierres.Select(c => c.Clave).Order());
        Assert.Equal(2, entorno.Reservas.Cierres.Count);
        var entrega = Assert.Single(entorno.Repositorio.Entregas);
        Assert.Equal(EstadoDeOperacion.Completada, entrega.Estado);
        Assert.All(entrega.Lineas, l => Assert.NotNull(l.CierreConfirmadoEn));
    }

    [Fact]
    public async Task La_operacion_se_audita_al_iniciar_antes_de_llamar_al_Inventario_y_cada_linea_y_el_cambio_de_estado_al_completar()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno");
        entorno.Repositorio.Poner(pedido);

        await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal(
            [AccionesAuditables.OperacionDePedidoIniciada, AccionesAuditables.LineaDePedidoEntregada, AccionesAuditables.EstadoDePedidoCambiado],
            entorno.Auditoria.Solicitudes.Select(s => s.Accion));
        Assert.Equal("entrega", entorno.Auditoria.Solicitudes[0].Detalle["operacion"]);
        Assert.Equal(pedido.Lineas[0].Id.ToString("D"), entorno.Auditoria.Solicitudes[1].Detalle["lineaId"]);
        Assert.Equal(2, entorno.Notificador.Solicitudes.Count);
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Equal("entregado", s.Campos["estado"]));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Si_el_Inventario_no_responde_la_linea_no_cambia_la_fila_queda_fallida_y_al_reintentar_la_clave_es_la_misma()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno");
        entorno.Repositorio.Poner(pedido);
        var entrada = new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]);
        entorno.Reservas.Respuesta = _ => ResultadoDeCerrarReserva.Incierto;

        var primera = await entorno.Caso.Entregar("LIM", entrada, default);
        entorno.Reservas.Respuesta = _ => ResultadoDeCerrarReserva.YaCerrada;
        var reintento = await entorno.Caso.Entregar("LIM", entrada, default);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (primera.Error!.Categoria, primera.Error.Clave));
        Assert.Equal(EstadoDeOperacion.Fallida, entorno.Repositorio.Entregas[0].Estado);
        Assert.True(reintento.EsExito, reintento.Error?.Clave);
        Assert.Equal(entorno.Reservas.Cierres[0].Clave, entorno.Reservas.Cierres[1].Clave);
        Assert.Equal(EstadoDeLineaImpresa.Entregado, pedido.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Un_rechazo_permanente_del_Inventario_tampoco_entrega_la_linea()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno");
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.Respuesta = _ => ResultadoDeCerrarReserva.Rechazada;

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDeOperacion.Fallida, entorno.Repositorio.Entregas[0].Estado);
    }

    [Fact]
    public async Task Con_un_cierre_parcial_la_fila_sigue_en_curso_con_el_cierre_confirmado_y_la_linea_pendiente_no_cambia()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno", "Dos");
        entorno.Repositorio.Poner(pedido);
        var primera = pedido.Lineas[0].Id;
        entorno.Reservas.Respuesta = linea => linea == primera ? ResultadoDeCerrarReserva.Cerrada : ResultadoDeCerrarReserva.Incierto;

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [primera, pedido.Lineas[1].Id]), default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        var entrega = entorno.Repositorio.Entregas[0];
        Assert.Equal(EstadoDeOperacion.EnCurso, entrega.Estado);
        Assert.Equal([primera], entrega.LineasCerradas);
        // La reanudación (T22) aplica entregado a la cerrada; aquí no se cambió ninguna.
        Assert.All(pedido.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, l.EstadoImpreso));
    }

    [Fact]
    public async Task Una_linea_que_ya_salio_por_Envios_queda_fuera_y_las_demas_se_entregan()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno", "Dos");
        entorno.Repositorio.Poner(pedido);
        var salio = pedido.Lineas[0].Id;
        entorno.Reservas.Respuesta = linea => linea == salio ? ResultadoDeCerrarReserva.YaSalio : ResultadoDeCerrarReserva.Cerrada;

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, pedido.Lineas.Select(l => l.Id).ToList()), default);

        Assert.True(resultado.EsExito);
        Assert.Equal(ResultadoVisibleDeLinea.EstadoInvalido, resultado.Valor.Lineas.Single(l => l.LineaId == salio).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.Entregado, pedido.Lineas[1].EstadoImpreso);
    }

    [Fact]
    public async Task Sin_poder_auditar_la_operacion_no_hay_fila_ni_llamada_al_Inventario()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno");
        entorno.Repositorio.Poner(pedido);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Entregas);
        Assert.Empty(entorno.Reservas.Cierres);
        Assert.Equal(1, entorno.Unidad.Revertidas);
    }

    [Fact]
    public async Task Si_el_tiempo_3_falla_la_fila_sobrevive_en_curso_con_el_cierre_confirmado_y_la_linea_no_se_entrega()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno");
        entorno.Repositorio.Poner(pedido);
        // La auditoría de la operación pasa; la de la compleción cae.
        entorno.Auditoria.CaeDespuesDe = 1;

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        var entrega = Assert.Single(entorno.Repositorio.Entregas);
        Assert.Equal(EstadoDeOperacion.EnCurso, entrega.Estado);
        Assert.NotNull(entrega.Lineas[0].CierreConfirmadoEn);
        Assert.Single(entorno.Reservas.Cierres);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public async Task Una_linea_no_recogida_no_se_entrega_se_rechaza_antes_de_llamar_al_Inventario_y_no_deja_rastro_de_operacion()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Olvidado");
        Assert.Single(pedido.MarcarNoRecogidas(Ahora.AddHours(361), new HashSet<Guid>()));
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.Equal("pedido-estado-invalido", resultado.Error!.Clave);
        Assert.Empty(entorno.Reservas.Cierres);
        Assert.Empty(entorno.Repositorio.Entregas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, pedido.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Solo_se_entrega_lo_que_esta_listo_y_un_pedido_a_domicilio_o_de_otra_sucursal_es_no_encontrado()
    {
        var entorno = new Entorno();
        var sinPreparar = PedidoDeDominio.Crear(new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234568", Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", Ahora), new("p-1", Ahora), null,
            [new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Sin preparar", null, 1, 50m, 50m, "LIM", false, null, null)], Ahora)).Pedido!;
        var deAyacucho = ListoParaRecojo("Otra");
        entorno.Repositorio.Poner(sinPreparar);
        entorno.Repositorio.Poner(deAyacucho);

        var invalido = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(sinPreparar.Id, [sinPreparar.Lineas[0].Id]), default);
        var otraRuta = await entorno.Caso.Entregar("AYA", new OperarLineasEntrada(deAyacucho.Id, [deAyacucho.Lineas[0].Id]), default);

        Assert.Equal("pedido-estado-invalido", invalido.Error!.Clave);
        Assert.Equal("prohibido", otraRuta.Error!.Clave);
        Assert.Empty(entorno.Reservas.Cierres);
        Assert.Empty(entorno.Repositorio.Entregas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public async Task Una_segunda_entrega_sobre_las_mismas_lineas_mientras_hay_una_en_curso_se_rechaza()
    {
        var entorno = new Entorno();
        var pedido = ListoParaRecojo("Uno");
        entorno.Repositorio.Poner(pedido);
        var linea = pedido.Lineas[0].Id;
        entorno.Repositorio.Entregas.Add(Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [linea], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", ResolucionDeSucursalFalsa.Lima), Ahora));

        var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [linea]), default);

        Assert.Equal("pedido-operacion-en-curso", resultado.Error!.Clave);
        Assert.Empty(entorno.Reservas.Cierres);
    }

    [Fact]
    public async Task Solo_el_encargado_entrega_y_la_administracion_y_el_cliente_son_prohibido()
    {
        var pedido = ListoParaRecojo("Uno");
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeCliente(Cuenta), ActorDePrueba.DePersonalDeLogistica() })
        {
            var entorno = new Entorno(actor);
            entorno.Repositorio.Poner(pedido);

            var resultado = await entorno.Caso.Entregar("LIM", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

            Assert.Equal("prohibido", resultado.Error!.Clave);
            Assert.Empty(entorno.Reservas.Cierres);
        }
    }
}
