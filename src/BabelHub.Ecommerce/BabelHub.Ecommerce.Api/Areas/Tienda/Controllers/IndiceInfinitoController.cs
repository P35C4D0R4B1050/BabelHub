using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El índice infinito (web-indice-infinito T08; IND-01, IND-03, IND-04, IND-07, IND-09; D03, D04). <b>Presentación pura</b>: llama a <see cref="IIndiceInfinito"/> y traduce el resultado, sin reglas propias. La dirección viaja en la ruta
/// (<c>GET /indice-infinito/{**direccion}</c>, con captura total: una dirección con <c>/</c> es simplemente no válida y no un 404), de modo que copiar el enlace ya incluye la dirección. <c>GET /indice-infinito</c> y <c>?direccion=</c> (el formulario)
/// redirigen con 302 a la canónica o a una al azar si viene vacía; <c>GET /indice-infinito/acciones/al-azar?desde=</c> redirige a una distinta (su ruta tiene dos segmentos: nunca puede ser una dirección válida). Una dirección no válida
/// responde <b>200</b> con el mensaje amable y una página al azar, <b>sin repetir nunca el texto recibido</b>. Las redirecciones solo arman rutas propias desde direcciones ya validadas (sin redirección abierta ni inyección en <c>Location</c>).
/// Pasan por el pipeline de límites de <c>transversal-seguridad</c> sin exenciones (IND-09) y no guardan la ruta como destino de retorno (<see cref="SinRetornoAttribute"/>).
/// </summary>
[Area("Tienda")]
[SinRetorno]
public sealed partial class IndiceInfinitoController(IIndiceInfinito indice, ContextoDePagina pagina) : Controller
{
    private const string Titulo = "El índice infinito";

    /// <summary>Un valor que el contrato siempre trata como no válido: se usa cuando el formulario llega con la dirección repetida (<c>?direccion=a&amp;direccion=b</c>) para dar la misma respuesta amable sin repetir nada.</summary>
    private const string EntradaNoValida = "!";

    /// <summary>La forma exacta de lo que se pone en <c>Location</c>: solo una ruta propia con una dirección ya validada.</summary>
    [GeneratedRegex("^/indice-infinito/[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex FormaDeLaRutaCanonica();

    /// <summary>La página de una dirección, la entrada del índice (<c>/indice-infinito</c>, con o sin barra final) y el formulario «Ir a una dirección» (<c>?direccion=</c>): una sola acción para evitar rutas ambiguas.</summary>
    [HttpGet("/indice-infinito")]
    [HttpGet("/indice-infinito/{**direccion}")]
    public async Task<IActionResult> Abrir(CancellationToken cancelacion)
    {
        var enRuta = RouteData.Values.TryGetValue("direccion", out var valor) ? valor as string : null;
        if (!string.IsNullOrEmpty(enRuta))
        {
            return await Traducir(indice.Abrir(enRuta), cancelacion);
        }

        // La entrada, la ruta con barra final y el formulario: un valor repetido se trata como no válido (el enlace lo uniría como «a,b»).
        var campo = Request.Query["direccion"];
        var texto = campo.Count switch
        {
            0 => null,
            1 => campo[0],
            _ => EntradaNoValida,
        };
        return await Traducir(indice.Entrar(texto), cancelacion);
    }

    /// <summary>«Otra página al azar»: redirige a una dirección nueva y distinta de la actual; un <c>desde</c> repetido o no válido se ignora (sin eco).</summary>
    [HttpGet("/indice-infinito/acciones/al-azar")]
    public IActionResult OtraAlAzar()
    {
        var desde = Request.Query["desde"];
        var nueva = indice.ElegirAlAzar(desde.Count == 1 ? desde[0] : null);
        return Redirigir(nueva);
    }

    private async Task<IActionResult> Traducir(ResultadoDelIndice resultado, CancellationToken cancelacion)
    {
        switch (resultado)
        {
            case ResultadoDelIndice.Mostrar mostrar:
                return await MostrarPagina(mostrar.Pagina, direccionNoValida: false, cancelacion);
            case ResultadoDelIndice.Redirigir redirigir:
                return Redirigir(redirigir.Direccion);
            case ResultadoDelIndice.DireccionNoValida noValida:
                return await MostrarPagina(noValida.PaginaAlAzar, direccionNoValida: true, cancelacion);
            default:
                throw new InvalidOperationException("Resultado del índice desconocido.");
        }
    }

    private async Task<IActionResult> MostrarPagina(PaginaDelIndice contenido, bool direccionNoValida, CancellationToken cancelacion)
    {
        var vista = new IndiceInfinitoVista { Direccion = contenido.Direccion, Texto = contenido.Texto, DireccionNoValida = direccionNoValida };
        return View("Pagina", await pagina.Completar(vista, Titulo, cancelacion: cancelacion));
    }

    private IActionResult Redirigir(string direccion)
    {
        var ruta = $"{IndiceInfinitoVista.RutaBase}/{direccion}";
        // Defensa en profundidad: la Aplicación ya validó la dirección; si no cumpliera la forma, no se redirige a ninguna parte.
        return FormaDeLaRutaCanonica().IsMatch(ruta) ? LocalRedirect(ruta) : throw new InvalidOperationException("La dirección del índice no tiene la forma esperada.");
    }
}
