namespace BabelHub.Ecommerce.Domain.Web.Indice;

/// <summary>El estado de una dirección recibida del visitante (IND-07): válida, vacía (equivale a pedir una al azar, spec §6) o no válida.</summary>
public enum EstadoDeDireccion
{
    Valida,
    Vacia,
    NoValida,
}

/// <summary>
/// El resultado de evaluar una dirección. Solo la dirección <b>válida</b> se conserva: un texto no válido <b>nunca se guarda ni se repite</b> (ni en el resultado ni en su representación como texto), para que ninguna respuesta,
/// enlace ni registro del índice lo devuelva (CTL-35, SEG-10).
/// </summary>
public readonly record struct ResultadoDeDireccion
{
    internal ResultadoDeDireccion(EstadoDeDireccion estado, string? direccion)
    {
        Estado = estado;
        Direccion = direccion;
    }

    public EstadoDeDireccion Estado { get; }

    /// <summary>La dirección canónica; nula si el estado no es <see cref="EstadoDeDireccion.Valida"/>.</summary>
    public string? Direccion { get; }

    public override string ToString() => Estado == EstadoDeDireccion.Valida ? $"Valida({Direccion})" : Estado.ToString();
}

/// <summary>
/// La dirección del índice infinito (IND-01, IND-07; D04): de 1 a 64 caracteres de <c>a-z</c> sin tilde, <c>0-9</c> y <c>-</c>, por lista permitida y sin normalizar nada (una mayúscula, una tilde, un espacio o una barra no son válidos).
/// Solo tipos del lenguaje: sin cultura, reloj ni estado.
/// </summary>
public static class DireccionDelIndice
{
    public const int LongitudMaxima = 64;

    public static ResultadoDeDireccion Evaluar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return new ResultadoDeDireccion(EstadoDeDireccion.Vacia, null);
        }

        return EsValida(texto) ? new ResultadoDeDireccion(EstadoDeDireccion.Valida, texto) : new ResultadoDeDireccion(EstadoDeDireccion.NoValida, null);
    }

    /// <summary>Verdadero si el texto cumple la forma de una dirección (1 a 64 caracteres de la lista permitida).</summary>
    public static bool EsValida(string texto)
    {
        if (texto.Length is < 1 or > LongitudMaxima)
        {
            return false;
        }

        foreach (var c in texto)
        {
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-'))
            {
                return false;
            }
        }

        return true;
    }
}
