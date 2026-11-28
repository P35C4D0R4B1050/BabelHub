namespace BabelHub.IntegrationTests;

/// <summary>
/// Categorías de las pruebas de integración (<c>[Trait("Categoria", …)]</c>) para elegir qué corre en cada momento sin quitar ninguna.
/// Por ejemplo, <c>--filter "Categoria!=Lanzamiento&amp;Categoria!=Guiones"</c> deja lo que no depende de construir imágenes ni de los
/// guiones de operación. Ninguna sale de la corrida completa.
/// </summary>
internal static class Categorias
{
    /// <summary>
    /// Construye imágenes (también arm64 emulada), levanta Compose o Docker de verdad, consulta registros remotos, compila otros proyectos o
    /// hace una restauración completa: se corre antes de lanzar y al tocar <c>infra/</c>, <c>scripts/</c> o un Dockerfile.
    /// </summary>
    public const string Lanzamiento = "Lanzamiento";

    /// <summary>
    /// Ejecuta los guiones de <c>scripts/</c> (despliegue, almacén, respaldos, VM-AUX) con decenas de procesos o contenedores propios; sus
    /// clases corren en serie y concentran la mayor parte del tiempo. Se corre al tocar <c>scripts/</c> o <c>infra/</c> y en un trabajo aparte.
    /// </summary>
    public const string Guiones = "Guiones";
}
