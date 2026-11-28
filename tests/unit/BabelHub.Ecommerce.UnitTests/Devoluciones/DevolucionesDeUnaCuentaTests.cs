using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>
/// Lo que Devoluciones aporta a la cancelación de la cuenta (T15 y T16; DEV-23, DEV-24, DEV-25; AC-DEV-25, AC-DEV-26, AC-DEV-27): el conteo de pendientes en tres números que no cuentan un caso en dos, solo de los pedidos de la cuenta, y la redacción de todo lo de la cuenta,
/// todo o nada, idempotente y sin datos personales.
/// </summary>
public sealed class DevolucionesDeUnaCuentaTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private const string Motivo = "MOTIVO-PERSONAL-SEMBRADO";

    private const string Texto = "TEXTO-PERSONAL-SEMBRADO";

    private static Guid Cuenta(EntornoDeDevoluciones entorno) => entorno.Actor.Actual!.UsuarioId!.Value;

    private static async Task<Guid> Impreso(EntornoDeDevoluciones cliente, int lineas = 2)
    {
        var pedidas = Enumerable.Range(0, lineas).Select(_ => EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m)).ToArray();
        var pedido = cliente.SembrarPedido(pedidas);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [.. pedidas.Select(l => new LineaPedidaParaDevolver(l.LineaId, 1))], new TextoLibre(Motivo), "lima-central"));
        Assert.True(solicitada.EsExito);
        return solicitada.Valor.DevolucionId;
    }

    private static async Task<Guid> Reporte(EntornoDeDevoluciones cliente)
    {
        var digital = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(cliente, digital).Pedido;
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId, enlaceEntregado: true);
        var reportado = await cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre(Motivo), new TextoLibre(Texto)));
        Assert.True(reportado.EsExito);
        return reportado.Valor.DevolucionId;
    }

    private static async Task<Guid> DigitalConReembolsoPendiente(EntornoDeDevoluciones cliente)
    {
        var digital = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(cliente, digital).Pedido;
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId);
        cliente.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;
        var pedida = await cliente.SolicitarDigital.Ejecutar(new PedirReembolsoDeDigitalEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre(Motivo)));
        Assert.True(pedida.EsExito);
        return pedida.Valor.DevolucionId;
    }

    private static EntornoDeDevoluciones Como(ActorDePrueba actor, EntornoDeDevoluciones datos) => new EntornoDeDevoluciones(actor).ConLosDatosDe(datos);

    private static Devolucion Guardada(EntornoDeDevoluciones entorno, Guid id) => entorno.Repositorio.Obtener(id, false).GetAwaiter().GetResult()!;

    // ---------- Pendientes (T15) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public async Task Un_impreso_abierto_cuenta_uno_solicitado_o_aprobado_y_sus_lineas_recibidas_cuentan_su_reembolso_hasta_reembolsarse()
    {
        var cliente = new EntornoDeDevoluciones();
        var id = await Impreso(cliente);
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), cliente);

        var solicitada = await cliente.Pendientes.Contar(Cuenta(cliente));
        await admin.Gestion.Aprobar(id);
        var aprobada = await cliente.Pendientes.Contar(Cuenta(cliente));
        admin.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;
        await admin.Recepcion.Ejecutar(id);
        var recibida = await cliente.Pendientes.Contar(Cuenta(cliente));
        foreach (var linea in Guardada(cliente, id).Lineas)
        {
            admin.Reembolsos.PorClaveNatural[$"dev:{id:D}:{linea.LineaDePedidoId:D}"] = EstadoDelReembolso.Reembolsado;
        }

        await Como(ActorDePrueba.DelSistema(), cliente).Conciliacion.Ejecutar(10);
        var reembolsada = await cliente.Pendientes.Contar(Cuenta(cliente));

        Assert.Equal([1, 1, 2, 0], [solicitada, aprobada, recibida, reembolsada]);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public async Task Un_digital_aprobado_con_el_reembolso_pendiente_cuenta_uno_y_se_suma_un_caso_de_cada_clase_sin_contar_los_de_otra_cuenta()
    {
        var cliente = new EntornoDeDevoluciones();
        await Impreso(cliente);
        await Reporte(cliente);
        await DigitalConReembolsoPendiente(cliente);
        var otra = Como(ActorDePrueba.DeCliente(), cliente);
        await Impreso(otra);
        await Reporte(otra);

        var propia = await cliente.Pendientes.Contar(Cuenta(cliente));
        var deLaOtra = await cliente.Pendientes.Contar(Cuenta(otra));

        Assert.Equal(3, propia);
        Assert.Equal(2, deLaOtra);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public async Task ContarEnTres_informa_abiertas_reportes_y_reembolsos_por_separado_sin_contar_un_caso_en_dos_ni_los_de_otra_cuenta_y_Contar_es_su_suma()
    {
        var cliente = new EntornoDeDevoluciones();
        await Impreso(cliente);
        await Reporte(cliente);
        await DigitalConReembolsoPendiente(cliente);
        var otra = Como(ActorDePrueba.DeCliente(), cliente);
        await Impreso(otra);
        await Reporte(otra);

        var propia = await cliente.Pendientes.ContarEnTres(Cuenta(cliente));
        var deLaOtra = await cliente.Pendientes.ContarEnTres(Cuenta(otra));

        Assert.Equal(new PendientesDeDevolucionesDeCuenta(DevolucionesAbiertas: 1, ReportesSinDecidir: 1, ReembolsosPendientes: 1), propia);
        Assert.Equal(new PendientesDeDevolucionesDeCuenta(1, 1, 0), deLaOtra);
        Assert.Equal(propia.Total, await cliente.Pendientes.Contar(Cuenta(cliente)));
        Assert.Equal(3, propia.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public async Task Una_cuenta_sin_devoluciones_cuenta_cero_y_sin_poder_contar_lanza_nunca_asume_cero()
    {
        var sin = new EntornoDeDevoluciones();
        var sinPedidos = new EntornoDeDevoluciones();
        await Impreso(sinPedidos);
        sinPedidos.Pedidos.NoResponde = true;
        var sinAlmacen = new EntornoDeDevoluciones();
        await Impreso(sinAlmacen);
        sinAlmacen.Repositorio.NoDisponible = true;

        Assert.Equal(0, await sin.Pendientes.Contar(Cuenta(sin)));
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => sinPedidos.Pendientes.Contar(Cuenta(sinPedidos)));
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => sinAlmacen.Pendientes.Contar(Cuenta(sinAlmacen)));
        await Assert.ThrowsAsync<ArgumentException>(() => sin.Pendientes.Contar(Guid.Empty));
    }

    // ---------- Redactar al cancelar (T16) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public async Task Redacta_el_motivo_y_el_texto_de_todas_las_de_la_cuenta_y_quita_al_cliente_y_el_acceso_conservando_lo_demas()
    {
        var cliente = new EntornoDeDevoluciones();
        var abierta = await Impreso(cliente);
        var reporte = await Reporte(cliente);
        var admin = Como(ActorDePrueba.DeAdministradorCentral(), cliente);
        var cerradaId = await Impreso(cliente);
        await admin.Gestion.Rechazar(cerradaId);
        await admin.Gestion.Aprobar(abierta);
        var antes = cliente.Repositorio.Todas.ToDictionary(d => d.Id, d => (d.PedidoId, Lineas: d.Lineas.Select(l => (l.LineaDePedidoId, l.Cantidad, l.Monto, l.Estado, l.EstadoEn)).ToList(), Cambios: d.Lineas.Sum(l => l.Cambios.Count)));
        var personal = Guardada(cliente, abierta).Lineas[0].Cambios[1].Responsable;

        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid());

        Assert.All(cliente.Repositorio.Todas, d =>
        {
            Assert.True(d.Motivo.EstaRedactado);
            Assert.True(d.Texto is null || d.Texto.EstaRedactado);
            Assert.NotNull(d.RedactadaEn);
            Assert.All(d.Lineas, l =>
            {
                Assert.Null(l.AccesoId);
                Assert.DoesNotContain(l.Cambios, c => c.Responsable is { Tipo: TipoDeResponsable.Cliente, Id: not null });
                Assert.Equal(TipoDeResponsable.Cliente, l.Cambios[0].Responsable.Tipo);
            });
            var previo = antes[d.Id];
            Assert.Equal(previo.PedidoId, d.PedidoId);
            Assert.Equal(previo.Lineas, d.Lineas.Select(l => (l.LineaDePedidoId, l.Cantidad, l.Monto, l.Estado, l.EstadoEn)));
            Assert.Equal(previo.Cambios, d.Lineas.Sum(l => l.Cambios.Count));
        });
        Assert.Equal(personal, Guardada(cliente, abierta).Lineas[0].Cambios[1].Responsable);
        Assert.Equal(TextoRedactado.Redactado, Guardada(cliente, reporte).Texto!.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public async Task Es_idempotente_sin_devoluciones_no_falla_y_audita_solo_el_conteo_la_primera_vez()
    {
        var cliente = new EntornoDeDevoluciones();
        var sin = new EntornoDeDevoluciones();
        await Impreso(cliente);
        await Reporte(cliente);

        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid());
        var auditorias = cliente.Auditoria.Solicitudes.Count;
        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid());
        await sin.Redaccion.Aplicar(Cuenta(sin), Guid.NewGuid());

        Assert.Equal(auditorias, cliente.Auditoria.Solicitudes.Count);
        var entrada = cliente.Auditoria.Solicitudes.Single(s => s.Accion.ToString() == "DevolucionesDeCuentaRedactadas");
        Assert.Equal(["cantidad"], entrada.Detalle.Keys);
        Assert.Equal("2", entrada.Detalle["cantidad"]);
        Assert.Empty(sin.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-27")]
    public async Task Es_reaplicable_una_devolucion_nueva_se_redacta_en_la_siguiente_corrida()
    {
        var cliente = new EntornoDeDevoluciones();
        await Impreso(cliente);
        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid());
        var nueva = await Impreso(cliente);

        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid());

        Assert.True(Guardada(cliente, nueva).Motivo.EstaRedactado);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-27")]
    public async Task Es_todo_o_nada_si_la_auditoria_falla_lanza_y_la_transaccion_revierte_todas_las_redacciones()
    {
        var cliente = new EntornoDeDevoluciones();
        var primera = await Impreso(cliente);
        var segunda = await Impreso(cliente);
        cliente.Auditoria.Caida = true;

        await Assert.ThrowsAnyAsync<Exception>(() => cliente.Unidad.Ejecutar(token => Aplicar(cliente, token)));

        Assert.False(Guardada(cliente, primera).Motivo.EstaRedactado);
        Assert.False(Guardada(cliente, segunda).Motivo.EstaRedactado);
    }

    private static async Task<bool> Aplicar(EntornoDeDevoluciones cliente, CancellationToken token)
    {
        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid(), token);
        return true;
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-27")]
    public async Task Solo_redacta_lo_de_la_cuenta_y_con_Pedidos_o_el_almacen_caidos_lanza()
    {
        var cliente = new EntornoDeDevoluciones();
        var propia = await Impreso(cliente);
        var otra = Como(ActorDePrueba.DeCliente(), cliente);
        var ajena = await Impreso(otra);

        await cliente.Redaccion.Aplicar(Cuenta(cliente), Guid.NewGuid());
        Assert.True(Guardada(cliente, propia).Motivo.EstaRedactado);
        Assert.False(Guardada(cliente, ajena).Motivo.EstaRedactado);
        cliente.Pedidos.NoResponde = true;
        var sinPedidos = await Record.ExceptionAsync(() => cliente.Redaccion.Aplicar(Cuenta(otra), Guid.NewGuid()));
        cliente.Pedidos.NoResponde = false;
        cliente.Repositorio.NoDisponible = true;
        var sinAlmacen = await Record.ExceptionAsync(() => cliente.Redaccion.Aplicar(Cuenta(otra), Guid.NewGuid()));
        cliente.Repositorio.NoDisponible = false;

        Assert.IsType<PendientesNoDisponiblesException>(sinPedidos);
        Assert.IsType<PendientesNoDisponiblesException>(sinAlmacen);
        Assert.False(Guardada(cliente, ajena).Motivo.EstaRedactado);
    }
}
