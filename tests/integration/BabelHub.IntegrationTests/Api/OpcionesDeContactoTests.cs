using BabelHub.Ecommerce.Api.Web.Contacto;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// Los datos de contacto del comercio que sale de la configuración del entorno (COM-02 a COM-05): obligatorios fuera de desarrollo y de prueba, ficticios dentro, validados con conjuntos
/// ASCII en el teléfono y el correo, y con enlaces <c>tel:</c> y <c>mailto:</c> armados con prefijo fijo y un valor ya validado. Sin red ni base de datos.
/// </summary>
public sealed class OpcionesDeContactoTests
{
    private const string Telefono = "Contacto:Telefono";
    private const string Correo = "Contacto:Correo";
    private const string Direccion = "Contacto:Direccion";

    private static (DatosDeContacto? Datos, LectorDeSecretos Lector) Leer(bool permitirEjemplo, params (string Clave, string? Valor)[] valores)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => v.Valor)).Build();
        var lector = new LectorDeSecretos(configuracion);
        return (DatosDeContacto.Leer(lector, permitirEjemplo), lector);
    }

    private static (DatosDeContacto? Datos, LectorDeSecretos Lector) Leer(string? telefono, string? correo, string? direccion, bool permitirEjemplo = false) =>
        Leer(permitirEjemplo, (Telefono, telefono), (Correo, correo), (Direccion, direccion));

    private const string DireccionValida = "Jr. Callao 123, Breña";

    [Fact]
    [Trait("Criterio", "AC-COM-03")]
    public void Fuera_de_desarrollo_y_de_prueba_falta_cada_dato_y_el_error_nombra_las_tres_variables_sin_valores()
    {
        var (datos, lector) = Leer(permitirEjemplo: false);

        Assert.Null(datos);
        Assert.Contains("BABELHUB__Contacto__Telefono: falta", lector.Problemas);
        Assert.Contains("BABELHUB__Contacto__Correo: falta", lector.Problemas);
        Assert.Contains("BABELHUB__Contacto__Direccion: falta", lector.Problemas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Criterio", "AC-COM-03")]
    public void Un_valor_vacio_o_solo_con_espacios_cuenta_como_faltante_fuera_de_desarrollo(string vacio)
    {
        var (datos, lector) = Leer(vacio, vacio, vacio);

        Assert.Null(datos);
        Assert.Equal(3, lector.Problemas.Count(p => p.EndsWith(": falta", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-COM-04")]
    public void En_desarrollo_y_prueba_lo_que_falta_se_sustituye_por_los_ficticios_claramente_falsos()
    {
        var (datos, lector) = Leer(permitirEjemplo: true);

        Assert.Empty(lector.Problemas);
        Assert.NotNull(datos);
        Assert.Equal("999 000 000", datos.Telefono);
        Assert.Equal("tel:+51999000000", datos.EnlaceDeLlamada);
        Assert.Equal("contacto@ejemplo.test", datos.Correo);
        Assert.Equal("mailto:contacto@ejemplo.test", datos.EnlaceDeCorreo);
        Assert.Contains("dato de ejemplo", datos.Direccion, StringComparison.Ordinal);
        Assert.True(datos.EsDeEjemplo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [Trait("Criterio", "AC-COM-04")]
    public void En_desarrollo_un_valor_vacio_se_trata_como_no_configurado_y_usa_el_ficticio(string vacio)
    {
        var (datos, lector) = Leer(vacio, vacio, vacio, permitirEjemplo: true);

        Assert.Empty(lector.Problemas);
        Assert.True(datos!.EsDeEjemplo);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-04")]
    public void Con_datos_configurados_en_desarrollo_se_muestran_los_configurados_y_no_los_ficticios()
    {
        var (datos, lector) = Leer("+51 987 654 321", "atencion@tienda.example", DireccionValida, permitirEjemplo: true);

        Assert.Empty(lector.Problemas);
        Assert.Equal("+51 987 654 321", datos!.Telefono);
        Assert.Equal("atencion@tienda.example", datos.Correo);
        Assert.Equal(DireccionValida, datos.Direccion);
        Assert.False(datos.EsDeEjemplo);
    }

    [Theory]
    [InlineData("+51 987 654 321", "tel:+51987654321")]
    [InlineData("987 654 321", "tel:+51987654321")]
    [InlineData("+987 654 321", "tel:+987654321")]
    [InlineData("(01) 234-5678", "tel:012345678")]
    [InlineData("+1 202 555 0100", "tel:+12025550100")]
    [InlineData("  987654321  ", "tel:+51987654321")]
    [Trait("Criterio", "AC-COM-01")]
    public void El_enlace_de_llamada_lleva_solo_los_digitos_y_el_mas_inicial_y_a_un_celular_de_nueve_digitos_sin_mas_se_le_antepone_el_51(string configurado, string enlace)
    {
        var (datos, lector) = Leer(configurado, "atencion@tienda.example", DireccionValida);

        Assert.Empty(lector.Problemas);
        Assert.Equal(enlace, datos!.EnlaceDeLlamada);
        Assert.Equal(configurado.Trim(' '), datos.Telefono);
    }

    [Theory]
    [InlineData("123456")] // 6 dígitos
    [InlineData("1234567890123456")] // 16 dígitos
    [InlineData("abc 123 4567")]
    [InlineData("987 654 32٣")] // dígito árabe-índico
    [InlineData("987654321\n")]
    [InlineData("987-654-321?x=1")]
    [InlineData("987 654 321")] // espacio de no separación: solo vale U+0020
    [InlineData("987 654 321 987 654 321 987 654 321")] // más de 30 caracteres
    [InlineData("987++654321")]
    [InlineData("987 654 +321")]
    [Trait("Criterio", "AC-COM-03")]
    public void Un_telefono_con_formato_invalido_impide_arrancar_en_cualquier_entorno(string telefono)
    {
        foreach (var ejemplo in new[] { false, true })
        {
            var (datos, lector) = Leer(telefono, "atencion@tienda.example", DireccionValida, ejemplo);

            Assert.Null(datos);
            var problema = Assert.Single(lector.Problemas);
            Assert.StartsWith("BABELHUB__Contacto__Telefono:", problema, StringComparison.Ordinal);
            Assert.DoesNotContain(telefono.Trim(), problema, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("atencion@tienda.example")]
    [InlineData("a.b-c_d+e@sub.tienda-uno.pe")]
    [Trait("Criterio", "AC-COM-04")]
    public void Un_correo_valido_se_acepta_y_el_enlace_es_el_prefijo_fijo_mas_la_direccion(string correo)
    {
        var (datos, lector) = Leer("987654321", correo, DireccionValida);

        Assert.Empty(lector.Problemas);
        Assert.Equal("mailto:" + correo, datos!.EnlaceDeCorreo);
    }

    [Theory]
    [InlineData("a b@tienda.example")]
    [InlineData("atencion@tienda.example?cc=otro@x.com")]
    [InlineData("atencion@tienda.example&body=x")]
    [InlineData("atencion%40x@tienda.example")]
    [InlineData("a,b@tienda.example")]
    [InlineData("atencion@tienda.e")] // sufijo de una letra
    [InlineData("atencion@tienda.example\n")] // `\n` final: la expresión usa \z, no $
    [InlineData("ñ@tienda.example")]
    [InlineData("atencion@tiéndá.example")]
    [InlineData("atencion@tienda.Kom")] // signo Kelvin: no es ASCII
    [InlineData("atencion@tienda.coİ")] // İ
    [InlineData("atencion@tienda.😀om")]
    [InlineData("atencion@tienda.om")] // uso privado
    [InlineData("@tienda.example")]
    [InlineData("atencion@")]
    [InlineData("atencion@@tienda.example")]
    [InlineData("atencion")]
    [Trait("Criterio", "AC-COM-03")]
    public void Un_correo_con_formato_invalido_impide_arrancar_sin_mostrar_su_valor(string correo)
    {
        var (datos, lector) = Leer("987654321", correo, DireccionValida);

        Assert.Null(datos);
        var problema = Assert.Single(lector.Problemas);
        Assert.StartsWith("BABELHUB__Contacto__Correo:", problema, StringComparison.Ordinal);
        Assert.DoesNotContain(correo.Trim(), problema, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-03")]
    public void Un_correo_de_mas_de_254_caracteres_se_rechaza()
    {
        var largo = new string('a', 60) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".pe";

        var (datos, lector) = Leer("987654321", largo, DireccionValida);

        Assert.Null(datos);
        Assert.Contains(lector.Problemas, p => p.StartsWith("BABELHUB__Contacto__Correo:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Jr. Callao 123, Breña")]
    [InlineData("Av. Ejemplo 1234 Of. 5 #6, Ayacucho")] // `#` se acepta (la regla no lo prohíbe)
    [InlineData("Jr. Callao 123\uD800")] // sustituto suelto: no está en la regla; la salida lo codifica sin lanzar
    [InlineData("Jr. Callao 123")] // uso privado
    [Trait("Criterio", "AC-COM-04")]
    public void Una_direccion_con_tildes_y_enie_o_caracteres_fuera_de_la_regla_se_acepta(string direccion)
    {
        var (datos, lector) = Leer("987654321", "atencion@tienda.example", direccion);

        Assert.Empty(lector.Problemas);
        Assert.Equal(direccion, datos!.Direccion);
    }

    [Theory]
    [InlineData("Jr. Lima")] // 8 caracteres
    [InlineData("123456789")] // 9 caracteres
    [InlineData("<b>Jr. Callao 123</b>")]
    [InlineData("Jr. \"Callao\" 123")]
    [InlineData("Jr. O'Higgins 123")]
    [InlineData("Jr. A & B 123")]
    [InlineData("Jr. `Callao` 123")]
    [InlineData("Jr. Callao 123\n")]
    [InlineData("Jr. Callao‮ 123")] // dirección de escritura (Cf)
    [InlineData("Jr. Callao​ 123")] // ancho cero (Cf)
    [InlineData("Jr. Callao\x2028 123")] // separador de línea (Zl)
    [InlineData("Jr. Callao\x2029 123")] // separador de párrafo (Zp)
    [Trait("Criterio", "AC-COM-03")]
    public void Una_direccion_corta_o_con_caracteres_prohibidos_impide_arrancar(string direccion)
    {
        var (datos, lector) = Leer("987654321", "atencion@tienda.example", direccion);

        Assert.Null(datos);
        var problema = Assert.Single(lector.Problemas);
        Assert.StartsWith("BABELHUB__Contacto__Direccion:", problema, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-03")]
    public void El_largo_de_la_direccion_se_mide_en_unidades_UTF16_con_el_borde_en_200()
    {
        var en200 = new string('a', 200);
        var en201 = new string('a', 201);

        Assert.Empty(Leer("987654321", "atencion@tienda.example", en200).Lector.Problemas);
        Assert.Single(Leer("987654321", "atencion@tienda.example", en201).Lector.Problemas);
        Assert.Empty(Leer("987654321", "atencion@tienda.example", new string('b', 10)).Lector.Problemas);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-03")]
    public void Los_tres_datos_invalidos_a_la_vez_se_informan_juntos_sin_ningun_valor_en_los_mensajes()
    {
        var (datos, lector) = Leer("SECRETO-TEL", "SECRETO@CORREO", "<SECRETO-DIR>");

        Assert.Null(datos);
        Assert.Equal(3, lector.Problemas.Count);
        Assert.All(lector.Problemas, p => Assert.DoesNotContain("SECRETO", p, StringComparison.Ordinal));
        Assert.DoesNotContain("SECRETO", string.Join('|', new ConfiguracionDeSecretosInvalidaException(lector.Problemas).Message), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-01")]
    public void El_constructor_de_los_datos_no_es_publico_solo_la_fabrica_que_valida_los_crea() =>
        Assert.Empty(typeof(DatosDeContacto).GetConstructors());
}
