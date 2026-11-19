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
        var purgar = new PurgarContactoDeIntenciones(e.Repositorio, e.Unidad, Bloqueo(e), e.Auditoria, e.Alertas, e.Reloj);

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
        var purgar = new PurgarContactoDeIntenciones(p.Repositorio, p.Unidad, Bloqueo(p), p.Auditoria, p.Alertas, p.Reloj);
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
        var purgar = new PurgarContactoDeIntenciones(e.Repositorio, e.Unidad, Bloqueo(e), e.Auditoria, e.Alertas, e.Reloj);
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

public sealed class ReintentoAutomaticoDeReembolsosTests
{
    private static Reembolso Pendiente(MotivoPendienteDeReembolso? motivo, int intentos, DateTimeOffset inicio)
    {
        var r = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, "comp:x", Guid.NewGuid(), Guid.NewGuid(), Importe.De(10m), inicio);
        var momento = inicio;
        for (var i = 0; i < intentos; i++)
        {
            r.Reclamar(momento);
            r.DejarPendiente(motivo ?? MotivoPendienteDeReembolso.SinRespuesta, momento);
            momento = momento.AddHours(2);
        }

        return r;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_reembolso_que_nunca_se_pidio_se_reintenta_de_inmediato()
    {
        Assert.True(Pendiente(null, 0, IntencionesDePrueba.Inicio).PuedeReintentarseAutomaticamente(IntencionesDePrueba.Inicio));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-17")]
    [InlineData(MotivoPendienteDeReembolso.RechazadoPorPasarela)]
    [InlineData(MotivoPendienteDeReembolso.NoSoportado)]
    public void Un_rechazo_o_un_medio_no_soportado_nunca_se_reintenta_solo_pero_la_administracion_si_puede(MotivoPendienteDeReembolso motivo)
    {
        var r = Pendiente(motivo, 1, IntencionesDePrueba.Inicio);
        var despues = IntencionesDePrueba.Inicio.AddDays(1);

        Assert.False(r.PuedeReintentarseAutomaticamente(despues));
        Assert.True(r.PuedeReintentarse(despues));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Tras_un_sin_respuesta_la_espera_crece_con_cada_intento_y_tiene_un_tope()
    {
        var inicio = IntencionesDePrueba.Inicio;
        var uno = Pendiente(MotivoPendienteDeReembolso.SinRespuesta, 1, inicio);
        var tres = Pendiente(MotivoPendienteDeReembolso.SinRespuesta, 3, inicio);
        var ultimoUno = uno.UltimoIntentoEn!.Value;
        var ultimoTres = tres.UltimoIntentoEn!.Value;

        Assert.True(uno.PuedeReintentarseAutomaticamente(ultimoUno.AddSeconds(31)));
        Assert.False(tres.PuedeReintentarseAutomaticamente(ultimoTres.AddSeconds(31)));
        Assert.True(tres.PuedeReintentarseAutomaticamente(ultimoTres.AddMinutes(3)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Tras_el_maximo_de_intentos_solo_lo_reintenta_la_administracion()
    {
        var r = Pendiente(MotivoPendienteDeReembolso.SinRespuesta, Reembolso.MaximoDeReintentosAutomaticos, IntencionesDePrueba.Inicio);
        var despues = r.UltimoIntentoEn!.Value.AddDays(2);

        Assert.False(r.PuedeReintentarseAutomaticamente(despues));
        Assert.True(r.PuedeReintentarse(despues));
    }
}

/// <summary>La purga de los avisos de pago y de Carrito a los 90 días (T24, T27): sin datos personales, así que solo se acota por la edad y por tanda.</summary>
public sealed class PurgarAvisosDePagosTests
{
    [Fact]
    public async Task Purga_los_avisos_de_mas_de_noventa_dias_y_conserva_los_recientes()
    {
        var e = new EntornoDePagos();
        e.Repositorio.Sembrar(IntencionesDePrueba.Abierta());
        var intento = Guid.NewGuid();
        var viejo = AvisoDePago.Crear(IntencionesDePrueba.PedidoId, intento, EventoDeAvisoDePago.Aprobado, e.Reloj.Ahora.AddDays(-91));
        var reciente = AvisoDePago.Crear(IntencionesDePrueba.PedidoId, intento, EventoDeAvisoDePago.Vencido, e.Reloj.Ahora.AddDays(-89));
        var viejoACarrito = AvisoACarrito.Crear(IntencionesDePrueba.PedidoId, intento, TransicionDeAviso.Aprobado, retenido: false, e.Reloj.Ahora.AddDays(-91));
        await e.Unidad.Ejecutar(async token =>
        {
            await e.Repositorio.AgregarAvisoDePago(viejo, token);
            await e.Repositorio.AgregarAvisoDePago(reciente, token);
            await e.Repositorio.AgregarAvisoACarrito(viejoACarrito, token);
            return 0;
        });

        var purgados = await new PurgarAvisosDePagos(e.Repositorio, e.Unidad, e.Reloj).Ejecutar(50);

        Assert.Equal(2, purgados);
        Assert.Equal(EventoDeAvisoDePago.Vencido, Assert.Single(e.Repositorio.AvisosDePago).Evento);
        Assert.Empty(e.Repositorio.AvisosACarrito);
    }

    [Fact]
    public async Task El_limite_de_noventa_dias_es_exclusivo_y_la_tanda_acota()
    {
        var e = new EntornoDePagos();
        var intento = Guid.NewGuid();
        await e.Unidad.Ejecutar(async token =>
        {
            await e.Repositorio.AgregarAvisoDePago(AvisoDePago.Crear(IntencionesDePrueba.PedidoId, intento, EventoDeAvisoDePago.Aprobado, e.Reloj.Ahora - PurgarAvisosDePagos.Retencion), token);
            await e.Repositorio.AgregarAvisoDePago(AvisoDePago.Crear(IntencionesDePrueba.PedidoId, intento, EventoDeAvisoDePago.Vencido, e.Reloj.Ahora - PurgarAvisosDePagos.Retencion - TimeSpan.FromSeconds(1)), token);
            return 0;
        });

        var purgados = await new PurgarAvisosDePagos(e.Repositorio, e.Unidad, e.Reloj).Ejecutar(50);

        Assert.Equal(1, purgados);
        Assert.Equal(EventoDeAvisoDePago.Aprobado, Assert.Single(e.Repositorio.AvisosDePago).Evento);
    }
}
