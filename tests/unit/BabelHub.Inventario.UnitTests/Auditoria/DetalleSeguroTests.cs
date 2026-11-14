using System.Text.Json;
using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// <c>DetalleSeguro</c> (AUD-06, AUD-24): lista blanca de claves por acción, redacción (no rechazo) de valores con forma de tarjeta, token, enlace
/// o dirección, marca de personales por clave y truncado a 1000 caracteres con marca, sin dejar un valor personal sin marcar.
/// </summary>
public sealed class DetalleSeguroTests
{
    private static readonly IReadOnlyCollection<ClaveDeDetalle> Claves =
    [
        new("precioAnterior", false),
        new("precioNuevo", false),
        new("motivoLibre", false),
        new("nombre", true),
        new("correo", true),
    ];

    private static DetalleSeguro Construir(params (string Clave, string? Valor)[] valores) =>
        DetalleSeguro.Construir(Claves, valores.ToDictionary(v => v.Clave, v => v.Valor));

    // ---------- Lista blanca ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void SoloEntranLasClavesDeLaListaBlanca_LoDemasSeDescarta_SinFallar()
    {
        var detalle = Construir(("precioAnterior", "10.00"), ("precioNuevo", "12.50"), ("clave", "no declarada"), ("PRECIONUEVO", "otra mayúscula"));

        Assert.Equal(["precioAnterior", "precioNuevo"], detalle.Valores.Keys);
        Assert.Equal(2, detalle.CantidadDeClavesDescartadas);
        Assert.False(detalle.Truncado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void ElDetalleSeSerializaComoJsonEstructurado_EnElOrdenDeLaLista_ConLosValoresComoTexto()
    {
        var detalle = Construir(("precioNuevo", "12.50"), ("precioAnterior", "10.00"), ("motivoLibre", "con \"comillas\" y \\ barra"));

        using var json = JsonDocument.Parse(detalle.Texto);
        Assert.Equal(["precioAnterior", "precioNuevo", "motivoLibre"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("con \"comillas\" y \\ barra", json.RootElement.GetProperty("motivoLibre").GetString());
    }

    [Fact]
    public void SinValoresONulos_ElDetalleEstaVacio()
    {
        var detalle = Construir(("precioAnterior", null), ("nombre", null));

        Assert.Equal("{}", detalle.Texto);
        Assert.Empty(detalle.Valores);
        Assert.Empty(detalle.ClavesPersonales);
    }

    [Fact]
    public void LosCaracteresDeControl_NoEntranAlDetalle()
    {
        var detalle = Construir(("motivoLibre", "linea1\r\nlinea2\t\u0000fin"));

        Assert.DoesNotMatch(@"[\u0000-\u001f]", detalle.Valores["motivoLibre"]);
        Assert.Equal("linea1  linea2  fin", detalle.Valores["motivoLibre"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Con-guion")]
    [InlineData("1clave")]
    [InlineData("a b")]
    public void UnNombreDeClaveInvalido_SeRechazaAlDeclararlo(string nombre) =>
        Assert.Throws<ArgumentException>(() => new ClaveDeDetalle(nombre, false));

    [Fact]
    public void UnaClaveDeclaradaDosVeces_SeRechaza() =>
        Assert.Throws<ArgumentException>(() => DetalleSeguro.Construir([new("a", false), new("a", true)], new Dictionary<string, string?>()));

    // ---------- Redacción de valores (no rechazo) ----------

    [Theory]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("4111111111111111")]
    [InlineData("pagó con 5500 0000 0000 0004 ayer")]
    [InlineData("378282246310005")]
    [Trait("Criterio", "AC-AUD-05")]
    public void UnNumeroConFormaDeTarjeta_SeRedacta_YElRegistroSeConserva(string valor)
    {
        var detalle = Construir(("motivoLibre", valor), ("precioNuevo", "12.50"));

        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["motivoLibre"]);
        Assert.Equal("12.50", detalle.Valores["precioNuevo"]);
        Assert.Equal(["motivoLibre"], detalle.ClavesRedactadas);
        Assert.DoesNotContain("4111", detalle.Texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1234567890123")]
    [InlineData("pedido 12345678901234567890")]
    [InlineData("4111 1111 1111")]
    [InlineData("S/ 1,234.50")]
    [InlineData("a las 10:30:00 del 14/11")]
    public void UnNumeroLargoQueNoPasaLuhnONoTieneLongitudDeTarjeta_NoSeRedacta(string valor)
    {
        var detalle = Construir(("motivoLibre", valor));

        Assert.Equal(valor, detalle.Valores["motivoLibre"]);
        Assert.Empty(detalle.ClavesRedactadas);
    }

    [Theory]
    [InlineData("ey" + "JhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + "ey" + "JzdWIiOiIxMjM0NTY3ODkwIn0." + "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c")]
    [InlineData("Bearer abc123def456")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("QWxhZGRpbjpvcGVuIHNlc2FtZVF1ZUVzVW5Ub2tlbkxhcmdvRGVBcGlLZXk=")]
    [InlineData("Zk9xW2pLm7Rt5Yu8Nv3Bc6Xs1Qa4Ez9HgJkLpOiUyTrEwQ2")]
    [InlineData("AGE-SECRET-" + "KEY-1QQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQ")]
    [Trait("Criterio", "AC-AUD-05")]
    public void UnValorConFormaDeToken_SeRedacta(string valor)
    {
        var detalle = Construir(("motivoLibre", valor));

        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["motivoLibre"]);
    }

    [Theory]
    [InlineData("https://cdn.babelhub.pe/libros/x.epub?firma=abc&expira=123")]
    [InlineData("HTTP://ejemplo.pe/a")]
    [InlineData("ftp://servidor/archivo")]
    [InlineData("ver www.ejemplo.pe/descarga")]
    [Trait("Criterio", "AC-AUD-05")]
    public void UnEnlace_SeRedacta(string valor)
    {
        var detalle = Construir(("motivoLibre", valor));

        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["motivoLibre"]);
    }

    [Theory]
    [InlineData("200.37.12.4")]
    [InlineData("desde 10.0.0.1 hoy")]
    [InlineData("2001:db8:85a3:0:0:8a2e:370:7334")]
    [InlineData("fe80::1ff:fe23:4567:890a")]
    [InlineData("persona@ejemplo.pe")]
    public void UnaDireccionIpOUnCorreoEnUnaClaveNoPersonal_SeRedacta(string valor)
    {
        var detalle = Construir(("motivoLibre", valor));

        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["motivoLibre"]);
    }

    [Fact]
    public void UnCorreoEnUnaClavePersonalDeclarada_SeConserva_PeroMarcado_YUnaIpSiempreSeRedacta()
    {
        var detalle = Construir(("correo", "persona@ejemplo.pe"), ("nombre", "Ana Pérez desde 200.37.12.4"));

        Assert.Equal("persona@ejemplo.pe", detalle.Valores["correo"]);
        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["nombre"]);
        Assert.Equal(["correo", "nombre"], detalle.ClavesPersonales.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("10.50")]
    [InlineData("1.2.3")]
    [InlineData("versión 2.1")]
    [InlineData("Cambio de precio por campaña")]
    public void UnTextoNormal_NoSeRedacta(string valor)
    {
        var detalle = Construir(("motivoLibre", valor));

        Assert.Equal(valor, detalle.Valores["motivoLibre"]);
    }


    // ---------- Formas que la revisión de seguridad encontró evadibles ----------

    [Theory]
    [InlineData("pedido 12 4111 1111 1111 1111")]
    [InlineData("4111 1111 1111 1111 5")]
    [InlineData("1-4111111111111111")]
    [InlineData("4111.1111.1111.1111")]
    [InlineData("4111/1111/1111/1111")]
    [InlineData("4111_1111_1111_1111")]
    [InlineData("4111  1111  1111  1111")]
    [InlineData("4111 - 1111 - 1111 - 1111")]
    [InlineData("4111\u00a01111\u00a01111\u00a01111")]
    [InlineData("4111\u200b1111\u200b1111\u200b1111")]
    [InlineData("\u0664\u0661\u0661\u0661 1111 1111 1111")]
    [Trait("Criterio", "AC-AUD-05")]
    public void UnaTarjetaConOtrosSeparadoresOMezcladaConOtrosNumeros_SeRedacta(string valor) =>
        Assert.Equal(DetalleSeguro.ValorRedactado, Construir(("motivoLibre", valor)).Valores["motivoLibre"]);

    [Theory]
    [InlineData("desde 192.168.1.1.")]
    [InlineData("ip:fe80::1")]
    [InlineData("ip:2001:db8::1")]
    [InlineData("ip fe80::1: rechazada")]
    [InlineData("(10.0.0.1)")]
    [InlineData("::1")]
    [Trait("Criterio", "AC-AUD-05")]
    public void UnaIpRodeadaDePuntuacion_SeRedacta(string valor) =>
        Assert.Equal(DetalleSeguro.ValorRedactado, Construir(("motivoLibre", valor)).Valores["motivoLibre"]);

    [Theory]
    [InlineData("bearer abc123def456")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:persona@ejemplo.pe")]
    [InlineData("pagos.ejemplo.pe/x?firma=abc")]
    [InlineData("tok_0123456789abcdef0123456789abcdef")]
    public void FormasDeTokenYEnlaceEnOtraGrafia_SeRedactan(string valor) =>
        Assert.Equal(DetalleSeguro.ValorRedactado, Construir(("motivoLibre", valor)).Valores["motivoLibre"]);

    [Fact]
    public void UnValorDemasiadoLargoSeRedactaSinAnalizarlo_YElAnalisisNoCuestaTiempo()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        var detalle = Construir(("motivoLibre", new string('x', 30_000)), ("correo", new string('y', 30_000)));

        reloj.Stop();
        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["motivoLibre"]);
        Assert.Equal(DetalleSeguro.ValorRedactado, detalle.Valores["correo"]);
        Assert.True(reloj.ElapsedMilliseconds < 2000, reloj.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("\ud800")]
    [InlineData("texto \udc00 suelto")]
    [InlineData("alto sin bajo \ud83d fin")]
    [Trait("Criterio", "AC-AUD-05")]
    public void UnParSustitutoHuerfano_NoHaceFallarElRegistro_SeReemplazaPorElCaracterDeSustitucion(string valor)
    {
        var detalle = Construir(("motivoLibre", valor), ("nombre", valor));

        Assert.Contains('\ufffd', detalle.Valores["motivoLibre"]);
        using var json = JsonDocument.Parse(detalle.Texto);
        Assert.True(json.RootElement.TryGetProperty("nombre", out _));
        Assert.Equal(["nombre"], detalle.ClavesPersonales);
    }

    [Fact]
    public void UnParSustitutoValido_SeConserva() =>
        Assert.Equal("libro 📚", Construir(("motivoLibre", "libro 📚")).Valores["motivoLibre"]);

    [Fact]
    public void LasColeccionesDelDetalleSonDeSoloLectura_NadiePuedeDejarElTextoIncoherente()
    {
        var detalle = Construir(("precioNuevo", "5.00"), ("nombre", "Ana"));

        Assert.False(detalle.Valores is Dictionary<string, string>);
        Assert.False(detalle.ClavesPersonales is HashSet<string>);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)detalle.Valores)["otra"] = "x");
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)detalle.ClavesPersonales).Add("otra"));
    }

