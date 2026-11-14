using System.Text.Json;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// Las reglas de PLA-27 sobre un archivo de Compose ya resuelto (<c>docker compose config --format json</c>): cada contenedor corre sin
/// usuario administrador, sin modo privilegiado, sin acceso al control del motor, sin capacidades añadidas, con límite de memoria y de
/// procesador, con comprobación de salud y con sistema de archivos de solo lectura, salvo una excepción registrada en
/// <c>x-excepciones</c> con su motivo y su mitigación. Devuelve un mensaje por incumplimiento, con el servicio y la regla.
/// </summary>
internal static partial class ReglasDeContenedores
{
    public static readonly string[] Reglas =
    [
        "usuario-no-administrador",
        "sin-modo-privilegiado",
        "sin-acceso-al-motor",
        "sin-espacios-del-anfitrion",
        "sin-capacidades-anadidas",
        "capacidades-descartadas",
        "limite-de-memoria",
        "limite-de-procesador",
        "comprobacion-de-salud",
        "sistema-de-archivos-de-solo-lectura",
        "puertos-en-direccion-privada",
        "sin-secretos-en-el-entorno",
    ];

    public static List<string> Verificar(string composeJson)
    {
        using var documento = JsonDocument.Parse(composeJson);
        var raiz = documento.RootElement;
        var incumplimientos = new List<string>();
        var excepciones = Excepciones(raiz, incumplimientos);

        if (!raiz.TryGetProperty("services", out var servicios))
        {
            incumplimientos.Add("El archivo no define servicios.");
            return incumplimientos;
        }

        var nombres = servicios.EnumerateObject().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var excepcion in excepciones.Where(e => !nombres.Contains(e.Servicio)))
        {
            incumplimientos.Add($"x-excepciones: el servicio «{excepcion.Servicio}» no existe.");
        }

        foreach (var servicio in servicios.EnumerateObject())
        {
            foreach (var regla in Reglas)
            {
                var motivo = Incumple(servicio.Value, regla);
                var excepcion = excepciones.FirstOrDefault(e => e.Servicio == servicio.Name && e.Regla == regla);
                if (motivo is not null && excepcion is null)
                {
                    incumplimientos.Add($"{servicio.Name}: incumple «{regla}» ({motivo}) y no consta como excepción registrada.");
                }
                else if (motivo is null && excepcion is not null)
                {
                    incumplimientos.Add($"{servicio.Name}: la excepción de «{regla}» ya no hace falta; retirarla.");
                }
            }
        }

