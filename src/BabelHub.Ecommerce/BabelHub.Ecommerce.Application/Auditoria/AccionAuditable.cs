using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// El identificador de una acción del catálogo de auditoría, tal como lo nombran los demás módulos (D15): un valor tipado y no un texto suelto.
/// Lleva la marca «aislada» de su acción (la que va al almacén de Identidad, D17) como espejo del catálogo: <c>IAuditoria</c> e
/// <c>IRegistroDeSeguridad</c> rechazan una marca que no coincida con la del catálogo, de modo que una acción no se enruta por error al almacén
/// equivocado. Un valor por defecto (sin identificador) nunca se registra.
/// </summary>
public readonly partial record struct AccionAuditable
{
    public AccionAuditable(string identificador, bool aislada = false)
    {
        if (string.IsNullOrEmpty(identificador) || !Forma().IsMatch(identificador))
        {
            throw new ArgumentException("El identificador de una acción empieza con mayúscula y solo lleva letras y dígitos (3 a 64).", nameof(identificador));
        }

        Identificador = identificador;
        Aislada = aislada;
    }

    /// <summary>El identificador estable; nulo solo en el valor por defecto, que ningún puerto acepta.</summary>
    public string Identificador { get; }

    /// <summary>Si el registro va al almacén aislado de Identidad y no al del módulo <c>Auditoria</c>.</summary>
    public bool Aislada { get; }

    public bool EsValida => Identificador is not null;

    public override string ToString() => Identificador ?? string.Empty;

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]{2,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Forma();
}
