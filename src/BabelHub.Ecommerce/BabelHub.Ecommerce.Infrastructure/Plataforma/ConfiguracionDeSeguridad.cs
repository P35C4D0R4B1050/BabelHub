using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>La configuración de secretos no es válida; el mensaje nombra las variables de entorno con problemas, nunca sus valores.</summary>
public sealed class ConfiguracionDeSecretosInvalidaException : Exception
{
    public ConfiguracionDeSecretosInvalidaException(IReadOnlyList<string> problemas)
        : base("La configuración de secretos no es válida: " + string.Join("; ", problemas))
    {
        Problemas = problemas;
    }

    public ConfiguracionDeSecretosInvalidaException()
        : this(["sin detalle"])
    {
    }

    public ConfiguracionDeSecretosInvalidaException(string message)
        : this([message])
    {
    }

    public ConfiguracionDeSecretosInvalidaException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problemas = [message];
    }

    public IReadOnlyList<string> Problemas { get; }
}

/// <summary>
/// Lee secretos de la configuración (variables de entorno <c>BABELHUB__…</c>) y los comprueba: obligatorios presentes, de al
/// menos 32 bytes, con variedad de caracteres y distintos entre sí (SEG-11, D10). Junta todos los problemas antes de fallar y
/// los nombra por la variable, nunca por su valor.
/// </summary>
public sealed class LectorDeSecretos(IConfiguration configuracion)
{
    public const int LongitudMinima = 32;
    private const int CaracteresDistintosMinimos = 8;

    private readonly List<string> _problemas = [];
    private readonly Dictionary<string, string> _porValor = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Problemas => _problemas;

    /// <summary>El nombre de la variable de entorno que corresponde a una ruta de configuración.</summary>
    public static string Variable(string ruta) => "BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal);

    /// <summary>Un secreto obligatorio: si falta, queda como problema y devuelve <see langword="null"/>.</summary>
    public string? Obligatorio(string ruta)
    {
        var valor = configuracion[ruta];
        if (string.IsNullOrWhiteSpace(valor))
        {
            _problemas.Add($"{Variable(ruta)}: falta");
            return null;
        }

        return valor;
    }

    public string? Opcional(string ruta) =>
        string.IsNullOrWhiteSpace(configuracion[ruta]) ? null : configuracion[ruta];

    /// <summary>Comprueba que el secreto sea fuerte y no se repita en otra variable.</summary>
    public void Comprobar(string ruta, string valor, bool esTexto = true)
    {
        if (esTexto && Encoding.UTF8.GetByteCount(valor) < LongitudMinima)
        {
            _problemas.Add($"{Variable(ruta)}: es débil (menos de {LongitudMinima} bytes)");
        }
        else if (valor.Distinct().Count() < CaracteresDistintosMinimos)
        {
            _problemas.Add($"{Variable(ruta)}: es débil (casi no varía)");
        }

        if (_porValor.TryGetValue(valor, out var otra))
        {
            _problemas.Add($"{Variable(ruta)}: se repite en {Variable(otra)}");
        }
        else
        {
            _porValor[valor] = ruta;
        }
    }

    public void Problema(string ruta, string detalle) => _problemas.Add($"{Variable(ruta)}: {detalle}");

    public void ExigirValida()
    {
        if (_problemas.Count > 0)
        {
            throw new ConfiguracionDeSecretosInvalidaException(_problemas.ToList());
        }
    }
}

public enum ResultadoDeCredencialDeEntrada
{
    Invalida,
    Actual,

    /// <summary>La de la rotación en curso: se acepta hasta 24 horas, pero su uso se registra y se alerta (SEG-15).</summary>
    Anterior,
}

/// <summary>
/// Credenciales de servicio que este servicio acepta, por alcance: la actual y, durante una rotación en dos pasos, la anterior.
/// La comparación es en tiempo constante y recorre siempre las dos.
/// </summary>
public sealed class OpcionesDeCredencialesDeEntrada
{
    private readonly Dictionary<AlcanceDeServicio, (byte[] Actual, byte[]? Anterior)> _credenciales;

