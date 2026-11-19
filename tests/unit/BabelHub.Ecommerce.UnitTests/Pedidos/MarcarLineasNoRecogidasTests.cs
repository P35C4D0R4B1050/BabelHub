using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T18 (PED-15, PED-16; AC-PED-13, 14): la revisión horaria de plazos de recojo y la liberación de las reservas.</summary>
public sealed class MarcarLineasNoRecogidasTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno(ActorDePrueba? actor = null)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public ReservasFalsas Reservas { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public MarcarLineasNoRecogidas Caso => new(
            new AutorizadorDePedidos(actor ?? ActorDePrueba.DelSistema(), Denegaciones, new ResolucionDeSucursalFalsa()),
            Repositorio,
            Unidad,
            Reservas,
            Auditoria,
            Notificador,
            Alertas,
            new RelojFijo(Ahora));
    }

    private static int _contador;

    private static string SiguienteNumero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    /// <summary>Un pedido de recojo con una línea por título, todas listas desde <paramref name="listoHace"/>.</summary>
    private static PedidoDeDominio ListoDesde(TimeSpan listoHace, params string[] titulos)
    {
        var instante = Ahora - listoHace;
        var lineas = titulos.Select(t => new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, t, null, 1, 50m, 50m, "LIM", false, null, null)).ToArray();
        var pedido = PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), SiguienteNumero(), Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m * lineas.Length, null, new("t-1", instante), new("p-1", instante), null, lineas, instante)).Pedido!;
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        pedido.PrepararLineas(ids, instante);
        pedido.MarcarListas(ids, instante);
        return pedido;
    }

    [Fact]
    [Trait("Criterio", "AC-PED-13")]
    public async Task Una_linea_con_15_dias_y_1_hora_pasa_a_no_recogida_libera_su_reserva_y_avisa_y_una_de_14_dias_no_cambia()
    {
        var entorno = new Entorno();
        var vencido = ListoDesde(TimeSpan.FromDays(15) + TimeSpan.FromHours(1), "Vencido");
        var vigente = ListoDesde(TimeSpan.FromDays(14), "Vigente");
        entorno.Repositorio.Poner(vencido);
        entorno.Repositorio.Poner(vigente);

        var resultado = await entorno.Caso.Revisar();

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(new ResultadoDeLaRevisionDePlazos(1, 1, 0, 1, 0), resultado.Valor);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, vencido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDePedido.NoRecogido, vencido.Estado);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, vigente.Lineas[0].EstadoImpreso);
        var liberacion = Assert.Single(entorno.Reservas.Liberaciones);
        Assert.Equal((vencido.Id, "no-recogido"), (liberacion.Pedido, liberacion.Motivo));
        Assert.Equal([vencido.Lineas[0].Id], liberacion.Lineas);
        Assert.Empty(entorno.Repositorio.LiberacionesDeReserva);
        // Un aviso al cliente y otro a la sucursal, con el estado «no recogido».
        Assert.Equal(2, entorno.Notificador.Solicitudes.Count);
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Equal("no-recogido", s.Campos["estado"]));
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.EstadoDePedidoCambiado, auditoria.Accion);
    }

    [Fact]
    public async Task Repetir_la_revision_no_cambia_nada_ni_avisa_ni_libera_dos_veces()
    {
        var entorno = new Entorno();
        var pedido = ListoDesde(TimeSpan.FromDays(16), "Uno", "Dos");
        entorno.Repositorio.Poner(pedido);

        await entorno.Caso.Revisar();
        var repetida = await entorno.Caso.Revisar();

        Assert.Equal(new ResultadoDeLaRevisionDePlazos(0, 0, 0, 0, 0), repetida.Valor);
        Assert.Single(entorno.Reservas.Liberaciones);
        Assert.Equal(2, entorno.Reservas.Liberaciones[0].Lineas.Count);
        Assert.Equal(2, entorno.Notificador.Solicitudes.Count);
    }

    [Fact]
    public async Task Una_linea_con_una_operacion_en_curso_se_omite_y_las_demas_se_marcan()
    {
        var entorno = new Entorno();
        var pedido = ListoDesde(TimeSpan.FromDays(16), "Uno", "Dos");
        entorno.Repositorio.Poner(pedido);
        var enCurso = pedido.Lineas[0].Id;
        entorno.Repositorio.Entregas.Add(Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [enCurso], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", null), Ahora));

        var resultado = await entorno.Caso.Revisar();

        Assert.Equal(1, resultado.Valor.LineasMarcadas);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, pedido.Lineas[1].EstadoImpreso);
        Assert.Equal([pedido.Lineas[1].Id], entorno.Reservas.Liberaciones.Single().Lineas);
    }

    [Fact]
    public async Task Si_el_Inventario_no_responde_la_liberacion_queda_anotada_con_un_intento_y_la_corrida_siguiente_la_repite()
    {
        var entorno = new Entorno();
        var pedido = ListoDesde(TimeSpan.FromDays(16), "Uno");
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.NoSePudo;

        var primera = await entorno.Caso.Revisar();
        var anotada = Assert.Single(entorno.Repositorio.LiberacionesDeReserva);
        Assert.Equal(new ResultadoDeLaRevisionDePlazos(1, 1, 0, 0, 1), primera.Valor);
        Assert.Equal((pedido.Lineas[0].Id, 1), (anotada.LineaId, anotada.Intentos));
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, pedido.Lineas[0].EstadoImpreso);

        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.Liberada;
        var segunda = await entorno.Caso.Revisar();

        Assert.Equal(new ResultadoDeLaRevisionDePlazos(0, 0, 0, 1, 0), segunda.Valor);
        Assert.Empty(entorno.Repositorio.LiberacionesDeReserva);
        Assert.Equal(2, entorno.Reservas.Liberaciones.Count);
    }

    [Fact]
    public async Task Un_409_de_liberar_es_un_cierre_confirmado_borra_la_fila_y_publica_una_alerta_tecnica()
    {
        var entorno = new Entorno();
        var pedido = ListoDesde(TimeSpan.FromDays(16), "Uno");
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.YaCerrada;

        var resultado = await entorno.Caso.Revisar();

        Assert.Equal(1, resultado.Valor.LiberacionesHechas);
        Assert.Empty(entorno.Repositorio.LiberacionesDeReserva);
        Assert.Contains(entorno.Alertas.Alertas, a => a.Motivo == MarcarLineasNoRecogidas.AlertaLiberacionYaCerrada);
    }

    [Fact]
    public async Task Sin_poder_auditar_el_pedido_se_revierte_no_se_libera_nada_y_se_reintenta_en_la_corrida_siguiente()
    {
        var entorno = new Entorno();
        var pedido = ListoDesde(TimeSpan.FromDays(16), "Uno");
        entorno.Repositorio.Poner(pedido);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.Caso.Revisar();

        Assert.Equal(new ResultadoDeLaRevisionDePlazos(1, 0, 1, 0, 0), resultado.Valor);
        Assert.Equal(1, entorno.Unidad.Revertidas);
        Assert.Empty(entorno.Reservas.Liberaciones);
        Assert.Empty(entorno.Notificador.Solicitudes);
    }

    [Fact]
    public async Task Solo_el_sistema_puede_correr_la_revision()
    {
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima), ActorDePrueba.DeCliente(Cuenta) })
        {
            var entorno = new Entorno(actor);
            entorno.Repositorio.Poner(ListoDesde(TimeSpan.FromDays(16), "Uno"));

            var resultado = await entorno.Caso.Revisar();

            Assert.Equal("prohibido", resultado.Error!.Clave);
            Assert.Empty(entorno.Reservas.Liberaciones);
        }
    }
}
