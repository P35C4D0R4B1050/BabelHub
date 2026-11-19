using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T14 (PED-19; AC-PED-03): los estados de traslado que informa el Inventario, idempotentes por evento y con las transiciones monótonas.</summary>
public sealed class RecibirEstadoDeTrasladoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public AuditoriaDelCatalogoEspia Auditoria { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public RecibirEstadoDeTraslado Caso => new(Repositorio, new UnidadDeTrabajoDeAuditoria(), Auditoria, Alertas, Notificador, new RelojFijo(Ahora));
    }

    private static (PedidoDeDominio Pedido, LineaDePedido Con, LineaDePedido Sin) PedidoConTraslado()
    {
        var conTraslado = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Con traslado", null, 2, 50m, 100m, "CUS", true, Guid.NewGuid(), null);
        var sinTraslado = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Sin traslado", null, 1, 50m, 50m, "LIM", false, null, null);
        var datos = new DatosDePedidoNuevo(Guid.NewGuid(), "ABCD234567", Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 150m, null, new("t-1", Ahora), new("p-1", Ahora), null, [conTraslado, sinTraslado], Ahora);
        var pedido = PedidoDeDominio.Crear(datos).Pedido!;
        return (pedido, pedido.Lineas.Single(l => l.Titulo == "Con traslado"), pedido.Lineas.Single(l => l.Titulo == "Sin traslado"));
    }

    private static string Evento(Guid traslado, string estado) => $"traslado:{traslado:D}:{estado}";

    private static EstadoDeTrasladoRecibido Cuerpo(PedidoDeDominio pedido, LineaDePedido linea, EstadoDelTrasladoInformado estado, string nombre, DateOnly? llegada = null) =>
        new(Evento(linea.TrasladoId!.Value, nombre), linea.TrasladoId!.Value, pedido.Id, linea.EdicionId, linea.Cantidad, estado, llegada);

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task En_transito_y_luego_recibido_llevan_la_linea_con_su_fecha_y_solo_entonces_se_puede_preparar()
    {
        var entorno = new Entorno();
        var (pedido, con, sin) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);
        var llegada = new DateOnly(2026, 11, 25);

        var transito = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito", llegada));
        var antes = pedido.PrepararLineas([con.Id], Ahora);
        var recibido = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.Recibido, "recibido", llegada));
        var despues = pedido.PrepararLineas([con.Id], Ahora);

        Assert.True(transito.EsExito);
        Assert.True(recibido.EsExito);
        Assert.Equal(ResultadoDeOperacionDeLinea.EsperaElTraslado, antes.Single().Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, despues.Single().Resultado);
        Assert.Equal(llegada, con.FechaEstimadaDeLlegada);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, con.EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, sin.EstadoImpreso);
    }

    [Fact]
    public async Task Un_recibido_sin_transito_previo_recorre_ambos_estados()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.Recibido, "recibido"));

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLineaImpresa.Recibido, con.EstadoImpreso);
    }

    [Fact]
    public async Task El_mismo_evento_repetido_no_cambia_nada_y_con_otro_cuerpo_se_rechaza()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);
        var cuerpo = Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito", new DateOnly(2026, 11, 25));
        await entorno.Caso.Recibir(cuerpo);
        var avisosAntes = entorno.Notificador.Solicitudes.Count;

        var repetido = await entorno.Caso.Recibir(cuerpo);
        var otroCuerpo = await entorno.Caso.Recibir(cuerpo with { FechaEstimadaDeLlegada = new DateOnly(2026, 12, 1) });

        Assert.True(repetido.EsExito);
        Assert.Equal(avisosAntes, entorno.Notificador.Solicitudes.Count);
        Assert.Equal(("validacion-fallida", CategoriaDeError.Validacion), (otroCuerpo.Error!.Clave, otroCuerpo.Error.Categoria));
    }

    [Fact]
    public async Task El_evento_debe_coincidir_con_el_cuerpo_y_los_datos_deben_ser_validos()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);
        var bueno = Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito");

        var otroEstado = await entorno.Caso.Recibir(bueno with { EventoId = Evento(con.TrasladoId!.Value, "recibido") });
        var otroTraslado = await entorno.Caso.Recibir(bueno with { EventoId = Evento(Guid.NewGuid(), "en-transito") });
        var sinCantidad = await entorno.Caso.Recibir(bueno with { Cantidad = 0 });
        var vacio = await entorno.Caso.Recibir(bueno with { PedidoId = Guid.Empty });

        Assert.All(new[] { otroEstado, otroTraslado, sinCantidad, vacio }, r => Assert.Equal(CategoriaDeError.Validacion, r.Error!.Categoria));
        Assert.Empty(entorno.Repositorio.EstadosDeTraslado);
    }

    [Fact]
    public async Task El_cruce_de_edicion_o_cantidad_distinta_es_validacion_y_no_cambia_la_linea()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);

        var edicion = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito") with { EdicionId = Guid.NewGuid() });
        var cantidad = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito") with { Cantidad = 9 });

        Assert.Equal(CategoriaDeError.Validacion, edicion.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, cantidad.Error!.Categoria);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, con.EstadoImpreso);
    }

    [Fact]
    public async Task Un_pedido_o_un_traslado_inexistente_es_no_encontrado_permanente_con_alerta_tecnica()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);

        var pedidoAjeno = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito") with { PedidoId = Guid.NewGuid() });
        var trasladoAjeno = Guid.NewGuid();
        var sinLinea = await entorno.Caso.Recibir(new EstadoDeTrasladoRecibido(Evento(trasladoAjeno, "en-transito"), trasladoAjeno, pedido.Id, con.EdicionId, 2, EstadoDelTrasladoInformado.EnTransito, null));

        Assert.Equal(CategoriaDeError.NoEncontrado, pedidoAjeno.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, sinLinea.Error!.Categoria);
        Assert.Equal(2, entorno.Alertas.Alertas.Count(a => a.Motivo == RecibirEstadoDeTraslado.AlertaPedidoInexistente));
    }

    [Fact]
    public async Task Una_linea_con_una_operacion_en_curso_responde_servicio_no_disponible_reintentable()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);
        entorno.Repositorio.Anulaciones.Add(Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, new PlanDeAnulacion(OrigenDeLaAnulacion.Administracion, MotivoDeAnulacion.Otro, [con.Id], [], false, 100m, []), new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora));

        var resultado = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito"));

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, con.EstadoImpreso);
        Assert.Empty(entorno.Repositorio.EstadosDeTraslado);
    }

    [Fact]
    public async Task Una_linea_anulada_responde_sin_efecto_con_alerta_y_el_cancelado_de_la_propia_liberacion_no_genera_alerta()
    {
        var entorno = new Entorno();
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);
        pedido.AplicarAnulacion(pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, Ahora);

        var enTransito = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito"));
        var cancelado = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.Cancelado, "cancelado"));

        Assert.True(enTransito.EsExito);
        Assert.True(cancelado.EsExito);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, con.EstadoImpreso);
        Assert.Equal([RecibirEstadoDeTraslado.AlertaLineaAnulada], entorno.Alertas.Alertas.Select(a => a.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-04")]
    public async Task Si_cambia_el_estado_del_pedido_se_audita_y_se_avisa_al_cliente_y_a_la_sucursal_con_el_evento_del_traslado()
    {
        var entorno = new Entorno();
        var datos = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Solo", null, 1, 50m, 50m, "CUS", true, Guid.NewGuid(), null);
        var pedido = PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), "ABCD234567", Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", Ahora), new("p-1", Ahora), null, [datos], Ahora)).Pedido!;
        entorno.Repositorio.Poner(pedido);
        var linea = pedido.Lineas[0];

        await entorno.Caso.Recibir(Cuerpo(pedido, linea, EstadoDelTrasladoInformado.EnTransito, "en-transito"));

        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(("confirmado", "en-transito"), (solicitud.Detalle["estadoAnterior"], solicitud.Detalle["estadoNuevo"]));
        Assert.Equal(2, entorno.Notificador.Solicitudes.Count);
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Contains(linea.TrasladoId!.Value.ToString("D"), s.EventoId, StringComparison.Ordinal));
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Equal("en-transito", s.Campos["estado"]));
    }

    [Fact]
    public async Task Si_el_Notificador_lanza_el_estado_cambia_igual_y_el_evento_queda_registrado()
    {
        var entorno = new Entorno();
        entorno.Notificador.Lanza = true;
        var (pedido, con, _) = PedidoConTraslado();
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Caso.Recibir(Cuerpo(pedido, con, EstadoDelTrasladoInformado.EnTransito, "en-transito"));

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLineaImpresa.EnTransito, con.EstadoImpreso);
        Assert.Single(entorno.Repositorio.EstadosDeTraslado);
    }
}
