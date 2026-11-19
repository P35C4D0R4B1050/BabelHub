using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
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

/// <summary>T19 y T20 (PED-13, PED-16 a PED-18, PED-21 a PED-24, PED-27; AC-PED-11, 14 a 18, 20, 22): la cancelación del cliente y la anulación de la administración, en tres tiempos.</summary>
public sealed class CancelarYAnularPedidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static int _contador;

    private sealed class Entorno(ActorDePrueba actor)
    {
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

        public LimitesPermisivosDePedidos Limites { get; } = new();

        public EstadoDeCuentaFalso Cuentas { get; } = new();

        private AutorizadorDePedidos Autorizador => new(actor, Denegaciones, new ResolucionDeSucursalFalsa());

        private ControlesDeGestionDePedidos Controles => new(Limites, Cuentas, Repositorio);

        private OrquestadorDeAnulaciones Orquestador => new(Autorizador, Controles, Repositorio, Unidad, Despacho, Reservas, Reembolsos, Accesos, Auditoria, Notificador, Alertas, new GeneradorDeIdentificadoresFalso(), new RelojFijo(Ahora));

        public CancelarPedidoDelCliente Cancelar => new(Autorizador, Controles, Orquestador);

        public AnularPedido Anular => new(Autorizador, Controles, Orquestador);
    }

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static DatosDeLineaNueva Impresa(string titulo, string fuente = "LIM", bool traslado = false, decimal precio = 50m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, null, 1, precio, precio, fuente, traslado, traslado ? Guid.NewGuid() : null, null);

    private static DatosDeLineaNueva Digital(string titulo, decimal precio = 30m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), true, titulo, null, 1, precio, precio, null, false, null, null);

    private static PedidoDeDominio Crear(TipoDeEntregaDelPedido tipo, decimal envio, params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? BabelHub.Ecommerce.Domain.Pedidos.ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), Numero(), Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? "LIM" : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new("t-1", Ahora), new("p-1", Ahora), lineas.Any(l => l.EsDigital) ? new("rd-1", Ahora) : null, lineas, Ahora);
        return PedidoDeDominio.Crear(datos).Pedido!;
    }

    private static Entorno ComoCliente() => new(ActorDePrueba.DeCliente(Cuenta));

    private static Entorno ComoAdministracion() => new(ActorDePrueba.DeAdministradorCentral());

    private static void Preparar(PedidoDeDominio pedido, params Guid[] lineaIds) => pedido.PrepararLineas(lineaIds, Ahora);

    // ---------- T19: la cancelación del cliente ----------

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public async Task El_cliente_cancela_un_pedido_confirmado_libera_la_reserva_reembolsa_el_total_con_el_envio_y_queda_anulado_con_su_auditoria()
    {
        var entorno = ComoCliente();
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, 12m, Impresa("Con traslado", "CUS", traslado: true));
        entorno.Repositorio.Poner(pedido);

        var resultado = await entorno.Cancelar.Cancelar(pedido.Id, default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoVisibleDelPedido.Anulado, 62m), (resultado.Valor.Estado, resultado.Valor.Reembolso));
        Assert.Equal(EstadoDeLineaImpresa.Anulado, pedido.Lineas[0].EstadoImpreso);
        var liberacion = Assert.Single(entorno.Reservas.Liberaciones);
        Assert.Equal((pedido.Id, "cancelado-por-el-cliente"), (liberacion.Pedido, liberacion.Motivo));
        Assert.Equal([pedido.Lineas[0].Id], liberacion.Lineas);
        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal((OrigenDelReembolso.CancelacionDelCliente, $"canc:{pedido.Id:N}", 12m), (reembolso.Origen, reembolso.ClaveNatural, reembolso.Envio));
        Assert.Equal(EstadoDeOperacion.Completada, Assert.Single(entorno.Repositorio.Anulaciones).Estado);
        Assert.Empty(entorno.Despacho.Cancelaciones);
        Assert.Equal([AccionesAuditables.OperacionDePedidoIniciada, AccionesAuditables.PedidoCanceladoPorElCliente, AccionesAuditables.EstadoDePedidoCambiado], entorno.Auditoria.Solicitudes.Select(s => s.Accion));
        Assert.All(entorno.Notificador.Solicitudes, s => Assert.Equal("anulado", s.Campos["estado"]));
        Assert.Single(entorno.Reembolsos.Procesados);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-16")]
    public async Task El_cliente_no_puede_cancelar_un_pedido_en_preparacion_en_transito_o_con_un_digital_y_se_le_indica_la_alternativa()
    {
        var enPreparacion = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("En preparación"));
        Preparar(enPreparacion, enPreparacion.Lineas[0].Id);
        var enTransito = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("En tránsito", "CUS", traslado: true));
        enTransito.RegistrarTraslado(enTransito.Lineas[0].Id, EstadoDeLineaImpresa.EnTransito, null, Ahora);
        var conDigital = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Impreso"), Digital("Digital"));

        foreach (var (pedido, alternativa) in new[] { (enPreparacion, "devolucion"), (enTransito, "devolucion"), (conDigital, "politica-de-reembolso-digital") })
        {
            var entorno = ComoCliente();
            entorno.Repositorio.Poner(pedido);

            var resultado = await entorno.Cancelar.Cancelar(pedido.Id, default);

            Assert.Equal((CategoriaDeError.Conflicto, "pedido-no-cancelable"), (resultado.Error!.Categoria, resultado.Error.Clave));
            Assert.Equal(alternativa, Assert.Single(resultado.Error.Errores!).Clave);
            Assert.Empty(entorno.Reservas.Liberaciones);
            Assert.Empty(entorno.Reembolsos.Registrados);
            Assert.Empty(entorno.Repositorio.Anulaciones);
            var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
            Assert.Equal(ResultadoAuditado.Rechazado, auditoria.Resultado);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public async Task Una_doble_cancelacion_no_reembolsa_dos_veces_y_una_operacion_en_curso_bloquea_la_segunda()
    {
        var entorno = ComoCliente();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);

        await entorno.Cancelar.Cancelar(pedido.Id, default);
        var segunda = await entorno.Cancelar.Cancelar(pedido.Id, default);

        Assert.Equal("pedido-estado-invalido", segunda.Error!.Clave);
        Assert.Single(entorno.Reembolsos.Registrados);

        var enCurso = ComoCliente();
        var otro = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Otro"));
        enCurso.Repositorio.Poner(otro);
        enCurso.Repositorio.Anulaciones.Add(Anulacion.Iniciar(Guid.NewGuid(), otro.Id, otro.PlanearCancelacionDelCliente().Plan!, new IniciadorDeOperacion(Guid.NewGuid(), "cliente", null), Ahora));

        var bloqueada = await enCurso.Cancelar.Cancelar(otro.Id, default);

        Assert.Equal("pedido-operacion-en-curso", bloqueada.Error!.Clave);
        Assert.Empty(enCurso.Reservas.Liberaciones);
    }

    [Fact]
    public async Task Un_pedido_ajeno_es_no_encontrado_y_los_textos_pendientes_se_comprueban_antes_de_bloquear_el_pedido()
    {
        var ajeno = ComoCliente();
        var pedidoAjeno = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Ajeno"));
        var otraCuenta = new Entorno(ActorDePrueba.DeCliente(Guid.NewGuid()));
        otraCuenta.Repositorio.Poner(pedidoAjeno);

        var noEncontrado = await otraCuenta.Cancelar.Cancelar(pedidoAjeno.Id, default);

        Assert.Equal("no-encontrado", noEncontrado.Error!.Clave);
        Assert.Single(otraCuenta.Denegaciones.Eventos);

        var pendientes = ComoCliente();
        pendientes.Cuentas.Estado = new EstadoDeCuenta(Cuenta, true, EstadoDeAceptacionDeLosTextos.Pendiente, true, false, false);
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        pendientes.Repositorio.Poner(pedido);

        var resultado = await pendientes.Cancelar.Cancelar(pedido.Id, default);

        Assert.Equal("textos-pendientes", resultado.Error!.Clave);
        Assert.DoesNotContain("obtener-con-bloqueo", pendientes.Repositorio.Llamadas);
        Assert.NotNull(ajeno);
    }

    [Fact]
    public async Task Si_el_Inventario_no_responde_la_cancelacion_queda_en_curso_sin_reembolso_y_las_lineas_no_cambian()
    {
        var entorno = ComoCliente();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.NoSePudo;

        var resultado = await entorno.Cancelar.Cancelar(pedido.Id, default);

        Assert.Equal((CategoriaDeError.NoDisponible, "servicio-no-disponible"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeOperacion.EnCurso, entorno.Repositorio.Anulaciones[0].Estado);
        Assert.Equal(1, entorno.Repositorio.Anulaciones[0].Intentos);
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Sin_poder_auditar_la_solicitud_no_hay_fila_ni_llamada_al_Inventario()
    {
        var entorno = ComoCliente();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.Cancelar.Cancelar(pedido.Id, default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Anulaciones);
        Assert.Empty(entorno.Reservas.Liberaciones);
    }

    [Fact]
    public async Task Si_Pagos_no_registra_el_reembolso_el_tiempo_3_se_revierte_y_la_fila_sigue_en_curso_con_las_reservas_ya_liberadas()
    {
        var entorno = ComoCliente();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        entorno.Reembolsos.FallaAlRegistrar = true;

        var resultado = await entorno.Cancelar.Cancelar(pedido.Id, default);

        Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
        var fila = Assert.Single(entorno.Repositorio.Anulaciones);
        Assert.Equal(EstadoDeOperacion.EnCurso, fila.Estado);
        Assert.NotNull(fila.PasoConfirmadoEn(PasoDeAnulacion.ReservasLiberadas));
        Assert.Equal(1, entorno.Unidad.Revertidas);
    }

    // ---------- T20: la anulación de la administración ----------

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    public async Task La_administracion_anula_un_pedido_libera_reembolsa_el_total_con_el_envio_y_revoca_el_acceso_digital_despues_de_registrar_el_reembolso()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, 12m, Impresa("Impreso"), Digital("Digital"));
        entorno.Repositorio.Poner(pedido);
        entorno.Accesos.AntesDeRevocar = () => Assert.Single(entorno.Reembolsos.Registrados);

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "pago-duplicado"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoVisibleDelPedido.Anulado, 92m, false), (resultado.Valor.Estado, resultado.Valor.Reembolso, resultado.Valor.ReembolsoPendiente));
        Assert.Equal([pedido.Id], entorno.Despacho.Cancelaciones);
        Assert.Equal("pedido-anulado", Assert.Single(entorno.Reservas.Liberaciones).Motivo);
        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal((OrigenDelReembolso.Anulacion, $"anul:{pedido.Id:N}", 12m, 2), (reembolso.Origen, reembolso.ClaveNatural, reembolso.Envio, reembolso.Lineas.Count));
        Assert.Equal(pedido.Lineas[1].EdicionId, Assert.Single(entorno.Accesos.Revocados).EdicionId);
        Assert.All(pedido.Lineas, l => Assert.True(l.EstadoImpreso == EstadoDeLineaImpresa.Anulado || l.EstadoDigital == EstadoDeLineaDigital.Anulado));
        Assert.Equal([AccionesAuditables.OperacionDePedidoIniciada, AccionesAuditables.PedidoAnulado, AccionesAuditables.EstadoDePedidoCambiado], entorno.Auditoria.Solicitudes.Select(s => s.Accion));
        Assert.DoesNotContain(entorno.Auditoria.Solicitudes.SelectMany(s => s.Detalle.Values), v => v is not null && v.Contains("Jr. Lima", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_motivo_es_obligatorio_de_la_lista_cerrada_y_el_de_la_cancelacion_del_cliente_no_le_corresponde_a_la_administracion()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);

        foreach (var (motivo, codigo) in new[] { ((string?)null, "obligatorio"), ("", "obligatorio"), ("texto libre", "invalido"), ("cancelado-por-el-cliente", "invalido"), ("Pago-Duplicado", "invalido") })
        {
            var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, motivo!), default);

            Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
            Assert.Equal(codigo, Assert.Single(resultado.Error.Errores!).Clave);
        }

        Assert.Empty(entorno.Reservas.Liberaciones);
        Assert.Empty(entorno.Repositorio.Anulaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-20")]
    public async Task Con_un_impreso_en_reparto_y_otro_en_preparacion_solo_se_anula_el_segundo_se_cancela_su_envio_y_no_se_reembolsa_el_envio_de_cobro()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, 12m, Impresa("De Cusco", "CUS"), Impresa("De Ayacucho", "AYA"));
        var cusco = pedido.Lineas[0].Id;
        var ayacucho = pedido.Lineas[1].Id;
        var envioCusco = Guid.NewGuid();
        var envioAyacucho = Guid.NewGuid();
        pedido.PrepararLineas([cusco, ayacucho], Ahora);
        pedido.MarcarListas([cusco, ayacucho], Ahora);
        pedido.AsignarEnvio([cusco], envioCusco);
        pedido.AsignarEnvio([ayacucho], envioAyacucho);
        pedido.DespacharLineas([cusco], Ahora);
        entorno.Repositorio.Poner(pedido);
        entorno.Despacho.Resultado.Add(new ResultadoDeCancelarEnvio(envioCusco, DestinoDelEnvioAlCancelar.Mantenido, [cusco]));
        entorno.Despacho.Resultado.Add(new ResultadoDeCancelarEnvio(envioAyacucho, DestinoDelEnvioAlCancelar.Cancelado, [ayacucho]));

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, pedido.Lineas[1].EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.Despachado, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoVisibleDelPedido.Despachado, resultado.Valor.Estado);
        Assert.Equal([ayacucho], Assert.Single(entorno.Reservas.Liberaciones).Lineas);
        var reembolso = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal((0m, ayacucho), (reembolso.Envio, Assert.Single(reembolso.Lineas).LineaId));
        Assert.Equal(50m, resultado.Valor.Reembolso);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-22")]
    [Trait("Criterio", "AC-PED-14")]
    public async Task Una_linea_en_preparacion_otra_lista_y_otra_no_recogida_se_anulan_y_cada_reserva_se_libera_una_sola_vez()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("En preparación"), Impresa("Lista"), Impresa("No recogida"));
        var ids = pedido.Lineas.Select(l => l.Id).ToList();
        pedido.PrepararLineas(ids, Ahora - TimeSpan.FromDays(20));
        pedido.MarcarListas(ids[1..], Ahora - TimeSpan.FromDays(20));
        pedido.MarcarNoRecogidas(Ahora, new HashSet<Guid> { ids[1] });
        entorno.Repositorio.Poner(pedido);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, pedido.Lineas[2].EstadoImpreso);

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "no-recogido"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.All(pedido.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Anulado, l.EstadoImpreso));
        Assert.Equal(3, entorno.Reservas.Liberaciones.Count);
        Assert.Equal(ids.Order(), entorno.Reservas.Liberaciones.SelectMany(l => l.Lineas).Order());
        Assert.Empty(entorno.Despacho.Cancelaciones);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, resultado.Valor.Estado);
    }

    [Fact]
    public async Task Un_409_de_liberar_deja_la_linea_fuera_y_el_plan_se_recalcula_sin_ella_con_su_alerta()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Sigue", precio: 50m), Impresa("Ya salió", precio: 70m));
        var salio = pedido.Lineas[1].Id;
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.RespuestaPorLineas = lineas => lineas.Contains(salio) ? ResultadoDeLiberarReserva.YaCerrada : ResultadoDeLiberarReserva.Liberada;

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(50m, resultado.Valor.Reembolso);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas[1].EstadoImpreso);
        Assert.Equal(salio, entorno.Reservas.Liberaciones.Where(l => l.Lineas.Contains(salio)).Select(l => l.Lineas.Single()).Distinct().Single());
        Assert.Equal(pedido.Lineas[0].Id, Assert.Single(Assert.Single(entorno.Reembolsos.Registrados).Lineas).LineaId);
        Assert.Contains(entorno.Alertas.Alertas, a => a.Motivo == OrquestadorDeAnulaciones.AlertaLiberacionYaCerrada);
    }

    [Fact]
    public async Task Un_pedido_solo_digital_o_ya_anulado_se_rechaza_y_el_rechazo_se_audita()
    {
        var soloDigital = ComoAdministracion();
        var digital = Crear(TipoDeEntregaDelPedido.Digital, 0m, Digital("Digital"));
        soloDigital.Repositorio.Poner(digital);

        var resultado = await soloDigital.Anular.Anular(new AnularPedidoEntrada(digital.Id, "otro"), default);

        Assert.Equal((CategoriaDeError.Conflicto, "pedido-sin-lineas-anulables"), (resultado.Error!.Categoria, resultado.Error.Clave));
        var auditoria = Assert.Single(soloDigital.Auditoria.Solicitudes);
        Assert.Equal(ResultadoAuditado.Rechazado, auditoria.Resultado);
        Assert.Empty(soloDigital.Repositorio.Anulaciones);
        Assert.Empty(soloDigital.Accesos.Revocados);

        var yaAnulado = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        yaAnulado.Repositorio.Poner(pedido);
        await yaAnulado.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        var repetida = await yaAnulado.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.Equal("pedido-estado-invalido", repetida.Error!.Clave);
        Assert.Single(yaAnulado.Reembolsos.Registrados);
    }

    [Fact]
    public async Task Si_Envios_falla_al_cancelar_o_lanza_el_tiempo_1_se_revierte_sin_fila_ni_auditoria_de_inicio()
    {
        foreach (var (falla, lanza) in new[] { (true, false), (false, true) })
        {
            var entorno = ComoAdministracion();
            var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, 12m, Impresa("Uno"));
            entorno.Repositorio.Poner(pedido);
            entorno.Despacho.Falla = falla;
            entorno.Despacho.Lanza = lanza;

            var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

            Assert.Equal("servicio-no-disponible", resultado.Error!.Clave);
            Assert.Empty(entorno.Repositorio.Anulaciones);
            Assert.Empty(entorno.Reservas.Liberaciones);
            Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas[0].EstadoImpreso);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-19")]
    public async Task Si_la_pasarela_no_responde_el_pedido_queda_anulado_con_el_reembolso_pendiente()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        entorno.Reembolsos.EstadoAlProcesar = null;

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "pago-duplicado"), default);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoVisibleDelPedido.Anulado, true), (resultado.Valor.Estado, resultado.Valor.ReembolsoPendiente));
        Assert.Equal(EstadoDeOperacion.Completada, entorno.Repositorio.Anulaciones[0].Estado);
        Assert.Single(entorno.Reembolsos.Registrados);
    }

    [Fact]
    public async Task Solo_la_administracion_central_anula_y_con_una_entrega_en_curso_se_rechaza()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        foreach (var actor in new[] { ActorDePrueba.DeEncargadoDe(ResolucionDeSucursalFalsa.Lima), ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.DeCliente(Cuenta) })
        {
            var otro = new Entorno(actor);
            otro.Repositorio.Poner(pedido);

            var prohibido = await otro.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

            Assert.Equal("prohibido", prohibido.Error!.Clave);
            Assert.Empty(otro.Reservas.Liberaciones);
        }

        var entorno = ComoAdministracion();
        entorno.Repositorio.Poner(pedido);
        entorno.Repositorio.Entregas.Add(Entrega.Iniciar(Guid.NewGuid(), pedido.Id, [pedido.Lineas[0].Id], new IniciadorDeOperacion(Guid.NewGuid(), "encargado-de-sucursal", null), Ahora));

        var enCurso = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.Equal("pedido-operacion-en-curso", enCurso.Error!.Clave);
        Assert.Empty(entorno.Reservas.Liberaciones);
    }

    [Fact]
    public async Task Repetir_una_anulacion_fallida_no_trata_como_salido_un_envio_ya_cancelado_y_anula_y_reembolsa_sus_lineas()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, 12m, Impresa("Uno", "CUS"));
        var linea = pedido.Lineas[0].Id;
        var envio = Guid.NewGuid();
        pedido.PrepararLineas([linea], Ahora);
        pedido.MarcarListas([linea], Ahora);
        pedido.AsignarEnvio([linea], envio);
        entorno.Repositorio.Poner(pedido);
        entorno.Despacho.Resultado.Add(new ResultadoDeCancelarEnvio(envio, DestinoDelEnvioAlCancelar.Cancelado, [linea]));
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.NoSePudo;
        await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);
        entorno.Repositorio.Anulaciones[0].Fallar(Ahora);

        // Envíos informa «mantenido» el envío que ya cancelo la primera vez.
        entorno.Despacho.Resultado.Clear();
        entorno.Despacho.Resultado.Add(new ResultadoDeCancelarEnvio(envio, DestinoDelEnvioAlCancelar.Mantenido, [linea]));
        entorno.Reservas.RespuestaDeLiberar = () => ResultadoDeLiberarReserva.Liberada;
        var reintento = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.True(reintento.EsExito, reintento.Error?.Clave);
        Assert.Equal(EstadoDeLineaImpresa.Anulado, pedido.Lineas[0].EstadoImpreso);
        Assert.Equal(62m, reintento.Valor.Reembolso);
    }

    [Fact]
    public async Task Si_otra_instancia_resolvio_la_fila_entre_el_tiempo_2_y_el_3_no_se_aplica_nada_dos_veces()
    {
        var entorno = ComoAdministracion();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, 0m, Impresa("Uno"));
        entorno.Repositorio.Poner(pedido);
        entorno.Reservas.RespuestaDeLiberar = () =>
        {
            entorno.Repositorio.Anulaciones[0].Descartar(Ahora);
            return ResultadoDeLiberarReserva.Liberada;
        };

        var resultado = await entorno.Anular.Anular(new AnularPedidoEntrada(pedido.Id, "otro"), default);

        Assert.Equal((CategoriaDeError.Conflicto, "operacion-ya-resuelta"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, pedido.Lineas[0].EstadoImpreso);
        Assert.DoesNotContain(entorno.Auditoria.Solicitudes, s => s.Accion == AccionesAuditables.PedidoAnulado);
    }
}
