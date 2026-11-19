using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class CompletarCompraTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    private sealed class Entorno
    {
        public required EntornoDeConfirmacion Base { get; init; }

        public required ConfirmacionDeCompraDoble Pedidos { get; init; }

        public required CompletarCompra Completar { get; init; }

        public IntencionDePago Intencion => Base.Intencion;

        public EntornoDePagos Pagos => Base.Entorno;

        public Task<ResultadoDeCompletarCompra> Ejecutar() => Completar.Completar(Pedido, Base.IntentoId);
    }

    /// <summary>La compra mixta ya aprobada, con las reservas confirmadas (fuente y traslado de la línea impresa), lista para el paso (c).</summary>
    private static async Task<Entorno> Aprobada(bool soloDigital = false)
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital);
        var intencion = b.Intencion;
        var ahora = b.Entorno.Reloj.Ahora;
        intencion.AplicarAprobacion(b.Intento, "ch_1", ahora);
        if (!soloDigital)
        {
            intencion.MarcarConfirmacionIniciada(ahora);
            intencion.MarcarReservasConfirmadas(ahora, [new ReservaConfirmadaDeLinea(IntencionesDePrueba.LineaImpresa, "AYA-01", true, Guid.Parse("55555555-5555-5555-5555-555555555551"), new DateOnly(2026, 11, 22))]);
        }

        intencion.Reclamar(ahora);
        await b.Entorno.Repositorio.GuardarIntencion(intencion);
        await b.Entorno.Repositorio.GuardarIntento(b.Intento);
        var pedidos = new ConfirmacionDeCompraDoble(b.Entorno.Unidad);
        var bloqueo = new BloqueoDeIntenciones(b.Entorno.Repositorio, b.Entorno.Cuenta);
        return new Entorno { Base = b, Pedidos = pedidos, Completar = new CompletarCompra(b.Entorno.Repositorio, b.Entorno.Unidad, bloqueo, pedidos, b.Entorno.Reloj) };
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Completar_entrega_la_compra_a_Pedidos_en_la_misma_transaccion_y_pasa_la_intencion_a_completada()
    {
        var e = await Aprobada();

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Completada, resultado.Tipo);
        Assert.Equal(EstadoDeIntencion.Completada, e.Intencion.Estado);
        Assert.Null(e.Intencion.ReclamadaHasta);
        Assert.Single(e.Pedidos.Compras);
        Assert.Equal(1, e.Pedidos.LlamadasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task La_compra_que_recibe_Pedidos_lleva_las_lineas_la_fuente_el_traslado_el_envio_el_contacto_y_los_textos_aceptados()
    {
        var e = await Aprobada();

        await e.Ejecutar();

        var compra = Assert.Single(e.Pedidos.Compras);
        Assert.Equal(Pedido, compra.PedidoId);
        Assert.Equal(InstantaneasDeCompraDePrueba.Cuenta, compra.CuentaId);
        Assert.Equal(TipoDeEntregaDeCompra.Domicilio, compra.TipoDeEntrega);
        Assert.Equal(140m, compra.Total);
        Assert.Equal(15m, compra.Envio);
        Assert.Equal("AYA", compra.RegionDeEntrega);
        Assert.Equal("Ana Pérez Quispe", compra.Contacto!.Nombre);
        Assert.Equal(3, compra.Aceptaciones.Count);
        var impresa = compra.Lineas.Single(l => !l.EsDigital);
        Assert.Equal("AYA-01", impresa.CodigoDeFuente);
        Assert.True(impresa.RequiereTraslado);
        Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555551"), impresa.TrasladoId);
        Assert.Equal(new DateOnly(2026, 11, 22), impresa.FechaEstimadaDeLlegada);
        Assert.Equal(100m, impresa.Importe);
        var digital = compra.Lineas.Single(l => l.EsDigital);
        Assert.Null(digital.CodigoDeFuente);
        Assert.False(digital.RequiereTraslado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task La_compra_entregada_a_Pedidos_no_repite_el_contacto_ni_los_titulos_al_imprimirla_o_serializarla()
    {
        var e = await Aprobada();

        await e.Ejecutar();

        var compra = Assert.Single(e.Pedidos.Compras);
        var texto = compra.ToString() + compra.Contacto + System.Text.Json.JsonSerializer.Serialize(compra);
        Assert.DoesNotContain("Ana Pérez", texto);
        Assert.DoesNotContain("Olivos", texto);
        Assert.DoesNotContain("987654321", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task El_aviso_PagoAprobado_a_Carrito_y_el_aviso_de_aprobado_al_cliente_se_insertan_en_la_transaccion_de_completada()
    {
        var e = await Aprobada();

        await e.Ejecutar();

        var aviso = Assert.Single(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Equal(TransicionDeAviso.Aprobado, aviso.Transicion);
        Assert.Equal(EstadoDeAvisoACarrito.Pendiente, aviso.Estado);
        Assert.Equal(e.Base.IntentoId, aviso.IntentoId);
        var pago = Assert.Single(e.Pagos.Repositorio.AvisosDePago);
        Assert.Equal(EventoDeAvisoDePago.Aprobado, pago.Evento);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task El_aviso_PagoAprobado_no_existe_antes_de_completar_el_paso_a_de_la_confirmacion_no_lo_inserta()
    {
        var e = await Aprobada();

        Assert.Empty(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Empty(e.Pagos.Repositorio.AvisosDePago);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Una_compra_solo_digital_se_completa_sin_fuentes()
    {
        var e = await Aprobada(soloDigital: true);

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Completada, resultado.Tipo);
        var compra = Assert.Single(e.Pedidos.Compras);
        Assert.Equal(TipoDeEntregaDeCompra.Digital, compra.TipoDeEntrega);
        Assert.Null(compra.Contacto);
        Assert.All(compra.Lineas, l => Assert.Null(l.CodigoDeFuente));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task La_marca_de_la_cuenta_se_lee_con_bloqueo_antes_de_bloquear_la_intencion()
    {
        var e = await Aprobada();
        e.Pagos.Orden.Clear();

        await e.Ejecutar();

        Assert.Equal(["marca-con-bloqueo", "intencion-con-bloqueo"], e.Pagos.Orden.Take(2).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-25")]
    public async Task Una_intencion_desvinculada_va_a_compensar_por_PAG_29_sin_llamar_a_Pedidos_ni_avisar()
    {
        var e = await Aprobada();
        e.Intencion.Desvincular(e.Pagos.Reloj.Ahora);

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Compensar, resultado.Tipo);
        Assert.Equal(MotivoDeCompensacion.IntencionDesvinculada, resultado.Motivo);
        Assert.Empty(e.Pedidos.Compras);
        Assert.Empty(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Empty(e.Pagos.Repositorio.AvisosDePago);
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task La_cancelacion_de_la_cuenta_en_ejecucion_va_a_compensar_por_PAG_32_sin_crear_el_pedido()
    {
        var e = await Aprobada();
        e.Pagos.Cuenta.CancelacionEnCurso = true;

        var resultado = await e.Ejecutar();

        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, resultado.Motivo);
        Assert.Empty(e.Pedidos.Compras);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task Si_Pedidos_responde_que_la_cuenta_esta_en_cancelacion_se_revierte_todo_y_se_compensa_por_PAG_32()
    {
        var e = await Aprobada();
        e.Pedidos.Resultado = ResultadoDeConfirmarCompra.CuentaEnCancelacion;

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Compensar, resultado.Tipo);
        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, resultado.Motivo);
        AssertRevertida(e);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-17")]
    [InlineData(ResultadoDeConfirmarCompra.Fallo)]
    [InlineData(ResultadoDeConfirmarCompra.DigitalYaPoseida)]
    public async Task Un_fallo_o_una_digital_ya_poseida_revierte_todo_y_compensa_como_falla_posterior_al_pago(ResultadoDeConfirmarCompra respuesta)
    {
        var e = await Aprobada();
        e.Pedidos.Resultado = respuesta;

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Compensar, resultado.Tipo);
        Assert.Equal(MotivoDeCompensacion.FallaPosteriorAlPago, resultado.Motivo);
        AssertRevertida(e);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_excepcion_no_controlada_se_reintenta_sin_compensar_y_no_deja_nada()
    {
        var e = await Aprobada();
        e.Pedidos.Lanza = true;

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Reintentar, resultado.Tipo);
        Assert.Null(resultado.Motivo);
        AssertRevertida(e);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_falla_la_insercion_de_los_avisos_se_revierte_tambien_la_intencion_completada()
    {
        var e = await Aprobada();
        e.Pagos.Repositorio.FallaAlAgregarAvisos = true;

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Reintentar, resultado.Tipo);
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Completar_una_compra_ya_completada_es_idempotente_y_no_repite_pedido_ni_avisos()
    {
        var e = await Aprobada();
        await e.Ejecutar();

        var segundo = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Completada, segundo.Tipo);
        Assert.Single(e.Pedidos.Compras);
        Assert.Single(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Single(e.Pagos.Repositorio.AvisosDePago);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_intencion_que_ya_no_esta_aprobada_se_reintenta_y_no_llama_a_Pedidos()
    {
        var e = await Aprobada();
        e.Intencion.Compensar(e.Pagos.Reloj.Ahora);

        var resultado = await e.Ejecutar();

        Assert.Equal(TipoDeCompletar.Reintentar, resultado.Tipo);
        Assert.Empty(e.Pedidos.Compras);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Con_los_impresos_sin_reservas_confirmadas_no_se_entrega_la_compra()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var intencion = b.Intencion;
        intencion.AplicarAprobacion(b.Intento, "ch_1", b.Entorno.Reloj.Ahora);
        await b.Entorno.Repositorio.GuardarIntencion(intencion);
        var pedidos = new ConfirmacionDeCompraDoble(b.Entorno.Unidad);
        var completar = new CompletarCompra(b.Entorno.Repositorio, b.Entorno.Unidad, new BloqueoDeIntenciones(b.Entorno.Repositorio, b.Entorno.Cuenta), pedidos, b.Entorno.Reloj);

        var resultado = await completar.Completar(Pedido, b.IntentoId);

        Assert.Equal(TipoDeCompletar.Reintentar, resultado.Tipo);
        Assert.Empty(pedidos.Compras);
        Assert.Equal(EstadoDeIntencion.Aprobada, b.Intencion.Estado);
    }

    private static void AssertRevertida(Entorno e)
    {
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);
        Assert.Empty(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Empty(e.Pagos.Repositorio.AvisosDePago);
    }
}
