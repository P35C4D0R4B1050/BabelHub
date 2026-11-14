using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Tools.RestauracionCheck;

/// <summary>Los nombres de las tablas y columnas que revisa PLA-40. Por defecto siguen el modelo lógico de <c>docs/03-diseno-de-software/05-modelo-de-datos.md</c>.</summary>
/// <remarks>
/// Los planes de pedidos y de selección y reservas fijan los nombres definitivos; hasta entonces se pueden cambiar sin tocar el código con las
/// opciones <c>--tabla-*</c>. Solo se aceptan identificadores simples (con o sin esquema), que se citan: nunca texto libre.
/// </remarks>
public sealed record NombresDeTablas(string Pedidos, string LineasDePedido, string Ediciones, string Reservas)
{
    public static NombresDeTablas PorDefecto { get; } = new("pedidos.pedidos", "pedidos.pedido_lineas", "catalogo.ediciones", "seleccion.reservas");

    private static readonly Regex Identificador = new(@"^[a-z_][a-z0-9_]*(\.[a-z_][a-z0-9_]*)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool EsValido(string nombre) => Identificador.IsMatch(nombre);

    /// <summary>El identificador listo para ponerlo en una consulta: cada parte entre comillas dobles.</summary>
    public static string Citar(string nombre) => string.Join('.', nombre.Split('.').Select(parte => "\"" + parte + "\""));
}

/// <summary>Lo que pide la línea de comandos de <c>RestauracionCheck</c>.</summary>
public sealed record Opciones(
    string BaseEcommerce,
    string BaseInventario,
    DateTime Instante,
    Uri SaludEcommerce,
    Uri SaludInventario,
    NombresDeTablas Tablas)
{
    /// <summary>Las variables de entorno con las cadenas de conexión, para que no viajen en los argumentos del proceso.</summary>
    public const string VariableEcommerce = "RESTAURACION_CHECK_ECOMMERCE";

    public const string VariableInventario = "RESTAURACION_CHECK_INVENTARIO";

    /// <summary>Interpreta los argumentos; devuelve el error de uso (sin ningún valor secreto) o las opciones.</summary>
    public static (Opciones? Opciones, string? Error) Interpretar(string[] args, Func<string, string?> entorno)
    {
        string? ecommerce = entorno(VariableEcommerce);
        string? inventario = entorno(VariableInventario);
        DateTime? instante = null;
        Uri? saludEcommerce = null;
        Uri? saludInventario = null;
        var tablas = NombresDeTablas.PorDefecto;

        for (var i = 0; i < args.Length; i++)
        {
            string Siguiente()
            {
                i++;
                return i < args.Length ? args[i] : throw new ArgumentException($"falta el valor de {args[i - 1]}");
            }

            try
            {
                switch (args[i])
                {
                    case "--bases":
                        ecommerce = Siguiente();
                        inventario = Siguiente();
                        break;
                    case "--instante":
                        var texto = Siguiente();
                        if (!DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var valor))
                        {
                            return (null, "--instante debe ser una fecha y hora (por ejemplo 2026-11-14T02:30:00Z)");
                        }

                        instante = valor;
                        break;
                    case "--salud-ecommerce":
                        saludEcommerce = UriDeSalud(Siguiente(), "--salud-ecommerce");
                        break;
                    case "--salud-inventario":
                        saludInventario = UriDeSalud(Siguiente(), "--salud-inventario");
                        break;
                    case "--tabla-pedidos":
                        tablas = tablas with { Pedidos = Tabla(Siguiente(), "--tabla-pedidos") };
                        break;
                    case "--tabla-lineas":
                        tablas = tablas with { LineasDePedido = Tabla(Siguiente(), "--tabla-lineas") };
                        break;
                    case "--tabla-ediciones":
                        tablas = tablas with { Ediciones = Tabla(Siguiente(), "--tabla-ediciones") };
                        break;
                    case "--tabla-reservas":
                        tablas = tablas with { Reservas = Tabla(Siguiente(), "--tabla-reservas") };
                        break;
                    default:
                        return (null, $"opción desconocida: {args[i]}");
                }
            }
            catch (ArgumentException ex)
            {
                return (null, ex.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(ecommerce) || string.IsNullOrWhiteSpace(inventario))
        {
            return (null, $"faltan las dos cadenas de conexión: --bases <ecommerce> <inventario> o las variables {VariableEcommerce} y {VariableInventario} (preferible: no viajan en los argumentos)");
        }

        if (instante is null)
        {
            return (null, "falta --instante (el instante del respaldo restaurado, en UTC)");
        }

        if (saludEcommerce is null || saludInventario is null)
        {
            return (null, "faltan --salud-ecommerce y --salud-inventario (la comprobación de salud de los dos servicios es parte de PLA-36)");
        }

        return (new Opciones(ecommerce, inventario, instante.Value, saludEcommerce, saludInventario, tablas), null);
    }

    private static Uri UriDeSalud(string texto, string opcion) =>
        Uri.TryCreate(texto, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : throw new ArgumentException($"{opcion} debe ser una dirección http o https");

    private static string Tabla(string texto, string opcion) =>
        NombresDeTablas.EsValido(texto) ? texto : throw new ArgumentException($"{opcion} debe ser un identificador simple, con o sin esquema (por ejemplo pedidos.pedidos)");
}
