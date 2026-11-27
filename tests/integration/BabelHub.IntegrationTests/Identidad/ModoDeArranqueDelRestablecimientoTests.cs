using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>Escribe una variable de entorno del proceso, que otros anfitriones de prueba leen: por eso corre sola.</summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class ModoDeArranqueDelRestablecimientoTests
{
    [Theory]
    [Trait("Criterio", "AC-RSF-02")]
    [InlineData("Ensayo")]
    [InlineData("Restauracion")]
    public async Task ElModoDeArranque_SeLeeDeLaVariableDelServicio(string modo)
    {
        const string variable = "BABELHUB__Modo";
        var previo = Environment.GetEnvironmentVariable(variable);
        var (salida, error) = (new StringWriter(), new StringWriter());
        try
        {
            Environment.SetEnvironmentVariable(variable, modo);

            var codigo = await ModoRestablecerSegundoFactor.Ejecutar(["--correo", "titular@example.test"], salida, error, "Production");

            Assert.Equal(1, codigo);
            Assert.Contains("solo existe en Staging y Production, en el modo normal", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previo);
        }
    }
}
