namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>Se rechaza el arranque: el mensaje nombra la variable y el motivo (enumerado), nunca un valor secreto (PAG-12).</summary>
public sealed class ConfiguracionDePagosInvalidaException : InvalidOperationException
{
    public ConfiguracionDePagosInvalidaException(string mensaje)
        : base(mensaje)
    {
    }

    public ConfiguracionDePagosInvalidaException()
    {
    }

    public ConfiguracionDePagosInvalidaException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}

/// <summary>
/// Lo que decide el arranque del módulo <c>Pagos</c>: la pasarela elegida, el entorno, si el modo prohíbe llamadas reales (<c>Restauracion</c> y <c>Ensayo</c>) y si el adaptador que se va a registrar es el nulo, si hay un
/// módulo Pedidos, y los secretos. Los secretos no se imprimen: el texto del registro no los repite.
/// </summary>
public sealed record OpcionesDeArranqueDePagos(
    string? Pasarela,
    string Entorno,
    bool SinLlamadasReales,
    bool PasarelaEsNula,
    bool PedidosDisponible,
    string? LlavePrivada,
    string? ClaveDeIntentos,
    string? ClaveDeIntentosAnterior)
{
    public override string ToString() => $"OpcionesDeArranqueDePagos {{ Pasarela = {Pasarela}, Entorno = {Entorno} }}";
}

/// <summary>
/// La guarda de arranque de Pagos (PAG-12, AC-PAG-10; D05): la lista blanca por entorno decide qué pasarela y qué llave se aceptan. <c>simulador</c> y <c>sk_test_</c> solo en <c>Development</c> y <c>Test</c>;
/// <c>sk_live_</c> solo en <c>Production</c>; cualquier otro entorno (como <c>Staging</c>) rechaza ambos. <c>culqi</c> exige Pedidos, la llave privada y la clave de intentos; los secretos no pueden faltar, ser cortos ni
/// repetirse, y con un modo que prohíbe llamadas reales el adaptador es el nulo. Una variable ausente falla el arranque: no hay valor por defecto que cobre.
/// </summary>
public static class GuardaDeArranqueDePagos
{
    public const string VariablePasarela = "Pagos:Pasarela";
    public const string VariableLlavePrivada = "BABELHUB__Pagos__LlavePrivada";
    public const string VariableClaveDeIntentos = "BABELHUB__Pagos__ClaveDeIntentos";
    public const string VariableClaveDeIntentosAnterior = "BABELHUB__Pagos__ClaveDeIntentosAnterior";

    public const string Simulador = "simulador";
    public const string Culqi = "culqi";

    public const int LargoMinimoDeLaClaveDeIntentos = 32;
    public const int LargoMinimoDeLaLlavePrivada = 16;

    private const string PrefijoDePruebas = "sk_test_";
    private const string PrefijoDeProduccion = "sk_live_";

    public static void Exigir(OpcionesDeArranqueDePagos opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);

        if (opciones.Pasarela is not (Simulador or Culqi))
        {
            throw Rechazo(VariablePasarela, "ausente o desconocida: solo «simulador» o «culqi»");
        }

        var entorno = opciones.Entorno ?? string.Empty;
        var esDePruebas = entorno.Equals("Development", StringComparison.OrdinalIgnoreCase) || entorno.Equals("Test", StringComparison.OrdinalIgnoreCase);
        var esProduccion = entorno.Equals("Production", StringComparison.OrdinalIgnoreCase);

        Secreto(VariableClaveDeIntentos, opciones.ClaveDeIntentos, LargoMinimoDeLaClaveDeIntentos);
        if (opciones.ClaveDeIntentosAnterior is not null)
        {
            Secreto(VariableClaveDeIntentosAnterior, opciones.ClaveDeIntentosAnterior, LargoMinimoDeLaClaveDeIntentos);
        }

        if (opciones.Pasarela == Simulador)
        {
            if (!esDePruebas)
            {
                throw Rechazo(VariablePasarela, "«simulador» solo se acepta en Development y Test");
            }
        }
        else
        {
            if (!opciones.PedidosDisponible)
            {
                throw Rechazo(VariablePasarela, "«culqi» exige el módulo Pedidos");
            }

            Secreto(VariableLlavePrivada, opciones.LlavePrivada, LargoMinimoDeLaLlavePrivada);
            LlaveSegunElEntorno(opciones.LlavePrivada!, esDePruebas, esProduccion);
            if (string.Equals(opciones.LlavePrivada, opciones.ClaveDeIntentos, StringComparison.Ordinal)
                || (opciones.ClaveDeIntentosAnterior is not null && (string.Equals(opciones.LlavePrivada, opciones.ClaveDeIntentosAnterior, StringComparison.Ordinal))))
            {
                throw Rechazo(VariableLlavePrivada, "no puede repetir una clave de intentos");
            }
        }

        if (opciones.ClaveDeIntentosAnterior is not null && string.Equals(opciones.ClaveDeIntentos, opciones.ClaveDeIntentosAnterior, StringComparison.Ordinal))
        {
            throw Rechazo(VariableClaveDeIntentosAnterior, "no puede repetir la clave actual");
        }

        if (opciones.SinLlamadasReales && !opciones.PasarelaEsNula)
        {
            throw Rechazo(VariablePasarela, "en Restauracion y Ensayo solo se acepta el adaptador nulo de la pasarela");
        }
    }

    private static void LlaveSegunElEntorno(string llave, bool esDePruebas, bool esProduccion)
    {
        if (llave.StartsWith(PrefijoDePruebas, StringComparison.Ordinal))
        {
            if (!esDePruebas)
            {
                throw Rechazo(VariableLlavePrivada, "una llave de pruebas solo se acepta en Development y Test");
            }

            return;
        }

        if (llave.StartsWith(PrefijoDeProduccion, StringComparison.Ordinal))
        {
            if (!esProduccion)
            {
                throw Rechazo(VariableLlavePrivada, "una llave de producción solo se acepta en Production");
            }

            return;
        }

        throw Rechazo(VariableLlavePrivada, "no tiene el prefijo de una llave de Culqi");
    }

    private static void Secreto(string variable, string? valor, int largoMinimo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw Rechazo(variable, "ausente");
        }

        if (valor.Length < largoMinimo)
        {
            throw Rechazo(variable, $"demasiado corta (mínimo {largoMinimo} caracteres)");
        }
    }

    private static ConfiguracionDePagosInvalidaException Rechazo(string variable, string motivo) =>
        new($"Configuración de Pagos inválida: {variable} {motivo}.");
}
