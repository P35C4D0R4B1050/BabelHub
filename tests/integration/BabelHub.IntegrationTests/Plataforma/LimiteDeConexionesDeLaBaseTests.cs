using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Composicion;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// Hallazgo de K6-1: cada cuenta de la base tiene <c>CONNECTION LIMIT 20</c> (el migrador y el aprovisionamiento) y el grupo de Npgsql, sin tope propio, abre hasta 100: con más de 20 consultas
/// simultáneas la base rechazaba con «too many connections for role» y la Api respondía 500. El tope del grupo queda por debajo del límite de la cuenta, de modo que el exceso <b>espera</b> un turno
/// y no falla; un valor explícito de la cadena se respeta.
/// </summary>
public sealed class LimiteDeConexionesDeLaBaseTests
{
    private const string Cadena = "Host=localhost;Port=5432;Database=inventario;Username=cuenta;Password=pruebaSinValor1";

    private static int Tope(NpgsqlDataSource fuente) => new NpgsqlConnectionStringBuilder(fuente.ConnectionString).MaxPoolSize;

    [Fact]
    public void LaCuentaDelInventario_ConGrupoSinTope_QuedaPorDebajoDelLimiteDeLaCuenta()
    {
        using var fuente = BasesDelInventario.Abrir(Cadena);

        Assert.InRange(Tope(fuente), 1, 18);
    }

    [Fact]
    public void LaCuentaDelEcommerce_ConGrupoSinTope_QuedaPorDebajoDelLimiteDeLaCuenta()
    {
        using var fuente = BasesDelEcommerce.Abrir(Cadena.Replace("inventario", "ecommerce", StringComparison.Ordinal));

        Assert.InRange(Tope(fuente), 1, 18);
    }

    [Fact]
    public void UnTopeExplicitoEnLaCadena_SeRespeta()
    {
        using var fuente = BasesDelInventario.Abrir(Cadena + ";Maximum Pool Size=7");

        Assert.Equal(7, Tope(fuente));
    }
}