        return incumplimientos;
    }

    private sealed record Excepcion(string Servicio, string Regla);

    private static List<Excepcion> Excepciones(JsonElement raiz, List<string> incumplimientos)
    {
        var lista = new List<Excepcion>();
        if (!raiz.TryGetProperty("x-excepciones", out var excepciones) || excepciones.ValueKind != JsonValueKind.Array)
        {
            return lista;
        }

        foreach (var excepcion in excepciones.EnumerateArray())
        {
            var servicio = Texto(excepcion, "servicio");
            var regla = Texto(excepcion, "regla");
            if (!Reglas.Contains(regla))
            {
                incumplimientos.Add($"x-excepciones: la regla «{regla}» de «{servicio}» no existe.");
            }

            if (string.IsNullOrWhiteSpace(Texto(excepcion, "motivo")) || string.IsNullOrWhiteSpace(Texto(excepcion, "mitigacion")))
            {
                incumplimientos.Add($"x-excepciones: la de «{servicio}» para «{regla}» debe tener su motivo y su mitigación.");
            }

            lista.Add(new Excepcion(servicio, regla));
        }

        return lista;
    }

    private static string Texto(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? string.Empty : string.Empty;

    private static bool Verdadero(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.True;

    private static IEnumerable<string> Lista(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString())
            : [];

    private static bool TieneLimite(JsonElement servicio, string directo, string dentroDeDeploy)
    {
        if (servicio.TryGetProperty(directo, out var valor) && valor.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && !string.IsNullOrWhiteSpace(valor.ToString()) && valor.ToString() != "0")
        {
            return true;
        }

        return servicio.TryGetProperty("deploy", out var deploy)
            && deploy.TryGetProperty("resources", out var recursos)
            && recursos.TryGetProperty("limits", out var limites)
            && limites.TryGetProperty(dentroDeDeploy, out var limite)
            && !string.IsNullOrWhiteSpace(limite.ToString()) && limite.ToString() != "0";
    }

    /// <summary>El motivo por el que el servicio incumple la regla, o <c>null</c> si la cumple.</summary>
    private static string? Incumple(JsonElement servicio, string regla)
    {
        switch (regla)
        {
            case "usuario-no-administrador":
            {
                var usuario = Texto(servicio, "user");
                return usuario.Length == 0
                    ? "no declara el usuario"
                    : EsAdministrador(usuario) ? $"corre como «{usuario}»" : null;
            }

            case "sin-modo-privilegiado":
                return Verdadero(servicio, "privileged") ? "privileged: true" : null;

            case "sin-acceso-al-motor":
            {
                var socket = servicio.TryGetProperty("volumes", out var volumenes) && volumenes.ValueKind == JsonValueKind.Array
                    && volumenes.EnumerateArray().Any(v => Texto(v, "source").Contains("docker.sock", StringComparison.Ordinal) || Texto(v, "target").Contains("docker.sock", StringComparison.Ordinal));
                return socket ? "monta el socket del motor de contenedores" : null;
            }

            case "sin-espacios-del-anfitrion":
                return Texto(servicio, "network_mode") == "host" ? "network_mode: host"
                    : Texto(servicio, "pid") == "host" ? "pid: host"
                    : Texto(servicio, "ipc") == "host" ? "ipc: host"
                    : null;

            case "sin-capacidades-anadidas":
                return Lista(servicio, "cap_add").Any() ? $"cap_add: {string.Join(", ", Lista(servicio, "cap_add"))}" : null;

            case "capacidades-descartadas":
                return !Lista(servicio, "cap_drop").Contains("ALL", StringComparer.OrdinalIgnoreCase) ? "no descarta todas las capacidades (cap_drop: [ALL])"
                    : !Lista(servicio, "security_opt").Contains("no-new-privileges:true") ? "puede ganar privilegios (falta no-new-privileges:true)"
                    : null;

            case "limite-de-memoria":
                return TieneLimite(servicio, "mem_limit", "memory") ? null : "sin límite de memoria";

            case "limite-de-procesador":
                return TieneLimite(servicio, "cpus", "cpus") ? null : "sin límite de procesador";

            case "comprobacion-de-salud":
            {
                if (!servicio.TryGetProperty("healthcheck", out var salud) || salud.ValueKind != JsonValueKind.Object)
                {
                    return "sin comprobación de salud";
                }

                return Verdadero(salud, "disable") || Lista(salud, "test").FirstOrDefault() == "NONE" ? "la comprobación de salud está desactivada" : null;
            }

            case "sistema-de-archivos-de-solo-lectura":
                return Verdadero(servicio, "read_only") ? null : "el sistema de archivos es escribible";

            case "puertos-en-direccion-privada":
            {
                if (!servicio.TryGetProperty("ports", out var puertos) || puertos.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var abiertos = puertos.EnumerateArray()
                    .Where(p => Texto(p, "host_ip") is "" or "0.0.0.0" or "::")
                    .Select(p => p.TryGetProperty("published", out var publicado) ? publicado.ToString() : "?")
                    .ToList();
                return abiertos.Count > 0 ? $"publica en todas las direcciones: {string.Join(", ", abiertos)}" : null;
            }

            case "sin-secretos-en-el-entorno":
            {
                if (!servicio.TryGetProperty("environment", out var entorno) || entorno.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var nombres = entorno.EnumerateObject().Where(p => NombreDeSecreto().IsMatch(p.Name) && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Length > 0).Select(p => p.Name).ToList();
                return nombres.Count > 0 ? $"lleva secretos en el archivo ({string.Join(", ", nombres)}): van en un archivo de secretos fuera del repositorio" : null;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(regla));
        }
    }

    private static bool EsAdministrador(string usuario)
    {
        var nombre = usuario.Split(':')[0];
        return nombre is "root" or "0" or "";
    }

    [GeneratedRegex(@"(?i)(password|passwd|secret|secreto|clave|token|credencial|contrasena)", RegexOptions.None, 1000)]
    private static partial Regex NombreDeSecreto();
}
