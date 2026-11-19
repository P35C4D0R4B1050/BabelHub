using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class AvisarACarritoTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    private sealed class Entorno
    {
        public required EntornoDeConfirmacion Base { get; init; }

        public required ProgresoDelCheckoutDoble Carrito { get; init; }

        public required AvisarACarrito Avisar { get; init; }

        public EntornoDePagos Pagos => Base.Entorno;

        public Guid IntentoId => Base.IntentoId;

        public Task Anotar(TransicionDeAviso transicion, bool retenido = false) =>
            Pagos.Repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(Pedido, IntentoId, transicion, retenido, Pagos.Reloj.Ahora));

        public IEnumerable<AvisoACarrito> Avisos => Pagos.Repositorio.AvisosACarrito;
    }

    private static async Task<Entorno> Nuevo(bool soloDigital = false)
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital);
        var carrito = new ProgresoDelCheckoutDoble(b.Entorno.Unidad);
        return new Entorno { Base = b, Carrito = carrito, Avisar = new AvisarACarrito(b.Entorno.Repositorio, b.Entorno.Unidad, carrito, b.Entorno.Alertas, b.Entorno.Reloj) };
    }

    private static async Task Resolver(Entorno e, Func<IntentoDeCobro, bool> cambio)
    {
        var intento = e.Pagos.Repositorio.Intentos.Single();
        cambio(intento);
        await e.Pagos.Repositorio.GuardarIntento(intento);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-24")]
    [InlineData(TransicionDeAviso.Aprobado, "aprobado")]
    [InlineData(TransicionDeAviso.Rechazado, "resuelto:Rechazado")]
    [InlineData(TransicionDeAviso.Vencido, "resuelto:Vencido")]
    [InlineData(TransicionDeAviso.Compensado, "resuelto:Compensado")]
    public async Task Cada_transicion_se_avisa_a_Carrito_despues_de_confirmar_y_queda_enviada(TransicionDeAviso transicion, string esperado)
    {
        var e = await Nuevo();
        await e.Anotar(transicion);

        await e.Avisar.Enviar(Pedido);

        Assert.Equal(esperado, Assert.Single(e.Carrito.Recibidos));
        Assert.Equal(EstadoDeAvisoACarrito.Enviado, Assert.Single(e.Avisos).Estado);
        Assert.Equal(0, e.Carrito.LlamadasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Enviar_otra_vez_no_repite_un_aviso_ya_enviado()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Aprobado);

        await e.Avisar.Enviar(Pedido);
        await e.Avisar.Enviar(Pedido);

        Assert.Single(e.Carrito.Recibidos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task El_pendiente_con_impresos_nace_retenido_y_no_sale_hasta_que_termina_reservas_extender()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Pendiente, retenido: true);

        await e.Avisar.Enviar(Pedido);

        Assert.Empty(e.Carrito.Recibidos);
        Assert.Equal(EstadoDeAvisoACarrito.Retenido, e.Avisos.Single().Estado);

        await e.Avisar.LiberarElPendienteRetenido(Pedido, e.IntentoId);

        Assert.Equal("pendiente", Assert.Single(e.Carrito.Recibidos));
        Assert.Equal(EstadoDeAvisoACarrito.Enviado, e.Avisos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Los_instantes_del_pendiente_son_los_de_Pagos_leidos_al_enviar_tras_extender_la_reserva()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Pendiente, retenido: true);
        var intencion = e.Base.Intencion;
        intencion.ExtenderReserva(IntencionesDePrueba.Inicio.AddMinutes(30));
        await e.Pagos.Repositorio.GuardarIntencion(intencion);

        await e.Avisar.LiberarElPendienteRetenido(Pedido, e.IntentoId);

        var (limite, reserva) = Assert.Single(e.Carrito.InstantesDePendiente);
        Assert.Equal(intencion.LimitePendienteEn, limite);
        Assert.Equal(IntencionesDePrueba.Inicio.AddMinutes(30), reserva);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Una_compra_solo_digital_informa_el_limite_de_espera_porque_no_tiene_reserva()
    {
        var e = await Nuevo(soloDigital: true);
        await e.Anotar(TransicionDeAviso.Pendiente);

        await e.Avisar.Enviar(Pedido);

        var (limite, reserva) = Assert.Single(e.Carrito.InstantesDePendiente);
        Assert.Equal(e.Base.Intencion.LimitePendienteEn, limite);
        Assert.Equal(limite, reserva);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Un_pendiente_de_un_intento_ya_resuelto_pasa_a_omitido_y_sale_el_aviso_de_lo_que_paso()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Pendiente);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddSeconds(5);
        await e.Anotar(TransicionDeAviso.Rechazado);
        await Resolver(e, i => i.MarcarRechazado(MotivoDeRechazo.Otro, e.Pagos.Reloj.Ahora));

        await e.Avisar.Enviar(Pedido);

        Assert.Equal("resuelto:Rechazado", Assert.Single(e.Carrito.Recibidos));
        Assert.Equal(EstadoDeAvisoACarrito.Omitido, e.Avisos.Single(a => a.Transicion == TransicionDeAviso.Pendiente).Estado);
        Assert.Equal(EstadoDeAvisoACarrito.Enviado, e.Avisos.Single(a => a.Transicion == TransicionDeAviso.Rechazado).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Los_avisos_de_una_intencion_salen_en_orden_de_creacion_aunque_se_guardaran_intercalados()
    {
        var e = await Nuevo();
        await Resolver(e, i => i.Vencer(e.Pagos.Reloj.Ahora));
        var ahora = e.Pagos.Reloj.Ahora;
        // Insertados al revés: primero el de compensado (más nuevo) y después el de vencido (más antiguo).
        await e.Pagos.Repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(Pedido, e.IntentoId, TransicionDeAviso.Compensado, false, ahora.AddSeconds(10)));
        await e.Pagos.Repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(Pedido, e.IntentoId, TransicionDeAviso.Vencido, false, ahora));

        await e.Avisar.Enviar(Pedido);

        Assert.Equal(["resuelto:Vencido", "resuelto:Compensado"], e.Carrito.Recibidos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Si_Carrito_cae_el_aviso_se_reintenta_con_espera_y_los_siguientes_no_se_adelantan()
    {
        var e = await Nuevo();
        await Resolver(e, i => i.Vencer(e.Pagos.Reloj.Ahora));
        await e.Anotar(TransicionDeAviso.Vencido);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddSeconds(1);
        await e.Anotar(TransicionDeAviso.Compensado);
        e.Carrito.Falla = true;

        await e.Avisar.Enviar(Pedido);

        Assert.Single(e.Carrito.Recibidos);
        var vencido = e.Avisos.Single(a => a.Transicion == TransicionDeAviso.Vencido);
        Assert.Equal(EstadoDeAvisoACarrito.Pendiente, vencido.Estado);
        Assert.Equal(1, vencido.Intentos);
        Assert.NotNull(vencido.ProximoIntentoEn);

        // Antes de la espera no se vuelve a intentar.
        e.Carrito.Falla = false;
        await e.Avisar.Enviar(Pedido);
        Assert.Single(e.Carrito.Recibidos);

        // Pasada la espera salen los dos, en orden.
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(1);
        await e.Avisar.Enviar(Pedido);
        Assert.Equal(["resuelto:Vencido", "resuelto:Vencido", "resuelto:Compensado"], e.Carrito.Recibidos);
        Assert.All(e.Avisos, a => Assert.Equal(EstadoDeAvisoACarrito.Enviado, a.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Si_el_checkout_aun_reserva_Carrito_responde_aun_no_y_se_reintenta()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Aprobado);
        e.Carrito.Respuesta = ResultadoDelAvisoDePago.AunNo;

        await e.Avisar.Enviar(Pedido);

        Assert.Equal(EstadoDeAvisoACarrito.Pendiente, e.Avisos.Single().Estado);
        Assert.Equal(1, e.Avisos.Single().Intentos);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-24")]
    [InlineData(ResultadoDelAvisoDePago.SinCambios)]
    [InlineData(ResultadoDelAvisoDePago.NoAplica)]
    public async Task Un_aviso_sin_cambios_o_que_ya_no_aplica_se_da_por_aceptado(ResultadoDelAvisoDePago respuesta)
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Aprobado);
        e.Carrito.Respuesta = respuesta;

        await e.Avisar.Enviar(Pedido);

        Assert.Equal(EstadoDeAvisoACarrito.Enviado, e.Avisos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Una_falla_del_envio_nunca_interrumpe_ni_cambia_el_estado_del_pago()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Aprobado);
        e.Carrito.Falla = true;
        var estadoAntes = e.Base.Intencion.Estado;

        await e.Avisar.Enviar(Pedido);

        Assert.Equal(estadoAntes, e.Base.Intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Si_la_base_cae_al_leer_los_avisos_se_publica_una_alerta_sin_interrumpir()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Aprobado);
        e.Pagos.Repositorio.FallaAlAgregarAvisos = false;
        var carrito = new ProgresoDelCheckoutDoble(e.Pagos.Unidad);
        var avisar = new AvisarACarrito(new RepositorioQueCae(e.Pagos.Repositorio), e.Pagos.Unidad, carrito, e.Pagos.Alertas, e.Pagos.Reloj);

        await avisar.Enviar(Pedido);

        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == AvisarACarrito.MotivoDeAlertaAvisoACarritoFallido);
        Assert.Empty(carrito.Recibidos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task El_trabajo_atiende_las_intenciones_con_avisos_listos_y_ninguna_si_no_hay()
    {
        var e = await Nuevo();

        var ninguna = await e.Avisar.EnviarPendientes(10);
        await e.Anotar(TransicionDeAviso.Aprobado);
        var una = await e.Avisar.EnviarPendientes(10);

        Assert.Equal(0, ninguna);
        Assert.Equal(1, una);
        Assert.Equal("aprobado", Assert.Single(e.Carrito.Recibidos));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Los_avisos_no_llevan_datos_personales_ni_de_pago()
    {
        var e = await Nuevo();
        await e.Anotar(TransicionDeAviso.Pendiente, retenido: true);

        var texto = string.Join(' ', e.Carrito.Recibidos) + e.Avisos.Single();

        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("ch_", texto);
    }

    /// <summary>Un repositorio que cae al leer los avisos.</summary>
    private sealed class RepositorioQueCae(IRepositorioDePagos interno) : IRepositorioDePagos
    {
        public Task<IReadOnlyList<AvisoACarrito>> ListarAvisosACarritoDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default) => throw new InvalidOperationException("La base cayó.");

        public Task<IntencionDePago?> ObtenerIntencion(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default) => interno.ObtenerIntencion(pedidoId, paraActualizar, cancelacion);

        public Task AgregarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default) => interno.AgregarIntencion(intencion, cancelacion);

        public Task GuardarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default) => interno.GuardarIntencion(intencion, cancelacion);

        public Task<IntentoDeCobro?> ObtenerIntento(Guid intentoId, CancellationToken cancelacion = default) => interno.ObtenerIntento(intentoId, cancelacion);

        public Task<IntentoDeCobro?> ObtenerIntentoActivo(Guid intencionId, CancellationToken cancelacion = default) => interno.ObtenerIntentoActivo(intencionId, cancelacion);

        public Task<ResultadoDeAgregarIntento> AgregarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default) => interno.AgregarIntento(intento, cancelacion);

        public Task GuardarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default) => interno.GuardarIntento(intento, cancelacion);

        public Task<Compensacion?> ObtenerCompensacion(Guid intentoId, CancellationToken cancelacion = default) => interno.ObtenerCompensacion(intentoId, cancelacion);

        public Task<bool> AgregarCompensacion(Compensacion compensacion, CancellationToken cancelacion = default) => interno.AgregarCompensacion(compensacion, cancelacion);

        public Task GuardarCompensacion(Compensacion compensacion, CancellationToken cancelacion = default) => interno.GuardarCompensacion(compensacion, cancelacion);

        public Task<bool> AgregarAvisoACarrito(AvisoACarrito aviso, CancellationToken cancelacion = default) => interno.AgregarAvisoACarrito(aviso, cancelacion);

        public Task<bool> AgregarAvisoDePago(AvisoDePago aviso, CancellationToken cancelacion = default) => interno.AgregarAvisoDePago(aviso, cancelacion);

        public Task<IReadOnlyList<AvisoDePago>> ListarAvisosDePagoDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default) => interno.ListarAvisosDePagoDeLaIntencion(intencionId, cancelacion);

        public Task<IReadOnlyList<Guid>> ListarIntencionesConAvisosACarritoListos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) => interno.ListarIntencionesConAvisosACarritoListos(ahora, limite, cancelacion);

        public Task GuardarAvisoACarrito(AvisoACarrito aviso, CancellationToken cancelacion = default) => interno.GuardarAvisoACarrito(aviso, cancelacion);

        public Task GuardarAvisoDePago(AvisoDePago aviso, CancellationToken cancelacion = default) => interno.GuardarAvisoDePago(aviso, cancelacion);
    }
}
