using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

/// <summary>
/// Las plantillas de correo con campos declarados (PLA-X; IDE-67, AC-IDE-56, PAG-05, REC-07, SEG-10): el renderizador pone solo los campos que la plantilla declara, rechaza el correo si falta uno, limpia los controles
/// (ningún dato abre una cabecera ni parte el asunto) y deja intactas las plantillas de texto fijo de seguridad, cuyos datos nunca entran. Las plantillas de Pagos y de reclamaciones entran por su proveedor.
/// </summary>
public sealed class PlantillasDeCorreoConCamposTests
{
    private static CorreoSaliente Correo(CategoriaDeCorreo categoria, string plantilla, params (string Clave, string Valor)[] datos) =>
        new(categoria, "ana@example.com", plantilla, datos.ToDictionary(d => d.Clave, d => d.Valor));

    private static RenderizadorDeCorreoDeProduccion Renderizador() =>
        RenderizadorDeCorreoDeProduccion.Con([new PlantillasDeCorreoDePagosParaPlataforma(), new PlantillasDeCorreoDeReclamos()]);

    [Fact]
    public void LaRespuestaDeUnReclamo_LlevaElNumeroLaFechaYLaRespuesta_SinElDocumentoNiElDomicilio()
    {
        var mensaje = Renderizador().De(Correo(
            CategoriaDeCorreo.RespuestaDeReclamo,
            PlantillasDeCorreoDeReclamos.RespuestaDeReclamo,
            ("numero", "2026-000123"),
            ("fecha", "2026-11-19"),
            ("respuesta", "Revisamos tu caso.\nTe devolvemos el importe."),
            ("documento", "12345678"),
            ("domicilio", "Jr. Los Libros 123")));

        Assert.Equal(["ana@example.com"], mensaje.Destinatarios);
        Assert.Equal("BabelHub: respondimos tu hoja de reclamación 2026-000123", mensaje.Asunto);
        Assert.Contains("2026-000123 el 2026-11-19", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("Revisamos tu caso.\nTe devolvemos el importe.", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Los Libros", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.Contains(PlantillasDeCorreoDeSeguridad.Firma, mensaje.Cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public void UnDatoQueFalta_HaceQueElCorreoNoSalga()
    {
        var sinRespuesta = Correo(CategoriaDeCorreo.RespuestaDeReclamo, PlantillasDeCorreoDeReclamos.RespuestaDeReclamo, ("numero", "2026-000123"), ("fecha", "2026-11-19"));

        Assert.Throws<InvalidOperationException>(() => Renderizador().De(sinRespuesta));
    }

    [Fact]
    public void LosControlesDeUnDato_SeVuelvenEspacios_EnElAsuntoSinSaltos_YEnElCuerpoSoloSeConservaElSaltoDeLinea()
    {
        var mensaje = Renderizador().De(Correo(
            CategoriaDeCorreo.RespuestaDeReclamo,
            PlantillasDeCorreoDeReclamos.RespuestaDeReclamo,
            ("numero", "2026-000123\r\nBcc: otra@example.com"),
            ("fecha", "2026-11-19"),
            ("respuesta", "uno\r\ndos\u0007tres")));

        Assert.DoesNotContain('\r', mensaje.Asunto);
        Assert.DoesNotContain('\n', mensaje.Asunto);
        Assert.Contains("uno \ndos tres", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', mensaje.Cuerpo);
        Assert.DoesNotContain('\u0007', mensaje.Cuerpo);
    }

    [Fact]
    public void UnDatoQueContieneLaMarcaDeOtroCampo_NoColaElValorDeEseCampo()
    {
        var mensaje = Renderizador().De(Correo(
            CategoriaDeCorreo.RespuestaDeReclamo,
            PlantillasDeCorreoDeReclamos.RespuestaDeReclamo,
            ("numero", "{respuesta}"),
            ("fecha", "2026-11-19"),
            ("respuesta", "TEXTO-SECRETO-DE-OTRO-CAMPO")));

        Assert.Equal("BabelHub: respondimos tu hoja de reclamación {respuesta}", mensaje.Asunto);
        Assert.Equal(1, mensaje.Cuerpo.Split("TEXTO-SECRETO-DE-OTRO-CAMPO").Length - 1);
    }

    [Theory]
    [InlineData(' ')]
    [InlineData(' ')]
    [InlineData('\u0085')]
    public void LosSeparadoresDeLineaUnicode_SeVuelvenEspacios_AlmenosEnElAsunto(char separador)
    {
        var mensaje = Renderizador().De(Correo(
            CategoriaDeCorreo.RespuestaDeReclamo,
            PlantillasDeCorreoDeReclamos.RespuestaDeReclamo,
            ("numero", $"2026{separador}000123"),
            ("fecha", "2026-11-19"),
            ("respuesta", "ok")));

        Assert.DoesNotContain(separador, mensaje.Asunto);
        Assert.Contains("2026 000123", mensaje.Asunto, StringComparison.Ordinal);
    }

    [Fact]
    public void UnDatoMuyLargo_SeRecorta()
    {
        var mensaje = Renderizador().De(Correo(
            CategoriaDeCorreo.RespuestaDeReclamo,
            PlantillasDeCorreoDeReclamos.RespuestaDeReclamo,
            ("numero", "2026-000123"),
            ("fecha", "2026-11-19"),
            ("respuesta", new string('x', 50_000))));

        Assert.True(mensaje.Cuerpo.Length < RenderizadorDeCorreoDeProduccion.LargoMaximoDeUnDato + 1000);
    }

    [Fact]
    public void LasPlantillasDePagos_PonenElMotivoYElImporteDeLaListaCerrada_YElRestoDeLosDatosNoEntra()
    {
        var renderizador = Renderizador();

        var rechazado = renderizador.De(Correo(CategoriaDeCorreo.Pago, "pago-rechazado", ("motivo", "tu tarjeta fue rechazada"), ("numeroDeTarjeta", "4111111111111111")));
        var reembolso = renderizador.De(Correo(CategoriaDeCorreo.Pago, "reembolso-automatico", ("importe", "S/ 1 234,50")));
        var aprobado = renderizador.De(Correo(CategoriaDeCorreo.Pago, "pago-aprobado", ("importe", "S/ 1,00")));

        Assert.Contains("tu tarjeta fue rechazada", rechazado.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("4111", rechazado.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("S/ 1 234,50", reembolso.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("S/ 1,00", aprobado.Cuerpo, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => renderizador.De(Correo(CategoriaDeCorreo.Pago, "pago-rechazado")));
    }

    [Fact]
    public void ElProveedorDePagos_OfreceSusCincoPlantillasConSusCampos()
    {
        var plantillas = new PlantillasDeCorreoDePagosParaPlataforma().Plantillas;

        Assert.Equal(["pago-aprobado", "pago-pendiente", "pago-rechazado", "pago-vencido", "reembolso-automatico"], plantillas.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["motivo"], plantillas["pago-rechazado"].Campos);
        Assert.Equal(["importe"], plantillas["reembolso-automatico"].Campos);
    }

    [Fact]
    public void LasPlantillasDeSeguridad_SonDeTextoFijo_YSusDatosNuncaEntran()
    {
        var mensaje = Renderizador().De(Correo(CategoriaDeCorreo.AlertaCriticaDeCuenta, "contrasena-cambiada", ("correo", "otra@example.com"), ("contrasena", "secreta")));

        Assert.DoesNotContain("otra@example.com", mensaje.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("secreta", mensaje.Cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public void DosProveedoresConLaMismaPlantilla_NoSePermiten_YUnaPlantillaInexistenteNoSale()
    {
        Assert.Throws<ArgumentException>(() => RenderizadorDeCorreoDeProduccion.Con([new PlantillasDeCorreoDeReclamos(), new PlantillasDeCorreoDeReclamos()]));
        Assert.Throws<InvalidOperationException>(() => Renderizador().De(Correo(CategoriaDeCorreo.Pago, "promocion-de-navidad")));
    }
}
