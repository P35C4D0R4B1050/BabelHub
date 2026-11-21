using System.Reflection;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// Lee el cuerpo de los textos legales de los recursos incrustados en el ensamblado (<c>Web/Textos/*.txt</c>): la web guarda solo el
/// cuerpo y su presentación; la versión y la vigencia las informa Identidad (WEB-29). El formato es texto plano: una primera línea
/// <c>PROVISIONAL</c> (si el texto es de trabajo), <c>TITULO:</c>, <c>RESUMEN:</c>, y secciones que empiezan con <c>## </c>; cada línea no vacía
/// de una sección es un párrafo (las que empiezan con «- » son elementos de una lista).
/// </summary>
public sealed class ContenidoDeTextosEmbebido : IContenidoDeTextosLegales
{
    private static readonly Assembly Ensamblado = typeof(ContenidoDeTextosEmbebido).Assembly;

    public Task<CuerpoDeTextoLegal?> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default)
    {
        var nombre = $"{TiposDeTextoLegal.Clave(tipo)}.txt";
        var recurso = Ensamblado.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith($".Textos.{nombre}", StringComparison.Ordinal));
        if (recurso is null)
        {
            return Task.FromResult<CuerpoDeTextoLegal?>(null);
        }

        using var flujo = Ensamblado.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(flujo);
        return Task.FromResult(Analizar(lector.ReadToEnd()));
    }

    /// <summary>Analiza el texto plano de un recurso; <see langword="null"/> si no tiene título.</summary>
    public static CuerpoDeTextoLegal? Analizar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var provisional = false;
        string? titulo = null;
        var resumen = string.Empty;
        var secciones = new List<SeccionDeTextoLegal>();
        string? tituloDeSeccion = null;
        var parrafos = new List<string>();

        void CerrarSeccion()
        {
            if (tituloDeSeccion is not null)
            {
                secciones.Add(new SeccionDeTextoLegal(tituloDeSeccion, [.. parrafos]));
            }

            parrafos.Clear();
        }

        foreach (var cruda in texto.Split('\n'))
        {
            var linea = cruda.TrimEnd('\r').Trim();
            if (linea.Length == 0)
            {
                continue;
            }

            if (linea == "PROVISIONAL")
            {
                provisional = true;
            }
            else if (linea.StartsWith("TITULO:", StringComparison.Ordinal))
            {
                titulo = linea["TITULO:".Length..].Trim();
            }
            else if (linea.StartsWith("RESUMEN:", StringComparison.Ordinal))
            {
                resumen = linea["RESUMEN:".Length..].Trim();
            }
            else if (linea.StartsWith("## ", StringComparison.Ordinal))
            {
                CerrarSeccion();
                tituloDeSeccion = linea[3..].Trim();
            }
            else if (tituloDeSeccion is not null)
            {
                parrafos.Add(linea);
            }
        }

        CerrarSeccion();
        return titulo is null ? null : new CuerpoDeTextoLegal(titulo, resumen, secciones, provisional);
    }
}

/// <summary>
/// Versión provisional de los textos legales, mientras el contrato de Identidad que informa la versión vigente (IDE-57, hallazgo R12 de
/// <c>web-base</c> para el carril A) no exista. Es un texto de trabajo y así se muestra; nunca debe usarse en producción.
/// </summary>
public sealed class VersionesDeTextosProvisionales : IVersionesDeTextosLegales
{
    public static readonly VersionVigenteDeTexto Version = new("0.1-provisional", new DateOnly(2026, 11, 14));

    public Task<VersionVigenteDeTexto?> ObtenerVigente(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) =>
        Task.FromResult<VersionVigenteDeTexto?>(Version);
}

/// <summary>
/// La versión vigente de cada texto tal como la guarda Identidad (IDE-57): la web muestra y envía <b>la misma</b> versión que Identidad compara al registrar la aceptación. Si Identidad no puede leerla, la excepción sube
/// y <c>ConsultarTextoLegal</c> responde «no disponible»; nunca se inventa una versión.
/// </summary>
public sealed class VersionesDeTextosDeIdentidad(IVersionesVigentesDeLosTextos identidad) : IVersionesDeTextosLegales
{
    public async Task<VersionVigenteDeTexto?> ObtenerVigente(TipoDeTextoLegal tipo, CancellationToken cancelacion = default)
    {
        var vigentes = await identidad.Consultar(cancelacion).ConfigureAwait(false);
        var version = tipo switch
        {
            TipoDeTextoLegal.Terminos => vigentes.Terminos,
            TipoDeTextoLegal.Privacidad => vigentes.Privacidad,
            TipoDeTextoLegal.Reembolso => vigentes.Reembolso,
            _ => null,
        };
        return version is null ? null : new VersionVigenteDeTexto(version, vigentes.VigenteDesde);
    }
}

/// <summary>Sin el contrato de Identidad que informa la versión vigente, en producción los textos legales no están disponibles: no se inventa una versión.</summary>
public sealed class VersionesDeTextosNoDisponibles : IVersionesDeTextosLegales
{
    public Task<VersionVigenteDeTexto?> ObtenerVigente(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) =>
        Task.FromResult<VersionVigenteDeTexto?>(null);
}
