using System.Text.Json;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>
/// Los avisos y la auditoría de cada cambio de estado (T14; DEV-11, DEV-12, DEV-29; AC-DEV-10, AC-DEV-11, AC-DEV-31): el aviso al cliente en la misma transacción con la cuenta leída de Pedidos y campos cerrados, la auditoría con el pedido, la devolución y el monto, y ni el motivo ni el texto
/// en ninguno de los dos.
/// </summary>
public sealed class AvisosYAuditoriaDeDevolucionesTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private const string MotivoSembrado = "SEMBRADO-MOTIVO-QUE-NO-DEBE-SALIR";

    private const string TextoSembrado = "SEMBRADO-TEXTO-QUE-NO-DEBE-SALIR";

    private sealed record Escenario(EntornoDeDevoluciones Cliente, Guid CuentaId, Guid PedidoId, Guid DevolucionId, Guid[] Lineas);

    /// <summary>Una devolución de dos líneas de impresos (100 y 20 soles) solicitada por el cliente.</summary>
    private static async Task<Escenario> ConSolicitada()
    {
        var cliente = new EntornoDeDevoluciones();
        var a = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
        var b = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 2, 20m);
        var pedido = cliente.SembrarPedido(a, b);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(a.LineaId, 2), new LineaPedidaParaDevolver(b.LineaId, 1)], new TextoLibre(MotivoSembrado), "lima-central"));
        Assert.True(solicitada.EsExito);
        return new Escenario(cliente, pedido.CuentaId, pedido.PedidoId, solicitada.Valor.DevolucionId, [a.LineaId, b.LineaId]);
    }

    private static EntornoDeDevoluciones Como(ActorDePrueba actor, Escenario e) => new EntornoDeDevoluciones(actor).ConLosDatosDe(e.Cliente);

    private static IReadOnlyList<SolicitudDeAviso> Avisos(EntornoDeDevoluciones entorno) => entorno.Notificador.Solicitudes;

    // ---------- Avisos (DEV-11, AC-DEV-10) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Aprobar_avisa_al_cliente_dueno_del_pedido_y_no_al_actor_en_la_misma_transaccion_con_campos_cerrados()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);

        await admin.Gestion.Aprobar(e.DevolucionId);

        var aviso = Assert.Single(Avisos(admin));
        Assert.Equal("devolucion-estado", aviso.Plantilla);
        Assert.Equal(DestinatarioDelAviso.Cliente, aviso.Destinatario);
        Assert.Equal(e.CuentaId.ToString("D"), aviso.DestinatarioRef);
        Assert.NotEqual(admin.Actor.Actual!.UsuarioId!.Value.ToString("D"), aviso.DestinatarioRef);
        Assert.Equal(["devolucionId", "estado"], aviso.Campos.Keys.Order());
        Assert.Equal("aprobada", aviso.Campos["estado"]);
        Assert.Equal(e.DevolucionId.ToString("D"), aviso.Campos["devolucionId"]);
        Assert.StartsWith($"devolucion:{e.DevolucionId:D}:aprobada:", aviso.EventoId, StringComparison.Ordinal);
        Assert.Equal(1, admin.Notificador.DentroDeLaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Rechazar_avisa_con_el_estado_rechazada_y_la_solicitud_no_avisa()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        Assert.Empty(Avisos(e.Cliente));

        await admin.Gestion.Rechazar(e.DevolucionId);

        Assert.Equal("rechazada", Assert.Single(Avisos(admin)).Campos["estado"]);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task La_recepcion_avisa_recibida_una_vez_y_reembolsada_por_cada_linea_con_eventos_distintos()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);

        var resultado = await admin.Recepcion.Ejecutar(e.DevolucionId);

        Assert.True(resultado.EsExito);
        var estados = Avisos(admin).Select(a => a.Campos["estado"]).ToList();
        Assert.Equal(["aprobada", "recibida", "reembolsada", "reembolsada"], estados);
        var reembolsadas = Avisos(admin).Where(a => a.Campos["estado"] == "reembolsada").Select(a => a.EventoId).ToList();
        Assert.Equal(2, reembolsadas.Distinct().Count());
        Assert.Equal(4, Avisos(admin).Select(a => a.EventoId).Distinct().Count());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Con_la_pasarela_caida_no_hay_aviso_de_reembolsada_y_la_conciliacion_lo_pide_al_completarse()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        admin.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;
        await admin.Recepcion.Ejecutar(e.DevolucionId);
        Assert.DoesNotContain(Avisos(admin), a => a.Campos["estado"] == "reembolsada");
        var sistema = Como(ActorDePrueba.DelSistema(), e);
        foreach (var linea in e.Lineas)
        {
            sistema.Reembolsos.PorClaveNatural[$"dev:{e.DevolucionId:D}:{linea:D}"] = EstadoDelReembolso.Reembolsado;
        }

        await sistema.Conciliacion.Ejecutar(10);

        Assert.Equal(2, Avisos(sistema).Count(a => a.Campos["estado"] == "reembolsada"));
        Assert.All(Avisos(sistema).Where(a => a.Campos["estado"] == "reembolsada"), a => Assert.Equal(e.CuentaId.ToString("D"), a.DestinatarioRef));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task El_rechazo_por_plazo_del_sistema_avisa_al_cliente_dueno_del_pedido()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        var sistema = Como(ActorDePrueba.DelSistema(), e);
        sistema.Reloj.Ahora = Ahora.AddDays(11);

        await sistema.Vencidas.Ejecutar(10);

        var aviso = Avisos(sistema).Last();
        Assert.Equal("rechazada", aviso.Campos["estado"]);
        Assert.Equal(e.CuentaId.ToString("D"), aviso.DestinatarioRef);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task El_reembolso_de_un_digital_avisa_aprobada_y_reembolsada_y_con_la_pasarela_caida_solo_aprobada()
    {
        var conPasarela = new EntornoDeDevoluciones();
        var sinPasarela = new EntornoDeDevoluciones();
        sinPasarela.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;
        foreach (var entorno in new[] { conPasarela, sinPasarela })
        {
            var digital = EntornoDeDevoluciones.Digital();
            var pedido = new PedidoParaDevolucionesParaPruebas(entorno, digital).Pedido;
            entorno.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId);
            var pedida = await entorno.SolicitarDigital.Ejecutar(new PedirReembolsoDeDigitalEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre(MotivoSembrado)));
            Assert.True(pedida.EsExito);
        }

        Assert.Equal(["aprobada", "reembolsada"], Avisos(conPasarela).Select(a => a.Campos["estado"]));
        Assert.Equal(["aprobada"], Avisos(sinPasarela).Select(a => a.Campos["estado"]));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Decidir_un_reporte_avisa_aprobada_y_reembolsada_o_rechazada()
    {
        var aprobar = new EntornoDeDevoluciones();
        var rechazar = new EntornoDeDevoluciones();
        foreach (var cliente in new[] { aprobar, rechazar })
        {
            var digital = EntornoDeDevoluciones.Digital();
            var pedido = new PedidoParaDevolucionesParaPruebas(cliente, digital).Pedido;
            cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId, enlaceEntregado: true);
            Assert.True((await cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre(MotivoSembrado), new TextoLibre(TextoSembrado)))).EsExito);
        }

        var adminA = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(aprobar);
        var adminR = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(rechazar);
        await adminA.Decidir.Aprobar(aprobar.Repositorio.Todas.Single().Id);
        await adminR.Decidir.Rechazar(rechazar.Repositorio.Todas.Single().Id);

        Assert.Equal(["aprobada", "reembolsada"], Avisos(adminA).Select(a => a.Campos["estado"]));
        Assert.Equal(["rechazada"], Avisos(adminR).Select(a => a.Campos["estado"]));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task El_evento_es_determinista_y_repetir_la_aprobacion_no_avisa_otra_vez()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        var evento = Avisos(admin).Single().EventoId;

        var repetida = await admin.Gestion.Aprobar(e.DevolucionId);

        Assert.Equal(CategoriaDeError.Conflicto, repetida.Error!.Categoria);
        Assert.Single(Avisos(admin));
        Assert.Matches($"^devolucion:{e.DevolucionId:D}:aprobada:[0-9a-f]{{8}}$", evento);
    }

    // ---------- El Notificador y Pedidos no bloquean ni se pierden ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-10")]
    [InlineData("lanza")]
    [InlineData("rechazado")]
    [InlineData("no-se-guardo")]
    public async Task Un_Notificador_que_falla_no_interrumpe_el_cambio_y_un_rechazo_de_la_plantilla_publica_una_alerta(string falla)
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        admin.Notificador.Lanza = falla == "lanza";
        admin.Notificador.Resultado = falla switch
        {
            "rechazado" => ResultadoDeLaSolicitudDeAviso.Rechazado,
            "no-se-guardo" => ResultadoDeLaSolicitudDeAviso.NoSeGuardo,
            _ => ResultadoDeLaSolicitudDeAviso.Creado,
        };

        var resultado = await admin.Gestion.Aprobar(e.DevolucionId);

        Assert.True(resultado.EsExito);
        Assert.All(admin.Repositorio.Todas.Single().Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
        Assert.Equal(falla == "rechazado" ? 1 : 0, admin.Alertas.Alertas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Con_Pedidos_caido_aprobar_no_cambia_nada_y_se_reintenta_sin_perder_el_aviso()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        admin.Pedidos.NoResponde = true;

        var caido = await admin.Gestion.Aprobar(e.DevolucionId);
        admin.Pedidos.NoResponde = false;
        var reintento = await admin.Gestion.Aprobar(e.DevolucionId);

        Assert.Equal(CategoriaDeError.NoDisponible, caido.Error!.Categoria);
        Assert.True(reintento.EsExito);
        Assert.Single(Avisos(admin));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Con_Pedidos_caido_la_revision_de_plazos_no_rechaza_y_la_siguiente_si_con_su_aviso()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        var sistema = Como(ActorDePrueba.DelSistema(), e);
        sistema.Reloj.Ahora = Ahora.AddDays(11);
        sistema.Pedidos.NoResponde = true;

        var caido = await sistema.Vencidas.Ejecutar(10);
        sistema.Pedidos.NoResponde = false;
        var siguiente = await sistema.Vencidas.Ejecutar(10);

        Assert.Equal(0, caido.Valor);
        Assert.Equal(1, siguiente.Valor);
        Assert.Equal(["aprobada", "rechazada"], Avisos(sistema).Select(a => a.Campos["estado"]));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-10")]
    public async Task Si_la_auditoria_cae_el_cambio_y_su_aviso_se_revierten_juntos()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        admin.Auditoria.Caida = true;

        await Record.ExceptionAsync(() => admin.Gestion.Aprobar(e.DevolucionId));

        Assert.Empty(Avisos(admin));
        Assert.All(admin.Repositorio.Todas.Single().Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Solicitada, l.Estado));
    }

    // ---------- Auditoría con el pedido y el monto (DEV-12, AC-DEV-11) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-11")]
    public async Task Cada_paso_del_recorrido_completo_deja_una_entrada_con_el_pedido_la_devolucion_y_el_monto()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        await admin.Recepcion.Ejecutar(e.DevolucionId);

        var entradas = admin.Auditoria.Solicitudes;

        Assert.All(entradas, s => Assert.Equal(e.PedidoId.ToString(), s.Detalle["pedidoId"]));
        var solicitada = entradas.Single(s => s.Accion.ToString() == "DevolucionSolicitada");
        Assert.Equal("120.00", solicitada.Detalle["monto"]);
        Assert.Equal("PEN", solicitada.Detalle["moneda"]);
        var cambios = entradas.Where(s => s.Accion.ToString() == "EstadoDeDevolucionCambiado").ToList();
        Assert.Equal(["aprobada", "recepcion-iniciada", "recibida", "reembolsada", "reembolsada"], cambios.Select(s => s.Detalle["estadoNuevo"]));
        Assert.All(cambios, s => Assert.Equal(e.DevolucionId.ToString(), s.Detalle["devolucionId"]));
        Assert.Equal(["120.00", "120.00", "120.00"], cambios.Take(3).Select(s => s.Detalle["monto"]));
        var reembolsos = cambios.Skip(3).Select(s => s.Detalle["monto"]).ToList();
        Assert.Contains("100.00", reembolsos);
        Assert.Contains("20.00", reembolsos);
        var reingreso = entradas.Single(s => s.Accion.ToString() == "ReingresoPorDevolucion");
        Assert.Equal("120.00", reingreso.Detalle["monto"]);
        Assert.Equal(e.PedidoId.ToString(), reingreso.Detalle["pedidoId"]);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-11")]
    public async Task El_rechazo_por_plazo_se_audita_con_el_pedido_y_el_monto_como_sistema()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        var sistema = Como(ActorDePrueba.DelSistema(), e);
        sistema.Reloj.Ahora = Ahora.AddDays(11);

        await sistema.Vencidas.Ejecutar(10);

        var rechazo = sistema.Auditoria.Solicitudes.Last();
        Assert.Equal("rechazada", rechazo.Detalle["estadoNuevo"]);
        Assert.Equal(e.PedidoId.ToString(), rechazo.Detalle["pedidoId"]);
        Assert.Equal("120.00", rechazo.Detalle["monto"]);
    }

    // ---------- Sin el motivo ni el texto (DEV-29, AC-DEV-31) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public async Task Ni_el_motivo_ni_el_texto_sembrados_aparecen_en_los_avisos_en_la_auditoria_ni_en_las_alertas()
    {
        var e = await ConSolicitada();
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), e);
        await admin.Gestion.Aprobar(e.DevolucionId);
        await admin.Recepcion.Ejecutar(e.DevolucionId);
        var digital = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(e.Cliente, digital).Pedido;
        e.Cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId, enlaceEntregado: true);
        await e.Cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre(MotivoSembrado), new TextoLibre(TextoSembrado)));
        var reporte = e.Cliente.Repositorio.Todas.Single(d => d.Origen == OrigenDeDevolucion.Reporte);
        await Como(ActorDePrueba.DeAdministradorCentral(), e).Decidir.Aprobar(reporte.Id);

        var salidas = JsonSerializer.Serialize(new
        {
            avisos = Avisos(admin).Concat(Avisos(e.Cliente)).Select(a => new { a.EventoId, a.Plantilla, a.Campos, a.DestinatarioRef }),
            auditoria = admin.Auditoria.Solicitudes.Select(s => new { Accion = s.Accion.ToString(), s.Detalle, s.ObjetoId }),
            alertas = admin.Alertas.Alertas.Select(a => a.ToString()),
        });

        Assert.DoesNotContain("SEMBRADO", salidas, StringComparison.Ordinal);
    }
}
