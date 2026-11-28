using System.Text.Json;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>El contacto que se entrega al operador y la política de cancelación (T02; ENV-10, ENV-13, ENV-17).</summary>
public sealed class DestinatarioYPoliticaDeCancelacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Criterio", "AC-ENV-09")]
    public void Un_contacto_completo_se_valida_y_se_recorta()
    {
        var resultado = DestinatarioParaGuia.Validar("  Ana Pérez Quispe ", " Av. Los Olivos 123, Ayacucho ", " 987654321 ");

        Assert.True(resultado.EsValido);
        Assert.Null(resultado.CampoFaltante);
        Assert.Equal("Ana Pérez Quispe", resultado.Destinatario!.Nombre);
        Assert.Equal("Av. Los Olivos 123, Ayacucho", resultado.Destinatario.Direccion);
        Assert.Equal("987654321", resultado.Destinatario.Telefono);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-09")]
    [InlineData(null, "Av. Los Olivos 123", "987654321", CampoDelDestinatario.Nombre)]
    [InlineData("Ana Pérez", "", "987654321", CampoDelDestinatario.Direccion)]
    [InlineData("Ana Pérez", "Av. Los Olivos 123", null, CampoDelDestinatario.Telefono)]
    [InlineData("Ana Pérez", null, null, CampoDelDestinatario.Direccion)]
    public void Un_contacto_incompleto_indica_el_primer_campo_que_falta_sin_repetir_datos(string? nombre, string? direccion, string? telefono, CampoDelDestinatario esperado)
    {
        var resultado = DestinatarioParaGuia.Validar(nombre, direccion, telefono);

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Destinatario);
        Assert.Equal(esperado, resultado.CampoFaltante);
        Assert.DoesNotContain("Ana", resultado.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-09")]
    [InlineData(121, 10, 10)]
    [InlineData(10, 263, 10)]
    [InlineData(10, 10, 21)]
    public void Un_campo_demasiado_largo_se_rechaza_como_no_valido(int nombre, int direccion, int telefono)
    {
        var resultado = DestinatarioParaGuia.Validar(new string('a', nombre), new string('b', direccion), new string('9', telefono));

        Assert.False(resultado.EsValido);
        Assert.NotNull(resultado.CampoFaltante);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-17")]
    [InlineData("Ana\nPérez", "Av. Los Olivos 123", "987654321", CampoDelDestinatario.Nombre)]
    [InlineData("Ana Pérez", "Av. Los Olivos 123", "98765-abc", CampoDelDestinatario.Telefono)]
    [InlineData("Ana Pérez", "Av. Los Olivos 123", "+ - ()", CampoDelDestinatario.Telefono)]
    public void Un_campo_con_caracteres_de_control_o_un_telefono_con_letras_se_rechaza_para_ese_campo(string nombre, string direccion, string telefono, CampoDelDestinatario esperado)
    {
        var resultado = DestinatarioParaGuia.Validar(nombre, direccion, telefono);

        Assert.False(resultado.EsValido);
        Assert.Equal(esperado, resultado.CampoFaltante);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void El_destinatario_nunca_se_escribe_en_texto_ni_en_json()
    {
        var destinatario = DestinatarioParaGuia.Validar("Ana Pérez Quispe", "Av. Los Olivos 123, Ayacucho", "987654321").Destinatario!;

        var texto = destinatario.ToString() + JsonSerializer.Serialize(destinatario) + JsonSerializer.Serialize(new { Destinatario = destinatario }) + $"{destinatario}";

        Assert.DoesNotContain("Ana", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Olivos", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
        Assert.Equal("[redactado]", destinatario.ToString());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DestinatarioParaGuia>("\"[redactado]\""));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-12")]
    public void El_envio_aplica_la_politica_al_cancelar()
    {
        var pendiente = EnvioTests.Nuevo();
        var conMarcador = EnvioTests.ConGuia();
        conMarcador.IniciarDespacho(Ahora);

        Assert.Equal(ResultadoDeCancelacion.Cancelado, pendiente.Cancelar(Ahora));
        Assert.Equal(ResultadoDeCancelacion.Mantenido, conMarcador.Cancelar(Ahora));
    }
}
