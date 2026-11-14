using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>La versión vigente de un texto legal y la fecha desde la que rige, como la informa Identidad (IDE-57).</summary>
public sealed record VersionVigenteDeTexto(string Version, DateOnly VigenteDesde);

/// <summary>Puerto: Identidad informa la versión vigente de cada texto. Lo implementa el adaptador de Identidad (carril A); hasta entonces, uno provisional.</summary>
public interface IVersionesDeTextosLegales
{
    Task<VersionVigenteDeTexto?> ObtenerVigente(TipoDeTextoLegal tipo, CancellationToken cancelacion = default);
}

/// <summary>El cuerpo de un texto legal tal como lo guardan los recursos de la web.</summary>
public sealed record CuerpoDeTextoLegal(string Titulo, string Resumen, IReadOnlyList<SeccionDeTextoLegal> Secciones, bool Provisional);

/// <summary>Puerto: el cuerpo de los textos legales (recursos versionados de la web). La web no guarda la versión ni la vigencia, solo el cuerpo y su presentación.</summary>
public interface IContenidoDeTextosLegales
{
    Task<CuerpoDeTextoLegal?> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default);
}

/// <summary>Arma el texto legal vigente: la versión que informa Identidad y el cuerpo de los recursos (WEB-29, AC-WEB-24).</summary>
public sealed class ConsultarTextoLegal(IVersionesDeTextosLegales versiones, IContenidoDeTextosLegales contenido) : ITextosLegales
{
    public async Task<Resultado<TextoLegal>> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default)
    {
        if (!Enum.IsDefined(tipo))
        {
            return Resultado<TextoLegal>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "texto-legal-no-encontrado"));
        }

        VersionVigenteDeTexto? version;
        try
        {
            version = await versiones.ObtenerVigente(tipo, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            version = null;
        }

        // Sin la versión de Identidad no se muestra un texto como vigente: se dice que no está disponible.
        if (version is null)
        {
            return Resultado<TextoLegal>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "version-de-textos-no-disponible"));
        }

        var cuerpo = await contenido.Obtener(tipo, cancelacion).ConfigureAwait(false);
        if (cuerpo is null)
        {
            return Resultado<TextoLegal>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "texto-legal-no-encontrado"));
        }

        return Resultado<TextoLegal>.Exito(new TextoLegal(tipo, cuerpo.Titulo, version.Version, version.VigenteDesde, cuerpo.Provisional, cuerpo.Resumen, cuerpo.Secciones));
    }
}
