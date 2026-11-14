namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// Cada servicio tiene su copia de los adaptadores de la plataforma y del Dominio de Auditoría (no comparten código, regla 5). Esta prueba de contrato
/// comprueba que las copias siguen siendo la misma salvo el nombre del servicio, para que una corrección no llegue a una sola.
/// </summary>
public sealed class CopiasDeAdaptadoresTests
{
    public static TheoryData<string> Archivos => new(
        "Infrastructure/Plataforma/AdaptadoresBase.cs",
        // UnidadDeTrabajoPostgres.cs ya no es copia: la del Inventario fija el alcance de datos y ofrece la lectura coherente (inventario-fuentes T03 y T04).
        "Application/Plataforma/ConexionDeLaUnidadDeTrabajo.cs",
        "Application/Plataforma/PuertosBase.cs",
        "Application/Plataforma/FallaSegura.cs",
        "Application/Plataforma/DependenciasExternas.cs",
        "Application/Plataforma/DatosProhibidos.cs",
        "Application/Plataforma/AlcancesDeServicio.cs",
        "Application/Plataforma/EntradasDeIntegracion.cs",
        "Application/Plataforma/CasoDeUso.cs",
        "Application/Plataforma/ConstructorDeValidacion.cs",
        "Application/Plataforma/TrabajosDeFondo.cs",
        "Infrastructure/Plataforma/TrabajoDeUmbralesDeAlerta.cs",
        "Infrastructure/Plataforma/MotorDeLimitesLocal.cs",
        "Infrastructure/Plataforma/AlmacenDeEntradasProcesadas.cs",
        "Infrastructure/Plataforma/VerificadorDeFirmaHmac.cs",
        "Infrastructure/Plataforma/ClientesHttpDeServicio.cs",
        "Infrastructure/Plataforma/ConfiguracionDeSeguridad.cs",
        "Infrastructure/Plataforma/RegistroRedactado.cs",
        "Application/Plataforma/MotorDeLimitesNoDisponible.cs",
        "Api/Plataforma/ManejoDeErrores.cs",
        "Api/Plataforma/LimitesDeUsoHttp.cs",
        "Api/Plataforma/CredencialDeServicio.cs",
        "Application/Plataforma/PasosDeRestauracion.cs",
        "Infrastructure/Plataforma/TrabajosDeFondoPostgres.cs",
        "Infrastructure/Plataforma/LatidoDeLaBase.cs",
        "Api/Plataforma/ModoDeArranque.cs",
        "Api/Plataforma/SaludHttp.cs",
        "Domain/Auditoria/DetalleSeguro.cs",
        "Domain/Auditoria/CatalogoDeAcciones.cs",
        "Domain/Auditoria/RegistroDeAuditoria.cs",
        "Domain/Auditoria/PoliticaDeAnonimizacion.cs",
        "Domain/Auditoria/AlertaDeSeguridad.cs",
        "Domain/Auditoria/AcumuladoDeLimite.cs");

    [Theory]
    [MemberData(nameof(Archivos))]
    public void LaCopiaDelInventario_EsLaMismaQueLaDelECommerceSalvoElNombreDelServicio(string ruta)
    {
        var raiz = BuscarRaiz();
        var ecommerce = Leer(raiz, "Ecommerce", ruta);
        var inventario = Leer(raiz, "Inventario", ruta);

        Assert.Equal(Normalizar(ecommerce), Normalizar(inventario));
    }

    private static string Leer(string raiz, string servicio, string ruta)
    {
        var capa = ruta[..ruta.IndexOf('/', StringComparison.Ordinal)];
        var resto = ruta[(ruta.IndexOf('/', StringComparison.Ordinal) + 1)..];
        return File.ReadAllText(Path.Combine(raiz, "src", $"BabelHub.{servicio}", $"BabelHub.{servicio}.{capa}", resto.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string Normalizar(string texto) =>
        texto.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("BabelHub.Ecommerce", "BabelHub.Servicio", StringComparison.Ordinal)
            .Replace("BabelHub.Inventario", "BabelHub.Servicio", StringComparison.Ordinal);

    private static string BuscarRaiz()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "AGENTS.md")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
