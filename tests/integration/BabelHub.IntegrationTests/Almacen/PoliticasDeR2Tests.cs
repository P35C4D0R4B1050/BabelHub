namespace BabelHub.IntegrationTests.Almacen;

/// <summary>
/// PLA-16, PLA-18 y PLA-35 (D26): la definición de los respaldos en Cloudflare R2. La credencial de la VM de datos solo alcanza los
/// contenedores de respaldos y no administra nada; cada contenedor tiene bloqueo de retención por al menos el plazo de PLA-18 y retiro por el
/// propio almacén; los libros y el registro de supresiones no comparten credenciales ni contenedores con los respaldos. Que R2 aplique el
/// bloqueo y el alcance de cada token se comprueba en la cuenta real (constancia T47).
/// </summary>
public sealed class PoliticasDeR2Tests
{
    private static readonly PoliticasDeR2 R2 = PoliticasDeR2.Cargar();
    private static readonly PoliticasDelAlmacen Oracle = PoliticasDelAlmacen.Cargar();

    private static readonly string[] Respaldos = ["respaldos-diarios", "respaldos-semanales", "respaldos-mensuales", "respaldos-wal"];

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void CadaClaseTieneSuContenedor_ConBloqueoYRetiroDeLosPlazosDeLaSpec()
    {
        Assert.Equal(Respaldos, R2.Contenedores.Select(c => c.Nombre));
        Assert.Equal([7, 28, 92, 7], R2.Contenedores.Select(c => c.BloqueoDias));
        Assert.All(R2.Contenedores, c => Assert.Equal(c.BloqueoDias, c.ExpiraDias));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void LaCredencialDeLaVmDeDatos_SoloAlcanzaLosContenedoresDeRespaldos()
    {
        var delEscritor = R2.Concesiones.Where(g => g.Credencial == "respaldos-escritor").ToList();

        Assert.Equal(Respaldos, delEscritor.Select(g => g.Contenedor));
        Assert.All(delEscritor, g => Assert.Equal("lectura-escritura", g.Permiso));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void SoloElEscritorEscribe_YNingunaCredencialAdministra()
    {
        Assert.Equal(["respaldos-escritor", "respaldos-lector", "respaldos-vigilante"], R2.Concesiones.Select(g => g.Credencial).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.All(R2.Concesiones.Where(g => g.Credencial != "respaldos-escritor"), g => Assert.Equal("lectura", g.Permiso));
        Assert.All(R2.Concesiones, g => Assert.Contains(g.Permiso, new[] { "lectura", "lectura-escritura" }));
        Assert.All(R2.Concesiones, g => Assert.Contains(g.Contenedor, Respaldos));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-35")]
    public void LosRespaldosYElRestoDelAlmacen_NoCompartenNiContenedoresNiCredenciales()
    {
        Assert.Empty(Oracle.Contenedores.Select(c => c.Nombre).Intersect(R2.Contenedores.Select(c => c.Nombre)));
        Assert.Empty(Oracle.Credenciales.Intersect(R2.Concesiones.Select(g => g.Credencial)));
        Assert.DoesNotContain(Oracle.Contenedores, c => c.Nombre.StartsWith("respaldos-", StringComparison.Ordinal));
    }
}
