namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Huella del origen de una solicitud (SEG-25): un resumen calculado con una clave secreta del entorno, con el
/// identificador de versión de la clave para poder rotarla sin reiniciar los contadores. La dirección completa nunca es
/// parte de este valor. Solo la construye la Infraestructura (<c>ICalculadorDeHuella</c>); el origen que no se puede
/// determinar usa la huella fija <see cref="Desconocido"/> (SEG-23).
/// </summary>
public sealed record HuellaDeOrigen
{
    private const string ValorDesconocido = "desconocido";
    private const string VersionDesconocida = "0";

    private HuellaDeOrigen(string valor, string versionDeClave)
    {
        Valor = valor;
        VersionDeClave = versionDeClave;
    }

    /// <summary>El origen que no se pudo determinar: todos esos orígenes comparten un mismo contador.</summary>
    public static HuellaDeOrigen Desconocido { get; } = new(ValorDesconocido, VersionDesconocida);

    public string Valor { get; }

    public string VersionDeClave { get; }

    public bool EsDesconocido => ReferenceEquals(this, Desconocido) || (Valor == ValorDesconocido && VersionDeClave == VersionDesconocida);

    internal static HuellaDeOrigen Crear(string valor, string versionDeClave) =>
        new(ValorDeHuella.ValidarValor(valor), ValorDeHuella.ValidarVersion(versionDeClave));

    public override string ToString() => $"{VersionDeClave}:{Valor}";
}
