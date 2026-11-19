using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class TrabajosDePagosTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    private static BloqueoDeIntenciones Bloqueo(EntornoDePagos e) => new(e.Repositorio, e.Cuenta);

    // ---------- Cerrar intenciones vencidas ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Una_intencion_abierta_vencida_se_cierra_sin_cobro_y_pierde_el_contacto()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var cerrar = new CerrarIntencionesVencidas(e.Repositorio, e.Unidad, Bloqueo(e), e.Alertas, e.Reloj);
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(16);

        var cerradas = await cerrar.Ejecutar(10);

        Assert.Equal(1, cerradas);
        var intencion = e.Repositorio.Intenciones.Single();
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, intencion.Estado);
        Assert.Equal(MotivoDeCierre.Vencida, intencion.MotivoDeCierre);
        Assert.Null(intencion.Contacto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Una_intencion_vigente_o_con_cobro_no_se_cierra_y_repetir_no_cambia_nada()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var cerrar = new CerrarIntencionesVencidas(e.Repositorio, e.Unidad, Bloqueo(e), e.Alertas, e.Reloj);

        Assert.Equal(0, await cerrar.Ejecutar(10));

        var intencion = e.Repositorio.Intenciones.Single();
        intencion.IniciarCobro(e.Reloj.Ahora);
        await e.Repositorio.GuardarIntencion(intencion);
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(16);
        Assert.Equal(0, await cerrar.Ejecutar(10));
        Assert.Equal(EstadoDeIntencion.ConCobro, e.Repositorio.Intenciones.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Cerrar_lee_la_marca_antes_de_bloquear_y_una_falla_alerta_sin_detener_la_corrida()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var cerrar = new CerrarIntencionesVencidas(e.Repositorio, e.Unidad, Bloqueo(e), e.Alertas, e.Reloj);
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(16);
        e.Repositorio.BloqueoNoDisponible = true;

        var cerradas = await cerrar.Ejecutar(10);

        Assert.Equal(0, cerradas);
        Assert.Contains(e.Alertas.Publicadas, a => a.Motivo == CerrarIntencionesVencidas.MotivoDeAlertaCierreFallido);
    }

    // ---------- Reintentar reembolsos ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_pendiente_se_reintenta_en_una_transaccion_corta_y_se_pide_fuera_de_ella()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        var reintentar = new ReintentarReembolsosPendientes(e.Pagos.Reembolsos, e.Pagos.Unidad, e.Procesar, e.Pagos.Alertas, e.Pagos.Reloj);

        var intentados = await reintentar.Ejecutar(10);

        Assert.Equal(1, intentados);
        Assert.Equal(EstadoDeReembolso.Reembolsado, e.ReembolsoPorId(id).Estado);
        Assert.Equal(0, e.Pagos.Pasarela.ReembolsosDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Tras_un_sin_respuesta_el_trabajo_espera_antes_de_pedir_otra_vez()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;
        var reintentar = new ReintentarReembolsosPendientes(e.Pagos.Reembolsos, e.Pagos.Unidad, e.Procesar, e.Pagos.Alertas, e.Pagos.Reloj);

        await reintentar.Ejecutar(10);
        var antes = await reintentar.Ejecutar(10);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(1);
        var despues = await reintentar.Ejecutar(10);

        Assert.Equal(0, antes);
        Assert.Equal(1, despues);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Los_reembolsos_ya_concluidos_no_se_reintentan()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.UnReembolsoPendiente();
        var reintentar = new ReintentarReembolsosPendientes(e.Pagos.Reembolsos, e.Pagos.Unidad, e.Procesar, e.Pagos.Alertas, e.Pagos.Reloj);
        await reintentar.Ejecutar(10);

        var segunda = await reintentar.Ejecutar(10);

        Assert.Equal(0, segunda);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
    }

    // ---------- Purgar el contacto ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task El_contacto_se_elimina_a_los_30_dias_de_creada_la_intencion_haya_o_no_pedido()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var purgar = new PurgarContactoDeIntenciones(e.Repositorio, e.Unidad, Bloqueo(e), e.Alertas, e.Reloj);

        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddDays(29).AddHours(23);
        Assert.Equal(0, await purgar.Ejecutar(10));
        Assert.NotNull(e.Repositorio.Intenciones.Single().Contacto);

        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddDays(30);
        Assert.Equal(1, await purgar.Ejecutar(10));
        Assert.Null(e.Repositorio.Intenciones.Single().Contacto);
        Assert.Equal(0, await purgar.Ejecutar(10));
        Assert.Empty(e.Alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task Una_compra_aprobada_sin_pedido_a_los_30_dias_pierde_igual_el_contacto_y_publica_una_alerta()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var p = b.Entorno;
        var intencion = b.Intencion;
        intencion.AplicarAprobacion(b.Intento, "ch_1", p.Reloj.Ahora);
        await p.Repositorio.GuardarIntencion(intencion);
        var purgar = new PurgarContactoDeIntenciones(p.Repositorio, p.Unidad, Bloqueo(p), p.Alertas, p.Reloj);
        p.Reloj.Ahora = IntencionesDePrueba.Inicio.AddDays(31);

        var purgados = await purgar.Ejecutar(10);

        Assert.Equal(1, purgados);
        Assert.Null(p.Repositorio.Intenciones.Single().Contacto);
        Assert.Contains(p.Alertas.Publicadas, a => a.Motivo == PurgarContactoDeIntenciones.MotivoDeAlertaAprobadaSinPedidoALos30Dias);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public async Task Si_la_purga_falla_se_alerta_y_el_contacto_sigue_hasta_la_proxima_corrida()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var purgar = new PurgarContactoDeIntenciones(e.Repositorio, e.Unidad, Bloqueo(e), e.Alertas, e.Reloj);
        e.Reloj.Ahora = IntencionesDePrueba.Inicio.AddDays(31);
        e.Repositorio.BloqueoNoDisponible = true;

        var purgados = await purgar.Ejecutar(10);

        Assert.Equal(0, purgados);
        Assert.Contains(e.Alertas.Publicadas, a => a.Motivo == PurgarContactoDeIntenciones.MotivoDeAlertaPurgaFallida);
        Assert.NotNull(e.Repositorio.Intenciones.Single().Contacto);

        e.Repositorio.BloqueoNoDisponible = false;
        Assert.Equal(1, await purgar.Ejecutar(10));
    }

    // ---------- Depurar las intenciones antiguas ----------

    private static async Task<EntornoDeReembolsos> CompletadaHaceDosAnios()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        Assert.NotNull(e.Pagos.Repositorio.Intenciones.Single().CerradaEn);
        return e;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Las_intenciones_cerradas_hace_mas_de_dos_anios_se_desvinculan_y_se_audita_solo_la_cantidad()
    {
        var e = await CompletadaHaceDosAnios();
        var depurar = new DepurarIntencionesAntiguas(e.Pagos.Repositorio, e.Pagos.Unidad, e.Pagos.Auditoria, e.Pagos.Reloj);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddDays(365 * 2 + 1);

        var desvinculadas = await depurar.Ejecutar(10);

        Assert.Equal(1, desvinculadas);
        Assert.Null(e.Pagos.Repositorio.Intenciones.Single().CuentaId);
        var auditoria = Assert.Single(e.Pagos.Auditoria.Solicitudes);
        Assert.Equal("IntencionesDePagoDesvinculadas", auditoria.Accion.Identificador);
        Assert.Equal("1", auditoria.Detalle["cantidad"]);
        Assert.Equal(["cantidad"], auditoria.Detalle.Keys);
        Assert.Null(auditoria.ObjetoId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Antes_de_los_dos_anios_no_se_desvincula_y_repetir_no_audita_de_nuevo()
    {
        var e = await CompletadaHaceDosAnios();
        var depurar = new DepurarIntencionesAntiguas(e.Pagos.Repositorio, e.Pagos.Unidad, e.Pagos.Auditoria, e.Pagos.Reloj);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddDays(365 * 2 - 1);

        Assert.Equal(0, await depurar.Ejecutar(10));
        Assert.Empty(e.Pagos.Auditoria.Solicitudes);

        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddDays(5);
        Assert.Equal(1, await depurar.Ejecutar(10));
        Assert.Equal(0, await depurar.Ejecutar(10));
        Assert.Single(e.Pagos.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Una_intencion_abierta_o_en_curso_nunca_se_depura()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var depurar = new DepurarIntencionesAntiguas(e.Repositorio, e.Unidad, e.Auditoria, e.Reloj);
        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(365 * 3);

        Assert.Equal(0, await depurar.Ejecutar(10));
        Assert.NotNull(e.Repositorio.Intenciones.Single().CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Si_la_auditoria_cae_la_depuracion_se_revierte_entera()
    {
        var e = await CompletadaHaceDosAnios();
        var depurar = new DepurarIntencionesAntiguas(e.Pagos.Repositorio, e.Pagos.Unidad, e.Pagos.Auditoria, e.Pagos.Reloj);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddDays(365 * 2 + 1);
        e.Pagos.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => depurar.Ejecutar(10));

        Assert.NotNull(e.Pagos.Repositorio.Intenciones.Single().CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-27")]
    public async Task Completar_una_compra_anota_su_fecha_de_cierre_para_la_retencion_de_dos_anios()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();

        Assert.Equal(e.Pagos.Reloj.Ahora, e.Pagos.Repositorio.Intenciones.Single().CerradaEn);
        Assert.Equal(Pedido, e.Pagos.Repositorio.Intenciones.Single().Id);
    }
}
