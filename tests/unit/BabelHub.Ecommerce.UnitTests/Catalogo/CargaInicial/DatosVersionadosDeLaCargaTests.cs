using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;

namespace BabelHub.Ecommerce.UnitTests.Catalogo.CargaInicial;

/// <summary>
/// Los datos versionados de la carga (T09; CIN-15, CIN-16; AC-CIN-12): el <c>catalogo.csv</c> real del repositorio cumple las reglas del lector y es reproducible por la regla determinista de <c>completar-datos.ps1</c> (FNV-1a de la
/// semilla fija); y el plan de inventario v1 que escribe el catálogo es el contrato que lee el Inventario (<c>plan-de-referencia.json</c>, el mismo archivo que lee la prueba del Inventario).
/// </summary>
[Trait("Entorno", "ConRepositorio")]
public sealed class DatosVersionadosDeLaCargaTests
{
    private const string Semilla = "babelhub-carga-inicial-2026-11";

    private static readonly string[] SucursalesSinLima = ["arequipa", "trujillo", "cusco", "piura", "ayacucho"];

    private static string Raiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string RutaDeDatos(string archivo) => Path.Combine(Raiz(), "tools", "carga-inicial", archivo);

    /// <summary>La regla de <c>completar-datos.ps1</c>: FNV-1a de 32 bits de «semilla|clave|etiqueta» entre 2^32.</summary>
    private static double U(string clave, string etiqueta)
    {
        ulong h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes($"{Semilla}|{clave}|{etiqueta}"))
        {
            h = ((h ^ b) * 16777619UL) % 4294967296UL;
        }

        return h / 4294967296.0;
    }

    private static string Moneda(double valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Isbn(long serie)
    {
        var doce = "9790" + serie.ToString("D8", CultureInfo.InvariantCulture);
        var suma = doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return doce + ((10 - (suma % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }

    private static ArchivoDeCargaLeido Leer() => LectorDelArchivoDeCarga.Leer(File.ReadAllText(RutaDeDatos("catalogo.csv"), new UTF8Encoding(false, throwOnInvalidBytes: true)));

    [Fact]
    [Trait("Criterio", "AC-CIN-12")]
    public void El_archivo_versionado_tiene_de_20_a_40_obras_validas_con_isbn_unicos_precios_y_stock_en_rango_y_descripciones_de_hasta_600()
    {
        var leido = Leer();

        Assert.Null(leido.MotivoDelArchivo);
        Assert.Empty(leido.Errores);
        Assert.InRange(leido.Filas.Count, 20, 40);
        var isbns = leido.Filas.SelectMany(f => new[] { f.IsbnImpreso.Isbn13 }.Concat(f.Digital is null ? [] : [f.Digital.Isbn.Isbn13])).ToList();
        Assert.Equal(isbns.Count, isbns.Distinct(StringComparer.Ordinal).Count());
        Assert.All(leido.Filas, f =>
        {
            Assert.InRange(f.PrecioImpreso, 25.00m, 120.00m);
            Assert.All(f.Stock, s => Assert.InRange(s.Value, 3, 25));
            Assert.NotEmpty(f.Stock);
            Assert.Contains(f.Stock, s => s.Key == "lima");
            Assert.True(f.Descripcion is { Length: > 0 and <= 600 });
            if (f.Digital is { } digital)
            {
                Assert.InRange(digital.Precio, 5.00m, 120.00m);
            }
        });
        Assert.Contains(leido.Filas, f => f.Digital is not null);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-12")]
    public void Ninguna_fila_del_archivo_versionado_repite_una_portada_ni_una_clave()
    {
        var leido = Leer();

        Assert.Equal(leido.Filas.Count, leido.Filas.Select(f => f.Portada).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(leido.Filas.Count, leido.Filas.Select(f => f.Clave).Distinct(StringComparer.Ordinal).Count());
        Assert.All(leido.Filas, f => Assert.EndsWith(".png", f.Portada, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-12")]
    public void Los_precios_el_stock_y_los_isbn_provisionales_del_archivo_son_los_que_produce_la_regla_determinista_documentada()
    {
        var leido = Leer();

        foreach (var fila in leido.Filas)
        {
            // Los ISBN y el stock se completaron con el número de la fila de datos (la primera es 1); la fila del archivo es ese número más 1.
            var n = fila.Numero - 1;
            Assert.Equal(Isbn(n), fila.IsbnImpreso.Isbn13);
            var precio = Math.Floor(25 + (U(fila.Clave, "precio") * 95)) + 0.90;
            Assert.Equal(Moneda(precio), fila.PrecioImpreso.ToString("0.00", CultureInfo.InvariantCulture));
            if (fila.Digital is { } digital)
            {
                Assert.Equal(Isbn(1000 + n), digital.Isbn.Isbn13);
                Assert.Equal(Moneda(Math.Floor(double.Parse(Moneda(precio), CultureInfo.InvariantCulture) * 0.45) + 0.90), digital.Precio.ToString("0.00", CultureInfo.InvariantCulture));
            }

            var otras = SucursalesSinLima.OrderBy(s => U(fila.Clave, "orden-" + s)).Take(2).ToList();
            var esperadas = new[] { "lima" }.Concat(otras).ToDictionary(s => s, s => 3 + (int)Math.Floor(U(fila.Clave, "stock-" + s) * 23));
            Assert.Equal(esperadas.OrderBy(e => e.Key).ToList(), fila.Stock.OrderBy(e => e.Key).ToList());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-08")]
    public void El_plan_que_escribe_el_catalogo_es_el_json_v1_de_referencia_que_lee_el_inventario()
    {
        var referencia = JsonNode.Parse(File.ReadAllText(RutaDeDatos("plan-de-referencia.json")))!;
        var plan = new PlanDeInventarioDeLaCarga(
        [
            new EdicionDelPlanDeInventario(Guid.Parse("3f2b8c1e-5a47-4d0e-9b6a-0c1d2e3f4a51"), "9790000000018", [new("lima", 12), new("ayacucho", 8), new("cusco", 5)]),
            new EdicionDelPlanDeInventario(Guid.Parse("7a9d4e20-1c3b-4f58-8e2d-6b5a4c3d2e12"), "9790000000025", [new("lima", 25), new("piura", 3)]),
        ]);

        var escrito = JsonNode.Parse(plan.ComoJson())!;

        Assert.True(JsonNode.DeepEquals(referencia, escrito));
        Assert.EndsWith("\n", plan.ComoJson(), StringComparison.Ordinal);
    }
}
