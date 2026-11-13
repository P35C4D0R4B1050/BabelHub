namespace BabelHub.Ecommerce.Application.Plataforma;

public enum PropositoDeCodigo
{
    VerificacionDeCuenta,
    RecuperacionDeAcceso,
}

/// <summary>
/// Correo que lleva un código de un solo uso. Solo se construye <see cref="ParaElTitular"/>, con la dirección registrada de
/// la cuenta a la que pertenece el código: no existe forma de dirigirlo a otra dirección (SEG-28). Es el único mensaje que
/// puede llevar un código; el código es un <see cref="ValorSecreto"/> y no aparece en su representación en texto.
/// </summary>
public sealed class CorreoDeCodigo
{
    private CorreoDeCodigo(string destinatario, PropositoDeCodigo proposito, ValorSecreto codigo)
    {
        Destinatario = destinatario;
        Proposito = proposito;
        Codigo = codigo;
    }

    public string Destinatario { get; }

    public PropositoDeCodigo Proposito { get; }

    public ValorSecreto Codigo { get; }

    /// <summary>
    /// Un correo para el titular del código. <paramref name="correoDelTitular"/> sale de la cuenta, nunca de la solicitud; si
    /// no es una sola dirección válida (o intenta inyectar cabeceras o varios destinatarios) se rechaza.
    /// </summary>
    public static CorreoDeCodigo ParaElTitular(string correoDelTitular, PropositoDeCodigo proposito, ValorSecreto codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);

        if (!EsUnaSolaDireccion(correoDelTitular))
        {
            throw new ArgumentException("El código se envía a una única dirección válida del titular.", nameof(correoDelTitular));
        }

        return new CorreoDeCodigo(correoDelTitular.Trim(), proposito, codigo);
    }

    public override string ToString() => $"Código {Proposito} para {Destinatario}: {ValorSecreto.Redactado}";

    private static bool EsUnaSolaDireccion(string? correo)
    {
        if (string.IsNullOrWhiteSpace(correo))
        {
            return false;
        }

        var texto = correo.Trim();
        if (texto.Length > 254 || texto.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>' or '"' or '(' or ')'))
        {
            return false;
        }

        var arroba = texto.IndexOf('@', StringComparison.Ordinal);
        return arroba > 0
            && arroba == texto.LastIndexOf('@')
            && arroba < texto.Length - 3
            && texto[(arroba + 1)..].Contains('.', StringComparison.Ordinal)
            && !texto.EndsWith('.');
    }
}
