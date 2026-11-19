using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Las operaciones de dinero y de datos personales dejan su rastro en la auditoría, en la misma transacción y sin datos de pago ni de la persona (AGENTS §4, PAG-11, CTL-35).</summary>
public sealed class AuditoriaDePagosTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task La_aprobacion_de_un_cobro_se_audita_como_aprobado_sin_la_referencia_de_la_pasarela()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());

        await e.Aprobar("ch_secreta");

        var auditoria = e.Entorno.Auditoria.Solicitudes.Single(s => s.Accion.Identificador == "CobroResuelto");
        Assert.Equal("aprobado", auditoria.Detalle["resultado"]);
        Assert.Equal(e.IntentoId.ToString("D"), auditoria.ObjetoId);
        Assert.DoesNotContain("ch_secreta", string.Join(' ', auditoria.Detalle.Values));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Repetir_la_aprobacion_no_audita_dos_veces()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        await e.Aprobar();
        e.Intencion.SoltarReclamacion();
        await e.Entorno.Repositorio.GuardarIntencion(e.Intencion);

        await e.Aprobar();

        Assert.Single(e.Entorno.Auditoria.Solicitudes, s => s.Accion.Identificador == "CobroResuelto");
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_la_auditoria_cae_la_aprobacion_no_se_marca_y_la_reconciliacion_la_recoge()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Auditoria.Caida = true;

        await e.Aprobar();

        Assert.Equal(EstadoDeIntento.Iniciado, e.Intento.Estado);
        Assert.Equal(EstadoDeIntencion.ConCobro, e.Intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task El_resultado_de_un_reembolso_se_audita_reembolsado_o_pendiente_con_su_motivo()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.Rechazado;
        await e.Procesar.Ejecutar(id);
        e.Pagos.Pasarela.RespuestaDeReembolso = null;
        e.ComoAdministrador();

        await e.Reintentar.Ejecutar(id);

        var resueltos = e.Pagos.Auditoria.Solicitudes.Where(s => s.Accion.Identificador == "ReembolsoResuelto").ToArray();
        Assert.Equal(2, resueltos.Length);
        Assert.Equal("pendiente", resueltos[0].Detalle["resultado"]);
        Assert.Equal("rechazado-por-pasarela", resueltos[0].Detalle["motivo"]);
        Assert.Equal("reembolsado", resueltos[1].Detalle["resultado"]);
        Assert.All(resueltos, s => Assert.DoesNotContain("re_1", string.Join(' ', s.Detalle.Values)));
        Assert.Equal(Pedido.ToString("D"), resueltos[1].ObjetoId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Cancelar_la_cuenta_audita_cuantas_intenciones_desvinculo_y_nada_si_no_hubo()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var aplicador = new DesvincularPagosDeUnaCuenta(e.Pagos.Repositorio, e.Pagos.Auditoria, e.Pagos.Reloj);

        await aplicador.Aplicar(InstantaneasDeCompraDePrueba.Cuenta, Guid.NewGuid());
        await aplicador.Aplicar(InstantaneasDeCompraDePrueba.Cuenta, Guid.NewGuid());

        var auditoria = Assert.Single(e.Pagos.Auditoria.Solicitudes, s => s.Accion.Identificador == "IntencionesDePagoDesvinculadas");
        Assert.Equal("1", auditoria.Detalle["cantidad"]);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task Liberar_el_contacto_a_pedido_de_Pedidos_se_audita_una_vez_y_sin_datos_personales()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var liberar = new LiberarContactoDeLaIntencion(e.Pagos.Repositorio, e.Pagos.Unidad, e.Pagos.Auditoria, e.Pagos.Reloj);

        await liberar.Liberar(Pedido);
        await liberar.Liberar(Pedido);

        var auditoria = Assert.Single(e.Pagos.Auditoria.Solicitudes, s => s.Accion.Identificador == "ContactoDeEntregaLiberado");
        Assert.Equal("pedido", auditoria.Detalle["origen"]);
        Assert.DoesNotContain("Ana", string.Join(' ', auditoria.Detalle.Values));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task La_purga_de_los_30_dias_audita_cada_contacto_eliminado_con_su_origen()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var purgar = new PurgarContactoDeIntenciones(e.Repositorio, e.Unidad, new BloqueoDeIntenciones(e.Repositorio, e.Cuenta), e.Auditoria, e.Alertas, e.Reloj);
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddDays(31);

        await purgar.Ejecutar(10);

        Assert.Equal("retencion", Assert.Single(e.Auditoria.Solicitudes, s => s.Accion.Identificador == "ContactoDeEntregaLiberado").Detalle["origen"]);
    }
}
