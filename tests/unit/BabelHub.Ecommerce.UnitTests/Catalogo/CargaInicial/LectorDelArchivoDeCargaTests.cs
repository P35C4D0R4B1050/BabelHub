using System.Text;
using BabelHub.Ecommerce.Application.Catalogo.Interno;

namespace BabelHub.Ecommerce.UnitTests.Catalogo.CargaInicial;

/// <summary>El lector y validador del archivo de datos de la carga inicial (T01; CIN-03, CIN-04, CIN-15; AC-CIN-03, AC-CIN-04, AC-CIN-12).</summary>
public sealed class LectorDelArchivoDeCargaTests
{
    internal const string Cabecera = "clave;titulo;autor;editorial;categoria;descripcion;portada;isbn_impreso;precio_impreso;isbn_digital;precio_digital;formato_digital;titular_digital;licencia_digital;vigente_hasta;stock_lima;stock_cusco";

    /// <summary>Un ISBN-13 de formato válido del rango 979-0 con el dígito de control calculado.</summary>
    internal static string Isbn(int n)
    {
        var cuerpo = "9790" + n.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var suma = cuerpo.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return cuerpo + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string Fila(string clave = "obra-1", string titulo = "Una obra", string autor = "Ana", string editorial = "Andina", string categoria = "Ficción", string descripcion = "Una sinopsis.", string portada = "obra-1.png", string? isbn = null, string precio = "45.90", string digital = ";;;;;", string stock = "12;")
        => $"{clave};{titulo};{autor};{editorial};{categoria};{descripcion};{portada};{isbn ?? Isbn(1)};{precio};{digital};{stock}";

    internal static string Archivo(params string[] filas) => Cabecera + "\n" + string.Join("\n", filas) + "\n";

    [Fact]
    [Trait("Criterio", "AC-CIN-12")]
    public void Una_fila_valida_con_digital_y_stock_se_lee_completa()
    {
        var digital = $"{Isbn(2)};22.90;epub;Dominio público;dominio-publico;";
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(digital: digital, stock: "12;5")));

        Assert.Null(leido.MotivoDelArchivo);
        Assert.Empty(leido.Errores);
        var fila = Assert.Single(leido.Filas);
        Assert.Equal(2, fila.Numero);
        Assert.Equal(45.90m, fila.PrecioImpreso);
        Assert.Equal(Isbn(1), fila.IsbnImpreso.Isbn13);
        Assert.NotNull(fila.Digital);
        Assert.Equal(22.90m, fila.Digital!.Precio);
        Assert.Equal(new[] { "lima", "cusco" }, fila.Stock.Select(s => s.Key));
        Assert.Equal(new[] { 12, 5 }, fila.Stock.Select(s => s.Value));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public void Cada_defecto_de_una_fila_la_rechaza_con_su_numero_y_su_campo_sin_repetir_lo_recibido()
    {
        var comercialSinVigencia = $"{Isbn(9)};30.00;epub;Editorial;comercial;";
        var archivo = Archivo(
            Fila(clave: "isbn-malo", isbn: "9790000000019"),
            Fila(clave: "precio-bajo", isbn: Isbn(3), precio: "10.00"),
            Fila(clave: "campo-vacio", isbn: Isbn(4), autor: string.Empty),
            Fila(clave: "descripcion-larga", isbn: Isbn(5), descripcion: new string('x', 601)),
            Fila(clave: "portada-fuera", isbn: Isbn(6), portada: "../otra.png"),
            Fila(clave: "primera", isbn: Isbn(7)),
            Fila(clave: "isbn-repetido", isbn: Isbn(7)),
            Fila(clave: "sin-vigencia", isbn: Isbn(8), digital: comercialSinVigencia),
            Fila(clave: "valida", isbn: Isbn(10)));

        var leido = LectorDelArchivoDeCarga.Leer(archivo);

        Assert.Null(leido.MotivoDelArchivo);
        Assert.Equal(new[] { "primera", "valida" }, leido.Filas.Select(f => f.Clave));
        var errores = leido.Errores.ToDictionary(e => e.Clave);
        Assert.Equal(("isbn_impreso", "isbn-invalido"), (errores["isbn-malo"].Campo, errores["isbn-malo"].Motivo));
        Assert.Equal(("precio_impreso", "precio-fuera-de-rango"), (errores["precio-bajo"].Campo, errores["precio-bajo"].Motivo));
        Assert.Equal(("autor", "obligatorio"), (errores["campo-vacio"].Campo, errores["campo-vacio"].Motivo));
        Assert.Equal(("descripcion", "demasiado-larga"), (errores["descripcion-larga"].Campo, errores["descripcion-larga"].Motivo));
        Assert.Equal(("portada", "portada-invalida"), (errores["portada-fuera"].Campo, errores["portada-fuera"].Motivo));
        Assert.Equal(("isbn_impreso", "isbn-repetido"), (errores["isbn-repetido"].Campo, errores["isbn-repetido"].Motivo));
        Assert.Equal(("vigente_hasta", "obligatorio"), (errores["sin-vigencia"].Campo, errores["sin-vigencia"].Motivo));
        // La segunda aparición del ISBN es la rechazada y el número de fila es el del archivo (la cabecera es la fila 1).
        Assert.Equal(8, errores["isbn-repetido"].Fila);
        Assert.DoesNotContain(leido.Errores, e => e.Motivo.Contains("xxx", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("24.99", false)]
    [InlineData("25.00", true)]
    [InlineData("120.00", true)]
    [InlineData("120.01", false)]
    [InlineData("45,90", false)]
    [Trait("Criterio", "AC-CIN-12")]
    public void El_precio_impreso_va_de_25_a_120_con_punto_decimal(string precio, bool valido)
    {
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(precio: precio)));

        Assert.Equal(valido, leido.Filas.Count == 1);
    }

