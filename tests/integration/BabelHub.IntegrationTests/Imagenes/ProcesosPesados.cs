namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// Las pruebas que levantan contenedores propios o lanzan decenas de procesos (<c>docker compose</c>, <c>bash</c>) corren solas: en paralelo
/// con otras quitan CPU a las pruebas que miden ventanas de tiempo reales (límites deslizantes, plazos de reintento) y las hacen fallar de
/// forma intermitente.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ProcesosPesadosCollection
{
    public const string Nombre = "Procesos pesados";
}
