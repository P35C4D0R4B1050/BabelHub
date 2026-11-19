using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T13 (PED-05, PED-06; AC-PED-04, AC-PED-05): un aviso al cliente y uno por sucursal responsable distinta, con evento determinista por operación, sin contacto y sin interrumpir la operación.</summary>
public sealed class AvisosDePedidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class NotificadorEspia : INotificador
    {
        public List<SolicitudDeAviso> Solicitudes { get; } = [];

        public bool Lanza { get; set; }

        public ResultadoDeLaSolicitudDeAviso Resultado { get; set; } = ResultadoDeLaSolicitudDeAviso.Creado;

        public Task<ResultadoDeLaSolicitudDeAviso> Solicitar(SolicitudDeAviso solicitud, CancellationToken cancelacion = default)
        {
            if (Lanza)
            {
                throw new InvalidOperationException("sin servicio de avisos");
            }

            Solicitudes.Add(solicitud);
            return Task.FromResult(Resultado);
        }
    }

    private static DatosDeLineaNueva Impresa(string titulo, string fuente = "LIM") => new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, null, 1, 50m, 50m, fuente, false, null, null);

    private static DatosDeLineaNueva Digital() => new(Guid.NewGuid(), Guid.NewGuid(), true, "Digital", null, 1, 20m, 20m, null, false, null, null);

    private static Pedido Crear(TipoDeEntregaDelPedido tipo, params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDeEntrega.Crear("Ana Pérez", "987654321", "Jr. Lima 123") : null;
        var envio = tipo == TipoDeEntregaDelPedido.Domicilio ? 12m : 0m;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? "LIM" : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new("t-1", Ahora), new("p-1", Ahora), lineas.Any(l => l.EsDigital) ? new("r-1", Ahora) : null, lineas, Ahora);
        return Pedido.Crear(datos).Pedido!;
    }

    [Fact]
    [Trait("Criterio", "AC-PED-04")]
    public async Task Al_pasar_a_listo_para_recojo_el_cliente_y_la_sucursal_reciben_un_aviso_cada_uno()
    {
        var notificador = new NotificadorEspia();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"), Impresa("Dos"));
        var ids = pedido.Lineas.Select(l => l.Id).ToList();

        await new AvisosDePedidos(notificador).Solicitar(pedido, "listo-para-recojo", AvisosDePedidos.HuellaDeLineas(ids), AvisosDePedidos.SucursalesResponsables(pedido, ids), default);

        Assert.Equal(2, notificador.Solicitudes.Count);
        var cliente = notificador.Solicitudes.Single(s => s.Destinatario == DestinatarioDelAviso.Cliente);
        var sucursal = notificador.Solicitudes.Single(s => s.Destinatario == DestinatarioDelAviso.Sucursal);
        Assert.Equal((Cuenta.ToString("D"), "LIM", "pedido-estado", "pedido-estado-sucursal"), (cliente.DestinatarioRef, sucursal.DestinatarioRef, cliente.Plantilla, sucursal.Plantilla));
        Assert.Equal(("ABCD234567", "listo-para-recojo"), (cliente.Campos["numeroDePedido"], cliente.Campos["estado"]));
    }

    [Fact]
    public async Task Un_pedido_de_cinco_lineas_genera_un_aviso_por_operacion_y_no_uno_por_linea()
    {
        var notificador = new NotificadorEspia();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("1"), Impresa("2"), Impresa("3"), Impresa("4"), Impresa("5"));

        await new AvisosDePedidos(notificador).Solicitar(pedido, "en-preparacion", AvisosDePedidos.HuellaDeLineas(pedido.Lineas.Select(l => l.Id)), AvisosDePedidos.SucursalesResponsables(pedido), default);

        Assert.Equal(2, notificador.Solicitudes.Count);
    }

    [Fact]
    public async Task Un_pedido_solo_digital_avisa_solo_al_cliente_y_a_domicilio_a_cada_fuente_distinta_de_la_operacion()
    {
        var digital = Crear(TipoDeEntregaDelPedido.Digital, Digital());
        var domicilio = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Lima", "LIM"), Impresa("Lima 2", "LIM"), Impresa("Cusco", "CUS"));
        var notificador = new NotificadorEspia();

        await new AvisosDePedidos(notificador).Solicitar(digital, "entregado", "creacion", AvisosDePedidos.SucursalesResponsables(digital), default);
        var soloDigital = notificador.Solicitudes.ToList();
        notificador.Solicitudes.Clear();
        var todas = domicilio.Lineas.Select(l => l.Id).ToList();
        await new AvisosDePedidos(notificador).Solicitar(domicilio, "en-preparacion", AvisosDePedidos.HuellaDeLineas(todas), AvisosDePedidos.SucursalesResponsables(domicilio, todas), default);

        Assert.Single(soloDigital);
        Assert.Equal(DestinatarioDelAviso.Cliente, soloDigital[0].Destinatario);
        Assert.Equal(3, notificador.Solicitudes.Count);
        Assert.Equal(["CUS", "LIM"], notificador.Solicitudes.Where(s => s.Destinatario == DestinatarioDelAviso.Sucursal).Select(s => s.DestinatarioRef).Order());
        // Una operación sobre solo las líneas de Cusco avisa únicamente a Cusco.
        var deCusco = domicilio.Lineas.Where(l => l.CodigoDeFuente == "CUS").Select(l => l.Id).ToList();
        Assert.Equal(["CUS"], AvisosDePedidos.SucursalesResponsables(domicilio, deCusco));
    }

    [Fact]
    public async Task El_evento_incluye_el_estado_destino_y_la_operacion_asi_preparar_y_marcar_listas_dan_dos_avisos_y_dos_operaciones_no_se_deduplican()
    {
        var notificador = new NotificadorEspia();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        var avisos = new AvisosDePedidos(notificador);

        await avisos.Solicitar(pedido, "en-preparacion", AvisosDePedidos.HuellaDeLineas(ids), ["LIM"], default);
        await avisos.Solicitar(pedido, "listo-para-recojo", AvisosDePedidos.HuellaDeLineas(ids), ["LIM"], default);
        await avisos.Solicitar(pedido, "listo-para-recojo", "otra-operacion", ["LIM"], default);

        var eventos = notificador.Solicitudes.Select(s => s.EventoId).ToList();
        Assert.Equal(eventos.Count, eventos.Distinct().Count());
        Assert.All(eventos, e => Assert.StartsWith($"pedido:{pedido.Id:N}:", e, StringComparison.Ordinal));
        Assert.Contains(eventos, e => e.Contains(":en-preparacion:", StringComparison.Ordinal));
        Assert.Contains(eventos, e => e.Contains(":listo-para-recojo:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_mismo_evento_se_repite_igual_para_que_el_Notificador_lo_deduplique()
    {
        var notificador = new NotificadorEspia();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var avisos = new AvisosDePedidos(notificador);

        await avisos.Solicitar(pedido, "anulado", "anulacion-1", ["LIM"], default);
        await avisos.Solicitar(pedido, "anulado", "anulacion-1", ["LIM"], default);

        Assert.Equal(notificador.Solicitudes[0].EventoId, notificador.Solicitudes[2].EventoId);
        Assert.Equal(notificador.Solicitudes[1].EventoId, notificador.Solicitudes[3].EventoId);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-05")]
    public async Task Si_el_Notificador_lanza_o_no_guarda_la_operacion_sigue_y_el_estado_cambia_igual()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var lanza = new NotificadorEspia { Lanza = true };
        var noGuarda = new NotificadorEspia { Resultado = ResultadoDeLaSolicitudDeAviso.NoSeGuardo };

        await new AvisosDePedidos(lanza).Solicitar(pedido, "en-preparacion", "op", ["LIM"], default);
        await new AvisosDePedidos(noGuarda).Solicitar(pedido, "en-preparacion", "op", ["LIM"], default);
        pedido.PrepararLineas([pedido.Lineas[0].Id], Ahora);

        Assert.Equal(EstadoDePedido.EnPreparacion, pedido.Estado);
    }

    [Fact]
    public async Task Ningun_aviso_lleva_el_contacto_ni_un_telefono_y_el_destinatario_es_una_referencia_opaca()
    {
        var notificador = new NotificadorEspia();
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno", "CUS"));

        await new AvisosDePedidos(notificador).Solicitar(pedido, "despachado", "envio-1", AvisosDePedidos.SucursalesResponsables(pedido), default);

        var todo = string.Join('|', notificador.Solicitudes.SelectMany(s => new[] { s.EventoId, s.DestinatarioRef }.Concat(s.Campos.Values)));
        Assert.DoesNotContain("Ana", todo, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", todo, StringComparison.Ordinal);
        Assert.DoesNotContain("Jr. Lima", todo, StringComparison.Ordinal);
        Assert.Equal(["numeroDePedido", "estado"], notificador.Solicitudes[0].Campos.Keys);
    }

    [Fact]
    public void La_huella_de_las_lineas_no_depende_del_orden_y_cambia_con_otras_lineas()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal(AvisosDePedidos.HuellaDeLineas([a, b]), AvisosDePedidos.HuellaDeLineas([b, a]));
        Assert.NotEqual(AvisosDePedidos.HuellaDeLineas([a, b]), AvisosDePedidos.HuellaDeLineas([a]));
        Assert.Equal(16, AvisosDePedidos.HuellaDeLineas([a]).Length);
    }

    [Fact]
    public void Cada_estado_del_pedido_tiene_su_nombre_estable()
    {
        Assert.Equal(
            ["confirmado", "en-transito", "recibido", "en-preparacion", "listo-para-recojo", "despachado", "entregado", "no-recogido", "anulado"],
            Enum.GetValues<EstadoDePedido>().Select(AvisosDePedidos.NombreDelEstado));
    }
}