    [Theory]
    [InlineData("4.99", false)]
    [InlineData("5.00", true)]
    [InlineData("120.00", true)]
    [InlineData("120.01", false)]
    [Trait("Criterio", "AC-CIN-12")]
    public void El_precio_digital_va_de_5_a_120(string precio, bool valido)
    {
        var digital = $"{Isbn(2)};{precio};epub;Dominio público;dominio-publico;";

        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(digital: digital)));

        Assert.Equal(valido, leido.Filas.Count == 1);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("5000", true)]
    [InlineData("5001", false)]
    [InlineData("tres", false)]
    [Trait("Criterio", "AC-CIN-12")]
    public void El_stock_declarado_va_de_1_a_5000(string stock, bool valido)
    {
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(stock: stock + ";")));

        Assert.Equal(valido, leido.Filas.Count == 1);
        if (!valido)
        {
            Assert.Equal("stock-fuera-de-rango", Assert.Single(leido.Errores).Motivo);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-12")]
    public void Una_celda_de_stock_vacia_no_carga_esa_sucursal()
    {
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(stock: ";7")));

        var fila = Assert.Single(leido.Filas);
        Assert.Equal(new[] { "cusco" }, fila.Stock.Select(s => s.Key));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public void Una_licencia_incoherente_con_su_tipo_rechaza_la_fila()
    {
        var dominioPublicoConVigencia = $"{Isbn(2)};22.90;epub;Titular;dominio-publico;2030-01-01";
        var tipoDesconocido = $"{Isbn(3)};22.90;epub;Titular;gratis;";
        var digitalSinFormato = $"{Isbn(4)};22.90;;Titular;comercial;2030-01-01";

        var leido = LectorDelArchivoDeCarga.Leer(Archivo(
            Fila(clave: "a", isbn: Isbn(11), digital: dominioPublicoConVigencia),
            Fila(clave: "b", isbn: Isbn(12), digital: tipoDesconocido),
            Fila(clave: "c", isbn: Isbn(13), digital: digitalSinFormato)));

        Assert.Empty(leido.Filas);
        Assert.Equal(new[] { "licencia-incoherente", "licencia-invalida", "digital-incompleta" }, leido.Errores.Select(e => e.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public void Un_archivo_de_mas_de_500_filas_se_rechaza_completo()
    {
        var filas = Enumerable.Range(1, 501).Select(i => Fila(clave: "obra-" + i, isbn: Isbn(i))).ToArray();

        var leido = LectorDelArchivoDeCarga.Leer(Archivo(filas));

        Assert.Equal("demasiadas-filas", leido.MotivoDelArchivo);
        Assert.Empty(leido.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public void Un_archivo_de_mas_de_2_MB_se_rechaza_completo()
    {
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(descripcion: new string('x', LectorDelArchivoDeCarga.MaximoDeBytes))));

        Assert.Equal("archivo-demasiado-grande", leido.MotivoDelArchivo);
    }

    [Theory]
    [InlineData("columna-desconocida", "clave;titulo;autor;editorial;categoria;descripcion;portada;isbn_impreso;precio_impreso;isbn_digital;precio_digital;formato_digital;titular_digital;licencia_digital;vigente_hasta;extra")]
    [InlineData("columna-faltante", "clave;titulo;autor;editorial;categoria;descripcion;portada;isbn_impreso;precio_impreso")]
    [InlineData("columna-repetida", "clave;clave;titulo;autor;editorial;categoria;descripcion;portada;isbn_impreso;precio_impreso;isbn_digital;precio_digital;formato_digital;titular_digital;licencia_digital;vigente_hasta")]
    [Trait("Criterio", "AC-CIN-04")]
    public void Una_cabecera_con_columnas_desconocidas_faltantes_o_repetidas_rechaza_el_archivo(string motivo, string cabecera)
    {
        var leido = LectorDelArchivoDeCarga.Leer(cabecera + "\nx\n");

        Assert.Equal(motivo, leido.MotivoDelArchivo);
    }

    [Theory]
    [InlineData("\u0001")]
    [InlineData("\t")]
    [InlineData("\u202E")]
    [InlineData("\u2028")]
    [InlineData("\uFFFD")]
    [Trait("Criterio", "AC-CIN-04")]
    public void Un_caracter_de_control_o_de_direccion_de_texto_rechaza_el_archivo(string caracter)
    {
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(descripcion: "Una" + caracter + "sinopsis")));

        Assert.Equal("caracter-prohibido", leido.MotivoDelArchivo);
        Assert.Empty(leido.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public void Una_celda_que_empieza_con_un_signo_de_formula_es_texto_y_las_comillas_protegen_el_punto_y_coma()
    {
        var leido = LectorDelArchivoDeCarga.Leer(Archivo(Fila(titulo: "=SUMA(1,2)", descripcion: "\"Una; sinopsis con \"\"comillas\"\" y punto y coma\"", autor: "-Ana", editorial: "@Andina")));

        var fila = Assert.Single(leido.Filas);
        Assert.Equal("=SUMA(1,2)", fila.Titulo);
        Assert.Equal("Una; sinopsis con \"comillas\" y punto y coma", fila.Descripcion);
        Assert.Equal("-Ana", fila.Autor);
        Assert.Equal("@Andina", fila.Editorial);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public void Las_comillas_sin_cerrar_o_un_numero_distinto_de_columnas_rechazan_solo_esa_fila()
    {
        var archivo = Archivo("clave;\"sin cerrar", "solo;dos;columnas", Fila(clave: "buena"));

        var leido = LectorDelArchivoDeCarga.Leer(archivo);

        Assert.Equal(new[] { "buena" }, leido.Filas.Select(f => f.Clave));
        Assert.Equal(new[] { "comillas-sin-cerrar", "columnas-distintas-de-la-cabecera" }, leido.Errores.Select(e => e.Motivo));
        Assert.Equal(new[] { 2, 3 }, leido.Errores.Select(e => e.Fila));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-03")]
    public void Un_sustituto_suelto_de_UTF16_rechaza_el_archivo_completo()
    {
        var conSustituto = Fila(clave: "malo", isbn: Isbn(20), descripcion: "a\uD800b");

        var leido = LectorDelArchivoDeCarga.Leer(Archivo(conSustituto, Fila(clave: "bueno", isbn: Isbn(21))));

        Assert.Equal("caracter-prohibido", leido.MotivoDelArchivo);
        Assert.Empty(leido.Filas);
    }
}
