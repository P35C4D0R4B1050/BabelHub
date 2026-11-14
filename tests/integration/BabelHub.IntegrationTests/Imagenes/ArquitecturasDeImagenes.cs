using System.Text.Json;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>Una excepción registrada: una imagen de terceros sin versión para una arquitectura en la que corre (PLA-03).</summary>
internal sealed record ExcepcionDeArquitectura(string Imagen, string Arquitectura, string Vm, string Motivo);

/// <summary>
/// Las arquitecturas de una imagen y la regla de PLA-03: cada imagen de terceros existe para ARM64 y x86_64, o consta como excepción
/// registrada. Lee el índice del manifiesto de la imagen (<c>docker buildx imagetools inspect --raw</c>) y devuelve un mensaje por
/// cada incumplimiento.
/// </summary>
internal static class ArquitecturasDeImagenes
{
    public static readonly string[] Requeridas = ["linux/amd64", "linux/arm64"];

    /// <summary>Las plataformas <c>sistema/arquitectura</c> del índice; las entradas de atestación (<c>unknown/unknown</c>) no cuentan.</summary>
    public static IReadOnlySet<string> DeIndice(string indiceJson)
    {
        using var documento = JsonDocument.Parse(indiceJson);
        var plataformas = new HashSet<string>(StringComparer.Ordinal);
        if (!documento.RootElement.TryGetProperty("manifests", out var manifiestos))
        {
            return plataformas;
        }

        foreach (var manifiesto in manifiestos.EnumerateArray())
        {
            if (!manifiesto.TryGetProperty("platform", out var plataforma))
            {
                continue;
            }

            var sistema = plataforma.GetProperty("os").GetString();
            var arquitectura = plataforma.GetProperty("architecture").GetString();
            if (sistema is not (null or "unknown") && arquitectura is not (null or "unknown"))
            {
                plataformas.Add($"{sistema}/{arquitectura}");
            }
        }

        return plataformas;
    }

    public static IReadOnlyList<ExcepcionDeArquitectura> LeerExcepciones(string json)
    {
        using var documento = JsonDocument.Parse(json);
        return [.. documento.RootElement.GetProperty("excepciones").EnumerateArray().Select(e => new ExcepcionDeArquitectura(
            e.GetProperty("imagen").GetString()!,
            e.GetProperty("arquitectura").GetString()!,
            e.GetProperty("vm").GetString()!,
            e.GetProperty("motivo").GetString()!))];
    }

    /// <summary>Lo que incumple una imagen: le falta una arquitectura sin excepción, o tiene una excepción que ya no hace falta.</summary>
    public static List<string> Incumplimientos(string imagen, IReadOnlySet<string> plataformas, IReadOnlyList<ExcepcionDeArquitectura> excepciones)
    {
        var incumplimientos = new List<string>();
        foreach (var arquitectura in Requeridas)
        {
            var excepcion = excepciones.FirstOrDefault(e => e.Imagen == imagen && e.Arquitectura == arquitectura);
            var existe = plataformas.Contains(arquitectura);
            if (!existe && excepcion is null)
            {
                incumplimientos.Add($"{imagen}: no existe para {arquitectura} y no consta como excepción registrada.");
            }
            else if (existe && excepcion is not null)
            {
                incumplimientos.Add($"{imagen}: la excepción para {arquitectura} ya no hace falta (la imagen existe); retirarla.");
            }
        }

        foreach (var excepcion in excepciones.Where(e => e.Imagen == imagen && string.IsNullOrWhiteSpace(e.Motivo)))
        {
            incumplimientos.Add($"{imagen}: la excepción para {excepcion.Arquitectura} no dice su motivo.");
        }

        return incumplimientos;
    }
}
