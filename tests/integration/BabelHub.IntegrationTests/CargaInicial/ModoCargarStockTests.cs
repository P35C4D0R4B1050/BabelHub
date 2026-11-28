using BabelHub.Inventario.Api.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.CargaInicial;

/// <summary>La orden <c>cargar-stock</c> del Inventario: sus argumentos, el rechazo del modo Operacion desde la configuración y el fallo sin configuración (CIN-13, CIN-17, CIN-20): termina antes de escribir, nombra la variable y nunca imprime valores ni rutas.</summary>
public sealed class ModoCargarStockTests
{
    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public void Los_argumentos_se_leen_y_un_argumento_desconocido_o_faltante_se_rechaza_con_una_clave()
    {
        var (completos, _) = ArgumentosDeCargarStock.Leer(["--plan", "p.json", "--sucursales", "s.csv", "--rutas", "r.csv", "--esperar"]);
        var (minimos, _) = ArgumentosDeCargarStock.Leer(["--sucursales", "s.csv", "--plan", "p.json"]);

        Assert.Equal(new ArgumentosDeCargarStock("p.json", "s.csv", "r.csv", true), completos);
        Assert.Equal(new ArgumentosDeCargarStock("p.json", "s.csv", null, false), minimos);
        Assert.Equal("falta-plan", ArgumentosDeCargarStock.Leer(["--sucursales", "s.csv"]).Error);
        Assert.Equal("falta-sucursales", ArgumentosDeCargarStock.Leer(["--plan", "p.json"]).Error);
        Assert.Equal("falta-el-valor-de-plan", ArgumentosDeCargarStock.Leer(["--plan"]).Error);
        Assert.Equal("falta-el-valor-de-plan", ArgumentosDeCargarStock.Leer(["--plan", "--sucursales", "s.csv"]).Error);
        Assert.Equal("argumento-desconocido", ArgumentosDeCargarStock.Leer(["--plan", "p.json", "--sucursales", "s.csv", "--forzar"]).Error);
    }

    [Theory]
    [InlineData(true)]
    [Trait("Criterio", "AC-CIN-13")]
    public void El_servicio_nunca_arranca_en_modo_Operacion_desde_la_configuracion(bool produccion)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modo"] = "Operacion" }).Build();

        Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(configuracion, produccion, DateTimeOffset.UtcNow, new AlmacenDeNoncesDePrueba()));
        Assert.Throws<BabelHub.Ecommerce.Api.Plataforma.ArranqueRechazadoException>(() => BabelHub.Ecommerce.Api.Plataforma.ArranqueSeguro.Resolver(configuracion, produccion, DateTimeOffset.UtcNow, new AlmacenDeNoncesDeEcommerceDePrueba()));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public async Task Sin_la_configuracion_del_servicio_termina_antes_de_escribir_nombrando_las_variables_y_sin_imprimir_valores_ni_rutas()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "babelhub-carga-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var plan = Path.Combine(carpeta, "plan.json");
            var sucursales = Path.Combine(carpeta, "sucursales.csv");
            await File.WriteAllTextAsync(plan, """{"version":1,"ediciones":[]}""");
            await File.WriteAllTextAsync(sucursales, "codigo;nombre;region\nlima;Lima;lima\n");
            var salida = new StringWriter();
            var error = new StringWriter();

            var codigo = await ModoCargarStock.Ejecutar(["cargar-stock", "--plan", plan, "--sucursales", sucursales], salida, error);

            var texto = error.ToString();
            Assert.Equal(1, codigo);
            Assert.Contains("configuración inválida", texto, StringComparison.Ordinal);
            Assert.Contains("BABELHUB__", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(carpeta, texto, StringComparison.Ordinal);
            Assert.Equal(string.Empty, salida.ToString());
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public async Task Un_archivo_ilegible_o_que_no_es_texto_se_rechaza_sin_nombrar_la_ruta()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "babelhub-carga-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var binario = Path.Combine(carpeta, "binario.json");
            await File.WriteAllBytesAsync(binario, [0xFF, 0xFE, 0xFD, 0x00]);
            var error = new StringWriter();

            var inexistente = await ModoCargarStock.Ejecutar(["cargar-stock", "--plan", Path.Combine(carpeta, "no-existe.json"), "--sucursales", binario], new StringWriter(), error);
            var invalido = await ModoCargarStock.Ejecutar(["cargar-stock", "--plan", binario, "--sucursales", binario], new StringWriter(), error);

            Assert.Equal((1, 1), (inexistente, invalido));
            Assert.Contains("archivo-ilegible", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("no-es-texto-valido", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(carpeta, error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    private sealed class AlmacenDeNoncesDePrueba : IAlmacenDeNoncesDeMarcas
    {
        public bool Consumir(string nonce) => true;
    }

    private sealed class AlmacenDeNoncesDeEcommerceDePrueba : BabelHub.Ecommerce.Api.Plataforma.IAlmacenDeNoncesDeMarcas
    {
        public bool Consumir(string nonce) => true;
    }
}
