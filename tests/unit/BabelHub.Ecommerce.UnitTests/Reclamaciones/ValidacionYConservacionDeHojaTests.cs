using System.Text.Json;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>Los datos del consumidor, la validación de la hoja y la política de conservación (T02; REC-03, REC-04, REC-17).</summary>
public sealed class ValidacionYConservacionDeHojaTests
{
    private static EntradaDeHoja Completa() =>
        new("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123", "ana@correo.com", "Libro", 49.90m, "reclamo", "Llegó dañado.", "PED-1");

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public void Una_hoja_con_todos_los_datos_es_valida_y_el_pedido_es_opcional()
    {
        var resultado = ValidacionDeHoja.Validar(Completa() with { PedidoReferencia = null });

        Assert.True(resultado.EsValida);
        Assert.Empty(resultado.Faltan);
        Assert.Empty(resultado.Invalidos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public void Una_hoja_vacia_lista_cada_campo_obligatorio_que_falta_sin_repetir_ningun_valor()
    {
        var resultado = ValidacionDeHoja.Validar(new EntradaDeHoja(null, " ", "", null, null, null, null, "  ", null));

        Assert.False(resultado.EsValida);
        Assert.Equal(
            new[] { CampoDeHoja.Nombre, CampoDeHoja.Documento, CampoDeHoja.Domicilio, CampoDeHoja.Correo, CampoDeHoja.BienContratado, CampoDeHoja.MontoReclamado, CampoDeHoja.Tipo, CampoDeHoja.Detalle },
            resultado.Faltan);
        Assert.Empty(resultado.Invalidos);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-03")]
    [InlineData(CampoDeHoja.Detalle)]
    [InlineData(CampoDeHoja.Documento)]
    [InlineData(CampoDeHoja.Domicilio)]
    public void Una_hoja_sin_detalle_documento_o_domicilio_se_rechaza_indicando_ese_campo(CampoDeHoja campo)
    {
        var entrada = campo switch
        {
            CampoDeHoja.Detalle => Completa() with { Detalle = null },
            CampoDeHoja.Documento => Completa() with { Documento = null },
            _ => Completa() with { Domicilio = null },
        };

        var resultado = ValidacionDeHoja.Validar(entrada);

        Assert.Equal(new[] { campo }, resultado.Faltan);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public void Los_largos_maximos_se_validan_y_el_campo_se_lista_como_invalido()
    {
        var entrada = Completa() with
        {
            Nombre = new string('a', ValidacionDeHoja.LongitudMaximaDelNombre + 1),
            Documento = new string('1', ValidacionDeHoja.LongitudMaximaDelDocumento + 1),
            Domicilio = new string('d', ValidacionDeHoja.LongitudMaximaDelDomicilio + 1),
            BienContratado = new string('b', ValidacionDeHoja.LongitudMaximaDelBien + 1),
            Detalle = new string('x', ValidacionDeHoja.LongitudMaximaDelDetalle + 1),
            PedidoReferencia = new string('p', ValidacionDeHoja.LongitudMaximaDeLaReferenciaDelPedido + 1),
        };

        var resultado = ValidacionDeHoja.Validar(entrada);

        Assert.Empty(resultado.Faltan);
        Assert.Equal(
            new[] { CampoDeHoja.Nombre, CampoDeHoja.Documento, CampoDeHoja.Domicilio, CampoDeHoja.BienContratado, CampoDeHoja.Detalle, CampoDeHoja.PedidoReferencia },
            resultado.Invalidos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public void Los_valores_en_el_largo_maximo_exacto_son_validos()
    {
        var entrada = Completa() with
        {
            Nombre = new string('a', 120),
            Documento = new string('1', 20),
            Domicilio = new string('d', 200),
            BienContratado = new string('b', 200),
            Detalle = new string('x', 3000),
            PedidoReferencia = new string('p', 40),
        };

        Assert.True(ValidacionDeHoja.Validar(entrada).EsValida);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-03")]
    [InlineData(-0.01)]
    [InlineData(1.005)]
    [InlineData(10000000000.00)]
    public void Un_monto_negativo_con_mas_de_dos_decimales_o_demasiado_grande_es_invalido(double monto)
    {
        var resultado = ValidacionDeHoja.Validar(Completa() with { MontoReclamado = (decimal)monto });

        Assert.Equal(new[] { CampoDeHoja.MontoReclamado }, resultado.Invalidos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public void Un_monto_de_cero_es_valido_y_un_tipo_desconocido_es_invalido()
    {
        Assert.True(ValidacionDeHoja.Validar(Completa() with { MontoReclamado = 0m }).EsValida);
        Assert.Equal(new[] { CampoDeHoja.Tipo }, ValidacionDeHoja.Validar(Completa() with { Tipo = "denuncia" }).Invalidos);
        Assert.True(ValidacionDeHoja.Validar(Completa() with { Tipo = "queja" }).EsValida);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-03")]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("a@@b.com")]
    [InlineData("a b@correo.com")]
    [InlineData("a@correo.com, b@correo.com")]
    [InlineData("a@correo.com;b@correo.com")]
    [InlineData("Ana <a@correo.com>")]
    [InlineData("a@correo.com\r\nBcc: x@y.com")]
    [InlineData("a@correo.com\nb@correo.com")]
    [InlineData("<a@correo.com>")]
    [InlineData("@correo.com")]
    [InlineData("a@.com")]
    public void Un_correo_con_varias_direcciones_nombre_para_mostrar_o_saltos_de_linea_es_invalido(string correo)
    {
        var resultado = ValidacionDeHoja.Validar(Completa() with { Correo = correo });

        Assert.Equal(new[] { CampoDeHoja.Correo }, resultado.Invalidos);
        Assert.False(CorreoNormalizado.TryCrear(correo, out _));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public void El_correo_admite_hasta_254_caracteres()
    {
        var local = new string('a', 64);
        var largo = $"{local}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 53)}.com";
        Assert.True(largo.Length <= 254);
        Assert.True(CorreoNormalizado.TryCrear(largo, out _));

        var demasiado = $"{local}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 63)}.{new string('e', 63)}.com";
        Assert.True(demasiado.Length > 254);
        Assert.False(CorreoNormalizado.TryCrear(demasiado, out _));
        Assert.Equal(new[] { CampoDeHoja.Correo }, ValidacionDeHoja.Validar(Completa() with { Correo = demasiado }).Invalidos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public void El_correo_normalizado_se_recorta_y_va_en_minusculas_sin_resolver_alias()
    {
        Assert.True(CorreoNormalizado.TryCrear("  Ana.Perez+Libros@Correo.COM ", out var normalizado));

        Assert.Equal("ana.perez+libros@correo.com", normalizado);
        Assert.Equal(CorreoNormalizado.Normalizar("ANA@x.com"), CorreoNormalizado.Normalizar(" ana@X.COM"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public void La_validacion_nunca_repite_el_texto_escrito_por_el_consumidor()
    {
        var resultado = ValidacionDeHoja.Validar(Completa() with { Correo = "secreto-sin-arroba", Documento = null });

        var texto = resultado.ToString() + string.Join(',', resultado.Faltan) + string.Join(',', resultado.Invalidos);

        Assert.DoesNotContain("secreto", texto);
        Assert.DoesNotContain("Ana", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public void Los_datos_del_consumidor_no_se_muestran_ni_se_serializan()
    {
        var datos = new DatosDelConsumidor("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123", "Ana@Correo.com");

        var texto = datos.ToString() + JsonSerializer.Serialize(datos) + JsonSerializer.Serialize(new { Datos = datos });

        Assert.Equal("[redactado]", datos.ToString());
        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("12345678", texto);
        Assert.DoesNotContain("Olivos", texto);
        Assert.DoesNotContain("correo.com", texto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ana@correo.com", datos.CorreoNormalizado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public void Los_datos_anonimizados_no_son_una_direccion_valida_ni_tienen_correo_normalizado()
    {
        var anonimos = DatosDelConsumidor.Anonimizados();

        Assert.False(CorreoNormalizado.TryCrear(anonimos.Correo, out _));
        Assert.Null(anonimos.CorreoNormalizado);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, anonimos.Nombre);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, anonimos.Documento);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, anonimos.Domicilio);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public void La_conservacion_es_de_dos_anios_por_dia_de_lima_y_se_anonimiza_desde_el_dia_del_cumpleanios()
    {
        // Registrada el 18-nov-2024 a las 22:00 en Lima (19-nov 03:00 UTC): su día es el 18.
        var registro = new DateTimeOffset(2024, 11, 19, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 11, 18), PoliticaDeConservacion.PrimerDiaAnonimizable(registro));
        // El día anterior (17-nov-2026 en Lima) todavía no: 2 años menos un día.
        Assert.False(PoliticaDeConservacion.DebeAnonimizarse(registro, new DateTimeOffset(2026, 11, 18, 4, 59, 0, TimeSpan.Zero)));
        // El 18-nov-2026 a las 00:00 en Lima (05:00 UTC) ya sí: dos años y un día antes de acabar el 18 cuenta como cumplido.
        Assert.True(PoliticaDeConservacion.DebeAnonimizarse(registro, new DateTimeOffset(2026, 11, 18, 5, 0, 0, TimeSpan.Zero)));
        Assert.True(PoliticaDeConservacion.DebeAnonimizarse(registro, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public void La_conservacion_de_una_hoja_registrada_un_29_de_febrero_cuenta_desde_el_28()
    {
        var registro = new DateTimeOffset(2024, 2, 29, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 2, 28), PoliticaDeConservacion.PrimerDiaAnonimizable(registro));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public void Dos_anios_y_un_dia_se_anonimizan_y_dos_anios_menos_un_dia_no()
    {
        var registro = new DateTimeOffset(2024, 6, 10, 15, 0, 0, TimeSpan.Zero);

        Assert.True(PoliticaDeConservacion.DebeAnonimizarse(registro, registro.AddYears(2).AddDays(1)));
        Assert.False(PoliticaDeConservacion.DebeAnonimizarse(registro, registro.AddYears(2).AddDays(-1)));
        Assert.False(PoliticaDeConservacion.DebeAnonimizarse(registro, registro));
    }
}
