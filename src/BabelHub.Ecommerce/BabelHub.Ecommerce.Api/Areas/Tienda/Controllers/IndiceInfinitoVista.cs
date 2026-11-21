using BabelHub.Ecommerce.Api.Web;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El modelo de la pantalla del índice infinito (web-indice-infinito T07): la dirección canónica y el texto de la <b>página mostrada</b> (la pedida o, si la dirección no era válida, la página al azar que se ofrece en su lugar) y si hay que dar el mensaje amable.
/// <b>Nunca lleva el texto recibido</b>: ni la dirección no válida ni lo escrito en el formulario (IND-07, CTL-35).
/// </summary>
public sealed class IndiceInfinitoVista : ModeloBaseDePagina
{
    public const string RutaBase = "/indice-infinito";

    public string Direccion { get; set; } = string.Empty;

    public string Texto { get; set; } = string.Empty;

    /// <summary>Verdadero cuando la dirección pedida no era válida: la pantalla muestra el mensaje amable junto a la página al azar.</summary>
    public bool DireccionNoValida { get; set; }

    /// <summary>La ruta canónica de la página mostrada (la que se comparte): solo se arma con una dirección ya validada.</summary>
    public string RutaCanonica => $"{RutaBase}/{Direccion}";

    /// <summary>El enlace de «Otra página al azar»: una redirección que elige una dirección distinta de la actual.</summary>
    public string RutaDeOtraAlAzar => $"{RutaBase}/acciones/al-azar?desde={Direccion}";
}
