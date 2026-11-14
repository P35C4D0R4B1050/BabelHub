namespace BabelHub.ArchitectureTests;

/// <summary>
/// Las reglas de dependencia entre proyectos de AGENTS.md §3 (1, 2, 5 y 6) y la separación de <c>tools/RestauracionCheck</c> (PLA-13),
/// como funciones sobre una lista de proyectos: devuelven un mensaje por cada violación, que señala la dependencia. Así se aplican
/// al repositorio real y también, con grafos construidos a propósito, a las pruebas de sensibilidad (AC-PLA-13).
/// </summary>
internal static class ReglasDeReferencias
{
    public const string Shared = "BabelHub.Shared";
    public const string RestauracionCheck = "RestauracionCheck";

    public static readonly string[] Servicios = ["BabelHub.Inventario", "BabelHub.Ecommerce"];

    private static readonly string[] Capas = ["Domain", "Application", "Infrastructure", "Api"];

    /// <summary>Reglas 1 y 2 y la dirección de las capas: una capa interna no depende de una externa (ni el Dominio ni la Aplicación de paquetes).</summary>
    public static List<string> CapasInternas(IReadOnlyList<Proyecto> proyectos)
    {
        var violaciones = new List<string>();
        foreach (var servicio in Servicios)
        {
            var permitidas = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Domain"] = [],
                ["Application"] = [$"{servicio}.Domain"],
                ["Infrastructure"] = [$"{servicio}.Application", $"{servicio}.Domain", Shared],
                ["Api"] = [$"{servicio}.Application", $"{servicio}.Domain", $"{servicio}.Infrastructure", Shared],
            };

            foreach (var capa in Capas)
            {
                var proyecto = proyectos.FirstOrDefault(p => p.Nombre == $"{servicio}.{capa}");
                if (proyecto is null)
                {
                    continue;
                }

                foreach (var referencia in proyecto.ProyectosReferenciados.Where(r => !permitidas[capa].Contains(r)))
                {
                    violaciones.Add($"{proyecto.Nombre} referencia {referencia}: una capa interna no puede depender de una externa ni de otro servicio.");
                }

                if (capa is "Domain" or "Application")
                {
                    foreach (var paquete in proyecto.Paquetes)
                    {
                        violaciones.Add($"{proyecto.Nombre} referencia el paquete {paquete}: el Dominio y la Aplicación no usan paquetes.");
                    }
                }
            }
        }

        return violaciones;
    }

    /// <summary>Regla 5: un servicio nunca referencia las capas del otro.</summary>
    public static List<string> ServiciosAjenos(IReadOnlyList<Proyecto> proyectos)
    {
        var violaciones = new List<string>();
        foreach (var proyecto in proyectos)
        {
            var propio = ServicioDe(proyecto.Nombre);
            foreach (var referencia in proyecto.ProyectosReferenciados.Where(r => r != Shared && ServicioDe(r) != propio))
            {
                violaciones.Add($"{proyecto.Nombre} referencia {referencia}, de otro servicio.");
            }
        }

        return violaciones;
    }

    /// <summary>Regla 6: <c>BabelHub.Shared</c> solo tiene contratos; no referencia nada ni lo referencian Dominio ni Aplicación.</summary>
    public static List<string> ContratosCompartidos(IReadOnlyList<Proyecto> proyectos)
    {
        var violaciones = new List<string>();
        foreach (var proyecto in proyectos)
        {
            if (proyecto.Nombre == Shared)
            {
                violaciones.AddRange(proyecto.ProyectosReferenciados.Select(r => $"{Shared} referencia {r}: el proyecto de contratos no referencia nada."));
            }
            else if ((proyecto.Nombre.EndsWith(".Domain", StringComparison.Ordinal) || proyecto.Nombre.EndsWith(".Application", StringComparison.Ordinal))
                     && proyecto.ProyectosReferenciados.Contains(Shared))
            {
                violaciones.Add($"{proyecto.Nombre} referencia {Shared}: el Dominio y la Aplicación no usan el proyecto de contratos.");
            }
        }

        return violaciones;
    }

    /// <summary>
    /// <c>tools/RestauracionCheck</c> está fuera de los servicios: ningún servicio lo referencia y él no referencia proyectos de los
    /// servicios (ni Dominio, Aplicación, Infraestructura ni Api). Si la herramienta aún no existe, no hay nada que comprobar.
    /// </summary>
    public static List<string> HerramientaDeRestauracion(IReadOnlyList<Proyecto> deSrc, IReadOnlyList<Proyecto> deTools)
    {
        var violaciones = new List<string>();
        foreach (var proyecto in deSrc.Where(p => p.ProyectosReferenciados.Contains(RestauracionCheck)))
        {
            violaciones.Add($"{proyecto.Nombre} referencia {RestauracionCheck}: ningún servicio puede depender de la herramienta de restauración.");
        }

        foreach (var herramienta in deTools.Where(p => p.Nombre == RestauracionCheck))
        {
            foreach (var referencia in herramienta.ProyectosReferenciados.Where(r => Servicios.Any(s => r.StartsWith(s + ".", StringComparison.Ordinal))))
            {
                violaciones.Add($"{RestauracionCheck} referencia {referencia}: la herramienta de restauración no referencia proyectos de los servicios.");
            }
        }

        return violaciones;
    }

    /// <summary>BabelHub.Inventario.Domain → BabelHub.Inventario.</summary>
    public static string ServicioDe(string proyecto)
    {
        var partes = proyecto.Split('.');
        return partes.Length >= 2 ? $"{partes[0]}.{partes[1]}" : proyecto;
    }
}
