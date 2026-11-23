using System.Text;
using BabelHub.Inventario.Application.Ajustes.Interno;

namespace BabelHub.Inventario.UnitTests.Ajustes.CargaInicial;

/// <summary>Los lectores de los archivos de la carga inicial del inventario (CIN-04, CIN-11): el plan de referencia v1 que comparte con el E-commerce, y la entrada hostil que rechaza el plan o el archivo entero.</summary>
public sealed class LectoresDeLaCargaInicialTests
{
    private static readonly string[] Codigos = ["lima", "arequipa", "cusco", "ayacucho", "piura"];

    internal static string RaizDelRepositorio()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "BabelHub.sln")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string Plan(string ediciones, string version = "1") => "{\"version\":" + version + ",\"ediciones\":[" + ediciones + "]}";

    private static string Edicion(string id, string isbn, string stock) => "{\"edicionId\":\"" + id + "\",\"isbn\":\"" + isbn + "\",\"stock\":{" + stock + "}}";

    private const string IdA = "3f2b8c1e-5a47-4d0e-9b6a-0c1d2e3f4a51";

    private const string IdB = "7a9d4e20-1c3b-4f58-8e2d-6b5a4c3d2e12";

    [Fact]
    [Trait("Criterio", "AC-CIN-08")]
    public void El_plan_de_referencia_compartido_con_el_ecommerce_se_lee_completo()
    {
        var texto = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "tools", "carga-inicial", "plan-de-referencia.json"), Encoding.UTF8);

        var lectura = LectorDelPlanDeInventario.Leer(texto, Codigos);

        Assert.True(lectura.EsValido, string.Join(", ", lectura.Errores));
        var plan = lectura.Valor!;
        Assert.Equal(2, plan.Ediciones.Count);
        Assert.Equal(Guid.Parse(IdA), plan.Ediciones[0].EdicionId);
        Assert.Equal("9790000000018", plan.Ediciones[0].Isbn);
        Assert.Equal([new StockDeLaCarga("lima", 12), new StockDeLaCarga("ayacucho", 8), new StockDeLaCarga("cusco", 5)], plan.Ediciones[0].Stock);
    }

    public static TheoryData<string, string> PlanesHostiles() => new()
    {
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":3"), "2"), "version" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":3"), "1.5"), "version" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":6000")), "stock-fuera-de-rango" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":0")), "stock-fuera-de-rango" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":-3")), "stock-fuera-de-rango" },
        { Plan(Edicion(IdA, "9790000000018", "\"tacna\":3")), "sucursal-desconocida" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":3,\"lima\":4")), "clave-repetida" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":3") + "," + Edicion(IdA, "9790000000025", "\"lima\":3")), "edicion-repetida" },
        { Plan(Edicion(IdA, "9790000000018", "\"lima\":3") + "," + Edicion(IdB, "9790000000018", "\"lima\":3")), "edicion-repetida" },
        { Plan(Edicion("no-es-un-guid", "9790000000018", "\"lima\":3")), "edicion" },
        { Plan(Edicion("00000000-0000-0000-0000-000000000000", "9790000000018", "\"lima\":3")), "edicion" },
        { Plan(Edicion(IdA, "123", "\"lima\":3")), "edicion" },
        { Plan(Edicion(IdA, "9790000000018", "")), "edicion" },
        { """{"version":1,"ediciones":[],"extra":1}""", "clave-desconocida" },
        { """{"version":1,"version":1,"ediciones":[]}""", "clave-repetida" },
        { """{"version":1,"ediciones":[{"edicionId":"3f2b8c1e-5a47-4d0e-9b6a-0c1d2e3f4a51","isbn":"9790000000018","stock":{"lima":3},"precio":10}]}""", "clave-desconocida" },
        { """{"ediciones":[]}""", "version" },
        { """{"version":1}""", "sin-ediciones" },
        { """{"version":1,"ediciones":[""", "json-invalido" },
        { "[1,2,3]", "json-invalido" },
        { """{"version":1,"ediciones":[]} basura""", "json-invalido" },
        { "{\"version\":1,\"ediciones\":[] /* comentario */}", "json-invalido" },
        { "{\"version\":1,\"ediciones\":[]}" + (char)0x202E, "caracter-no-permitido" },
        { "{\"version\":1,\"ediciones\":[]}" + (char)0x2028, "caracter-no-permitido" },
        { "{\"version\":1,\"ediciones\":[]}\u0007", "caracter-no-permitido" },
        { string.Empty, "archivo-vacio" },
        { """{"version":1,"ediciones":[{"edicionId":"3f2b8c1e-5a47-4d0e-9b6a-0c1d2e3f4a51","isbn":"9790000000018","stock":{"lima":{"a":{"b":{"c":{"d":1}}}}}}]}""", "json-invalido" },
    };

    [Theory]
    [MemberData(nameof(PlanesHostiles))]
    [Trait("Criterio", "AC-CIN-08")]
    public void Un_plan_manipulado_se_rechaza_completo_con_su_motivo(string texto, string clave)
    {
        var lectura = LectorDelPlanDeInventario.Leer(texto, Codigos);

        Assert.False(lectura.EsValido);
        Assert.Equal(clave, Assert.Single(lectura.Errores).Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-08")]
    public void Un_plan_con_mas_de_500_ediciones_o_mas_de_2_MB_se_rechaza()
    {
        var muchas = Plan(string.Join(",", Enumerable.Range(1, 501).Select(i => Edicion(new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]).ToString("D"), $"9790000{i:D5}0", "\"lima\":3"))));
        var enorme = Plan(Edicion(IdA, "9790000000018", "\"lima\":3")) + new string(' ', (2 * 1024 * 1024) + 1);

        Assert.Equal("demasiadas-ediciones", Assert.Single(LectorDelPlanDeInventario.Leer(muchas, Codigos).Errores).Clave);
        Assert.Equal("archivo-demasiado-grande", Assert.Single(LectorDelPlanDeInventario.Leer(enorme, Codigos).Errores).Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public void Las_sucursales_y_las_rutas_validas_se_leen_y_una_celda_con_signo_de_formula_es_texto()
    {
        var sucursales = LectorDeSucursalesYRutas.LeerSucursales("codigo;nombre;region\r\nlima;Lima Centro;lima\r\ncusco;\"=2+2; Cusco\";cusco\r\n");
        var rutas = LectorDeSucursalesYRutas.LeerRutas("origen;destino;dias\nlima;cusco;2\ncusco;lima;2\n", ["lima", "cusco"]);

        Assert.True(sucursales.EsValido, string.Join(", ", sucursales.Errores));
        Assert.Equal("=2+2; Cusco", sucursales.Valor![1].Nombre);
        Assert.True(rutas.EsValido);
        Assert.Equal(2, rutas.Valor!.Count);
    }

    [Theory]
    [InlineData("codigo;nombre;region\nlima;Lima;lima\nlima;Otra;lima\n", "codigo-repetido")]
    [InlineData("codigo;nombre;region\nLIMA;Lima;lima\n", "codigo")]
    [InlineData("codigo;nombre;region\n../x;Lima;lima\n", "codigo")]
    [InlineData("codigo;nombre;region\nlima;Lima;marte\n", "region")]
    [InlineData("codigo;nombre;region;extra\nlima;Lima;lima;1\n", "columnas-invalidas")]
    [InlineData("codigo;nombre\nlima;Lima\n", "columnas-invalidas")]
    [InlineData("codigo;nombre;region\nlima;Lima\n", "fila-mal-formada")]
    [InlineData("codigo;nombre;region\nlima;\"Lima;lima\n", "fila-mal-formada")]
    [InlineData("codigo;nombre;region\nlima;Lima‮;lima\n", "caracter-no-permitido")]
    [InlineData("codigo;nombre;region\n", "sin-sucursales")]
    [InlineData("", "archivo-vacio")]
    [Trait("Criterio", "AC-CIN-04")]
    public void Un_archivo_de_sucursales_invalido_se_rechaza_con_su_motivo(string texto, string clave)
    {
        var lectura = LectorDeSucursalesYRutas.LeerSucursales(texto);

        Assert.False(lectura.EsValido);
        Assert.Contains(lectura.Errores, e => e.Clave == clave);
    }

    [Theory]
    [InlineData("origen;destino;dias\nlima;tacna;2\n", "sucursal-desconocida")]
    [InlineData("origen;destino;dias\nlima;lima;2\n", "sucursal-desconocida")]
    [InlineData("origen;destino;dias\nlima;cusco;0\n", "dias")]
    [InlineData("origen;destino;dias\nlima;cusco;dos\n", "dias")]
    [InlineData("origen;destino;dias\nlima;cusco;2\nlima;cusco;3\n", "ruta-repetida")]
    [Trait("Criterio", "AC-CIN-04")]
    public void Un_archivo_de_rutas_invalido_se_rechaza_con_su_motivo(string texto, string clave)
    {
        var lectura = LectorDeSucursalesYRutas.LeerRutas(texto, ["lima", "cusco"]);

        Assert.False(lectura.EsValido);
        Assert.Contains(lectura.Errores, e => e.Clave == clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public void Un_archivo_de_mas_de_500_filas_se_rechaza_completo()
    {
        var filas = string.Join("\n", Enumerable.Range(1, 501).Select(i => $"s{i};Sucursal {i};lima"));

        var lectura = LectorDeSucursalesYRutas.LeerSucursales("codigo;nombre;region\n" + filas);

        Assert.Equal("demasiadas-filas", Assert.Single(lectura.Errores).Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-04")]
    public void Los_archivos_versionados_del_repositorio_son_validos()
    {
        var carpeta = Path.Combine(RaizDelRepositorio(), "tools", "carga-inicial");

        var sucursales = LectorDeSucursalesYRutas.LeerSucursales(File.ReadAllText(Path.Combine(carpeta, "sucursales.csv"), Encoding.UTF8));
        var rutas = LectorDeSucursalesYRutas.LeerRutas(File.ReadAllText(Path.Combine(carpeta, "rutas.csv"), Encoding.UTF8), sucursales.Valor!.Select(s => s.Codigo).ToList());

        Assert.True(sucursales.EsValido, string.Join(", ", sucursales.Errores));
        Assert.Equal("lima", sucursales.Valor![0].Codigo);
        Assert.Equal(6, sucursales.Valor.Count);
        Assert.True(rutas.EsValido, string.Join(", ", rutas.Errores));
        Assert.Equal(10, rutas.Valor!.Count);
    }
}
