using System.Text.RegularExpressions;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests.Contratos;

/// <summary>
/// El documento <c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c> describe exactamente las operaciones congeladas en
/// <c>BabelHub.Shared</c>: ni una más ni una menos, con su método y ruta, alcance, token, clave de idempotencia, éxito, DTOs y errores.
/// </summary>
public sealed partial class DocumentoDeContratosTests
{
    private static readonly string Ruta = Path.Combine(Proyectos.Raiz, "docs", "03-diseno-de-software", "08-contratos-entre-servicios-v1.md");

    private static IReadOnlyDictionary<string, string> Filas()
    {
        Assert.True(File.Exists(Ruta), "Falta docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md.");
        var filas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var linea in File.ReadAllLines(Ruta))
        {
            var m = FilaDeOperacion().Match(linea);
            if (m.Success)
            {
                Assert.True(filas.TryAdd(m.Groups[1].Value, linea), $"La operación {m.Groups[1].Value} está repetida en el documento.");
            }
        }

        return filas;
    }

    private static string Nombre(Type tipo) =>
        tipo.IsGenericType ? $"{tipo.Name[..tipo.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", tipo.GetGenericArguments().Select(Nombre))}>" : tipo.Name;

    private static string SinPrefijo(string ruta) =>
        ruta.StartsWith(RutasDeServicio.Inventario, StringComparison.Ordinal) ? ruta[RutasDeServicio.Inventario.Length..] : ruta[RutasDeServicio.Ecommerce.Length..];

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public void El_documento_lista_las_mismas_operaciones_que_el_catalogo()
    {
        var enDocumento = Filas().Keys.Order(StringComparer.Ordinal).ToList();
        var enCatalogo = CatalogoDeOperaciones.V1.Select(o => o.Nombre).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(enCatalogo, enDocumento);
    }

    [Fact]
    public void Cada_fila_del_documento_repite_el_contrato_de_su_operacion()
    {
        var filas = Filas();
        foreach (var operacion in CatalogoDeOperaciones.V1)
        {
            var celdas = filas[operacion.Nombre].Split('|', StringSplitOptions.TrimEntries).Skip(1).ToArray();
            Assert.True(celdas.Length >= 9, $"{operacion.Nombre}: la fila debe tener las nueve columnas.");

            Assert.Equal($"`{operacion.Metodo} {SinPrefijo(operacion.Ruta)}`", celdas[1]);
            Assert.Equal($"`{operacion.Alcance}`", celdas[2]);
            Assert.Equal(operacion.RequiereTokenDeUsuario ? "Sí" : "No", celdas[3]);
            Assert.Equal(operacion.RequiereClaveDeIdempotencia ? "Sí" : "No", celdas[4]);
            Assert.Equal(operacion.CodigoDeExito.ToString(System.Globalization.CultureInfo.InvariantCulture), celdas[5]);
            Assert.Equal(operacion.Solicitud is null ? "—" : $"`{Nombre(operacion.Solicitud)}`", celdas[6]);
            Assert.Equal(operacion.Respuesta is null ? "—" : $"`{Nombre(operacion.Respuesta)}`", celdas[7]);

            var errores = celdas[8].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim('`')).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(operacion.Errores.Order(StringComparer.Ordinal).ToList(), errores);
        }
    }

    [Fact]
    public void El_documento_declara_las_decisiones_y_lo_no_congelado()
    {
        var texto = File.ReadAllText(Ruta);

        Assert.Contains(". Decisiones de contrato tomadas en ECO-0", texto, StringComparison.Ordinal);
        Assert.Contains(". Lo que no se congela", texto, StringComparison.Ordinal);
        Assert.Contains("v1", texto, StringComparison.Ordinal);
    }

    [GeneratedRegex("^\\| `((?:inventario|ecommerce)\\.[a-z0-9.-]+)` \\|")]
    private static partial Regex FilaDeOperacion();
}
