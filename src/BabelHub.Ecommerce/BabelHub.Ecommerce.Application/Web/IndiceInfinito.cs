namespace BabelHub.Ecommerce.Application.Web;

/// <summary>Una página del índice infinito: su dirección canónica y su texto generado (nunca se guarda: se recalcula en cada solicitud).</summary>
public sealed record PaginaDelIndice(string Direccion, string Texto);

/// <summary>Lo que la Presentación debe hacer con una dirección o con el campo del formulario (IND-01, IND-03, IND-07).</summary>
public abstract record ResultadoDelIndice
{
    private ResultadoDelIndice()
    {
    }

    /// <summary>La dirección es válida: se muestra su página.</summary>
    public sealed record Mostrar(PaginaDelIndice Pagina) : ResultadoDelIndice;

    /// <summary>Redirigir (302) a esta dirección canónica, ya validada: la del campo del formulario o, si venía vacía, una al azar.</summary>
    public sealed record Redirigir(string Direccion) : ResultadoDelIndice;

    /// <summary>
    /// La dirección no es válida (IND-07): se responde 200 con un mensaje amable y <b>una página al azar ya generada</b>. El texto recibido <b>no está</b> en el resultado: nunca se repite en la respuesta, los enlaces, el campo ni los registros.
    /// </summary>
    public sealed record DireccionNoValida(PaginaDelIndice PaginaAlAzar) : ResultadoDelIndice;
}

/// <summary>
/// Contrato público del índice infinito (web-indice-infinito; IND-01 a IND-07): una función pura de una dirección a una página de texto generado, sin base de datos, caché ni estado (IND-06). Lo consume solo la Presentación; no usa ningún otro
/// módulo de negocio. Los modelos son propios: no expone tipos del Dominio.
/// </summary>
public interface IIndiceInfinito
{
    /// <summary>
    /// Abre la dirección de la ruta: <see cref="ResultadoDelIndice.Mostrar"/> si es válida; <see cref="ResultadoDelIndice.Redirigir"/> a una al azar si viene vacía (spec §6); <see cref="ResultadoDelIndice.DireccionNoValida"/> en cualquier otro caso.
    /// </summary>
    ResultadoDelIndice Abrir(string? direccion);

    /// <summary>
    /// Procesa el campo «Ir a una dirección»: recorta solo el espacio U+0020 de los extremos (un <c>%0a</c> no se recorta); <see cref="ResultadoDelIndice.Redirigir"/> a la canónica si es válida o a una al azar si el campo viene vacío o de solo espacios;
    /// <see cref="ResultadoDelIndice.DireccionNoValida"/> en cualquier otro caso.
    /// </summary>
    ResultadoDelIndice Entrar(string? textoDelFormulario);

    /// <summary>Una dirección al azar nueva y <b>distinta de la actual</b>; si <paramref name="desde"/> no es válida se ignora.</summary>
    string ElegirAlAzar(string? desde = null);
}