    public OpcionesDeCredencialesDeEntrada(IEnumerable<KeyValuePair<AlcanceDeServicio, (string Actual, string? Anterior)>> credenciales)
    {
        ArgumentNullException.ThrowIfNull(credenciales);
        _credenciales = credenciales.ToDictionary(
            p => p.Key,
            p => (Encoding.UTF8.GetBytes(p.Value.Actual), p.Value.Anterior is null ? null : Encoding.UTF8.GetBytes(p.Value.Anterior)));
    }

    /// <summary>
    /// A qué alcance pertenece la credencial presentada, si a alguno, y si es la anterior de una rotación. Compara contra todas sin cortar al
    /// primer acierto, para que el tiempo no diga qué alcance existe.
    /// </summary>
    public (AlcanceDeServicio? Alcance, bool EsAnterior) Identificar(string? presentada)
    {
        if (string.IsNullOrEmpty(presentada))
        {
            return (null, false);
        }

        var bytes = Encoding.UTF8.GetBytes(presentada);
        AlcanceDeServicio? encontrado = null;
        var esAnterior = false;
        foreach (var (alcance, (actual, anterior)) in _credenciales)
        {
            var coincideActual = CryptographicOperations.FixedTimeEquals(bytes, actual);
            var coincideAnterior = anterior is not null && CryptographicOperations.FixedTimeEquals(bytes, anterior);
            if (coincideActual || coincideAnterior)
            {
                encontrado = alcance;
                esAnterior = !coincideActual;
            }
        }

        return (encontrado, esAnterior);
    }

    public ResultadoDeCredencialDeEntrada Comprobar(AlcanceDeServicio alcance, string? presentada)
    {
        if (string.IsNullOrEmpty(presentada) || !_credenciales.TryGetValue(alcance, out var esperadas))
        {
            return ResultadoDeCredencialDeEntrada.Invalida;
        }

        var bytes = Encoding.UTF8.GetBytes(presentada);
        var esActual = CryptographicOperations.FixedTimeEquals(bytes, esperadas.Actual);
        var esAnterior = esperadas.Anterior is not null && CryptographicOperations.FixedTimeEquals(bytes, esperadas.Anterior);

        return esActual ? ResultadoDeCredencialDeEntrada.Actual : esAnterior ? ResultadoDeCredencialDeEntrada.Anterior : ResultadoDeCredencialDeEntrada.Invalida;
    }
}

