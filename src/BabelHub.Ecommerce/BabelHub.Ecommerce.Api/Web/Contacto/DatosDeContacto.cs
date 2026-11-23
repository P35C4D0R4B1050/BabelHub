using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.Ecommerce.Api.Web.Contacto;

/// <summary>
/// El contacto del comercio que muestran el pie de toda página y <c>/contacto</c> (COM-01 a COM-06): teléfono, correo y dirección de la <b>configuración del entorno</b>
/// (<c>BABELHUB__Contacto__Telefono</c>, <c>…Correo</c> y <c>…Direccion</c>), nunca del repositorio. Solo la fábrica <see cref="Leer"/> lo crea, ya validado: los enlaces
/// <c>tel:</c> y <c>mailto:</c> son un prefijo fijo más un valor que solo contiene los caracteres permitidos (ASCII; sin <c>?</c>, <c>&amp;</c>, <c>%</c>, <c>,</c> ni espacios).
/// Los mensajes de error nombran la variable y nunca su valor. Una misma instancia, inmutable, sirve a todas las solicitudes.
/// </summary>
public sealed partial class DatosDeContacto
{
    /// <summary>La configuración que se lee: la ruta de cada dato (<c>BABELHUB__Contacto__…</c>).</summary>
    public const string RutaDelTelefono = "Contacto:Telefono";

    public const string RutaDelCorreo = "Contacto:Correo";

    public const string RutaDeLaDireccion = "Contacto:Direccion";

    private const int MaximoDeCaracteresDelTelefono = 30;
    private const int MinimoDeDigitos = 7;
    private const int MaximoDeDigitos = 15;
    private const int MaximoDelCorreo = 254;
    private const int MinimoDeLaDireccion = 10;
    private const int MaximoDeLaDireccion = 200;

    private DatosDeContacto(string telefono, string enlaceDeLlamada, string correo, string direccion, bool esDeEjemplo)
    {
        Telefono = telefono;
        EnlaceDeLlamada = enlaceDeLlamada;
        Correo = correo;
        EnlaceDeCorreo = "mailto:" + correo;
        Direccion = direccion;
        EsDeEjemplo = esDeEjemplo;
    }

    public string Telefono { get; }

    /// <summary><c>tel:</c> con solo los dígitos y un <c>+</c> inicial si se configuró; a un celular de 9 dígitos que empieza con 9 y no lleva <c>+</c> se le antepone <c>+51</c>.</summary>
    public string EnlaceDeLlamada { get; }

    public string Correo { get; }

    public string EnlaceDeCorreo { get; }

    public string Direccion { get; }

    /// <summary>Verdadero si alguno de los tres datos es el valor ficticio de desarrollo o de prueba (COM-05).</summary>
    public bool EsDeEjemplo { get; }

    /// <summary>Los valores ficticios claramente falsos de desarrollo y de prueba (COM-05): nunca datos reales.</summary>
    internal static DatosDeContacto DeEjemplo { get; } = Crear("999 000 000", "contacto@ejemplo.test", "Jr. Ejemplo 123, Ayacucho (dato de ejemplo)", esDeEjemplo: true);

    /// <summary>
    /// Lee y valida los tres datos. Un dato ausente, vacío o con solo espacios es «no configurado»: con <paramref name="permitirEjemplo"/> (solo <c>Development</c> y <c>Test</c>) se usa el
    /// ficticio y sin él es un problema; uno presente que incumple el formato es un problema en cualquier entorno. Los problemas se agregan al <paramref name="lector"/> (nombran la variable,
    /// nunca el valor) y entonces devuelve <see langword="null"/>.
    /// </summary>
    internal static DatosDeContacto? Leer(LectorDeSecretos lector, bool permitirEjemplo)
    {
        ArgumentNullException.ThrowIfNull(lector);
        var telefono = Leido(lector, RutaDelTelefono, permitirEjemplo, DeEjemplo.Telefono, TelefonoValido);
        var correo = Leido(lector, RutaDelCorreo, permitirEjemplo, DeEjemplo.Correo, CorreoValido);
        var direccion = Leido(lector, RutaDeLaDireccion, permitirEjemplo, DeEjemplo.Direccion, DireccionValida);
        if (telefono is null || correo is null || direccion is null)
        {
            return null;
        }

        var esDeEjemplo = telefono == DeEjemplo.Telefono || correo == DeEjemplo.Correo || direccion == DeEjemplo.Direccion;
        return Crear(telefono, correo, direccion, esDeEjemplo);
    }

