using System.Text.Json;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using DominioMedioDePago = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Los modelos del contrato público de Pagos y los puertos propios (T07; PAG-02, PAG-27, PAG-30, AC-PAG-02, AC-PAG-26).</summary>
public sealed class ContratosDePagosTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void Los_modelos_del_cobro_no_muestran_el_token_ni_el_token_de_la_clave()
    {
        var cobrar = new CobrarEntrada(Guid.NewGuid(), "clave-firmada-secreta", BabelHub.Ecommerce.Application.Pagos.MedioDePago.Tarjeta, TokenDelMedioDePago.Crear("tkn_test_abc123XYZ"));
        var emitida = new ClaveDeIntentoEmitida("token.firmado.secreto", DateTimeOffset.UnixEpoch, 123.90m, DateTimeOffset.UnixEpoch, null);

        var texto = cobrar + " " + emitida + JsonSerializer.Serialize(cobrar);

        Assert.DoesNotContain("abc123XYZ", texto);
        Assert.DoesNotContain("secreta", texto);
        Assert.DoesNotContain("firmado", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public void El_contacto_de_la_compra_aprobada_y_la_compra_no_se_muestran_ni_se_serializan()
    {
        var contacto = new ContactoDeLaCompra("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123", "AYA");
        var compra = new CompraAprobada(
            Guid.NewGuid(), Guid.NewGuid(), TipoDeEntregaDeCompra.Domicilio, null, "AYA", 15m, 140m, contacto, [],
            [new LineaDeCompraAprobada(Guid.NewGuid(), Guid.NewGuid(), false, "La ciudad y los perros", "9780306406157", 1, 125m, 125m, "AYA-01", false, null, null)]);

        var texto = contacto + " " + compra + JsonSerializer.Serialize(contacto) + JsonSerializer.Serialize(new { contacto });

        foreach (var secreto in new[] { "Ana", "987654321", "Olivos", "ciudad", "9780306406157" })
        {
            Assert.DoesNotContain(secreto, texto);
        }

        Assert.Equal("Ana Pérez Quispe", contacto.Nombre);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public void Lo_que_viaja_a_la_pasarela_no_tiene_un_campo_para_el_contacto_de_entrega()
    {
        var campos = typeof(SolicitudDeCobroEnPasarela).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["ReferenciaOpaca", "Importe", "Medio", "Token", "CorreoDelTitular"], campos);
        Assert.DoesNotContain(campos, c => c.Contains("Nombre", StringComparison.OrdinalIgnoreCase) || c.Contains("Telefono", StringComparison.OrdinalIgnoreCase) || c.Contains("Direccion", StringComparison.OrdinalIgnoreCase) || c.Contains("Contacto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void La_solicitud_a_la_pasarela_no_repite_el_token_ni_el_correo_en_su_texto()
    {
        var solicitud = new SolicitudDeCobroEnPasarela(Guid.NewGuid(), Importe.De(123.90m), DominioMedioDePago.Yape, TokenDelMedioDePago.Crear("tkn_test_abc123XYZ"), "ana@correo.com");

        var texto = solicitud.ToString() + JsonSerializer.Serialize(solicitud.Token);

        Assert.DoesNotContain("abc123XYZ", texto);
        Assert.DoesNotContain("ana@correo.com", texto);
        Assert.Contains("123.90", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public void El_aviso_de_la_pasarela_no_repite_su_cuerpo_ni_su_firma()
    {
        var aviso = new AvisoDeLaPasarelaEntrada(System.Text.Encoding.UTF8.GetBytes("{\"cargo\":\"ch_secreto\"}"), "1700000000", "firma-secreta");

        Assert.DoesNotContain("secreto", aviso.ToString());
        Assert.DoesNotContain("secreta", aviso.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public void Una_respuesta_de_reserva_solo_es_correcta_con_su_resultado_correcto()
    {
        Assert.True(new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.Correcto, true).EsCorrecta);
        Assert.All(
            new[] { ResultadoDeLaLlamadaDeReserva.EstadoInvalido, ResultadoDeLaLlamadaDeReserva.NoExiste, ResultadoDeLaLlamadaDeReserva.SinRespuesta },
            r => Assert.False(new RespuestaDeReserva<bool>(r, false).EsCorrecta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Los_resultados_de_reembolso_en_la_pasarela_distinguen_aplicado_rechazado_no_soportado_y_sin_respuesta()
    {
        Assert.Equal("rf_1", ResultadoDeReembolsoEnPasarela.Aplicado("rf_1").Referencia);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.NoSoportado, ResultadoDeReembolsoEnPasarela.NoSoportado.Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, ResultadoDeReembolsoEnPasarela.Rechazado.Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, ResultadoDeReembolsoEnPasarela.SinRespuesta.Tipo);
        Assert.DoesNotContain("rf_1", ResultadoDeReembolsoEnPasarela.Aplicado("rf_1").ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void Los_motivos_de_rechazo_publicos_coinciden_con_los_del_dominio()
    {
        Assert.Equal(Enum.GetNames<MotivoDeRechazo>(), Enum.GetNames<MotivoDeRechazoDePago>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public void Los_origenes_y_estados_de_reembolso_publicos_coinciden_con_los_del_dominio()
    {
        Assert.Equal(Enum.GetNames<OrigenDeReembolso>(), Enum.GetNames<OrigenDelReembolso>());
        Assert.Equal(Enum.GetNames<EstadoDeReembolso>(), Enum.GetNames<EstadoDelReembolso>());
        Assert.Equal(Enum.GetNames<MotivoPendienteDeReembolso>(), Enum.GetNames<MotivoPendienteDelReembolso>());
    }
}