    [Fact]
    public void LosNombresDeLasClavesNoDeclaradasNoSeConservan_SoloSuCantidad()
    {
        var detalle = Construir(("clave-controlada-por-el-cliente", "x"), ("<script>", "y"));

        Assert.Equal(2, detalle.CantidadDeClavesDescartadas);
        Assert.DoesNotContain("script", detalle.Texto, StringComparison.Ordinal);
    }

    [Fact]
    public void UnNombreDeClaveConSaltoDeLineaFinal_SeRechaza() =>
        Assert.Throws<ArgumentException>(() => new ClaveDeDetalle("clave\n", false));

    // ---------- Marca de personales ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void LosCamposPersonalesQueEntranQuedanMarcados_YLosNoPersonalesNo()
    {
        var detalle = Construir(("precioNuevo", "12.50"), ("nombre", "Ana Pérez"), ("correo", null));

        Assert.Equal(["nombre"], detalle.ClavesPersonales);
        Assert.True(detalle.EsPersonal("nombre"));
        Assert.False(detalle.EsPersonal("precioNuevo"));
        Assert.False(detalle.EsPersonal("correo"));
    }

    // ---------- Truncado ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-20")]
    public void UnDetalleDe1500Caracteres_QuedaEn1000_MarcadoComoTruncado()
    {
        var detalle = Construir(("precioAnterior", "10.00"), ("motivoLibre", new string('x', 1500)));

        Assert.True(detalle.Truncado);
        Assert.True(detalle.Texto.Length <= DetalleSeguro.MaximoDeCaracteres, detalle.Texto.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(detalle.Texto.Length > 900);
        using var json = JsonDocument.Parse(detalle.Texto);
        Assert.Equal("10.00", json.RootElement.GetProperty("precioAnterior").GetString());
        Assert.EndsWith("…", json.RootElement.GetProperty("motivoLibre").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    [Trait("Criterio", "AC-AUD-20")]
    public void ElLimiteEsDeMilCaracteres_ContandoElJsonCompleto(int largoDelTexto, bool truncado)
    {
        // El JSON {"motivoLibre":"…"} agrega 18 caracteres alrededor del valor.
        var valor = new string('x', largoDelTexto - 18);
        var detalle = Construir(("motivoLibre", valor));

        Assert.Equal(truncado, detalle.Truncado);
        Assert.True(detalle.Texto.Length <= DetalleSeguro.MaximoDeCaracteres);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-20")]
    public void SiUnaClavePersonalNoCabe_SeOmiteEntera_NuncaParcial_YSoloQuedanMarcadasLasQueEstan()
    {
        var detalle = Construir(("motivoLibre", new string('x', 960)), ("nombre", new string('N', 200)), ("correo", "persona@ejemplo.pe"));

        Assert.True(detalle.Truncado);
        Assert.DoesNotContain("nombre", detalle.Valores.Keys);
        Assert.DoesNotContain('N', detalle.Texto);
        Assert.Empty(detalle.ClavesPersonales);
        Assert.True(detalle.Texto.Length <= DetalleSeguro.MaximoDeCaracteres);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-20")]
    public void ConUnDetalleTruncado_TodaClavePersonalPresenteSigueMarcada()
    {
        // La clave personal va antes de la que desborda: queda en el detalle y marcada.
        IReadOnlyCollection<ClaveDeDetalle> claves = [new("nombre", true), new("motivoLibre", false)];
        var detalle = DetalleSeguro.Construir(claves, new Dictionary<string, string?> { ["nombre"] = "Ana Pérez", ["motivoLibre"] = new string('x', 2000) });

        Assert.True(detalle.Truncado);
        Assert.Equal(["nombre"], detalle.ClavesPersonales);
        foreach (var clave in detalle.Valores.Keys)
        {
            Assert.Equal(claves.Single(c => c.Nombre == clave).Personal, detalle.EsPersonal(clave));
        }
    }

    [Fact]
    public void LosValoresConEscapesJsonSeMidenYaEscapados_ElTextoNuncaSuperaElMaximo()
    {
        var detalle = Construir(("motivoLibre", string.Concat(Enumerable.Repeat("\"\\", 800))));

        Assert.True(detalle.Texto.Length <= DetalleSeguro.MaximoDeCaracteres);
        using var json = JsonDocument.Parse(detalle.Texto);
        Assert.True(json.RootElement.TryGetProperty("motivoLibre", out _));
    }

    [Fact]
    public void UnaClavePersonalQueNoCabeSolaTampocoSeCorta_SeOmite()
    {
        var detalle = Construir(("nombre", new string('N', 990)));

        Assert.True(detalle.Truncado);
        Assert.Empty(detalle.Valores);
        Assert.Equal("{}", detalle.Texto);
    }
}
