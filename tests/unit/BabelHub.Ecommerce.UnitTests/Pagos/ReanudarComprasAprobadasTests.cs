using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class ReanudarComprasAprobadasTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    private sealed class Entorno
    {
        public required EntornoDeConfirmacion Base { get; init; }

        public required ReanudarComprasAprobadas Reanudar { get; init; }

        public required ProcesarReembolso Procesar { get; init; }

        public EntornoDePagos Pagos => Base.Entorno;

        public IntencionDePago Intencion => Base.Intencion;

        public Task<ResultadoDeReanudar> Correr() => Reanudar.Ejecutar(50);

        public void Avanzar(TimeSpan tiempo) => Pagos.Reloj.Ahora = Pagos.Reloj.Ahora.Add(tiempo);
    }

    private static async Task<Entorno> ConCompraAprobada()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var p = b.Entorno;
        var intencion = b.Intencion;
        intencion.AplicarAprobacion(b.Intento, "ch_1", p.Reloj.Ahora);
        await p.Repositorio.GuardarIntencion(intencion);
        await p.Repositorio.GuardarIntento(b.Intento);
        return Armar(b);
    }

    private static Entorno Armar(EntornoDeConfirmacion b)
    {
        var p = b.Entorno;
        var procesar = new ProcesarReembolso(p.Repositorio, p.Reembolsos, p.Unidad, p.Pasarela, p.Auditoria, p.Alertas, p.Reloj, new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        var reanudar = new ReanudarComprasAprobadas(p.Repositorio, p.Reembolsos, p.Unidad, p.Confirmador, b.Compensador, procesar, p.Alertas, p.Reloj);
        return new Entorno { Base = b, Reanudar = reanudar, Procesar = procesar };
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compra_aprobada_sin_completar_se_retoma_por_el_unico_punto_de_entrada()
    {
        var e = await ConCompraAprobada();

        var resultado = await e.Correr();

        Assert.Equal(1, resultado.Compras);
        Assert.Equal((Pedido, e.Base.IntentoId, "ch_1"), Assert.Single(e.Pagos.Confirmador.Llamadas));
        Assert.Empty(e.Base.Compensador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compra_reclamada_por_un_orquestador_en_vuelo_no_se_toca()
    {
        var e = await ConCompraAprobada();
        e.Intencion.Reclamar(e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntencion(e.Intencion);

        await e.Correr();

        Assert.Empty(e.Pagos.Confirmador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_reclamacion_vencida_ya_no_protege_a_la_compra_y_se_retoma()
    {
        var e = await ConCompraAprobada();
        e.Intencion.Reclamar(e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntencion(e.Intencion);
        e.Avanzar(TimeSpan.FromMinutes(3));

        await e.Correr();

        Assert.Single(e.Pagos.Confirmador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Antes_de_los_30_minutos_desde_aprobado_en_una_compra_que_no_se_completa_no_se_compensa()
    {
        var e = await ConCompraAprobada();
        e.Avanzar(TimeSpan.FromMinutes(29));

        await e.Correr();

        Assert.Empty(e.Base.Compensador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Pasados_los_30_minutos_desde_aprobado_en_una_compra_que_sigue_sin_completarse_se_compensa_con_alerta()
    {
        var e = await ConCompraAprobada();
        e.Avanzar(TimeSpan.FromMinutes(31));

        await e.Correr();

        var llamada = Assert.Single(e.Base.Compensador.Llamadas);
        Assert.Equal(MotivoDeCompensacion.FallaPosteriorAlPago, llamada.Motivo);
        Assert.Equal(VarianteDeCompensacion.Completa, llamada.Variante);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == ReanudarComprasAprobadas.MotivoDeAlertaCompraSinCompletar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compra_ya_completada_o_compensada_no_se_retoma()
    {
        var e = await ConCompraAprobada();
        e.Intencion.Completar(e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntencion(e.Intencion);

        var resultado = await e.Correr();

        Assert.Equal(0, resultado.Compras);
        Assert.Empty(e.Pagos.Confirmador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compensacion_ya_iniciada_se_reanuda_de_inmediato_sin_esperar_los_30_minutos()
    {
        var e = await ConCompraAprobada();
        await e.Pagos.Repositorio.AgregarCompensacion(Compensacion.Iniciar(Pedido, e.Base.IntentoId, MotivoDeCompensacion.CancelacionDeCuentaEnCurso, VarianteDeCompensacion.Completa, true, e.Pagos.Reloj.Ahora));

        var resultado = await e.Correr();

        Assert.Equal(1, resultado.Compensaciones);
        var llamada = e.Base.Compensador.Llamadas.Single(l => l.Motivo == MotivoDeCompensacion.CancelacionDeCuentaEnCurso);
        Assert.Equal(VarianteDeCompensacion.Completa, llamada.Variante);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compensacion_terminada_no_se_toca()
    {
        var e = await ConCompraAprobada();
        var terminada = Compensacion.Iniciar(Pedido, e.Base.IntentoId, MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.Completa, false, e.Pagos.Reloj.Ahora);
        terminada.MarcarReembolsoRegistrado(Guid.NewGuid(), e.Pagos.Reloj.Ahora);
        terminada.MarcarCheckoutCerrado(e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.AgregarCompensacion(terminada);

        var resultado = await e.Correr();

        Assert.Equal(0, resultado.Compensaciones);
    }

    // ---------- Reembolsos huérfanos ----------

    private static async Task<(Entorno E, Guid Id)> ConReembolsoSolicitado()
    {
        var e = await ConCompraAprobada();
        var intento = e.Base.Intento;
        var reembolso = Reembolso.CrearDeCargo(Guid.NewGuid(), OrigenDeReembolso.Compensacion, ClaveDeReembolso.Compensacion(intento.Id), Pedido, intento.Id, intento.Importe, e.Pagos.Reloj.Ahora);
        reembolso.Reclamar(e.Pagos.Reloj.Ahora);
        await e.Pagos.Reembolsos.Agregar(reembolso);
        return (e, reembolso.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_solicitado_huerfano_que_la_pasarela_ya_aplico_se_marca_reembolsado_sin_pedirlo_otra_vez()
    {
        var (e, id) = await ConReembolsoSolicitado();
        e.Avanzar(TimeSpan.FromMinutes(3));
        e.Pagos.Pasarela.ReembolsosDelCargo = [new ReembolsoEnPasarela(id, "re_5", Importe.De(140m))];

        var resultado = await e.Correr();

        Assert.Equal(1, resultado.Reembolsos);
        Assert.Equal(EstadoDeReembolso.Reembolsado, e.Pagos.Reembolsos.Todos.Single().Estado);
        Assert.Equal("re_5", e.Pagos.Reembolsos.Todos.Single().ReferenciaDePasarela);
        Assert.Empty(e.Pagos.Pasarela.Reembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_huerfano_que_la_pasarela_no_aplico_vuelve_a_pendiente_para_su_reintento()
    {
        var (e, _) = await ConReembolsoSolicitado();
        e.Avanzar(TimeSpan.FromMinutes(3));

        await e.Correr();

        var reembolso = e.Pagos.Reembolsos.Todos.Single();
        Assert.Equal(EstadoDeReembolso.Pendiente, reembolso.Estado);
        Assert.Equal(MotivoPendienteDeReembolso.SinRespuesta, reembolso.MotivoPendiente);
        Assert.Empty(e.Pagos.Pasarela.Reembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_no_se_puede_consultar_la_pasarela_el_huerfano_se_deja_como_esta()
    {
        var (e, _) = await ConReembolsoSolicitado();
        e.Avanzar(TimeSpan.FromMinutes(3));
        e.Pagos.Pasarela.ReembolsosDelCargo = null;

        await e.Correr();

        Assert.Equal(EstadoDeReembolso.Solicitado, e.Pagos.Reembolsos.Todos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_solicitado_hace_poco_aun_esta_en_vuelo_y_no_se_consulta()
    {
        var (e, _) = await ConReembolsoSolicitado();
        e.Avanzar(TimeSpan.FromSeconds(30));

        var resultado = await e.Correr();

        Assert.Equal(0, resultado.Reembolsos);
        Assert.Equal(0, e.Pagos.Pasarela.ConsultasDeReembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_falla_con_un_elemento_alerta_y_no_detiene_a_los_demas()
    {
        var (e, id) = await ConReembolsoSolicitado();
        e.Avanzar(TimeSpan.FromMinutes(3));
        e.Base.Compensador.Lanza = true;
        await e.Pagos.Repositorio.AgregarCompensacion(Compensacion.Iniciar(Pedido, e.Base.IntentoId, MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.Completa, true, e.Pagos.Reloj.Ahora));
        e.Pagos.Pasarela.ReembolsosDelCargo = [new ReembolsoEnPasarela(id, "re_5", Importe.De(140m))];

        var resultado = await e.Correr();

        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == ReanudarComprasAprobadas.MotivoDeAlertaRetomaFallida);
        Assert.Equal(1, resultado.Reembolsos);
        Assert.Equal(EstadoDeReembolso.Reembolsado, e.Pagos.Reembolsos.Todos.Single().Estado);
        Assert.Single(e.Pagos.Confirmador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Reanudar_un_reembolso_que_no_existe_no_se_encuentra()
    {
        var e = await ConCompraAprobada();

        var resultado = await e.Procesar.Reanudar(Guid.NewGuid());

        Assert.Equal(ClavesDePagos.NoEncontrado, resultado.Error!.Clave);
    }
}
