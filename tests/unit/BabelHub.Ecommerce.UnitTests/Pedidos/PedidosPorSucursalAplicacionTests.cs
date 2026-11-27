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

/// <summary>
/// T45 (PED-05, PED-08, PED-22, PED-42, PED-43; AC-PED-37, AC-PED-38, AC-PED-40): el encargado ve y opera solo las líneas de su sucursal, cada aviso va a la sucursal de su línea y, sin nada salido, se reembolsan todos los envíos.
/// </summary>
public sealed class PedidosPorSucursalAplicacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Entorno(ActorDePrueba actor)
    {
        public RepositorioDePedidosFalso Repositorio { get; } = new();

        public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

        public LimitesPermisivosDePedidos Limites { get; } = new();

        public UnidadDeTrabajoEspia Unidad { get; } = new();

        public NotificadorDePedidosEspia Notificador { get; } = new();

        public ReservasFalsas Reservas { get; } = new();

        public ReembolsosFalsos Reembolsos { get; } = new();

        public AuditoriaDePedidosEspia Auditoria { get; } = new();

        public DespachoDeEnviosFalso Despacho { get; } = new();

        private AutorizadorDePedidos Autorizador => new(actor, Denegaciones, new ResolucionDeSucursalFalsa());

        private ControlesDeGestionDePedidos Controles => new(Limites, new EstadoDeCuentaFalso(), Repositorio);

        public ConsultasDePedidos Consultas => new(Autorizador, Controles, Repositorio, Reembolsos, new AuditoriaDeContactoFalsa());

        public OperarLineasDelPedido Operar => new(Autorizador, Controles, Repositorio, Unidad, Despacho, Auditoria, Notificador, new RelojFijo(Ahora));

        public EntregarLineasDelPedido Entregar => new(Autorizador, Controles, Repositorio, Unidad, Reservas, Auditoria, Notificador, new GeneradorDeIdentificadoresFalso(), new RelojFijo(Ahora));

        private OrquestadorDeAnulaciones Orquestador => new(Autorizador, Controles, Repositorio, Unidad, Despacho, Reservas, Reembolsos, new AccesosDeBibliotecaFalsos(), Auditoria, Notificador, new AlertasTecnicasEspia(), new GeneradorDeIdentificadoresFalso(), new RelojFijo(Ahora));

        public CancelarPedidoDelCliente Cancelar => new(Autorizador, Controles, Orquestador);

        public AnularPedido Anular => new(Autorizador, Controles, Orquestador);
    }

    private static DatosDeLineaNueva Impresa(string titulo, string fuente, decimal precio = 50m, int cantidad = 1, bool traslado = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, null, cantidad, precio, precio * cantidad, fuente, traslado, traslado ? Guid.NewGuid() : null, null);

    private static DatosDeLineaNueva Digital() => new(Guid.NewGuid(), Guid.NewGuid(), true, "Digital", null, 1, 20m, 20m, null, false, null, null);

    private static PedidoDeDominio Crear(TipoDeEntregaDelPedido tipo, decimal envio, string? sucursalComun, params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Cuenta, tipo, sucursalComun, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new("t-1", Ahora), new("p-1", Ahora), lineas.Any(l => l.EsDigital) ? new("rd-1", Ahora) : null, lineas, Ahora);
        return PedidoDeDominio.Crear(datos).Pedido!;
    }

    /// <summary>Un recojo por sucursal: una línea de Ayacucho (S/ 80) lista y otra de Cusco (S/ 50) confirmada.</summary>
    private static (PedidoDeDominio Pedido, Guid DeAyacucho, Guid DeCusco) RecojoDeDosSucursales()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, null, Impresa("Libro de Ayacucho", "AYA", 40m, 2), Impresa("Libro de Cusco", "CUS"));
        var (ayacucho, cusco) = (pedido.Lineas[0].Id, pedido.Lineas[1].Id);
        pedido.PrepararLineas([ayacucho], Ahora);
        pedido.MarcarListas([ayacucho], Ahora);
        return (pedido, ayacucho, cusco);
    }

    private static ActorDePrueba Encargado(Guid sucursal) => ActorDePrueba.DeEncargadoDe(sucursal);

    // ---------- PED-08: lo que ve el encargado ----------

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task Cada_encargado_lista_y_abre_el_pedido_con_solo_sus_lineas_su_estado_y_su_importe()
    {
        var (pedido, deAyacucho, deCusco) = RecojoDeDosSucursales();

        foreach (var (sucursal, codigo, linea, estado, importe) in new[]
        {
            (ResolucionDeSucursalFalsa.Ayacucho, "AYA", deAyacucho, EstadoVisibleDelPedido.ListoParaRecojo, 80m),
            (ResolucionDeSucursalFalsa.Cusco, "CUS", deCusco, EstadoVisibleDelPedido.Confirmado, 50m),
        })
        {
            var entorno = new Entorno(Encargado(sucursal));
            entorno.Repositorio.Poner(pedido);

            var lista = await entorno.Consultas.ListarDeRecojo(codigo, new FiltroDePedidos(), default);
            var detalle = await entorno.Consultas.ObtenerParaLaSucursal(codigo, pedido.Id, default);

            var fila = Assert.Single(lista.Valor.Pedidos);
            Assert.Equal((estado, importe, 1, codigo), (fila.Estado, fila.Total, fila.CantidadDeLineas, fila.CodigoDeSucursalDeRecojo));
            Assert.Equal(fila, detalle.Valor.Resumen);
            var visible = Assert.Single(detalle.Valor.Lineas);
            Assert.Equal((linea, codigo), (visible.LineaId, visible.CodigoDeSucursal));
            Assert.Empty(entorno.Denegaciones.Eventos);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task Una_sucursal_sin_lineas_en_el_pedido_no_lo_lista_y_al_pedirlo_recibe_el_rechazo_registrado()
    {
        var (pedido, _, _) = RecojoDeDosSucursales();
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Lima));
        entorno.Repositorio.Poner(pedido);

        var lista = await entorno.Consultas.ListarDeRecojo("LIM", new FiltroDePedidos(), default);
        var detalle = await entorno.Consultas.ObtenerParaLaSucursal("LIM", pedido.Id, default);

        Assert.Empty(lista.Valor.Pedidos);
        Assert.Equal("no-encontrado", detalle.Error!.Clave);
        Assert.Equal(["recurso-ajeno"], entorno.Denegaciones.Eventos.Select(e => e.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task El_filtro_de_estado_del_encargado_compara_el_estado_de_sus_lineas()
    {
        var (pedido, _, _) = RecojoDeDosSucursales();
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Ayacucho));
        entorno.Repositorio.Poner(pedido);

        var listos = await entorno.Consultas.ListarDeRecojo("AYA", new FiltroDePedidos(EstadoVisibleDelPedido.ListoParaRecojo), default);
        var confirmados = await entorno.Consultas.ListarDeRecojo("AYA", new FiltroDePedidos(EstadoVisibleDelPedido.Confirmado), default);

        Assert.Single(listos.Valor.Pedidos);
        Assert.Empty(confirmados.Valor.Pedidos);
        Assert.Equal(EstadoDePedido.Confirmado, pedido.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task El_titular_ve_todas_las_lineas_con_la_sucursal_que_atiende_cada_impresa()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, null, Impresa("Uno", "AYA"), Impresa("Dos", "CUS"), Digital());
        var entorno = new Entorno(ActorDePrueba.DeCliente(Cuenta));
        entorno.Repositorio.Poner(pedido);

        var detalle = await entorno.Consultas.ObtenerParaElCliente(pedido.Id, default);

        Assert.Equal(new[] { "AYA", "CUS", null }, detalle.Valor.Lineas.Select(l => l.CodigoDeSucursal));
        Assert.Equal((120m, 3, null), (detalle.Valor.Resumen.Total, detalle.Valor.Resumen.CantidadDeLineas, detalle.Valor.Resumen.CodigoDeSucursalDeRecojo));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public async Task En_un_pedido_anterior_el_encargado_de_la_sucursal_comun_ve_sus_impresas_aunque_vengan_de_otra_fuente_y_no_las_digitales()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, "AYA", Impresa("Por traslado", "CUS", traslado: true), Impresa("Propia", "AYA"), Digital());
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Ayacucho));
        entorno.Repositorio.Poner(pedido);

        var detalle = await entorno.Consultas.ObtenerParaLaSucursal("AYA", pedido.Id, default);

        Assert.Equal(new[] { "AYA", "AYA" }, detalle.Valor.Lineas.Select(l => l.CodigoDeSucursal));
        Assert.Equal((100m, 2), (detalle.Valor.Resumen.Total, detalle.Valor.Resumen.CantidadDeLineas));
        var deLaFuente = new Entorno(Encargado(ResolucionDeSucursalFalsa.Cusco));
        deLaFuente.Repositorio.Poner(pedido);
        Assert.Equal("no-encontrado", (await deLaFuente.Consultas.ObtenerParaLaSucursal("CUS", pedido.Id, default)).Error!.Clave);
    }

    // ---------- PED-05: los avisos van a la sucursal de cada línea ----------

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task El_aviso_de_la_linea_de_Ayacucho_va_al_cliente_y_a_Ayacucho_y_no_a_Cusco()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, null, Impresa("Uno", "AYA"), Impresa("Dos", "CUS"));
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Ayacucho));
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Operar.PrepararDeRecojo("AYA", new OperarLineasEntrada(pedido.Id, [pedido.Lineas[0].Id]), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(new[] { Cuenta.ToString("D"), "AYA" }, entorno.Notificador.Solicitudes.Select(s => s.DestinatarioRef));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public void La_anulacion_avisa_a_cada_sucursal_de_las_lineas_anuladas()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, null, Impresa("Uno", "AYA"), Impresa("Dos", "CUS"), Impresa("Tres", "AYA"), Digital());

        Assert.Equal(new[] { "AYA", "CUS" }, AvisosDePedidos.SucursalesResponsables(pedido, [.. pedido.Lineas.Select(l => l.Id)]));
        Assert.Equal(new[] { "CUS" }, AvisosDePedidos.SucursalesResponsables(pedido, [pedido.Lineas[1].Id]));
        Assert.Empty(AvisosDePedidos.SucursalesResponsables(pedido, [pedido.Lineas[3].Id]));
    }

    // ---------- PED-42: lo que no atiende su sucursal ----------

    [Theory]
    [InlineData("preparar", true)]
    [InlineData("preparar", false)]
    [InlineData("listas", true)]
    [InlineData("listas", false)]
    [InlineData("entregar", true)]
    [InlineData("entregar", false)]
    [Trait("Criterio", "AC-PED-38")]
    public async Task Pedir_una_linea_de_otra_sucursal_sola_o_junto_con_la_propia_rechaza_todo_sin_cambios_ni_llamadas(string operacion, bool conLaPropia)
    {
        var (pedido, deAyacucho, deCusco) = RecojoDeDosSucursales();
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Ayacucho));
        entorno.Repositorio.Poner(pedido);
        Guid[] lineas = conLaPropia ? [deAyacucho, deCusco] : [deCusco];
        var entrada = new OperarLineasEntrada(pedido.Id, lineas);

        var resultado = operacion switch
        {
            "preparar" => await entorno.Operar.PrepararDeRecojo("AYA", entrada, default),
            "listas" => await entorno.Operar.MarcarListasDeRecojo("AYA", entrada, default),
            _ => await entorno.Entregar.Entregar("AYA", entrada, default),
        };

        Assert.Equal("no-encontrado", resultado.Error!.Clave);
        Assert.Equal(["recurso-ajeno"], entorno.Denegaciones.Eventos.Select(e => e.Motivo));
        Assert.Equal(new[] { EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.Confirmado }, pedido.Lineas.Select(l => l.EstadoImpreso!.Value));
        Assert.Empty(entorno.Reservas.Cierres);
        Assert.Empty(entorno.Repositorio.Entregas);
        Assert.DoesNotContain("actualizar", entorno.Repositorio.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-38")]
    public async Task El_encargado_entrega_la_suya_mientras_la_de_la_otra_sucursal_sigue_su_curso_y_la_respuesta_es_solo_de_lo_suyo()
    {
        var (pedido, deAyacucho, _) = RecojoDeDosSucursales();
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Ayacucho));
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Entregar.Entregar("AYA", new OperarLineasEntrada(pedido.Id, [deAyacucho]), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.Entregado, resultado.Valor.Estado);
        Assert.Equal(deAyacucho, Assert.Single(resultado.Valor.Lineas).LineaId);
        Assert.Equal(EstadoDePedido.Confirmado, pedido.Estado);
        Assert.Equal(deAyacucho, Assert.Single(entorno.Reservas.Cierres).Linea);
        Assert.Equal(new[] { Cuenta.ToString("D"), "AYA" }, entorno.Notificador.Solicitudes.Select(s => s.DestinatarioRef));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-38")]
    public async Task La_respuesta_de_preparar_lleva_el_estado_de_las_lineas_del_encargado()
    {
        var (pedido, _, deCusco) = RecojoDeDosSucursales();
        var entorno = new Entorno(Encargado(ResolucionDeSucursalFalsa.Cusco));
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Operar.PrepararDeRecojo("CUS", new OperarLineasEntrada(pedido.Id, [deCusco]), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.EnPreparacion, resultado.Valor.Estado);
    }

    // ---------- PED-22 y PED-17: sin nada salido, todos los envíos ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Criterio", "AC-PED-40")]
    public async Task Sin_nada_salido_cancelar_o_anular_reembolsa_el_total_con_los_dos_envios_una_sola_vez(bool cancelaElCliente)
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, 60m, null, Impresa("Uno", "AYA"), Impresa("Dos", "CUS"));
        var entorno = new Entorno(cancelaElCliente ? ActorDePrueba.DeCliente(Cuenta) : ActorDePrueba.DeAdministradorCentral());
        entorno.Repositorio.Poner(pedido);

        var primera = cancelaElCliente
            ? (await entorno.Cancelar.Cancelar(pedido.Id, default)).EsExito
            : (await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default)).EsExito;
        var segunda = cancelaElCliente
            ? (await entorno.Cancelar.Cancelar(pedido.Id, default)).EsExito
            : (await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default)).EsExito;

        Assert.True(primera);
        Assert.False(segunda);
        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal((60m, 160m), (reembolso.Envio, reembolso.Lineas.Sum(l => l.Monto) + reembolso.Envio));
        Assert.Equal(EstadoDePedido.Anulado, pedido.Estado);
    }
}
