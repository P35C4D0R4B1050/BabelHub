namespace BabelHub.Ecommerce.Domain.Pedidos;

/// <summary>
/// El número de un pedido (PED-01): 10 caracteres aleatorios y únicos del alfabeto sin caracteres ambiguos (sin 0, 1, I ni O) que el cliente puede leer por teléfono. Es <b>aleatorio</b>: no revela el volumen de pedidos ni se puede adivinar el de otro cliente. El generador criptográfico
/// vive en la Infraestructura; aquí solo se define el formato y cómo se arma a partir de una fuente de números aleatorios.
/// </summary>
public sealed record NumeroDePedido
{
    public const int Longitud = 10;

    public const string Alfabeto = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private NumeroDePedido(string valor) => Valor = valor;

    public string Valor { get; }

    /// <summary>El número si tiene exactamente 10 caracteres del alfabeto; si no, <see langword="null"/>.</summary>
    public static NumeroDePedido? Crear(string? valor) =>
        valor is { Length: Longitud } && valor.All(c => Alfabeto.Contains(c, StringComparison.Ordinal)) ? new NumeroDePedido(valor) : null;

    /// <summary>Arma un número con una fuente de enteros aleatorios (<c>siguiente(maximoExclusivo)</c>); la fuente de producción es criptográfica.</summary>
    public static NumeroDePedido Generar(Func<int, int> siguiente)
    {
        ArgumentNullException.ThrowIfNull(siguiente);
        var caracteres = new char[Longitud];
        for (var i = 0; i < Longitud; i++)
        {
            var indice = siguiente(Alfabeto.Length);
            if (indice < 0 || indice >= Alfabeto.Length)
            {
                throw new InvalidOperationException("La fuente de números aleatorios devolvió un valor fuera del rango pedido.");
            }

            caracteres[i] = Alfabeto[indice];
        }

        return new NumeroDePedido(new string(caracteres));
    }

    public override string ToString() => Valor;
}