/// <summary>Secretos comunes a los dos servicios, ya validados.</summary>
public sealed record ConfiguracionDeSeguridad(
    OpcionesDeHuella Huella,
    OpcionesDeCredencialesDeSalida CredencialesDeSalida,
    OpcionesDeCredencialesDeEntrada CredencialesDeEntrada,
    OpcionesDeFirmaDeIntegraciones Integraciones)
{
    /// <summary>
    /// Lee y valida los secretos: <c>BABELHUB__Seguridad__ClaveDeHuella</c> (y <c>…Anterior</c> en una rotación),
    /// <c>BABELHUB__Seguridad__Credenciales__&lt;Alcance&gt;</c> (y <c>…&lt;Alcance&gt;Anterior</c>) por cada alcance requerido y
    /// <c>BABELHUB__Integraciones__&lt;Proveedor&gt;__Secreto</c> (y <c>…SecretoAnterior</c>). Falla al arrancar, con todos los
    /// problemas, si falta uno obligatorio, es débil, se repite entre alcances o la anterior es igual a la actual.
    /// </summary>
    public static ConfiguracionDeSeguridad Cargar(
        IConfiguration configuracion,
        IReadOnlyCollection<AlcanceDeServicio> alcancesRequeridos,
        IReadOnlyCollection<string> proveedoresRequeridos)
    {
        var lector = new LectorDeSecretos(configuracion);
        var resultado = Leer(lector, alcancesRequeridos, proveedoresRequeridos);
        lector.ExigirValida();
        return resultado!;
    }

    /// <summary>Lee con un lector ya creado, para que otros secretos del servicio (por ejemplo, Turnstile) se comprueben con los mismos criterios y sin repetirse con estos.</summary>
    internal static ConfiguracionDeSeguridad? Leer(
        LectorDeSecretos lector,
        IReadOnlyCollection<AlcanceDeServicio> alcancesRequeridos,
        IReadOnlyCollection<string> proveedoresRequeridos)
    {
        var huella = LeerHuella(lector);
        var (salida, entrada) = LeerCredenciales(lector, alcancesRequeridos);
        var integraciones = LeerIntegraciones(lector, proveedoresRequeridos);

        return huella is null || salida is null || entrada is null || integraciones is null
            ? null
            : new ConfiguracionDeSeguridad(huella, salida, entrada, integraciones);
    }

    private static OpcionesDeHuella? LeerHuella(LectorDeSecretos lector)
    {
        const string ruta = "Seguridad:ClaveDeHuella";
        const string rutaAnterior = "Seguridad:ClaveDeHuellaAnterior";
        var actual = lector.Obligatorio(ruta);
        var anterior = lector.Opcional(rutaAnterior);
        if (actual is null)
        {
            return null;
        }

        try
        {
            var opciones = new OpcionesDeHuella(actual, anterior);
            lector.Comprobar(ruta, ParteDeLaClave(actual), esTexto: false);
            if (anterior is not null)
            {
                lector.Comprobar(rutaAnterior, ParteDeLaClave(anterior), esTexto: false);
            }

            return opciones;
        }
        catch (ArgumentException ex)
        {
            // El mensaje describe la regla incumplida, nunca el valor.
            var detalle = ex.Message;
            var corte = detalle.LastIndexOf(" (", StringComparison.Ordinal);
            lector.Problema(ex.ParamName == "claveAnterior" ? rutaAnterior : ruta, corte > 0 ? detalle[..corte] : detalle);
            return null;
        }
    }

    private static string ParteDeLaClave(string claveVersionada) => claveVersionada[(claveVersionada.IndexOf(':', StringComparison.Ordinal) + 1)..];

    private static (OpcionesDeCredencialesDeSalida? Salida, OpcionesDeCredencialesDeEntrada? Entrada) LeerCredenciales(
        LectorDeSecretos lector,
        IReadOnlyCollection<AlcanceDeServicio> requeridos)
    {
        var salida = new List<KeyValuePair<AlcanceDeServicio, ValorSecreto>>();
        var entrada = new List<KeyValuePair<AlcanceDeServicio, (string Actual, string? Anterior)>>();
        foreach (var alcance in requeridos.Distinct())
        {
            var ruta = $"Seguridad:Credenciales:{alcance}";
            var rutaAnterior = ruta + "Anterior";
            var actual = lector.Obligatorio(ruta);
            if (actual is null)
            {
                continue;
            }

            var anterior = lector.Opcional(rutaAnterior);
            lector.Comprobar(ruta, actual);
            if (anterior is not null)
            {
                if (string.Equals(anterior, actual, StringComparison.Ordinal))
                {
                    lector.Problema(rutaAnterior, "es igual a la actual");
                }
                else
                {
                    lector.Comprobar(rutaAnterior, anterior);
                }
            }

            salida.Add(new(alcance, new ValorSecreto(actual)));
            entrada.Add(new(alcance, (actual, anterior)));
        }

        if (lector.Problemas.Count > 0)
        {
            return (null, null);
        }

        return (new OpcionesDeCredencialesDeSalida(salida), new OpcionesDeCredencialesDeEntrada(entrada));
    }

    private static OpcionesDeFirmaDeIntegraciones? LeerIntegraciones(LectorDeSecretos lector, IReadOnlyCollection<string> proveedores)
    {
        var secretos = new List<KeyValuePair<string, IReadOnlyList<byte[]>>>();
        foreach (var proveedor in proveedores.Distinct(StringComparer.Ordinal))
        {
            var ruta = $"Integraciones:{proveedor}:Secreto";
            var rutaAnterior = $"Integraciones:{proveedor}:SecretoAnterior";
            var actual = lector.Obligatorio(ruta);
            if (actual is null)
            {
                continue;
            }

            var anterior = lector.Opcional(rutaAnterior);
            lector.Comprobar(ruta, actual);
            var lista = new List<byte[]> { Encoding.UTF8.GetBytes(actual) };
            if (anterior is not null)
            {
                if (string.Equals(anterior, actual, StringComparison.Ordinal))
                {
                    lector.Problema(rutaAnterior, "es igual a la actual");
                }
                else
                {
                    lector.Comprobar(rutaAnterior, anterior);
                    lista.Add(Encoding.UTF8.GetBytes(anterior));
                }
            }

            secretos.Add(new(proveedor, lista));
        }

        return lector.Problemas.Count > 0 ? null : new OpcionesDeFirmaDeIntegraciones(secretos);
    }
}
