using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.EntornoDePagosExtensiones;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class IniciarCobroAvisosTests
{
    private static async Task<CobrarEntrada> Pedir(EntornoDePagos e)
    {
        var clave = (await e.EmitirClave.Ejecutar(new EmitirClaveDeIntentoEntrada(InstantaneasDeCompraDePrueba.Pedido))).Valor;
        return new CobrarEntrada(InstantaneasDeCompraDePrueba.Pedido, clave.Token, Application.Pagos.MedioDePago.Yape, TokenDelMedioDePago.Crear("tkn_test_abc123XYZ"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Un_rechazo_anota_los_avisos_en_su_transaccion_y_despues_de_confirmar_avisa_a_Carrito()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.Otro);

        await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Equal(TransicionDeAviso.Rechazado, Assert.Single(e.Repositorio.AvisosACarrito).Transicion);
        Assert.Equal(EventoDeAvisoDePago.Rechazado, Assert.Single(e.Repositorio.AvisosDePago).Evento);
        Assert.Single(e.Avisador.Enviados);
        Assert.Equal(0, e.Avisador.LlamadasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Si_la_auditoria_cae_el_rechazo_no_deja_avisos()
    {
        var e = await ConIntencionLista();
        e.Pasarela.Respuesta = _ =>
        {
            e.Auditoria.Caida = true;
            return ResultadoDeCobro.Rechazado(MotivoDeRechazo.Otro);
        };

        await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Empty(e.Repositorio.AvisosACarrito);
        Assert.Empty(e.Repositorio.AvisosDePago);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Un_pendiente_con_impresos_anota_el_aviso_retenido_y_lo_libera_cuando_termina_reservas_extender()
    {
        var e = await ConIntencionLista();
        e.Pasarela.NoResponde = true;

        await e.Cobrar.Ejecutar(await Pedir(e));

        var aviso = Assert.Single(e.Repositorio.AvisosACarrito);
        Assert.Equal(TransicionDeAviso.Pendiente, aviso.Transicion);
        Assert.Equal(EstadoDeAvisoACarrito.Retenido, aviso.Estado);
        Assert.Equal(EventoDeAvisoDePago.Pendiente, Assert.Single(e.Repositorio.AvisosDePago).Evento);
        Assert.Single(e.Reservas.Extensiones);
        Assert.Single(e.Avisador.Liberados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Si_reservas_extender_falla_el_aviso_sigue_retenido_para_la_consulta_del_trabajo()
    {
        var e = await ConIntencionLista();
        e.Pasarela.NoResponde = true;
        e.Reservas.Extension = new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null);

        await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Empty(e.Avisador.Liberados);
        Assert.Equal(EstadoDeAvisoACarrito.Retenido, e.Repositorio.AvisosACarrito.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Un_aprobado_no_anota_avisos_en_el_cobro_porque_el_PagoAprobado_nace_al_completar()
    {
        var e = await ConIntencionLista();

        await e.Cobrar.Ejecutar(await Pedir(e));

        Assert.Empty(e.Repositorio.AvisosACarrito);
        Assert.Empty(e.Repositorio.AvisosDePago);
    }
}
