using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>Los códigos de texto con que se guardan los enumerados de Pedidos (los de los <c>CHECK</c> de <see cref="DefinicionDePedidos"/>): en minúsculas y con guiones bajos, o los códigos cerrados del dominio.</summary>
internal static partial class CodigosDePedidos
{
    public static string De<T>(T valor) where T : struct, Enum => Separar().Replace(valor.ToString(), "$1_$2").ToLowerInvariant();

    public static T A<T>(string codigo) where T : struct, Enum
    {
        foreach (var valor in Enum.GetValues<T>())
        {
            if (De(valor) == codigo)
            {
                return valor;
            }
        }

        throw new InvalidOperationException($"Un código guardado de {typeof(T).Name} desconocido.");
    }

    public static string Motivo(MotivoDeAnulacion motivo) => MotivosDeAnulacion.Codigo(motivo);

    public static MotivoDeAnulacion MotivoDe(string codigo) =>
        MotivosDeAnulacion.TryParse(codigo, out var motivo) ? motivo : throw new InvalidOperationException("Un motivo de anulación guardado desconocido.");

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex Separar();
}
