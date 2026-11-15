using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class DatosProhibidosTests
{
    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [InlineData("password=Sup3rSecreta!", "Sup3rSecreta")]
    [InlineData("Contraseña: Temporal-2026-xyz", "Temporal-2026")]
    [InlineData("{\"contrasena\":\"Temporal-2026-xyz\"}", "Temporal-2026")]
    [InlineData("código de respaldo codigo=ABCD-EFGH-IJKL", "ABCD-EFGH")]
    [InlineData("otp: 482913", "482913")]
    [InlineData("sid=4f9a2c1d8e7b6a5f", "4f9a2c1d8e7b6a5f")]
    [InlineData("Set-Cookie: sesion=abc123def456; Path=/", "abc123def456")]
    [InlineData("secret=JBSWY3DPEHPK3PXP", "JBSWY3DPEHPK3PXP")]
    [InlineData("clave=firmaDeCredenciales-v3", "firmaDeCredenciales")]
    [InlineData("cvv=123", "=123")]
    [InlineData("api_key: AKIA123456789SECRETO", "AKIA123456789")]
    [InlineData("signing-key=Zq8Lw2Xv5Nc7", "Zq8Lw2Xv5Nc7")]
    [InlineData("private_key: MIIEvQIBADAN", "MIIEvQIBADAN")]
    [InlineData("hmac-key=0f1e2d3c4b5a6978", "0f1e2d3c4b5a6978")]
    public void Redactar_ConUnDatoProhibidoSembrado_NoLoConserva(string texto, string sembrado)
    {
        var redactado = RedactorDeDatosProhibidos.Redactar(texto);

        Assert.DoesNotContain(sembrado, redactado, StringComparison.Ordinal);
        Assert.Contains(ValorSecreto.Redactado, redactado, StringComparison.Ordinal);
        Assert.True(RedactorDeDatosProhibidos.ContieneDatosProhibidos(texto));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [InlineData("https://tienda.pe/descargas/libro-7?firma=a1b2c3d4e5f6&expira=1763035200", "a1b2c3d4e5f6")]
    [InlineData("https://s3.pe/x?X-Amz-Signature=deadbeef0123&X-Amz-Expires=300", "deadbeef0123")]
    [InlineData("GET /enlace?token=eyAbC123xyz&modo=descarga", "eyAbC123xyz")]
    public void Redactar_ConUnEnlaceFirmado_OcultaLaFirmaYElToken(string texto, string sembrado)
    {
        Assert.DoesNotContain(sembrado, RedactorDeDatosProhibidos.Redactar(texto), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [InlineData("tarjeta 4111 1111 1111 1111 vence 12/28", "4111 1111 1111 1111")]
    [InlineData("pago con 4111-1111-1111-1111", "4111-1111-1111-1111")]
    [InlineData("pan:5500000000000004", "5500000000000004")]
    [InlineData("tarjeta 378282246310005", "378282246310005")]
    public void Redactar_ConUnNumeroDeTarjetaValido_LoOculta(string texto, string tarjeta)
    {
        var redactado = RedactorDeDatosProhibidos.Redactar(texto);

        Assert.DoesNotContain(tarjeta, redactado, StringComparison.Ordinal);
        Assert.Contains(ValorSecreto.Redactado, redactado, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [InlineData("Authorization: Bearer abcdefghijklmnop12345")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.firma123")]
    public void Redactar_ConUnTokenDeAcceso_NoConservaNingunaParte(string texto)
    {
        var redactado = RedactorDeDatosProhibidos.Redactar(texto);

        Assert.DoesNotContain("abcdefghijklmnop12345", redactado, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", redactado, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [InlineData("Pedido 12345 confirmado para la sucursal Miraflores")]
    [InlineData("Se reservaron 3 unidades de la edición 9788437604947")]
    [InlineData("Tu pedido llegará el 14/11/2026 entre las 10:00 y las 12:00")]
    [InlineData("Número de seguimiento 4111111111111112")]
    [InlineData("")]
    public void Redactar_ConTextoCorriente_NoLoAltera(string texto)
    {
        Assert.Equal(texto, RedactorDeDatosProhibidos.Redactar(texto));
        Assert.False(RedactorDeDatosProhibidos.ContieneDatosProhibidos(texto));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("X-Credencial-De-Servicio")]
    [InlineData("x-credencial-de-origen")]
    [InlineData("X-Verificacion-De-Persona")]
    [InlineData("X-Origen-Reenviado")]
    [InlineData("cf-turnstile-response")]
    [InlineData("password")]
    [InlineData("nueva_contraseña")]
    [InlineData("access_token")]
    [InlineData("X-Amz-Signature")]
    [InlineData("codigo")]
    [InlineData("sessionId")]
    public void EsNombreSensible_ConUnaCabeceraOCampoPrivado_EsVerdadero(string nombre)
    {
        Assert.True(RedactorDeDatosProhibidos.EsNombreSensible(nombre));
    }

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("traceId")]
    [InlineData("pedidoId")]
    [InlineData("User-Agent")]
    [InlineData("")]
    [InlineData(null)]
    public void EsNombreSensible_ConUnNombreCorriente_EsFalso(string? nombre)
    {
        Assert.False(RedactorDeDatosProhibidos.EsNombreSensible(nombre));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void RedactarPares_OcultaElValorDeLosNombresSensiblesYRedactaElRestoSinPerderLosDemas()
    {
        var pares = new List<KeyValuePair<string, string?>>
        {
            new("X-Credencial-De-Servicio", "s3creto-de-servicio-largo-0123456789"),
            new("Content-Type", "application/json"),
            new("detalle", "intento con password=Hunter2xx"),
            new("vacio", null),
        };

        var resultado = RedactorDeDatosProhibidos.RedactarPares(pares);

        Assert.Equal(4, resultado.Count);
        Assert.Equal(ValorSecreto.Redactado, resultado[0].Value);
        Assert.Equal("application/json", resultado[1].Value);
        Assert.DoesNotContain("Hunter2xx", resultado[2].Value, StringComparison.Ordinal);
        Assert.Equal(string.Empty, resultado[3].Value);
    }

    [Fact]
    public void Redactar_ConUnTextoQueNoSePuedeAnalizarATiempo_LoDescartaCompleto()
    {
        var hostil = "password=" + new string('=', 200_000) + new string(' ', 5);

        var redactado = RedactorDeDatosProhibidos.Redactar(hostil);

        Assert.DoesNotContain(new string('=', 50), redactado, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void ValorSecreto_NuncaApareceEnSuRepresentacionEnTexto()
    {
        var codigo = new ValorSecreto("482913");

        Assert.Equal("[redactado]", codigo.ToString());
        Assert.DoesNotContain("482913", $"{codigo}", StringComparison.Ordinal);
        Assert.DoesNotContain("482913", System.Text.Json.JsonSerializer.Serialize(new { Codigo = codigo.ToString() }), StringComparison.Ordinal);
        Assert.Equal("482913", codigo.Revelar());
        Assert.Equal(new ValorSecreto("482913"), codigo);
        Assert.Throws<ArgumentException>(() => new ValorSecreto(string.Empty));
    }

    // ---------- Hash del identificador de sesión y cuerpo de un borrador (web-base T19, hallazgo R12) ----------

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    [InlineData("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    public void ElHashDelIdentificadorDeSesion_SeRedacta_ConOSinContexto(string hash)
    {
        Assert.DoesNotContain(hash, RedactorDeDatosProhibidos.Redactar($"sesion {hash}"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hash, RedactorDeDatosProhibidos.Redactar(hash), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hash, RedactorDeDatosProhibidos.Redactar($"id_hash={hash}; otro=1"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hash, RedactorDeDatosProhibidos.Redactar($"fila {{\"id\":\"{hash}\"}}"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public void UnaCadenaHexadecimalQueNoMide64_NoSeRedacta()
    {
        const string Correlacion = "3f2b8c1d4e5a6978b0c1d2e3f4a5b6c7";
        const string Sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";

        Assert.Equal($"traza {Correlacion}", RedactorDeDatosProhibidos.Redactar($"traza {Correlacion}"));
        Assert.Equal($"commit {Sha1}", RedactorDeDatosProhibidos.Redactar($"commit {Sha1}"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-28")]
    [InlineData("borrador {\"nombre\":\"Ana Pérez\",\"direccion\":\"Av. Siempre Viva 742\"}")]
    [InlineData("{\"borrador\":{\"nombre\":\"Ana Pérez\",\"direccion\":\"Av. Siempre Viva 742\"},\"plantilla\":\"/cuenta/datos\"}")]
    [InlineData("borrador: {\"nombre\":\"Ana Pérez\",\"direccion\":\"Av. }Siempre{ Viva 742\"}")]
    [InlineData("el borrador={\"nombre\":\"Ana Pérez\",\"direccion\":\"Av. Siempre Viva 742\"} se guardó")]
    [InlineData("{\"borrador\":\"{\\\"nombre\\\":\\\"Ana Pérez\\\",\\\"direccion\\\":\\\"Av. Siempre Viva 742\\\"}\"}")]
    public void ElCuerpoDeUnBorrador_SeRedactaCompleto(string texto)
    {
        var redactado = RedactorDeDatosProhibidos.Redactar(texto);

        Assert.DoesNotContain("Ana", redactado, StringComparison.Ordinal);
        Assert.DoesNotContain("Siempre", redactado, StringComparison.Ordinal);
        Assert.DoesNotContain("742", redactado, StringComparison.Ordinal);
        Assert.Contains("[redactado]", redactado, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-28")]
    public void UnTextoSobreBorradoresSinCuerpo_ConservaSuSentido()
    {
        Assert.Equal("se purgaron 3 borradores vencidos", RedactorDeDatosProhibidos.Redactar("se purgaron 3 borradores vencidos"));
        Assert.Equal("borrador descartado por vencimiento", RedactorDeDatosProhibidos.Redactar("borrador descartado por vencimiento"));
    }
}
