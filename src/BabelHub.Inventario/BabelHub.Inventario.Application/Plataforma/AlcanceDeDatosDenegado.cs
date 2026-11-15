namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// La política de seguridad por filas de la base rechazó una escritura por estar fuera del alcance del actor. La Aplicación que llama la
/// traduce a <c>INV-PROHIBIDO</c> con el evento de acceso denegado (no a «sin stock»); nunca lleva datos de la fila.
/// </summary>
public sealed class AlcanceDeDatosDenegadoException : Exception
{
    public AlcanceDeDatosDenegadoException()
        : base("La política de filas rechazó el acceso: la fila está fuera del alcance del actor.")
    {
    }

    public AlcanceDeDatosDenegadoException(Exception interna)
        : base("La política de filas rechazó el acceso: la fila está fuera del alcance del actor.", interna)
    {
    }
}
