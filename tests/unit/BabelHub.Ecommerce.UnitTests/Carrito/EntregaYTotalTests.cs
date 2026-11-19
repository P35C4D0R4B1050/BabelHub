using System.Text.Json;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T03: los tipos de la entrega (dirección con su región, teléfono móvil peruano normalizado, nombre), el contacto redactado, el costo de envío y el total de la compra (CAR-08, CAR-23, CAR-30, D06, D11, D17).
/// </summary>
public sealed class EntregaYTotalTests
{
    private const string Redactado = "[redactado]";

    [Theory]
    [Trait("Criterio", "AC-CAR-26")]
    [InlineData("987654321", "987654321")]
    [InlineData("987 654 321", "987654321")]
    [InlineData("987-654-321", "987654321")]
    [InlineData("(987) 654 321", "987654321")]
    [InlineData("+51 987 654 321", "987654321")]
    [InlineData("+51987654321", "987654321")]
    [InlineData("0051 987654321", "987654321")]
    [InlineData("51987654321", "987654321")]
    [InlineData("  987654321  ", "987654321")]
    public void Un_telefono_movil_peruano_se_acepta_y_se_guarda_normalizado(string texto, string esperado)
    {
        Assert.True(TelefonoDeContacto.TryCrear(texto, out var telefono));

        Assert.Equal(esperado, telefono!.Valor);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-26")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("887654321")]
    [InlineData("98765432")]
    [InlineData("9876543210")]
    [InlineData("98765432a")]
    [InlineData("+52987654321")]
    [InlineData("+5198765432")]
    [InlineData("5198765432")]
    [InlineData("0051+987654321")]
    [InlineData("987+654321")]
    [InlineData("٩٨٧٦٥٤٣٢١")]
    [InlineData("987654321\n")]
    [InlineData("987\t654321")]
    public void Un_telefono_que_no_es_movil_peruano_se_rechaza(string? texto) => Assert.False(TelefonoDeContacto.TryCrear(texto, out _));

    [Theory]
    [Trait("Criterio", "AC-CAR-26")]
    [InlineData("Ana Pérez", true)]
    [InlineData("  Ana  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("Ana\nPérez", false)]
    [InlineData("Ana\rPérez", false)]
    [InlineData("Ana\tPérez", false)]
    [InlineData("Ana\u0000Pérez", false)]
    [InlineData("Ana\u2028Pérez", false)]
    [InlineData("Ana\u2029Pérez", false)]
    public void El_nombre_no_admite_vacios_ni_caracteres_de_control_ni_saltos_de_linea(string? texto, bool valido) =>
        Assert.Equal(valido, NombreDeContacto.TryCrear(texto, out _));

    [Fact]
    [Trait("Criterio", "AC-CAR-26")]
    public void El_nombre_tiene_un_tope_de_80_caracteres()
    {
        Assert.True(NombreDeContacto.TryCrear(new string('a', 80), out _));
        Assert.False(NombreDeContacto.TryCrear(new string('a', 81), out _));
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-07")]
    [InlineData("Jr. Lima 123, Huamanga", "Ayacucho", true)]
    [InlineData("", "Ayacucho", false)]
    [InlineData("   ", "Ayacucho", false)]
    [InlineData(null, "Ayacucho", false)]
    [InlineData("Jr. Lima 123", "", false)]
    [InlineData("Jr. Lima 123", null, false)]
    [InlineData("Jr. Lima\n123", "Ayacucho", false)]
    [InlineData("Jr. Lima 123", "Ayacu\ncho", false)]
    [InlineData("Jr. Lima\u2028123", "Ayacucho", false)]
    public void La_direccion_exige_texto_y_region_y_no_admite_saltos_de_linea(string? texto, string? region, bool valida) =>
        Assert.Equal(valida, DireccionDeEntrega.TryCrear(texto, region, out _));

    [Fact]
    [Trait("Criterio", "AC-CAR-07")]
    public void La_direccion_tiene_un_tope_de_200_caracteres_y_la_region_de_60()
    {
        Assert.True(DireccionDeEntrega.TryCrear(new string('a', 200), "Ayacucho", out _));
        Assert.False(DireccionDeEntrega.TryCrear(new string('a', 201), "Ayacucho", out _));
        Assert.True(DireccionDeEntrega.TryCrear("Jr. Lima 123", new string('r', 60), out _));
        Assert.False(DireccionDeEntrega.TryCrear("Jr. Lima 123", new string('r', 61), out _));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-26")]
    public void El_contacto_de_la_entrega_junta_nombre_telefono_y_direccion_y_se_compara_por_valor()
    {
        var a = Contacto();
        var b = Contacto();
        var otroTelefono = ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez"), TelefonoDeContacto.Crear("911222333"), DireccionDeEntrega.Crear("Jr. Lima 123", "Ayacucho"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, otroTelefono);
        Assert.Equal("Ana Pérez", a.Nombre.Valor);
        Assert.Equal("987654321", a.Telefono.Valor);
        Assert.Equal("Jr. Lima 123", a.Direccion.Texto);
        Assert.Equal("Ayacucho", a.Direccion.Region);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-26")]
    public void El_contacto_y_cada_dato_personal_se_redactan_al_convertirlos_a_texto_y_al_serializarlos()
    {
        var contacto = Contacto();
        object[] datos = [contacto, contacto.Nombre, contacto.Telefono, contacto.Direccion];

        foreach (var dato in datos)
        {
            Assert.Equal(Redactado, dato.ToString());
            Assert.Equal("\"" + Redactado + "\"", JsonSerializer.Serialize(dato, dato.GetType()));
            Assert.Equal($"{{\"c\":\"{Redactado}\"}}", JsonSerializer.Serialize(new { c = dato }));
            Assert.DoesNotContain("987654321", $"{dato}", StringComparison.Ordinal);
            Assert.DoesNotContain("Ana", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", dato), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Un_contacto_serializado_no_se_puede_leer_de_vuelta()
    {
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<ContactoDeEntrega>("\"[redactado]\""));
    }

    [Fact]
    public void Un_contacto_exige_sus_tres_datos()
    {
        Assert.Throws<ArgumentNullException>(() => ContactoDeEntrega.Crear(null!, TelefonoDeContacto.Crear("987654321"), DireccionDeEntrega.Crear("Jr. Lima 123", "Ayacucho")));
        Assert.Throws<ArgumentException>(() => NombreDeContacto.Crear(""));
        Assert.Throws<ArgumentException>(() => TelefonoDeContacto.Crear("123"));
        Assert.Throws<ArgumentException>(() => DireccionDeEntrega.Crear("", "Ayacucho"));
    }

    [Theory]
    [InlineData("12.00", true)]
    [InlineData("0", true)]
    [InlineData("0.01", true)]
    [InlineData("12.5", true)]
    [InlineData("-0.01", false)]
    [InlineData("12.005", false)]
    [InlineData("12.001", false)]
    [InlineData("10000000000", false)]
    public void El_costo_de_envio_son_soles_no_negativos_con_a_lo_mas_dos_decimales(string texto, bool valido) =>
        Assert.Equal(valido, CostoDeEnvio.TryCrear(decimal.Parse(texto, System.Globalization.CultureInfo.InvariantCulture), out _));

    [Fact]
    public void El_costo_de_envio_se_guarda_con_dos_decimales_y_el_cero_existe()
    {
        Assert.True(CostoDeEnvio.TryCrear(12.5m, out var costo));

        Assert.Equal("12.50", costo!.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0m, CostoDeEnvio.Cero.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-21")]
    public void Un_carrito_de_111_90_con_domicilio_suma_el_envio_de_12_y_totaliza_123_90_con_recojo_111_90_y_el_digital_no_lleva_envio()
    {
        var lineas = new[] { new ImporteDeLinea(37.30m, 3) };
        var envio = CostoDeEnvio.Crear(12.00m);

        var domicilio = TotalDeCompra.Calcular(lineas, TipoDeEntrega.Domicilio, envio);
        var recojo = TotalDeCompra.Calcular(lineas, TipoDeEntrega.Recojo, envio);
        var digital = TotalDeCompra.Calcular(lineas, TipoDeEntrega.Digital, envio);

        Assert.Equal((111.90m, 12.00m, 123.90m), (domicilio.Subtotal, domicilio.Envio, domicilio.Total));
        Assert.Equal((111.90m, 0m, 111.90m), (recojo.Subtotal, recojo.Envio, recojo.Total));
        Assert.Equal((111.90m, 0m, 111.90m), (digital.Subtotal, digital.Envio, digital.Total));
    }

    [Theory]
    [InlineData("0.125", 1, "0.13")]
    [InlineData("0.124", 1, "0.12")]
    [InlineData("2.675", 1, "2.68")]
    [InlineData("1.005", 3, "3.02")]
    [InlineData("-0.125", 1, "-0.13")]
    public void Los_importes_se_redondean_a_dos_decimales_alejandose_de_cero(string precio, int cantidad, string esperado)
    {
        var total = TotalDeCompra.Calcular([new ImporteDeLinea(decimal.Parse(precio, System.Globalization.CultureInfo.InvariantCulture), cantidad)], TipoDeEntrega.Recojo, CostoDeEnvio.Cero);

        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), total.Subtotal);
    }

    [Fact]
    public void El_subtotal_suma_los_importes_ya_redondeados_de_cada_linea()
    {
        var total = TotalDeCompra.Calcular([new ImporteDeLinea(0.125m, 1), new ImporteDeLinea(0.125m, 1)], TipoDeEntrega.Recojo, CostoDeEnvio.Cero);

        Assert.Equal(0.26m, total.Subtotal);
    }

    [Fact]
    public void Los_tres_tipos_de_entrega_existen()
    {
        Assert.Equal(["Recojo", "Domicilio", "Digital"], Enum.GetNames<TipoDeEntrega>());
    }

    private static ContactoDeEntrega Contacto() =>
        ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez"), TelefonoDeContacto.Crear("987654321"), DireccionDeEntrega.Crear("Jr. Lima 123", "Ayacucho"));
}
