namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Huella opaca del sujeto de un límite que no es un origen (por ejemplo, el número de WhatsApp de un cliente en el tope de
/// avisos, NOT-14). Solo la construye la Infraestructura (<c>ICalculadorDeHuella</c>) con la clave secreta del entorno, de
/// modo que ningún módulo pueda guardar en los contadores el dato original.
/// </summary>
public sealed record HuellaDeSujeto
{
    private HuellaDeSujeto(string valor, string versionDeClave)
    {
        Valor = valor;
        VersionDeClave = versionDeClave;
    }

    public string Valor { get; }

    public string VersionDeClave { get; }

    internal static HuellaDeSujeto Crear(string valor, string versionDeClave) =>
        new(ValorDeHuella.ValidarValor(valor), ValorDeHuella.ValidarVersion(versionDeClave));

    public override string ToString() => $"{VersionDeClave}:{Valor}";
}