    private static DatosDeContacto Crear(string telefono, string correo, string direccion, bool esDeEjemplo) =>
        new(telefono, EnlaceDeLlamadaDe(telefono), correo, direccion, esDeEjemplo);

    private static string? Leido(LectorDeSecretos lector, string ruta, bool permitirEjemplo, string ficticio, Func<string, bool> valido)
    {
        // Solo se recortan los espacios U+0020 de los extremos; el valor se muestra ya recortado.
        var valor = lector.Opcional(ruta)?.Trim(' ');
        if (string.IsNullOrEmpty(valor))
        {
            if (permitirEjemplo)
            {
                return ficticio;
            }

            lector.Problema(ruta, "falta");
            return null;
        }

        if (!valido(valor))
        {
            lector.Problema(ruta, "no tiene un formato válido (ver la spec web-informacion-del-comercio §5)");
            return null;
        }

        return valor;
    }

    /// <summary>De 7 a 15 dígitos <c>0-9</c>, hasta 30 caracteres y solo espacios U+0020, guiones, paréntesis y un <c>+</c> inicial.</summary>
    internal static bool TelefonoValido(string valor)
    {
        if (valor.Length is 0 or > MaximoDeCaracteresDelTelefono)
        {
            return false;
        }

        var digitos = 0;
        for (var i = 0; i < valor.Length; i++)
        {
            var c = valor[i];
            if (c is >= '0' and <= '9')
            {
                digitos++;
            }
            else if (!(c is ' ' or '-' or '(' or ')' || (c == '+' && i == 0)))
            {
                return false;
            }
        }

        return digitos is >= MinimoDeDigitos and <= MaximoDeDigitos;
    }

    /// <summary>La forma «parte local, arroba, dominio, punto y sufijo»: ASCII, de hasta 254 caracteres; sin <c>?</c>, <c>&amp;</c>, <c>%</c>, <c>,</c>, espacios ni letras fuera de A-Z.</summary>
    internal static bool CorreoValido(string valor) => valor.Length <= MaximoDelCorreo && ExpresionDelCorreo().IsMatch(valor);

    /// <summary>De 10 a 200 caracteres (unidades UTF-16), sin caracteres de control, de formato ni separadores de línea o de párrafo, y sin <c>&lt;</c> <c>&gt;</c> <c>"</c> <c>'</c> <c>&amp;</c> ni la tilde grave.</summary>
    internal static bool DireccionValida(string valor)
    {
        if (valor.Length is < MinimoDeLaDireccion or > MaximoDeLaDireccion)
        {
            return false;
        }

        foreach (var c in valor)
        {
            if (c is '<' or '>' or '"' or '\'' or '&' or '`')
            {
                return false;
            }

            if (char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }

    private static string EnlaceDeLlamadaDe(string telefono)
    {
        var digitos = new string([.. telefono.Where(c => c is >= '0' and <= '9')]);
        if (telefono.StartsWith('+'))
        {
            return "tel:+" + digitos;
        }

        return digitos.Length == 9 && digitos[0] == '9' ? "tel:+51" + digitos : "tel:" + digitos;
    }

    // `\z` y no `$` (que acepta un `\n` final), sin IgnoreCase (el signo Kelvin coincidiría con `k`) y con clases A-Za-z y 0-9 (nunca `\d` ni `\w`, que admiten Unicode).
    [GeneratedRegex(@"^[A-Za-z0-9+_-]+(\.[A-Za-z0-9+_-]+)*@([A-Za-z0-9-]+\.)+[A-Za-z]{2,}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ExpresionDelCorreo();
}
