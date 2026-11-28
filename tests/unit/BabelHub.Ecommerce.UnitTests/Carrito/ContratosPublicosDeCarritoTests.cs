using System.Text.Json;
using BabelHub.Ecommerce.Application.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>T07: los modelos del contrato público de Carrito (contacto redactado, posesión de ediciones) y su forma.</summary>
public sealed class ContratosPublicosDeCarritoTests
{
    private static ContactoDeCompra Contacto() => new("Ana Pérez", "987654321", "Jr. Lima 123", "Ayacucho");

    [Fact]
    public void El_contacto_de_compra_se_redacta_al_imprimirlo_y_al_serializarlo()
    {
        var contacto = Contacto();

        Assert.Equal("[redactado]", contacto.ToString());
        Assert.Equal("\"[redactado]\"", JsonSerializer.Serialize(contacto));
        Assert.Equal("{\"c\":\"[redactado]\"}", JsonSerializer.Serialize(new { c = contacto }));
        Assert.DoesNotContain("987654321", $"{contacto}", StringComparison.Ordinal);
    }

    [Fact]
    public void Un_contacto_serializado_no_se_puede_leer_de_vuelta()
    {
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<ContactoDeCompra>("\"[redactado]\""));
    }

    [Fact]
    public void Una_instantanea_con_contacto_no_lo_deja_ver_al_imprimirla_ni_al_serializarla()
    {
        var instantanea = new InstantaneaDeCompra(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TipoDeEntregaDeCompra.Domicilio,
            null,
            "ayacucho",
            111.90m,
            12m,
            123.90m,
            Contacto(),
            [new AceptacionDeCompra(TextoLegalDeCompra.Terminos, "2026-11-01", DateTimeOffset.UnixEpoch)],
            [],
            "huella");

        var texto = JsonSerializer.Serialize(instantanea);

        Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Jr. Lima", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", instantanea.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void El_contacto_de_compra_acepta_datos_ausentes_para_que_el_caso_de_uso_responda_por_campo_y_sigue_redactado()
    {
        var vacio = new ContactoDeCompra(null, "  ", null, "");

        Assert.Equal(["", "  ", "", ""], [vacio.Nombre, vacio.Telefono, vacio.Direccion, vacio.Region]);
        Assert.Equal("[redactado]", vacio.ToString());
    }

    [Fact]
    public void Un_tercero_que_no_responde_no_declara_ninguna_edicion_poseida()
    {
        Assert.Equal(EstadoDePosesion.NoSePuedeConfirmar, ResultadoDePosesion.NoSePuedeConfirmar.Estado);
        Assert.Empty(ResultadoDePosesion.NoSePuedeConfirmar.Poseidas);
    }
}
